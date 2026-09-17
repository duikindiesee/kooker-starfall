using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace CityLife.Items
{
    public enum ItemActionKind
    {
        Pickup,
        Drop,
        Place,
        Store,
        Retrieve
    }

    [Serializable]
    public struct ItemActionRequest
    {
        public int requestId;
        public ItemActionKind action;
        public string actorId;
        public string itemId;
        public string targetId;            // containerItemId for Store/Retrieve, supportId for Place
        public Vector3 position;          // intent drop position or placement position
        public Quaternion rotation;        // placement or drop orientation
        public Vector3 supportNormal;      // surface normal for Place
    }

    [Serializable]
    public struct ItemReceipt
    {
        public int requestId;
        public string worldId;
        public string generationId;
        public string actorId;
        public ItemActionKind action;
        public string itemId;
        public string targetId;
        public bool success;
        public bool duplicate;
        public string code;
        public float totalCarriedMassKg;   // updated carried mass of the actor
    }

    [Serializable]
    public struct ActorCarryLimits
    {
        public float maxCarryMassKg;
        public int maxCarriedItems;

        public ActorCarryLimits(float maxCarryMassKg = 25f, int maxCarriedItems = 1)
        {
            this.maxCarryMassKg = maxCarryMassKg;
            this.maxCarriedItems = maxCarriedItems;
        }

        public bool IsValid() => ItemDefinition.Finite(maxCarryMassKg) && maxCarryMassKg > 0f && maxCarriedItems > 0 && maxCarriedItems <= 10;
    }

    /// <summary>
    /// Trusted adapter authority boundary for container access and placement verification.
    /// Scene/reach/LOS/collider checks belong to the adapter, not untrusted model proposals.
    /// </summary>
    public interface IItemActionAuthority
    {
        bool AuthorizeContainerAccess(string actorId, string containerItemId, string containerOwnerActorId);
        bool AuthorizePlacement(
            string worldId,
            string generationId,
            string actorId,
            string itemId,
            string targetSupportId,
            Vector3 position,
            Quaternion rotation,
            Vector3 supportNormal);
    }

    /// <summary>
    /// Reference implementation of IItemActionAuthority for tests and simple policy wiring.
    /// </summary>
    public sealed class BasicItemActionAuthority : IItemActionAuthority
    {
        public bool allowContainerAccess = true;
        public bool allowPlacement = true;
        public string requiredActorId = null;

        public bool AuthorizeContainerAccess(string actorId, string containerItemId, string containerOwnerActorId)
        {
            if (!allowContainerAccess) return false;
            if (requiredActorId != null && actorId != requiredActorId) return false;
            if (!string.IsNullOrEmpty(containerOwnerActorId) && !string.Equals(containerOwnerActorId, actorId, StringComparison.Ordinal))
                return false;
            return true;
        }

        public bool AuthorizePlacement(
            string worldId,
            string generationId,
            string actorId,
            string itemId,
            string targetSupportId,
            Vector3 position,
            Quaternion rotation,
            Vector3 supportNormal)
        {
            if (!allowPlacement) return false;
            if (requiredActorId != null && actorId != requiredActorId) return false;
            return true;
        }
    }

    /// <summary>
    /// Single-use, revision-bound preparation token for authoritative physical item transitions.
    /// Invalidated automatically upon any model revision mutation or explicit cancellation.
    /// </summary>
    public sealed class PreparedItemTransitionToken
    {
        public int TokenId { get; }
        public long BoundRevision { get; }
        public ItemActionRequest Request { get; }
        public ItemActionKind Action => Request.action;
        public string ItemId => Request.itemId;
        public string TargetId => Request.targetId;
        public string ActorId => Request.actorId;
        public int AssignedSlot { get; }
        public bool IsCommitted { get; internal set; }
        public bool IsCancelled { get; internal set; }

        internal PreparedItemTransitionToken(int tokenId, long boundRevision, ItemActionRequest request, int assignedSlot)
        {
            TokenId = tokenId;
            BoundRevision = boundRevision;
            Request = request;
            AssignedSlot = assignedSlot;
            IsCommitted = false;
            IsCancelled = false;
        }
    }

    /// <summary>
    /// Authoritative deterministic contract and state model for physical items and containers.
    /// Rejects nonfinite/negative values, containment cycles, duplicate ownership, and invalid transitions atomically.
    /// </summary>
    public sealed class ItemModel
    {
        public const int MaxReceiptLedgerSize = 512;

        private sealed class ReceiptRecord
        {
            public string signature;
            public ItemReceipt receipt;
        }

        public string WorldId { get; }
        public string GenerationId { get; }
        public long Tick { get; private set; }

        private readonly Dictionary<string, ItemDefinition> definitions = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemStateSnapshot> items = new Dictionary<string, ItemStateSnapshot>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActorCarryLimits> actorLimits = new Dictionary<string, ActorCarryLimits>(StringComparer.Ordinal);
        private readonly Dictionary<int, ReceiptRecord> receipts = new Dictionary<int, ReceiptRecord>();
        private readonly Dictionary<int, PreparedItemTransitionToken> activeTokens = new Dictionary<int, PreparedItemTransitionToken>();
        private readonly Dictionary<string, TombstoneRecord> tombstones = new Dictionary<string, TombstoneRecord>(StringComparer.Ordinal);
        private readonly Dictionary<int, SavedMaterialBatchReceiptRecord> materialReceipts = new Dictionary<int, SavedMaterialBatchReceiptRecord>();
        private readonly Dictionary<int, PreparedMaterialBatch> activeMaterialTokens = new Dictionary<int, PreparedMaterialBatch>();
        private int nextTokenId = 1;
        private int nextMaterialTokenId = 1;

        public const int MaxTombstoneLedgerSize = 1024;
        public const int MaxMaterialReceiptLedgerSize = 512;

        public int ItemCount => items.Count;
        public int DefinitionCount => definitions.Count;
        public int TombstoneCount => tombstones.Count;
        public int MaterialReceiptCount => materialReceipts.Count;
        public int HighestReceiptRequestId { get; private set; }
        public long IssuanceHighWatermark { get; private set; }
        public long Revision { get; private set; }
        public bool IsManaged { get; private set; }
        public bool IsDetachedFork { get; private set; }

        public bool EnableManagedWorkflow()
        {
            if (!IsDetachedFork) return false;
            IsManaged = true;
            BumpRevision();
            return true;
        }

        private void BumpRevision()
        {
            Revision++;
            activeTokens.Clear();
            activeMaterialTokens.Clear();
        }

        public bool TryGetReceipt(int requestId, out string signature, out ItemReceipt receipt)
        {
            signature = null;
            receipt = default;
            if (receipts.TryGetValue(requestId, out var rec))
            {
                signature = rec.signature;
                receipt = rec.receipt;
                return true;
            }
            return false;
        }

        public bool ResumeRequestSequence(int persistedBound)
        {
            if (persistedBound < 0) return false;
            if (persistedBound > HighestReceiptRequestId)
            {
                HighestReceiptRequestId = persistedBound;
                BumpRevision();
            }
            return true;
        }

        public bool TryAllocateNextRequestId(out int nextRequestId)
        {
            if (HighestReceiptRequestId >= int.MaxValue)
            {
                nextRequestId = -1;
                return false;
            }
            nextRequestId = HighestReceiptRequestId + 1;
            return true;
        }

        public ItemModel(string worldId, string generationId)
        {
            if (!IsValidId(worldId) || !IsValidId(generationId))
                throw new ArgumentException("Invalid world or generation identifier.");
            WorldId = worldId;
            GenerationId = generationId;
        }

        public static bool IsValidId(string s) =>
            !string.IsNullOrEmpty(s) && s.Length <= 80 && System.Text.RegularExpressions.Regex.IsMatch(s, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}\z");

        public bool RegisterDefinition(ItemDefinition def)
        {
            if (def == null || !def.IsValid() || definitions.ContainsKey(def.itemTypeId))
                return false;
            // Store detached copy to prevent caller aliasing
            definitions.Add(def.itemTypeId, def.Clone());
            BumpRevision();
            return true;
        }

        public bool RegisterItem(string itemId, string itemTypeId, ItemLocationKind initialLocation, Vector3 position, Quaternion rotation, string supportId = null)
        {
            if (!IsValidId(itemId) || items.ContainsKey(itemId) || tombstones.ContainsKey(itemId))
                return false;
            if (!definitions.TryGetValue(itemTypeId, out var def))
                return false;
            if (!ItemDefinition.Finite(position.x) || !ItemDefinition.Finite(position.y) || !ItemDefinition.Finite(position.z))
                return false;
            if (!ItemDefinition.TryCanonicalizeRotation(rotation, out var canonicalRotation))
                return false;

            if (def.isAnchored)
            {
                if (initialLocation != ItemLocationKind.Anchored)
                    return false;
            }
            else
            {
                if (initialLocation == ItemLocationKind.Anchored)
                    return false;
            }

            if (initialLocation == ItemLocationKind.Placed)
            {
                if (string.IsNullOrEmpty(supportId) || !IsValidId(supportId))
                    return false;
            }
            else if (!string.IsNullOrEmpty(supportId))
            {
                return false;
            }

            var state = new ItemStateSnapshot
            {
                itemId = itemId,
                itemTypeId = itemTypeId,
                location = initialLocation,
                holderActorId = null,
                containerItemId = null,
                containerSlot = -1,
                placedSupportId = supportId,
                position = position,
                rotation = canonicalRotation,
                lastUpdatedTick = Tick
            };

            items.Add(itemId, state);
            BumpRevision();
            return true;
        }

        public bool SetActorCarryLimits(string actorId, ActorCarryLimits limits)
        {
            if (!IsValidId(actorId) || !limits.IsValid())
                return false;
            actorLimits[actorId] = limits;
            BumpRevision();
            return true;
        }

        public ActorCarryLimits GetActorCarryLimits(string actorId)
        {
            if (!string.IsNullOrEmpty(actorId) && actorLimits.TryGetValue(actorId, out var limits))
                return limits;
            return new ActorCarryLimits(25f, 1);
        }

        public bool TryGetItemContainerSlot(string itemId, out int slot)
        {
            slot = -1;
            if (string.IsNullOrEmpty(itemId) || !items.TryGetValue(itemId, out var snap))
                return false;
            if (snap.location != ItemLocationKind.Stored)
                return false;
            slot = snap.containerSlot;
            return slot >= 0;
        }

        public string GetContainerSlotOccupant(string containerId, int slot)
        {
            if (string.IsNullOrEmpty(containerId) || slot < 0) return null;
            foreach (var kvp in items)
            {
                if (kvp.Value.location == ItemLocationKind.Stored &&
                    string.Equals(kvp.Value.containerItemId, containerId, StringComparison.Ordinal) &&
                    kvp.Value.containerSlot == slot)
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        public Dictionary<int, string> GetContainerSlotOccupants(string containerId)
        {
            var map = new Dictionary<int, string>();
            if (string.IsNullOrEmpty(containerId)) return map;
            foreach (var kvp in items)
            {
                if (kvp.Value.location == ItemLocationKind.Stored &&
                    string.Equals(kvp.Value.containerItemId, containerId, StringComparison.Ordinal) &&
                    kvp.Value.containerSlot >= 0)
                {
                    map[kvp.Value.containerSlot] = kvp.Key;
                }
            }
            return map;
        }

        public int AllocateFirstVacantSlot(string containerId, int maxSlots)
        {
            if (string.IsNullOrEmpty(containerId) || maxSlots <= 0) return -1;
            var occupied = new HashSet<int>();
            foreach (var kvp in items)
            {
                if (kvp.Value.location == ItemLocationKind.Stored &&
                    string.Equals(kvp.Value.containerItemId, containerId, StringComparison.Ordinal))
                {
                    if (kvp.Value.containerSlot >= 0)
                    {
                        occupied.Add(kvp.Value.containerSlot);
                    }
                }
            }
            for (int s = 0; s < maxSlots; s++)
            {
                if (!occupied.Contains(s))
                {
                    return s;
                }
            }
            return -1;
        }

        public bool TryGetItem(string itemId, out ItemStateSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrEmpty(itemId) || !items.TryGetValue(itemId, out var state))
                return false;
            snapshot = state.Clone();
            return true;
        }

        public bool TryGetDefinition(string itemTypeId, out ItemDefinition definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(itemTypeId) || !definitions.TryGetValue(itemTypeId, out var def))
                return false;
            definition = def.Clone();
            return true;
        }

        public List<string> GetContainerContents(string containerId)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(containerId)) return list;
            foreach (var kvp in items)
            {
                if (kvp.Value.location == ItemLocationKind.Stored &&
                    string.Equals(kvp.Value.containerItemId, containerId, StringComparison.Ordinal))
                {
                    list.Add(kvp.Key);
                }
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public List<string> GetContainerAncestors(string containerId)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(containerId)) return list;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = containerId;
            while (!string.IsNullOrEmpty(current))
            {
                if (!visited.Add(current)) break;
                if (!items.TryGetValue(current, out var state)) break;
                if (state.location == ItemLocationKind.Stored && !string.IsNullOrEmpty(state.containerItemId))
                {
                    list.Add(state.containerItemId);
                    current = state.containerItemId;
                }
                else
                {
                    break;
                }
            }
            return list;
        }

        public string GetContainerRootHolder(string containerId)
        {
            if (string.IsNullOrEmpty(containerId)) return null;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = containerId;
            while (!string.IsNullOrEmpty(current))
            {
                if (!visited.Add(current)) return null;
                if (!items.TryGetValue(current, out var state)) return null;
                if (state.location == ItemLocationKind.Carried)
                    return state.holderActorId;
                if (state.location == ItemLocationKind.Stored)
                    current = state.containerItemId;
                else
                    return null;
            }
            return null;
        }

        public List<string> GetActorCarriedItemIds(string actorId)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(actorId)) return list;
            foreach (var kvp in items)
            {
                if (kvp.Value.location == ItemLocationKind.Carried &&
                    string.Equals(kvp.Value.holderActorId, actorId, StringComparison.Ordinal))
                {
                    list.Add(kvp.Key);
                }
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public float GetItemTotalMassKg(string rootItemId)
        {
            if (string.IsNullOrEmpty(rootItemId) || !items.TryGetValue(rootItemId, out var rootItem))
                return 0f;

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(rootItemId);
            visited.Add(rootItemId);

            float totalMass = 0f;

            while (queue.Count > 0)
            {
                string currentId = queue.Dequeue();
                if (!items.TryGetValue(currentId, out var itemState))
                    continue;
                if (!definitions.TryGetValue(itemState.itemTypeId, out var def))
                    continue;

                totalMass += def.massKg;

                if (def.isContainer)
                {
                    foreach (var kvp in items)
                    {
                        if (kvp.Value.location == ItemLocationKind.Stored &&
                            string.Equals(kvp.Value.containerItemId, currentId, StringComparison.Ordinal))
                        {
                            if (visited.Add(kvp.Key))
                            {
                                queue.Enqueue(kvp.Key);
                            }
                            else
                            {
                                throw new InvalidOperationException($"Containment cycle detected involving item '{kvp.Key}'.");
                            }
                        }
                    }
                }
            }

            return totalMass;
        }

        public float GetContainerContainedMassKg(string containerId)
        {
            if (string.IsNullOrEmpty(containerId)) return 0f;
            float sum = 0f;
            foreach (var childId in GetContainerContents(containerId))
            {
                sum += GetItemTotalMassKg(childId);
            }
            return sum;
        }

        public float GetContainerContainedVolumeM3(string containerId)
        {
            if (string.IsNullOrEmpty(containerId)) return 0f;
            float sum = 0f;
            foreach (var childId in GetContainerContents(containerId))
            {
                if (items.TryGetValue(childId, out var item) && definitions.TryGetValue(item.itemTypeId, out var def))
                {
                    sum += def.dimensions.VolumeM3;
                }
            }
            return sum;
        }

        public float GetActorCarriedMassKg(string actorId)
        {
            if (string.IsNullOrEmpty(actorId)) return 0f;
            float sum = 0f;
            foreach (var itemId in GetActorCarriedItemIds(actorId))
            {
                sum += GetItemTotalMassKg(itemId);
            }
            return sum;
        }

        public bool WouldCreateContainmentCycle(string itemToStore, string targetContainerId)
        {
            if (string.IsNullOrEmpty(itemToStore) || string.IsNullOrEmpty(targetContainerId))
                return false;

            if (string.Equals(itemToStore, targetContainerId, StringComparison.Ordinal))
                return true;

            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = targetContainerId;

            while (!string.IsNullOrEmpty(current))
            {
                if (!visited.Add(current))
                {
                    return true;
                }

                if (string.Equals(current, itemToStore, StringComparison.Ordinal))
                {
                    return true;
                }

                if (items.TryGetValue(current, out var state) && state.location == ItemLocationKind.Stored)
                {
                    current = state.containerItemId;
                }
                else
                {
                    break;
                }
            }

            return false;
        }

        public void AdvanceTick()
        {
            Tick++;
            BumpRevision();
        }

        /// <summary>
        /// Authoritatively updates the position and rotation of an unconstrained Free item
        /// from trusted physics settlement or movement observation.
        /// Narrowed to trusted adapter access with strict world, generation, and identity validation.
        /// Rejects items that are Carried, Stored, Placed, or Anchored, or non-finite/non-canonical transforms.
        /// </summary>
        public bool SyncFreeTransform(string worldId, string generationId, string itemId, Vector3 position, Quaternion rotation)
        {
            if (!string.Equals(worldId, WorldId, StringComparison.Ordinal) ||
                !string.Equals(generationId, GenerationId, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrEmpty(itemId) || !items.TryGetValue(itemId, out var item))
            {
                return false;
            }

            if (item.location != ItemLocationKind.Free)
            {
                return false;
            }

            if (definitions.TryGetValue(item.itemTypeId, out var def) && def.isAnchored)
            {
                return false;
            }

            if (!ItemDefinition.Finite(position.x) || !ItemDefinition.Finite(position.y) || !ItemDefinition.Finite(position.z))
            {
                return false;
            }

            if (!ItemDefinition.TryCanonicalizeRotation(rotation, out var canonicalRot))
            {
                return false;
            }

            item.position = position;
            item.rotation = canonicalRot;
            item.lastUpdatedTick = Tick;
            BumpRevision();
            return true;
        }

        public List<ItemStateSnapshot> GetAllItemSnapshots()
        {
            var list = new List<ItemStateSnapshot>(items.Count);
            foreach (var kvp in items)
            {
                list.Add(kvp.Value.Clone());
            }
            return list;
        }

        public List<(int requestId, string signature, ItemReceipt receipt)> GetAllReceiptRecords()
        {
            var list = new List<(int, string, ItemReceipt)>(receipts.Count);
            foreach (var kvp in receipts)
            {
                list.Add((kvp.Key, kvp.Value.signature, kvp.Value.receipt));
            }
            return list;
        }

        public bool CanRestoreSnapshot(PhysicalSavePayload payload)
        {
            if (payload == null) return false;
            if (!string.Equals(payload.worldId, WorldId, StringComparison.Ordinal) ||
                !string.Equals(payload.generationId, GenerationId, StringComparison.Ordinal))
            {
                return false;
            }
            if (!IsValidId(payload.actorId)) return false;
            if (payload.tick < 0) return false;
            if (payload.items == null) return false;

            var payloadTombstoneIds = new HashSet<string>(StringComparer.Ordinal);
            if (payload.isManaged)
            {
                if (payload.tombstones == null || payload.materialReceipts == null) return false;
                if (payload.tombstones.Count > MaxTombstoneLedgerSize) return false;
                if (payload.materialReceipts.Count > MaxMaterialReceiptLedgerSize) return false;
                if (payload.issuanceHighWatermark < 0) return false;

                foreach (var t in payload.tombstones)
                {
                    if (string.IsNullOrEmpty(t.itemId) || !IsValidId(t.itemId)) return false;
                    if (!payloadTombstoneIds.Add(t.itemId)) return false; // Duplicate tombstone ID in payload
                    if (!IsValidId(t.itemTypeId)) return false;
                    if (!definitions.ContainsKey(t.itemTypeId)) return false;
                    if (t.retiredTick < 0 || t.retiredTick > payload.tick) return false;
                    if (string.IsNullOrEmpty(t.reason) || string.IsNullOrEmpty(t.provenance)) return false;
                }

                var matReceiptIds = new HashSet<int>();
                foreach (var mr in payload.materialReceipts)
                {
                    if (mr == null || mr.requestId <= 0 || string.IsNullOrEmpty(mr.signature)) return false;
                    if (!matReceiptIds.Add(mr.requestId)) return false;
                    if (mr.receipt.requestId != mr.requestId) return false;
                    if (!string.Equals(mr.receipt.worldId, WorldId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(mr.receipt.generationId, GenerationId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(mr.receipt.actorId, payload.actorId, StringComparison.Ordinal)) return false;
                }
            }
            else
            {
                if (payload.tombstones != null && payload.tombstones.Count > 0) return false;
                if (payload.materialReceipts != null && payload.materialReceipts.Count > 0) return false;
                if (payload.issuanceHighWatermark != 0) return false;
            }

            var itemMap = new Dictionary<string, SavedItemRecord>(StringComparer.Ordinal);
            var itemDefs = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

            foreach (var rec in payload.items)
            {
                if (rec == null) return false;
                if (!IsValidId(rec.itemId) || itemMap.ContainsKey(rec.itemId) || tombstones.ContainsKey(rec.itemId) || payloadTombstoneIds.Contains(rec.itemId)) return false;
                if (!IsValidId(rec.itemTypeId)) return false;

                if (!definitions.TryGetValue(rec.itemTypeId, out var liveDef)) return false;
                if (liveDef.isAnchored) return false;

                // Finite positive mass and exact match
                if (!ItemDefinition.Finite(rec.massKg) || rec.massKg <= 0f || Mathf.Abs(rec.massKg - liveDef.massKg) > 0.0001f)
                    return false;

                // Finite positive dimensions and exact match
                if (!ItemDefinition.Finite(rec.dimensions.width) || rec.dimensions.width <= 0f ||
                    !ItemDefinition.Finite(rec.dimensions.height) || rec.dimensions.height <= 0f ||
                    !ItemDefinition.Finite(rec.dimensions.depth) || rec.dimensions.depth <= 0f)
                {
                    return false;
                }
                if (Mathf.Abs(rec.dimensions.width - liveDef.dimensions.width) > 0.0001f ||
                    Mathf.Abs(rec.dimensions.height - liveDef.dimensions.height) > 0.0001f ||
                    Mathf.Abs(rec.dimensions.depth - liveDef.dimensions.depth) > 0.0001f)
                {
                    return false;
                }

                if (!ItemDefinition.Finite(rec.position.x) || !ItemDefinition.Finite(rec.position.y) || !ItemDefinition.Finite(rec.position.z))
                    return false;
                if (!ItemDefinition.TryCanonicalizeRotation(rec.rotation, out _))
                    return false;

                // Reject negative or future lastUpdatedTick instead of silently substituting payload.tick
                if (rec.lastUpdatedTick < 0 || rec.lastUpdatedTick > payload.tick)
                    return false;

                if (rec.location == ItemLocationKind.Free)
                {
                    if (!string.IsNullOrEmpty(rec.holderActorId)) return false;
                    if (!string.IsNullOrEmpty(rec.containerItemId)) return false;
                    if (rec.containerSlot != -1) return false;
                }
                else if (rec.location == ItemLocationKind.Carried)
                {
                    if (!string.Equals(rec.holderActorId, payload.actorId, StringComparison.Ordinal)) return false;
                    if (!string.IsNullOrEmpty(rec.containerItemId)) return false;
                    if (rec.containerSlot != -1) return false;
                }
                else if (rec.location == ItemLocationKind.Stored)
                {
                    if (!string.IsNullOrEmpty(rec.holderActorId)) return false;
                    if (string.IsNullOrEmpty(rec.containerItemId) || !IsValidId(rec.containerItemId)) return false;
                    if (string.Equals(rec.containerItemId, rec.itemId, StringComparison.Ordinal)) return false;
                    if (rec.containerSlot < -1) return false;
                }
                else
                {
                    // Placed, Anchored, or unknown states are rejected
                    return false;
                }

                itemMap.Add(rec.itemId, rec);
                itemDefs.Add(rec.itemId, liveDef);
            }

            // Validate containment relationships and cycle absence
            var containerChildren = new Dictionary<string, List<SavedItemRecord>>(StringComparer.Ordinal);
            foreach (var rec in payload.items)
            {
                if (rec.location == ItemLocationKind.Stored)
                {
                    // Missing parent check
                    if (!itemMap.TryGetValue(rec.containerItemId, out var parentRec))
                        return false;

                    // Non-container parent check
                    var parentDef = itemDefs[rec.containerItemId];
                    if (!parentDef.isContainer)
                        return false;

                    // Cycle check along ancestor chain
                    var visited = new HashSet<string>(StringComparer.Ordinal);
                    visited.Add(rec.itemId);
                    string current = rec.containerItemId;
                    while (!string.IsNullOrEmpty(current))
                    {
                        if (!visited.Add(current))
                        {
                            return false; // Containment cycle detected
                        }
                        if (!itemMap.TryGetValue(current, out var ancRec))
                        {
                            return false; // Dangling ancestor
                        }
                        if (ancRec.location == ItemLocationKind.Stored)
                        {
                            current = ancRec.containerItemId;
                        }
                        else
                        {
                            break; // Root reached (Free or Carried)
                        }
                    }

                    if (!containerChildren.TryGetValue(rec.containerItemId, out var childList))
                    {
                        childList = new List<SavedItemRecord>();
                        containerChildren[rec.containerItemId] = childList;
                    }
                    childList.Add(rec);
                }
            }

            // Recursive mass helper: calculates total mass of an item including all descendants
            float CalculateItemTotalMass(string rootItemId)
            {
                float total = 0f;
                var stack = new Stack<string>();
                stack.Push(rootItemId);
                while (stack.Count > 0)
                {
                    string curId = stack.Pop();
                    if (itemDefs.TryGetValue(curId, out var def))
                    {
                        total += def.massKg;
                    }
                    if (containerChildren.TryGetValue(curId, out var children))
                    {
                        foreach (var ch in children)
                        {
                            stack.Push(ch.itemId);
                        }
                    }
                }
                return total;
            }

            // Validate immediate container limits and ancestor capacity
            foreach (var rec in payload.items)
            {
                var def = itemDefs[rec.itemId];
                if (def.isContainer)
                {
                    containerChildren.TryGetValue(rec.itemId, out var directChildren);
                    int directCount = directChildren != null ? directChildren.Count : 0;
                    if (directCount > def.maxContainedSlots)
                        return false;

                    if (directChildren != null && directChildren.Count > 0)
                    {
                        bool allUnassigned = true;
                        bool anyUnassigned = false;
                        var occupiedSlots = new HashSet<int>();
                        foreach (var ch in directChildren)
                        {
                            if (ch.containerSlot == -1)
                            {
                                anyUnassigned = true;
                            }
                            else
                            {
                                allUnassigned = false;
                                if (ch.containerSlot < 0 || ch.containerSlot >= def.maxContainedSlots)
                                    return false;
                                if (!occupiedSlots.Add(ch.containerSlot))
                                    return false; // Duplicate occupancy
                            }
                        }
                        if (anyUnassigned && !allUnassigned)
                        {
                            // Inconsistent slot assignment (mix of unassigned and assigned) is malformed
                            return false;
                        }
                    }

                    float directVolume = 0f;
                    float recursiveContainedMass = 0f;
                    if (directChildren != null)
                    {
                        foreach (var ch in directChildren)
                        {
                            var chDef = itemDefs[ch.itemId];
                            directVolume += chDef.dimensions.VolumeM3;
                            recursiveContainedMass += CalculateItemTotalMass(ch.itemId);
                        }
                    }

                    if (directVolume > def.maxContainedVolumeM3)
                        return false;

                    if (recursiveContainedMass > def.maxContainedMassKg)
                        return false;
                }
            }

            // Validate actor carry limits (direct count and total carried mass including descendants exactly once)
            int carriedCount = 0;
            float totalCarriedMass = 0f;
            foreach (var rec in payload.items)
            {
                if (rec.location == ItemLocationKind.Carried)
                {
                    carriedCount++;
                    totalCarriedMass += CalculateItemTotalMass(rec.itemId);
                }
            }

            var limits = GetActorCarryLimits(payload.actorId);
            if (carriedCount > limits.maxCarriedItems) return false;
            if (totalCarriedMass > limits.maxCarryMassKg) return false;

            if (payload.receipts != null)
            {
                if (payload.receipts.Count > MaxReceiptLedgerSize) return false;
                var seenRequestIds = new HashSet<int>();
                foreach (var r in payload.receipts)
                {
                    if (r == null || r.requestId <= 0 || string.IsNullOrEmpty(r.signature)) return false;
                    if (!seenRequestIds.Add(r.requestId)) return false;
                    if (r.receipt.requestId != r.requestId) return false;
                    if (!string.Equals(r.receipt.worldId, WorldId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(r.receipt.generationId, GenerationId, StringComparison.Ordinal)) return false;
                    if (!string.Equals(r.receipt.actorId, payload.actorId, StringComparison.Ordinal)) return false;
                    if (!ItemDefinition.Finite(r.receipt.totalCarriedMassKg) || r.receipt.totalCarriedMassKg < 0f) return false;
                }
            }

            if (payload.isManaged)
            {
                if (payload.issuanceHighWatermark < 0) return false;

                if (payload.tombstones != null)
                {
                    if (payload.tombstones.Count > MaxTombstoneLedgerSize) return false;
                    var seenTombstones = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var t in payload.tombstones)
                    {
                        if (!IsValidId(t.itemId) || !IsValidId(t.itemTypeId)) return false;
                        if (!seenTombstones.Add(t.itemId)) return false; // Duplicate tombstone
                        if (itemMap.ContainsKey(t.itemId)) return false; // Active item cannot be tombstone
                        if (t.retiredTick < 0 || t.retiredTick > payload.tick) return false;
                    }
                }

                if (payload.materialReceipts != null)
                {
                    if (payload.materialReceipts.Count > MaxMaterialReceiptLedgerSize) return false;
                    var seenMatReqs = new HashSet<int>();
                    foreach (var mr in payload.materialReceipts)
                    {
                        if (mr == null || mr.requestId <= 0 || string.IsNullOrEmpty(mr.signature)) return false;
                        if (!seenMatReqs.Add(mr.requestId)) return false;
                        if (mr.receipt.requestId != mr.requestId) return false;
                        if (!string.Equals(mr.receipt.worldId, WorldId, StringComparison.Ordinal)) return false;
                        if (!string.Equals(mr.receipt.generationId, GenerationId, StringComparison.Ordinal)) return false;
                        if (!string.Equals(mr.receipt.actorId, payload.actorId, StringComparison.Ordinal)) return false;
                        if (!ItemDefinition.Finite(mr.receipt.totalCarriedMassKg) || mr.receipt.totalCarriedMassKg < 0f) return false;
                    }
                }
            }

            return true;
        }

        public bool RestoreSnapshot(PhysicalSavePayload payload)
        {
            if (!CanRestoreSnapshot(payload)) return false;

            // Determine slot assignments for stored items
            var containerDirectChildren = new Dictionary<string, List<SavedItemRecord>>(StringComparer.Ordinal);
            foreach (var rec in payload.items)
            {
                if (rec.location == ItemLocationKind.Stored)
                {
                    if (!containerDirectChildren.TryGetValue(rec.containerItemId, out var list))
                    {
                        list = new List<SavedItemRecord>();
                        containerDirectChildren[rec.containerItemId] = list;
                    }
                    list.Add(rec);
                }
            }

            var assignedSlots = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kvp in containerDirectChildren)
            {
                var children = kvp.Value;
                bool allUnassigned = true;
                foreach (var ch in children)
                {
                    if (ch.containerSlot != -1)
                    {
                        allUnassigned = false;
                        break;
                    }
                }

                if (allUnassigned)
                {
                    var sorted = new List<SavedItemRecord>(children);
                    sorted.Sort((a, b) => string.Compare(a.itemId, b.itemId, StringComparison.Ordinal));
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        assignedSlots[sorted[i].itemId] = i;
                    }
                }
                else
                {
                    foreach (var ch in children)
                    {
                        assignedSlots[ch.itemId] = ch.containerSlot;
                    }
                }
            }

            var newItems = new Dictionary<string, ItemStateSnapshot>(StringComparer.Ordinal);
            foreach (var rec in payload.items)
            {
                ItemDefinition.TryCanonicalizeRotation(rec.rotation, out var canonicalRot);
                int slot = -1;
                if (rec.location == ItemLocationKind.Stored)
                {
                    if (!assignedSlots.TryGetValue(rec.itemId, out slot))
                    {
                        slot = rec.containerSlot;
                    }
                }

                var snap = new ItemStateSnapshot
                {
                    itemId = rec.itemId,
                    itemTypeId = rec.itemTypeId,
                    location = rec.location,
                    holderActorId = rec.holderActorId,
                    containerItemId = string.IsNullOrEmpty(rec.containerItemId) ? null : rec.containerItemId,
                    containerSlot = slot,
                    placedSupportId = null,
                    position = rec.position,
                    rotation = canonicalRot,
                    lastUpdatedTick = rec.lastUpdatedTick
                };
                newItems.Add(rec.itemId, snap);
            }

            int maxRestoredRequestId = 0;
            var newReceipts = new Dictionary<int, ReceiptRecord>();
            if (payload.receipts != null)
            {
                foreach (var r in payload.receipts)
                {
                    newReceipts.Add(r.requestId, new ReceiptRecord { signature = r.signature, receipt = r.receipt });
                    if (r.requestId > maxRestoredRequestId)
                    {
                        maxRestoredRequestId = r.requestId;
                    }
                }
            }

            // Apply atomically
            items.Clear();
            foreach (var kvp in newItems)
            {
                items.Add(kvp.Key, kvp.Value);
            }

            receipts.Clear();
            foreach (var kvp in newReceipts)
            {
                receipts.Add(kvp.Key, kvp.Value);
            }

            if (payload.isManaged)
            {
                IsManaged = true;
                tombstones.Clear();
                if (payload.tombstones != null)
                {
                    foreach (var t in payload.tombstones)
                    {
                        tombstones[t.itemId] = t;
                    }
                }

                materialReceipts.Clear();
                if (payload.materialReceipts != null)
                {
                    foreach (var mr in payload.materialReceipts)
                    {
                        materialReceipts[mr.requestId] = new SavedMaterialBatchReceiptRecord
                        {
                            requestId = mr.requestId,
                            signature = mr.signature,
                            receipt = mr.receipt
                        };
                    }
                }

                IssuanceHighWatermark = payload.issuanceHighWatermark;
            }

            HighestReceiptRequestId = Math.Max(HighestReceiptRequestId, maxRestoredRequestId);

            if (payload.tick > Tick)
            {
                Tick = payload.tick;
            }

            BumpRevision();
            return true;
        }

        public static string BuildRequestSignature(ItemActionRequest req, Quaternion canonicalRotation)
        {
            var sb = new StringBuilder(128);
            sb.Append("action=").Append(req.action)
              .Append(";actor=").Append(req.actorId ?? "")
              .Append(";item=").Append(req.itemId ?? "")
              .Append(";target=").Append(req.targetId ?? "");

            if (req.action == ItemActionKind.Drop || req.action == ItemActionKind.Place)
            {
                sb.Append(";pos=")
                  .Append(req.position.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(req.position.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(req.position.z.ToString("R", CultureInfo.InvariantCulture));

                sb.Append(";rot=")
                  .Append(canonicalRotation.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(canonicalRotation.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(canonicalRotation.z.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(canonicalRotation.w.ToString("R", CultureInfo.InvariantCulture));
            }

            if (req.action == ItemActionKind.Place)
            {
                sb.Append(";norm=")
                  .Append(req.supportNormal.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(req.supportNormal.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(req.supportNormal.z.ToString("R", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        public bool CancelPreparedTransition(PreparedItemTransitionToken token)
        {
            if (token == null) return false;
            if (activeTokens.TryGetValue(token.TokenId, out var active) && active == token)
            {
                activeTokens.Remove(token.TokenId);
                token.IsCancelled = true;
                return true;
            }
            return false;
        }

        public ItemReceipt RecordRejectionReceipt(ItemActionRequest request, string code)
        {
            return RecordRejectionReceipt(WorldId, GenerationId, request, code);
        }

        public ItemReceipt RecordRejectionReceipt(string worldId, string generationId, ItemActionRequest request, string code)
        {
            if (worldId != WorldId || generationId != GenerationId)
            {
                return Deny(request, "world-mismatch");
            }

            if (request.requestId <= 0)
            {
                return Deny(request, "invalid-request-id");
            }

            Quaternion canonicalRotation = Quaternion.identity;
            if (request.action == ItemActionKind.Drop || request.action == ItemActionKind.Place)
            {
                ItemDefinition.TryCanonicalizeRotation(request.rotation, out canonicalRotation);
            }

            string signature = BuildRequestSignature(request, canonicalRotation);
            if (receipts.TryGetValue(request.requestId, out var prior))
            {
                if (prior.signature == signature)
                {
                    var duplicate = prior.receipt;
                    duplicate.duplicate = true;
                    return duplicate;
                }
                return Deny(request, "request-id-conflict");
            }

            if (receipts.Count >= MaxReceiptLedgerSize)
            {
                return Deny(request, "bounded-ledger-full");
            }

            var receipt = Deny(request, code);
            receipts.Add(request.requestId, new ReceiptRecord { signature = signature, receipt = receipt });
            if (request.requestId > HighestReceiptRequestId)
            {
                HighestReceiptRequestId = request.requestId;
            }
            BumpRevision();
            return receipt;
        }

        public bool TryPrepareTransition(ItemActionRequest request, IItemActionAuthority authority, out PreparedItemTransitionToken token, out string rejectionCode)
        {
            return TryPrepareTransition(WorldId, GenerationId, request, authority, out token, out rejectionCode);
        }

        public bool TryPrepareTransition(string worldId, string generationId, ItemActionRequest request, IItemActionAuthority authority, out PreparedItemTransitionToken token, out string rejectionCode)
        {
            token = null;
            rejectionCode = null;

            if (worldId != WorldId || generationId != GenerationId)
            {
                rejectionCode = "world-mismatch";
                return false;
            }

            if (request.requestId <= 0)
            {
                rejectionCode = "invalid-request-id";
                return false;
            }

            if (!Enum.IsDefined(typeof(ItemActionKind), request.action))
            {
                rejectionCode = "unsupported-action";
                return false;
            }

            if (!IsValidId(request.actorId) || !IsValidId(request.itemId))
            {
                rejectionCode = "invalid-scope-id";
                return false;
            }

            Quaternion canonicalRotation = Quaternion.identity;
            if (request.action == ItemActionKind.Drop || request.action == ItemActionKind.Place)
            {
                if (!ItemDefinition.TryCanonicalizeRotation(request.rotation, out canonicalRotation))
                {
                    rejectionCode = "invalid-rotation";
                    return false;
                }
            }

            string signature = BuildRequestSignature(request, canonicalRotation);
            if (receipts.TryGetValue(request.requestId, out var prior))
            {
                if (prior.signature == signature)
                {
                    rejectionCode = "duplicate-request";
                    return false;
                }
                rejectionCode = "request-id-conflict";
                return false;
            }

            if (receipts.Count >= MaxReceiptLedgerSize)
            {
                rejectionCode = "bounded-ledger-full";
                return false;
            }

            if (!items.TryGetValue(request.itemId, out var item))
            {
                rejectionCode = "item-not-found";
                return false;
            }

            if (!definitions.TryGetValue(item.itemTypeId, out var def))
            {
                rejectionCode = "item-definition-missing";
                return false;
            }

            foreach (var active in activeTokens.Values)
            {
                if (string.Equals(active.ItemId, request.itemId, StringComparison.Ordinal))
                {
                    rejectionCode = "item-in-transition";
                    return false;
                }
            }

            var limits = GetActorCarryLimits(request.actorId);
            int assignedSlot = -1;

            switch (request.action)
            {
                case ItemActionKind.Pickup:
                {
                    if (item.location == ItemLocationKind.Anchored || def.isAnchored)
                    {
                        rejectionCode = "anchored-scenery-cannot-be-picked-up";
                        return false;
                    }
                    if (item.location == ItemLocationKind.Carried)
                    {
                        rejectionCode = "item-already-carried";
                        return false;
                    }
                    if (item.location == ItemLocationKind.Stored)
                    {
                        rejectionCode = "item-is-stored-retrieve-first";
                        return false;
                    }
                    if (item.location != ItemLocationKind.Free && item.location != ItemLocationKind.Placed)
                    {
                        rejectionCode = "item-not-available";
                        return false;
                    }

                    var carried = GetActorCarriedItemIds(request.actorId);
                    if (carried.Count >= limits.maxCarriedItems)
                    {
                        rejectionCode = "actor-hands-full";
                        return false;
                    }

                    float itemTotalMass = GetItemTotalMassKg(request.itemId);
                    float currentActorMass = GetActorCarriedMassKg(request.actorId);
                    if (currentActorMass + itemTotalMass > limits.maxCarryMassKg)
                    {
                        rejectionCode = "carry-mass-capacity-exceeded";
                        return false;
                    }
                    break;
                }

                case ItemActionKind.Drop:
                {
                    if (item.location != ItemLocationKind.Carried || !string.Equals(item.holderActorId, request.actorId, StringComparison.Ordinal))
                    {
                        rejectionCode = "item-not-carried-by-actor";
                        return false;
                    }

                    if (!ItemDefinition.Finite(request.position.x) || !ItemDefinition.Finite(request.position.y) || !ItemDefinition.Finite(request.position.z))
                    {
                        rejectionCode = "invalid-drop-transform";
                        return false;
                    }
                    break;
                }

                case ItemActionKind.Place:
                {
                    if (item.location != ItemLocationKind.Carried || !string.Equals(item.holderActorId, request.actorId, StringComparison.Ordinal))
                    {
                        rejectionCode = "item-not-carried-by-actor";
                        return false;
                    }

                    if (string.IsNullOrEmpty(request.targetId) || !IsValidId(request.targetId))
                    {
                        rejectionCode = "invalid-scope-id";
                        return false;
                    }

                    if (!ItemDefinition.Finite(request.position.x) || !ItemDefinition.Finite(request.position.y) || !ItemDefinition.Finite(request.position.z))
                    {
                        rejectionCode = "invalid-placement-transform";
                        return false;
                    }

                    if (!ItemDefinition.Finite(request.supportNormal.x) || !ItemDefinition.Finite(request.supportNormal.y) || !ItemDefinition.Finite(request.supportNormal.z) ||
                        request.supportNormal.y < 0.5f)
                    {
                        rejectionCode = "unsupported-placement-surface";
                        return false;
                    }

                    if (authority == null)
                    {
                        rejectionCode = "placement-authority-missing";
                        return false;
                    }

                    if (!authority.AuthorizePlacement(WorldId, GenerationId, request.actorId, request.itemId, request.targetId, request.position, canonicalRotation, request.supportNormal))
                    {
                        rejectionCode = "placement-authorization-denied";
                        return false;
                    }
                    break;
                }

                case ItemActionKind.Store:
                {
                    if (item.location != ItemLocationKind.Carried || !string.Equals(item.holderActorId, request.actorId, StringComparison.Ordinal))
                    {
                        rejectionCode = "item-not-carried-by-actor";
                        return false;
                    }

                    if (string.IsNullOrEmpty(request.targetId) || !IsValidId(request.targetId))
                    {
                        rejectionCode = "invalid-scope-id";
                        return false;
                    }

                    if (string.Equals(request.itemId, request.targetId, StringComparison.Ordinal))
                    {
                        rejectionCode = "cannot-store-in-itself";
                        return false;
                    }

                    if (!items.TryGetValue(request.targetId, out var containerItem))
                    {
                        rejectionCode = "target-container-not-found";
                        return false;
                    }

                    if (!definitions.TryGetValue(containerItem.itemTypeId, out var containerDef) || !containerDef.isContainer)
                    {
                        rejectionCode = "target-not-a-container";
                        return false;
                    }

                    string rootHolder = GetContainerRootHolder(request.targetId);
                    if (authority == null)
                    {
                        rejectionCode = "container-authority-missing";
                        return false;
                    }
                    if (!authority.AuthorizeContainerAccess(request.actorId, request.targetId, rootHolder))
                    {
                        rejectionCode = "container-access-denied";
                        return false;
                    }

                    if (WouldCreateContainmentCycle(request.itemId, request.targetId))
                    {
                        rejectionCode = "containment-cycle-detected";
                        return false;
                    }

                    assignedSlot = AllocateFirstVacantSlot(request.targetId, containerDef.maxContainedSlots);
                    if (assignedSlot < 0)
                    {
                        rejectionCode = "container-slot-capacity-exceeded";
                        return false;
                    }

                    float currentContainedMass = GetContainerContainedMassKg(request.targetId);
                    float itemTotalMass = GetItemTotalMassKg(request.itemId);
                    if (currentContainedMass + itemTotalMass > containerDef.maxContainedMassKg)
                    {
                        rejectionCode = "container-mass-capacity-exceeded";
                        return false;
                    }

                    float currentContainedVolume = GetContainerContainedVolumeM3(request.targetId);
                    if (currentContainedVolume + def.dimensions.VolumeM3 > containerDef.maxContainedVolumeM3)
                    {
                        rejectionCode = "container-volume-capacity-exceeded";
                        return false;
                    }

                    foreach (var ancestorId in GetContainerAncestors(request.targetId))
                    {
                        if (items.TryGetValue(ancestorId, out var ancestorState) &&
                            definitions.TryGetValue(ancestorState.itemTypeId, out var ancestorDef) &&
                            ancestorDef.isContainer)
                        {
                            if (GetContainerContainedMassKg(ancestorId) + itemTotalMass > ancestorDef.maxContainedMassKg)
                            {
                                rejectionCode = "ancestor-container-mass-capacity-exceeded";
                                return false;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(rootHolder) && !string.Equals(rootHolder, request.actorId, StringComparison.Ordinal))
                    {
                        var otherLimits = GetActorCarryLimits(rootHolder);
                        float otherMass = GetActorCarriedMassKg(rootHolder);
                        if (otherMass + itemTotalMass > otherLimits.maxCarryMassKg)
                        {
                            rejectionCode = "carry-mass-capacity-exceeded";
                            return false;
                        }
                    }
                    break;
                }

                case ItemActionKind.Retrieve:
                {
                    if (string.IsNullOrEmpty(request.targetId) || !IsValidId(request.targetId))
                    {
                        rejectionCode = "invalid-scope-id";
                        return false;
                    }

                    if (item.location != ItemLocationKind.Stored || !string.Equals(item.containerItemId, request.targetId, StringComparison.Ordinal))
                    {
                        rejectionCode = "item-not-stored-in-target";
                        return false;
                    }

                    if (!items.TryGetValue(request.targetId, out var containerItem))
                    {
                        rejectionCode = "target-container-not-found";
                        return false;
                    }

                    string rootHolder = GetContainerRootHolder(request.targetId);
                    if (authority == null)
                    {
                        rejectionCode = "container-authority-missing";
                        return false;
                    }
                    if (!authority.AuthorizeContainerAccess(request.actorId, request.targetId, rootHolder))
                    {
                        rejectionCode = "container-access-denied";
                        return false;
                    }

                    var carried = GetActorCarriedItemIds(request.actorId);
                    if (carried.Count >= limits.maxCarriedItems)
                    {
                        rejectionCode = "actor-hands-full";
                        return false;
                    }

                    float itemTotalMass = GetItemTotalMassKg(request.itemId);
                    bool alreadyCarriedByThisActor = string.Equals(rootHolder, request.actorId, StringComparison.Ordinal);
                    float netMassChange = alreadyCarriedByThisActor ? 0f : itemTotalMass;

                    float currentActorMass = GetActorCarriedMassKg(request.actorId);
                    if (currentActorMass + netMassChange > limits.maxCarryMassKg)
                    {
                        rejectionCode = "carry-mass-capacity-exceeded";
                        return false;
                    }

                    assignedSlot = item.containerSlot;
                    break;
                }

                default:
                    rejectionCode = "unsupported-action";
                    return false;
            }

            int tokenId = nextTokenId++;
            token = new PreparedItemTransitionToken(tokenId, Revision, request, assignedSlot);
            activeTokens[tokenId] = token;
            return true;
        }

        public bool TryCommitTransition(PreparedItemTransitionToken token, out ItemReceipt receipt)
        {
            receipt = default;
            if (token == null || token.IsCommitted || token.IsCancelled)
                return false;

            if (!activeTokens.TryGetValue(token.TokenId, out var active) || active != token)
                return false;

            if (token.BoundRevision != Revision)
            {
                token.IsCancelled = true;
                activeTokens.Remove(token.TokenId);
                return false;
            }

            if (!items.TryGetValue(token.ItemId, out var item))
            {
                token.IsCancelled = true;
                activeTokens.Remove(token.TokenId);
                return false;
            }

            string successCode;
            Quaternion canonicalRot = Quaternion.identity;

            switch (token.Action)
            {
                case ItemActionKind.Store:
                    item.location = ItemLocationKind.Stored;
                    item.holderActorId = null;
                    item.placedSupportId = null;
                    item.containerItemId = token.TargetId;
                    item.containerSlot = token.AssignedSlot;
                    item.lastUpdatedTick = Tick;
                    successCode = "stored-in-container";
                    break;

                case ItemActionKind.Retrieve:
                    item.location = ItemLocationKind.Carried;
                    item.holderActorId = token.ActorId;
                    item.placedSupportId = null;
                    item.containerItemId = null;
                    item.containerSlot = -1;
                    item.lastUpdatedTick = Tick;
                    successCode = "retrieved-from-container";
                    break;

                case ItemActionKind.Pickup:
                    item.location = ItemLocationKind.Carried;
                    item.holderActorId = token.ActorId;
                    item.placedSupportId = null;
                    item.containerItemId = null;
                    item.containerSlot = -1;
                    item.lastUpdatedTick = Tick;
                    successCode = "picked-up";
                    break;

                case ItemActionKind.Drop:
                    ItemDefinition.TryCanonicalizeRotation(token.Request.rotation, out canonicalRot);
                    item.location = ItemLocationKind.Free;
                    item.holderActorId = null;
                    item.placedSupportId = null;
                    item.containerItemId = null;
                    item.containerSlot = -1;
                    item.position = token.Request.position;
                    item.rotation = canonicalRot;
                    item.lastUpdatedTick = Tick;
                    successCode = "dropped-intent-recorded";
                    break;

                case ItemActionKind.Place:
                    ItemDefinition.TryCanonicalizeRotation(token.Request.rotation, out canonicalRot);
                    item.location = ItemLocationKind.Placed;
                    item.holderActorId = null;
                    item.placedSupportId = token.Request.targetId;
                    item.containerItemId = null;
                    item.containerSlot = -1;
                    item.position = token.Request.position;
                    item.rotation = canonicalRot;
                    item.lastUpdatedTick = Tick;
                    successCode = "placed-on-support";
                    break;

                default:
                    token.IsCancelled = true;
                    activeTokens.Remove(token.TokenId);
                    return false;
            }

            string signature = BuildRequestSignature(token.Request, canonicalRot);
            receipt = new ItemReceipt
            {
                requestId = token.Request.requestId,
                worldId = WorldId,
                generationId = GenerationId,
                actorId = token.ActorId,
                action = token.Action,
                itemId = token.ItemId,
                targetId = token.TargetId ?? "",
                success = true,
                duplicate = false,
                code = successCode,
                totalCarriedMassKg = GetActorCarriedMassKg(token.ActorId)
            };

            receipts.Add(token.Request.requestId, new ReceiptRecord { signature = signature, receipt = receipt });
            if (token.Request.requestId > HighestReceiptRequestId)
            {
                HighestReceiptRequestId = token.Request.requestId;
            }

            token.IsCommitted = true;
            activeTokens.Remove(token.TokenId);
            BumpRevision();
            return true;
        }

        private ItemReceipt Deny(ItemActionRequest req, string code)
        {
            return new ItemReceipt
            {
                requestId = req.requestId,
                worldId = WorldId,
                generationId = GenerationId,
                actorId = req.actorId ?? "",
                action = req.action,
                itemId = req.itemId ?? "",
                targetId = req.targetId ?? "",
                success = false,
                duplicate = false,
                code = code,
                totalCarriedMassKg = GetActorCarriedMassKg(req.actorId ?? "")
            };
        }

        public ItemReceipt Execute(string worldId, string generationId, ItemActionRequest request, IItemActionAuthority authority = null)
        {
            if (worldId != WorldId || generationId != GenerationId)
            {
                return Deny(request, "world-mismatch");
            }

            if (request.requestId <= 0)
            {
                return Deny(request, "invalid-request-id");
            }

            if (!Enum.IsDefined(typeof(ItemActionKind), request.action))
            {
                return Deny(request, "unsupported-action");
            }

            if (!IsValidId(request.actorId) || !IsValidId(request.itemId))
            {
                return Deny(request, "invalid-scope-id");
            }

            Quaternion canonicalRotation = Quaternion.identity;
            if (request.action == ItemActionKind.Drop || request.action == ItemActionKind.Place)
            {
                if (!ItemDefinition.TryCanonicalizeRotation(request.rotation, out canonicalRotation))
                {
                    return Deny(request, "invalid-rotation");
                }
            }

            string signature = BuildRequestSignature(request, canonicalRotation);
            if (receipts.TryGetValue(request.requestId, out var prior))
            {
                if (prior.signature == signature)
                {
                    var duplicate = prior.receipt;
                    duplicate.duplicate = true;
                    return duplicate;
                }
                return Deny(request, "request-id-conflict");
            }

            if (receipts.Count >= MaxReceiptLedgerSize)
            {
                return Deny(request, "bounded-ledger-full");
            }

            ItemReceipt Finish(ItemReceipt receipt)
            {
                receipts.Add(request.requestId, new ReceiptRecord { signature = signature, receipt = receipt });
                if (request.requestId > HighestReceiptRequestId)
                {
                    HighestReceiptRequestId = request.requestId;
                }
                BumpRevision();
                return receipt;
            }

            ItemReceipt Reject(string code) => Finish(Deny(request, code));

            switch (request.action)
            {
                case ItemActionKind.Store:
                case ItemActionKind.Retrieve:
                {
                    if (!TryPrepareTransition(worldId, generationId, request, authority, out var token, out var rejectCode))
                    {
                        return Reject(rejectCode);
                    }
                    if (!TryCommitTransition(token, out var receipt))
                    {
                        return Reject("commit-failed");
                    }
                    return receipt;
                }

                case ItemActionKind.Pickup:
                {
                    if (!items.TryGetValue(request.itemId, out var item))
                    {
                        return Reject("item-not-found");
                    }

                    if (!definitions.TryGetValue(item.itemTypeId, out var def))
                    {
                        return Reject("item-definition-missing");
                    }

                    var limits = GetActorCarryLimits(request.actorId);

                    if (item.location == ItemLocationKind.Anchored || def.isAnchored)
                    {
                        return Reject("anchored-scenery-cannot-be-picked-up");
                    }
                    if (item.location == ItemLocationKind.Carried)
                    {
                        return Reject("item-already-carried");
                    }
                    if (item.location == ItemLocationKind.Stored)
                    {
                        return Reject("item-is-stored-retrieve-first");
                    }
                    if (item.location != ItemLocationKind.Free && item.location != ItemLocationKind.Placed)
                    {
                        return Reject("item-not-available");
                    }

                    var carried = GetActorCarriedItemIds(request.actorId);
                    if (carried.Count >= limits.maxCarriedItems)
                    {
                        return Reject("actor-hands-full");
                    }

                    float itemTotalMass = GetItemTotalMassKg(request.itemId);
                    float currentActorMass = GetActorCarriedMassKg(request.actorId);
                    if (currentActorMass + itemTotalMass > limits.maxCarryMassKg)
                    {
                        return Reject("carry-mass-capacity-exceeded");
                    }

                    item.location = ItemLocationKind.Carried;
                    item.holderActorId = request.actorId;
                    item.placedSupportId = null;
                    item.containerItemId = null;
                    item.containerSlot = -1;
                    item.lastUpdatedTick = Tick;

                    return Finish(new ItemReceipt
                    {
                        requestId = request.requestId,
                        worldId = WorldId,
                        generationId = GenerationId,
                        actorId = request.actorId,
                        action = request.action,
                        itemId = request.itemId,
                        targetId = request.targetId ?? "",
                        success = true,
                        duplicate = false,
                        code = "picked-up",
                        totalCarriedMassKg = GetActorCarriedMassKg(request.actorId)
                    });
                }

                case ItemActionKind.Drop:
                {
                    if (!items.TryGetValue(request.itemId, out var item))
                    {
                        return Reject("item-not-found");
                    }

                    if (!definitions.TryGetValue(item.itemTypeId, out var def))
                    {
                        return Reject("item-definition-missing");
                    }

                    if (item.location != ItemLocationKind.Carried || !string.Equals(item.holderActorId, request.actorId, StringComparison.Ordinal))
                    {
                        return Reject("item-not-carried-by-actor");
                    }

                    if (!ItemDefinition.Finite(request.position.x) || !ItemDefinition.Finite(request.position.y) || !ItemDefinition.Finite(request.position.z))
                    {
                        return Reject("invalid-drop-transform");
                    }

                    item.location = ItemLocationKind.Free;
                    item.holderActorId = null;
                    item.containerItemId = null;
                    item.containerSlot = -1;
                    item.placedSupportId = null;
                    item.position = request.position;
                    item.rotation = canonicalRotation;
                    item.lastUpdatedTick = Tick;

                    return Finish(new ItemReceipt
                    {
                        requestId = request.requestId,
                        worldId = WorldId,
                        generationId = GenerationId,
                        actorId = request.actorId,
                        action = request.action,
                        itemId = request.itemId,
                        targetId = request.targetId ?? "",
                        success = true,
                        duplicate = false,
                        code = "dropped-intent-recorded",
                        totalCarriedMassKg = GetActorCarriedMassKg(request.actorId)
                    });
                }

                case ItemActionKind.Place:
                {
                    if (!items.TryGetValue(request.itemId, out var item))
                    {
                        return Reject("item-not-found");
                    }

                    if (!definitions.TryGetValue(item.itemTypeId, out var def))
                    {
                        return Reject("item-definition-missing");
                    }

                    if (item.location != ItemLocationKind.Carried || !string.Equals(item.holderActorId, request.actorId, StringComparison.Ordinal))
                    {
                        return Reject("item-not-carried-by-actor");
                    }

                    if (string.IsNullOrEmpty(request.targetId) || !IsValidId(request.targetId))
                    {
                        return Reject("invalid-scope-id");
                    }

                    if (!ItemDefinition.Finite(request.position.x) || !ItemDefinition.Finite(request.position.y) || !ItemDefinition.Finite(request.position.z))
                    {
                        return Reject("invalid-placement-transform");
                    }

                    if (!ItemDefinition.Finite(request.supportNormal.x) || !ItemDefinition.Finite(request.supportNormal.y) || !ItemDefinition.Finite(request.supportNormal.z) ||
                        request.supportNormal.y < 0.5f)
                    {
                        return Reject("unsupported-placement-surface");
                    }

                    if (authority == null)
                    {
                        return Reject("placement-authority-missing");
                    }

                    if (!authority.AuthorizePlacement(WorldId, GenerationId, request.actorId, request.itemId, request.targetId, request.position, canonicalRotation, request.supportNormal))
                    {
                        return Reject("placement-authorization-denied");
                    }

                    item.location = ItemLocationKind.Placed;
                    item.holderActorId = null;
                    item.containerItemId = null;
                    item.containerSlot = -1;
                    item.placedSupportId = request.targetId;
                    item.position = request.position;
                    item.rotation = canonicalRotation;
                    item.lastUpdatedTick = Tick;

                    return Finish(new ItemReceipt
                    {
                        requestId = request.requestId,
                        worldId = WorldId,
                        generationId = GenerationId,
                        actorId = request.actorId,
                        action = request.action,
                        itemId = request.itemId,
                        targetId = request.targetId,
                        success = true,
                        duplicate = false,
                        code = "placed-on-support",
                        totalCarriedMassKg = GetActorCarriedMassKg(request.actorId)
                    });
                }

                default:
                    return Reject("unsupported-action");
            }
        }

        public ItemModel CreateDetachedFork(bool enableManaged = false)
        {
            var fork = new ItemModel(WorldId, GenerationId);
            fork.IsDetachedFork = true;
            fork.IsManaged = this.IsManaged || enableManaged;
            fork.Tick = this.Tick;
            fork.Revision = this.Revision;
            fork.HighestReceiptRequestId = this.HighestReceiptRequestId;
            fork.IssuanceHighWatermark = this.IssuanceHighWatermark;

            foreach (var kvp in definitions)
                fork.definitions[kvp.Key] = kvp.Value.Clone();

            foreach (var kvp in actorLimits)
                fork.actorLimits[kvp.Key] = kvp.Value;

            foreach (var kvp in items)
                fork.items[kvp.Key] = kvp.Value.Clone();

            foreach (var kvp in receipts)
                fork.receipts[kvp.Key] = new ReceiptRecord { signature = kvp.Value.signature, receipt = kvp.Value.receipt };

            foreach (var kvp in tombstones)
                fork.tombstones[kvp.Key] = kvp.Value;

            foreach (var kvp in materialReceipts)
                fork.materialReceipts[kvp.Key] = new SavedMaterialBatchReceiptRecord
                {
                    requestId = kvp.Value.requestId,
                    signature = kvp.Value.signature,
                    receipt = kvp.Value.receipt
                };

            // activeTokens and activeMaterialTokens are intentionally not copied to prevent token aliasing
            return fork;
        }

        public bool IsRetired(string itemId) => !string.IsNullOrEmpty(itemId) && tombstones.ContainsKey(itemId);

        public bool TryGetTombstone(string itemId, out TombstoneRecord tombstone)
        {
            tombstone = default;
            if (string.IsNullOrEmpty(itemId)) return false;
            return tombstones.TryGetValue(itemId, out tombstone);
        }

        public List<TombstoneRecord> GetAllTombstones()
        {
            return new List<TombstoneRecord>(tombstones.Values);
        }

        public bool TryGetMaterialReceipt(int requestId, out string signature, out MaterialBatchReceipt receipt)
        {
            signature = null;
            receipt = default;
            if (materialReceipts.TryGetValue(requestId, out var rec))
            {
                signature = rec.signature;
                receipt = rec.receipt;
                return true;
            }
            return false;
        }

        public List<SavedMaterialBatchReceiptRecord> GetAllMaterialReceipts()
        {
            return new List<SavedMaterialBatchReceiptRecord>(materialReceipts.Values);
        }

        public bool TryAllocateIssuanceId(string prefix, out string nextItemId)
        {
            nextItemId = null;
            if (string.IsNullOrEmpty(prefix)) prefix = "item";
            if (IssuanceHighWatermark >= long.MaxValue - 1) return false;
            IssuanceHighWatermark++;
            nextItemId = $"{prefix}-{IssuanceHighWatermark:D6}";
            return true;
        }

        public static string ComputeMaterialBatchSignature(MaterialBatchRequest request)
        {
            var sb = new StringBuilder(128);
            sb.Append(request.requestId).Append(':')
              .Append(request.worldId).Append(':')
              .Append(request.generationId).Append(':')
              .Append(request.actorId).Append(':')
              .Append(request.commandName).Append(':')
              .Append(request.sourcePlaceId).Append(':');
            if (request.effects != null)
            {
                foreach (var eff in request.effects)
                {
                    sb.Append((int)eff.kind).Append(',')
                      .Append(eff.itemId).Append(',')
                      .Append(eff.itemTypeId).Append(',')
                      .Append((int)eff.destinationLocation).Append(',')
                      .Append(eff.destinationContainerId).Append(';');
                }
            }
            return ItemPersistence.ComputeSha256(sb.ToString());
        }

        public bool TryPrepareMaterialBatch(
            MaterialBatchRequest request,
            IMaterialBatchAuthority authority,
            out PreparedMaterialBatch token,
            out string rejection)
        {
            token = null;
            rejection = null;

            if (!IsDetachedFork || !IsManaged)
            {
                rejection = "material-batches-require-managed-detached-candidate";
                return false;
            }

            if (request.requestId <= 0)
            {
                rejection = "invalid-request-id";
                return false;
            }

            if (!string.Equals(request.worldId, WorldId, StringComparison.Ordinal) ||
                !string.Equals(request.generationId, GenerationId, StringComparison.Ordinal) ||
                !IsValidId(request.actorId))
            {
                rejection = "scope-mismatch";
                return false;
            }

            string signature = ComputeMaterialBatchSignature(request);

            if (materialReceipts.TryGetValue(request.requestId, out var existingRecord))
            {
                if (string.Equals(existingRecord.signature, signature, StringComparison.Ordinal))
                {
                    rejection = "duplicate-replay";
                    return false;
                }
                rejection = "conflicting-request-id";
                return false;
            }

            if (request.requestId <= HighestReceiptRequestId)
            {
                rejection = "stale-request-sequence";
                return false;
            }

            if (authority == null || !authority.AuthorizeBatch(this, request, out rejection))
            {
                if (string.IsNullOrEmpty(rejection)) rejection = "authority-denied";
                return false;
            }

            if (request.effects == null || request.effects.Count == 0)
            {
                rejection = "empty-effects";
                return false;
            }

            int consumeCount = 0;
            int issueCount = 0;
            foreach (var eff in request.effects)
            {
                if (eff.kind == MaterialEffectKind.Consume || eff.kind == MaterialEffectKind.PlantSeed)
                    consumeCount++;
                else if (eff.kind == MaterialEffectKind.Issue)
                    issueCount++;
            }

            if (tombstones.Count + consumeCount > MaxTombstoneLedgerSize)
            {
                rejection = "tombstone-ledger-overflow";
                return false;
            }

            var batchItemsToConsume = new List<string>();
            var batchTombstones = new List<TombstoneRecord>();
            var batchNewItems = new List<ItemStateSnapshot>();
            var simulatedItems = new Dictionary<string, ItemStateSnapshot>(items, StringComparer.Ordinal);
            var simulatedDefs = new Dictionary<string, ItemDefinition>(definitions, StringComparer.Ordinal);

            // Phase 1: validate and apply consumes to simulated set
            foreach (var eff in request.effects)
            {
                if (eff.kind == MaterialEffectKind.Consume || eff.kind == MaterialEffectKind.PlantSeed)
                {
                    if (string.IsNullOrEmpty(eff.itemId) || !simulatedItems.TryGetValue(eff.itemId, out var existingItem))
                    {
                        rejection = "consume-item-not-found";
                        return false;
                    }

                    if (batchItemsToConsume.Contains(eff.itemId))
                    {
                        rejection = "duplicate-consume-in-batch";
                        return false;
                    }

                    // Check if item is container holding items
                    if (simulatedDefs.TryGetValue(existingItem.itemTypeId, out var existingDef) && existingDef.isContainer)
                    {
                        foreach (var kvp in simulatedItems)
                        {
                            if (kvp.Value.location == ItemLocationKind.Stored && string.Equals(kvp.Value.containerItemId, eff.itemId, StringComparison.Ordinal))
                            {
                                rejection = "cannot-consume-nonempty-container";
                                return false;
                            }
                        }
                    }

                    // Consume removes from simulated set
                    simulatedItems.Remove(eff.itemId);
                    batchItemsToConsume.Add(eff.itemId);

                    batchTombstones.Add(new TombstoneRecord
                    {
                        itemId = eff.itemId,
                        itemTypeId = existingItem.itemTypeId,
                        retiredTick = Tick,
                        retiredByActorId = request.actorId,
                        reason = eff.kind == MaterialEffectKind.PlantSeed ? "planted" : "consumed",
                        provenance = eff.provenance ?? request.commandName
                    });
                }
            }

            // Phase 2: validate issues against simulated set
            foreach (var eff in request.effects)
            {
                if (eff.kind == MaterialEffectKind.Issue)
                {
                    if (!IsValidId(eff.itemId) || simulatedItems.ContainsKey(eff.itemId) || tombstones.ContainsKey(eff.itemId))
                    {
                        rejection = "issue-item-id-invalid-or-duplicate";
                        return false;
                    }

                    if (string.IsNullOrEmpty(eff.itemTypeId) || !definitions.TryGetValue(eff.itemTypeId, out var itemDef))
                    {
                        rejection = "issue-definition-missing";
                        return false;
                    }

                    if (itemDef.isAnchored)
                    {
                        rejection = "cannot-issue-anchored-item";
                        return false;
                    }

                    if (!ItemDefinition.Finite(eff.position.x) || !ItemDefinition.Finite(eff.position.y) || !ItemDefinition.Finite(eff.position.z))
                    {
                        rejection = "nonfinite-position";
                        return false;
                    }

                    if (!ItemDefinition.TryCanonicalizeRotation(eff.rotation, out var canonRot))
                    {
                        rejection = "non-canonical-rotation";
                        return false;
                    }

                    int assignedSlot = -1;
                    string containerId = null;
                    string holderActor = null;

                    if (eff.destinationLocation == ItemLocationKind.Carried)
                    {
                        holderActor = request.actorId;
                    }
                    else if (eff.destinationLocation == ItemLocationKind.Stored)
                    {
                        if (string.IsNullOrEmpty(eff.destinationContainerId) || !simulatedItems.TryGetValue(eff.destinationContainerId, out var targetContainer))
                        {
                            rejection = "destination-container-missing";
                            return false;
                        }

                        if (!simulatedDefs.TryGetValue(targetContainer.itemTypeId, out var targetDef) || !targetDef.isContainer)
                        {
                            rejection = "destination-not-a-container";
                            return false;
                        }

                        containerId = eff.destinationContainerId;

                        // Count existing items in container
                        int directCount = 0;
                        float directVolume = itemDef.dimensions.VolumeM3;
                        var occupiedSlots = new HashSet<int>();

                        foreach (var kvp in simulatedItems)
                        {
                            if (kvp.Value.location == ItemLocationKind.Stored && string.Equals(kvp.Value.containerItemId, containerId, StringComparison.Ordinal))
                            {
                                directCount++;
                                if (kvp.Value.containerSlot >= 0) occupiedSlots.Add(kvp.Value.containerSlot);
                                if (simulatedDefs.TryGetValue(kvp.Value.itemTypeId, out var childDef))
                                {
                                    directVolume += childDef.dimensions.VolumeM3;
                                }
                            }
                        }

                        if (directCount + 1 > targetDef.maxContainedSlots)
                        {
                            rejection = "destination-container-slots-full";
                            return false;
                        }

                        if (directVolume > targetDef.maxContainedVolumeM3)
                        {
                            rejection = "destination-container-volume-exceeded";
                            return false;
                        }

                        // Find first free slot
                        for (int s = 0; s < targetDef.maxContainedSlots; s++)
                        {
                            if (!occupiedSlots.Contains(s))
                            {
                                assignedSlot = s;
                                break;
                            }
                        }
                    }
                    else if (eff.destinationLocation == ItemLocationKind.Free)
                    {
                        // Free item on ground
                    }
                    else
                    {
                        rejection = "unsupported-destination-location";
                        return false;
                    }

                    var newSnap = new ItemStateSnapshot
                    {
                        itemId = eff.itemId,
                        itemTypeId = eff.itemTypeId,
                        location = eff.destinationLocation,
                        holderActorId = holderActor,
                        containerItemId = containerId,
                        containerSlot = assignedSlot,
                        placedSupportId = null,
                        position = eff.position,
                        rotation = canonRot,
                        lastUpdatedTick = Tick
                    };

                    simulatedItems.Add(eff.itemId, newSnap);
                    batchNewItems.Add(newSnap);
                }
            }

            // Phase 3: validate actor carry limits over the whole simulated state
            int actorCarriedCount = 0;
            float actorCarriedMass = 0f;
            var limits = GetActorCarryLimits(request.actorId);

            foreach (var kvp in simulatedItems)
            {
                if (kvp.Value.location == ItemLocationKind.Carried && string.Equals(kvp.Value.holderActorId, request.actorId, StringComparison.Ordinal))
                {
                    actorCarriedCount++;
                    actorCarriedMass += CalculateSimulatedTotalMass(kvp.Key, simulatedItems, simulatedDefs);
                }
            }

            if (actorCarriedCount > limits.maxCarriedItems)
            {
                rejection = "actor-carry-slots-exceeded";
                return false;
            }

            if (actorCarriedMass > limits.maxCarryMassKg)
            {
                rejection = "actor-carry-mass-exceeded";
                return false;
            }

            // All checks passed! Create revision-bound token
            token = new PreparedMaterialBatch(
                nextMaterialTokenId++,
                Revision,
                request,
                batchNewItems,
                batchItemsToConsume,
                batchTombstones);

            activeMaterialTokens[token.TokenId] = token;
            return true;
        }

        private static float CalculateSimulatedTotalMass(
            string itemId,
            Dictionary<string, ItemStateSnapshot> simItems,
            Dictionary<string, ItemDefinition> simDefs)
        {
            if (!simItems.TryGetValue(itemId, out var item) || !simDefs.TryGetValue(item.itemTypeId, out var def))
                return 0f;

            float total = def.massKg;
            if (def.isContainer)
            {
                foreach (var kvp in simItems)
                {
                    if (kvp.Value.location == ItemLocationKind.Stored && string.Equals(kvp.Value.containerItemId, itemId, StringComparison.Ordinal))
                    {
                        total += CalculateSimulatedTotalMass(kvp.Key, simItems, simDefs);
                    }
                }
            }
            return total;
        }

        public bool TryCommitMaterialBatch(PreparedMaterialBatch token, out MaterialBatchReceipt receipt)
        {
            receipt = default;

            if (token == null || token.IsCommitted || token.IsCancelled)
                return false;

            if (token.BoundRevision != Revision)
                return false;

            if (!activeMaterialTokens.ContainsKey(token.TokenId))
                return false;

            // Apply consumes: remove from items, add to tombstones
            var retiredIds = new List<string>(token.ItemsToConsume.Count);
            foreach (var t in token.GeneratedTombstones)
            {
                items.Remove(t.itemId);
                tombstones[t.itemId] = t;
                retiredIds.Add(t.itemId);
            }

            // Apply issues: add to items
            var issuedIds = new List<string>(token.NewItemsToRegister.Count);
            foreach (var item in token.NewItemsToRegister)
            {
                items.Add(item.itemId, item);
                issuedIds.Add(item.itemId);
            }

            int actorCarriedCount = 0;
            float actorCarriedMass = 0f;
            foreach (var kvp in items)
            {
                if (kvp.Value.location == ItemLocationKind.Carried && string.Equals(kvp.Value.holderActorId, token.Request.actorId, StringComparison.Ordinal))
                {
                    actorCarriedCount++;
                    actorCarriedMass += GetItemTotalMassKg(kvp.Key);
                }
            }

            receipt = new MaterialBatchReceipt
            {
                requestId = token.Request.requestId,
                worldId = WorldId,
                generationId = GenerationId,
                actorId = token.Request.actorId,
                commandName = token.Request.commandName,
                success = true,
                duplicate = false,
                code = "material-batch-committed",
                issuedItemIds = issuedIds,
                retiredItemIds = retiredIds,
                totalCarriedMassKg = actorCarriedMass,
                totalCarriedCount = actorCarriedCount
            };

            string sig = ComputeMaterialBatchSignature(token.Request);
            materialReceipts[token.Request.requestId] = new SavedMaterialBatchReceiptRecord
            {
                requestId = token.Request.requestId,
                signature = sig,
                receipt = receipt
            };

            if (token.Request.requestId > HighestReceiptRequestId)
            {
                HighestReceiptRequestId = token.Request.requestId;
            }

            token.IsCommitted = true;
            BumpRevision();
            return true;
        }

        public bool CancelMaterialBatch(PreparedMaterialBatch token)
        {
            if (token == null || token.IsCommitted) return false;
            token.IsCancelled = true;
            return activeMaterialTokens.Remove(token.TokenId);
        }

    }
}