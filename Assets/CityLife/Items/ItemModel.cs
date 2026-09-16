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

        public int ItemCount => items.Count;
        public int DefinitionCount => definitions.Count;

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
            return true;
        }

        public bool RegisterItem(string itemId, string itemTypeId, ItemLocationKind initialLocation, Vector3 position, Quaternion rotation, string supportId = null)
        {
            if (!IsValidId(itemId) || items.ContainsKey(itemId))
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
                placedSupportId = supportId,
                position = position,
                rotation = canonicalRotation,
                lastUpdatedTick = Tick
            };

            items.Add(itemId, state);
            return true;
        }

        public bool SetActorCarryLimits(string actorId, ActorCarryLimits limits)
        {
            if (!IsValidId(actorId) || !limits.IsValid())
                return false;
            actorLimits[actorId] = limits;
            return true;
        }

        public ActorCarryLimits GetActorCarryLimits(string actorId)
        {
            if (!string.IsNullOrEmpty(actorId) && actorLimits.TryGetValue(actorId, out var limits))
                return limits;
            return new ActorCarryLimits(25f, 1);
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

        public void AdvanceTick() => Tick++;

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
                return receipt;
            }

            ItemReceipt Reject(string code) => Finish(Deny(request, code));

            if (!items.TryGetValue(request.itemId, out var item))
            {
                return Reject("item-not-found");
            }

            if (!definitions.TryGetValue(item.itemTypeId, out var def))
            {
                return Reject("item-definition-missing");
            }

            var limits = GetActorCarryLimits(request.actorId);

            switch (request.action)
            {
                case ItemActionKind.Pickup:
                {
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

                case ItemActionKind.Store:
                {
                    if (item.location != ItemLocationKind.Carried || !string.Equals(item.holderActorId, request.actorId, StringComparison.Ordinal))
                    {
                        return Reject("item-not-carried-by-actor");
                    }

                    if (string.IsNullOrEmpty(request.targetId) || !IsValidId(request.targetId))
                    {
                        return Reject("invalid-scope-id");
                    }

                    if (string.Equals(request.itemId, request.targetId, StringComparison.Ordinal))
                    {
                        return Reject("cannot-store-in-itself");
                    }

                    if (!items.TryGetValue(request.targetId, out var containerItem))
                    {
                        return Reject("target-container-not-found");
                    }

                    if (!definitions.TryGetValue(containerItem.itemTypeId, out var containerDef) || !containerDef.isContainer)
                    {
                        return Reject("target-not-a-container");
                    }

                    string rootHolder = GetContainerRootHolder(request.targetId);
                    if (authority == null)
                    {
                        return Reject("container-authority-missing");
                    }
                    if (!authority.AuthorizeContainerAccess(request.actorId, request.targetId, rootHolder))
                    {
                        return Reject("container-access-denied");
                    }

                    if (WouldCreateContainmentCycle(request.itemId, request.targetId))
                    {
                        return Reject("containment-cycle-detected");
                    }

                    var contents = GetContainerContents(request.targetId);
                    if (contents.Count >= containerDef.maxContainedSlots)
                    {
                        return Reject("container-slot-capacity-exceeded");
                    }

                    float currentContainedMass = GetContainerContainedMassKg(request.targetId);
                    float itemTotalMass = GetItemTotalMassKg(request.itemId);
                    if (currentContainedMass + itemTotalMass > containerDef.maxContainedMassKg)
                    {
                        return Reject("container-mass-capacity-exceeded");
                    }

                    float currentContainedVolume = GetContainerContainedVolumeM3(request.targetId);
                    if (currentContainedVolume + def.dimensions.VolumeM3 > containerDef.maxContainedVolumeM3)
                    {
                        return Reject("container-volume-capacity-exceeded");
                    }

                    // Validate all ancestor containers in nested hierarchy
                    foreach (var ancestorId in GetContainerAncestors(request.targetId))
                    {
                        if (items.TryGetValue(ancestorId, out var ancestorState) &&
                            definitions.TryGetValue(ancestorState.itemTypeId, out var ancestorDef) &&
                            ancestorDef.isContainer)
                        {
                            if (GetContainerContainedMassKg(ancestorId) + itemTotalMass > ancestorDef.maxContainedMassKg)
                            {
                                return Reject("ancestor-container-mass-capacity-exceeded");
                            }
                        }
                    }

                    // If container hierarchy is carried by another actor, verify that actor's carry limit
                    if (!string.IsNullOrEmpty(rootHolder) && !string.Equals(rootHolder, request.actorId, StringComparison.Ordinal))
                    {
                        var otherLimits = GetActorCarryLimits(rootHolder);
                        float otherMass = GetActorCarriedMassKg(rootHolder);
                        if (otherMass + itemTotalMass > otherLimits.maxCarryMassKg)
                        {
                            return Reject("carry-mass-capacity-exceeded");
                        }
                    }

                    item.location = ItemLocationKind.Stored;
                    item.holderActorId = null;
                    item.placedSupportId = null;
                    item.containerItemId = request.targetId;
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
                        code = "stored-in-container",
                        totalCarriedMassKg = GetActorCarriedMassKg(request.actorId)
                    });
                }

                case ItemActionKind.Retrieve:
                {
                    if (string.IsNullOrEmpty(request.targetId) || !IsValidId(request.targetId))
                    {
                        return Reject("invalid-scope-id");
                    }

                    if (item.location != ItemLocationKind.Stored || !string.Equals(item.containerItemId, request.targetId, StringComparison.Ordinal))
                    {
                        return Reject("item-not-stored-in-target");
                    }

                    if (!items.TryGetValue(request.targetId, out var containerItem))
                    {
                        return Reject("target-container-not-found");
                    }

                    string rootHolder = GetContainerRootHolder(request.targetId);
                    if (authority == null)
                    {
                        return Reject("container-authority-missing");
                    }
                    if (!authority.AuthorizeContainerAccess(request.actorId, request.targetId, rootHolder))
                    {
                        return Reject("container-access-denied");
                    }

                    var carried = GetActorCarriedItemIds(request.actorId);
                    if (carried.Count >= limits.maxCarriedItems)
                    {
                        return Reject("actor-hands-full");
                    }

                    float itemTotalMass = GetItemTotalMassKg(request.itemId);
                    bool alreadyCarriedByThisActor = string.Equals(rootHolder, request.actorId, StringComparison.Ordinal);
                    float netMassChange = alreadyCarriedByThisActor ? 0f : itemTotalMass;

                    float currentActorMass = GetActorCarriedMassKg(request.actorId);
                    if (currentActorMass + netMassChange > limits.maxCarryMassKg)
                    {
                        return Reject("carry-mass-capacity-exceeded");
                    }

                    item.location = ItemLocationKind.Carried;
                    item.holderActorId = request.actorId;
                    item.containerItemId = null;
                    item.placedSupportId = null;
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
                        code = "retrieved-from-container",
                        totalCarriedMassKg = GetActorCarriedMassKg(request.actorId)
                    });
                }

                default:
                    return Reject("unsupported-action");
            }
        }
    }
}
