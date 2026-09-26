# Conserved Food Commands: Material Transaction Primitives (Pass 1)

## 1. Executive Summary

This document specifies the architecture, contracts, and persistence rules for **Material Transaction Pass 1** of the Food-to-Physical Ownership Bridge.

### 1.1 First Slice Scope & Boundaries
- **Component-Only Slice**: Implements typed atomic issue and consume transactions on **detached `ItemModel` candidates only**.
- **Runtime Inactive**: Runtime gameplay remains completely inactive in this slice:
  - No live nutrition mutations or food physiology tick modifications.
  - No death custody activation or scene follower re-wiring.
  - No live user save file I/O or automatic world migration.
  - Zero dual-writes between scalar food quantities and physical items.
- **Physical Item Authority**: `ItemModel` is established as the sole inventory authority for physical food units (fruit and seed). Each physical unit possesses exactly one stable ID throughout its entire lifecycle, residing in either an active state (Free, Carried, Stored) or a terminal retired state (Tombstone).

---

## 2. Component Layout & Files

```
Assets/CityLife/Items/
  ├── ItemModel.cs                      // Extended: Detached candidate enforcement, material batch preparation, atomic commit, tombstones, material receipts ledger, issuance watermark
  ├── ItemPersistence.cs                // Extended: Schema v3 (starfall.physical-save.v3) support, managed payload fields, strict v1/v2 backward compatibility, resurrection rejection
  ├── ItemMaterialTransaction.cs        // NEW: Material batch requests, effects, receipts, tombstones, authority interface, prepared revision token
  ├── ItemMaterialTransactionChecks.cs  // NEW: 34 deterministic test checks verifying isolation, rollbacks, carry limits, replay, and persistence
  └── ItemMaterialTransactionChecks.cs.meta
Assets/CityLife/Food/
  ├── FoodPhysicalCatalog.cs            // NEW: Trusted opt-in physical definitions for fruit (0.020 kg) and seed (0.0002 kg)
  └── FoodPhysicalCatalog.cs.meta
docs/
  └── FOOD-CONSERVED-COMMANDS.md        // This specification document
```

All existing checkpoint repository, codec, runtime autonomy, and player control files remain strictly frozen in this slice.

---

## 3. Data Contracts & Public API

### 3.1 Material Batch Types (`ItemMaterialTransaction.cs`)

```csharp
public enum MaterialEffectKind
{
    Issue,
    Consume,
    PlantSeed
}

[Serializable]
public struct MaterialBatchItemEffect
{
    public MaterialEffectKind kind;
    public string itemId;
    public string itemTypeId;
    public ItemLocationKind destinationLocation; // Free, Carried, Stored
    public string destinationContainerId;
    public string holderActorId;
    public Vector3 position;
    public Quaternion rotation;
    public string provenance;                   // e.g. "biological-harvest", "consumed-fruit", "planted-seed"
}

[Serializable]
public struct MaterialBatchRequest
{
    public int requestId;
    public string worldId;
    public string generationId;
    public string actorId;
    public string commandName;                  // "Gather", "Eat", "Harvest", "Plant", "CampAid"
    public string sourcePlaceId;                // Place provenance for biological audit
    public List<MaterialBatchItemEffect> effects;
}

[Serializable]
public struct TombstoneRecord
{
    public string itemId;
    public string itemTypeId;
    public long retiredTick;
    public string retiredByActorId;
    public string reason;                       // "consumed", "planted", etc.
    public string provenance;
}

[Serializable]
public struct MaterialBatchReceipt
{
    public int requestId;
    public string worldId;
    public string generationId;
    public string actorId;
    public string commandName;
    public bool success;
    public bool duplicate;
    public string code;
    public List<string> issuedItemIds;
    public List<string> retiredItemIds;
    public float totalCarriedMassKg;
    public int totalCarriedCount;
}
```

### 3.2 Trusted Authority Interface

```csharp
public interface IMaterialBatchAuthority
{
    bool AuthorizeBatch(ItemModel model, MaterialBatchRequest request, out string failureReason);
}
```
Universal-allow booleans are strictly avoided. Production callers must supply typed authorities verifying actor scope, command permission, and effect provenance. Test-specific fixtures (e.g. `TestMaterialBatchAuthority`) explicitly gate artificial deterministic fixtures.

---

## 4. ItemModel Candidate Workflow

### 4.1 Detached Fork Invariant
Directly constructed models (`new ItemModel(...)`) and live/default models are **prohibited from executing material batches**. Opt-in managed workflow requires an explicit detached candidate:

```csharp
// Fork deep-copies definitions, limits, active items, receipts, static anchors, watermark, and tombstones
ItemModel candidateFork = sourceModel.CreateDetachedFork(enableManaged: true);
```
- `IsDetachedFork`: True only on instances produced by `CreateDetachedFork()`.
- `IsManaged`: Explicit opt-in flag. `EnableManagedWorkflow()` requires `IsDetachedFork == true` and refuses live direct models.
- Any attempt to invoke `TryPrepareMaterialBatch` on an unmanaged or direct model fails immediately with:
  `material-batches-require-managed-detached-candidate`.

### 4.2 Multi-Output Atomic Validation & Rollback
`TryPrepareMaterialBatch` operates on a simulated in-memory graph without mutating candidate state:
1. **Consumes Validated**: Consumed items must exist and cannot be non-empty containers.
2. **Issues Validated**: Issued IDs must be valid and cannot overlap with active items or tombstones. Definitions must exist and cannot be anchored. Destination container slots, volumes, and nesting depths are evaluated across all effects collectively.
3. **Actor Carry Limits Enforced**: Total root carried items (1 hand limit) and total mass (25.0 kg limit) are enforced across the entire resultant graph.
4. **All-or-Nothing Guarantee**: If ANY effect fails (e.g. second output exceeds container slots or mass limit), preparation rejects atomically. Zero candidate state is mutated: input items are not consumed, outputs are not issued, and zero tombstones are created.

### 4.3 Revision-Bound Token Lifecycle
Upon successful preparation, a `PreparedMaterialBatch` token is issued:
- Bound to exact `Revision` at preparation time.
- Single-use: committing marks `IsCommitted = true`; subsequent commit attempts fail.
- Cancellation: `CancelMaterialBatch(token)` invalidates the token.
- Stale Invalidation: Any model revision change (mutation, definition registration, another commit) invalidates active tokens.
- Model Binding: Foreign tokens (prepared on model A) cannot be committed to model B.

### 4.4 Tombstone Invariant & Anti-Resurrection
When an item is consumed:
- It is permanently removed from active items and recorded in `tombstones`.
- `IsRetired(itemId)` returns `true`.
- **Registration Rejected**: `RegisterItem` fails closed if `itemId` exists in `tombstones`.
- **Actions Rejected**: Any `Execute` command (Pickup, Store, Drop, Place) targeting a retired item fails.
- **Restore Rejected**: `CanRestoreSnapshot` and `RestoreSnapshot` fail closed if any active item ID exists in `tombstones`.
- **Bounded Ledger**: Tombstones ledger is capped at `MaxTombstoneLedgerSize = 1024` to prevent memory exhaustion, failing closed on overflow rather than truncating history.

### 4.5 Sequential Issuance Watermark
- Stable issuance IDs are generated via `TryAllocateIssuanceId(prefix, out nextItemId)`.
- Format: `{prefix}-{IssuanceHighWatermark:D6}` (e.g. `fruit-000001`).
- Watermark increases monotonically and persists across saves/restarts.
- Overflows fail closed (`long.MaxValue - 1`).

---

## 5. Persistence Specification (Schema v3)

### 5.1 Version Tag & Envelope
- **Managed Schema Version**: `starfall.physical-save.v3`
- **Legacy Schema Versions**: `starfall.physical-save.v2` (container slots aware) and `starfall.physical-save.v1`.
- Envelope contains `schema`, UTF-8 JSON `payload`, and SHA-256 checksum.

### 5.2 Extended Payload Fields
`PhysicalSavePayload` includes managed metadata:
```csharp
public bool isManaged = false;
public long issuanceHighWatermark = 0;
public List<TombstoneRecord> tombstones = new List<TombstoneRecord>();
public List<SavedMaterialBatchReceiptRecord> materialReceipts = new List<SavedMaterialBatchReceiptRecord>();
```

### 5.3 Strict Schema Discrimination & Backward Compatibility
1. **Managed Saves (v3)**:
   - Serialized with schema `starfall.physical-save.v3`.
   - `isManaged` must be `true`.
   - Validates tombstones, receipts, and watermark against strict structural and boundary limits.
   - Rejects payloads where any active item matches a tombstone ID.
2. **Legacy Saves (v1 / v2)**:
   - Maintained with zero regressions.
   - Serialized with schema `starfall.physical-save.v2` when `isManaged == false`.
   - **Fail-Closed on Injected Metadata**: A file claiming schema v1 or v2 that contains `isManaged == true`, non-empty tombstones, non-empty material receipts, or non-zero watermark is rejected as corrupt/tampered.
   - A file claiming schema v3 where `isManaged == false` is rejected.
   - Leaves original files byte-preserved without automatic in-place mutation.

---

## 6. Opt-in Food Physical Catalog

`FoodPhysicalCatalog.cs` defines trusted physical properties for biological items:
- **Fruit (`food.fruit.v1`)**:
  - Mass: `0.020 kg` (20 grams).
  - Dimensions: `(0.035, 0.035, 0.035) m` cube.
  - Non-container (`maxContainedSlots = 0`, `maxContainedVolumeM3 = 0`, `maxContainedMassKg = 0`).
  - Not anchored.
- **Seed (`food.seed.v1`)**:
  - Mass: `0.0002 kg` (0.2 grams).
  - Dimensions: `(0.006, 0.006, 0.006) m` cube.
  - Non-container.
  - Not anchored.

Definitions are opt-in and do not modify `PhysicalItemCatalog.CreateDefaultCatalog` or automatically mint instances.

---

## 7. Downstream Integration Requirements

Before Pass 2 (Paired Command Transaction Service) and Pass 4 (Runtime Cutover) can proceed:
1. **Checkpoint Codec Support for Managed Payload**:
   - `FoodOwnershipCheckpointCodec.cs` currently verifies `starfall.physical-save.v2`.
   - In Pass 2, the checkpoint codec must be explicitly updated and reviewed to admit `starfall.physical-save.v3` when managed mode is active.
2. **Paired Transaction Coordinator**:
   - An authoritative single owner must serialize pairs of `FoodModel` nutrition/tick state and `ItemModel` candidate batches.
   - Guarantees that biological growth or consumption is acknowledged simultaneously with physical item transitions.
3. **Runtime Scene Follower & Grid Visuals**:
   - Runtime follower binding for newly issued fruits/seeds into actor hands or container panels.
