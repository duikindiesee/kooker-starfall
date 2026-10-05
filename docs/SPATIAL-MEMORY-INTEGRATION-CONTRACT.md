# Spatial Memory and Planner Integration Contract

**Date:** 16 September 2026  
**Author:** Antigravity worker02 (`gemini-3.8-flash-high`)  
**Branch:** `codex/starfall-agy-02-spatial-memory`  
**Task:** `a958dc2d-6d9a-47a6-8207-9753ca58bfdc` (exclusive claim: `starfall-agy-02`, stream `6ad1193a-8b28-4abe-966c-c7f3c2a3dfae`)  
**Status:** Source-only implementation completed; pending execution and verification by coordinator.

---

## 1. Overview & Architectural Role

Spatial memory presentation introduces a read-only projection (`StarfallMapViewModel`) and user-facing HUD (`StarfallMapHud`) for the inhabitant's existing place ledger (`FoodState.exploredCells` and `FoodState.observedPlaces`).

This milestone preserves all strict boundaries established in `AGENTS.md` and `docs/WORLD-FOUNDATION.md`:
- Pure display never creates, modifies, or infers knowledge.
- The view model consumes only scoped actor-level data (`FoodState.exploredCells`, `FoodState.observedPlaces`, and measured actor position). It has **no access** to global resource registries, procedural terrain heightmaps, or authored coordinate lists.
- Scope mismatches (differing `world`, `generation`, or `actorId`) fail closed immediately, presenting no leaked places or coordinates.

---

## 2. Integration Contract for the Next Planner Lane (Task `7811bff9`)

The next lane addresses bounded needs/day planning. That task is dependency-gated and not part of this implementation. To prevent architectural drift, the interface between spatial memory and future planning is defined as follows:

### 2.1. Scoped Read-Only Remembered Opportunities
- The planner must consult **only** scoped place ledger records (`FoodState.observedPlaces` / `StarfallMapViewModel.GetRememberedBeliefs()`).
- Every remembered opportunity carries an explicit timestamp (`foodTick`), observation kind (`first-seen`, `changed`, `revisit`), and condition when seen (`WasAvailableWhenSeen`).
- **A remembered belief is not live truth.** The planner must treat remembered places strictly as *historical beliefs from prior visits*, not guaranteed available resources. Remembered resources must never be presented as currently available without live verification.

### 2.2. Live Navigation & Perception Revalidation on Arrival
- When a planner selects a remembered opportunity (e.g. travel toward a known berry bush or freshwater spring), navigation guides the inhabitant toward the remembered coordinates.
- **Perception revalidation is mandatory upon arrival.** When within line-of-sight and physical interaction radius, live perception (`NpcAutonomy.Perception` / `IFoodAccess.Inspect`) must revalidate whether the resource is actually present, permitted, and available.
- If conditions have changed (e.g. a berry bush was depleted, or season progressed), the planner cannot force execution based on obsolete memory.

### 2.3. Guarded Outcomes Alone Update Knowledge
- Planning rollouts, hypothetical search trees, candidate evaluations, and prompt formatting are strictly read-only; they **must never mutate the place ledger**.
- Knowledge is updated **only** when an authoritative physical event occurs:
  1. Actor physically steps into a supported 3m cell on walkable ground or refuge floor (`PlaceLedger.Occupy`).
  2. Authoritative line-of-sight perception senses an eligible place target (`PlaceLedger.Observe`), generating an immutable hash-chained observation event.
  3. Action execution yields an authoritative `FoodReceipt` (`FoodModel.Execute`).
- **Clarification:** The existing single-action survival prompt (`StarfallSurvivalThought`) is an immediate reactive step selector operating under current line-of-sight. It is **not** a durable multi-step planner, and no claim of planning capability is made for it.

---

## 3. View-Model & Presentation Invariants

The implementation in `Assets/CityLife/Scripts/StarfallMapViewModel.cs` enforces the following verified properties:

1. **Unknown Entries Absent:**
   Unvisited cells and unobserved places do not exist in the view model. Fog of war is complete; no unobserved world coordinates are disclosed.
2. **Strict Scope Binding & Fail-Closed Isolation:**
   Explicit binding requires matching `world`, `generation`, and `actorId`. Any mismatch results in `IsValid = false`, zero cells, zero events, and a descriptive error status. Reloading into a new world requires explicit rebind and starts with zero inherited places.
3. **Distinct Event Revisions Preserved:**
   Observations form an append-only, hash-chained timeline. Revisiting a place or observing condition changes records distinct dated events (`first-seen` -> `changed` -> `revisit`) with monotonic `foodTick` and local session `brainTick` provenance. Prior events are never overwritten.
4. **Save/Load Round-Trip Stability:**
   Saving to an immutable snapshot and reloading into a fresh model restores identical visible history, hashes, and beliefs.
5. **View Immutability:**
   All exposed collections return arrays of immutable readonly structs (`ExploredCellEntry`, `RememberedPlaceBelief`, `ObservationEventEntry`). Modifying returned arrays or copies cannot alter authoritative state in `FoodState`.
6. **Nonfinite Coordinate Safety:**
   `float.NaN`, `float.PositiveInfinity`, or coordinates outside world bounds (|mag| >= 10000) are handled safely without exceptions, setting `HasValidActorPosition = false` and rendering safe fallback representations.
7. **Non-Mutating Pure Projection:**
   Repeated querying, grid formatting, or inspection runs arbitrary times without changing `exploredCells.Count`, `observedPlaces.Count`, or hash chain validity.

---

## 4. UI & HUD Layout

`StarfallMapHud` (`Assets/CityLife/Scripts/StarfallMapHud.cs`) displays:
- **Location:** Anchored top-right (`x: 1110..1570`, `y: 20..500` at 1600x900 resolution).
- **Non-Obtrusive:** Leaves the inhabitant (center screen) and lower control HUDs (bottom / bottom-left) completely visible.
- **Toggle:** Press `M` to show/hide.
- **Palette:** High-contrast translucent dark slate (`rgba(5, 11, 18, 0.92)`), cyan headers, wheat/amber grid, and clear distinction between `@` (you), `·` (explored), `[ ]` (unknown), `B` (remembered berry), `S` (remembered spring), and `R` (remembered refuge).
- **Screenshot Readiness:** Clean textual labels suitable for actual in-game video and screenshot captures.

---

## 5. Verification Runner

- **Editor Runner:** `CityLife.World.Editor.StarfallMapValidation.Run`
- **Argument:** `-starfallMapEvidence <new_absolute_path>`
- **Execution:** Runs all 116 existing `Starfall.Food.FoodChecks` plus 11 comprehensive `StarfallMapChecks` (sections 1-11), emitting `validation-report.json` and `passed.txt`.
- **Note:** Coordinator executes pinned Unity in the designated test slot. No test results or screenshots are fabricated in this source pass.

---

## 6. Standalone Player Map Revisit Diagnostic

- **Player Flag:** `-starfallMapAcceptance <absolute-empty-evidence-dir>`
- **Role:** Opt-in diagnostic executed in compiled integrated player without LLM dependency.
- **Workflow:**
  1. Respects delivery authority gate (`survivalAuthorityEvidence`) before diagnostic traversal.
  2. Cancels any pending model inferences; labels `LastChoice = "SCRIPTED_DIAGNOSTIC_NOT_MODEL"`, `LastChoiceByModel = false`.
  3. Caches genuinely observed `berry-food` position/approach from live `Observed("berry-food", ...)` perception (not authored registry).
  4. Plans and traverses a normal-frame route departing > 15m away to completely clear perception radius (12m) and LOS, verifying exclusion from `visiblePlaceIds`.
  5. Plans and traverses a normal-frame return route back to the cached berry approach.
  6. Authoritative perception and `RememberCurrentWorld()` append a real `PlaceObservationEvent` with `kind = "revisit"` and valid hash chain.
  7. Persists save, outputs structured summary PASS report (`map-acceptance-summary.json`, `summary.json`, `summary.txt`), sets `MapHud.Expanded = true` for existing frame capture, and holds actor safely idle at terminal state without repeated model calls.

