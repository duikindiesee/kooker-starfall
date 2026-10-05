# CityLife Unity island foundation: source and scope

This milestone is an explorable CityLife island foundation. The user explicitly permitted a new, larger island and better Unity-era road, plot and building approaches. Exact copying of the browser's roads, saved placements and terrain was waived. Full gameplay, multiplayer, household runtimes and Susie remain later milestones. A successful terrain build does not establish those features.

## Source baseline and identity

The browser baseline is [`duikindiesee/citylife` at `b71307023aea600da27aa3eada6cdcce7ba758ed`](https://github.com/duikindiesee/citylife/tree/b71307023aea600da27aa3eada6cdcce7ba758ed), package version `0.53.1`. Its `index.html` loads `src/colony/main.tsx`, so the reference is the current colony experience, rather than the legacy v1 town. The coordinating task's live pre-port inspection recorded the public login stamp `0.53.0 / 00c58d2`. No authenticated production world or player save was inspected or transferred. The live stamp and Git baseline differ; this foundation does not claim to reproduce production's exact current world.

The useful identity comes from implementation, not from assuming a proposal is shipped:

| Source implementation | Character or boundary carried forward | Unity milestone boundary |
| --- | --- | --- |
| [`terrain.ts`](https://github.com/duikindiesee/citylife/blob/b71307023aea600da27aa3eada6cdcce7ba758ed/src/colony/terrain.ts), `BIOME_COLOR` | Namib-like stony tan flats, rust dune shoulders, dark exposed rock, pale coastal sand, turquoise shallows | Source palette and base terrain mathematics are reused. Unity materials and lighting still require visual validation. |
| [`quiverTreeLogic.ts`](https://github.com/duikindiesee/citylife/blob/b71307023aea600da27aa3eada6cdcce7ba758ed/src/colony/render/quiverTreeLogic.ts), `calculateQuiverTrees` | Rare quiver trees on eligible rocky ground, coordinate-derived scatter and age variation | Distinctive vegetation is a visual reference. The old road/plot exclusion and exact scatter layout are not parity requirements. |
| [`foliageLogic.ts`](https://github.com/duikindiesee/citylife/blob/b71307023aea600da27aa3eada6cdcce7ba758ed/src/colony/render/foliageLogic.ts), `calculateFoliagePositions` | Sparse cyan, magenta, lime, violet and amber flora, using an avalanche hash rather than a repeating lattice | Preserve the visual family; do not restore dense temperate forests. |
| [`darkCity.ts`](https://github.com/duikindiesee/citylife/blob/b71307023aea600da27aa3eada6cdcce7ba758ed/src/colony/render/darkCity.ts), `buildDarkCity` | Floating dark rock slab, cyan waterline, starfield and distant gas giant | This is scenery and world identity, not a multi-planet gameplay design. |
| [`scale.ts`](https://github.com/duikindiesee/citylife/blob/b71307023aea600da27aa3eada6cdcce7ba758ed/src/colony/scale.ts) | One world unit is one metre; one source grid cell spans four metres; adult eye height 1.6 m | Preserve understandable physical scale in Unity traversal and future houses. |

The source biome names remain `Forest`, `Plains`, etc. even though their appearance is desert. Source comments and classification code explain that those IDs also affect settlement and route decisions. This milestone reuses IDs for terrain colour, without promising to carry over the old siting rules.

## A deliberately new world version

The committed [IslandDefinition.json](../Assets/CityLife/Resources/IslandDefinition.json) defines `citylife-island-foundation-4242-v1`, generator `citylife.desert-island.v1`, seed `4242`, 1024 cells, four metres per cell and height scale 240. It covers a **4.096 km by 4.096 km square**, including water. That square is **16.777216 km²**, not sixteen square kilometres of buildable land. The independently executed source base at these settings measures **6.007008 km² of above-sea grid cells** for seed 4242; this is a sampled horizontal land area, not a claim that all of it is flat, buildable or populated.

The browser baseline uses 608 cells (2.432 km across) and height scale 54. The Unity square is about 2.84 times the browser square's area. Increased relief is deliberate; 240 is a height conversion factor, not the measured tallest peak. Changing dimensions or relief changes the world even if the seed is unchanged. The road/river/settlement passes are absent from this base. **Reusing seed 4242 does not make this the browser's existing saved world.**

The port preserves the actual browser `RNG`, shuffled value-noise, fBm, ridged elevation, radial coast mask, float32 elevation rounding and pre-river biome classification. It does not substitute Unity's global random stream or an unrelated noise algorithm. The square's 1024 cells become 1025 mesh vertices along each axis so the Unity mesh closes at its outer edge. Source vectors only cover the original source domain `0 <= x,z < cells`; the added outermost mesh vertex requires Unity-specific seam/boundary validation.

At a four-metre grid, the terrain supports broad terrain forms, rather than sub-metre construction grading. A 64-cell chunk covers 256 m per side. The intended performance tradeoff is detailed nearby terrain and simpler distant terrain. Increasing the resolution doubles each axis and approximately quadruples field/mesh work; an enormous fully detailed populated world is not promised. Runtime frame timing, mesh count, memory and walk/fly behaviour must be measured in the actual Unity player before calling this milestone complete.

## Determinism and edit boundary

[IslandDefinition.cs](../Assets/CityLife/Scripts/IslandDefinition.cs) validates the generator and settings and fingerprints the complete definition. [WorldEdits.cs](../Assets/CityLife/Scripts/WorldEdits.cs) stores terrain deltas separately under `citylife.unity.edits.v1`, with `worldId`, `baseFingerprint` and `revision`. A mismatched world, generator/config fingerprint or invalid edit must be rejected before mutating the active terrain. A future generator upgrade must use an explicit migration or a distinct world; silently regenerating underneath saved houses is unacceptable.

The base terrain samples global coordinates. Each noise permutation is created once from the world seed; sampling consumes no further random values. Loading chunks in a different travel order must therefore sample the same base. Reproducible scatter must likewise derive from global coordinates and a versioned rule. World identity, generation settings and edit revision are distinct concepts; a seed alone does not preserve edits or establish shared multiplayer state.

This is a local world foundation. The edit format is a boundary for later persistence and network work, not proof of server synchronization, ownership, multiplayer, an in-game terrain editing tool, or compatibility with an existing player's browser save.

## What the browser actually supplies for later integration

| Implemented source boundary | Evidence | Consequence for later Unity work |
| --- | --- | --- |
| Versioned durable spatial document | `src/colony/spatial/worldLayoutDocument.ts`: `citylife.world-layout/v1`, generator `citylife-v3`, SHA-256 revision metadata, frames, placements, roads, terrain edits and other spatial records | Build an explicit converter with coordinate/frame and identity validation. Do not rename a Unity save to this schema and call it compatible. |
| Boot barrier and revision validation | `src/colony/worldLayoutBoot.ts`: checks world ID and stored revision, captures or hydrates the layout before declaring readiness | Preserve atomic loading and world/revision checks when a compatible service is actually connected. |
| Local revision store | `src/colony/worldLayoutStore.ts`: Dexie/IndexedDB records, transaction/revision conflict handling, corruption errors and bounded rollback | This is real local persistence code; it is not evidence that all browsers already synchronize a shared world remotely. |
| Asset manifest loader | `src/colony/stores/useWorldAssets.ts`: obtains an auth token, fetches an asset manifest, records failure and uses fallback assets | Its configured target is for local development. No production service URL or credentials have been inferred or copied. Unity service integration remains unverified. |
| Fixed simulation steps distinct from frame rendering | `src/colony/runtime.ts`: frame loop accumulates time and uses `1 / COLONY.time.stepsPerSec` for simulation stepping | Keep future simulation/bot work independent of rendering frames. No simulation, runtime provisioning or bot reasoning is implemented by this terrain milestone. |

Roads, plots, grading and house construction will be designed deliberately after this foundation. The user allowed those browser-era rules to improve. Shared household identity, private runtime boundaries and a future Kooker HQ development/commercial area remain product direction, not claims of completed features. A visible house or character must not be treated as a connected bot runtime. Private archives, personal identities, credentials, operator profiles and player data must remain outside published terrain/assets.

## Oracle and provenance

[tools/reference-vectors.mjs](../tools/reference-vectors.mjs) executes the pinned browser TypeScript itself after transpilation using the source checkout's installed TypeScript. It calls original `Terrain.generateElevation`, `Terrain.classify` and `Terrain.worldY` methods on an initialized receiver; it deliberately bypasses the constructor's river, distance-to-water, buildability and landing passes. The source's terrain/noise formulas are not reimplemented in the JavaScript test generator.

[SourceVectors.json](../Assets/CityLife/Resources/SourceVectors.json) contains:

- Exact source commit, source version, raw-file SHA-256 and Git blob IDs for 14 relevant source files.
- 420 terrain vectors at source 608-cell and Unity 1024-cell settings, including corners, interior points and both sides of multiple chunk seams.
- 42 noise vectors, including negative coordinates and permutation wrap boundaries, plus 96 RNG outputs.
- Seeds `0`, `4242`, `314`, `-1`, signed int minimum and signed int maximum.
- Twelve complete pre-river field hashes and measured land/biome counts. Each hash encodes row-major float32 elevation, float32 moisture and uint8 biome in explicit little-endian order.
- Source-noise forward/reverse sampling equality and unsigned seed-wrap checks.

Run from the Unity repository after `npm ci` in the pinned sibling source checkout:

```powershell
node tools/reference-vectors.mjs
node tools/reference-vectors.mjs --check
```

An alternate source checkout and output file may be supplied as the first and second positional arguments. The script refuses a different source commit or changed tracked input files. `--check` regenerates in memory and requires byte-identical JSON. It does not rewrite the expected output.

Raw-file hashes describe the actual checked-out bytes; Git blob IDs identify canonical repository contents independently of checkout line-ending conversion. To compare field hashes with the C# implementation, use the documented encoding and source domain. Unity's height-only hash over a closed mesh is a different quantity and must not be compared directly to this elevation/moisture/biome oracle hash.

The source HQ packs include accompanying procedural provenance. For example, `hq-campus-shell-pack.PROVENANCE.md` records original generated geometry, metres/Y-up/+Z-forward coordinates, material colours without textures and the repository's own asset terms; `hq-reception-pack.PROVENANCE.md` records the reception furniture similarly. These are useful future import candidates, but this provenance review does not establish Unity import, materials, collisions, traversal or service integration. No third-party licensing grant is invented, and public repository visibility alone is not a general asset licence.

## Evidence ledger for this subtask

| Claim | Status | Evidence | Remaining gap |
| --- | --- | --- | --- |
| Reference code is the pinned current source baseline | Verified | Oracle enforces Git HEAD and clean tracked inputs; JSON records commit, version, 14 file hashes and Git blob IDs | Production login build differs; authenticated production world was not inspected |
| Independent numerical reference values exist | Verified | `node tools/reference-vectors.mjs` executes original TS methods and emits 420 terrain, 42 noise, 96 RNG values | C# comparison, Unity mesh seams and runtime behaviour need their own test evidence |
| Reference regeneration is deterministic | Verified | `node tools/reference-vectors.mjs --check` requires byte-identical generated JSON; noise sample-order and seed-wrap assertions run | Cross-runtime C# equality is a separate check |
| The 1024-cell seed-4242 base has about 6.007 km² of land | Verified for source oracle at new settings | `fieldSummaries`, 375438 above-sea cells × 16 m²; field SHA-256 `97491e5c6817b76ab448cb25ab32e3fb5e9824063b6ceb5a269a2d46d734607d` | Does not establish usable building area, existing settlements or player performance |
| Existing roads, plots, houses, multiplayer and bots are ported | Deferred | Explicit scope above | Later milestones |
| The Unity island is playable and visually validated | Not established by this subtask | Build and runtime evidence must be provided separately | Actual player traversal, appearance, collision and measured performance |
