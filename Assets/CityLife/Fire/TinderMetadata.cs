using System;
using UnityEngine;

namespace CityLife.Fire
{
    /// <summary>
    /// Source-only metadata descriptor for the dry brush / tinder visual asset.
    /// Provides physical specifications (mass, design target dimensions, pivot) and explicit boundary notices.
    /// PENDING RESOURCE-OWNER APPROVAL AND INTEGRATION.
    /// 
    /// CONTRACT BOUNDARIES & STATUS:
    /// - Authoring status: AUTHOR-COMPLETED SOURCE-ONLY.
    /// - Compilation and rendering are unverified in this authoring pass.
    /// - No canonical resource type ID has been approved for dry brush or tinder.
    /// - stone.collectable.v1 remains provisional and must not be registered or treated as implemented.
    /// - AG2 owns active shared core.
    /// - No parallel inventory, fake fuel transactions, warmth gameplay, or consumption authority.
    /// - This file and its associated geometry are SOURCE-ONLY visual deliverables.
    /// </summary>
    [Serializable]
    public sealed class TinderMetadata
    {
        public const string AssetName = "DryBrushTinderBundle";
        public const string AssetVersion = "citylife.fire.tinder.dry-brush.v1-source";
        public const string GeneratorClass = "CityLife.Fire.TinderGeometry";
        public const string AuthorStatus = "AUTHOR-COMPLETED SOURCE-ONLY";
        public const string VerificationStatus = "COMPILATION_AND_RENDERING_UNVERIFIED";

        /// <summary>
        /// Intended physical mass in kilograms for this bundle.
        /// Realistic dry fibrous brush / kindling mass (approx 200 grams).
        /// Explicitly pending resource-owner approval and integration.
        /// </summary>
        public const float IntendedMassKg = 0.20f;

        /// <summary>
        /// Intended target bounding dimensions in metres (Width X, Height Y, Depth Z).
        /// ~30cm diameter cluster, ~15cm height.
        /// NOTE: These dimensions represent design targets until runtime mesh bounds are measured and validated.
        /// </summary>
        public static readonly Vector3 TargetDimensionsMetres = new Vector3(0.30f, 0.15f, 0.28f);

        /// <summary>
        /// Backwards-compatible alias for TargetDimensionsMetres.
        /// </summary>
        public static readonly Vector3 IntendedDimensionsMetres = TargetDimensionsMetres;

        /// <summary>
        /// Local pivot position relative to the geometry.
        /// Aligned to the base centre at ground contact (Y = 0.0m).
        /// </summary>
        public static readonly Vector3 PivotLocalPosition = Vector3.zero;

        public const string PivotDescription = "Base centre (ground plane contact at Y = 0.00m)";

        /// <summary>
        /// Status flag: asset is strictly provisional and source-only.
        /// </summary>
        public const bool IsProvisional = true;

        /// <summary>
        /// Status flag: pending resource-owner approval.
        /// </summary>
        public const bool PendingResourceOwnerApproval = true;

        /// <summary>
        /// Explicit resource type registration status.
        /// </summary>
        public const string CanonicalResourceTypeStatus =
            "PROVISIONAL_UNAPPROVED - No canonical resource type ID has been approved; " +
            "stone.collectable.v1 remains provisional and must not be registered or treated as implemented. " +
            "No fire or tinder resource type is registered in core.";

        /// <summary>
        /// Explicit scope limitation statement.
        /// </summary>
        public const string ScopeLimitationNotice =
            "SOURCE-ONLY visual and procedural geometry asset. Standalone deliverable. " +
            "Contains NO gameplay items, NO inventory integration, NO warmth mechanics, " +
            "and NO fuel consumption authority. AG2 owns active shared core.";

        // Instance fields for serialization / inspector display if instantiated
        public string assetName = AssetName;
        public string assetVersion = AssetVersion;
        public float massKg = IntendedMassKg;
        public Vector3 dimensionsMetres = TargetDimensionsMetres;
        public Vector3 pivot = PivotLocalPosition;
        public bool isProvisional = IsProvisional;
        public bool pendingApproval = PendingResourceOwnerApproval;
        public string resourceStatus = CanonicalResourceTypeStatus;
        public string scopeNotice = ScopeLimitationNotice;
        public string authorStatus = AuthorStatus;
        public string verificationStatus = VerificationStatus;
    }
}
