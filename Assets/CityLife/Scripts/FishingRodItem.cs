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
            cachedRodMaterial.SetColor("_BaseColor", new Color(0.46f, 0.32f, 0.18f, 1f));
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
            cachedCorkMaterial.SetColor("_BaseColor", new Color(0.72f, 0.58f, 0.40f, 1f));
            cachedCorkMaterial.SetFloat("_Smoothness", 0.20f);
            return cachedCorkMaterial;
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
            // Handle: z in [0, 0.35m], radius = 0.016m (cork grip)
            // Blank: z in [0.35, 2.10m], tapers from 0.012m to 0.003m
            for (int r = 0; r <= segments; r++)
            {
                float fr = r / (float)segments;
                float z = fr * RodLength;

                float radius;
                if (z <= 0.35f)
                {
                    radius = 0.016f; // Cork handle grip
                }
                else
                {
                    float blankFr = (z - 0.35f) / (RodLength - 0.35f);
                    radius = Mathf.Lerp(0.012f, 0.003f, blankFr); // Tapered wood blank
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

            var tipGo = new GameObject("RodTip");
            tipGo.transform.SetParent(visual.transform, false);
            tipGo.transform.localPosition = new Vector3(0, 0, RodLength);

            return visual;
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
            phys.GripLocalRotation = Quaternion.Euler(15f, 0f, 0f);
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

        public void SetBobberVisible(bool visible)
        {
            if (BobberObject != null)
            {
                BobberObject.SetActive(visible);
            }
        }
    }
}
