using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    /// <summary>
    /// Physical handcrafted river fishing rod.
    /// Features a flexible tapered ash wood blank, cork handle grip, carved reel seat,
    /// wire line guides, and dynamic line/buoyant bobber attachment.
    /// Registered in authoritative PhysicalItemCatalog as "tool-fishing-rod".
    /// </summary>
    [SelectionBase]
    [DefaultExecutionOrder(101)]
    public sealed class FishingRodItem : MonoBehaviour
    {
        public const string ItemTypeId = "tool-fishing-rod";
        public const float RodLength = 2.10f;
        public const float RodMassKg = 0.85f;
        private static readonly Vector3 HandPalmGripPoint = new Vector3(0.018f, 0.065f, 0f);
        private static readonly Quaternion HandGripRotation = Quaternion.Euler(-60f, 25f, 0f);
        // New-world starter tool on reachable shore. Persisted item poses still
        // come from ItemPersistence; this does not relocate a saved player's rod.
        public static Vector3 InitialWorldPosition => new Vector3(40f, CoastalTerrain.Height(40f, -30f) + .15f, -30f);

        [Header("References")]
        public PhysicalItem PhysicalItem;
        public NpcInteractable Interactable;
        public Transform TipTransform;
        public Transform HandleTransform;
        public LineRenderer LineRenderer;
        public GameObject BobberObject;

        private static Mesh cachedRodMesh;
        private static Material cachedRodMaterial;
        private static Material cachedCorkMaterial;
        private static Material cachedReelMaterial;
        private static Material cachedGuideMaterial;
        private static Material cachedBoneMaterial;

        public static Mesh GetOrCreateRodMesh()
        {
            if (cachedRodMesh != null) return cachedRodMesh;
            cachedRodMesh = GenerateProceduralRodMesh();
            return cachedRodMesh;
        }

        public static Material GetOrCreateRodMaterial()
        {
            if (cachedRodMaterial != null) return cachedRodMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            cachedRodMaterial = new Material(shader)
            {
                name = "RiverFishingSpear_WoodShaft"
            };
            // Weathered fire-hardened hardwood spear shaft
            cachedRodMaterial.SetColor("_BaseColor", new Color(0.44f, 0.28f, 0.16f, 1f));
            cachedRodMaterial.SetFloat("_Smoothness", 0.15f);
            return cachedRodMaterial;
        }

        public static Material GetOrCreateBoneMaterial()
        {
            if (cachedBoneMaterial != null) return cachedBoneMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            cachedBoneMaterial = new Material(shader)
            {
                name = "PrimitiveSpear_CarvedBone"
            };
            // Carved bone / flint harpoon tip
            cachedBoneMaterial.SetColor("_BaseColor", new Color(0.86f, 0.84f, 0.76f, 1f));
            cachedBoneMaterial.SetFloat("_Smoothness", 0.25f);
            return cachedBoneMaterial;
        }

        public static Material GetOrCreateCorkMaterial()
        {
            if (cachedCorkMaterial != null) return cachedCorkMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            cachedCorkMaterial = new Material(shader)
            {
                name = "RiverFishingSpear_RawhideLashing"
            };
            // Rawhide and sinew cord binding
            cachedCorkMaterial.SetColor("_BaseColor", new Color(0.36f, 0.26f, 0.18f, 1f));
            cachedCorkMaterial.SetFloat("_Smoothness", 0.15f);
            return cachedCorkMaterial;
        }

        private static Material GetOrCreateReelMaterial()
        {
            if (cachedReelMaterial != null) return cachedReelMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            cachedReelMaterial = new Material(shader) { name = "RiverFishingSpear_TetherHank" };
            // Coiled hank of twisted plant fiber retrieval tether
            cachedReelMaterial.SetColor("_BaseColor", new Color(0.45f, 0.38f, 0.26f, 1f));
            cachedReelMaterial.SetFloat("_Smoothness", 0.10f);
            return cachedReelMaterial;
        }

        private static Material GetOrCreateGuideMaterial()
        {
            if (cachedGuideMaterial != null) return cachedGuideMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            cachedGuideMaterial = new Material(shader) { name = "RiverFishingSpear_SinewRings" };
            // Sinew tie-down loops along the shaft
            cachedGuideMaterial.SetColor("_BaseColor", new Color(0.42f, 0.34f, 0.22f, 1f));
            cachedGuideMaterial.SetFloat("_Smoothness", 0.12f);
            return cachedGuideMaterial;
        }

        public static Mesh GenerateProceduralRodMesh()
        {
            var mesh = new Mesh { name = "Procedural_Primitive_Fishing_Spear_Mesh" };

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            const int sides = 8;
            const int segments = 12;

            // Spear extends along local Z axis from 0 to RodLength (2.10m)
            // Grip section (z in [0, 0.35m]): radius 0.032m (giving width 0.064m >= 0.06f)
            // Shaft section: smoothly tapers to 0.020m, then socket at tip.
            for (int r = 0; r <= segments; r++)
            {
                float fr = r / (float)segments;
                float z = fr * RodLength;

                float radius;
                if (z <= 0.35f)
                {
                    radius = 0.032f; // Ergonomic grip section
                }
                else if (z <= 1.85f)
                {
                    float shaftFr = (z - 0.35f) / 1.50f;
                    radius = Mathf.Lerp(0.032f, 0.020f, shaftFr);
                }
                else
                {
                    float socketFr = (z - 1.85f) / (RodLength - 1.85f);
                    radius = Mathf.Lerp(0.020f, 0.016f, socketFr);
                }

                for (int s = 0; s <= sides; s++)
                {
                    float angle = (s / (float)sides) * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * radius;
                    float y = Mathf.Sin(angle) * radius;

                    vertices.Add(new Vector3(x, y, z));
                    normals.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0).normalized);
                    uvs.Add(new Vector2(s / (float)sides, fr));
                }
            }

            for (int r = 0; r < segments; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int curr = r * (sides + 1) + s;
                    int next = curr + (sides + 1);

                    triangles.Add(curr);
                    triangles.Add(next);
                    triangles.Add(curr + 1);

                    triangles.Add(curr + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }

            // Butt cap (z = 0)
            int buttCenter = vertices.Count;
            vertices.Add(Vector3.zero);
            normals.Add(-Vector3.forward);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s < sides; s++)
            {
                triangles.Add(buttCenter);
                triangles.Add(s);
                triangles.Add(s + 1);
            }

            // Tip cap (z = RodLength)
            int tipCenter = vertices.Count;
            vertices.Add(new Vector3(0, 0, RodLength));
            normals.Add(Vector3.forward);
            uvs.Add(new Vector2(0.5f, 0.5f));
            int lastRing = segments * (sides + 1);
            for (int s = 0; s < sides; s++)
            {
                triangles.Add(tipCenter);
                triangles.Add(lastRing + s + 1);
                triangles.Add(lastRing + s);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static GameObject CreateVisual(Transform parent, Material rodMat = null, Material corkMat = null)
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            var mf = visual.AddComponent<MeshFilter>();
            mf.sharedMesh = GetOrCreateRodMesh();

            var mr = visual.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rodMat != null ? rodMat : GetOrCreateRodMaterial();

            // 1. Rawhide Grip Binding (replaces modern cork grip with rugged rawhide cord)
            var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            grip.name = "VisibleCorkGrip"; // Retained for catalog/integration checks
            grip.transform.SetParent(visual.transform, false);
            grip.transform.localPosition = new Vector3(0, 0, 0.18f);
            grip.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            grip.transform.localScale = new Vector3(0.046f, 0.175f, 0.046f);
            grip.GetComponent<Renderer>().sharedMaterial = corkMat != null ? corkMat : GetOrCreateCorkMaterial();
            var gripCollider = grip.GetComponent<Collider>();
            if (gripCollider != null) SafeDestroy(gripCollider);

            var rawhideAlias = new GameObject("RawhideGrip");
            rawhideAlias.transform.SetParent(grip.transform, false);

            // 2. Carved Bone Spear Point at tip (z = 2.10m extending forward)
            var spearPoint = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spearPoint.name = "SpearPoint";
            spearPoint.transform.SetParent(visual.transform, false);
            spearPoint.transform.localPosition = new Vector3(0, 0, RodLength + 0.08f);
            spearPoint.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            spearPoint.transform.localScale = new Vector3(0.024f, 0.09f, 0.024f);
            spearPoint.GetComponent<Renderer>().sharedMaterial = GetOrCreateBoneMaterial();
            var pointCol = spearPoint.GetComponent<Collider>();
            if (pointCol != null) SafeDestroy(pointCol);

            // 3. Carved Bone Harpoon Barb (angled backward)
            var barb = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barb.name = "HarpoonBarb";
            barb.transform.SetParent(visual.transform, false);
            barb.transform.localPosition = new Vector3(0, 0.026f, RodLength + 0.02f);
            barb.transform.localRotation = Quaternion.Euler(145f, 0, 0);
            barb.transform.localScale = new Vector3(0.014f, 0.055f, 0.014f);
            barb.GetComponent<Renderer>().sharedMaterial = GetOrCreateBoneMaterial();
            var barbCol = barb.GetComponent<Collider>();
            if (barbCol != null) SafeDestroy(barbCol);

            // 4. Sinew / Rawhide Spear Lashing securing head to wooden shaft
            var lashing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lashing.name = "SpearLashing";
            lashing.transform.SetParent(visual.transform, false);
            lashing.transform.localPosition = new Vector3(0, 0, RodLength - 0.06f);
            lashing.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            lashing.transform.localScale = new Vector3(0.038f, 0.065f, 0.038f);
            lashing.GetComponent<Renderer>().sharedMaterial = GetOrCreateCorkMaterial();
            var lashingCol = lashing.GetComponent<Collider>();
            if (lashingCol != null) SafeDestroy(lashingCol);

            // 5. Coiled Retrieval Tether Hank (replaces lathe metal reel spool with a primitive hank of sinew line)
            var reel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            reel.name = "VisibleReelSpool";
            reel.transform.SetParent(visual.transform, false);
            reel.transform.localPosition = new Vector3(0, -0.045f, 0.42f);
            reel.transform.localScale = new Vector3(0.08f, 0.035f, 0.08f);
            reel.GetComponent<Renderer>().sharedMaterial = GetOrCreateReelMaterial();
            var reelCollider = reel.GetComponent<Collider>();
            if (reelCollider != null) SafeDestroy(reelCollider);

            AddRodAccent(visual.transform, "ReelHub", new Vector3(0, -0.055f, 0.42f),
                new Vector3(0.028f, 0.028f, 0.028f), GetOrCreateCorkMaterial());
            AddRodAccent(visual.transform, "ReelCrank", new Vector3(0.055f, -0.045f, 0.42f),
                new Vector3(0.016f, 0.048f, 0.016f), GetOrCreateRodMaterial());
            AddRodAccent(visual.transform, "ReelKnob", new Vector3(0.055f, -0.075f, 0.42f),
                new Vector3(0.022f, 0.022f, 0.022f), GetOrCreateCorkMaterial());

            // 6. Sinew Tie-Down Rings along spear shaft (formerly wire line guides)
            for (int guide = 0; guide < 4; guide++)
            {
                float z = 0.62f + guide * 0.39f;
                AddRodAccent(visual.transform, "LineGuide" + (guide + 1), new Vector3(0, 0.016f, z),
                    new Vector3(0.020f, 0.020f, 0.020f), GetOrCreateGuideMaterial());
            }

            var tipGo = new GameObject("RodTip");
            tipGo.transform.SetParent(visual.transform, false);
            tipGo.transform.localPosition = new Vector3(0, 0, RodLength);

            return visual;
        }

        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        private static GameObject AddRodAccent(Transform parent, string objectName, Vector3 localPosition,
            Vector3 localScale, Material material)
        {
            var accent = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            accent.name = objectName;
            accent.transform.SetParent(parent, false);
            accent.transform.localPosition = localPosition;
            accent.transform.localScale = localScale;
            accent.GetComponent<Renderer>().sharedMaterial = material;
            var collider = accent.GetComponent<Collider>();
            if (collider != null) SafeDestroy(collider);
            return accent;
        }

        public static GameObject SpawnWorldFishingRod(Transform parent, string worldId, Vector3 position, string stableId = "fishing-rod-01")
        {
            var rodGo = new GameObject(stableId);
            if (parent != null) rodGo.transform.SetParent(parent, false);
            rodGo.transform.position = position;
            // Lay naturally angled along rock / ground
            rodGo.transform.rotation = Quaternion.Euler(6f, 45f, 4f);
            rodGo.layer = 11; // Interactable

            var visual = CreateVisual(rodGo.transform);

            var col = rodGo.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0, RodLength * 0.5f);
            col.size = new Vector3(0.08f, 0.08f, RodLength);
            col.isTrigger = false;

            var approach = new GameObject(stableId + " approach");
            approach.transform.SetParent(rodGo.transform, false);
            approach.transform.localPosition = new Vector3(0.28f, 0, 0.4f);

            var sightCenter = new GameObject(stableId + " sight-center");
            sightCenter.transform.SetParent(rodGo.transform, false);
            sightCenter.transform.localPosition = new Vector3(0, 0, RodLength * 0.5f);

            var ni = rodGo.AddComponent<NpcInteractable>();
            ni.StableId = stableId;
            ni.WorldId = worldId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;
            ni.CustomSightTarget = sightCenter.transform;

            var phys = rodGo.AddComponent<PhysicalItem>();
            phys.itemId = stableId;
            phys.itemTypeId = ItemTypeId;
            phys.massKg = RodMassKg;
            phys.dimensions = new PhysicalDimensions(0.08f, 0.08f, RodLength);
            phys.ConfigureComponents();

            // Maintain center along rod length after ConfigureComponents initializes dimensions
            if (phys.ItemCollider is BoxCollider box)
            {
                box.center = new Vector3(0, 0, RodLength * 0.5f);
            }

            // Ensure unit scale at root level to satisfy PhysicalItem lossyScale validation
            rodGo.transform.SetParent(null, true);
            rodGo.transform.localScale = Vector3.one;

            var rodComp = rodGo.AddComponent<FishingRodItem>();
            rodComp.PhysicalItem = phys;
            rodComp.Interactable = ni;

            var tipGo = visual.transform.Find("RodTip");
            rodComp.TipTransform = tipGo != null ? tipGo : visual.transform;

            var handleGo = new GameObject("RodHandle");
            handleGo.transform.SetParent(rodGo.transform, false);
            handleGo.transform.localPosition = new Vector3(0, 0, 0.18f);
            rodComp.HandleTransform = handleGo.transform;
            ConfigureGripPose(phys, handleGo.transform.localPosition);

            return rodGo;
        }

        private void Update()
        {
            if (PhysicalItem != null && !PhysicalItem.IsCarried && Interactable != null && Interactable.Approach != null)
            {
                Transform actor = null;
                var auto = FindFirstObjectByType<NpcAutonomy>();
                if (auto != null) actor = auto.transform;
                if (actor != null)
                {
                    Vector3 localActor = transform.InverseTransformPoint(actor.position);
                    float targetZ = Mathf.Clamp(localActor.z, 0.35f, 1.85f);
                    Interactable.Approach.localPosition = new Vector3(0.28f, 0f, targetZ);
                }
            }
        }

        public void AttachLineRenderer(LineRenderer lr)
        {
            LineRenderer = lr;
        }

        private void LateUpdate() => ApplyStableCarryPose();

        public void ApplyStableCarryPose()
        {
            if (PhysicalItem == null || !PhysicalItem.IsCarried || PhysicalItem.CarriedHand == null)
                return;

            // Runtime-loaded rods may receive this component only when a fishing
            // action first inspects the held item. Rebuild the authored grip pivots
            // before trying to pose the rod so it cannot remain loose at the wrist.
            if (HandleTransform == null)
                ConfigureRuntimeRod(PhysicalItem, Interactable);
            if (HandleTransform == null)
                return;

            Transform hand = PhysicalItem.CarriedHand;
            Animator animator = hand.GetComponentInParent<Animator>();
            Transform facing = animator != null ? animator.transform : hand.root;
            Vector3 forward = Vector3.ProjectOnPlane(facing.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

            // The generic walk cycle swings the wrist; keep a carried fishing rod
            // in a readable, upward-ready posture while retaining the animated arm.
            // Keep the pole nearly vertical and choose a perpendicular up vector.
            // Passing world-up for a nearly vertical LookRotation makes roll
            // underdetermined and was the source of the diagonal waist-level pose.
            Vector3 rodAxis = (forward * 0.12f + Vector3.up * 0.993f).normalized;
            Vector3 rodUp = Vector3.ProjectOnPlane(forward, rodAxis).normalized;
            if (rodUp.sqrMagnitude < 0.001f)
                rodUp = Vector3.ProjectOnPlane(facing.right, rodAxis).normalized;
            Quaternion rodRotation = Quaternion.LookRotation(rodAxis, rodUp);
            hand.rotation = rodRotation * Quaternion.Inverse(HandGripRotation);

            Vector3 offset = HandPalmGripPoint - HandGripRotation * HandleTransform.localPosition;
            if (PhysicalItem.IsLeftHand) offset.x = -offset.x;
            PhysicalItem.GripLocalOffset = offset;
            PhysicalItem.UpdateGripPose();
        }

        public static void ConfigureRuntimeRod(PhysicalItem physical, NpcInteractable interactable)
        {
            if (physical == null) return;
            var root = physical.gameObject;
            var rod = root.GetComponent<FishingRodItem>() ?? root.AddComponent<FishingRodItem>();
            rod.PhysicalItem = physical;
            rod.Interactable = interactable;
            rod.TipTransform = root.transform.Find("Visual/RodTip");
            var handle = root.transform.Find("RodHandle");
            if (handle == null)
            {
                handle = new GameObject("RodHandle").transform;
                handle.SetParent(root.transform, false);
                handle.localPosition = new Vector3(0, 0, .18f);
            }
            rod.HandleTransform = handle;
            ConfigureGripPose(physical, handle.localPosition);
            var collider = root.GetComponent<BoxCollider>();
            if (collider != null) collider.center = new Vector3(0, 0, RodLength * .5f);

            if (interactable != null)
            {
                var sight = root.transform.Find(root.name + " sight-center");
                if (sight == null)
                {
                    sight = new GameObject(root.name + " sight-center").transform;
                    sight.SetParent(root.transform, false);
                    sight.localPosition = new Vector3(0, 0, RodLength * 0.5f);
                }
                interactable.CustomSightTarget = sight;
            }
        }

        private static void ConfigureGripPose(PhysicalItem physical, Vector3 handleLocalPosition)
        {
            // PhysicalItem positions the rod origin relative to the wrist, then applies
            // this rotation. Solve the origin offset from the actual cork grip point so
            // the palm stays on the cork instead of inheriting a guessed -Z offset.
            physical.GripLocalRotation = HandGripRotation;
            physical.GripLocalOffset = HandPalmGripPoint - HandGripRotation * handleLocalPosition;
        }

        public void SetBobberVisible(bool visible)
        {
            if (BobberObject != null)
            {
                BobberObject.SetActive(visible);
            }
        }
    }
}
