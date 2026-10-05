using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.Items
{
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
        public string provenance; // e.g. "biological-harvest", "consumed-fruit-residue", "assisted-camp-aid", "planted-seed"
    }

    [Serializable]
    public struct MaterialBatchRequest
    {
        public int requestId;
        public string worldId;
        public string generationId;
        public string actorId;
        public string commandName;      // "Gather", "Eat", "Harvest", "Plant", "CampAid"
        public string sourcePlaceId;    // for provenance audit
        public List<MaterialBatchItemEffect> effects;
    }

    [Serializable]
    public struct TombstoneRecord
    {
        public string itemId;
        public string itemTypeId;
        public long retiredTick;
        public string retiredByActorId;
        public string reason;           // "consumed", "planted", etc.
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

    [Serializable]
    public sealed class SavedMaterialBatchReceiptRecord
    {
        public int requestId;
        public string signature;
        public MaterialBatchReceipt receipt;
    }

    public interface IMaterialBatchAuthority
    {
        bool AuthorizeBatch(ItemModel model, MaterialBatchRequest request, out string failureReason);
    }

    public sealed class BasicMaterialBatchAuthority : IMaterialBatchAuthority
    {
        public bool allowAll = true;
        public string requiredActorId = null;

        public bool AuthorizeBatch(ItemModel model, MaterialBatchRequest request, out string failureReason)
        {
            failureReason = null;
            if (!allowAll)
            {
                failureReason = "authority-refused";
                return false;
            }
            if (requiredActorId != null && !string.Equals(request.actorId, requiredActorId, StringComparison.Ordinal))
            {
                failureReason = "actor-mismatch";
                return false;
            }
            if (request.effects == null || request.effects.Count == 0)
            {
                failureReason = "empty-effects";
                return false;
            }
            return true;
        }
    }

    public sealed class PreparedMaterialBatch
    {
        public int TokenId { get; }
        public long BoundRevision { get; }
        public MaterialBatchRequest Request { get; }
        public List<ItemStateSnapshot> NewItemsToRegister { get; }
        public List<string> ItemsToConsume { get; }
        public List<TombstoneRecord> GeneratedTombstones { get; }
        public bool IsCommitted { get; internal set; }
        public bool IsCancelled { get; internal set; }

        internal PreparedMaterialBatch(
            int tokenId,
            long boundRevision,
            MaterialBatchRequest request,
            List<ItemStateSnapshot> newItems,
            List<string> itemsToConsume,
            List<TombstoneRecord> tombstones)
        {
            TokenId = tokenId;
            BoundRevision = boundRevision;
            Request = request;
            NewItemsToRegister = newItems ?? new List<ItemStateSnapshot>();
            ItemsToConsume = itemsToConsume ?? new List<string>();
            GeneratedTombstones = tombstones ?? new List<TombstoneRecord>();
            IsCommitted = false;
            IsCancelled = false;
        }
    }
}
