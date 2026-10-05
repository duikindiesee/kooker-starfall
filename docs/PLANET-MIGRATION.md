# Planet-scale progression and staged proof

13 September 2026. **Architecture only. No spherical world, boat system or planet-save migration is implemented.** The immediate [integrated coastal candidate](INTEGRATED-CANDIDATE.md) remains a finite flat regional slice. The user's later refinement keeps near-term terrain flat/streamable at the systems level while horizon and sky suggest a planetary setting; do not convert this build wholesale to spherical gravity.

The long-term direction is a genuinely round traversable world with meaningful roaming and renewable survival resources. Separated islands should be visible across water but initially unreachable without acquiring boat-building/travel abilities. Early confinement comes from the canyon watershed's real walls, slopes and deep water; it must still contain substantial useful exploration. A painted globe, skybox or invisible boundary does not prove a traversable planet or progression.

## Architectural implications

| System | Future requirement |
|---|---|
| Coordinates and gravity | Define an immutable planet centre/radius/datum and world-scoped coordinate schema. Store high-precision planet coordinates and explicit local tangent frames; derive local up radially and apply gravity toward the centre. Character movement, jump, buoyancy, animation and camera must use local up, not hard-coded global Y. |
| Streaming | Version a globe tiling scheme with stable patch IDs, seeded biome/resource descriptors, seam continuity, physical units and LOD bounds. Floating origins change rendering/physics coordinates without changing saved identity or canonical position. Streamed physics and navigation must agree with rendered terrain. |
| Ocean | A continuous spherical water datum and bounded waves/tide/surge contract. The same surface determines immersion, shorelines, flood risk, buoyancy and horizon. Region transitions must not introduce cracks, double surfaces or dry-water seams. |
| Navigation | Geodesic/tangent navigation and connected patch portals, with local clearance/slope/capability checks. Explicit land, climb and boat graphs prevent NPCs from routing through deep sea or unloaded void. Revalidate after terrain edits and vessel movement. |
| Weather | Stable planet/time inputs and spatial weather fields with continuous patch boundaries. Sample local wind directions, rain, thermal/exposure and shelter in the same coordinate frame; avoid changing weather with camera origin or render order. |
| Saves | Follow [the save lifecycle contract](SAVE-GAME-CONTRACT.md). Version coordinate frames, generator/biome descriptors, canonical actor/vessel positions, local orientations, random streams and resource timers. Explicitly transform regional edits/objects through a reviewed migration or keep the regional save separate. Never reinterpret old X/Z coordinates as latitude/longitude. |
| Resources | Seed renewable food/water/material distributions by stable world/patch/object identity. Persist extraction, growth and regrowth state independently of chunk loading. Visiting another patch or reloading must not replenish everything. Early resources must support survival and a reachable boat progression path. |
| Horizon and scale | Choose production radius from traversal/progression needs and measured streaming/performance limits. Verify eye-height horizon, distant-island visibility, celestial angular separation and camera stability. A tiny test sphere is a geometry proof, not the final world size. |
| Boats and travel | Introduce explicit construction/material costs, boarding state, capacity, steering, currents, safe landing and land-to-vessel transitions. Reaching an island becomes a physical voyage with a validated return route, not a hidden-wall toggle. Save vessel/passenger/cargo state in one checkpoint. |

## Migration stages

1. Finish and visibly review the isolated flat coastal integration. Keep all old worlds, definitions and releases. Establish a world-surface interface and remove implicit global-Y assumptions only behind explicit adapters.
2. Create a separate, tiny spherical engineering fixture with its own world/seed/schema and build. Supply radial gravity, tangent camera/controller, closed ocean, two adjacent terrain patches and one NPC route. No regional-save conversion.
3. Add an explicit full checkpoint for that fixture, using the save contract. Prove quit/relaunch/load across a patch boundary and after an origin shift before calling persistence accepted.
4. Test patch streaming, seam collision, planetary navigation and deterministic biome/resource regeneration with persistent depletion. Establish memory/time/performance budgets and compatibility before scaling the radius/content.
5. Build a separate larger planetary candidate with multiple visible islands and a canyon starting watershed; add the first boat trip and return. Migrate a regional save only with an explicit supported mapping, backup and comparison report; otherwise preserve it as a separate regional world.

## Smallest playable spherical proof

Use a deliberately small test radius, approximately 300 metres, two adjacent terrain patches with a continuous walkable arc of at least 120 metres, a shallow shore and closed ocean surface. These are proposed proof dimensions, not a production-size choice. Include one stable-ID inhabitant, one pickup/delivery pair across the patch seam, and visible distant geometry for camera/horizon inspection. No full ecosystem or polished art is required.

In its separate Windows player, walk across visible curvature and the seam in both directions; turn the camera and jump without rolling, snapping or losing stable local gravity; compare eye-height water horizons on both patches; let the NPC navigate and deliver across the seam; save on the second patch, quit the process, relaunch and load with the same identity, inventory, orientation, weather tick and event boundary. Repeat with a controlled origin shift and reverse patch-loading order. Require zero falls through seams, no invalid routes, bounded camera/gravity error and explicit physics tolerance measurements. Retain source/build hashes, timings, native input evidence, screenshots and exact state comparisons.

The spherical proof is incomplete until persistence and NPC navigation pass in the actual player. A mesh sphere, physics unit test or editor camera orbit establishes only a component. Its result must not overwrite or relabel the flat coastal candidate.
