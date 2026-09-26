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
    public sealed class FishingRodItem : MonoBehaviour
    {
        public const string ItemTypeId = "tool-fishing-rod";
        public const float RodLength = 2.10f;
        public const float RodMassKg = 0.85f;
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

        public static Mesh GetOrCreateRodMesh()
        {
            if (cachedRodMesh != null) return cachedRodMesh;
            cachedRodMesh = GenerateProceduralRodMesh();
            return cachedRodMesh;
        }

        public static Material GetOrCreateRodMaterial()
        {
            if (cachedRodMaterial != null) return cachedRodMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            cachedRodMaterial = new Material(shader)
            {
                name = "RiverFishingRod_WoodBlank"
            };
            // Warm ash wood tone
            cachedRodMaterial.SetColor("_BaseColor", new Color(0.50f, 0.20f, 0.07f, 1f));
            cachedRodMaterial.SetFloat("_Smoothness", 0.45f);
            return cachedRodMaterial;
        }

        public static Material GetOrCreateCorkMaterial()
        {
            if (cachedCorkMaterial != null) return cachedCorkMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            cachedCorkMaterial = new Material(shader)
            {
                name = "RiverFishingRod_CorkGrip"
            };
            // Pale natural cork tone
            cachedCorkMaterial.SetColor("_BaseColor", new Color(0.91f, 0.68f, 0.39f, 1f));
            cachedCorkMaterial.SetFloat("_Smoothness", 0.20f);
            return cachedCorkMaterial;
        }

        private static Material GetOrCreateReelMaterial()
        {
            if (cachedReelMaterial != null) return cachedReelMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            cachedReelMaterial = new Material(shader) { name = "RiverFishingRod_TealReel" };
            cachedReelMaterial.SetColor("_BaseColor", new Color(0.035f, 0.28f, 0.34f, 1f));
            cachedReelMaterial.SetFloat("_Smoothness", 0.58f);
            return cachedReelMaterial;
        }

        private static Material GetOrCreateGuideMaterial()
        {
            if (cachedGuideMaterial != null) return cachedGuideMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            cachedGuideMaterial = new Material(shader) { name = "RiverFishingRod_LineGuides" };
            cachedGuideMaterial.SetColor("_BaseColor", new Color(0.96f, 0.82f, 0.47f, 1f));
            cachedGuideMaterial.SetFloat("_Smoothness", 0.72f);
            return cachedGuideMaterial;
        }

        public static Mesh GenerateProceduralRodMesh()
        {
            var mesh = new Mesh { name = "Procedural_Fishing_Rod_Mesh" };

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            const int sides = 8;
            const int segments = 12;

            // Rod extends along local Z axis from 0 to RodLength (2.1m)
            // Handle: z in [0, 0.35m]. The blank is deliberately stout enough
            // to read as a crafted fishing pole at the pulled-back game camera.
            for (int r = 0; r <= segments; r++)
            {
                float fr = r / (float)segments;
                float z = fr * RodLength;

                float radius;
                if (z <= 0.35f)
                {
                    radius = 0.036f; // Stout handle blank; separate cork overlay below
                }
                else
                {
                    float blankFr = (z - 0.35f) / (RodLength - 0.35f);
                    radius = Mathf.Lerp(0.036f, 0.012f, blankFr); // Tapered wood blank
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

            // Reel spool geometry: small drum mounted at z = 0.18m, offset in -Y
            int reelStart = vertices.Count;
            float reelRadius = 0.032f;
            float reelWidth = 0.024f;
            float reelZ = 0.18f;
            float reelY = -0.035f;

            for (int rs = 0; rs <= sides; rs++)
            {
                float angle = (rs / (float)sides) * Mathf.PI * 2f;
                float rx = Mathf.Cos(angle) * reelRadius;
                float ry = Mathf.Sin(angle) * reelRadius + reelY;

                vertices.Add(new Vector3(rx, ry, reelZ - reelWidth * 0.5f));
                normals.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0).normalized);
                uvs.Add(new Vector2(rs / (float)sides, 0));

                vertices.Add(new Vector3(rx, ry, reelZ + reelWidth * 0.5f));
                normals.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0).normalized);
                uvs.Add(new Vector2(rs / (float)sides, 1));
            }

            for (int rs = 0; rs < sides; rs++)
            {
                int curr = reelStart + rs * 2;
                int next = curr + 2;

                triangles.Add(curr);
                triangles.Add(next);
                triangles.Add(curr + 1);

                triangles.Add(curr + 1);
                triangles.Add(next);
                triangles.Add(next + 1);
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

            // The rod used to be one slim, dark mesh. At the normal game camera
            // distance it read as a line beside the inhabitant, not as fishing
            // equipment. Give the grip, reel and guides distinct handcrafted
            // materials and a clear silhouette without changing line physics.
            var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            grip.name = "VisibleCorkGrip";
            grip.transform.SetParent(visual.transform, false);
            grip.transform.localPosition = new Vector3(0, 0, 0.18f);
            grip.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            grip.transform.localScale = new Vector3(0.044f, 0.175f, 0.044f);
            grip.GetComponent<Renderer>().sharedMaterial = corkMat != null ? corkMat : GetOrCreateCorkMaterial();
            var gripCollider = grip.GetComponent<Collider>();
            if (gripCollider != null) Destroy(gripCollider);

            var reel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            reel.name = "VisibleReelSpool";
            reel.transform.SetParent(visual.transform, false);
            // Keep the spool beyond the hand so its side plate is visible in the
            // player camera; the old along-shaft cylinder read as no reel at all.
            reel.transform.localPosition = new Vector3(0, -0.075f, 0.43f);
            reel.transform.localScale = new Vector3(0.18f, 0.045f, 0.18f);
            reel.GetComponent<Renderer>().sharedMaterial = GetOrCreateReelMaterial();
            var reelCollider = reel.GetComponent<Collider>();
            if (reelCollider != null) Destroy(reelCollider);

            AddRodAccent(visual.transform, "ReelHub", new Vector3(0, -0.102f, 0.43f),
                new Vector3(0.052f, 0.052f, 0.052f), GetOrCreateGuideMaterial());
            AddRodAccent(visual.transform, "ReelCrank", new Vector3(0.11f, -0.075f, 0.43f),
                new Vector3(0.022f, 0.082f, 0.022f), GetOrCreateGuideMaterial());
            AddRodAccent(visual.transform, "ReelKnob", new Vector3(0.11f, -0.14f, 0.43f),
                new Vector3(0.036f, 0.036f, 0.036f), GetOrCreateReelMaterial());
            for (int guide = 0; guide < 4; guide++)
            {
                float z = 0.62f + guide * 0.39f;
                AddRodAccent(visual.transform, "LineGuide" + (guide + 1), new Vector3(0, 0.020f, z),
                    new Vector3(0.022f, 0.022f, 0.022f), GetOrCreateGuideMaterial());
            }

            var tipGo = new GameObject("RodTip");
            tipGo.transform.SetParent(visual.transform, false);
            tipGo.transform.localPosition = new Vector3(0, 0, RodLength);

            return visual;
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
            if (collider != null) Destroy(collider);
            return accent;
        }

        public static GameObject SpawnWorldFishingRod(Transform parent, string worldId, Vector3 position, string stableId = "fishing-rod-01")
        {
            var rodGo = new GameObject(stableId);
            rodGo.transform.SetParent(parent, false);
            rodGo.transform.position = position;
            // Lay naturally angled along rock / ground
            rodGo.transform.rotation = Quaternion.Euler(6f, 45f, 4f);
            rodGo.layer = 11; // Interactable

            var visual = CreateVisual(rodGo.transform);

            var col = rodGo.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0, RodLength * 0.5f);
            col.size = new Vector3(0.12f, 0.12f, RodLength);
            col.isTrigger = true;

            var approach = new GameObject(stableId + " approach");
            approach.transform.SetParent(rodGo.transform, false);
            approach.transform.localPosition = new Vector3(0.3f, 0, 0.4f);

            var ni = rodGo.AddComponent<NpcInteractable>();
            ni.StableId = stableId;
            ni.WorldId = worldId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;

            var phys = rodGo.AddComponent<PhysicalItem>();
            phys.itemId = stableId;
            phys.itemTypeId = ItemTypeId;
            phys.massKg = RodMassKg;
            phys.dimensions = new PhysicalDimensions(0.08f, 0.08f, RodLength);
            phys.GripLocalOffset = new Vector3(0.018f, 0.065f, -0.18f);
            phys.GripLocalRotation = Quaternion.Euler(-28f, 25f, 0f);
            phys.ConfigureComponents();

            if (phys.Body != null)
            {
                phys.Body.isKinematic = true;
                phys.Body.useGravity = false;
            }

            var rodComp = rodGo.AddComponent<FishingRodItem>();
            rodComp.PhysicalItem = phys;
            rodComp.Interactable = ni;

            var tipGo = visual.transform.Find("RodTip");
            rodComp.TipTransform = tipGo != null ? tipGo : visual.transform;

            var handleGo = new GameObject("RodHandle");
            handleGo.transform.SetParent(rodGo.transform, false);
            handleGo.transform.localPosition = new Vector3(0, 0, 0.18f);
            rodComp.HandleTransform = handleGo.transform;

            return rodGo;
        }

        public void AttachLineRenderer(LineRenderer lr)
        {
            LineRenderer = lr;
        }

        public static void ConfigureRuntimeRod(PhysicalItem physical, NpcInteractable interactable)
        {
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
            physical.GripLocalOffset = new Vector3(.018f, .065f, -.18f);
            physical.GripLocalRotation = Quaternion.Euler(-28f, 25f, 0f);
            var collider = root.GetComponent<BoxCollider>();
            if (collider != null) collider.center = new Vector3(0, 0, RodLength * .5f);
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
