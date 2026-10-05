using System;
using UnityEngine;

namespace CityLife.Stones
{
    /// <summary>
    /// Distinguishable hand-carryable stone shape archetypes with distinct geometric profiles.
    /// </summary>
    public enum StoneShapeKind
    {
        /// <summary>
        /// Water-smoothed, rounded, oblate/prolate pebble or river cobble.
        /// Low-frequency organic deformation without sharp cleavage creases.
        /// </summary>
        RiverCobble = 0,

        /// <summary>
        /// Sharp, angular, faceted quarry stone or fractured fieldstone.
        /// Features planar cleavage facets and blocky proportions.
        /// </summary>
        Fieldstone = 1,

        /// <summary>
        /// Broad, tabular slab or paver with flattened top/bottom planes.
        /// Low height-to-width aspect ratio suitable for hearth flooring or capping.
        /// </summary>
        FlatSlab = 2,

        /// <summary>
        /// Asymmetric, tapered teardrop/wedge shape designed for ergonomic hand grip.
        /// Thicker bulbous palm base tapering to a narrower striking edge.
        /// </summary>
        Handstone = 3
    }

    /// <summary>
    /// Standalone configuration parameters for deterministic stone mesh generation.
    /// </summary>
    [Serializable]
    public struct StoneGenerationConfig
    {
        /// <summary>
        /// Minimum supported uniform scale factor (0.25x = ~4-7cm pebble).
        /// Rejects smaller values to prevent degenerate sub-centimetre geometry.
        /// </summary>
        public const float MinSupportedScale = 0.25f;

        /// <summary>
        /// Maximum supported uniform scale factor (3.00x = ~50-84cm boulder/hearth slab).
        /// Rejects larger values to maintain sensible metre-scale stone envelopes.
        /// </summary>
        public const float MaxSupportedScale = 3.00f;

        public StoneShapeKind shape;
        public int seed;
        public int variantIndex;
        public float uniformScale;
        public bool flatShaded;

        /// <summary>
        /// Initializes a stone generation configuration with validated parameters.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when shape is not a defined StoneShapeKind or uniformScale is non-finite or outside [0.25, 3.00].
        /// </exception>
        public StoneGenerationConfig(
            StoneShapeKind shape,
            int seed = 0,
            int variantIndex = 0,
            float uniformScale = 1.0f,
            bool flatShaded = true)
        {
            this.shape = shape;
            this.seed = seed;
            this.variantIndex = variantIndex;
            this.uniformScale = uniformScale;
            this.flatShaded = flatShaded;
            Validate();
        }

        /// <summary>
        /// Produces a verified default configuration for the specified stone shape.
        /// </summary>
        public static StoneGenerationConfig DefaultFor(StoneShapeKind shape, int seed = 0, int variantIndex = 0)
        {
            return new StoneGenerationConfig(shape, seed, variantIndex, 1.0f, true);
        }

        /// <summary>
        /// Validates this configuration against defined shape enums and the supported finite scale envelope.
        /// Note: default(StoneGenerationConfig) leaves uniformScale = 0.0f, which is rejected by this method.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if shape is undefined or uniformScale is non-finite or outside [MinSupportedScale, MaxSupportedScale].
        /// </exception>
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(StoneShapeKind), shape))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shape),
                    shape,
                    $"Invalid stone shape kind: {(int)shape}. Supported shapes are RiverCobble (0), Fieldstone (1), FlatSlab (2), and Handstone (3).");
            }

            if (float.IsNaN(uniformScale) || float.IsInfinity(uniformScale) || uniformScale < MinSupportedScale || uniformScale > MaxSupportedScale)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(uniformScale),
                    uniformScale,
                    $"Uniform scale {uniformScale} is outside the supported finite range [{MinSupportedScale:F2}, {MaxSupportedScale:F2}].");
            }
        }
    }

    /// <summary>
    /// Explicit metre-scale physical and geometric metadata produced alongside a stone mesh.
    /// Documents intended design mass and volumetric assumptions without claiming physical simulation evidence.
    /// </summary>
    [Serializable]
    public struct StoneMetadata
    {
        public StoneShapeKind shape;
        public int seed;
        public int variantIndex;
        public float uniformScale;
        public bool flatShaded;

        // Metre-scale bounding box dimensions
        public float widthMetres;
        public float heightMetres;
        public float depthMetres;
        public float boundingVolumeM3;

        // Volumetric design assumptions (design metadata, not measured physics simulation)
        public float formFactor;
        public float estimatedVolumeM3;
        public float assumedDensityKgPerM3;
        public float intendedMassKg;

        // Mesh metrics
        public int vertexCount;
        public int triangleCount;
        public Vector3 boundsMin;
        public Vector3 boundsMax;
        public Vector3 pivotOffset;

        /// <summary>
        /// Verifies dimensions are strictly positive, finite, and within valid item range (<= 20m).
        /// </summary>
        public bool HasValidDimensions =>
            !float.IsNaN(widthMetres) && !float.IsInfinity(widthMetres) && widthMetres > 0.001f && widthMetres <= 20.0f &&
            !float.IsNaN(heightMetres) && !float.IsInfinity(heightMetres) && heightMetres > 0.001f && heightMetres <= 20.0f &&
            !float.IsNaN(depthMetres) && !float.IsInfinity(depthMetres) && depthMetres > 0.001f && depthMetres <= 20.0f;

        /// <summary>
        /// Verifies whether the intended mass falls into reasonable hand-carryable range for an inhabitant (0.5 kg to 25.0 kg).
        /// </summary>
        public bool IsHandCarryable =>
            !float.IsNaN(intendedMassKg) && !float.IsInfinity(intendedMassKg) &&
            intendedMassKg >= 0.5f && intendedMassKg <= 25.0f;

        public override string ToString()
        {
            return $"{shape} (seed={seed}, var={variantIndex}, flat={flatShaded}): {widthMetres:F3}m x {heightMetres:F3}m x {depthMetres:F3}m, " +
                   $"intendedMass={intendedMassKg:F2}kg (density={assumedDensityKgPerM3:F0}kg/m³), verts={vertexCount}, tris={triangleCount}";
        }
    }

    /// <summary>
    /// Bundled result of stone mesh generation containing the standalone Mesh and its explicit metadata.
    /// </summary>
    public struct StoneGenerationResult
    {
        public Mesh mesh;
        public StoneMetadata metadata;

        public StoneGenerationResult(Mesh mesh, StoneMetadata metadata)
        {
            this.mesh = mesh;
            this.metadata = metadata;
        }
    }
}
