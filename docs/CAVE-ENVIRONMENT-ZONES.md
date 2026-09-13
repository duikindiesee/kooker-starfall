# First-refuge environment-zone contract

Status: integration guidance and tested shared C# model, **not actual-cave acceptance**. Isolated branch `codex/starfall-environment-zones` is based on `47d1cdb`; the accepted `starfall-environment` worktree, source, evidence and builds are unchanged. No Unity editor or live process is launched for this contract work.

## Confirmed integration inputs

Build a Living World supplied the following on 13 September 2026:

- Worktree `work/starfall-integrated`, branch `codex/starfall-integrated-preview`.
- Proposed world `starfall.integrated-coastal.v1`, revision `terrain-r2-weathered-banks.integrated1`, zone `first-refuge`.
- This is the 180 x 200 m regional coast. It is not the proposed 1200 x 1600 m main world.
- Tentative horizontal points: entrance x=-7,z=3; interior x=-11,z=0, on the dry west neck. These are **not measured spawn/floor elevations or authored cave geometry**.
- Ground must be measured against the actual layer-10 terrain mesh, cross-checked with `CoastalTerrain.Height`. Cave roof/walls require their own authored colliders.
- No cave is authored yet. Floor, lowest ingress sill and maximum tide/surge/flood stage are unknown. Flood safety is unverified.

## Contract: starfall.environment-zone.v1

Use [CaveEnvironmentZone.cs](../Assets/CityLife/EnvironmentZones/CaveEnvironmentZone.cs), an independent pure C# evaluator with no Unity, scene, AI, memory or accepted-baseline dependencies. It accepts immutable policy, outdoor weather, and an adapter-supplied geometry/thermal/water probe. Never set a global `sheltered=true` based merely on the cave name or a bounding-box overlap.

All distances/elevations are metres in the world +Y frame; wind points toward travel in m/s; temperature is degrees C; precipitation, occlusion and wetness are fractions 0..1. Outdoor wind is finite and <=30 m/s, matching the foundation envelope. Invalid weather produces `Valid=false`; retain the previous valid observation, mark current perception unavailable and do not award refuge status.

| Quantity | Proposed starting policy | Acceptance meaning |
|---|---|---|
| Portal blend | Smoothstep from 0 to 1 over 3 m inside the validated entrance path | No abrupt wind/rain/temperature jump at the entrance |
| Fully occluded interior wind | 0.10 x exposed vector | At least 85% attenuation required; direction remains unchanged in v1 |
| Fully occluded interior precipitation | 0.01 x exposed precipitation | At least 98% attenuation required, including wind-driven rain |
| Interior thermal target | Adapter supplies a validated local rock/air target; **no default credited heat** | During cold outside air <=5 C, refuge air >=7 C and >=6 C warmer than outside |
| Flood clearance | At least 0.75 m above the established maximum water level | BOTH lowest refuge floor and lowest connected ingress must meet it |
| Cold hysteresis | Enter <=5 C apparent; exit >=7 C | Same authored thresholds as the baseline; no medical claim |

The contract computes `freeboard = min(lowestRefugeFloorY, lowestConnectedIngressY) - maximumDesignWaterY`. Unknown maximum water, unknown floor/sill, nonfinite values or insufficient clearance keep `QualifiesAsDryRefuge=false`. Wind/rain attenuation may still be physically measurable in an unverified or flood-prone shelter; that must not be mislabeled a safe refuge.

The water maximum is the **local upper water-surface envelope**, including the applicable ocean tide, surge, river flood/runoff and wave crest, with its source/revision/design event recorded. The current visual wave amplitude is not a flood bound. Never substitute the -2 m nominal water datum for this unknown maximum. Assess every hydraulically connected opening, not only the visible doorway. Geometry changes invalidate prior floor/ingress measurements.

`RockAirTargetC` represents an explicitly authored thermal-buffer model, calibrated environmental measurement or established heat source. In the tests it is a synthetic 12 C input; that number is **not** an assertion about the future cave. Missing thermal validation yields no warming credit. Warmer shelter does not imply instant drying, and actors enter carrying their previous wetness.

## Unity adapter guidance for Build a Living World

1. Own the zone registry and authored volume in the integrated worktree. Match world ID, revision and zone ID exactly. Compute signed path distance from the entrance through the cave's actual navigable interior; outside is <=0. A generic box must not credit exterior points through a wall or beneath a separate overhang. Reject absent/unloaded/changed collision data.
2. Supply `GeometryVerified` only after roof/wall enclosure, standing clearance, entrance continuity and return navigation pass in the actual player. Use a dedicated world-geometry mask including the actual terrain and cave colliders; exclude characters, loose objects, triggers, water surfaces and presentation-only meshes. Layer 10 is the supplied terrain layer, not an assumption that every cave surface already belongs to it.
3. Measure `WindOcclusion01` using fixed, recorded upstream rays (negative normalized wind vector) from the actor's torso and a small fixed cross-section around it. Measure `RainOcclusion01` against the reverse of the actual precipitation travel vector, including horizontal wind drift. Use deterministic sample offsets and actual collider hits, not renderer visibility. Record sample count/ray length/mask. Five rays per phenomenon all blocked gives 1; four of five does not meet the full-refuge threshold. Test wind aligned with the entrance as the adverse direction.
4. Query `CaveZoneEvaluator.Evaluate` using the existing world clock's current weather. Feed its local vector to local particles, suitable loose objects and foliage, and its local precipitation to emissions/impacts. Use separate spatial samples or batches for different positions: **do not set a single cave wind global that also shelters the exposed valley**. The same sample drives player and inhabitant perception.
5. Step the new `ZoneExposure` once per accepted 0.02 s simulation tick, preserving wetness when crossing the portal. Pause prevents updates. **Do not feed these already attenuated/warmed values into legacy `ExposureState.Step(..., sheltered:true)`**: that would apply its extra +8 C warming and drying a second time. Adopt the new exposure path only in the integration branch; preserve the accepted baseline.
6. Persist actor exposure with the owning world's versioned save wrapper: world/revision, zone-policy fingerprint, contract ID, weather tick, actor ID, wetness, apparent C and cold hysteresis. Validate the entire wrapper before `TryRestore`; invalid exposure fields are rejected without mutation. The provided DTO round-trip check is not a production save-file implementation.
7. If zones overlap, choose one by explicit integer priority, then ordinal zone ID for ties. Do not stack warming or multiplicative attenuation. Zone selection cannot depend on traversal, registration order or render frames. Apply the selected zone's blend to the common exposed weather.

Suggested initial probe budget: at most 10 rays per actor at 5 Hz (refresh every 10 world ticks), immediate refresh after a portal crossing, >0.25 m movement, material wind-direction change or collider revision. Record the cache age. Budget two actors at <=100 routine rays/s; measure actual player frame cost before accepting this budget. Do not delay fail-closed invalidation of unloaded or changed geometry until a cache timer expires.

## Required actual-player evidence

Use the same seeded weather and fixed camera/settings for exposed, threshold and fully interior points. Test both player and inhabitant, with unchanged outdoor reference sampling. Retain a trace with tick, actor/zone IDs, world and geometry revisions, position, blend, all ray hits, raw/local wind, raw/local rain, raw/local/apparent temperature, wetness, cold flag, water-known flag, floor/sill/water elevations, freeboard and refuge status.

| Test | Required result |
|---|---|
| Strong cold rain, outside and inside simultaneously | Outside stays exposed; interior wind <=15%, rain <=2%; cold refuge temperature gates pass |
| Entrance-aligned wind and angled driving rain | No shelter credit through an unblocked entrance path; qualifying interior remains protected |
| Walk outside -> doorway -> interior -> outside | Smooth, reversible local changes; wetness carries across boundary; no leaked zone membership through walls |
| Wet/cold occupant in qualifying interior for 30 s | Wetness <=0.05 and cold clears with the tested thermal setting; actual geometry/thermal data must be reported |
| Both actors / shelter-seeking inhabitant | Both consume the same spatial contract; inhabitant reaches and remains in a valid refuge through real navigation |
| Pause / resume / save-load | No exposure drift while paused; validated reload continues from the same weather tick and actor exposure |
| Maximum design water/wave event | Every refuge floor and connected ingress maintains >=0.75 m clearance; retain elevations, envelope source and actual rendered water evidence |
| Unknown water bound, removed roof or geometry revision change | Refuge status withheld immediately; no fabricated flood safety or stale shelter credit |
| Runtime budget | Record resolution, frame percentiles, probe cost and ray counts in actual integrated player |

## Contract-test evidence and limits

[Compiled contract tests](../evidence/verified/environment-zones/contract-checks.json) run the **same C# source** using .NET 8, without Unity. Thirty-one checks passed: attenuation, temperature, threshold blending, invalid identity/geometry/thermal/water rejection, low floor/ingress, exact freeboard threshold, overflowing weather/clearance, drying/cold recovery, pause, deterministic replay, DTO continuation and atomic invalid restore rejection. Geometry, temperatures and flood elevations in these tests are synthetic. This does not establish Unity compilation, cave occlusion, flood safety, visuals, navigation or actual-player acceptance.

Run the test project with the installed .NET 8 SDK: `dotnet run --project tools/environment-zones/ContractChecks.csproj -- evidence/local/cave-zone-checks.json`. No external NuGet packages are required. The integration owner must compile the additive helper in the pinned Unity editor and run the above actual-player cases when cave geometry and a justified water envelope are available.
