# Integrated coastal candidate

13 September 2026. Target: `0.0.7-integrated.1`, a separate Windows player assembled in `codex/starfall-integrated-preview`. **Compiled candidate available; 16 automated runtime checks passed. Native menu clicks passed; full native controls and clothing coverage remain pending.** Every existing branch and build remains separate. Protected main requires MoJoJo review; this checkpoint is not a merge/deployment request.

## Dependency order and current scope

1. Coastal main-world planning branch `8342aca`, retaining its finite coastal component geometry and panorama contract.
2. Environment component `47d1cdb` (including `ff5edad`): reuse its fixed-tick clock, weather/exposure/force contracts through a regional adapter. Do not instantiate its elevated test terrace in the world.
3. Autonomous inhabitant `75fc4cb`: preserve perception, deterministic goal selection, validated action API, optional provider cancellation/timeouts/fallback and pause/display controls. Successful model output inside a player remains unverified by that checkpoint.
4. Save/new-game documentation `f62c274`; reconcile the architecture without implying full save integration.
5. Accepted coastal build/evidence `ddff192` (including `761d4d5`), preserving the separate main-world planning branch and historical component evidence.
6. Cave-zone helper `c75357f`, including 31 synthetic contract checks; those checks do not accept the actual cave.
7. Hunter clothing `2c176575` after the clean source/evidence handoff passed continuous pelvis/crotch coverage and acceptable motion clipping in its separate player (compiled source `8659a92`). Bare shoulders are user accepted. No additional cosmetic gate. Repeat functional coverage in this candidate.

The current regional terrain remains 180 by 200 metres, with visual-only ocean beyond it. The proposed 1200 by 1600 metre main world is not implemented by importing this component. Swimming, boats, a complete food chain, full-world saves and live memory recording are not added by this integration. [Planet progression](PLANET-MIGRATION.md) is separate architecture work.

The integrated source binds `starfall.integrated-coastal.v1` to the coastal generator revision plus the explicit integration revision. This is a versioned regional definition, not a production per-save world instance. NPC navigation samples the actual rendered ground collider, tests capsule clearance and slopes, and rejects deep-water routes. Every pickup/delivery still passes the original action authority, including terrain occlusion. The nearby refuge is a discoverable place, not a carryable item; its existence does not establish a persisted memory event or verified thermal safety.

## Visual and spatial acceptance

The user's Fish River Canyon reference supplies compositional direction: broad layered arid walls, open vistas and a long luminous light-turquoise freshwater corridor. The river should meander through deeper pools, rocky banks and crossings before visibly widening into darker sea. Avoid a cramped trench, cliff-like slabs, uniform water colour, disconnected ponds or a decorative route that cannot be traversed. Do not describe this full landscape as implemented from a source import or an aerial image.

The signature view is from an elevated canyon viewpoint and along the river axis toward the ocean opening. Frame the blue giant low beyond that opening, with several smaller separated moons. Celestial geometry must have coherent apparent scale, depth/parallax and very slow motion; reject visibly colliding discs or a flat decal cluster. Preserve the vista in appropriate weather/time states without concealing collision or performance defects. Current celestial motion is a visual ephemeris, not a gravitational orbital simulation.

The starting watershed should eventually contain substantial exploration: riverbanks, pools, overhangs/caves, crossings, terraces and resource pockets. High walls, steep slopes and deep water should form legible physical boundaries, with upper plateaus and offshore islands visible as future destinations. Boats and climbing later open routes. The inherited finite component edge is a technical limit, not proof that this progression/boundary design exists.

The canyon rim must ultimately be difficult but achievable. Keep most sheer walls unclimbable early, but provide one discoverable staged ascent through readable ledges or a narrow switchback, with rest points and safe fall recovery; later simple gear/stamina may open it. The rim milestone should reveal river, ocean, islands, giant and moons together. Do not use arbitrary jump puzzles or grant an untrained NPC a fatal climbing route. Actual player/camera/fall and NPC capability-restriction checks are required before accepting the ascent; no such route is claimed in this component integration.

## First shelter gate

Near spawn, provide a small natural rock cave/overhang with a dry navigable floor, a visible entrance, safe entry/exit and clearance for standing, sitting, sleeping and carrying. It is a primitive refuge with space for future fire, storage and bedding, not a finished house. Check floor, entry sill, connected water ingress, roof/side collision, camera clearance and lighting from inside and outside.

Attenuation must come from the actual wind/driving-rain obstruction geometry with an entrance blend. Do not apply a blanket invented temperature bonus. Unverified thermal state cannot become a cold-safe claim. Verify the maximum regional water surface and a positive freeboard margin against the lowest floor/ingress; a nominal constant or shader property range alone is insufficient. The vertex shader now clamps wave strength, but actual material/water and geometry readbacks remain required. Place discovery and live memory write/readback are separate claims.

## Same-executable acceptance

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Separate Windows build | Passed compilation | preview-build.json: zero errors, 14 warnings | Warnings retained; not full-world acceptance |
| Integrated runtime | 16/16 automated checks passed | integrated-runtime.json; one autonomous delivery; no runtime errors | Synthetic Input System devices do not establish native keyboard acceptance |
| Native menus | Passed observed clicks | Local native-04 screenshots: Options, Controls, sensitivity 0.12 to 0.10; reset observed to 0.12 | Automated native P key had no observed response; hardware keyboard, capture, Esc, F11 and traversal pending |
| Clothing | Attached; partial visual review | Crouch-2 actual-player frame shows covered visible pelvis | Other angles are partly occluded; comprehensive moving coverage pending |
| Earlier builds preserved | Passed | Local preserved-builds-final.json: 5946 files checked, no differences | Scope is the recorded before-snapshot |


See [round 104 evidence](../evidence/milestones/coastal/round-104/README.md) for the exact compiled source and evidence boundaries.

`IntegratedAcceptance` exercises the real compiled components using per-process Input System devices and captures the rendered player. Its report explicitly leaves native mouse/window use and visual clothing review pending. It is not a replacement for those observations. Retain failed runs, exact build/source hashes, screenshots, runtime errors and timing evidence. A compiled executable alone is a candidate, not final-world readiness.

## Visual trail and publication boundary

Every accepted feature needs a linked trail: clearly labelled concept art/reference with provenance, original actual-player evidence tied to its executable/source, and a truthful romanticized Discord-ready draft that distinguishes playable behavior from planned ambitions. The retained [user concept reference](KOKERBOOM-REFERENCE.md), [hunter component evidence](HUNTER-CLOTHING.md), and [environment component evidence](ENVIRONMENT-TERRACE-COMPARISON.md) remain separate from this candidate's forthcoming captures. A concept image must never be captioned as a screenshot. Preserve original frames alongside any labelled crop/contact sheet.

Write the candidate's public-facing draft only from its verified results; a failure or incomplete gate stays visible in the draft. External posting requires explicit per-update approval. This workstream does not send media or summaries to Discord automatically.

Club acceptance reopened: the previously attached club is excluded from this candidate because the clothing owner reported knuckle intersection. Accepted clothing is retained; a corrected grip requires a separate clean handoff and compiled pose review.

Future design only: [knowledge progression, persistent death/return and resource transformation](WORLD-KNOWLEDGE-PROGRESSION.md) defines provenance, private-memory boundaries, inventory recovery and save/reload acceptance. These mechanics are not implemented by the current integrated preview.

14 September recheck: preserved candidate recovered; native Controls click entered possession. Native P injection again gave no visible response; hardware check pending. See [dependency queue](INTEGRATION-QUEUE.md). No new runtime build or mainline integration is implied.
