using System;
using UnityEngine;

namespace CityLife.Wood
{
    /// <summary>
    /// Mutable serialized asset metadata container declaring stated bounding dimensions,
    /// dry mass, volume, and geometric topology for a procedurally generated fallen-wood asset.
    /// Authored estimates for asset metadata staging only; not measured botanical species data.
    /// No runtime physics or gameplay authority is claimed.
    /// </summary>
    [Serializable]
    public sealed class FallenWoodAssetMetadata
    {
        [Header("Asset Identity")]
        public string profileName;
        public uint seed;

        [Header("Physical Extents (Metres)")]
        public float width;
        public float height;
        public float depth;
        public Bounds localBounds;

        [Header("Physical Mass & Density")]
        public float massKg;
        public float volumeM3;
        public float dryDensityKgM3;

        [Header("Mesh Geometry Metrics")]
        public int vertexCount;
        public int triangleCount;
        public bool hasBranchFork;

        public FallenWoodAssetMetadata() { }

        public FallenWoodAssetMetadata(
            string profileName,
            uint seed,
            Bounds bounds,
            float volumeM3,
            float dryDensityKgM3,
            int vertexCount,
            int triangleCount,
            bool hasBranchFork)
        {
            this.profileName = profileName ?? "FallenWood";
            this.seed = seed;
            this.localBounds = bounds;
            this.width = bounds.size.x;
            this.height = bounds.size.y;
            this.depth = bounds.size.z;
            this.volumeM3 = volumeM3;
            this.dryDensityKgM3 = dryDensityKgM3;
            this.massKg = volumeM3 * dryDensityKgM3;
            this.vertexCount = vertexCount;
            this.triangleCount = triangleCount;
            this.hasBranchFork = hasBranchFork;
        }

        public FallenWoodAssetMetadata(FallenWoodAssetMetadata source)
        {
            if (source == null) return;
            profileName = source.profileName;
            seed = source.seed;
            width = source.width;
            height = source.height;
            depth = source.depth;
            localBounds = source.localBounds;
            massKg = source.massKg;
            volumeM3 = source.volumeM3;
            dryDensityKgM3 = source.dryDensityKgM3;
            vertexCount = source.vertexCount;
            triangleCount = source.triangleCount;
            hasBranchFork = source.hasBranchFork;
        }

        public FallenWoodAssetMetadata Clone() => new FallenWoodAssetMetadata(this);

        /// <summary>
        /// Explicit conversion bridge to CityLife.Items.PhysicalDimensions (real dependency on Assets/CityLife/Items/ItemDefinition.cs).
        /// Asset metadata only; does not claim inventory or runtime items registration.
        /// </summary>
        public CityLife.Items.PhysicalDimensions ToPhysicalDimensions()
        {
            return new CityLife.Items.PhysicalDimensions(width, height, depth);
        }

        /// <summary>
        /// Validates that extents, volume, mass, and vertex counts are finite, positive, and sensible.
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(profileName) &&
                   IsFinitePositive(width, 20f) &&
                   IsFinitePositive(height, 20f) &&
                   IsFinitePositive(depth, 20f) &&
                   IsFinitePositive(massKg, 1000f) &&
                   IsFinitePositive(volumeM3, 10f) &&
                   IsFinitePositive(dryDensityKgM3, 2500f) &&
                   vertexCount >= 12 &&
                   triangleCount >= 12;
        }

        private static bool IsFinitePositive(float val, float max)
        {
            return !float.IsNaN(val) && !float.IsInfinity(val) && val > 0f && val <= max;
        }

        public override string ToString()
        {
            return $"FallenWoodAssetMetadata[{profileName}, seed={seed}, size=({width:F3}m, {height:F3}m, {depth:F3}m), mass={massKg:F3}kg, vol={volumeM3:F5}m3, verts={vertexCount}, tris={triangleCount}]";
        }
    }
}
