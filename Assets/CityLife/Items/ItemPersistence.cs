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
    /// Authoritative record of a single persisted physical item in Free or Carried state.
    /// Preserves stable item ID, type ID, location, actor holder, physical dimensions, mass, pose, and tick.
    /// Unity engine references (GameObject, Transform, Rigidbody) are never serialized.
    /// </summary>
    [Serializable]
    public sealed class SavedItemRecord
    {
        public string itemId;
        public string itemTypeId;
        public ItemLocationKind location;
        public string holderActorId;
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
    /// Contains only valid Free and Carried physical items and action replay receipts.
    /// </summary>
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
        public const string SchemaVersion = "starfall.physical-save.v1";
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
                items = new List<SavedItemRecord>(),
                receipts = new List<SavedReceiptRecord>()
            };

            var allItems = model.GetAllItemSnapshots();
            foreach (var snap in allItems)
            {
                // Only Free and Carried physical items are scoped for persistence in this slice
                if (snap.location != ItemLocationKind.Free && snap.location != ItemLocationKind.Carried)
                    continue;

                if (!model.TryGetDefinition(snap.itemTypeId, out var def))
                    continue;

                ItemDefinition.TryCanonicalizeRotation(snap.rotation, out var canonicalRot);

                var rec = new SavedItemRecord
                {
                    itemId = snap.itemId,
                    itemTypeId = snap.itemTypeId,
                    location = snap.location,
                    holderActorId = snap.holderActorId,
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

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string payloadJson = JsonUtility.ToJson(payload, false);
                if (Encoding.UTF8.GetByteCount(payloadJson) > MaxFileSizeBytes)
                    return false;

                string sha = ComputeSha256(payloadJson);
                var envelope = new PhysicalSaveEnvelope
                {
                    schema = SchemaVersion,
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
                if (envelope == null || envelope.schema != SchemaVersion ||
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

                // Items validation
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                int carriedCount = 0;
                float carriedMass = 0f;
                foreach (var it in candidate.items)
                {
                    if (it == null) return false;
                    if (!ItemModel.IsValidId(it.itemId)) return false;
                    if (!seenIds.Add(it.itemId)) return false; // Duplicate item ID
                    if (!ItemModel.IsValidId(it.itemTypeId)) return false;

                    if (liveModel != null)
                    {
                        if (!liveModel.TryGetDefinition(it.itemTypeId, out var liveDef)) return false;
                        if (liveDef.isAnchored) return false;

                        // Finite positive mass and exact match
                        if (!ItemDefinition.Finite(it.massKg) || it.massKg <= 0f || Mathf.Abs(it.massKg - liveDef.massKg) > 0.0001f) return false;

                        // Finite positive dimensions and exact match
                        if (!ItemDefinition.Finite(it.dimensions.width) || it.dimensions.width <= 0f ||
                            !ItemDefinition.Finite(it.dimensions.height) || it.dimensions.height <= 0f ||
                            !ItemDefinition.Finite(it.dimensions.depth) || it.dimensions.depth <= 0f)
                        {
                            return false;
                        }
                        if (Mathf.Abs(it.dimensions.width - liveDef.dimensions.width) > 0.0001f ||
                            Mathf.Abs(it.dimensions.height - liveDef.dimensions.height) > 0.0001f ||
                            Mathf.Abs(it.dimensions.depth - liveDef.dimensions.depth) > 0.0001f) return false;
                    }

                    if (!ItemDefinition.Finite(it.position.x) || !ItemDefinition.Finite(it.position.y) || !ItemDefinition.Finite(it.position.z)) return false;
                    if (!ItemDefinition.TryCanonicalizeRotation(it.rotation, out var canonRot)) return false;
                    it.rotation = canonRot;

                    // Reject negative or future lastUpdatedTick
                    if (it.lastUpdatedTick < 0 || it.lastUpdatedTick > candidate.tick) return false;

                    if (it.location == ItemLocationKind.Free)
                    {
                        if (!string.IsNullOrEmpty(it.holderActorId)) return false;
                    }
                    else if (it.location == ItemLocationKind.Carried)
                    {
                        if (!string.Equals(it.holderActorId, expectedActor, StringComparison.Ordinal)) return false;
                        carriedCount++;
                        carriedMass += it.massKg;
                    }
                    else
                    {
                        // Unsupported state in this slice
                        return false;
                    }
                }

                var limits = liveModel != null ? liveModel.GetActorCarryLimits(expectedActor) : new ActorCarryLimits(25f, 1);
                if (carriedCount > limits.maxCarriedItems) return false;
                if (carriedMass > limits.maxCarryMassKg) return false;

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

                payload = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

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
    }
}
