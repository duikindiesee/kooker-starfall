# Integrated coastal candidate

13 September 2026. Target: `0.0.7-integrated.1`, a separate Windows player assembled in `codex/starfall-integrated-preview`. **Source integration in progress; no combined-player acceptance yet.** Every existing branch and build remains separate. Protected main requires MoJoJo review; this checkpoint is not a merge/deployment request.

## Dependency order and current scope

1. Coastal main-world planning branch `8342aca`, retaining its finite coastal component geometry and panorama contract.
2. Environment component `47d1cdb` (including `ff5edad`): reuse its fixed-tick clock, weather/exposure/force contracts through a regional adapter. Do not instantiate its elevated test terrace in the world.
3. Autonomous inhabitant `75fc4cb`: preserve perception, deterministic goal selection, validated action API, optional provider cancellation/timeouts/fallback and pause/display controls. Successful model output inside a player remains unverified by that checkpoint.
4. Save/new-game documentation `f62c274`; reconcile the architecture without implying full save integration.
5. Accepted coastal build/evidence `ddff192` (including `761d4d5`), preserving the separate main-world planning branch and historical component evidence.
6. Cave-zone helper only from a clean reviewed commit; synthetic contract tests do not accept the actual cave.
7. Hunter clothing only after a clean source/evidence handoff passes continuous pelvis/crotch coverage and acceptable motion clipping. Bare shoulders are user accepted. No additional cosmetic gate.

The current regional terrain remains 180 by 200 metres, with visual-only ocean beyond it. The proposed 1200 by 1600 metre main world is not implemented by importing this component. Swimming, boats, a complete food chain, full-world saves and live memory recording are not added by this integration. [Planet progression](PLANET-MIGRATION.md) is separate architecture work.

The integrated source binds `starfall.integrated-coastal.v1` to the coastal generator revision plus the explicit integration revision. This is a versioned regional definition, not a production per-save world instance. NPC navigation samples the actual rendered ground collider, tests capsule clearance and slopes, and rejects deep-water routes. Every pickup/delivery still passes the original action authority, including terrain occlusion. The nearby refuge is a discoverable place, not a carryable item; its existence does not establish a persisted memory event or verified thermal safety.

## Visual and spatial acceptance

The user's Fish River Canyon reference supplies compositional direction: broad layered arid walls, open vistas and a long luminous light-turquoise freshwater corridor. The river should meander through deeper pools, rocky banks and crossings before visibly widening into darker sea. Avoid a cramped trench, cliff-like slabs, uniform water colour, disconnected ponds or a decorative route that cannot be traversed. Do not describe this full landscape as implemented from a source import or an aerial image.

The signature view is from an elevated canyon viewpoint and along the river axis toward the ocean opening. Frame the blue giant low beyond that opening, with several smaller separated moons. Celestial geometry must have coherent apparent scale, depth/parallax and very slow motion; reject visibly colliding discs or a flat decal cluster. Preserve the vista in appropriate weather/time states without concealing collision or performance defects. Current celestial motion is a visual ephemeris, not a gravitational orbital simulation.

The starting watershed should eventually contain substantial exploration: riverbanks, pools, overhangs/caves, crossings, terraces and resource pockets. High walls, steep slopes and deep water should form legible physical boundaries, with upper plateaus and offshore islands visible as future destinations. Boats and climbing later open routes. The inherited finite component edge is a technical limit, not proof that this progression/boundary design exists.

## First shelter gate

Near spawn, provide a small natural rock cave/overhang with a dry navigable floor, a visible entrance, safe entry/exit and clearance for standing, sitting, sleeping and carrying. It is a primitive refuge with space for future fire, storage and bedding, not a finished house. Check floor, entry sill, connected water ingress, roof/side collision, camera clearance and lighting from inside and outside.

Attenuation must come from the actual wind/driving-rain obstruction geometry with an entrance blend. Do not apply a blanket invented temperature bonus. Unverified thermal state cannot become a cold-safe claim. Verify the maximum regional water surface and a positive freeboard margin against the lowest floor/ingress; a nominal constant or shader property range alone is insufficient. The vertex shader now clamps wave strength, but actual material/water and geometry readbacks remain required. Place discovery and live memory write/readback are separate claims.

## Same-executable acceptance

| Claim | Status | Required evidence | Remaining gap |
|---|---|---|---|
| Source integration | In progress | Exact dependency commits, clean source, metadata/public-file safeguards and focused authority/control checks | Unity compilation and compatibility |
| Traversal and NPC autonomy | Unverified | Actual Windows player movement on the coast, real pickup/carry/delivery, slope/cliff/deep-water boundaries, escape from refuge, no falling through terrain | Combined runtime and native observation |
| Environment and shelter | Unverified | Fixed-tick weather, wind/water response, rain/exposure, paused clocks, exterior/entrance/interior comparisons, measured clearance/freeboard | Actual scene and thermal limits |
| Clothing | Awaiting eligible handoff, then recheck in candidate | Standing/walk/carry/crouch/seated multi-angle frames, continuous pelvis coverage, no unacceptable clipping | Same-executable coverage review |
| Mouse and menus | Source changes unverified | Click/RMB capture, clear hint, sensible configurable sensitivity, no capture jump, Escape release, usable pause/options cursor, intended resume capture, F11 window/fullscreen preservation in spectator and possession modes | Visible native input checks; injected Input System tests alone are insufficient |
| Signature vista | Unverified | Actual player cameras along canyon/outlet and at elevation, separated moons/giant, weather comparisons and measured timing | Wider canyon/roaming/progression remain future work |

`IntegratedAcceptance` exercises the real compiled components using per-process Input System devices and captures the rendered player. Its report explicitly leaves native mouse/window use and visual clothing review pending. It is not a replacement for those observations. Retain failed runs, exact build/source hashes, screenshots, runtime errors and timing evidence. A compiled executable alone is a candidate, not final-world readiness.
