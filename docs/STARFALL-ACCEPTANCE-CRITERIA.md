# Starfall Living World: Acceptance Criteria & Technical Specification

Document ID: `STARFALL-ACCEPTANCE-CRITERIA-V1`  
Revision: `1.0.0` (Living World Single-Thread Baseline)  
Author: Antigravity Autonomous Pair Programming System  
Target Branch: `origin/codex/starfall-integrated-living-world` -> `dev` ([PR #5](https://github.com/duikindiesee/kooker-starfall/pull/5))

---

## 1. Ground Truth & Architectural Principles

1. **CityLife Evolved into Starfall**:
   - The project is no longer an abstract browser city simulator.
   - CityLife's deterministic numerical foundation (fixed 0.02s tick, $O(1)$ spatial queries, bounded allocations, zero GC spikes) is the computational substrate for Starfall's survival simulation.
2. **Single Unified Unity Thread**:
   - All systems (AG1 food memory, AG2 basket storage, AG3 procedural stone supply, AG4 dry tinder aggregation, AG5 fallen wood, scanner terminal, and stone-age crafting) run in a single executable and scene graph.
   - Zero testbed segregation: the live world is the true runtime.
3. **Stone-Age Progression Priority**:
   - Primitive survival starts with stones as the core technology.
   - Knapping (lithic reduction), stacking (dry masonry), and thermal enclosing (hearth circles) form the prerequisite ladder before advanced shelters or tools.

---

## 2. Formal Acceptance Criteria (AC)

### AC-01: Celestial & Atmospheric System
- **AC-01.1 (Gas Giant Geometry & Scale)**:
  The celestial sphere must feature a blue gas giant whose apparent angular diameter spans at least 25° of the player's field of view. The planet mesh must feature procedural or authored atmospheric bands with cerulean, cyan, and deep azure coloration.
- **AC-01.2 (Limb Darkening & Secondary Glow)**:
  The planet shader or atmospheric shell must exhibit limb brightening/scattering along the sun-facing edge and deep occlusion on the shadow side.
- **AC-01.3 (Galactic Band & Cosmic Dust)**:
  A high-contrast galactic band (dust cloud, stellar nurseries) must span the skybox, remaining visible at all times and providing starlight ambient lighting at night.
- **AC-01.4 (Orbital Satellites)**:
  At least one orbiting satellite/moon must be positioned along the planetary equatorial plane.

### AC-02: Canyon, Mesa & Terrestrial Topography
- **AC-02.1 (Deterministic Mathematical Surface)**:
  The terrain must provide deterministic height $y = f(x, z)$ and surface normal $\vec{n} = g(x, z)$ queries in $O(1)$ time without physics raycast overhead for AI navigation.
- **AC-02.2 (Terracotta & Sand Palette)**:
  Surface materials must adhere strictly to the concept color swatches:
  - Ochre Canyon Walls: RGB `(0.63, 0.29, 0.15)`
  - Golden Desert Sand: RGB `(0.83, 0.61, 0.35)`
  - Weathered Basalt Rocks: RGB `(0.17, 0.16, 0.15)`
- **AC-02.3 (Slope-Governed Locomotion)**:
  Slopes exceeding 45° must be classified non-traversable for the autonomous inhabitant, preventing pathing up sheer mesa cliffs.

### AC-03: Coastal & Submerged Hydrology
- **AC-03.1 (Turquoise Water Transparency)**:
  The water surface must render with high transmission transparency, revealing submerged sand ripples and rocky shelves down to at least 4 metres depth.
- **AC-03.2 (Caustic Projection)**:
  Underwater surfaces must display animated caustic patterns corresponding to surface wave frequencies.
- **AC-03.3 (Rigorous Flood & Wave Clearance)**:
  Every permanent campsite, hearth, and sleeping shelter must maintain a minimum freeboard of 0.75m above the validated maximum wave crest datum ($y_{refuge} - y_{waveMax} \ge 0.75\text{ m}$).

### AC-04: Flora & Ecosystem Generation
- **AC-04.1 (Kokerboom Quiver Tree Architecture)**:
  Quiver trees must exhibit the iconic sculptural dichotomous branching (symmetric Y-forks) with golden-ochre fibrous bark and turquoise terminal leaf rosettes.
- **AC-04.2 (Fruiting Succulents)**:
  Forageable desert succulents must be scattered across canyon terraces and coastal flats, providing harvestable food clusters that track depletion and seasonal regrowth via `PlaceLedger`.
- **AC-04.3 (Fuel & Tinder Dispersal)**:
  Naturally fallen branches and dry brush clumps must spawn deterministically across the terrain within reach of the inhabitant.

### AC-05: First Inhabitant Autonomy & Survival
- **AC-05.1 (Autonomous Needs Loop)**:
  The inhabitant must possess an internal state vector (Energy, Hydration, Satiety, Warmth) that drives goal selection:
  - Hunger $\rightarrow$ Scout & forage succulents.
  - Cold $\rightarrow$ Seek refuge / ignite hearth.
  - Exhaustion $\rightarrow$ Return to refuge mat to sleep.
- **AC-05.2 (Authentic Attire & Stature)**:
  The humanoid inhabitant must be styled according to the approved clothing concept: pale hide tunic, plant-fibre cord belt, rugged cross-stitching, and hide wraps.
- **AC-05.3 (Physical Carriage)**:
  Items carried by the inhabitant (stones, tinder bundles, branches, woven baskets) must be physically attached to grip sockets, with mass influencing movement speed.

### AC-06: Stone-Age Progression & Masonry Architecture
- **AC-06.1 (Shape Archetype Verification)**:
  `StoneMeshGenerator` must generate all 4 hand-carryable shape kinds (`RiverCobble`, `Fieldstone`, `FlatSlab`, `Handstone`) with valid normals, spatial UVs, outward winding, and stable bottom pivots ($y=0$ base).
- **AC-06.2 (Lithic Reduction / Knapping)**:
  Striking a raw river cobble with a handstone atop an anvil stone must consume the cobble and produce functional chipped tools:
  - Sharp Stone Blade (`tool-stone-blade`): Used for cutting fibrous plants and scraping hide.
  - Fire Striker (`tool-fire-striker`): Hard flint pebble used to cast sparks into dry tinder.
- **AC-06.3 (Dry-Stone Hearth Construction)**:
  The inhabitant can gather 6–12 cobbles/fieldstones and construct a `HearthRing`.
  - Effect: Increases hearth burn duration by +50% and reduces wind dissipation.
- **AC-06.4 (Protective Windbreak Walls)**:
  The inhabitant can gather 8+ fieldstones/slabs and stack a dry-stone `WindbreakWall`.
  - Effect: Attenuates incident wind by 60–80% in its downwind shadow zone (1.5m radius).
- **AC-06.5 (Elevated Storage Cairns)**:
  The inhabitant can stack flat slabs into a `StorageCairn` (0.4m elevated stone platform).
  - Effect: Baskets placed on the cairn are protected from ground moisture, dirt, and vermin.
- **AC-06.6 (Structural Permanence & Persistence)**:
  Constructed stone masonry must serialize its placement coordinates, stone IDs, and integrity state in the world save manifest.

### AC-07: Engineering Integrity & Verification
- **AC-07.1 (Zero Regressions)**:
  All 1,513 AG1-AG5 tests, 2,847 Island validation assertions, and 31 environment-zone contract checks must pass on every build.
- **AC-07.2 (Single Batchmode Command)**:
  The entire integrated living world must build cleanly via a single batchmode entrypoint (`-starfallIntegrated` or `IslandBootstrap.Run()`).
- **AC-07.3 (Framerate & Memory Envelope)**:
  Mean framerate must maintain $\ge 60\text{ FPS}$ on baseline hardware, with GC allocations under 10 KB per frame during steady-state simulation.

---

## 3. Verification & Compliance Matrix

| AC Identifier | Verification Method | Automated Evidence Path | Pass Requirement |
|---|---|---|---|
| **AC-01** | Visual inspection & sky shader unit tests | `evidence/local/visual-receipts/sky.json` | Planet visible, 25° FoV, galactic arm rendered |
| **AC-02** | Heightfield sampling & mesh collider checks | `IslandValidation.cs` | Ground height $O(1)$, slope limits $\le 45^\circ$ |
| **AC-03** | Water surface & refuge clearance checks | `RefugeRuntime.ValidateGeometry()` | Freeboard clearance $\ge 0.75\text{ m}$ |
| **AC-04** | Kokerboom hash & forageable place checks | `CaveFoodMemoryChecks.cs` | All succulent locations tracked in PlaceLedger |
| **AC-05** | Autonomy state machine & navigation runs | `NpcMilestoneValidation.cs` | Needs-driven transitions pass |
| **AC-06** | Stone generation, knapping & building checks | `StoneBuildingValidation.cs` | 4 stone shapes, knapping, 3 structures buildable |
| **AC-07** | Integrated master suite execution | `IntegratedCoastalBuild.Run()` | Zero errors, 0 exit code, executable generated |
