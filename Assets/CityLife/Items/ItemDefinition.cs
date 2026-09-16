using System;
using UnityEngine;

namespace CityLife.Items
{
    /// <summary>
    /// Sensible finite positive physical bounding dimensions in metres.
    /// </summary>
    [Serializable]
    public struct PhysicalDimensions
    {
        public float width;
        public float height;
        public float depth;

        public PhysicalDimensions(float width, float height, float depth)
        {
            this.width = width;
            this.height = height;
            this.depth = depth;
        }

        public float VolumeM3 => width * height * depth;

        public bool IsValid()
        {
            return ItemDefinition.Finite(width) && width > 0f && width <= 20f &&
                   ItemDefinition.Finite(height) && height > 0f && height <= 20f &&
                   ItemDefinition.Finite(depth) && depth > 0f && depth <= 20f;
        }
    }

    /// <summary>
    /// Immutable item definition declaring physical dimensions, dry mass, container properties, and anchoring.
    /// Copies on ingress/egress to prevent external aliasing.
    /// </summary>
    [Serializable]
    public sealed class ItemDefinition
    {
        public string itemTypeId;
        public PhysicalDimensions dimensions;
        public float massKg;
        public bool isContainer;
        public float maxContainedMassKg;
        public float maxContainedVolumeM3;
        public int maxContainedSlots;
        public bool isAnchored;
        public bool requiresSupportToPlace;

        public ItemDefinition() { }

        public ItemDefinition(ItemDefinition source)
        {
            if (source == null) return;
            itemTypeId = source.itemTypeId;
            dimensions = source.dimensions;
            massKg = source.massKg;
            isContainer = source.isContainer;
            maxContainedMassKg = source.maxContainedMassKg;
            maxContainedVolumeM3 = source.maxContainedVolumeM3;
            maxContainedSlots = source.maxContainedSlots;
            isAnchored = source.isAnchored;
            requiresSupportToPlace = source.requiresSupportToPlace;
        }

        public ItemDefinition Clone() => new ItemDefinition(this);

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>
        /// Canonical rotation validation and normalization rule.
        /// Rejects NaN, infinity, zero, near-zero, and non-unit quaternions.
        /// Normalizes near-unit quaternions to exact unit length.
        /// </summary>
        public static bool TryCanonicalizeRotation(Quaternion q, out Quaternion normalized)
        {
            normalized = Quaternion.identity;
            if (!Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w))
                return false;

            float sqrMag = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            // Reject zero, near-zero, and non-unit quaternions (must be within [0.98, 1.02])
            if (sqrMag < 0.98f || sqrMag > 1.02f)
                return false;

            float invMag = 1.0f / Mathf.Sqrt(sqrMag);
            normalized = new Quaternion(q.x * invMag, q.y * invMag, q.z * invMag, q.w * invMag);
            return true;
        }

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(itemTypeId) || !ItemModel.IsValidId(itemTypeId))
                return false;

            if (!dimensions.IsValid())
                return false;

            if (!Finite(massKg) || massKg <= 0f || massKg > 10000f)
                return false;

            if (isContainer)
            {
                if (!Finite(maxContainedMassKg) || maxContainedMassKg <= 0f || maxContainedMassKg > 10000f)
                    return false;
                if (!Finite(maxContainedVolumeM3) || maxContainedVolumeM3 <= 0f || maxContainedVolumeM3 > 1000f)
                    return false;
                if (maxContainedSlots <= 0 || maxContainedSlots > 256)
                    return false;
            }
            else
            {
                if (maxContainedMassKg != 0f || maxContainedVolumeM3 != 0f || maxContainedSlots != 0)
                    return false;
            }

            return true;
        }
    }
}
