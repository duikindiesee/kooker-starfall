# Isolated Eden food fixture and integration contract

Status: v0.1.0 berry player passed 59 compiled checks and separate-process reload. Additive v0.1.1 Eden/physiology/return revision is under validation; no combined-world integration performed. Base checkout: `75fc4cb`; isolated branch `codex/starfall-food`. No model service, asset downloads, external publisher, hunting or weapon system is involved.

## Bounded world

Two mature purple-red berry bushes start with two fruit each for seed 4242 (the primary has three for odd seeds). At most four living plants, including the garden, may exist. Each missing primary fruit takes 30 eligible simulation seconds to return. A ripe fruit expires after 90 seconds on its bush; fallen seeds live for 40 seconds and attempt germination at age 30. The seeded viability gate admits approximately one in twelve candidate seeds before soil, season, spacing, population and resource checks. At most one natural germination occurs per 300-second ecology epoch. Cultivation takes 60 eligible seconds. These are intentionally compressed fictional fixture timings, not real botany.

Wet season, soil water ≥20 and temperature 10–35°C permit growth. Germination additionally requires water ≥40, an unused suitable site, capacity and a 20-unit water budget. Regrown fruit consumes two water units. Sites are a fixed small fixture graph with separate, spaced moist-soil locations; this is not a general terrain ecology solver. Severe cold below 0°C or water below ten kills seedlings after 60 stressed seconds and mature bushes after 180. Unsupported season/weather merely pauses growth. Manual Wet/dry and Cold/mild buttons are explicit fixture environment inputs, not forecasts. Watering uses 250 ml of finite freshwater to add twenty soil-water units.

Harvest removes one ripe berry, never the bush. Eating consumes carried food, increases food reserve and hydration, remembers the receipt and retains a visible inventory seed if there is space. Unknown food stays forbidden even at zero food reserve. The signed berry lesson and cultivation lesson are authored safe discovery events requiring actual perception and reach. Seeing a seed germinate records an observation; it does not magically prove edibility. No local model is needed.

Camp aid is an explicit assistance action, visibly logged and counted in the world save. It restores minimal needs, at least one seed and a limited water reserve. It does not regenerate plants or silently refill ordinary resources. Repeated aid marks an assisted run; it is not a claim of a closed sustainable food economy. Old worlds remain available as separate save files after New World or Clean reset.

## Build a Living World adapter

Bring only `Assets/CityLife/Food/FoodModel.cs` and `EdenEcology.cs` into a reviewed integration branch, plus their Unity metadata. `FoodWorld` and `FoodBuild` are the separate fixture and must not replace the combined-world bootstrap. `FoodChecks` can run in that branch as a contract suite.

| Boundary | Concrete hook | Combined-world responsibility |
|---|---|---|
| Identity | `new FoodModel(worldId, generationId, seed)` | Use the authoritative save identity, distinct reset generation and pinned ecology rules `food-eden.2`; never use a global NPC cache |
| Clock | `FoodModel.FixedStep(paused)` | Call once per authoritative 50 Hz fixed tick, never once per rendered frame; do not run the fixture's clock too |
| Weather/soil | `FoodState.wetSeason`, `temperatureC`, `soilWater` | Trusted environment adapter supplies quantized, recorded inputs before each ecology second; one soil/water authority only. Current fixture uses explicit test controls |
| Perception | Existing `NpcPerception.Sense(tick)` and stable `NpcInteractable` IDs | Bind target registry to the combined world and generation; terrain placement must map `berry`, `berry2`, `berry3`, `berry4`, `bed`, `spring`, `sea` to actual entities and approach points |
| Permission/safety | Implement `IFoodAccess.Inspect(target)` | Return actual visibility, reach, permission and authoritative freshwater verification. Never accept these booleans from a model/network request. Freshwater verification must include water quality/salinity rather than visual colour |
| Actions | `FoodModel.Execute(world, generation, requestId, FoodAction, target, access)` | Feed high-level intent through the trusted action scheduler; IDs monotonic within world generation; stale/reset/cross-world requests rejected. Use receipt deltas, not animation completion, to report success |
| Navigation | Fixture uses `CharacterController` approach movement | Replace with the combined world's navigation/action scheduler; cancellation or unreachable target must not execute the action remotely |
| Water quantities | `FoodState.freshwaterMl` | Current fixture owns a 2 L source reserve. If combined water already owns volume, move this transfer into its atomic transaction coordinator; do not maintain two independent mutable reserves |
| Save | `Json()`, `Restore(payload, world, generation)` | Place the food payload in the same atomic versioned world snapshot as environment, transforms and memory. Save/reload must occur at a tick boundary; do not mix this fixture's standalone disk save with a second world-save writer |
| Player logs | `FoodReceipt`, `EdenEvent`, existing `NpcDecisionLog.Record` | Display observed fact, proposed goal, permitted action, receipt and provenance separately. Persist food beliefs/outcomes per world; label assistance and fixture interventions |
| External memory | Existing `StarfallMemoryExport` currently only accepts pickup/deliver | Do not mislabel food actions as pickup/deliver. A separately reviewed food-event mapping/schema extension is required; no external memory service is connected here |
| Optional model | No new inference API | Existing broker may select bounded food goals from permitted observations/knowledge. It cannot mutate `FoodState`, grant evidence, alter water or declare food safe |

The fixed site IDs and one inhabitant are deliberate bounds. Before wider integration, replace the fixture site graph with a stable terrain registry, extend namespace ownership for multiple actors, and coordinate action IDs and atomic water/inventory transfers. Do not describe those connections as already installed.

## Save contract

The checksum envelope `starfall.food-save.v1` contains `starfall.food.v1` with rules `food-eden.2`, identity, seed, clock/subtick, nutrition, inventory/seeds, berry stocks/timers, all plant growth/stress/death state, dropped seed identity/age/outcome, ecology events, knowledge provenance, assistance count, environment inputs, last action receipt and actor position. Load validates a candidate before mutation. Writes flush a temporary snapshot and rename it to an immutable content-hashed filename. Only the active pointer is atomically replaced; all prior snapshots remain. New worlds and resets use new identities but deterministic initial state for a given seed. There is no offline growth and no migration from earlier draft food rules.

## Evidence gates

Model checks run in the editor and again inside the compiled food player. Scripted player acceptance moves the real capsule through perception/range gates and records rendered screenshots, receipts and decisions. A separate executable process reloads the saved snapshot. Computer-use input and actual human acceptance are separate gates; file existence, a build and model assertions alone do not prove the playable loop.

The concept [storyboard](FOOD-CONCEPT.svg) is illustrated design intent. Runtime screenshots must be labelled independently and may be simpler than the concept.

## Staged physiology and persistent return (v0.1.1)

The revised rules ID is `food-eden.2`; no automatic old-save migration. The interactive save folder is separately named `food-eden-2`; v0.1.0 saves and builds remain intact. The state now contains an inhabitant ID, body incarnation and `FoodBody`: stomach fullness, energy (the existing `satiety` field), hydration, fat reserve, protein sufficiency, health and fatigue. A meal increases fullness and available energy; fullness above the next meal capacity rejects consumption without removing inventory. Activity costs more energy/water. Deficits consume fat before health, while sustained high energy slowly transfers into fat. Low protein delays recovery only after a long deficit; it is not an instant death meter. These are fictional game units, not a medical model.

Basal energy expenditure is 2 units/second; movement adds 3. Hydration uses 4 idle or 6 active units/second. Food/water warnings appear below 20%; the ordinary starting body does not die from one missed meal. Severe zero-water exposure has a 900-second grace period before health loss; zero energy and fat has a 3600-second grace period. Sheltered rest lowers fatigue and slowly restores health when energy/water/protein permit. The shelter gate is actual distance to the fixture refuge, not model prose. Movement slows under sustained deficit/fatigue. Body shape is unchanged.

At verified death, append a hash-linked `FoodDeath` with world, generation, inhabitant, incarnation, tick, cause and measured conditions. Inventory transfers into a persisted owned `RecoveryBag` at the refuge. `Return` creates baseline physiology in a new body incarnation, retaining the world clock, ecosystem, structures (owned outside food), memories and death history. `Recover` requires actual refuge reach/permission and consumes bag contents only up to inventory capacity. Neither action deletes or rewinds an old save. The current small fixture retains up to 128 death records per save; arbitrary lifelong archival is not claimed.

A starvation death can teach at most one not-yet-known supported mechanic: a previously known-safe berry restores energy; or, only after the bed was observed and a seed already existed, saved seeds can be planted in that known bed. No eligible lesson means no fabricated hint. The first lesson does not grant berry safety knowledge or reveal coordinates. Death/return keep `actorId`; a different actor cannot load that actor's physiology/knowledge payload. This is one active inhabitant per fixture; a multi-inhabitant combined save must own a keyed collection of physiology/knowledge records around one shared ecology rather than instantiate several independent worlds.

The pure physiology checks execute from the compiled source assembly without a Unity editor: fasting, activity, fat use, sustained surplus, sheltered recovery, delayed protein effects and eventual starvation/dehydration. They do not establish rendered/input acceptance. The planned compiled mortality scenario explicitly starts at a synthetic final starvation boundary, then verifies actual death/save/reload/return/recovery and insight; it must never be described as hours of natural play.

For `food-eden.2`, additionally integrate `FoodPhysiology.cs`; coordinate death and return with the outer world actor controller. Include food deaths in the world's append-only history schema once reviewed. Food does not reset an outer terrain/environment/structure system. Natural seed site 2 is suitable moist soil, while site 3 is unsuitable substrate; those fixture properties must become stable authored habitat records in the combined terrain adapter.
