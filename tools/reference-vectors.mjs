#!/usr/bin/env node
// Execute the pinned browser implementation as an independent oracle for the C# port.
// No C# implementation, copied noise formula, backend access or player data is used here.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const targetRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const positional = process.argv.slice(2).filter(arg => arg !== '--check');
const sourceRoot = path.resolve(positional[0] || path.join(targetRoot, '..', 'citylife-source'));
const outputPath = path.resolve(positional[1] || path.join(targetRoot, 'Assets/CityLife/Resources/SourceVectors.json'));
const pinnedCommit = 'b71307023aea600da27aa3eada6cdcce7ba758ed';
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const git = (...args) => execFileSync('git', ['-C', sourceRoot, ...args], { encoding: 'utf8' }).trim();
assert.equal(git('rev-parse', 'HEAD'), pinnedCommit, 'Oracle must run against the recorded source commit');
const sourcePaths = [
  'src/engine/rng.ts', 'src/colony/noise.ts', 'src/colony/config.ts',
  'src/colony/scale.ts', 'src/colony/terrain.ts', 'src/colony/render/foliageLogic.ts',
  'src/colony/render/quiverTreeLogic.ts', 'src/colony/render/darkCity.ts',
  'src/colony/spatial/worldLayoutDocument.ts', 'src/colony/worldLayoutBoot.ts',
  'src/colony/worldLayoutStore.ts', 'src/colony/stores/useWorldAssets.ts',
  'index.html', 'package.json',
];
git('diff', '--exit-code', 'HEAD', '--', ...sourcePaths);
const sourceFiles = sourcePaths.map(relative => ({
  path: relative,
  sha256: sha256(readFileSync(path.join(sourceRoot, relative))),
  gitBlob: git('rev-parse', `${pinnedCommit}:${relative}`),
}));
const ts = createRequire(path.join(sourceRoot, 'package.json'))('typescript');
const moduleCache = new Map();
// Transpile only the local modules needed by Terrain. Reject unexpected package imports.
function loadSource(relative) {
  const full = path.resolve(sourceRoot, relative);
  assert.ok(full.startsWith(sourceRoot + path.sep), 'Source import escaped the checkout');
  if (moduleCache.has(full)) return moduleCache.get(full).exports;
  const module = { exports: {} };
  moduleCache.set(full, module);
  const js = ts.transpileModule(readFileSync(full, 'utf8'), {
    fileName: full,
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
  }).outputText;
  const localRequire = request => {
    assert.ok(request.startsWith('.'), `Unexpected external oracle dependency: ${request}`);
    const resolved = path.resolve(path.dirname(full), request + '.ts');
    assert.ok(existsSync(resolved), `Missing oracle module ${resolved}`);
    return loadSource(path.relative(sourceRoot, resolved));
  };
  const factory = new vm.Script(`(function(exports, require, module) {\n${js}\n})`, { filename: full }).runInThisContext();
  factory(module.exports, localRequire, module);
  return module.exports;
}
const { RNG } = loadSource('src/engine/rng.ts');
const { Noise } = loadSource('src/colony/noise.ts');
const { COLONY } = loadSource('src/colony/config.ts');
const { Terrain, Biome, BIOME_COLOR } = loadSource('src/colony/terrain.ts');
assert.equal(typeof Terrain.prototype.generateElevation, 'function');
assert.equal(typeof Terrain.prototype.classify, 'function');
const originalWorld = structuredClone(COLONY.world);
const seeds = [0, 4242, 314, -1, -2147483648, 2147483647];
const settings = { cells: 1024, cellSize: 4, heightScale: 240, seaLevel: originalWorld.seaLevel, noise: originalWorld.noise };
const rngVectors = seeds.map(seed => {
  const rng = new RNG(seed);
  return { seed, values: Array.from({ length: 16 }, () => rng.next()) };
});
const wrapRng = new RNG(4294967295);
assert.deepEqual(rngVectors.find(row => row.seed === -1).values, Array.from({ length: 16 }, () => wrapRng.next()));
const noiseCoordinates = [[0, 0], [0.1, 0.9], [1.3, 1.3], [2.6, 3.1], [-0.25, -3.75], [255.99, 256.01], [64.5, -128.25]];
const noiseVectors = [];
for (const seed of seeds) {
  const rng = new RNG(seed);
  const noise = new Noise(rng);
  const moisture = new Noise(rng); // The second shuffle consumes the SAME source RNG.
  const sample = ([x, z]) => ({ seed, x, z, value2: noise.value2(x, z), fbm: noise.fbm(x, z, 5), ridged: noise.ridged(x, z, 5), moistureFbm: moisture.fbm(x, z, 4) });
  const forward = noiseCoordinates.map(sample);
  const reverse = [...noiseCoordinates].reverse().map(sample).reverse();
  assert.deepEqual(forward, reverse, 'Source noise must not depend on sample order');
  noiseVectors.push(...forward);
}

function fieldHash(terrain) {
  // Canonical row-major little-endian bytes: float32 elevation, float32 moisture, uint8 biome.
  const bytes = Buffer.alloc(terrain.size * terrain.size * 9);
  for (let i = 0; i < terrain.elev.length; i++) {
    bytes.writeFloatLE(terrain.elev[i], i * 9);
    bytes.writeFloatLE(terrain.moisture[i], i * 9 + 4);
    bytes[i * 9 + 8] = terrain.biome[i];
  }
  return sha256(bytes);
}
function generateSourceBase(seed, cells, heightScale) {
  COLONY.world.size = cells;
  COLONY.world.heightScale = heightScale;
  // TypeScript 'private' methods remain methods after transpilation. Calling the original
  // methods on this receiver bypasses constructor passes for rivers, landing and buildability.
  const terrain = Object.create(Terrain.prototype);
  terrain.size = cells;
  terrain.elev = new Float32Array(cells * cells);
  terrain.moisture = new Float32Array(cells * cells);
  terrain.biome = new Uint8Array(cells * cells);
  terrain.water = new Uint8Array(cells * cells); // No river pass has run.
  const rng = new RNG(seed);
  terrain.generateElevation(new Noise(rng), new Noise(rng));
  terrain.classify();
  return terrain;
}
function coordinates(cells) {
  const last = cells - 1;
  const half = Math.floor(cells / 2);
  const points = [[0, 0], [last, 0], [0, last], [last, last], [half, half], [half - 1, half], [half + 1, half], [half, half - 1], [half, half + 1]];
  for (const seam of [64, 128, 256, 512, 768]) {
    if (seam + 1 < cells) points.push([seam - 1, half], [seam, half], [seam + 1, half], [half, seam - 1], [half, seam], [half, seam + 1]);
  }
  points.push([Math.floor(cells * 0.23), Math.floor(cells * 0.67)], [Math.floor(cells * 0.71), Math.floor(cells * 0.39)]);
  return [...new Map(points.map(point => [point.join(','), point])).values()];
}
const terrainVectors = [];
const fieldSummaries = [];
for (const { cells, heightScale } of [{ cells: originalWorld.size, heightScale: originalWorld.heightScale }, settings]) {
  for (const seed of seeds) {
    const terrain = generateSourceBase(seed, cells, heightScale);
    for (const [x, z] of coordinates(cells)) {
      const i = terrain.idx(x, z);
      terrainVectors.push({ seed, cells, heightScale, x, z, elevation: terrain.elev[i], moisture: terrain.moisture[i], height: terrain.worldY(x, z), biome: terrain.biome[i] });
    }
    const biomeCounts = Array.from({ length: 9 }, () => 0);
    let minHeight = Infinity;
    let maxHeight = -Infinity;
    let landCells = 0;
    for (let i = 0; i < terrain.elev.length; i++) {
      const h = (terrain.elev[i] - originalWorld.seaLevel) * heightScale;
      minHeight = Math.min(minHeight, h);
      maxHeight = Math.max(maxHeight, h);
      if (terrain.elev[i] >= originalWorld.seaLevel) landCells++;
      biomeCounts[terrain.biome[i]]++;
    }
    assert.equal(biomeCounts[Biome.River], 0);
    const summary = { seed, cells, heightScale, sha256: fieldHash(terrain), landCells, landAreaSquareMetres: landCells * settings.cellSize ** 2, minHeight, maxHeight, biomeCounts };
    fieldSummaries.push(summary);
    console.log(JSON.stringify({ phase: 'source-base', seed, cells, sha256: summary.sha256, landCells }));
  }
}
Object.assign(COLONY.world, originalWorld);
const result = {
  schemaVersion: 1,
  sourceRepository: 'https://github.com/duikindiesee/citylife',
  sourceCommit: pinnedCommit,
  sourceVersion: JSON.parse(readFileSync(path.join(sourceRoot, 'package.json'), 'utf8')).version,
  method: 'Original TypeScript RNG, Noise, Terrain.generateElevation, Terrain.classify and Terrain.worldY executed after CommonJS transpilation. Constructor and river/buildability/landing passes are not executed.',
  sourceFiles,
  sourceSettings: { cells: originalWorld.size, cellSize: 4, heightScale: originalWorld.heightScale, seaLevel: originalWorld.seaLevel, noise: originalWorld.noise },
  settings,
  coordinateDomain: 'Integer source cells, 0 <= x,z < cells. A Unity mesh vertex at x or z == cells is a new outer boundary and is not covered by this source oracle.',
  heightPrecision: 'Source elev and moisture have Float32Array rounding; source worldY calculates height as a double from that float32 elevation. A Unity float height needs a final float32 rounding.',
  fieldHashEncoding: 'Row-major z then x; each cell is float32-LE elevation, float32-LE moisture, uint8 biome (9 bytes per cell). Rivers disabled.',
  biomes: Object.entries(BIOME_COLOR).map(([id, color]) => ({ id: Number(id), name: Biome[id], hex: color.toString(16).padStart(6, '0') })),
  rngVectors, noiseVectors, terrainVectors, fieldSummaries,
};
const json = JSON.stringify(result, null, 2) + '\n';
mkdirSync(path.dirname(outputPath), { recursive: true });
if (process.argv.includes('--check')) {
  assert.equal(readFileSync(outputPath, 'utf8'), json, 'Committed source vectors are stale');
  console.log(`Verified identical source vectors: ${sha256(json)}`);
} else {
  writeFileSync(outputPath, json);
  console.log(`Wrote ${terrainVectors.length} terrain, ${noiseVectors.length} noise and ${rngVectors.length * 16} RNG values: ${sha256(json)}`);
}
