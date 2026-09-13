# World foundation

## Identity and physical scale

`Assets/CityLife/Resources/IslandDefinition.json` pins `citylife.desert-island.v1`, seed 4242 and all generation settings. Its full SHA256 fingerprint includes an explicit ordered configuration and source commit. The default world ID is `citylife-island-foundation-4242-v1`; it is not the browser's `seed-4242` world.

The region is 4096m square: 1024 cells at 4 m, plus the final boundary row/column for a closed mesh. It spans 16.78km² including ocean. Height scale is 240 m per normalized elevation unit, compared with 54 in the old 608-cell browser region. This increases visual relief as well as room for future neighbourhoods. The highest actual ground and dry-land area are measured by generation and recorded in validation/runtime evidence. None of the area figures is a promise of a house count.

## Determinism

The source mulberry32 PRNG, permutation tables, value-noise interpolation, fBm/ridges, radial mask and biome classifications are arithmetically ported. Doubles preserve the source computation; float32 conversion occurs where the browser stored its arrays. Source river carving, road grading and placement pads are omitted intentionally. This expanded v1 island is therefore not a full recreation of an old browser save.

Samples depend on immutable seed permutations and absolute grid coordinates. Chunk order and player travel do not advance a random stream. The independent oracle script executes original TypeScript methods from commit `b71307023aea600da27aa3eada6cdcce7ba758ed`; it does not copy the C# equations. See `tools/reference-vectors.mjs` and `Assets/CityLife/Resources/SourceVectors.json`.

Regenerate the oracle only against that pinned source checkout with its TypeScript dependency installed:

```powershell
node tools/reference-vectors.mjs ../citylife-source --check
```

Normal Unity builds use the checked-in reference values and need no Node installation. The stronger tests compare full independent regeneration, selected original source heights/biomes, random/noise sequences, reverse chunk traversal, ocean boundaries and edit isolation. Bit-for-bit cross-CPU/platform portability beyond the tested Windows build is not claimed; source sample tolerances are recorded.

## Edits and future shared state

The base terrain remains immutable. `WorldEdits` carries its own schema, world ID, revision and base fingerprint; terrain deltas are separate from generated arrays. Loading validates the whole candidate before any mutation. A mismatch fails with an explicit error, retaining the original file. Saves write a temporary file, atomically replace the previous file and retain a backup. The current client has no terraforming tool; this boundary and save/load checks establish how future edits can be applied safely.

Generation configuration is copied when a field is constructed. Mutating loaded field settings is rejected at generation/save application/hash boundaries. A changed version/config identifies a new world, never an automatic migration of existing houses. An actual shared service must supply an authoritative world identity, base definition, revisions and persisted edits; distributing the same seed alone does not implement multiplayer. No service endpoint or authentication contract has been invented here.

The existing browser spatial contract has frames, zones, reservations, placements, roads, ways, terrain edits, networks and portals. Future converters must explicitly validate that contract and perform coordinate/version migration. This first milestone does not deserialize it as a Unity edit file or silently discard those fields.

## Rendering and navigation

The complete height/biome base is generated once, away from the render thread. Unity draws 256 m chunks using 4 m/16 m/64 m sampling. After initialization, meshes are cached per resolution with at most two changes per frame and distance hysteresis. Initialization builds all coarse chunks and the initial requested detail. Skirts close mixed-resolution cracks; normals sample neighbouring global coordinates. The base stays resident; this is bounded LOD caching rather than loading an infinite world from disk/network. Detail is selected from distance and camera height.

The sea uses the original three directional sine waves on the GPU, with derivative filtering for distant subpixel ripples. Depth colour and foam consult the generated bathymetry at texel centres. Quiver trees retain the original forked form and bark/succulent palette; dry scrub gets sparse colourful plants. Deterministic coordinate hashes scatter vegetation and small stones. Persistent meshes per species/chunk make the vegetation visible in both normal and offscreen rendering. These decorative meshes are resident alongside the terrain and are distance culled; they are not an infinite vegetation streaming system.

The close-up pass adds procedural six-blade dry tufts and small stones plus a credited CC0 ground image at a three-metre tile scale. The image supplies triplanar surface detail while the biome palette controls colour. These decorations do not change the terrain fingerprint or saved edits. The **4 / Detail** camera selects an existing grass tuft near Landing without adding a plant for the view. Day/night remains an exploration preview, not the browser's canonical shared clock. See [asset credits](ASSET-CREDITS.md).

Walking follows the highest-detail mesh triangles, blocks water/steep gradients and preserves eye clearance. A newly visited chunk can briefly display a coarser surface while detail settles; the grounding rule remains the fine grid. Flight/orbit allow large-distance inspection. This is a terrain explorer, with no character model, rigid-body traffic or object collision game yet. Later roads, plots and building tools can adopt Unity-native approaches without preserving the browser's implementation problems.


## Knowledge progression and persistent death/return: design contract

The user-directed [knowledge progression rules](STARFALL-KNOWLEDGE-PROGRESSION.md) define naive starting knowledge, provenance/confidence/corrections, persistent-world death and return, recoverable inventory, at-most-one grounded lesson, privacy, and staged ecology/survival milestones. These are planned, not runtime-proven. Death lessons and post-return reflection remain after the current one-living-thought proof. Existing memory event schema v1 and old saves are unchanged.
