# Cave Food Memory Projection & Integration Contract

**Date:** 16 September 2026  
**Author:** Antigravity worker (`gemini-3.8-flash-high`)  
**Branch:** `codex/starfall-agy-02-spatial-memory`  
**HEAD:** `eddccedc2fd64f6fb8cddea485bc7fa1cb176f66`  
**Task:** `a958dc2d-6d9a-47a6-8207-9753ca58bfdc`  
**Status:** In run `unity-checks-28`, source compiled cleanly under Unity 6000.0.6f1, but test execution failed at exit code 1 due to conjoined assertion on legacy snapshot representation (`CaveFoodMemoryChecks.cs:47`). Checks and documentation repaired; repaired checks authored and pending coordinator execution.

---

## 1. Overview & Architectural Role

`CaveFoodMemory` is a narrow, read-only projection over the inhabitant's existing `FoodState` and `PlaceLedger` observation history (`FoodState.observedPlaces`). It projects dated beliefs specifically for observed fruiting-succulent food sources (such as `berry-food` / `fruiting-succulent` places) to inform future route choice.

### Strict Architectural Boundaries
- **Narrow Read-Only Projection:** Pure query logic only. Zero mutation of `FoodState`, `exploredCells`, or `observedPlaces`.
- **Actual Existing Interfaces:** Built strictly on existing `PlaceLedger.Valid`, `PlaceLedger.Observe`, `FoodModel.Id`, and `FoodState` structures.
- **No World Registry Enumeration:** Does not query or inspect authored world resource registries or global coordinate lists. Fog-of-war is absolute; undiscovered resources remain unknown.
- **No New Persistence:** Introduces no new save files, schema fields, or serialization formats.
- **No Carried Counts Manipulation:** Does not alter inventory, satiety, hydration, or carried fruit counts.
- **No Route Execution:** Does not plan waypoints, move actors, or execute navigation.
- **No Speculative Interfaces:** No speculative storage, fuel, admission interfaces, or fake dispatch authority. AG2 owns the active runtime core.
- **Source-Only Status:** Authored in this single fresh pass. No claim of live wiring or autonomous execution.

---

## 2. Exact API Specification

### 2.1. `CaveFoodBelief` (Struct)
```csharp
namespace Starfall.Food
{
    public readonly struct CaveFoodBelief
    {
        // Identity and evidence data members (public readonly fields):
        public readonly string Id;
        public readonly string ObjectKind;
        public readonly string ObservedType;
        public readonly Vector3 Position;
        public readonly int LastSeenFoodTick;
        public readonly int LastSeenBrainTick;
        public readonly string LastSeenKind; // "first-seen", "changed", "revisit"
        public readonly bool WasAvailableWhenSeen;
        public readonly bool WasPermittedWhenSeen;
        public readonly string ProvenanceHash;
        public readonly int Sequence;
        public readonly string Reason;
        public readonly string StatusText;

        // Computed properties (getter properties / core invariants):
        public bool ReadyToGather => false;
        public bool RequiresRuntimeRevalidation => true;
        public bool IsCandidateForRoute => WasPermittedWhenSeen && WasAvailableWhenSeen;
    }
}
```

#### Invariant: History is Never Ready-to-Gather
- `ReadyToGather` is permanently hardcoded to `false`. Historical observations record what was true at `LastSeenFoodTick`; they do not guarantee presence or readiness at the current tick.
- `RequiresRuntimeRevalidation` is permanently `true`. Live revalidation via line-of-sight and reach (`IFoodAccess.Inspect`) is mandatory before any gather interaction can take place.
- `Reason` provides an explicit, human-readable rationale suitable for route evaluation (e.g. indicating whether the candidate is eligible, deprioritized due to depletion, or disqualified due to lack of permission).

---

## 2.2. `CaveFoodMemory` (Static Class)
```csharp
namespace Starfall.Food
{
    public static class CaveFoodMemory
    {
        public const string FruitingSucculentType = "fruiting-succulent";
        public const string PlaceObjectKind = "Place";

        public static bool TryProject(
            FoodState state,
            string expectedWorld,
            string expectedGeneration,
            string expectedActor,
            out List<CaveFoodBelief> beliefs,
            out string rejectionReason);

        public static List<CaveFoodBelief> Project(
            FoodState state,
            string expectedWorld,
            string expectedGeneration,
            string expectedActor);

        public static bool TryGetLatestBelief(
            FoodState state,
            string expectedWorld,
            string expectedGeneration,
            string expectedActor,
            string id,
            out CaveFoodBelief belief,
            out string rejectionReason);
    }
}
```

---

## 3. Invariants & Fail-Closed Behavior

1. **Scope Validation:**
   The caller must supply expected `world`, `generation`, and `actorId`. If any scope identifier fails syntax validation (`FoodModel.Id`), or does not match `FoodState.world`, `FoodState.generation`, or `FoodState.actorId`, projection fails closed immediately (`TryProject` returns `false`, `Project` throws `ArgumentException`).
2. **Ledger Validation:**
   The ledger is validated via `PlaceLedger.Valid(state)`. Any tampered hash, broken hash chain link (`previousHash`), sequence gap, non-monotonic tick, or non-finite position coordinate causes the projection to fail closed.
3. **Empty / Legacy History (Omitted Fields vs. Explicit-Null Acceptance):**
   - **Pre-PlaceLedger Snapshots:** Older `starfall.food.v1` snapshots omit `exploredCells` and `observedPlaces` fields entirely from serialized JSON.
   - **Parser Representation Diagnostic:** Under Unity's `JsonUtility`, deserializing JSON where fields are omitted may leave fields as `null` or retain default empty collections (`new List<...>`) initialized by field declarations. Rather than asserting a specific parser representation without runtime proof, test diagnostics inspect and report exact field state.
   - **Semantic Acceptance:** Both representations are semantically valid pre-place-ledger histories. In both cases (omitted fields hydrating to null or empty lists, as well as an explicit-null history where fields are explicitly set to `null`), `PlaceLedger.Valid(state)` validates successfully, and `CaveFoodMemory.TryProject` succeeds with zero beliefs. Undiscovered food remains unknown; no coordinates or beliefs are invented.
   - **Restoration Safety:** When restored through `FoodModel.Restore(json, world, generation)`, legacy JSON with omitted fields is validated and safely hydrated to clean empty lists (`exploredCells` and `observedPlaces`), preserving scope, eliminating readiness (`ReadyToGather == false`), and ensuring zero invented knowledge.
4. **Fruiting-Succulent Filtering:**
   Only observation events with `objectKind == "Place"` and `observedType == "fruiting-succulent"` are admitted. Non-food places (e.g. `freshwater-seep`) are excluded.
5. **Latest-Seen Superseding:**
   If a place has been observed multiple times, only the newest observation event (highest tick/sequence) is projected. If a food source was observed available in event #1 but depleted in event #2, event #2 overrides event #1.
6. **Deterministic Order:**
   Returned beliefs are sorted deterministically by `Id` using ordinal string comparison.
7. **Zero Source Mutation:**
   Repeated querying produces identical outputs and leaves `FoodState` JSON serialization bit-for-bit unchanged.

---

## 4. Later Integration Requirements for Route Choice & Runtime

Future autonomous planning or route selection lanes must adhere to the following contracts:

### 4.1. Missing Source / Route Outcomes (Cannot Infer Absence from History)
- A historical belief records that a source was present and in a given state at tick $T$.
- If an inhabitant selects a route toward a remembered fruiting succulent and finds it missing upon arrival (e.g. depleted by wildlife, despawned, or destroyed), **absence cannot be inferred from history alone**.
- History was not wrong; conditions changed.
- The outcome of the arrival must be handled by live perception: authoritative line-of-sight perception must record a new `PlaceObservationEvent` (`changed` with `available = false`), appending to the hash chain in `PlaceLedger`.

### 4.2. Distant Placement & Fog-of-War
- Coordinates in a `CaveFoodBelief` represent the position where the resource was previously observed.
- Distance to the source does not confer access or perception.
- Route planning can use `belief.Position` to navigate toward the source, but no interaction is permitted at a distance.

### 4.3. Real Access Revalidation Mandatory Upon Arrival
- When the actor physically arrives within interaction range, live perception (`IFoodAccess.Inspect`) must revalidate:
  1. `visible`: Target must be in unobstructed line-of-sight.
  2. `inReach`: Target must be within physical reach distance.
  3. `permitted`: Target must have valid interaction permissions.
- Even if `WasAvailableWhenSeen == true`, gathering directly from memory without live revalidation is strictly forbidden.

---

## 5. Authored Test Suite (`CaveFoodMemoryChecks`)

Authored in `Assets/CityLife/Food/CaveFoodMemoryChecks.cs` following the `FoodChecks.Run` pattern (`List<string> Run(string folder)` with exception assertions):

| Section | Target Invariant | Coverage Description |
| :--- | :--- | :--- |
| **1. No History / Legacy** | Unknown food yields empty beliefs | Verifies fresh `FoodState` (empty history), genuine omitted-field legacy JSON (verifying omission, parser diagnostics, and ledger validation), separate explicit-null history (`null` lists), and restoration via `FoodModel.Restore` yielding 0 beliefs without invented coordinates or readiness. |
| **2. Scope Mismatch** | Fail-closed scope gating | Verifies mismatched `world`, `generation`, `actor`, invalid ID format, and null state fail closed and throw `ArgumentException`. |
| **3. Tampered Ledger** | Fail-closed integrity | Verifies tampered hashes, silent payload modifications, corrupted sequences, broken hash links, and non-finite positions fail closed. |
| **4. Latest Superseding** | Depletion/permission overrides | Verifies newest events override older availability, filters non-fruiting-succulents, and preserves deterministic ID ordering. |
| **5. Deserialization & Readiness** | Historical readiness rejection | Verifies save/load reload preserves dated beliefs, and confirms `ReadyToGather == false` and `RequiresRuntimeRevalidation == true` across restarts. |
| **6. Zero Source Mutation** | Immutability guarantee | Verifies `FoodState` JSON serialization before and after projection is bit-for-bit identical with unchanged counts. |

*Note: In independent run `unity-checks-28`, source compiled cleanly under Unity 6000.0.6f1, but test execution failed at exit code 1 at `CaveFoodMemoryChecks.cs:47` (conjoined null-lists assertion). The test suite has been repaired with decoupled diagnostics, verified field omission, explicit-null preservation, and `FoodModel.Restore` validation. Repaired checks have not yet been executed; awaiting coordinator execution.*
