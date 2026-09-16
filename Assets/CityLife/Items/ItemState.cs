using System;
using UnityEngine;

namespace CityLife.Items
{
    /// <summary>
    /// Explicit, mutually exclusive item location states.
    /// </summary>
    public enum ItemLocationKind
    {
        Free,       // Unattached in the world, unowned, awaiting or subject to physics
        Carried,    // Held by an actor (single authoritative actor owner)
        Stored,     // Contained inside another item (single authoritative container)
        Placed,     // Rested/mounted on a supported surface/socket in the world
        Anchored    // Permanently anchored scenery/terrain fixture (cannot be picked up)
    }

    /// <summary>
    /// Safe snapshot of an item's authoritative state.
    /// </summary>
    [Serializable]
    public sealed class ItemStateSnapshot
    {
        public string itemId;
        public string itemTypeId;
        public ItemLocationKind location;
        public string holderActorId;
        public string containerItemId;
        public string placedSupportId;
        public Vector3 position;
        public Quaternion rotation;
        public long lastUpdatedTick;

        public ItemStateSnapshot() { }

        public ItemStateSnapshot(ItemStateSnapshot source)
        {
            if (source == null) return;
            itemId = source.itemId;
            itemTypeId = source.itemTypeId;
            location = source.location;
            holderActorId = source.holderActorId;
            containerItemId = source.containerItemId;
            placedSupportId = source.placedSupportId;
            position = source.position;
            rotation = source.rotation;
            lastUpdatedTick = source.lastUpdatedTick;
        }

        public ItemStateSnapshot Clone() => new ItemStateSnapshot(this);

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(itemId) || !ItemModel.IsValidId(itemId)) return false;
            if (string.IsNullOrEmpty(itemTypeId) || !ItemModel.IsValidId(itemTypeId)) return false;
            if (!ItemDefinition.Finite(position.x) || !ItemDefinition.Finite(position.y) || !ItemDefinition.Finite(position.z)) return false;
            if (!ItemDefinition.TryCanonicalizeRotation(rotation, out _)) return false;
            if (lastUpdatedTick < 0) return false;

            switch (location)
            {
                case ItemLocationKind.Free:
                    return string.IsNullOrEmpty(holderActorId) &&
                           string.IsNullOrEmpty(containerItemId) &&
                           string.IsNullOrEmpty(placedSupportId);

                case ItemLocationKind.Carried:
                    return !string.IsNullOrEmpty(holderActorId) &&
                           ItemModel.IsValidId(holderActorId) &&
                           string.IsNullOrEmpty(containerItemId) &&
                           string.IsNullOrEmpty(placedSupportId);

                case ItemLocationKind.Stored:
                    return string.IsNullOrEmpty(holderActorId) &&
                           !string.IsNullOrEmpty(containerItemId) &&
                           ItemModel.IsValidId(containerItemId) &&
                           string.IsNullOrEmpty(placedSupportId);

                case ItemLocationKind.Placed:
                    return string.IsNullOrEmpty(holderActorId) &&
                           string.IsNullOrEmpty(containerItemId) &&
                           !string.IsNullOrEmpty(placedSupportId) &&
                           ItemModel.IsValidId(placedSupportId);

                case ItemLocationKind.Anchored:
                    return string.IsNullOrEmpty(holderActorId) &&
                           string.IsNullOrEmpty(containerItemId) &&
                           string.IsNullOrEmpty(placedSupportId);

                default:
                    return false;
            }
        }
    }
}
