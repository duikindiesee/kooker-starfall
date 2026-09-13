# Coastal main-world acceptance

The user's 13 September direction supersedes the close-up coastal composition as the main-world target. The original four-screen Windows panorama is the visual authority. The retained high-resolution panorama (reference 06, 6400 x 2880, SHA256 `054709501737b038f26aad0d1ef5a846f3955f44efc7bdf3b5d2b2cc41a8e8ec`) was visually inspected in this workstream. It shows open space between trees, distant environmental-scale ridges, rocky shores, luminous cyan water, a blue giant and galaxy, and a continuous submerged landscape. Exact correspondence between this attachment and the original four-screen Windows presentation still needs verification; do not silently substitute a component screenshot.

The preserved 180 x 200 metre `starfall.coastal-slice.v1` scene is a component study. Its player bake tests controller and collider plumbing only. It cannot satisfy the main-world scale or landscape acceptance gate.

## Proposed finite world

Use a new explicit world identity, `starfall.coastal-world.v1`, with immutable seed, dimensions, content revision and source fingerprint. Initial design envelope: 1,200 x 1,600 metres, with a walkable shore route of at least 600 metres and layered mesa walls roughly 80–160 metres above the river. These are design targets, not measured implementation facts. Retain the tree at its established physical scale; do not enlarge everything uniformly. Connect the rocky tree bank, open sand plain, canyon passage, river mouth, sea overlook and submerged shelf as one landscape. Boundaries must be legible and physically enforced. Distant decorative geometry must be labelled separately from reachable land.

## Required player evidence

| Claim | Status | Evidence required | Remaining gap |
|---|---|---|---|
| Spacious finite world | Design target | Actual player route from tree bank through canyon to sea; coordinates, travelled metres and elapsed time | Main-world terrain and route unimplemented |
| Environmental-scale cliffs | Unverified | Normal 1.85 m eye-height views at bank, canyon foot and overlook with measured ridge heights/distances | No acceptance from aerial or close-up harness images |
| Coherent panorama language | Reference inspected | Comparable actual player wide views showing open spacing, pale tree, turquoise river/sea, mesas, giant and galaxy | Main-world rendering and user review |
| Real traversal and collision | Component implementation in progress | Native WASD/RMB traversal plus automated body sweeps against real terrain, rocks and tree; no falls or clipping | Build/runtime checks and native acceptance |
| Underwater continuity | Design target | Enter water, cross surface, traverse submerged shelf and return to shore in the same player; stable buoyancy, view transition and collision | Component player blocks deep-water walking; swimming absent |
| Finite bounds | Unverified | Test every edge and water approach without falling into void or entering visual-only ocean | Main-world bounds unimplemented |
| Performance/readability | Unverified | Actual player timing distribution across a full route, resolution/hardware recorded; inspect captures for occlusion/scale | No sustained performance claim |
| Future inhabitant integration | Design only | Stable location/object IDs, world fingerprint, reachable activity anchors and perception boundaries | NPC integration deliberately separate |

## Inhabitant boundary

Future location IDs: `tree-bank`, `canyon-gate`, `river-mouth`, `sea-overlook`, `submerged-shelf`. Objects require stable IDs and explicit capability/activity metadata; proximity does not grant permission. Navigation must derive from the same collision surfaces used by the player. Reachability and discoverability must be measured, including unavailable underwater capabilities. World revisions must invalidate stale navigation and observations explicitly. The existing NPC executor remains the sole action authority, and memory provenance remains owned by its existing workstream. This branch does not connect a model, publish memory events, or introduce an alternative executor.

## Preservation and checkpoints

## Environment integration contract

The separate environment workstream's `Assets/CityLife/Environment/EnvironmentModel.cs` was read on 13 September. `Starfall.EnvironmentFoundation.IEnvironmentSurface` supplies `WorldId`, `Revision`, `PhysicalBounds`, `Contains`, `TryGround` (height and normal), `WaterLevel`, `WaterDepth`, and `Current`. WaterDepth is bed-to-surface depth; signed immersion is `WaterLevel(position) - position.y` only where water depth is positive. The component adapter currently returns zero current and samples the analytic coastal terrain; this is not an implemented flowing river or an exact rendered-triangle contact test. Player body motion must still use actual collision geometry.

The environment clock uses 0.02-second ticks. Main-world integration must supply a new adapter for the new physical bounds and world revision, validate all route samples against rendered/collision terrain, and consume the shared weather/water contract without copying its simulation into this branch. The adapter has been inspected as source only here; the environment owner's reported tests and pending compiled-player checks are separate evidence. No environment source has been imported, no runtime has been connected, and no Unity process is controlled by this documentation change.

Start: preserved coastal commit `30bdefbbc6731b611848054d9aa86887cd7a7799`. Isolated branch: `codex/starfall-coastal-playable`. Retain R01/R02 studies and all existing R06/R19 releases. Component checkpoint R03 remains labelled as such even if its player checks pass. Each later world revision gets unused build/evidence directories, source hashes, runtime results and a milestone entry. No push, merge, deployment or publication is authorized.
