using System;
using UnityEngine;

namespace CityLife.Wood
{
    /// <summary>
    /// Visual asset presentation component for a procedurally generated fallen branch or log.
    /// Operates strictly in local metre units with unit transform scale (1, 1, 1).
    /// Bounding dimensions and dry mass are held as static asset metadata only.
    /// No runtime physics, collision, inventory, fuel, or gameplay authority is claimed.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public sealed class FallenWoodInstance : MonoBehaviour
    {
        [Header("Generation Parameters")]
        [Tooltip("Morphology and dimensional configuration for this fallen-wood asset.")]
        public FallenWoodProfile profile;

        [Tooltip("Deterministic explicit seed driving all geometry, curvature, and colour variation.")]
        public uint seed = 42u;

        [Tooltip("If true, regenerates the procedural mesh in Awake/OnEnable.")]
        public bool autoGenerateOnEnable = true;

        [Header("Read-Only Stated Asset Metadata")]
        [Tooltip("Stated bounding dimensions and dry mass metadata (SOURCE ONLY, physics validation pending).")]
        [SerializeField]
        private FallenWoodAssetMetadata statedMetadata;

        public FallenWoodAssetMetadata Metadata => statedMetadata;

        [NonSerialized]
        private Mesh ownedMesh;

        public Mesh OwnedMesh => ownedMesh;
        public bool IsMeshOwned(Mesh m) => m != null && m == ownedMesh;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        private void Reset()
        {
            if (profile == null)
            {
                profile = FallenWoodProfile.CreateBranchPreset();
            }
        }

        private void OnEnable()
        {
            EnsureComponents();
            if (autoGenerateOnEnable && (meshFilter.sharedMesh == null || statedMetadata == null))
            {
                Generate();
            }
        }

        private void OnDestroy()
        {
            ReleaseOwnedMesh();
        }

        private void EnsureComponents()
        {
            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// Releases and destroys the locally owned procedural mesh using play/editor appropriate destruction.
        /// Never destroys external or imported shared meshes.
        /// </summary>
        public void ReleaseOwnedMesh()
        {
            if (ownedMesh != null)
            {
                if (meshFilter != null && meshFilter.sharedMesh == ownedMesh)
                {
                    meshFilter.sharedMesh = null;
                }
                DestroyMeshSafe(ownedMesh);
                ownedMesh = null;
            }
        }

        private static void DestroyMeshSafe(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }

        /// <summary>
        /// Validates that the transform and all parent ancestors have uniform unit scale (1, 1, 1)
        /// and no shear or mirroring/reflection.
        /// Does not mutate or reparent ancestor transforms.
        /// </summary>
        public static bool IsSupportedHierarchy(Transform targetTransform, out string failureReason)
        {
            if (targetTransform == null)
            {
                failureReason = "Target transform is null.";
                return false;
            }

            Transform parent = targetTransform.parent;
            if (parent == null)
            {
                failureReason = null;
                return true;
            }

            // 1. Inspect ancestor local scales
            Transform curr = parent;
            while (curr != null)
            {
                Vector3 ls = curr.localScale;
                if (float.IsNaN(ls.x) || float.IsNaN(ls.y) || float.IsNaN(ls.z) ||
                    float.IsInfinity(ls.x) || float.IsInfinity(ls.y) || float.IsInfinity(ls.z))
                {
                    failureReason = $"Ancestor '{curr.name}' has non-finite localScale: {ls}.";
                    return false;
                }

                if (ls.x <= 0f || ls.y <= 0f || ls.z <= 0f)
                {
                    failureReason = $"Ancestor '{curr.name}' has non-positive/mirrored localScale: {ls}.";
                    return false;
                }

                const float eps = 1e-4f;
                if (Mathf.Abs(ls.x - 1f) > eps || Mathf.Abs(ls.y - 1f) > eps || Mathf.Abs(ls.z - 1f) > eps)
                {
                    failureReason = $"Ancestor '{curr.name}' has non-unit localScale: {ls}.";
                    return false;
                }

                curr = curr.parent;
            }

            // 2. Inspect parent localToWorldMatrix for rigid orthonormal basis (no shear or scaling)
            Matrix4x4 m = parent.localToWorldMatrix;
            Vector3 c0 = m.GetColumn(0);
            Vector3 c1 = m.GetColumn(1);
            Vector3 c2 = m.GetColumn(2);

            const float lenEps = 1e-3f;
            if (Mathf.Abs(c0.magnitude - 1f) > lenEps ||
                Mathf.Abs(c1.magnitude - 1f) > lenEps ||
                Mathf.Abs(c2.magnitude - 1f) > lenEps)
            {
                failureReason = $"Parent hierarchy scale magnitude is non-unit: ({c0.magnitude:F4}, {c1.magnitude:F4}, {c2.magnitude:F4}).";
                return false;
            }

            const float dotEps = 1e-3f;
            if (Mathf.Abs(Vector3.Dot(c0, c1)) > dotEps ||
                Mathf.Abs(Vector3.Dot(c1, c2)) > dotEps ||
                Mathf.Abs(Vector3.Dot(c0, c2)) > dotEps)
            {
                failureReason = "Parent hierarchy contains shear/non-orthogonal axes.";
                return false;
            }

            float det = Vector3.Dot(c0, Vector3.Cross(c1, c2));
            if (Mathf.Abs(det - 1f) > 1e-3f)
            {
                failureReason = $"Parent hierarchy transform determinant is {det:F4} (expected +1.0; reflection or scale detected).";
                return false;
            }

            failureReason = null;
            return true;
        }

        /// <summary>
        /// Generates the procedural fallen-wood mesh and assigns it to the local MeshFilter.
        /// Fails closed if hierarchy scale/shear is unsupported or profile is invalid, preserving existing mesh.
        /// Replaces mesh atomically and cleans up prior owned mesh.
        /// </summary>
        [ContextMenu("Regenerate Mesh")]
        public Mesh Generate()
        {
            EnsureComponents();

            // 1. Fail closed on unsupported hierarchy before mutating transform or mesh
            if (!IsSupportedHierarchy(transform, out string hierarchyError))
            {
                Debug.LogWarning($"[FallenWoodInstance] Generation aborted on '{name}': {hierarchyError}. Preserving existing mesh.", this);
                return null;
            }

            // 2. Validate profile before generation
            FallenWoodProfile activeProfile = profile ?? FallenWoodProfile.CreateBranchPreset();
            if (!activeProfile.IsValid())
            {
                Debug.LogWarning($"[FallenWoodInstance] Generation aborted on '{name}': Profile is invalid. Preserving existing mesh.", this);
                return null;
            }

            // 3. Generate mesh into a local temporary variable before mutating state
            Mesh newMesh;
            FallenWoodAssetMetadata newMetadata;
            try
            {
                newMesh = FallenWoodGenerator.GenerateMesh(activeProfile, seed, out newMetadata);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FallenWoodInstance] Mesh generation failed on '{name}': {ex.Message}. Preserving existing mesh.", this);
                return null;
            }

            // 4. Enforce unit localScale now that generation succeeded and hierarchy is verified
            transform.localScale = Vector3.one;

            // 5. Atomic replacement: track new owned mesh, assign sharedMesh, update metadata
            Mesh priorOwned = ownedMesh;
            ownedMesh = newMesh;
            meshFilter.sharedMesh = newMesh;
            statedMetadata = newMetadata;

            // 6. Release prior owned mesh only (never destroy external/imported meshes)
            if (priorOwned != null && priorOwned != newMesh)
            {
                DestroyMeshSafe(priorOwned);
            }

            return newMesh;
        }
    }
}
