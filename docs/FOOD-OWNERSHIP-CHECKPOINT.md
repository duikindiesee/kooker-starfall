# Food Ownership Checkpoint Specification & Architecture (Pass 5 Bounded Repair)

## 1. Executive Summary

This document specifies the durable storage infrastructure for the Food-to-Physical Ownership Bridge.
Pass 5 performs a targeted bounded repair fixing the Unity runtime exit at `chk3-seq2-committed`:
- **CHECK 3 Initial Ledger Watermark Alignment**: Aligned initial sequence 1 empty-receipt envelope watermark from 1 to 0 (`BuildValidEnvelope(1, "", 0, ...)`), satisfying the anti-reissue guard (`candR.transactionId <= currentAuthoritative.requestHighWatermark` absent in prior ledger) when sequence 2 introduces transactions 1 and 2 with high-watermark 5.
- **CHECK 4 Decoded Payload Tamper**: Mutated the decoded `foodPayload` compact JSON string (`"\"satiety\":6500"` to `"\"satiety\":6501"`) with an explicit change assertion, serializing without hash recomputation (`JsonUtility.ToJson(tamperedFoodEnv, true)`), preserving the exact food payload hash mismatch check.
- **Exhaustive Diagnostic Capture**: Across all verification checks, captured every `Commit`, `LoadAuthoritativeCheckpoint`, and `GetAuthoritativePointer` result into local variables and added actual status/message diagnostics via `Diag(...)` to failure messages while preserving stable check names in `passed`.

### Key Pass 4 & Pass 5 Updates
- **Authoritative Dual-Authority Load**: Added `public CheckpointLoadResult LoadAuthoritativeCheckpoint(FoodModel authoritativeFoodModel, ItemModel authoritativeItemModel, out FoodOwnershipCheckpointEnvelope envelope)` holding an OS-exclusive lock (`.commit.lock`) across repository initialization verification, pointer read, checkpoint envelope read, and both structural and non-mutating semantic validation (`ValidateEnvelopeStructural` and `ValidateEnvelopeSemantic`). On ANY failure, `envelope` and `result.Checkpoint` are strictly null.
- **Fail-Closed Legacy Overload**: Retained legacy no-authority overload `LoadAuthoritativeCheckpoint(out envelope)` fail-closed returning `CheckpointLoadStatus.MissingAuthority` with diagnostic message and null payload to prevent unvalidated admission.
- **Reentrant Lock Prevention**: Implemented private helper `LoadAuthoritativeCheckpointUnderLock` enabling `Commit` (which already holds `.commit.lock`) to validate the current acknowledged checkpoint without attempting recursive lock acquisition or deadlocking.
- **Diagnostic Interpolation Safety**: Fixed sequence and hash mismatch diagnostic branches in `LoadAuthoritativeCheckpointUnderLock` which previously assigned `envelope = null` prior to interpolating `{envelope.sequence}` and `{envelope.checkpointHash}`, eliminating `NullReferenceException`.
- **Pre-Admission Validation Ordering & Fixture Split**: Preserved production ordering in `Commit` where the current acknowledged checkpoint (seq 1) is validated against supplied authority models before candidate admission. Split `chk2` check fixtures: candidate payload mutations (bad seed / bad scope) expect `ValidationFailed`; passing wrong authority models to `Commit` evaluates against the current sequence 1 checkpoint and expects `CorruptState` with unchanged pointer bytes and unmutated models.
- **Verification Suite Expansion**: Updated all 20 production load calls across the test harness to the dual-authority overload and added comprehensive Check 9 covering fail-closed authority checks, tamper detection under load with recomputed hashes, and diagnostic interpolation safety.

### Scope Boundaries
- **Storage Infrastructure Only**: Provides durable, cryptographically verified checkpoint persistence, recovery, and ledger continuity for combined food and physical item state.
- **No Inventory Authority or Command Minting**: Does not convert scalar inventory into physical items, execute harvest/consume actions, adjust nutrition, mint migration items, or alter gameplay rules.
- **No Model Mutation**: All validations operate against detached instances or non-mutating query methods (`ItemModel.CanRestoreSnapshot`, `FoodModel.Valid`). Live models supplied to the repository remain strictly untouched.
- **Preserves Existing Nutrition and Caps**: Carried fruit (max 4), seeds (max 4), freshwater (2000 mL), actor carry mass (25 kg), and container volumes/masses remain identical to frozen baselines.

---

## 2. Component Layout

```
Assets/CityLife/Food/
  ├── FoodOwnershipCheckpoint.cs          // Data models, envelope, pointer, init record, result enums (including MissingAuthority)
  ├── FoodOwnershipCheckpointCodec.cs     // Canonical hashing, JSON encoding/decoding, non-mutating validation, ledger continuity
  ├── FoodOwnershipCheckpointRepository.cs// OS-exclusive locked repository, dual-authority load, initialization marker, atomic pointer replacement, recovery
  └── FoodOwnershipCheckpointChecks.cs    // 9 focused verification checks (authored for coordinator execution)
docs/
  └── FOOD-OWNERSHIP-CHECKPOINT.md        // This specification document
```

---

## 3. Data Envelope & Schema Specification

### 3.1 Checkpoint Envelope (`FoodOwnershipCheckpointEnvelope`)
- **Schema Identifier**: `starfall.food-ownership-checkpoint.v1`
- **Budget / Max Size**: `2,097,152` bytes (2 MB).
- **Ledger Capacity**: Up to `256` receipt records (`MaxReceiptLedgerSize`).

| Field | Type | Description |
|---|---|---|
| `schema` | `string` | Version tag (`starfall.food-ownership-checkpoint.v1`). |
| `worldId` | `string` | Shared world identifier (validated against `FoodModel.Id` and `ItemModel.WorldId`). |
| `actorId` | `string` | Shared inhabitant actor identifier (validated against `FoodModel.Id` and `ItemModel.ActorId`). |
| `foodGeneration` | `string` | Subsystem food generation (e.g. `combined-v1`). |
| `physicalGeneration` | `string` | Subsystem physical item generation (e.g. `gen-01`). |
| `sequence` | `long` | Monotonic checkpoint sequence (`>= 1`). |
| `previousCheckpointHash` | `string` | Empty string for sequence 1; 64-char lowercase hex SHA-256 for sequence `> 1`. |
| `requestHighWatermark` | `long` | Highest bridge transaction ID evaluated or committed (`>= 0`, monotonic). |
| `foodPayload` | `string` | Exact verbatim UTF-8 JSON text of `FoodState`. |
| `foodPayloadHash` | `string` | 64-char lowercase hex SHA-256 of `foodPayload`. |
| `physicalPayload` | `string` | Exact verbatim UTF-8 JSON text of `PhysicalSavePayload`. |
| `physicalPayloadHash` | `string` | 64-char lowercase hex SHA-256 of `physicalPayload`. |
| `receipts` | `List<FoodOwnershipReceiptRecord>` | Ordered ledger of recently evaluated bridge receipts. |
| `checkpointHash` | `string` | Canonical SHA-256 integrity hash of the envelope. |

### 3.2 Authoritative Commit Pointer (`FoodOwnershipPointer`)
- **Schema Identifier**: `starfall.food-ownership-pointer.v1`
- **Budget / Max Size**: `65,536` bytes (64 KB).
- **Pointer File Name**: `authoritative.pointer` (backup: `authoritative.pointer.bak`).

| Field | Type | Description |
|---|---|---|
| `schema` | `string` | Version tag (`starfall.food-ownership-pointer.v1`). |
| `worldId` | `string` | Bound world identifier. |
| `actorId` | `string` | Bound actor identifier. |
| `foodGeneration` | `string` | Bound food generation identifier. |
| `physicalGeneration` | `string` | Bound physical generation identifier. |
| `sequence` | `long` | Monotonic checkpoint sequence of current authoritative head. |
| `checkpointFilename` | `string` | Name of immutable checkpoint file on disk. |
| `checkpointHash` | `string` | Declared canonical hash of authoritative checkpoint file. |
| `previousCheckpointHash`| `string` | Checkpoint hash of predecessor (for diagnostic chain audits). |
| `pointerHash` | `string` | Canonical SHA-256 integrity hash of the pointer itself. |

### 3.3 Repository Initialization Record (`FoodOwnershipRepositoryInit`)
- **Schema Identifier**: `starfall.food-ownership-repository-init.v1`
- **Budget / Max Size**: `16,384` bytes (16 KB).
- **Init File Name**: `repository.init`.

| Field | Type | Description |
|---|---|---|
| `schema` | `string` | Version tag (`starfall.food-ownership-repository-init.v1`). |
| `worldId` | `string` | Bound world identifier. |
| `actorId` | `string` | Bound actor identifier. |
| `foodGeneration` | `string` | Bound food generation identifier. |
| `physicalGeneration` | `string` | Bound physical generation identifier. |
| `initializedUtcTicks` | `long` | UTC tick timestamp when repository was explicitly initialized. |
| `initHash` | `string` | Canonical SHA-256 integrity hash of the initialization record. |

### 3.4 Receipt Record (`FoodOwnershipReceiptRecord`)
- **Status Enum**: `Committed = 1`, `Rejected = 2`.
- Tracks bridge-local transaction identity independently of subsystem request numbers:
  - `transactionId`: Monotonically increasing 64-bit integer (`> 0`).
  - `requestSignature`: Action signature (e.g. `Gather:berry`).
  - `foodRequestId`: Independent legacy `FoodModel` request number (`-1` or `> 0`).
  - `itemRequestId`: Independent `ItemModel` request number (`-1` or `> 0`).
  - `status`: Distinct `Committed` or `Rejected` enum.
  - `statusCode`: Stable diagnostic result code (e.g. `ok`, `inventory-full`).
  - `checkpointSequence`: Sequence where the receipt was committed (`<= envelope.sequence`).
  - `payloadHash`: 64-char SHA-256 hash of receipt outcome payload.

---

## 4. Canonical Hashing & Tamper Detection

To prevent floating-point or serializer formatting drift, payloads and metadata are hashed deterministically:
1. `foodPayloadHash = SHA256(UTF8(foodPayload))`
2. `physicalPayloadHash = SHA256(UTF8(physicalPayload))`
3. `checkpointHash = SHA256(UTF8(CanonicalString))`
4. `pointerHash = SHA256(UTF8(CanonicalPointerString))`
5. `initHash = SHA256(UTF8(CanonicalInitString))`

Canonical envelope string formatting:
```
schema + '\n' +
worldId + '\n' +
actorId + '\n' +
foodGeneration + '\n' +
physicalGeneration + '\n' +
sequence (InvariantCulture) + '\n' +
previousCheckpointHash + '\n' +
requestHighWatermark (InvariantCulture) + '\n' +
foodPayloadHash + '\n' +
physicalPayloadHash + '\n' +
receipts[0].transactionId + ':' + receipts[0].requestSignature + ':' + ... + ';' + ... + '\n'
```

Any byte modified in payloads, receipts, sequence, or scope immediately invalidates either the payload checksum or canonical envelope checksum upon decode.

---

## 5. OS-Exclusive Locking & Publication Pipeline

### 5.1 Lock Scope
An OS-exclusive lock file (`.commit.lock`) is acquired via:
```csharp
new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
```
This stream is held open continuously across:
1. Verifying repository initialization marker (`repository.init`).
2. Reading current authoritative pointer.
3. Checking repository scope matching against scope configuration.
4. Pre-admission structural and semantic validation of the CURRENT authoritative pair on disk under lock.
5. Expected-sequence comparison (`candidate.sequence == currentSequence + 1`).
6. Previous checkpoint hash matching (`candidate.previousCheckpointHash == currentPointer.checkpointHash`).
7. Ledger continuity and anti-reissue validation (`ValidateSuccessorLedger`).
8. Structural and non-mutating semantic validation of the candidate checkpoint against both live authoritative models.
9. Writing and fsync-flushing temporary immutable checkpoint candidate file (`chk-...tmp-<guid>`).
10. Reading back and verifying written checkpoint file bytes.
11. Atomic publication of immutable checkpoint file (`File.Move`).
12. Writing and fsync-flushing temporary pointer file (`authoritative.pointer.tmp-<guid>`).
13. Atomic replacement of authoritative pointer (`File.Replace` if existing, `File.Move` if first creation).
14. Exception reconciliation and readback classification.
15. Acknowledgement and staging cleanup.

The lock stream is disposed only in the `finally` block after the commit outcome has been fully reconciled.

### 5.2 Explicit Repository Initialization & Ambiguity Resolution (Finding 6)
- **Initialization Requirement**: A repository must be explicitly initialized via `InitializeEmpty()` under `.commit.lock` before it can accept commits. This produces `repository.init`.
- **Uninitialized Rejection**: Committing to an uninitialized repository fails closed with `CheckpointCommitStatus.CorruptState`. Loading returns `CheckpointLoadStatus.Uninitialized`.
- **Ambiguous Artifact Rejection**: If `authoritative.pointer` is absent, the repository scans for ANY existing files or directories (including `authoritative.pointer.bak`, `chk-*.tmp*`, `.staging/`, `chk-*.json`, or unknown files). If any are found, the state is classified as `AmbiguousArtifactsWithoutPointer`. `InitializeEmpty()` refuses to overwrite ambiguous directories; explicit operator recovery is required.
- **Clean State**: `InitializeEmpty()` succeeds only on an empty directory or an existing matching `repository.init`.

### 5.3 Mandatory Non-Null Dual Models & Current Pair Validation (Findings 3 & 4)
- **Strict Dual Authority**: Both `authoritativeFoodModel` and `authoritativeItemModel` must be supplied (non-null) to `Commit()`. Passing null fails closed immediately with `CheckpointCommitStatus.ValidationFailed` without touching disk or reading state.
- **Pre-Admission Validation of Current Authoritative Pair**: Under the same commit lock, before deriving or admitting sequence `N+1`, the repository loads the current acknowledged checkpoint referenced by `authoritative.pointer` and validates it structurally and semantically against both live models. If the current checkpoint file is missing, truncated, unparseable, or semantically invalid, the commit fails closed with `CorruptState` without writing candidate artifacts.

### 5.4 Cross-Scope Commit Protection (Finding 2)
- If the repository path contains an authoritative pointer or `repository.init` belonging to a different `worldId`, `actorId`, `foodGeneration`, or `physicalGeneration`, `ReadAuthoritativePointerInternal` returns `ScopeMismatch`.
- `Commit()` immediately fails closed with `CheckpointCommitStatus.CorruptState` (or `ScopeMismatch`). It NEVER overwrites the foreign pointer, NEVER resets sequence to 0 or 1, and leaves the existing scope data intact on disk.

### 5.5 Ledger Monotonicity, Monotonic Watermark, and Anti-Reissue Invariants (Finding 5)
- **Monotonic Watermark**: Candidate `requestHighWatermark` must be `>= current.requestHighWatermark`. It can never regress.
- **Retained Receipt Immutability**: All receipt records present in both the current envelope and candidate envelope must match identically across all immutable fields (`requestSignature`, `foodRequestId`, `itemRequestId`, `status`, `statusCode`, `checkpointSequence`, `payloadHash`). Any alteration is rejected with `ValidationFailed`.
- **Bounded Eviction & Anti-Reissue**: When the receipt ledger reaches capacity (`MaxReceiptLedgerSize = 256`), oldest receipts may be evicted. However:
  - Any new receipt added to the ledger must have `transactionId > current.requestHighWatermark`.
  - Evicted transaction IDs (`transactionId <= current.requestHighWatermark` and no longer in the ledger) can NEVER be reissued.
  - New receipts must reference the candidate's sequence (`receipt.checkpointSequence == candidate.sequence`).

### 5.6 Atomic Replacement Reconciliation & Indeterminate Classification (Finding 1)
- When executing atomic pointer publication (`File.Replace` or `File.Move`):
  - **Success Path**: The call completes normally, candidate pointer is acknowledged, and staging is cleaned up (`CheckpointCommitStatus.Success`).
  - **Exception Path**: If `File.Replace` or `File.Move` throws an exception, the repository performs an immediate readback of `authoritative.pointer` under the held `.commit.lock`:
    1. If the pointer matches candidate bytes and hashes, publication succeeded despite the post-replacement exception: `CommittedPostCommitFailure`.
    2. If the pointer demonstrably matches the exact previous pointer text and hashes, publication failed cleanly: `NotCommitted`. `NotCommitted` is strictly reserved for verified identical OLD authoritative pointer bytes (`pointerExisted == true`).
    3. If the pointer file is missing, unreadable, locked by a sharing violation, has an unparseable schema/hash, or does not demonstrably match the previous pointer text: `Indeterminate`. Missing readback pointer after publication exception MUST ALWAYS mean `Indeterminate` (recovery required), including on first publication (`pointerExisted == false`). `File.Exists` absence is not proof of unchanged authority and can hide IO failure. The repository reports that operator or crash recovery is required and NEVER assumes uncommitted state.

### 5.7 Fail-Closed Recovery Guarantees
- **No Fallback on Corrupted Checkpoint**: If the newest pointer references a corrupted or truncated checkpoint file, loading returns `CorruptCheckpoint`. It never falls back to an older checkpoint.
- **No Fallback on Corrupted Pointer**: If `authoritative.pointer` is corrupt, loading returns `CorruptPointer`. It does not automatically load `authoritative.pointer.bak`.
- **No Staging Clutter**: Staging directories (`.staging/`) are cleaned up under lock, including directory removal if empty.

### 5.8 Authoritative Checkpoint Load Pipeline (Pass 4)
- **Dual Authority Requirement**: `LoadAuthoritativeCheckpoint(FoodModel authoritativeFoodModel, ItemModel authoritativeItemModel, out FoodOwnershipCheckpointEnvelope envelope)` requires both non-null models. Passing null immediately fails closed with `CheckpointLoadStatus.MissingAuthority`.
- **OS-Exclusive Locking**: Acquires and holds `.commit.lock` across initialization validation, pointer reading and decoding, scope verification, checkpoint envelope reading, structural validation, and semantic validation.
- **Under-Lock Validation & Non-Reentrant Helper**: To avoid lock recursion or deadlocks when called by `Commit`, the private helper `LoadAuthoritativeCheckpointUnderLock` accepts the already-read pointer and models under the existing lock.
- **Full Semantic Admission Under Load**: Loads do not rely merely on valid SHA-256 hashes on disk. The checkpoint payload is validated semantically against detached `FoodModel` (seed, actor, ecology, PlaceLedger) and `ItemModel` (`ItemPersistence.TryLoad` and `ItemModel.CanRestoreSnapshot`). If disk bytes were tampered with even alongside fully recomputed SHA-256 hashes, semantic validation fails closed with `CorruptCheckpoint`.
- **Fail-Closed Guarantees**: On ANY failure (corrupt checkpoint, corrupt pointer, scope mismatch, missing authority, lock contention), `envelope` and `result.Checkpoint` are strictly null. The repository NEVER falls back to an older checkpoint.
- **Legacy Overload Fail-Closed**: The parameterless overload `LoadAuthoritativeCheckpoint(out envelope)` is strictly disabled fail-closed, returning `CheckpointLoadStatus.MissingAuthority` with a diagnostic message and null envelope/checkpoint.
- **Diagnostic Pointer Inspection**: `GetAuthoritativePointer()` provides read-only metadata inspection and does not perform restore admission.

---

## 6. Model Validation Without Mutation

### 6.1 Reused Public Validators
- **Food Subsystem**:
  - `FoodModel.Valid(FoodState s, string world, string generation)`: Validates body physiology, fat, hydration, tick bounds, ecology drops/bushes, PlaceLedger cell coordinates, and LOS observation event chains.
  - `detachedFoodModel.Restore(string json, string world, string generation)`: Reused against a temporary detached `FoodModel` instance to validate actor ID, seed, and PlaceLedger without mutating the caller's live `FoodModel`.
- **Physical Item Subsystem**:
  - `ItemPersistence.TryLoad(string path, string world, string gen, string actor, ItemModel liveModel, out PhysicalSavePayload payload)`: Validates physical envelope checksum, schema versioning, v2 slot occupancy (`containerSlot >= 0`), item ID uniqueness, and position/rotation bounds. Executed using a private temporary staging file in `.staging/` that is cleaned up in a `finally` block.
  - `ItemModel.CanRestoreSnapshot(PhysicalSavePayload payload)`: Evaluates live definition catalog admission, mass match, dimensions match, container hierarchy, cycle freedom, slot capacities, contained volume limits, recursive contained mass limits, and actor carry limits without mutating the live `ItemModel`.

### 6.2 Semantic Dual Scope Matching
`ValidateEnvelopeSemantic` validates that `envelope.worldId`, `foodGeneration`, `physicalGeneration`, `actorId`, and random `seed` exactly match both the detached restored `FoodModel` and the live `ItemModel`. Mismatches are rejected.

---

## 7. Verification Suite (`FoodOwnershipCheckpointChecks`)

The 9 focused checks authored in `Assets/CityLife/Food/FoodOwnershipCheckpointChecks.cs`:

1. `chk1-roundtrip`: End-to-end commit and load roundtrip with populated `FoodModel` and `ItemModel`; asserts live models are unmutated and loaded checkpoint matches candidate.
2. `chk2-rejections`: Validates that invalid schemas, malformed JSON, mixed worlds, mixed generations, mixed actors, uncataloged item types, bad candidate seeds, bad candidate item scopes, and null model references are rejected with `ValidationFailed`. Validates that passing mismatched authority models (wrong food seed or wrong item world) to `Commit` evaluates the current sequence 1 checkpoint against the supplied authority and fails closed with `CorruptState`, preserving disk pointer bytes and leaving supplied models completely unmutated.
3. `chk3-ledger`: Validates ledger constraints: initial sequence 1 begins with watermark 0 and empty receipts; sequence 2 introduces transaction IDs 1 & 2 (high-watermark 5); duplicate transaction IDs, unordered entries, ledger size overflow (>256), watermark regression (5 to 4), status/signature/hash alteration of retained receipts, success receipts pointing to future checkpoints, and anti-reissue of evicted transaction IDs are strictly rejected.
4. `chk4-hashing-tamper`: Asserts deterministic canonical hashing across envelope, pointer, and init schemas; single-byte tamper detection across payloads (including compact decoded food payload and physical payload) and headers; and envelope size limit enforcement.
5. `chk5-fault-injection`: Injects failures across all commit pipeline stages: verifies demonstrably unchanged pointer replacement failure yields `NotCommitted`, readback IO fault yields `Indeterminate`, locked-file replacement yields `Indeterminate`, replacement exception with candidate readback yields `CommittedPostCommitFailure`, missing pointer yields `Indeterminate`, corrupt pointer yields `Indeterminate`, and first-publication missing and unreadable cases yield `Indeterminate`. Separately isolates fault scenarios (5f, 5g, 5h, 5i, 5i-corrupt, firstpub-missing, firstpub-unreadable) into clean repositories with correct seed checkpoints without backward pointer overwrites; asserts every intended fault hook ran; and independently reopens/reloads to verify authoritative disk state (old pointer unchanged, new pointer committed, or recovery required).
6. `chk6-corruption-fails-closed`: Verifies that corrupting the newest acknowledged checkpoint or pointer produces an explicit fail-closed result (`CorruptCheckpoint` / `CorruptPointer`) without fallback to older checkpoints or empty initialization. Verifies that commit fails closed when the current checkpoint file on disk is corrupted prior to admitting a successor.
7. `chk7-orphans-and-ambiguity`: Verifies orphan precommit staging files are ignored when an authoritative pointer exists; verifies that unpointed repositories with backup pointers, staging dirs, first candidate orphan tmp files, or unknown files fail closed as `AmbiguousArtifactsWithoutPointer` and cannot be initialized as empty.
8. `chk8-concurrency-and-collision`: Validates rejection of stale sequences, sequence gaps, previous hash mismatches, competing writers holding the OS lock (including during `InitializeEmpty`), cross-scope commit overwrites (preserving target pointer bytes), commit bypass on uninitialized directory, cross-scope init conflict, and identical-filename byte collisions.
9. `chk9-load-authoritative`: Authoritative load validation:
   - 9A: Deprecated no-authority overload fails closed with `MissingAuthority`, returning null envelope and null result checkpoint.
   - 9B & 9C: Null food or null item authority fails closed with `MissingAuthority`.
   - 9D: Wrong food authority model (seed 99999) fails closed with `CorruptCheckpoint`, leaving disk pointer and models unmutated.
   - 9E: Wrong item authority model (`foreign-world`) fails closed with `ScopeMismatch`, leaving disk pointer and models unmutated.
   - 9F - 9K: Tampered payloads with fully recomputed canonical hashes fail closed with `CorruptCheckpoint`, returning null envelope without falling back to older valid checkpoints (9F: seed 88888; 9G: satiety 99999 > 10000; 9H: PlaceLedger cell x = 99999 > 3334; 9I: uncataloged `plasma-chisel` item; 9J: dangling container reference; 9K: actor carry mass capacity 75 kg > 25 kg limit).
   - 9L & 9M: Diagnostic interpolation safety verifying sequence mismatch and hash mismatch branches capture envelope details before returning, executing cleanly without `NullReferenceException`.

---

## 8. Authoring & Verification State

> [!IMPORTANT]
> In accordance with the execution constraints of this authoring pass (direct file read/edit only; baseline 54 files frozen; no terminal commands; no Unity editor execution; no TaskAPI; no git; no network access), the verification suite in `Assets/CityLife/Food/FoodOwnershipCheckpointChecks.cs` is authored specifically for coordinator execution within the Unity test harness.
> The checks have not been executed during this authoring pass and remain in an unverified state until run by the designated coordinator.
