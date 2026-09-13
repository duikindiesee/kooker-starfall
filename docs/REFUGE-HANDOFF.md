# Refuge integration handoff

Status: isolated v0.0.8-refuge.2 compiled from 88827d8ba38e541270250e9e4096f3b8046c8a08. Forty scripted checks passed in two visible runs. See FIRST-REFUGE.md for evidence and the borderline 33.665ms p95 result. No merge/deploy approval, integrated inhabitant or human-input acceptance is implied. Preserve the earlier v1 failed candidate.

## Binding contract

- Authoring: `CityLife.World.Editor.RefugeBuild.Attach(camera, ground)` currently includes both geometry and the standalone test controller/weather setup. Do not call it unchanged in the combined player: retain the existing inhabitant, camera/input controller, action authority and shared clock. Extract/bind geometry and contents first; omit the standalone `RefugeRuntime` controller and `RefugeRain` clock presentation when replacing them with integrated adapters.
- Stable candidate IDs: world `starfall.refuge-regional.v1`, revision `terrain-r2-weathered-banks.refuge2`, zone `first-refuge`. These identify this finite component candidate, not a completed large canyon. Integrated adoption needs its own explicit world-revision mapping and fresh evidence.
- Layer 10: actual terrain, dry cave floor and ramp. Layer 8: cave rock enclosure and solid props. Layer 9: player capsule. Geometry queries use 8|10 and exclude actors/triggers. Ground/support queries should use 10 from the actor's local feet; do not raycast from above the roof and assign that elevation to an interior actor.
- Proximity actions: fire toggle within 2.5m, storage transfer within 2m, rest within 2m of mat. The standalone controls are operator test affordances, not evidence that a naive inhabitant knows how to ignite a fire, use a plant, or craft anything. Bind future NPC proposals through the existing capability and authority gates.
- `HearthState` advances once per accepted 50 Hz world tick. Do not add a second wall-clock accumulator. Invalid local weather/site extinguishes; fuel and reserve counts are bounded. Session state is not a world-save implementation.
- Cave policy source is retained from environment-zones c75357f. Geometry, roof/wind rays, freeboard and local heat must be remeasured after adoption. The shader's fixed regional upper visual envelope is -1.889m; it does not cover future tide/surge/flood mechanics. V2 runtime remeasured floor/ingress at 1.8m, giving 3.689m limited regional clearance; adoption requires another fresh measurement.
- Preserve the current integrated overhang until the replacement geometry and movement tests pass in a separate combined candidate. Do not leave overlapping duplicate shelters or two player controllers.

## Required joined acceptance

Existing inhabitant routes from spawn into refuge, uses fire/rest/storage through its sole executor, and exits without roof-grounding, controller conflict or trapping. Verify camera/body clearance, local/exposed weather comparison, finite fuel and weather extinguish, bedding separation, saved-world identity, and visible player evidence. No dream/memory, predator-deterrence or defense claim until separately runtime-proven.

Integration order remains dependency-ordered and subject to the authoritative review queue: accepted coastal/environment inputs -> combined world -> exact refuge geometry/state/adapters -> joined inhabitant acceptance -> MoJoJo review. No authoritative task/workstream IDs or review receipts have been invented. [Knowledge progression roadmap](KNOWLEDGE-PROGRESSION.md) remains future design beyond safe hearth/bedding.
