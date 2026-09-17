using System;
using UnityEngine;

namespace CityLife.Wood
{
    /// <summary>
    /// Configuration profile defining the morphological, dimensional, and aesthetic
    /// parameters for procedurally generating a fallen branch or log mesh.
    /// All spatial dimensions are expressed strictly in local metres.
    /// </summary>
    [Serializable]
    public sealed class FallenWoodProfile
    {
        [Header("Identity")]
        public string profileName = "Fallen Wood";

        [Header("Dimensions (Local Metres)")]
        [Tooltip("Length of primary trunk/stem along the spine in metres.")]
        public float length = 0.65f;

        [Tooltip("Cross-sectional radius at the thicker base end in metres.")]
        public float baseRadius = 0.028f;

        [Tooltip("Cross-sectional radius at the tapered tip end in metres.")]
        public float tipRadius = 0.016f;

        [Header("Mesh Topology")]
        [Tooltip("Number of radial vertices around the circular cross-section.")]
        [Range(4, 24)]
        public int radialSegments = 10;

        [Tooltip("Number of longitudinal segments (rings) along the length.")]
        [Range(2, 32)]
        public int lengthSegments = 8;

        [Header("Morphology & Curvature")]
        [Tooltip("Maximum natural bow/deflection displacement along the stem in metres.")]
        public float curvature = 0.035f;

        [Tooltip("Amplitude of radial bark roughness displacement in metres.")]
        public float barkRoughness = 0.003f;

        [Header("Branch Fork (Optional)")]
        [Tooltip("Probability (0 to 1) of generating a secondary twig/branch fork.")]
        [Range(0f, 1f)]
        public float branchChance = 0.5f;

        [Tooltip("Length of the secondary branch fork in metres.")]
        public float branchLength = 0.22f;

        [Tooltip("Base radius of the secondary branch fork in metres.")]
        public float branchRadius = 0.012f;

        [Tooltip("Angle in degrees of the fork relative to trunk forward direction.")]
        [Range(15f, 75f)]
        public float branchAngleDeg = 45f;

        [Header("Physical Material Properties")]
        [Tooltip("Authored dry wood density estimate in kg/m^3 (~680 kg/m^3 for seasoned dry deadwood staging; not measured species data).")]
        public float dryDensityKgM3 = 680f;

        [Header("Authored Vertex Color Palettes")]
        [Tooltip("Primary base bark color tone (weathered trunk surface).")]
        public Color32 barkColorBase = new Color32(92, 76, 60, 255);

        [Tooltip("Secondary bark variation tone (lichen/weathering highlight).")]
        public Color32 barkColorVariation = new Color32(132, 114, 90, 255);

        [Tooltip("End-grain heartwood core color tone (exposed inner wood).")]
        public Color32 endGrainCoreColor = new Color32(188, 158, 118, 255);

        [Tooltip("End-grain sapwood/perimeter ring color tone.")]
        public Color32 endGrainSapColor = new Color32(218, 196, 162, 255);

        public FallenWoodProfile() { }

        public FallenWoodProfile(FallenWoodProfile source)
        {
            if (source == null) return;
            profileName = source.profileName;
            length = source.length;
            baseRadius = source.baseRadius;
            tipRadius = source.tipRadius;
            radialSegments = source.radialSegments;
            lengthSegments = source.lengthSegments;
            curvature = source.curvature;
            barkRoughness = source.barkRoughness;
            branchChance = source.branchChance;
            branchLength = source.branchLength;
            branchRadius = source.branchRadius;
            branchAngleDeg = source.branchAngleDeg;
            dryDensityKgM3 = source.dryDensityKgM3;
            barkColorBase = source.barkColorBase;
            barkColorVariation = source.barkColorVariation;
            endGrainCoreColor = source.endGrainCoreColor;
            endGrainSapColor = source.endGrainSapColor;
        }

        public FallenWoodProfile Clone() => new FallenWoodProfile(this);

        /// <summary>
        /// Sensible preset for a medium fallen tree branch (~0.65m length, ~0.45-0.65kg dry mass).
        /// </summary>
        public static FallenWoodProfile CreateBranchPreset()
        {
            return new FallenWoodProfile
            {
                profileName = "Fallen Branch",
                length = 0.65f,
                baseRadius = 0.028f,
                tipRadius = 0.016f,
                radialSegments = 10,
                lengthSegments = 8,
                curvature = 0.035f,
                barkRoughness = 0.003f,
                branchChance = 0.5f,
                branchLength = 0.22f,
                branchRadius = 0.012f,
                branchAngleDeg = 45f,
                dryDensityKgM3 = 680f,
                barkColorBase = new Color32(92, 76, 60, 255),
                barkColorVariation = new Color32(132, 114, 90, 255),
                endGrainCoreColor = new Color32(188, 158, 118, 255),
                endGrainSapColor = new Color32(218, 196, 162, 255)
            };
        }

        /// <summary>
        /// Sensible preset for a heavy fallen log (~1.15m length, ~15-20kg dry mass).
        /// </summary>
        public static FallenWoodProfile CreateLogProfile()
        {
            return new FallenWoodProfile
            {
                profileName = "Fallen Log",
                length = 1.15f,
                baseRadius = 0.095f,
                tipRadius = 0.075f,
                radialSegments = 12,
                lengthSegments = 10,
                curvature = 0.025f,
                barkRoughness = 0.006f,
                branchChance = 0.0f,
                branchLength = 0f,
                branchRadius = 0f,
                branchAngleDeg = 0f,
                dryDensityKgM3 = 680f,
                barkColorBase = new Color32(84, 68, 54, 255),
                barkColorVariation = new Color32(120, 102, 82, 255),
                endGrainCoreColor = new Color32(180, 150, 112, 255),
                endGrainSapColor = new Color32(210, 188, 152, 255)
            };
        }

        /// <summary>
        /// Sensible preset for a small kindling stick (~0.38m length, ~0.08-0.12kg dry mass).
        /// </summary>
        public static FallenWoodProfile CreateKindlingPreset()
        {
            return new FallenWoodProfile
            {
                profileName = "Fallen Kindling",
                length = 0.38f,
                baseRadius = 0.014f,
                tipRadius = 0.008f,
                radialSegments = 8,
                lengthSegments = 6,
                curvature = 0.020f,
                barkRoughness = 0.002f,
                branchChance = 0.35f,
                branchLength = 0.12f,
                branchRadius = 0.006f,
                branchAngleDeg = 40f,
                dryDensityKgM3 = 680f,
                barkColorBase = new Color32(100, 84, 68, 255),
                barkColorVariation = new Color32(142, 122, 98, 255),
                endGrainCoreColor = new Color32(195, 165, 125, 255),
                endGrainSapColor = new Color32(222, 202, 168, 255)
            };
        }

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(profileName))
                return false;

            if (!IsFinite(length, 0.05f, 10f) ||
                !IsFinite(baseRadius, 0.002f, 1f) ||
                !IsFinite(tipRadius, 0.001f, 1f) ||
                radialSegments < 4 || radialSegments > 32 ||
                lengthSegments < 2 || lengthSegments > 32 ||
                !IsFinite(curvature, 0f, 2f) ||
                !IsFinite(barkRoughness, 0f, 0.1f) ||
                !IsFinite(dryDensityKgM3, 100f, 2000f) ||
                !IsFinite(branchChance, 0f, 1f))
            {
                return false;
            }

            if (branchChance > 0f)
            {
                // Enabled forks require valid finite morphology parameters
                if (!IsFinite(branchLength, 0.02f, 5f) ||
                    !IsFinite(branchRadius, 0.001f, 1f) ||
                    !IsFinite(branchAngleDeg, 15f, 75f))
                {
                    return false;
                }
            }
            else
            {
                // Zero-branch presets with zero fork parameters remain valid
                if (!IsFinite(branchLength, 0f, 5f) ||
                    !IsFinite(branchRadius, 0f, 1f) ||
                    !IsFinite(branchAngleDeg, 0f, 75f))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFinite(float val, float min, float max)
        {
            return !float.IsNaN(val) && !float.IsInfinity(val) && val >= min && val <= max;
        }
    }
}
