# Physical Items: Foundation Slice Source Note

Authoritative Task: `4279edae-3f78-4c74-843a-c67489749451`  
Component Namespace: `CityLife.Items`  
Milestone: Living, Physical World (Bounded First Source Slice)  
Status: Component verified in pinned Unity Editor: 58 checks passed; runtime integration and player acceptance pending  

---

## 1. Overview & Architectural Boundaries

This document records the first source slice for physical items in Kooker: Starfall.
Per the project architecture and task requirements:
- Existing action APIs (`CityLife.World.NpcActionApi`), interactables (`CityLife.World.NpcInteractable`), food simulation (`Starfall.Food.FoodModel`), and world save systems remain untouched.
- No second live inventory is created in the runtime.
- The new code under `Assets/CityLife/Items/` implements an authoritative, deterministic contract and state model designed for subsequent integration into Unity action gates and physics loops.

---

## 2. Implemented Source Components

All newly authored source files reside strictly under `Assets/CityLife/Items/`:

### `ItemDefinition.cs`
- **`PhysicalDimensions`**: Represents 3D bounding extents in metres (`width`, `height`, `depth` > 0 and finite, up to 20m), computing volume in m³.
- **`ItemDefinition`**: Declares immutable physical metadata:
  - Stable type identifier (`itemTypeId`).
  - Sensible finite positive mass in kilograms (`massKg` > 0 and finite, up to 10,000 kg).
  - Container attributes (`isContainer`, `maxContainedMassKg`, `maxContainedVolumeM3`, `maxContainedSlots`).
  - Anchoring flag (`isAnchored`) for scenery and immovable fixtures.
  - Placement requirement (`requiresSupportToPlace`).
  - Copy constructor and `Clone()` preventing caller aliasing.
  - **Canonical Rotation Ingress Rule**: `TryCanonicalizeRotation(Quaternion q, out Quaternion normalized)`. Strictly rejects zero, near-zero, non-unit, NaN, and infinity quaternions, canonicalizing valid near-unit quaternions to exact unit length.

### `ItemState.cs`
- **`ItemLocationKind`**: Explicit, mutually exclusive item states:
  - `Free`: Unattached in the world, unheld, uncontained, awaiting or subject to physics.
  - `Carried`: Held by an actor (single authoritative owner `holderActorId`).
  - `Stored`: Contained inside a container item (`containerItemId`).
  - `Placed`: Rested or mounted on a supported surface or socket (`placedSupportId`).
  - `Anchored`: Permanently fixed scenery/terrain fixture (cannot be picked up).
- **`ItemStateSnapshot`**: Thread-safe, deep-cloned state snapshot enforcing single ownership/container invariants, canonical rotation verification, and finite transform vectors.

### `ItemModel.cs`
Authoritative deterministic model managing item registration, hierarchy, and atomic state transitions:
- **Identifier Validation**: Enforces alphanumeric/dot/underscore/dash identifiers (`IsValidId`) up to 80 characters.
- **Action Ingress & Replay Identity**: Evaluates `ItemActionRequest` (`Pickup`, `Drop`, `Place`, `Store`, `Retrieve`).
  - Constructs canonical, culture-invariant semantic request signatures (`BuildRequestSignature`) including action, actor, item, target, position, rotation, and support normal.
  - Ingress ledger (bounded to 512 receipts) returns exact idempotent receipts on identical replay (`duplicate = true`), while rejecting changed payloads under the same `requestId` with `"request-id-conflict"`.
- **Definition & State Ingress Protection**:
  - `RegisterDefinition` and `TryGetDefinition` clone data to ensure callers cannot mutate internal definition tables.
  - `TryGetItem` returns detached clones.
- **Nested Containers & Contained Mass**:
  - `GetItemTotalMassKg` recursively visits containers and contents using a visited set to guarantee every item is counted exactly once (no double-counting).
  - `Store` validates immediate container slot, volume, and mass limits, traverses all ancestor containers verifying `maxContainedMassKg` along the entire chain, and verifies carrier limits.
  - `Retrieve` calculates net mass change: if retrieving from a container already carried by the actor, net mass change is zero, preventing double-counting against carry capacity.
- **Cycle Detection**: `WouldCreateContainmentCycle` traverses container links to reject direct and indirect multi-level containment cycles atomically.
- **Anchored Scenery Protection**: Items marked `isAnchored = true` or `ItemLocationKind.Anchored` cannot be picked up (fails with `"anchored-scenery-cannot-be-picked-up"`).
- **Trusted Authority Boundaries**:
  - `IItemActionAuthority` defines the adapter boundary:
    - `AuthorizeContainerAccess`: Protects carried/stored containers from unauthorized or foreign actor access; defaults to refusal if authority is missing.
    - `AuthorizePlacement`: Validates actual contact, reach, LOS, and support normal; model proposals cannot self-assert placement success.
- **Explicit Drop & Supported Place Intent**:
  - `Drop` sets state to `Free` at the specified intent position without simulating resting physics.
  - `Place` sets state to `Placed` on the validated support surface without claiming physics equilibrium.

### `ItemChecks.cs`
Self-contained, deterministic C# check suite executing purely in-memory with zero file, network, or native engine side effects.

---

## 3. API Entry Points & Check Invocation

### Direct In-Memory Check Invocation
The deterministic check suite can be called directly in an active Unity C# context:
```csharp
// Returns a list of passed check assertions; throws InvalidOperationException on any failure.
List<string> passedChecks = CityLife.Items.ItemChecks.Run();
```

### Checks-Only Unity Editor Runner
For headless or batch execution via the pinned Unity Editor:
```powershell
# Invocation by the coordinator running the pinned Editor in batch mode:
Unity.exe -batchmode -nographics -projectPath <checkout-path> `
  -executeMethod CityLife.Items.Editor.PhysicalItemValidation.Run `
  -physicalItemEvidence <absolute-output-directory> `
  -logFile <log-path>
```
- Entry point: `CityLife.Items.Editor.PhysicalItemValidation.Run()`
- Parameter: `-physicalItemEvidence <output-directory>` (parsed from command line).
- On success: runs `ItemChecks.Run()`, writes every passed check name to `passed.txt`, writes `summary.json` with status `"PASS"` and check count, logs `PHYSICAL_ITEM_CHECKS_PASS <count>`, and exits with code `0`.
- On failure/exception: writes stack trace to `failed.txt`, writes `summary.json` with status `"FAIL"` and error message, logs `PHYSICAL_ITEM_CHECKS_FAIL`, and exits with code `1`.
- Clean execution: no scene construction, no gameplay integration, no player build, no native process launches.

### Model Usage
```csharp
// Instantiate scoped model
var model = new CityLife.Items.ItemModel("starfall-world-01", "generation-01");

// Register item definition (automatically cloned and protected from aliasing)
model.RegisterDefinition(new CityLife.Items.ItemDefinition
{
    itemTypeId = "wood-plank",
    dimensions = new CityLife.Items.PhysicalDimensions(0.2f, 0.05f, 1.2f),
    massKg = 3.5f
});

// Register world item with canonical rotation
model.RegisterItem("plank-01", "wood-plank", CityLife.Items.ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

// Execute an atomic action with a trusted authority adapter
var authority = new CityLife.Items.BasicItemActionAuthority();
var request = new CityLife.Items.ItemActionRequest
{
    requestId = 1,
    action = CityLife.Items.ItemActionKind.Pickup,
    actorId = "inhabitant-01",
    itemId = "plank-01"
};

CityLife.Items.ItemReceipt receipt = model.Execute("starfall-world-01", "generation-01", request, authority);
```

---

## 4. Status of Checks & Verification Boundary

- **Authored & Compiled**: The model contracts, snapshot types, authority interfaces, in-memory check suite, and Editor test runner have been authored and verified for syntax against `UnityEngine.CoreModule` and `UnityEditor`.
- **Independent Editor verification**: On 16 September 2026, coordinator run02 using pinned Unity 6000.6.0f1 exited 0 with `PHYSICAL_ITEM_CHECKS_PASS 58`. The 58-line passed.txt SHA256 is `D6AEDFD3EC6D25B1DCE16FD0230423C89DDA7DBF111AFD2B001D35819A05932F`; all five source hashes were unchanged during validation. Four independent adversarial probes also passed. The earlier fixture failure is retained in run01 evidence.
- **Boundary**: These are component checks in an Editor process. Runtime action wiring, physical settling, persistence and compiled-player acceptance remain open.

---

## 5. Predeclared Physical Budgets & Targets (Requirement Contract)

The following numeric budgets are predeclared for subsequent compiled-player acceptance testing per `docs/PHYSICAL-ITEM-ACCEPTANCE.md`. These are explicit invariant targets, not post-hoc adjusted thresholds:

| Physical Metric | Target Threshold | Validation Scenario |
|---|---|---|
| **Penetration Depth** | $\le 0.01\text{ m}$ (10 mm) | Contact with terrain, mesh colliders, and stacked objects |
| **Settling Time** | $\le 5.0\text{ s}$ | Drop onto flat ground, 15° slopes, and 30° rocky terrain |
| **Settled Linear Velocity** | $\le 0.03\text{ m/s}$ | Measured after settling period has elapsed |
| **Settled Angular Velocity** | $\le 3.0^\circ\text{/s}$ | Measured after settling period has elapsed |
| **Rest Drift** | $\le 0.02\text{ m}$ (20 mm) | 30-second resting observation post-settlement |
| **Contact Scenarios** | Stacked, sloped, and moving contacts | Loose stones, wood logs, planks, and woven baskets |
| **Handling Durability** | 50 handling cycles | Repeated pickup $\rightarrow$ carry $\rightarrow$ drop without desync or duplicate instances |
| **Storage Durability** | 50 storage cycles | Repeated store $\rightarrow$ retrieve without capacity leak or transform explosion |
| **Persistence Stability** | 5 reloads per state | Free, carried, stored, and placed items survive round-trip reload |
| **Physics Performance Budget** | $p_{95} \le 4.0\text{ ms}$ physics frame time | 100 dynamic physical item scenario on pinned reference hardware |
| **Total Frame Time** | $\le 33.3\text{ ms}$ ($\ge 30\text{ fps}$) | Populated scene during physical settling and handling |

---

## 6. Remaining Integration Work (Future Slices)

The foundation model authored in this slice establishes the contract boundary. The following areas represent future integration work:
1. **Rigidbody & Collider Coupling**:
   - Spawning Unity `Rigidbody` and `Collider` components for free and placed items.
   - Releasing items from kinematic hand parenting to dynamic physics, restoring gravity and contact response.
   - Polling velocity to detect physical settlement without synthetic resting teleportation.
2. **Save/Load & In-Flight Action Reconciliation**:
   - Integrating item state serialization with the atomic save pipeline.
   - Reconciling in-flight requests interrupted by save/restart events.
3. **Food Inventory Migration**:
   - Migrating `Starfall.Food.FoodModel`'s scalar counters (`carriedFruit`, `seeds`) into first-class physical item entities (`fruiting-succulent-berry`, `succulent-seed`).
4. **Material Recipe Conservation & Waste**:
   - Crafting recipes (e.g. fallen wood $\rightarrow$ planks) that strictly conserve mass, emitting explicit offcuts and sawdust waste rather than silently creating or destroying material.
5. **Visual & Player Acceptance**:
   - Human inspection in the compiled Windows player, natural grip alignments on character skeletons, cave refuge container placements, and camera review.

