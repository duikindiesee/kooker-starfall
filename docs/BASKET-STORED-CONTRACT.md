# Basket Stored Item Persistence Contract

**Date**: 17 September 2026  
**Status**: Authoring complete (Production Pass 1 / Slice A; checks authored, not run; pending Codex/coordinator validation)  
**Schema Version**: `starfall.physical-save.v2` (with backward-compatible support for `starfall.physical-save.v1`)  
**Scope**: Durable persistence, stable slot allocation, prepared transitions, and atomic transactional execution of the physical stored item graph.

---

## 1. Scope and Design Boundary

This document defines the contract for serializing, storing, validating, and restoring containment hierarchies (`ItemLocationKind.Stored`) alongside existing `Free` and `Carried` physical items, including persistent container slots, two-phase prepared transitions, and transactional adapter execution.

### Bounded Scope
- **Included**: Durable serialization of item containment (`containerItemId`) and persistent stable container slots (`containerSlot`), recursive mass calculation, nested container hierarchy validation, two-phase prepared transitions (`TryPrepareTransition` + `TryCommitTransition`), revision-bound single-use tokens, real transactional adapter execution in `NpcActionApi` and `NpcAutonomy` (`Store` / `Retrieve`), exact atomic rollback of runtime physical state, and backward compatibility with in-memory migration of v1 saves.
- **Excluded**: Fuel consumption, basket 3D assets/materials, grid slot UI, inventory windowing, food consumption planner, and live player input bindings (reserved for Slice B).

---

## 2. Exact APIs and Data Schema

### 2.1 `SavedItemRecord`
The durable record represents an individual physical item. Unity engine references (`GameObject`, `Transform`, `Rigidbody`) are never serialized.

```csharp
[Serializable]
public sealed class SavedItemRecord
{
    public string itemId;
    public string itemTypeId;
    public ItemLocationKind location;          // Free (0), Carried (1), Stored (2)
    public string holderActorId;               // Non-empty for Carried, null/empty for Free and Stored
    public string containerItemId;            // Non-empty for Stored, null/empty for Free and Carried
    public int containerSlot = -1;             // Stable 0-indexed slot for Stored, -1 for Free and Carried
    public float massKg;                      // Dry mass matching live ItemDefinition
    public PhysicalDimensions dimensions;      // Bounding dimensions matching live ItemDefinition
    public Vector3 position;                  // World position (Free) or local/rest pose
    public Quaternion rotation;                // Normalized canonical rotation
    public long lastUpdatedTick;              // Monotonic tick <= snapshot tick
}
```

### 2.2 `PhysicalSavePayload` and `PhysicalSaveEnvelope`
- **Current Envelope Schema Identifier**: `"starfall.physical-save.v2"`
- **Legacy Envelope Schema Identifier**: `"starfall.physical-save.v1"`
- **Integrity**: SHA-256 hash calculated over the exact raw JSON of `PhysicalSavePayload` prior to in-memory migration.
- **Payload Scope**: Scoped to specific `worldId`, `generationId`, and `actorId`.

```csharp
[Serializable]
public sealed class PhysicalSavePayload
{
    public string worldId;
    public string generationId;
    public string actorId;
    public long tick;
    public List<SavedItemRecord> items = new List<SavedItemRecord>();
    public List<SavedReceiptRecord> receipts = new List<SavedReceiptRecord>();
}
```

### 2.3 Authoritative Snapshot & Persistence Methods

- `ItemPersistence.CreateSnapshot(ItemModel model, string actorId)`:
  Collects all authoritative item snapshots from `model.GetAllItemSnapshots()`.
  Serializes items with `location` in `Free`, `Carried`, or `Stored`.
  Stores exact `snap.containerItemId` and `snap.containerSlot` for stored items.
  Placed and Anchored items remain outside this persistence slice and are not serialized.
- `ItemPersistence.SaveAtomic(string path, PhysicalSavePayload payload)`:
  Validates scope IDs, bounds, writes to temp file, flushes to disk (`fsync`), and atomically replaces target file with `.bak` fallback.
- `ItemPersistence.TryLoad(string path, string expectedWorld, string expectedGen, string expectedActor, ItemModel liveModel, out PhysicalSavePayload payload)`:
  Verifies envelope schema (`v2` or legacy `v1`), SHA-256 integrity against raw unmigrated payload text, world/generation/actor scope, canonicalizes quaternion rotations, and performs complete preflight validation:
  - If envelope schema is `starfall.physical-save.v1` and legacy unassigned records (`containerSlot < 0`) are present on Stored items, performs deterministic in-memory migration: direct stored children per container are sorted by `itemId` (`StringComparer.Ordinal`) and assigned slots `0..N-1`. The disk file remains strictly untouched until an explicit new save.
  - If envelope schema is `starfall.physical-save.v2` and any Stored item has `containerSlot < 0`, `TryLoad` fails closed immediately, rejecting the malformed payload without modifying memory or disk.
  - When `liveModel` is non-null: executes full definition-backed validation (`liveModel.CanRestoreSnapshot`), strictly validating definitions, containment hierarchy, strict volume/mass capacities, slot occupancy without gaps or collisions, actor carry limits, and receipts.
  - When `liveModel` is null: permits legacy payloads consisting solely of `Free` and `Carried` records. If any record has `location == ItemLocationKind.Stored`, `TryLoad` fails closed immediately because containment requires authoritative definition-backed validation; structural-only acceptance is not permitted.
- `ItemModel.CanRestoreSnapshot(PhysicalSavePayload payload)`:
  Pure query preflight validation. Returns `true` if and only if all graph, definition, strict capacity, stable slot range `[0, def.maxContainedSlots)`, duplicate slot occupancy absence, carry limit, and receipt invariants hold. Mutates zero state.
- `ItemModel.RestoreSnapshot(PhysicalSavePayload payload)`:
  Invokes `CanRestoreSnapshot(payload)`. If valid, atomically replaces `items`, `receipts`, `HighestReceiptRequestId`, advances `Tick`, assigns stable slots (with fallback migration if all direct children are unassigned), and increments `Revision`. If invalid, returns `false` leaving live model state completely unchanged.

---

## 3. Schema Policy, Migration, and Prepared Transitions

### 3.1 Legacy v1 Compatibility, In-Memory Migration, and Strict v2 On-Disk Schema Policy
- Older save files created under `starfall.physical-save.v1` did not contain the `containerSlot` field in their JSON payloads.
- `ItemPersistence.TryLoad` accepts both `"starfall.physical-save.v2"` and `"starfall.physical-save.v1"` envelopes.
- SHA-256 checksums are strictly computed and verified against the original on-disk payload JSON string before any in-memory migration occurs.
- On-disk migration is strictly restricted to envelope schema `starfall.physical-save.v1`: direct children of each container with `containerSlot < 0` are sorted by `itemId` using `StringComparer.Ordinal` and assigned sequential slots `0..N-1` in memory.
- The on-disk file is left strictly untouched until an explicit `SaveAtomic` operation writes a new `v2` payload.
- On-disk saves with schema `starfall.physical-save.v2` containing any stored item with `containerSlot < 0` are rejected immediately as malformed by `TryLoad` without modifying memory or disk.
- In `ItemModel.CanRestoreSnapshot`, legacy unassigned records (`containerSlot == -1` for stored items) are allowed as an in-memory compatibility boundary only if all direct children for that container are unassigned; mixed or partially assigned slots are rejected as malformed.

### 3.2 Two-Phase Prepared Transitions
To enable atomic physical interaction without split-brain state or partial failures, `ItemModel` implements an authoritative prepared transition pattern:
1. `TryPrepareTransition(request, authority, out token, out rejectionCode)`:
   Validates authority permissions, hands-full, item availability, containment cycles, mass, volume, and container slot capacity. Allocates the first vacant container slot (`AllocateFirstVacantSlot`) and binds the resulting `PreparedItemTransitionToken` to the current model `Revision`.
2. `TryCommitTransition(token, out receipt)`:
   Validates that the token has not been used or cancelled, and that `token.BoundRevision == this.Revision`. If valid, mutates the authoritative model (`location = Stored`, `containerSlot = token.AssignedSlot`), records the completion receipt, invalidates active tokens, and increments `Revision`.
3. `CancelPreparedTransition(token)` / Mutation Invalidation:
   Tokens are single-use. Any mutation to the model (including other committed actions, tick advancements, or save restorations) bumps `Revision`, instantly invalidating all outstanding tokens.
4. `RecordRejectionReceipt(request, code)`:
   Authoritatively records rejection receipts in the receipt ledger and increments `Revision`.

### 3.3 Transactional Adapter Boundary (`NpcActionApi` & `NpcAutonomy`)
- `NpcActionKind` extends with `Store = 3` and `Retrieve = 4`.
- `NpcActionApi.Execute(int requestId, NpcActionKind action, string itemId, string containerId)` executes real transactional transitions:
  - Immediate duplicate replay check against `PhysicalModel.TryGetReceipt` prior to spatial, reach, LOS, or component checks. Duplicate completions and `request-id-conflict` are returned directly from the durable model ledger.
  - Required hand checks on Retrieve: hand transform must be non-null and belong to the actor's scene, failing closed with `item-unavailable` or `world-mismatch`.
  - Required hand attachment checks on Store: verifies `heldPhys.IsCarried && heldPhys.CarriedHand == hand` and registered held object matches `Held`.
  - Complete permission validation: checks `containerInteractable.Permission`, container root interactable permission, and `itemInteractable.Permission`, failing closed with `permission-denied` on permission refusal.
  - Valid scoped guard refusals (such as `cargo-ownership-mismatch`, `permission-denied`, `item-unavailable`, `world-mismatch`) are recorded in the authoritative ledger via `PhysicalModel.RecordRejectionReceipt`, ensuring identical denial replay across restarts.
  - Reach and line-of-sight checks navigate to the container's accessible root interactable (via `GetAccessibleRootInteractable`), enabling interaction with nested stored containers without requiring exposed colliders on hidden items.
  - Prepares model transition via `TryPrepareTransition`.
  - Captures exact runtime state of affected bodies via `PhysicalItem.CaptureRuntimeState()`.
  - Applies runtime physical changes (`ApplyStored` or `AttachToHand`).
  - Commits via `TryCommitTransition`. On any failure, exception, or test failpoint (`FailAfterPhysicalApplyForTesting`), executes exact atomic rollback via `PhysicalItem.RestoreRuntimeState()`, restoring hand attachment, follower kinematic states, zeroed follower velocities, and leaving unrelated bodies completely undisturbed.
- Matching overload `ExecutePlayerAction(NpcActionKind kind, string itemId, string containerId)` in `NpcAutonomy` provides player and autonomous execution with monotonic request ID allocation.

### 3.4 Placed and Anchored State Boundaries
- Items in `Placed` (socketed/rested on scenery) and `Anchored` (immovable world structures) locations remain explicitly outside the physical inventory/persistence scope.
- They are not serialized by `CreateSnapshot` and are rejected if present in loaded payloads. They are never silently reclassified as `Free` or `Stored`.

---

## 4. Graph Validation and Invariants

Before any mutation occurs in `RestoreSnapshot`, the complete containment graph is validated:

1. **Unique Identities**:
   Every `itemId` must be valid (`ItemModel.IsValidId`) and unique across the payload. Duplicate IDs cause immediate refusal.
2. **Definition Conformance and Measurement Tolerance**:
   Every `itemTypeId` must exist in live model definitions. Mass and dimensions must be finite, positive, and match definitions within `<= 0.0001f` kg / m measurement tolerance.
3. **Exclusive Ownership Fields**:
   - `Free`: `holderActorId == null/empty` AND `containerItemId == null/empty` AND `containerSlot == -1`.
   - `Carried`: `holderActorId == payload.actorId` AND `containerItemId == null/empty` AND `containerSlot == -1`.
   - `Stored`: `holderActorId == null/empty` AND `containerItemId != null/empty` AND `containerItemId != itemId` AND `containerSlot >= 0` (or `-1` for unmigrated legacy).
4. **Parent Validity**:
   For any `Stored` item:
   - The referenced `containerItemId` must exist in `payload.items`.
   - The parent item's definition must have `isContainer == true`.
5. **Cycle Detection (Acyclic Directed Graph)**:
   Traversing the ancestor chain (`containerItemId -> parent.containerItemId -> ...`) must never encounter an item already visited. Any containment loop causes immediate refusal.
6. **Immediate Container Capacity & Slot Occupancy (Strict Limits)**:
   - **Slots**: Direct child count <= `def.maxContainedSlots`.
   - **Slot Occupancy**: Each assigned `containerSlot` must be in `[0, def.maxContainedSlots)` and no two stored items within the same container may occupy the same slot (duplicate slot occupancy rejected).
   - **Volume**: Sum of direct children volumes <= `def.maxContainedVolumeM3` (strict `>` rejects).
   - **Mass**: Total recursive contained mass <= `def.maxContainedMassKg` (strict `>` rejects).
7. **Ancestor Mass Capacity (Strict Limits)**:
   Enforced across all ancestors in nested hierarchies (`recursiveContainedMass > def.maxContainedMassKg` rejects).
8. **Actor Carry Limits with Descendants (Strict Limits)**:
   - Directly carried items count <= `limits.maxCarriedItems`.
   - Total carried mass = dry mass of carried item(s) + all recursive descendants <= `limits.maxCarryMassKg`.
9. **Atomic Failure**:
   If any validation rule fails, `RestoreSnapshot` returns `false` without modifying live model state.

### 5.1 Architecture and Explicit Runtime Bindings
To replace the single-item binder limitation while avoiding implicit scene-wide searches or guessed identities, runtime restoration requires an explicit, complete stable-ID runtime binding set:

```csharp
[Serializable]
public sealed class PhysicalItemRuntimeBinding
{
    public string itemId;
    public PhysicalItem physicalItem;
    public NpcInteractable interactable;

    public PhysicalItemRuntimeBinding() { }
    public PhysicalItemRuntimeBinding(PhysicalItem physicalItem, NpcInteractable interactable = null);
    public PhysicalItemRuntimeBinding(string itemId, PhysicalItem physicalItem, NpcInteractable interactable = null);
}

public static bool RestoreRuntime(
    PhysicalSavePayload payload,
    ItemModel model,
    NpcActionApi actions,
    IEnumerable<PhysicalItemRuntimeBinding> bindings)
```

Convenience overload accepting `IEnumerable<PhysicalItem>` constructs `PhysicalItemRuntimeBinding` elements automatically from attached `NpcInteractable` components without implicit scene lookups.

### 5.2 Atomic Preflight Validation
Before mutating ANY model state, held state, transform, renderer, collider, or rigidbody, the complete payload/model/live binding graph is preflighted:
1. **Scope and Authority**:
   - `actions.PhysicalModel == model`
   - `actions.WorldId == payload.worldId == model.WorldId`
   - `actions.AgentId == payload.actorId`
   - `model.GenerationId == payload.generationId`
   - `actions.ActorTransform` valid and in active scene.
2. **Complete Live-Item Set and Empty Model Contract**:
   - Every binding has a non-empty, valid `itemId` matching `physicalItem.itemId` and `interactable.StableId` (if present).
   - Duplicate binding IDs, duplicate GameObjects, duplicate `PhysicalItem` instances, duplicate `NpcInteractable` instances, duplicate/cross-wired `Rigidbody` instances, or duplicate/cross-wired `Collider` instances cause immediate atomic refusal.
   - Binding count must exactly match `payload.items.Count`. Missing bindings or extra bindings cause immediate refusal.
   - If live model is non-empty (`model.ItemCount > 0`), empty replacement (`payload.items.Count == 0`) is rejected before mutation to prevent silent orphan bodies. Every live item currently in `model` must be covered by `bindingMap` and `payload.items` with consistent item ID and `itemTypeId`.
   - Empty model contract: an empty live model (`model.ItemCount == 0`) with empty payload (`payload.items.Count == 0`) and empty bindings is accepted as a valid empty-graph restoration.
3. **Single Carried Root Invariant**:
   - Existing `NpcActionApi` exposes exactly one `Held` interactable. Payloads with multiple carried roots are rejected atomically before mutation.
   - If a carried root exists, `actions.HandTransform` must exist in the actor's scene, `holderActorId` must match `actions.AgentId`, and an `NpcInteractable` is strictly required.
   - If `actions.Held` is non-null prior to restoration, it must belong to the registered binding set; foreign/unrelated held objects cause immediate refusal.
4. **Physical Component & Definition Conformance**:
   - Every physical item must be in the actor's scene and bound to `model`, `payload.worldId`, and `model.GenerationId`.
   - `Body` and `ItemCollider` must belong to the item's own `GameObject` (`Body.gameObject == gameObject`, `ItemCollider.gameObject == gameObject`, `GetComponent<Rigidbody>() == Body`, single collider matching `ItemCollider`).
   - Rigidbodies and colliders must be unique across all bindings (rejects cross-wired equal-mass bodies and colliders).
   - Dimensions, dry mass, and `Body.mass` must match live `ItemDefinition` within `<= 0.0001f` kg / m.
   - World poses (`transform.position`, `transform.rotation`) and payload record poses must be finite.
   - Each `lossyScale` axis is explicitly verified with `ItemDefinition.Finite` prior to epsilon comparisons in both `PhysicalItem.IsValid` and the multi-item restore binder (with tests covering helper-level nonfinite checks and engine transform assignment runtime boundaries).
   - Exactly one supported non-compound collider (Box, Sphere, Capsule, convex Mesh) with positive extents.
   - Non-unit world scale (`lossyScale != Vector3.one` within 0.001f) or nonfinite scale causes immediate refusal.
   - Coherent physical flags across Free (`!isKinematic`, `useGravity`, `!isTrigger`, collider enabled), Carried (`isKinematic`, `!useGravity`, `isTrigger`, collider enabled), Stored (`isKinematic`, `!useGravity`, collider disabled), and Anchored states.
5. **Authoritative Model Preflight**:
   - `model.CanRestoreSnapshot(payload)` runs full acyclic containment verification, strict slot capacity, strict volume capacity, strict ancestor mass capacity, actor carry limits with nested descendants, and receipt integrity.
6. **Deterministic RestoreHeld Preflight**:
   - Every requirement of `actions.RestoreHeld` is validated prior to mutation, guaranteeing that the final commit cannot partially fail.
7. **Complete Pre-Refusal State Equality**:
   - Refusal checks verify that pre-refusal state is preserved exactly across all model snapshots, receipt records, action receipts (comparing every field: `requestId`, `worldId`, `generationId`, `actorId`, `action`, `itemId`, `targetId`, `success`, `duplicate`, `code`, and `totalCarriedMassKg <= 0.0001f`), request signatures, request keys, interactables, transform poses, finite lossy scale, components, velocities, renderers, and tracked extra/foreign scene objects.

### 5.3 Stored State, Visual/Physics Transitions, and Renderer Restoration
- **Stored State**: Descendant items in `ItemLocationKind.Stored` state have their renderers disabled (`Renderer.enabled = false`), colliders disabled (`Collider.enabled = false`), and rigidbodies made kinematic (`isKinematic = true`, `useGravity = false`, zero velocities). They are parented to their container transform in topological order without spawning duplicate bodies.
- **Owned Renderer State Preservation**: Renderer management traverses only owned renderers of the specific `PhysicalItem` and stops at child `PhysicalItem` boundaries, preventing interference with nested child items.
- **Decorative Renderers & Stored-Exit Survival**: Owned disabled renderers (such as decorative child meshes with `enabled == false`) have their initial enabled states captured before Stored transition and restored on exit. Actual Stored-exit transitions (transitioning an item with a disabled decorative renderer into Stored, repeated restores while Stored, and transitioning out to Free, Carried, and back to Stored) verify that enabled and disabled renderer states survive, IDs/bodies remain unique, membership and mass remain correct. In addition, ordinary parent `ReleaseToPhysics` and `AttachToHand` operations outside the restore binder verify that nested stored renderers remain hidden (`enabled == false`).
- **Cold Bound Scene Expectations**: Cold bound scenes require explicit pre-instantiated scene bindings matching model items; non-stored items reflect authored visual states, stored items begin with disabled colliders/renderers and kinematic bodies, and duplicate visuals or bodies are prohibited.
- **Already-Stored Acceptance**: `PhysicalItem.IsValid()` and preflight accept items in valid `IsStored` state (disabled collider, kinematic body). Repeated restores on already-stored graphs succeed idempotently without state drift or mass accumulation.
- **Transitioning Out (Stored -> Free / Carried)**:
  - `ReleaseToPhysics` restores enabled solid collider (`isTrigger = false`, `enabled = true`), dynamic physics (`isKinematic = false`, `useGravity = true`), unparents to root level with unit scale, restores owned renderers to their original states, and resets stored flags.
  - `AttachToHand` restores enabled trigger collider (`isTrigger = true`, `enabled = true`), attaches kinematic follower to hand, restores owned renderers to their original states, and resets stored flags.
- **Transitioning In (Free / Carried -> Stored)**: `ApplyStored` records active renderer states, disables renderers, disables colliders, zeroes velocities, sets kinematic physics, and parents to the container transform.

### 5.4 Legacy Compatibility and Backward Safety
- The existing single-item 5-parameter signature `RestoreRuntime(payload, model, actions, demonstrationItem, demonstrationInteractable)` is preserved with identical semantics and refusal guards. It requires exactly 1 item in `payload` in `Free` or `Carried` state, and fails closed atomically if `Stored` or multiple items are present.
- Legacy v1 save payloads without `containerItemId` deserialize cleanly, validate via null-model `TryLoad`, and restore correctly.

---

## 6. Resolved Foundation & Remaining Work Gaps

### 6.1 Resolved in PASS60: Multi-Instance Physical Bootstrap & Cold Restore
The cold-start instantiation prerequisite is now integrated and validated:
1. **Authoritative Catalog (`PhysicalItemCatalog.cs`)**: Immutable authoritative item definitions configured and populated to the live model; untrusted save payloads cannot fabricate definitions, capacities, or physical parameters.
2. **Atomic Staged Instantiation**: Candidate physical GameObjects and bindings are staged inactive (`SetActive(false)`), validated for definition conformance, component validity, scene boundaries, and hand availability, and disposed on failure without mutating baseline scene objects, model, registry, or disk save bytes.
3. **Commit & Duplicate Prevention**: On successful staging validation, baseline demo items are cleaned up, candidate objects activated, and stable IDs committed, preventing duplicate demo items or carried/stored mass accumulation.
4. **Registry Lifecycle Seam**: `PhysicalItemBootstrap.AllInteractables` exposes all live physical interactables, and `NpcAutonomy` enumerates them in `Start`, `AllInteractables`, and `ResetState` before `NpcActionApi` copies the registry.
5. **FixedUpdate Dynamic Synchronization**: All eligible Free items bound to the live model are continuously synchronized via `NpcActionApi.SyncFreeTransform`.
6. **Backward Compatibility**: Old single-demo saves (both Free and Carried) restore cleanly via the validated list binder.

### 6.2 Resolved in Slice B: Player Integration, Container Inspection Panel & Controls Isolation
1. **`WovenBasketVisual` Procedural Mesh**: Generates parametric open woven basket adhering to <= 0.3x0.3x0.3m catalog dimensions, zero colliders/duplicate components on visual child, neutral scale, and explicit mesh disposal on Destroy.
2. **Starter Layout Bootstrap**: `PhysicalItemBootstrap` supports opt-in starter layout (`OptInStarterLayout`) instantiating the 6-item canyon starter set (`canyon-basket-01`, rubies, chisels).
3. **Safe Default Save Path & Rejected Provenance**: `GetDefaultSavePath()` roots saves in persistent storage (`Saves/physical-world-{worldId}.json`). On load failure of corrupt saves, provenance is preserved (`SourceSaveRejected`, `RejectedSourcePath`), `SaveCurrentState` refuses to overwrite rejected source file, and recovery saves write to distinct timestamped paths without touching original bytes.
4. **Player Controls & Panel Isolation**:
   - `PhysicalContainerPanel` provides 4-slot container inspection and transactional Store/Retrieve actions.
   - Same-frame input gating: opening the panel suppresses look input and releases the cursor; closing the panel restores cursor lock.
   - `Esc` closes the panel first before options menu. `Tab` closes panel and toggles possession.
   - Closed-panel target disambiguation: `CurrentPickupTarget` is completely decoupled from `ContainerPanel.SelectedTarget`. Closing the panel with a basket nearby does NOT hijack `G`; the player can freely cycle nearby items (`T`) and pick loose items (e.g. ruby) without picking up the basket.

### 6.3 Remaining Work and Deferred Gaps
The following capabilities remain deferred for subsequent milestones:
1. **Advanced Container Visuals & Spatial Layout**:
   - Visible nested 3D socket/item transforms in open containers.
   - Full 2D slot grid matrices with variable item slot footprints.
2. **World Generation & Placement**:
   - Cave scene proc-gen placement of storage containers.
   - Multi-container transfer gestures.

---

## 7. Authored Checks Summary

### 7.1 Pure C# Persistence Checks (`BasketPersistenceChecks.cs`, 203 checks)
Deterministic pure C# checks validate in-memory models and serialized payloads:
1. `nested stored roundtrip`: Chest -> Basket -> Pouch -> Gem, plus Chisel in Basket. Verifies exact IDs, locations, container IDs, ancestor chains, recursive total masses, and contained masses before and after save/restore within `<= 0.0001f` kg tolerance.
2. `null-model TryLoad boundary and carry overcapacity`: Verifies that `TryLoad` with null `liveModel` fails closed for valid nested Stored payloads and genuinely carried overweight payloads with nested stored descendants. Legacy Free/Carried payloads load successfully with null `liveModel`.
3. `repeated five restores`: 5 sequential restore cycles verifying idempotency, mass stability (<= 0.0001f kg), and receipt ledger consistency without state drift.
4. `graph refusal invariants`: Duplicate item IDs, dangling/missing parent containers, self-containment, direct/indirect containment cycles, non-container parents, immediate slot overflow, immediate volume overflow, immediate mass overflow, ancestor mass overflow, actor carry overflow, and exclusive ownership field violations.
5. `legacy Free/Carried v1 compatibility`: Deserializes v1 JSON without `containerItemId`, verifying `null` mapping, successful restore, operational transitions (Pickup, Drop), and null-model `TryLoad` load success.
6. `replay receipts after restore`: Verifies idempotent receipt duplicate return, conflicting request refusal, monotonic sequence allocation above restored receipts, and execution of subsequent actions.

### 7.2 Additive Runtime Integration Checks (`PhysicalItemRuntimeRestoreChecks.cs`, invoked by `PhysicalItemRuntimeChecks.Run`)
Executed within an isolated local physics scene with real `GameObject`, `PhysicalItem`, `Rigidbody`, `BoxCollider`, `MeshRenderer`, and `NpcInteractable` components:
1. `multi-item-restore-mixed-graph-success`: Initial atomic restoration of mixed graph (carried basket holding pouch holding gem; free chest holding chisel; free rock).
2. `multi-item-restore-carried-root-state`: Verifies hand attachment, `NpcActionApi.Held` alignment, trigger collider, unit lossyScale, and active renderers.
3. `multi-item-restore-disabled-decorative-renderer-initial-state`: Verifies disabled decorative renderer child remains disabled on carried root.
4. `multi-item-restore-nested-stored-state`: Verifies parent-child scene hierarchy, kinematic non-dynamic rigidbodies, disabled colliders, disabled renderers, and zero velocities.
5. `multi-item-restore-free-root-state`: Verifies ground positions, solid enabled colliders, dynamic rigidbodies, and active renderers.
6. `multi-item-restore-descendant-mass-exact`: Verifies recursive carried mass (2.6000kg), subtree mass, and dry body mass within `<= 0.0001f` kg tolerance.
7. `multi-item-restore-repeated-five-cycles-stable`: 5 consecutive runtime restore cycles verifying idempotency, scale preservation, decorative renderer stability, no duplicate bodies spawned, and zero mass drift.
8. `multi-item-restore-transition-stored-to-free`: Transitions gem from nested stored state to free on floor, restoring solid collider, dynamic physics, active renderers, and unparented unit scale.
9. `multi-item-restore-transition-free-to-carried`: Transitions gem from free to carried in hand and basket to free on floor, updating hand ownership and carried mass.
10. `multi-item-restore-parent-drop-preserves-child-and-decorative-renderers`: Verifies releasing parent basket to physics does not enable nested pouch renderer or disabled decorative renderer.
11. `multi-item-restore-transition-carried-to-stored`: Transitions gem back into nested stored pouch, restoring hidden renderers and non-colliding physics.
12. `multi-item-restore-repeated-transitions-preserve-decorative-renderer`: Verifies repeated transitions across Free, Carried, and Stored preserve original disabled state of decorative renderer.
13. `multi-item-restore-transition-basket-with-deco-to-stored`: Transitions basket with disabled decorative renderer into Stored chest, verifying both main and decorative renderers disabled.
14. `multi-item-restore-stored-with-deco-repeated-restores-stable`: Repeated restore cycles while Stored keep both renderers disabled and preserve container total mass.
15. `multi-item-restore-stored-exit-to-free-preserves-decorative-and-main-renderers`: Transitions Stored basket to Free on floor; verifies enabled main renderer and disabled decorative renderer survive, collider/physics restored, masses correct.
16. `multi-item-restore-stored-exit-to-carried-preserves-decorative-and-main-renderers`: Transitions that basket to Carried; verifies enabled main renderer and disabled decorative renderer survive, hand attachment and carried mass correct.
17. `multi-item-restore-transition-back-to-stored-hides-all-renderers`: Transitions basket back to Stored; verifies all renderers disabled.
18. `multi-item-restore-reset-to-carried-root`: Restores baseline payload; verifies carried root and nested stored renderer states.
19. `multi-item-parent-release-to-physics-outside-binder-preserves-hidden-nested-renderers`: Ordinary parent `ReleaseToPhysics` outside restore binder verifies nested stored pouch and gem renderers remain hidden.
20. `multi-item-parent-attach-to-hand-outside-binder-preserves-hidden-nested-renderers`: Ordinary parent `AttachToHand` outside restore binder verifies nested stored pouch and gem renderers remain hidden.
21. `multi-item-restore-uniqueness-invariants`: Verifies uniqueness of IDs, GameObjects, Rigidbodies, Colliders, component ownership, single-collider rule, finite scale, and unit scale.
22. `multi-item-restore-refusal-missing-binding-preserves-state`: Atomic refusal when binding set is missing an item; verifies full pre-refusal state equality.
23. `multi-item-restore-refusal-extra-binding-preserves-state`: Atomic refusal when binding set has an extraneous item; verifies state equality including extra binding.
24. `multi-item-restore-refusal-duplicate-binding-preserves-state`: Atomic refusal on duplicate binding IDs; verifies full state equality.
25. `multi-item-restore-refusal-cross-world-preserves-state`: Atomic refusal on cross-world interactable binding; verifies full state equality.
26. `multi-item-restore-refusal-generation-mismatch-preserves-state`: Atomic refusal on generation mismatch; verifies full state equality.
27. `multi-item-restore-refusal-actor-mismatch-preserves-state`: Atomic refusal on payload actor mismatch; verifies full state equality.
28. `multi-item-restore-refusal-incompatible-mass-preserves-state`: Atomic refusal when live Rigidbody mass differs from definition; verifies full state equality.
29. `multi-item-restore-refusal-incompatible-dimensions-preserves-state`: Atomic refusal when physical dimensions differ from definition; verifies full state equality.
30. `multi-item-restore-refusal-multiple-carried-roots-preserves-state`: Atomic refusal when payload contains multiple carried roots; verifies full state equality.
31. `multi-item-restore-unrelated-held-setup-success`: Verifies valid setup of registered non-physical held alien interactable via `NpcActionApi.RestoreHeld`.
32. `multi-item-restore-refusal-unrelated-held-preserves-state`: Atomic refusal when actor holds unrelated object; verifies full state including held identity and alien object preserved.
33. `multi-item-restore-unrelated-held-cleanup-success`: Verifies clean teardown of alien fixture and restoration of original held root.
34. `multi-item-restore-refusal-omitted-live-item-preserves-state`: Atomic refusal when payload and bindings match each other but omit an existing live model item.
35. `multi-item-restore-refusal-empty-replacement-preserves-state`: Atomic refusal when attempting empty payload/binding replacement on a non-empty live model.
36. `multi-item-restore-empty-model-empty-payload-success`: Empty-empty positive restoration on dedicated empty model, empty payload, and empty bindings, verifying zero item count, correct tick, zero receipts, no held object, and unchanged scene state.
37. `multi-item-restore-equal-mass-fixture-preconditions`: Verifies dedicated equal-mass fixture setup (two genuine 1.0kg definitions/bodies with identical dimensions and valid components).
38. `multi-item-restore-equal-mass-baseline-success`: Verifies baseline multi-item restore of equal-mass fixture before mutation.
39. `multi-item-restore-equal-mass-crosswire-preserves-mass-match`: Verifies cross-wired Body alias preserves strict equal-mass match (both 1.0kg matching definition), isolating reference ownership refusal from mass mismatch.
40. `multi-item-restore-refusal-crosswired-body-preserves-state`: Atomic refusal when two bindings share or cross-wire the same Rigidbody; verifies complete pre-refusal equal-mass fixture state before mutation undo.
41. `multi-item-restore-refusal-crosswired-collider-preserves-state`: Atomic refusal when two bindings share or cross-wire the same Collider; preserves full scene state.
42. `multi-item-restore-refusal-foreign-gameobject-body-preserves-state`: Atomic refusal when an item's Body references an external GameObject; preserves full scene state including foreign object.
43. `multi-item-restore-refusal-incoherent-physics-flags-preserves-state`: Atomic refusal when physical component flags are incoherent with state (e.g. Free item with kinematic body); preserves full state.
44. `multi-item-restore-refusal-model-type-mismatch-preserves-state`: Atomic refusal when live item definition type conflicts with registered model type; preserves full state.
45. `multi-item-restore-refusal-non-unit-scale-preserves-state`: Atomic refusal when live item transform has non-unit scale; preserves full state.
46. `lossy-scale-finite-helper-contract`: Verifies `!ItemDefinition.Finite` on `float.NaN`, `float.PositiveInfinity`, and `float.NegativeInfinity`.
47. `unity-transform-rejects-nonfinite-scale-assignment`: Directly tests and documents Unity's engine runtime boundary where assigning non-finite scale to `Transform.localScale` is ignored by the engine.
48. `legacy-single-item-restore-success`: Verifies backward compatibility of 5-argument `RestoreRuntime` on single-item payloads.
49. `legacy-single-item-restore-rejects-multi-item`: Verifies 5-argument `RestoreRuntime` fails closed atomically on multi-item payloads.
50. `multi-item-restore-receipt-duplicate-replay`: Replays canonical restored action receipt 10, verifying idempotent duplicate receipt return without state mutation.
51. `multi-item-restore-conflict-req-valid-rotation`: Verifies conflict request uses valid canonical rotation `Quaternion.identity`.
52. `multi-item-restore-conflict-signature-changed`: Verifies conflict request canonical signature diverges from saved pickup signature.
53. `multi-item-restore-receipt-conflict-refusal`: Verifies conflicting request with restored requestId 10 returns `!success && !duplicate && code == "request-id-conflict"` while fully preserving unchanged scene and model state.
54. `multi-item-restore-monotonic-sequence-allocation`: Verifies monotonic requestId allocation advances strictly above restored receipts.
55. `multi-item-restore-subsequent-action-success`: Verifies execution of subsequent actions using allocated sequence IDs after restore.

### 7.3 Multi-Instance Bootstrap and Cold Restore Integration Checks (`PhysicalItemBootstrapRuntimeChecks.cs`, invoked by `PhysicalItemRuntimeChecks.Run`)
Executed within an isolated local physics scene with live `PhysicalItemBootstrap`, `NpcAutonomy`, `NpcActionApi`, `PhysicalItemCatalog`, and full component graphs:
1. `bootstrap-cold-construction-baseline-demo-item-created`, `bootstrap-cold-construction-baseline-demo-id-exact`, `bootstrap-cold-construction-baseline-demo-mass-exact`, `bootstrap-cold-construction-baseline-model-count`, `bootstrap-cold-construction-baseline-demo-free`, `bootstrap-cold-construction-baseline-flags`, `bootstrap-cold-construction-baseline-all-interactables-count`: Fresh bootstrap without save path creates exactly 1 demonstration item (`canyon-artifact-01`), 2.5kg, Free, registered in model and interactables.
2. `bootstrap-author-model-pickup-receipt`, `bootstrap-author-store-pouch`, `bootstrap-author-store-gem`, `bootstrap-author-store-chisel`, `bootstrap-mixed-save-atomic-success`, `bootstrap-mixed-save-exists-on-disk`: Mixed graph authoring and atomic disk persistence.
3. `bootstrap-cold-construction-mixed-graph-load-success`, `bootstrap-cold-construction-mixed-flags-valid`, `bootstrap-cold-construction-exact-binding-count`, `bootstrap-cold-binding-non-null`, `bootstrap-cold-binding-expected-id`, `bootstrap-cold-binding-unique-instance`, `bootstrap-cold-physical-item-id-match`, `bootstrap-cold-interactable-id-match`, `bootstrap-cold-construction-all-six-ids-present`, `bootstrap-cold-no-leftover-demo-item`: Loads 6-item mixed graph (`chest-01`, `basket-01`, `pouch-01`, `gem-01`, `chisel-01`, `rock-01`) from disk, creating exact stable IDs with exactly one live instance each and no leftover baseline demo item.
4. `bootstrap-registry-includes-all-eight-interactables`, `bootstrap-registry-unique-id`, `bootstrap-registry-baseline-nonphysical-preserved`, `bootstrap-actions-api-constructed-cleanly`, `bootstrap-actions-registers-physical-item`: Complete registry exposure before `NpcActionApi` copies it, verifying baseline nonphysical entries (`cargo-nonphys`, `depot-nonphys`) are preserved.
5. `bootstrap-cold-restore-basket-is-carried`, `bootstrap-cold-restore-basket-carried-hand-match`, `bootstrap-cold-restore-actions-held-match`, `bootstrap-cold-restore-basket-held-by-agent`, `bootstrap-cold-restore-basket-trigger-collider-enabled`, `bootstrap-cold-restore-basket-kinematic-body`, `bootstrap-cold-restore-basket-renderer-visible`, `bootstrap-cold-restore-pouch-stored-in-basket`, `bootstrap-cold-restore-pouch-parented-to-basket`, `bootstrap-cold-restore-pouch-collider-disabled`, `bootstrap-cold-restore-pouch-body-kinematic`, `bootstrap-cold-restore-pouch-renderer-hidden`, `bootstrap-cold-restore-gem-stored-in-pouch`, `bootstrap-cold-restore-gem-parented-to-pouch`, `bootstrap-cold-restore-gem-collider-disabled`, `bootstrap-cold-restore-chisel-stored-in-chest`, `bootstrap-cold-restore-chisel-parented-to-chest`, `bootstrap-cold-restore-chisel-collider-disabled`, `bootstrap-cold-restore-chest-is-free`, `bootstrap-cold-restore-chest-solid-collider-enabled`, `bootstrap-cold-restore-chest-dynamic-physics`, `bootstrap-cold-restore-rock-is-free`, `bootstrap-cold-restore-rock-solid-collider-enabled`, `bootstrap-cold-restore-carried-mass-exact`, `bootstrap-cold-restore-basket-contained-mass-exact`, `bootstrap-cold-restore-chest-total-mass-exact`: Cold runtime restoration verifying Carried root, Stored descendants, Free roots, trigger/solid colliders, renderers visibility, and exact masses within `<= 0.0001f` kg tolerance.
6. `bootstrap-save-reload-cycle-[1..5]-load-success`, `bootstrap-save-reload-cycle-[1..5]-count-exact`, `bootstrap-save-reload-cycle-[1..5]-carried-mass-stable`, `bootstrap-save-reload-cycle-[1..5]-chest-mass-stable`, `bootstrap-save-reload-cycle-[1..5]-resave-success`: 5 sequential save/reload cycles across fresh bootstrap and model instances verifying zero state drift, mass stability (<= 0.0001f), and no duplicate bodies.
7. `bootstrap-save-single-demo-free-success`, `bootstrap-old-single-demo-free-load-success`, `bootstrap-old-single-demo-free-count-one`, `bootstrap-old-single-demo-free-demonstration-item-wired`, `bootstrap-old-single-demo-free-state-free`, `bootstrap-save-single-demo-carried-success`, `bootstrap-old-single-demo-carried-load-success`, `bootstrap-old-single-demo-carried-held-match`: Backward compatibility loading legacy single-demo saves in both Free and Carried states using the validated list binder.
8. `bootstrap-refusal-baseline-demo-exists`, `bootstrap-refusal-unknown-type-refused`, `bootstrap-refusal-unknown-type-preserves-baseline-model-and-demo`, `bootstrap-refusal-unknown-type-zero-staged-leaks`, `bootstrap-refusal-unknown-type-preserves-save-bytes`, `bootstrap-refusal-cycle-graph-refused`, `bootstrap-refusal-cycle-zero-staged-leaks`, `bootstrap-refusal-cycle-preserves-save-bytes`, `bootstrap-refusal-duplicate-ids-refused`, `bootstrap-refusal-duplicate-zero-staged-leaks`, `bootstrap-refusal-duplicate-preserves-save-bytes`, `bootstrap-refusal-wrong-scope-refused`, `bootstrap-refusal-wrong-scope-zero-staged-leaks`, `bootstrap-refusal-wrong-scope-preserves-save-bytes`, `bootstrap-refusal-corrupt-envelope-refused`, `bootstrap-refusal-corrupt-zero-staged-leaks`, `bootstrap-refusal-corrupt-preserves-save-bytes`: Atomic refusal on unknown types, containment graph cycles, duplicate item IDs, scope mismatches, and corrupt envelopes, preserving baseline objects, baseline model, baseline registry, and original disk save bytes without staged leaks.
9. `bootstrap-replay-load-mixed-success`, `bootstrap-receipt-duplicate-replay-success`, `bootstrap-receipt-conflict-refusal`, `bootstrap-monotonic-sequence-allocation`, `bootstrap-subsequent-action-success`: Receipt replay, conflicting request refusal, monotonic sequence allocation strictly above restored receipts, and execution of subsequent actions.
10. `bootstrap-repeated-reset-interactables-count-constant`, `bootstrap-repeated-reset-phys-items-count-constant`, `bootstrap-repeated-reset-request-id-preserved`: 5 consecutive autonomy resets do not accumulate instances, duplicate interactables, or reapply receipts.
11. `bootstrap-fixedupdate-syncs-chest-free-item`, `bootstrap-fixedupdate-syncs-rock-free-item`, `bootstrap-fixedupdate-stored-item-location-preserved`: Dynamic physics motion of multiple Free items is synchronized to the authoritative model without desynchronizing Stored items.

### 7.4 Production Transactional Basket and Stable Slots Integration Checks (`BasketTransactionChecks.cs`, invoked by `PhysicalItemRuntimeChecks.Run`)
Executed within the isolated runtime scene, verifying production Slice A store/retrieve transactions:
1. **Model & Sequence Allocation**:
   - `basket-tx-register-basket-def`, `basket-tx-register-chest-def`, `basket-tx-register-apple-def`, `basket-tx-register-pear-def`, `basket-tx-register-heavy-def`, `basket-tx-register-bulky-def`: Valid registration of container and non-container definitions with positive dimensions, masses, and limits.
   - `basket-tx-allocate-req-1`, `basket-tx-first-req-is-1`, `basket-tx-resume-req-seq`, `basket-tx-allocate-req-after-resume`, `basket-tx-resumed-monotonic-id`: Monotonic request sequence allocation advancing strictly above resumed bounds.
2. **Deterministic First-Vacant Slot Allocation & Stable Slots**:
   - `basket-tx-reg-basket`, `basket-tx-reg-apple-1..3`, `basket-tx-reg-pear-1`: Registration of items across Free and Carried initial states.
   - `basket-tx-prep-store-1`, `basket-tx-token-assigned-slot-0`, `basket-tx-commit-store-1`, `basket-tx-receipt-store-1`, `basket-tx-item-slot-is-0`: Preparation and commit of initial item assigns first vacant slot 0.
   - `basket-tx-prep-store-2`, `basket-tx-token-assigned-slot-1`, `basket-tx-commit-store-2`, `basket-tx-item-slot-is-1`: Sequential items allocate ascending vacant slots (slot 1).
   - `basket-tx-prep-store-3`, `basket-tx-token-assigned-slot-2`, `basket-tx-commit-store-3`: Third item allocates slot 2.
3. **Stable Holes on Retrieve (No Reshuffling)**:
   - `basket-tx-prep-ret-1`, `basket-tx-ret-token-recorded-slot-1`, `basket-tx-commit-ret-1`, `basket-tx-receipt-ret-1`: Retrieving item from slot 1 marks it Carried and resets its `containerSlot` to -1.
   - `basket-tx-retrieved-slot-is-neg1`, `basket-tx-retrieved-location-is-carried`: Stored slot cleared to -1 on non-stored states.
   - `basket-tx-slot-0-remains-apple-1`, `basket-tx-slot-1-is-vacant-hole`, `basket-tx-slot-2-remains-apple-3`: Surviving items retain exact original slots without index shifting; vacated slot remains a vacant hole.
   - `basket-tx-prep-store-4`, `basket-tx-fills-vacant-hole-at-slot-1`, `basket-tx-commit-store-4`, `basket-tx-pear-stored-in-slot-1`, `basket-tx-apple-1-unmoved`, `basket-tx-apple-3-unmoved`: New item stored deterministically reuses the first vacant hole (slot 1) without perturbing items in slots 0 and 2.
4. **Capacity & Cycle Enforcements**:
   - `basket-tx-fill-slot-3`, `basket-tx-assigned-last-slot-3`, `basket-tx-commit-slot-3`: Slot 3 filled, reaching container capacity.
   - `basket-tx-reject-slot-overflow`: Attempting to store into a full container fails closed with `container-slot-capacity-exceeded`.
   - `basket-tx-reject-mass-capacity`: Exceeding container mass limit fails closed with `container-mass-capacity-exceeded`.
   - `basket-tx-reject-volume-capacity`: Exceeding container volume limit fails closed with `container-volume-capacity-exceeded`.
   - `basket-tx-prep-nest`, `basket-tx-commit-nest`, `basket-tx-prep-nest-2`, `basket-tx-commit-nest-2`, `basket-tx-reject-cycle`: Containment loops across nested hierarchies fail closed with `containment-cycle-detected`.
5. **Revision-Bound Token Invalidation**:
   - `basket-tx-advance-tick-bumps-revision`, `basket-tx-commit-fails-on-revision-mismatch`, `basket-tx-gem-remained-carried`: Mutating model revision between prepare and commit invalidates the token, rejecting commit without state mutation.
6. **Live Runtime NpcActionApi Transaction & Atomic Rollback**:
   - `basket-tx-reg-live-basket`, `basket-tx-reg-live-apple`, `basket-tx-reg-live-other`, `basket-tx-restore-held-apple`: Authoritative wiring of live scene interactables and physical items.
   - `basket-tx-reject-missing-hand`, `basket-tx-missing-hand-refusal-replay`: Missing actor hand on Retrieve fails closed (`item-unavailable`), records refusal receipt in model ledger, and replays duplicate denial identically.
   - `basket-tx-reject-item-permission`, `basket-tx-item-permission-refusal-replay`: False `NpcInteractable.Permission` on item fails closed (`permission-denied`), records refusal receipt in model ledger, and replays duplicate denial identically.
   - `basket-tx-failpoint-rejected`, `basket-tx-failpoint-apple-still-held`, `basket-tx-failpoint-apple-is-carried`, `basket-tx-failpoint-apple-not-stored`, `basket-tx-failpoint-apple-collider-restored`, `basket-tx-failpoint-apple-parent-restored`, `basket-tx-failpoint-unrelated-unmoved`, `basket-tx-failpoint-unrelated-unrotated`: Deterministic test failpoint after runtime apply verifies that an unexpected exception or failure before commit executes exact atomic rollback, leaving hand attachment intact, follower kinematic/velocity states zeroed, and unrelated scene bodies undisturbed.
   - `basket-tx-api-store-success`, `basket-tx-held-cleared-after-store`, `basket-tx-apple-phys-is-stored`, `basket-tx-apple-col-disabled-when-stored`, `basket-tx-apple-parented-to-basket`: Execution of `NpcActionKind.Store` through `NpcActionApi` sets kinematic body, disables collider/renderers, parents to container, and clears held item.
   - `basket-tx-duplicate-replay-success`: Replaying identical request ID returns `duplicate == true`, `success == true` immediately from physical model receipts without false spatial rejections.
   - `basket-tx-conflict-refused`: Conflicting request with same ID returns `request-id-conflict`.
   - `basket-tx-api-ret-success`, `basket-tx-apple-restored-to-held`, `basket-tx-apple-phys-is-carried`, `basket-tx-apple-col-enabled-as-trigger`: Execution of `NpcActionKind.Retrieve` restores kinematic trigger carry in actor hand and enables renderers.
   - `basket-tx-cargo-mismatch-fail`, `basket-tx-held-intact-after-failure`: Scoped guard refusal (`cargo-ownership-mismatch`) records refusal receipt in authoritative ledger and rolls back runtime physical state to exact pre-attempt condition.
   - `basket-tx-unrelated-body-unmoved`, `basket-tx-unrelated-body-unrotated`: Unrelated scene bodies are completely undisturbed during transaction failure and rollback.
7. **Save Migration & Strict Malformed Slot Refusals**:
   - `basket-tx-load-legacy-v1`, `basket-tx-v1-payload-not-null`, `basket-tx-v1-disk-file-untouched`: Loading legacy v1 payload verifies SHA-256 integrity against raw JSON text without mutating on-disk file bytes.
   - `basket-tx-migrated-apple-slot-0`, `basket-tx-migrated-pear-slot-1`: Direct children in v1 payload are sorted by `itemId` (ordinal) and assigned sequential slots `0..N-1` in memory.
   - `basket-tx-restore-migrated-snapshot`, `basket-tx-restored-model-apple-slot-0`, `basket-tx-restored-model-pear-slot-1`: Restores migrated payload into live model.
   - `basket-tx-v2-reject-unassigned-slot-on-disk`: On-disk save with schema `starfall.physical-save.v2` containing any stored item with `containerSlot = -1` is rejected closed by `ItemPersistence.TryLoad` without mutating memory or disk.
   - `basket-tx-reject-out-of-bounds-slot`, `basket-tx-reject-duplicate-slot-occupancy`, `basket-tx-reject-free-item-with-slot`, `basket-tx-reject-inconsistent-slots`: Fails closed on out-of-bounds slot, duplicate slot occupancy, non-stored slot values, or inconsistent mixed assignments.
8. **50 Store/Retrieve Cycles & Physical Stability**:
   - `basket-tx-50-cycles-completed`: 50 consecutive Store/Retrieve transactions execute successfully without state drift or hierarchy corruption.
   - `basket-tx-50-cycles-mass-exact`: Mass remains strictly 0.2000kg (`<= 0.0001f` kg tolerance) across all 50 cycles without phantom mass accumulation.
   - `basket-tx-50-cycles-scale-unit`: Transform `lossyScale` remains unit `Vector3.one` (`<= 0.001f` tolerance) across all 50 cycles.
9. **5 Restarts with Durable Scoped Denial Replay**:
   - `basket-tx-initial-denial-recorded`, `basket-tx-initial-store-recorded`, `basket-tx-initial-save-atomic`: Authoritative model records scoped denial receipt (`cargo-ownership-mismatch`) and valid store receipt, persisting atomically.
   - `basket-tx-restart-[1..5]-load-success`, `basket-tx-restart-[1..5]-restore-success`: 5 sequential save, reload, and restore cycles into fresh models.
   - `basket-tx-restart-denial-replay-[1..5]`: Replaying pre-restart denial request returns `duplicate == true`, `!success`, and `code == "cargo-ownership-mismatch"` identically across all 5 restarts.
   - `basket-tx-restart-store-replay-[1..5]`: Replaying pre-restart store request returns `duplicate == true`, `success == true`, and `code == "stored-in-container"` identically across all 5 restarts.
   - `basket-tx-restart-[1..5]-monotonic-above-restored`: Monotonic sequence allocation advances strictly above restored highest receipt across every restart.
   - `basket-tx-restart-[1..5]-resave-success`: Re-saving at each cycle preserves full ledger and item state.

### 7.4 Player Integration Suite (`BasketPlayerIntegrationChecks.cs`, orchestrated via `PhysicalItemValidation.RunAsync`)
Executed within an isolated local physics scene driven asynchronously across genuine PlayMode frames via `PhysicalItemValidation.OnEditorUpdate`, validating Slice B player contracts:
1. **WovenBasketVisual Geometry & Mesh Disposal**:
   - `basket-player-visual-component-present`, `basket-player-mesh-generated`, `basket-player-mesh-has-vertices`, `basket-player-mesh-has-triangles`: Procedural mesh generation with verified topology.
   - `basket-player-mesh-width-within-catalog`, `basket-player-mesh-height-within-catalog`, `basket-player-mesh-depth-within-catalog`: Strict adherence to catalog dimensions (<= 0.3x0.3x0.3m).
   - `basket-player-visual-has-zero-colliders`, `basket-player-visual-no-duplicate-item`, `basket-player-visual-no-duplicate-body`: Strict collider and component isolation on visual children.
   - `basket-player-visual-unit-scale`: Unit local scale.
   - `basket-player-mesh-disposal-precondition`, `basket-player-mesh-disposed-on-destroy`: Procedural mesh asset lifecycle management disposing mesh on GameObject destruction.
2. **PhysicalItemCatalog & Bootstrap Starter Layout**:
   - `basket-player-catalog-has-basket`, `basket-player-catalog-get-basket`, `basket-player-basket-has-4-slots`, `basket-player-basket-10kg-limit`: Catalog registration of 4-slot, 10kg container-basket.
   - `basket-player-bootstrap-starter-model-count`, `basket-player-bootstrap-basket-created`: Opt-in starter layout (`OptInStarterLayout = true`) instantiates complete 6-item set including free canyon basket.
   - Baseline autoload scoping and path isolation: every test bootstrap explicitly assigns `ExplicitSavePathOverride` before activation; fresh bootstrap with `OptInStarterLayout = false` does not touch or create default user save files on disk.
3. **Coherent Production Lifecycle Fixture & Genuine PlayMode Frames**:
   - Complete single-graph fixture integration (`actorGo`, `CharacterController`, `CharacterPreviewActor`, `Animator`, `Camera`, `CharacterPreviewCamera`, `PreviewDisplayMode`, `NpcDecisionHud`, `NpcAutonomy`, `PhysicalItemBootstrap`, `NpcPlayerControls`, `PhysicalContainerPanel`).
   - Imported avatar rig with verified humanoid right hand (`basket-player-animator-present`, `basket-player-humanoid-hand-present`).
   - Single physical authority: physical interactables register exclusively through `bootstrap.RegisterBinding`; `brain.Registry` retains only separate non-physical objects, preventing duplicate-ID collisions during `ResetState()`.
   - Genuine `NpcAutonomy.Start()` execution over real PlayMode frame updates sets `brain.Ready = ResetState()` without reflection or private setter relaxation (`basket-player-brain-ready`, `basket-player-brain-actions-initialized`, `basket-player-actions-model-aligned`). Autonomy is explicitly paused after Ready.
4. **Authoritative Slot Projection & Transactional Store/Retrieve Flow**:
   - `basket-player-initial-slots-empty`: Initial container slot state is empty.
   - `basket-player-pickup-ruby-success`: Pickup of loose item into hand.
   - `basket-player-store-ruby-success`, `basket-player-held-cleared-after-store`: Transactional Store into basket assigns slot 0 and clears held cargo.
   - `basket-player-slot-0-occupied`, `basket-player-slot-0-has-ruby`, `basket-player-try-get-item-slot-0`: Model projection matches assigned slot.
   - `basket-player-retrieve-ruby-success`, `basket-player-ruby-held-after-retrieve`, `basket-player-slot-empty-after-retrieve`: Retrieve extracts cargo to hand and restores empty slot.
5. **Refusal Invariants: Hands-Full, Reach & True LOS Authority**:
   - `basket-player-store-back-success`, `basket-player-pickup-chisel-success`: Setup of carried item with stored container contents.
   - `basket-player-refuse-hands-full-retrieve`, `basket-player-held-preserved-on-refusal`: Retrieve fails closed with `hands-full` when hand is occupied, preserving held item and container state.
   - `basket-player-refuse-out-of-reach`, `basket-player-held-preserved-on-far-refusal`: Interaction distance (> 0.65m approach, > 1.7m eye distance) fails closed with `out-of-reach`.
   - `basket-player-refuse-los-blocked`, `basket-player-held-preserved-on-los-refusal`: True Line of Sight occlusion verified against real obstruction wall on layer 8, failing closed with `line-of-sight-blocked`.
6. **Stable Slot Holes & Capacity Limits**:
   - `basket-player-hand-cleared-after-drop`, `basket-player-store-chisel-success`, `basket-player-chisel-in-slot-1`: Chisel stored into slot 1.
   - `basket-player-retrieve-hole-success`, `basket-player-stable-hole-slot-0`: Retrieving item from slot 0 leaves slot 0 as vacant hole without reshuffling slot 1.
   - `basket-player-refill-hole-success`, `basket-player-hole-refilled-slot-0`, `basket-player-slot-1-chisel-unmoved`: Storing new item deterministically reuses vacant hole at slot 0 without moving chisel at slot 1.
   - `basket-player-all-4-slots-filled`, `basket-player-refuse-slot-capacity-exceeded`: Filling all 4 container slots causes 5th store attempt to fail closed with `container-slot-capacity-exceeded`.
   - `basket-player-hand-cleared-for-panel`, `basket-player-ruby-free-for-panel`, `basket-player-basket-free-for-panel`: Verified baseline item location cleanup to Free before UI and panel tests.
7. **Normal Save Entry Point & Live Model Disk Verification**:
   - `basket-player-normal-save-returns-true`, `basket-player-save-file-created`, `basket-player-save-not-rejected`: Bootstrap `SaveCurrentState` produces atomic on-disk payload.
   - `basket-player-disk-payload-valid`: Re-read verification via `ItemPersistence.TryLoad` with definition-backed live model succeeds.
8. **Safe Default Save Path, Sibling Preservation & Cold Corrupt Save Protection**:
   - `basket-player-default-save-path-rooted`, `basket-player-default-save-path-is-json`: Default save path is rooted in persistent storage and incorporates instance worldId.
   - `basket-player-cold-corrupt-actions-null`: Tested on separate cold fixture before `Actions` exists to verify genuine cold parser rejection.
   - `basket-player-corrupt-load-returns-false`, `basket-player-corrupt-sets-source-rejected`, `basket-player-corrupt-records-rejected-path`: Corrupt save sets rejected provenance flags.
   - `basket-player-rejected-save-bytes-unchanged`: Disk bytes of rejected file are preserved bit-for-bit.
   - `basket-player-refuse-overwriting-rejected-source`, `basket-player-last-save-failed-flag-set`, `basket-player-refuse-error-code-match`, `basket-player-rejected-source-still-byte-unchanged`: `SaveCurrentState` refuses to overwrite rejected source file, reporting `refused-overwriting-rejected-source` and preserving byte-for-byte equality.
   - `basket-player-recovery-save-succeeds`, `basket-player-recovery-file-created`, `basket-player-recovery-path-distinct`, `basket-player-recovery-path-not-sibling`, `basket-player-existing-sibling-preserved`, `basket-player-rejected-source-preserved-after-recovery`: Explicit recovery save searches for collision-free `<name>-recovery[-{counter}].json` destinations, preserving unrelated sibling files and rejected source files untouched.
   - `basket-player-recovery-repeat-same-target`: Repeated recovery saves to the active deliberate recovery path overwrite that same target.
   - `basket-player-refuse-overwriting-sibling`, `basket-player-sibling-bytes-preserved-after-refusal`: Direct attempts to overwrite existing siblings via `SaveCurrentState(siblingPath, isRecoveryAction: true)` fail closed with `refused-overwriting-existing-sibling`.
9. **Controls & Container Panel Verification**:
   - `basket-player-tab-entered-possession`: Tab key enters possession mode.
   - `basket-player-regression-basket-in-reach`, `basket-player-regression-ruby-in-reach`: Items positioned within interaction reach.
   - `basket-player-panel-is-open`, `basket-player-panel-selected-basket`: Opening container panel selects target container.
   - `basket-player-slot-1-button-interactable`, `basket-player-slot-button-click-retrieves-chisel`: Direct `Button.onClick.Invoke()` on slot 1 button executes retrieval into hand.
   - `basket-player-slot-2-button-exists`, `basket-player-slot-click-hands-full-preserves-held`, `basket-player-slot-click-hands-full-receipt`: Direct `Button.onClick.Invoke()` on slot 2 button with full hands refuses retrieval with `hands-full` receipt and preserves held item.
   - `basket-player-store-button-interactable`, `basket-player-store-button-stores-item`: Direct `Button.onClick.Invoke()` on store button transactions held item into container.
   - `basket-player-panel-closed-for-move`, `basket-player-panel-open-away-from-containers`, `basket-player-selected-container-absent-away`: Verified no-container state when actor moves away from container reach.
   - `basket-player-save-renders-without-container`, `basket-player-save-receipt-rendered`: Save button and receipt label remain interactive and render full paths even when no container is selected.
   - `basket-player-panel-closed-away`, `basket-player-panel-is-open-for-retrieve`, `basket-player-slot-button-click-retrieves-ruby`, `basket-player-panel-closed-for-drop`: Deliberate retrieval and ground-drop of ruby-01 via slot 0 button click before cycling test.
   - `basket-player-panel-open-suppresses-looking`: Look input suppressed during open panel.
   - `basket-player-pickup-target-non-null`, `basket-player-pickup-target-is-ruby`: `CurrentPickupTarget` cycled among free items selects free ruby-01.
   - `basket-player-pickup-picks-ruby-not-basket`, `basket-player-basket-remains-on-ground`: Pressing real `G` key picks loose ruby and leaves basket on ground.
   - `basket-player-ruby-dropped-back`: Second `G` key press drops ruby back to ground.
   - `basket-player-menu-open`, `basket-player-menu-resume`: Options menu open and resume transitions via real `Escape` key events.
10. **Cold Restore Complete Replacement (Graph, Visual, Stored Slots, Object Absence)**:
    - `basket-player-cold-restore-load-succeeds`, `basket-player-cold-restore-item-count-exact-1`, `basket-player-cold-restore-has-custom-item`, `basket-player-cold-restore-cleared-default-item`: Loading custom save during cold construction completely replaces baseline starter items.
    - `basket-player-cold-restore-pre-count-is-6`, `basket-player-cold-restore-old-basket-existed`: Starter 6-item layout initialized; exact owner reference captured from `richBootstrap.Bindings`.
    - `basket-player-cold-restore-rich-load-succeeds`, `basket-player-cold-restore-rich-count-is-2`: Cold restore replaces starter graph with restored woven basket and stored ruby gem.
    - `basket-player-cold-restore-old-scene-objects-destroyed`: Destruction of old starter GameObjects (`canyon-basket-01`) verified on captured binding reference without global `GameObject.Find`.
    - `basket-player-cold-restore-new-basket-created`, `basket-player-cold-restore-woven-basket-visual-created`, `basket-player-cold-restore-visual-has-no-colliders`: Instantiation of restored basket with valid procedural `WovenBasketVisual` and zero visual colliders confirmed from restored bindings.
    - `basket-player-cold-restore-stored-slot-restored`, `basket-player-cold-restore-bindings-count-exact`: Slot 0 occupancy of restored stored gem and exact binding count confirmed.

---

## 8. Verification Invocations & Acceptance Criteria

1. **Test Runner Seams**:
   - `BasketTransactionChecks.Run(scene, physics, CreateGo, Check, passed, worldId, genId, agentId, actorGo, handGo)` is invoked synchronously by `PhysicalItemRuntimeChecks.Run` (Section 13).
   - `BasketPlayerIntegrationChecks.RunAsync(checkRaw, passed, setCleanup)` is invoked asynchronously by `PhysicalItemValidation.ExecuteSuiteInPlayMode` and stepped frame-by-frame via `PhysicalItemValidation.OnEditorUpdate` in genuine PlayMode.
2. **Offline Authoring Boundary**:
   Under coordinator-directed constraints (Pass 4), all contracts, rollback mechanisms, guards, and test checks are authored and verified structurally without live Unity execution, test runner invocation, or shell commands. Live test execution is reserved for coordinator validation.
