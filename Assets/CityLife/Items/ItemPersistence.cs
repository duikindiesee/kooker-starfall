using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using CityLife.World;

namespace CityLife.Items
{
    /// <summary>
    /// File envelope containing schema identifier, JSON payload, and SHA-256 integrity hash.
    /// Follows the durable snapshot patterns established in FoodModel and EnvironmentModel.
    /// </summary>
    [Serializable]
    public sealed class PhysicalSaveEnvelope
    {
        public string schema = ItemPersistence.SchemaVersion;
        public string payload;
        public string sha256;
    }

    /// <summary>
    /// Authoritative record of a single persisted physical item in Free, Carried, or Stored state.
    /// Preserves stable item ID, type ID, location, actor holder, container item ID, physical dimensions, mass, pose, and tick.
    /// Unity engine references (GameObject, Transform, Rigidbody) are never serialized.
    /// </summary>
    [Serializable]
    public sealed class SavedItemRecord
    {
        public string itemId;
        public string itemTypeId;
        public ItemLocationKind location;
        public string holderActorId;
        public string containerItemId;
        public int containerSlot = -1;
        public float massKg;
        public PhysicalDimensions dimensions;
        public Vector3 position;
        public Quaternion rotation;
        public long lastUpdatedTick;
    }

    /// <summary>
    /// Persisted action receipt record preserving semantic signature and idempotent replay identity.
    /// </summary>
    [Serializable]
    public sealed class SavedReceiptRecord
    {
        public int requestId;
        public string signature;
        public ItemReceipt receipt;
    }

    /// <summary>
    /// Authoritative snapshot payload scoped to a specific world, generation, and inhabitant actor.
    /// Contains valid Free, Carried, and Stored physical items and action replay receipts.
    /// </summary>
    [Serializable]
    public sealed class PhysicalSavePayload
    {
        public string worldId;
        public string generationId;
        public string actorId;
        public long tick;
        public bool isManaged = false;
        public long issuanceHighWatermark = 0;
        public List<SavedItemRecord> items = new List<SavedItemRecord>();
        public List<SavedReceiptRecord> receipts = new List<SavedReceiptRecord>();
        public List<TombstoneRecord> tombstones = new List<TombstoneRecord>();
        public List<SavedMaterialBatchReceiptRecord> materialReceipts = new List<SavedMaterialBatchReceiptRecord>();
    }

    /// <summary>
    /// Explicit runtime binding connecting a stable itemId to its live scene components.
    /// Eliminates scene-wide reflection or guessed identity by requiring an authoritative mapping.
    /// </summary>
    [Serializable]
    public sealed class PhysicalItemRuntimeBinding
    {
        public string itemId;
        public PhysicalItem physicalItem;
        public NpcInteractable interactable;

        public PhysicalItemRuntimeBinding() { }

        public PhysicalItemRuntimeBinding(PhysicalItem physicalItem, NpcInteractable interactable = null)
        {
            this.physicalItem = physicalItem;
            this.itemId = physicalItem != null ? physicalItem.itemId : null;
            this.interactable = interactable != null ? interactable : (physicalItem != null ? physicalItem.GetComponent<NpcInteractable>() : null);
        }

        public PhysicalItemRuntimeBinding(string itemId, PhysicalItem physicalItem, NpcInteractable interactable = null)
        {
            this.itemId = itemId;
            this.physicalItem = physicalItem;
            this.interactable = interactable != null ? interactable : (physicalItem != null ? physicalItem.GetComponent<NpcInteractable>() : null);
        }
    }

    /// <summary>
    /// Durable atomic snapshot persistence utility for physical items.
    /// Guarantees:
    /// 1. Atomic durable writes via temporary files, fsync flush, and atomic replacement without deleting prior saves.
    /// 2. Atomic rejection: malformed JSON, checksum failure, foreign world/generation/actor, duplicate IDs,
    ///    non-finite coordinates, unsupported states, or live definition mismatch reject without mutating live state.
    /// 3. Replay safety: preserves action request receipts across restart to prevent duplicate re-execution or conflicts.
    /// 4. Scoped runtime restoration: binds Carried items into the authoritative hand using scale-neutral follower,
    ///    and Free items with dynamic physics, normal gravity, and solid colliders.
    /// </summary>
    public static class ItemPersistence
    {
        public const string SchemaVersion = "starfall.physical-save.v2";
        public const string LegacySchemaVersion = "starfall.physical-save.v1";
        public const string ManagedSchemaVersion = "starfall.physical-save.v3";
        public const int MaxFileSizeBytes = 1024 * 1024; // 1 MB envelope budget

        public static string ComputeSha256(string text)
        {
            if (text == null) return "";
            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(64);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public static PhysicalSavePayload CreateSnapshot(ItemModel model, string actorId)
        {
            if (model == null || !ItemModel.IsValidId(actorId))
                throw new ArgumentException("Valid model and actorId are required to create snapshot.");

            var payload = new PhysicalSavePayload
            {
                worldId = model.WorldId,
                generationId = model.GenerationId,
                actorId = actorId,
                tick = model.Tick,
                isManaged = model.IsManaged,
                issuanceHighWatermark = model.IssuanceHighWatermark,
                items = new List<SavedItemRecord>(),
                receipts = new List<SavedReceiptRecord>(),
                tombstones = model.GetAllTombstones(),
                materialReceipts = model.GetAllMaterialReceipts()
            };

            var allItems = model.GetAllItemSnapshots();
            foreach (var snap in allItems)
            {
                // Only Free, Carried, and Stored physical items are scoped for persistence
                if (snap.location != ItemLocationKind.Free &&
                    snap.location != ItemLocationKind.Carried &&
                    snap.location != ItemLocationKind.Stored)
                {
                    continue;
                }

                if (!model.TryGetDefinition(snap.itemTypeId, out var def))
                    continue;

                ItemDefinition.TryCanonicalizeRotation(snap.rotation, out var canonicalRot);

                var rec = new SavedItemRecord
                {
                    itemId = snap.itemId,
                    itemTypeId = snap.itemTypeId,
                    location = snap.location,
                    holderActorId = snap.holderActorId,
                    containerItemId = snap.containerItemId,
                    containerSlot = snap.containerSlot,
                    massKg = def.massKg,
                    dimensions = def.dimensions,
                    position = snap.position,
                    rotation = canonicalRot,
                    lastUpdatedTick = snap.lastUpdatedTick
                };
                payload.items.Add(rec);
            }

            var allReceipts = model.GetAllReceiptRecords();
            foreach (var r in allReceipts)
            {
                payload.receipts.Add(new SavedReceiptRecord
                {
                    requestId = r.requestId,
                    signature = r.signature,
                    receipt = r.receipt
                });
            }

            return payload;
        }

        public static bool SaveAtomic(string path, PhysicalSavePayload payload)
        {
            if (string.IsNullOrEmpty(path) || payload == null) return false;
            if (!Path.IsPathRooted(path)) return false; // Require explicit absolute path

            // Bounded payload validation before serialization
            if (!ItemModel.IsValidId(payload.worldId) || !ItemModel.IsValidId(payload.generationId) || !ItemModel.IsValidId(payload.actorId))
                return false;
            if (payload.tick < 0)
                return false;
            if (payload.items == null || payload.items.Count > 1000)
                return false;
            if (payload.receipts != null && payload.receipts.Count > ItemModel.MaxReceiptLedgerSize)
                return false;
            if (payload.tombstones != null && payload.tombstones.Count > ItemModel.MaxTombstoneLedgerSize)
                return false;
            if (payload.materialReceipts != null && payload.materialReceipts.Count > ItemModel.MaxMaterialReceiptLedgerSize)
                return false;
            if (payload.issuanceHighWatermark < 0)
                return false;

            if (!payload.isManaged)
            {
                if ((payload.tombstones != null && payload.tombstones.Count > 0) ||
                    (payload.materialReceipts != null && payload.materialReceipts.Count > 0) ||
                    payload.issuanceHighWatermark != 0)
                {
                    return false;
                }
            }

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                PhysicalSavePayload canonicalCopy = CanonicalizePayloadForSave(payload);

                string payloadJson = JsonUtility.ToJson(canonicalCopy, false);
                if (Encoding.UTF8.GetByteCount(payloadJson) > MaxFileSizeBytes)
                    return false;

                string sha = ComputeSha256(payloadJson);
                var envelope = new PhysicalSaveEnvelope
                {
                    schema = canonicalCopy.isManaged ? ManagedSchemaVersion : SchemaVersion,
                    payload = payloadJson,
                    sha256 = sha
                };
                string envelopeJson = JsonUtility.ToJson(envelope, true);
                if (Encoding.UTF8.GetByteCount(envelopeJson) > MaxFileSizeBytes)
                    return false;

                string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(envelopeJson);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(tmp, path, path + ".bak");
                }
                else
                {
                    File.Move(tmp, path);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Produces a detached deep copy of the save payload canonicalized for serialization under schema v2.
        /// Preserves caller payload, items, and receipts without mutation or alias sharing.
        /// Deterministically assigns slots 0..n-1 by ordinal item ID on the detached copy for any direct-child
        /// container group where EVERY stored slot is exactly -1 (legacy in-memory contract).
        /// Leaves already-assigned valid slots, holes, and independent nested containers untouched.
        /// Never normalizes mixed assigned/unassigned groups, negative values below -1, or non-stored items.
        /// </summary>
        private static PhysicalSavePayload CanonicalizePayloadForSave(PhysicalSavePayload source)
        {
            if (source == null) return null;

            var copy = new PhysicalSavePayload
            {
                worldId = source.worldId,
                generationId = source.generationId,
                actorId = source.actorId,
                tick = source.tick,
                isManaged = source.isManaged,
                issuanceHighWatermark = source.issuanceHighWatermark,
                items = source.items != null ? new List<SavedItemRecord>(source.items.Count) : new List<SavedItemRecord>(),
                receipts = source.receipts != null ? new List<SavedReceiptRecord>(source.receipts.Count) : null,
                tombstones = source.tombstones != null ? new List<TombstoneRecord>(source.tombstones.Count) : null,
                materialReceipts = source.materialReceipts != null ? new List<SavedMaterialBatchReceiptRecord>(source.materialReceipts.Count) : null
            };

            if (source.items != null)
            {
                for (int i = 0; i < source.items.Count; i++)
                {
                    var it = source.items[i];
                    if (it == null)
                    {
                        copy.items.Add(null);
                        continue;
                    }

                    copy.items.Add(new SavedItemRecord
                    {
                        itemId = it.itemId,
                        itemTypeId = it.itemTypeId,
                        location = it.location,
                        holderActorId = it.holderActorId,
                        containerItemId = it.containerItemId,
                        containerSlot = it.containerSlot,
                        massKg = it.massKg,
                        dimensions = it.dimensions,
                        position = it.position,
                        rotation = it.rotation,
                        lastUpdatedTick = it.lastUpdatedTick
                    });
                }
            }

            if (source.receipts != null)
            {
                for (int i = 0; i < source.receipts.Count; i++)
                {
                    var r = source.receipts[i];
                    if (r == null)
                    {
                        copy.receipts.Add(null);
                        continue;
                    }

                    copy.receipts.Add(new SavedReceiptRecord
                    {
                        requestId = r.requestId,
                        signature = r.signature,
                        receipt = r.receipt
                    });
                }
            }

            if (source.tombstones != null)
            {
                for (int i = 0; i < source.tombstones.Count; i++)
                {
                    copy.tombstones.Add(source.tombstones[i]);
                }
            }

            if (source.materialReceipts != null)
            {
                for (int i = 0; i < source.materialReceipts.Count; i++)
                {
                    var mr = source.materialReceipts[i];
                    if (mr == null)
                    {
                        copy.materialReceipts.Add(null);
                        continue;
                    }

                    copy.materialReceipts.Add(new SavedMaterialBatchReceiptRecord
                    {
                        requestId = mr.requestId,
                        signature = mr.signature,
                        receipt = mr.receipt
                    });
                }
            }

            // Group direct stored children by containerItemId
            var containerGroups = new Dictionary<string, List<SavedItemRecord>>(StringComparer.Ordinal);
            for (int i = 0; i < copy.items.Count; i++)
            {
                var it = copy.items[i];
                if (it != null && it.location == ItemLocationKind.Stored && !string.IsNullOrEmpty(it.containerItemId))
                {
                    if (!containerGroups.TryGetValue(it.containerItemId, out var group))
                    {
                        group = new List<SavedItemRecord>();
                        containerGroups[it.containerItemId] = group;
                    }
                    group.Add(it);
                }
            }

            foreach (var kvp in containerGroups)
            {
                var children = kvp.Value;
                if (children == null || children.Count == 0) continue;

                bool allExactlyMinusOne = true;
                for (int i = 0; i < children.Count; i++)
                {
                    if (children[i].containerSlot != -1)
                    {
                        allExactlyMinusOne = false;
                        break;
                    }
                }

                if (allExactlyMinusOne)
                {
                    var sorted = new List<SavedItemRecord>(children);
                    sorted.Sort((a, b) => string.CompareOrdinal(a.itemId, b.itemId));
                    for (int s = 0; s < sorted.Count; s++)
                    {
                        sorted[s].containerSlot = s;
                    }
                }
            }

            return copy;
        }

        /// <summary>
        /// Attempts to load and validate a physical save payload from disk.
        /// When liveModel is non-null, executes authoritative definition-backed graph, capacity, and carry validation.
        /// When liveModel is null, permits legacy payloads containing only Free and Carried records;
        /// any record with Stored location fails closed because containment requires full definition-backed validation.
        /// </summary>
        public static bool TryLoad(
            string path,
            string expectedWorld,
            string expectedGen,
            string expectedActor,
            ItemModel liveModel,
            out PhysicalSavePayload payload)
        {
            payload = null;
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || !File.Exists(path)) return false;

            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > MaxFileSizeBytes) return false;

                string text = File.ReadAllText(path);
                if (string.IsNullOrEmpty(text)) return false;

                var envelope = JsonUtility.FromJson<PhysicalSaveEnvelope>(text);
                if (envelope == null || (envelope.schema != SchemaVersion && envelope.schema != LegacySchemaVersion && envelope.schema != ManagedSchemaVersion) ||
                    string.IsNullOrEmpty(envelope.payload) || string.IsNullOrEmpty(envelope.sha256))
                {
                    return false;
                }

                string computedSha = ComputeSha256(envelope.payload);
                if (!string.Equals(computedSha, envelope.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return false; // Malformed / truncated checksum mismatch
                }

                var candidate = JsonUtility.FromJson<PhysicalSavePayload>(envelope.payload);
                if (candidate == null) return false;

                // Scope validation
                if (!string.Equals(candidate.worldId, expectedWorld, StringComparison.Ordinal)) return false;
                if (!string.Equals(candidate.generationId, expectedGen, StringComparison.Ordinal)) return false;
                if (!string.Equals(candidate.actorId, expectedActor, StringComparison.Ordinal)) return false;
                if (candidate.tick < 0) return false;
                if (candidate.items == null) return false;

                bool isManagedSchema = string.Equals(envelope.schema, ManagedSchemaVersion, StringComparison.Ordinal);
                bool isLegacy = string.Equals(envelope.schema, LegacySchemaVersion, StringComparison.Ordinal);

                if (isManagedSchema)
                {
                    if (!candidate.isManaged) return false;
                    if (candidate.tombstones == null || candidate.tombstones.Count > ItemModel.MaxTombstoneLedgerSize) return false;
                    if (candidate.materialReceipts == null || candidate.materialReceipts.Count > ItemModel.MaxMaterialReceiptLedgerSize) return false;
                    if (candidate.issuanceHighWatermark < 0) return false;

                    var diskTombstoneIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var t in candidate.tombstones)
                    {
                        if (string.IsNullOrEmpty(t.itemId) || !ItemModel.IsValidId(t.itemId)) return false;
                        if (!diskTombstoneIds.Add(t.itemId)) return false;
                        if (!ItemModel.IsValidId(t.itemTypeId)) return false;
                        if (t.retiredTick < 0 || t.retiredTick > candidate.tick) return false;
                        if (string.IsNullOrEmpty(t.reason) || string.IsNullOrEmpty(t.provenance)) return false;
                    }

                    var diskMatReceiptIds = new HashSet<int>();
                    foreach (var mr in candidate.materialReceipts)
                    {
                        if (mr == null || mr.requestId <= 0 || string.IsNullOrEmpty(mr.signature)) return false;
                        if (!diskMatReceiptIds.Add(mr.requestId)) return false;
                        if (mr.receipt.requestId != mr.requestId) return false;
                        if (!string.Equals(mr.receipt.worldId, expectedWorld, StringComparison.Ordinal)) return false;
                        if (!string.Equals(mr.receipt.generationId, expectedGen, StringComparison.Ordinal)) return false;
                        if (!string.Equals(mr.receipt.actorId, expectedActor, StringComparison.Ordinal)) return false;
                    }
                }
                else
                {
                    if (candidate.isManaged) return false;
                    if (candidate.tombstones != null && candidate.tombstones.Count > 0) return false;
                    if (candidate.materialReceipts != null && candidate.materialReceipts.Count > 0) return false;
                    if (candidate.issuanceHighWatermark != 0) return false;
                }

                // Deterministic migration for provenance-aware legacy v1 saves ONLY.
                // Strict v2 validation: missing or invalid slots in v2 on-disk saves must reject without mutation.
                // Validates checksums against original version before migration.
                // Leaves original file untouched on disk.
                if (!isLegacy)
                {
                    // Strict v2 check: every stored item must have valid containerSlot >= 0
                    foreach (var it in candidate.items)
                    {
                        if (it != null && it.location == ItemLocationKind.Stored && it.containerSlot < 0)
                        {
                            return false; // Malformed v2 disk save: stored item has unassigned slot
                        }
                    }
                }
                else
                {
                    var containerGroups = new Dictionary<string, List<SavedItemRecord>>(StringComparer.Ordinal);
                    foreach (var it in candidate.items)
                    {
                        if (it == null) continue;
                        if (it.location == ItemLocationKind.Stored && !string.IsNullOrEmpty(it.containerItemId))
                        {
                            if (!containerGroups.TryGetValue(it.containerItemId, out var list))
                            {
                                list = new List<SavedItemRecord>();
                                containerGroups[it.containerItemId] = list;
                            }
                            list.Add(it);
                        }
                        else
                        {
                            it.containerSlot = -1;
                        }
                    }

                    foreach (var kvp in containerGroups)
                    {
                        kvp.Value.Sort((a, b) => string.CompareOrdinal(a.itemId, b.itemId));
                        for (int i = 0; i < kvp.Value.Count; i++)
                        {
                            kvp.Value[i].containerSlot = i;
                        }
                    }
                }

                // Items validation and canonicalization
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                var candidateTombstoneSet = (isManagedSchema && candidate.tombstones != null)
                    ? new HashSet<string>(candidate.tombstones.ConvertAll(t => t.itemId), StringComparer.Ordinal)
                    : null;
                int fallbackCarriedCount = 0;
                float fallbackCarriedMass = 0f;
                foreach (var it in candidate.items)
                {
                    if (it == null) return false;
                    if (!ItemModel.IsValidId(it.itemId)) return false;
                    if (!seenIds.Add(it.itemId)) return false; // Duplicate item ID
                    if (candidateTombstoneSet != null && candidateTombstoneSet.Contains(it.itemId)) return false; // Resurrection of retired item rejected
                    if (liveModel != null && liveModel.IsRetired(it.itemId)) return false;
                    if (!ItemModel.IsValidId(it.itemTypeId)) return false;

                    if (!ItemDefinition.Finite(it.position.x) || !ItemDefinition.Finite(it.position.y) || !ItemDefinition.Finite(it.position.z)) return false;
                    if (!ItemDefinition.TryCanonicalizeRotation(it.rotation, out var canonRot)) return false;
                    it.rotation = canonRot;

                    // Reject negative or future lastUpdatedTick
                    if (it.lastUpdatedTick < 0 || it.lastUpdatedTick > candidate.tick) return false;

                    if (it.location == ItemLocationKind.Free)
                    {
                        if (!string.IsNullOrEmpty(it.holderActorId)) return false;
                        if (!string.IsNullOrEmpty(it.containerItemId)) return false;
                        if (it.containerSlot != -1) return false;
                    }
                    else if (it.location == ItemLocationKind.Carried)
                    {
                        if (!string.Equals(it.holderActorId, expectedActor, StringComparison.Ordinal)) return false;
                        if (!string.IsNullOrEmpty(it.containerItemId)) return false;
                        if (it.containerSlot != -1) return false;
                        fallbackCarriedCount++;
                        fallbackCarriedMass += it.massKg;
                    }
                    else if (it.location == ItemLocationKind.Stored)
                    {
                        // Stored records require full definition-backed validation against a live ItemModel.
                        // When liveModel is null, fail closed immediately.
                        if (liveModel == null) return false;

                        if (!string.IsNullOrEmpty(it.holderActorId)) return false;
                        if (string.IsNullOrEmpty(it.containerItemId) || !ItemModel.IsValidId(it.containerItemId)) return false;
                        if (string.Equals(it.containerItemId, it.itemId, StringComparison.Ordinal)) return false;
                        if (it.containerSlot < 0) return false;
                    }
                    else
                    {
                        // Unsupported state in this slice (Placed, Anchored, etc.)
                        return false;
                    }
                }

                if (liveModel != null)
                {
                    // Full graph, definition, capacity, carry limits, and receipts validation
                    if (!liveModel.CanRestoreSnapshot(candidate))
                        return false;
                }
                else
                {
                    // Fallback validation for legacy Free/Carried payloads without a live model.
                    // Note: Stored records fail closed above because full definition-backed validation is required.
                    var limits = new ActorCarryLimits(25f, 1);
                    if (fallbackCarriedCount > limits.maxCarriedItems) return false;
                    if (fallbackCarriedMass > limits.maxCarryMassKg) return false;

                    // Receipts validation
                    if (candidate.receipts != null)
                    {
                        if (candidate.receipts.Count > ItemModel.MaxReceiptLedgerSize) return false;
                        var seenReqs = new HashSet<int>();
                        foreach (var r in candidate.receipts)
                        {
                            if (r == null || r.requestId <= 0 || string.IsNullOrEmpty(r.signature)) return false;
                            if (!seenReqs.Add(r.requestId)) return false; // Duplicate receipt ID
                            if (r.receipt.requestId != r.requestId) return false;
                            if (!string.Equals(r.receipt.worldId, expectedWorld, StringComparison.Ordinal)) return false;
                            if (!string.Equals(r.receipt.generationId, expectedGen, StringComparison.Ordinal)) return false;
                            if (!string.Equals(r.receipt.actorId, expectedActor, StringComparison.Ordinal)) return false;
                            if (!ItemDefinition.Finite(r.receipt.totalCarriedMassKg) || r.receipt.totalCarriedMassKg < 0f) return false;
                        }
                    }
                }

                payload = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Scoped runtime restoration restricted strictly to the single demonstration item in Free or Carried state.
        /// Intentionally fails closed atomically if Stored items, multiple items, or non-Free/Carried states are present.
        /// Full runtime scene container binding and grid slot visualization are deferred to subsequent passes.
        /// </summary>
        public static bool RestoreRuntime(
            PhysicalSavePayload payload,
            ItemModel model,
            NpcActionApi actions,
            PhysicalItem demonstrationItem,
            NpcInteractable demonstrationInteractable)
        {
            if (payload == null || model == null || actions == null || demonstrationItem == null || demonstrationInteractable == null)
                return false;

            // API model identity, world scope, and actor identity preflight
            if (actions.PhysicalModel != model)
                return false;
            if (!string.Equals(actions.WorldId, payload.worldId, StringComparison.Ordinal))
                return false;
            if (!string.Equals(actions.AgentId, payload.actorId, StringComparison.Ordinal))
                return false;

            // Ensure supported single-item live model set before clearing state
            if (model.ItemCount != 1 || !model.TryGetItem(demonstrationItem.itemId, out _))
                return false;

            // Reject missing/extra/unknown item IDs atomically:
            // Supported live instance set in this slice is strictly the single demonstration item.
            if (payload.items == null || payload.items.Count != 1)
                return false;

            var demoRecord = payload.items[0];
            if (demoRecord == null)
                return false;

            if (!string.Equals(demoRecord.itemId, demonstrationItem.itemId, StringComparison.Ordinal) ||
                !string.Equals(demonstrationItem.itemId, demonstrationInteractable.StableId, StringComparison.Ordinal))
            {
                return false;
            }

            // Preflight live scene / GameObject validity
            if (demonstrationItem.gameObject != demonstrationInteractable.gameObject)
                return false;
            if (!demonstrationItem.gameObject.scene.IsValid() ||
                actions.ActorTransform == null ||
                demonstrationItem.gameObject.scene != actions.ActorTransform.gameObject.scene)
            {
                return false;
            }
            if (demonstrationInteractable.Kind != NpcObjectKind.Item)
                return false;
            if (!string.Equals(demonstrationInteractable.WorldId, payload.worldId, StringComparison.Ordinal))
                return false;
            if (!actions.IsObjectRegistered(demonstrationInteractable.StableId, demonstrationInteractable))
                return false;

            // Preflight physical item component
            if (!demonstrationItem.IsValid())
                return false;
            if (!demonstrationItem.IsBoundTo(model, payload.worldId, model.GenerationId))
                return false;

            // Preflight location-specific prerequisites and reject unrelated actions.Held for Free as well as Carried
            if (demoRecord.location == ItemLocationKind.Carried)
            {
                if (actions.HandTransform == null || actions.HandTransform.gameObject.scene != actions.ActorTransform.gameObject.scene)
                    return false;
                if (!string.Equals(demoRecord.holderActorId, actions.AgentId, StringComparison.Ordinal))
                    return false;
                if (actions.Held != null && actions.Held != demonstrationInteractable)
                    return false;
            }
            else if (demoRecord.location == ItemLocationKind.Free)
            {
                if (!string.IsNullOrEmpty(demoRecord.holderActorId))
                    return false;
                if (actions.Held != null)
                    return false;
            }
            else
            {
                return false;
            }

            // Preflight model restoration without mutating model state
            if (!model.CanRestoreSnapshot(payload))
                return false;

            // ALL PREFLIGHT GUARDS PASSED. Perform model restoration.
            if (!model.RestoreSnapshot(payload))
                return false;

            // Apply live scene/physics changes
            if (demoRecord.location == ItemLocationKind.Carried)
            {
                demonstrationItem.AttachToHand(actions.HandTransform);
                demonstrationInteractable.HeldBy = payload.actorId;
                if (!actions.RestoreHeld(demonstrationInteractable))
                {
                    return false;
                }
            }
            else if (demoRecord.location == ItemLocationKind.Free)
            {
                demonstrationItem.ReleaseToPhysics(demoRecord.position, demoRecord.rotation);
                demonstrationInteractable.HeldBy = "";
                if (!actions.RestoreHeld(null))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Authoritative multi-item runtime restoration for containment graphs containing Free, Carried, and Stored items.
        /// Atomically validates the entire payload, authoritative ItemModel, and live binding set before mutating ANY state.
        /// Rejects missing/extra/duplicate bindings, cross-world/generation mismatches, multiple carried roots,
        /// capacity/carry overflows, or invalid physical components atomically.
        /// Stored descendants are rendered hidden, colliders disabled, and rigidbodies kinematic without spawning duplicate bodies.
        /// Repeated restorations accept valid already-stored bindings, preserve stable identities, and do not accumulate mass.
        /// </summary>
        public static bool RestoreRuntime(
            PhysicalSavePayload payload,
            ItemModel model,
            NpcActionApi actions,
            IEnumerable<PhysicalItemRuntimeBinding> bindings)
        {
            if (payload == null || model == null || actions == null || bindings == null)
                return false;

            // API model identity, world scope, and actor identity preflight
            if (actions.PhysicalModel != model)
                return false;
            if (!string.Equals(actions.WorldId, payload.worldId, StringComparison.Ordinal))
                return false;
            if (!string.Equals(actions.AgentId, payload.actorId, StringComparison.Ordinal))
                return false;
            if (!string.Equals(model.WorldId, payload.worldId, StringComparison.Ordinal))
                return false;
            if (!string.Equals(model.GenerationId, payload.generationId, StringComparison.Ordinal))
                return false;
            if (actions.ActorTransform == null || !actions.ActorTransform.gameObject.scene.IsValid())
                return false;

            if (payload.items == null)
                return false;

            // Build and strictly validate binding set (no duplicates, valid IDs, non-null items/GameObjects, unique bodies/colliders)
            var bindingMap = new Dictionary<string, PhysicalItemRuntimeBinding>(StringComparer.Ordinal);
            var seenGo = new HashSet<GameObject>();
            var seenPhys = new HashSet<PhysicalItem>();
            var seenNi = new HashSet<NpcInteractable>();
            var seenBodies = new HashSet<Rigidbody>();
            var seenColliders = new HashSet<Collider>();

            foreach (var b in bindings)
            {
                if (b == null || b.physicalItem == null) return false;
                string id = !string.IsNullOrEmpty(b.itemId) ? b.itemId : b.physicalItem.itemId;
                if (string.IsNullOrEmpty(id) || !ItemModel.IsValidId(id)) return false;
                if (b.itemId != null && !string.Equals(b.itemId, b.physicalItem.itemId, StringComparison.Ordinal)) return false;

                if (bindingMap.ContainsKey(id)) return false; // Duplicate binding ID
                if (!seenPhys.Add(b.physicalItem)) return false; // Duplicate PhysicalItem instance
                if (b.physicalItem.gameObject == null) return false;
                if (!seenGo.Add(b.physicalItem.gameObject)) return false; // Duplicate GameObject

                var p = b.physicalItem;
                if (p.Body == null || p.ItemCollider == null) return false;
                if (p.Body.gameObject != p.gameObject) return false;
                if (p.ItemCollider.gameObject != p.gameObject) return false;
                if (p.GetComponent<Rigidbody>() != p.Body) return false;
                var cols = p.GetComponents<Collider>();
                if (cols.Length != 1 || cols[0] != p.ItemCollider) return false;
                if (!seenBodies.Add(p.Body)) return false; // Duplicate / cross-wired Rigidbody across bindings
                if (!seenColliders.Add(p.ItemCollider)) return false; // Duplicate / cross-wired Collider across bindings

                if (b.interactable != null)
                {
                    if (b.interactable.gameObject != b.physicalItem.gameObject) return false;
                    if (!string.Equals(b.interactable.StableId, id, StringComparison.Ordinal)) return false;
                    if (!seenNi.Add(b.interactable)) return false; // Duplicate interactable
                }

                bindingMap.Add(id, b);
            }

            // Exactly matching counts: no missing and no extra bindings permitted
            if (bindingMap.Count != payload.items.Count)
                return false;

            // Complete live-item set & empty contract:
            // If the live model already contains items, an empty payload/binding set cannot wipe it out (rejects empty replacement).
            // Furthermore, every existing live item in the model must be accounted for in bindings and payload (rejects omitted live item).
            if (model.ItemCount > 0)
            {
                if (payload.items.Count == 0)
                    return false; // Reject empty replacement of non-empty model before mutation

                if (model.ItemCount != bindingMap.Count)
                    return false; // Reject omitted live item (counts must match)

                foreach (var liveSnap in model.GetAllItemSnapshots())
                {
                    if (!bindingMap.TryGetValue(liveSnap.itemId, out var liveBinding))
                        return false; // Missing live item in bindings
                    if (!string.Equals(liveBinding.physicalItem.itemTypeId, liveSnap.itemTypeId, StringComparison.Ordinal))
                        return false; // Item type mismatch between live model and binding
                }
            }

            // Preflight payload items mapping & identify carried root
            var payloadItemMap = new Dictionary<string, SavedItemRecord>(StringComparer.Ordinal);
            SavedItemRecord carriedRecord = null;
            PhysicalItemRuntimeBinding carriedBinding = null;

            foreach (var rec in payload.items)
            {
                if (rec == null || string.IsNullOrEmpty(rec.itemId)) return false;
                if (!bindingMap.TryGetValue(rec.itemId, out var b)) return false; // Missing binding
                if (payloadItemMap.ContainsKey(rec.itemId)) return false; // Duplicate in payload
                payloadItemMap.Add(rec.itemId, rec);

                if (rec.location == ItemLocationKind.Carried)
                {
                    // Existing NpcActionApi has one Held: reject unrepresentable multiple carried roots before mutation
                    if (carriedRecord != null) return false;
                    carriedRecord = rec;
                    carriedBinding = b;
                }
            }

            // Carried root preflight
            if (carriedRecord != null)
            {
                if (actions.HandTransform == null || actions.HandTransform.gameObject.scene != actions.ActorTransform.gameObject.scene)
                    return false;
                if (!string.Equals(carriedRecord.holderActorId, actions.AgentId, StringComparison.Ordinal))
                    return false;
                if (carriedBinding.interactable == null)
                    return false; // Carried root must have interactable for hand ownership
            }

            // Preflight actions.Held: if actor is holding something, it must be part of the registered binding set
            if (actions.Held != null)
            {
                if (!actions.IsObjectRegistered(actions.Held.StableId, actions.Held))
                    return false;
                if (!bindingMap.ContainsKey(actions.Held.StableId))
                    return false; // Unrelated held object
            }

            // Preflight each item's live physical component, definition conformance, and scene integrity
            foreach (var rec in payload.items)
            {
                var b = bindingMap[rec.itemId];
                var phys = b.physicalItem;

                if (phys.gameObject.scene != actions.ActorTransform.gameObject.scene) return false;
                if (!phys.gameObject.scene.IsValid()) return false;
                if (phys.isAnchored) return false;
                if (!string.Equals(phys.itemId, rec.itemId, StringComparison.Ordinal)) return false;
                if (!string.Equals(phys.itemTypeId, rec.itemTypeId, StringComparison.Ordinal)) return false;

                if (!model.TryGetDefinition(rec.itemTypeId, out var def)) return false;
                if (def.isAnchored) return false;

                // Mass and dimensions check within 0.0001f
                if (Mathf.Abs(phys.massKg - def.massKg) > 0.0001f || Mathf.Abs(rec.massKg - def.massKg) > 0.0001f) return false;
                if (Mathf.Abs(phys.dimensions.width - def.dimensions.width) > 0.0001f ||
                    Mathf.Abs(phys.dimensions.height - def.dimensions.height) > 0.0001f ||
                    Mathf.Abs(phys.dimensions.depth - def.dimensions.depth) > 0.0001f) return false;
                if (Mathf.Abs(rec.dimensions.width - def.dimensions.width) > 0.0001f ||
                    Mathf.Abs(rec.dimensions.height - def.dimensions.height) > 0.0001f ||
                    Mathf.Abs(rec.dimensions.depth - def.dimensions.depth) > 0.0001f) return false;

                // Finite pose check on both live transform and payload record
                if (!ItemDefinition.Finite(phys.transform.position.x) || !ItemDefinition.Finite(phys.transform.position.y) || !ItemDefinition.Finite(phys.transform.position.z)) return false;
                if (!ItemDefinition.Finite(phys.transform.rotation.x) || !ItemDefinition.Finite(phys.transform.rotation.y) || !ItemDefinition.Finite(phys.transform.rotation.z) || !ItemDefinition.Finite(phys.transform.rotation.w)) return false;
                if (!ItemDefinition.Finite(rec.position.x) || !ItemDefinition.Finite(rec.position.y) || !ItemDefinition.Finite(rec.position.z)) return false;
                if (!ItemDefinition.TryCanonicalizeRotation(rec.rotation, out _)) return false;

                // Component and body mass check
                if (phys.Body == null || phys.ItemCollider == null) return false;
                if (!ItemDefinition.Finite(phys.Body.mass) || phys.Body.mass <= 0f || Mathf.Abs(phys.Body.mass - def.massKg) > 0.0001f) return false;

                // Supported single collider check
                var cols = phys.GetComponents<Collider>();
                if (cols.Length != 1) return false;

                if (phys.ItemCollider is BoxCollider box)
                {
                    if (box.size.x <= 0f || box.size.y <= 0f || box.size.z <= 0f) return false;
                }
                else if (phys.ItemCollider is SphereCollider sphere)
                {
                    if (sphere.radius <= 0f) return false;
                }
                else if (phys.ItemCollider is CapsuleCollider capsule)
                {
                    if (capsule.radius <= 0f || capsule.height <= 0f) return false;
                }
                else if (phys.ItemCollider is MeshCollider mesh)
                {
                    if (!mesh.convex) return false;
                }
                else
                {
                    return false;
                }

                // Scale check
                Vector3 scale = phys.transform.lossyScale;
                if (!ItemDefinition.Finite(scale.x) || !ItemDefinition.Finite(scale.y) || !ItemDefinition.Finite(scale.z))
                    return false;
                if (Mathf.Abs(scale.x - 1f) > 0.001f || Mathf.Abs(scale.y - 1f) > 0.001f || Mathf.Abs(scale.z - 1f) > 0.001f)
                    return false;

                // Validate full physical component (checks own GameObject, coherent active/stored flags, scale, single supported collider)
                if (!phys.IsValid()) return false;

                // Model binding check
                if (!phys.IsBoundTo(model, payload.worldId, model.GenerationId))
                    return false;

                // Preflight interactable if present
                if (b.interactable != null)
                {
                    if (b.interactable.Kind != NpcObjectKind.Item) return false;
                    if (!string.Equals(b.interactable.WorldId, payload.worldId, StringComparison.Ordinal)) return false;
                    if (b.interactable.gameObject.scene != actions.ActorTransform.gameObject.scene) return false;
                    if (!actions.IsObjectRegistered(b.interactable.StableId, b.interactable)) return false;
                }

                // Location-specific preflight
                if (rec.location == ItemLocationKind.Stored)
                {
                    if (!string.IsNullOrEmpty(rec.holderActorId)) return false;
                    if (string.IsNullOrEmpty(rec.containerItemId)) return false;
                    if (!bindingMap.TryGetValue(rec.containerItemId, out var parentB) || parentB.physicalItem == null) return false;
                }
                else if (rec.location == ItemLocationKind.Free)
                {
                    if (!string.IsNullOrEmpty(rec.holderActorId)) return false;
                    if (!string.IsNullOrEmpty(rec.containerItemId)) return false;
                }
                else if (rec.location == ItemLocationKind.Carried)
                {
                    if (!string.IsNullOrEmpty(rec.containerItemId)) return false;
                    if (!string.Equals(rec.holderActorId, actions.AgentId, StringComparison.Ordinal)) return false;
                }
                else
                {
                    return false; // Unsupported state (Placed, Anchored)
                }
            }

            // Pure query preflight of containment graph, capacities, and receipts against authoritative model
            if (!model.CanRestoreSnapshot(payload))
                return false;

            // Preflight RestoreHeld requirements for carried root
            if (carriedBinding != null)
            {
                if (carriedBinding.interactable == null ||
                    carriedBinding.interactable.Kind != NpcObjectKind.Item ||
                    !actions.IsObjectRegistered(carriedBinding.interactable.StableId, carriedBinding.interactable) ||
                    !string.Equals(carriedBinding.interactable.WorldId, actions.WorldId, StringComparison.Ordinal) ||
                    carriedBinding.interactable.gameObject.scene != actions.ActorTransform.gameObject.scene)
                {
                    return false;
                }
            }

            // ALL PREFLIGHT GUARDS PASSED. Zero state was mutated during preflight.
            // Apply authoritative model restoration
            if (!model.RestoreSnapshot(payload))
                return false;

            // Topological order for scene hierarchy: roots (Free / Carried) first, then Stored by depth
            var depths = new Dictionary<string, int>(StringComparer.Ordinal);
            int GetDepth(string itemId)
            {
                if (depths.TryGetValue(itemId, out int d)) return d;
                var itemRec = payloadItemMap[itemId];
                if (itemRec.location != ItemLocationKind.Stored || string.IsNullOrEmpty(itemRec.containerItemId))
                {
                    depths[itemId] = 0;
                    return 0;
                }
                int pDepth = GetDepth(itemRec.containerItemId);
                depths[itemId] = pDepth + 1;
                return pDepth + 1;
            }

            foreach (var rec in payload.items)
            {
                GetDepth(rec.itemId);
            }

            var sortedItems = new List<SavedItemRecord>(payload.items);
            sortedItems.Sort((a, b) => depths[a.itemId].CompareTo(depths[b.itemId]));

            // Apply live scene / physics / visual state
            foreach (var rec in sortedItems)
            {
                var b = bindingMap[rec.itemId];
                var phys = b.physicalItem;

                if (rec.location == ItemLocationKind.Carried)
                {
                    phys.AttachToHand(actions.HandTransform);
                    if (b.interactable != null) b.interactable.HeldBy = payload.actorId;
                }
                else if (rec.location == ItemLocationKind.Free)
                {
                    phys.ReleaseToPhysics(rec.position, rec.rotation);
                    if (b.interactable != null) b.interactable.HeldBy = "";
                }
                else if (rec.location == ItemLocationKind.Stored)
                {
                    var parentBinding = bindingMap[rec.containerItemId];
                    phys.ApplyStored(parentBinding.physicalItem.transform, rec.containerItemId);
                    if (b.interactable != null) b.interactable.HeldBy = "";
                }
            }

            // Restore authoritative hand ownership on actions API
            if (carriedBinding != null)
            {
                if (!actions.RestoreHeld(carriedBinding.interactable))
                {
                    return false;
                }
            }
            else
            {
                if (!actions.RestoreHeld(null))
                {
                    return false;
                }
            }

            Physics.SyncTransforms();
            return true;
        }

        public static bool RestoreRuntime(
            PhysicalSavePayload payload,
            ItemModel model,
            NpcActionApi actions,
            IEnumerable<PhysicalItem> physicalItems)
        {
            if (physicalItems == null) return false;
            var list = new List<PhysicalItemRuntimeBinding>();
            foreach (var pi in physicalItems)
            {
                if (pi == null) return false;
                list.Add(new PhysicalItemRuntimeBinding(pi));
            }
            return RestoreRuntime(payload, model, actions, list);
        }
    }
}
