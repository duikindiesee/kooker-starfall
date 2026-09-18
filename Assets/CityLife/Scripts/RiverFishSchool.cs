using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    /// <summary>
    /// Autonomous river fish ecology swimming in the clear freshwater canyon channel.
    /// Fish navigate shallow river runs and deep meander pools, reacting to currents
    /// and offering catchable high-protein sustenance for the inhabitant.
    /// </summary>
    public sealed class RiverFishSchool : MonoBehaviour
    {
        public const int DefaultFishCount = 8;
        public const float NutritionProtein = 2500f;
        public const float NutritionSatiety = 2000f;
        public const float DriveReductionEndorphins = 2200f;

        [Serializable]
        public class RiverFishInstance
        {
            public GameObject gameObject;
            public Vector3 swimCenter;
            public float swimRadius;
            public float swimSpeed;
            public float phase;
            public float swimDepth;
            public Transform bodyTransform;
            public NpcInteractable interactable;
            public PhysicalItem physicalItem;
        }

        public readonly List<RiverFishInstance> ActiveFish = new List<RiverFishInstance>();

        private void Update()
        {
            float t = Time.time;
            for (int i = 0; i < ActiveFish.Count; i++)
            {
                var fish = ActiveFish[i];
                if (fish == null || fish.gameObject == null || !fish.gameObject.activeSelf) continue;

                // Circular / elliptical orbit around local pool center
                float angle = t * fish.swimSpeed + fish.phase;
                float currentX = fish.swimCenter.x + Mathf.Cos(angle) * fish.swimRadius;
                float currentZ = fish.swimCenter.z + Mathf.Sin(angle) * (fish.swimRadius * 1.8f);

                // Riverbed depth compliance
                float groundY = CoastalTerrain.Height(currentX, currentZ);
                float waterY = CoastalWater.Level;
                float fishY = Mathf.Clamp(waterY - fish.swimDepth, groundY + 0.12f, waterY - 0.08f);

                Vector3 newPos = new Vector3(currentX, fishY, currentZ);
                Vector3 delta = newPos - fish.gameObject.transform.position;
                fish.gameObject.transform.position = newPos;

                if (delta.sqrMagnitude > 0.0001f)
                {
                    // Face swimming direction with gentle lateral spine wiggle
                    Quaternion lookRot = Quaternion.LookRotation(delta.normalized, Vector3.up);
                    float wiggle = Mathf.Sin(t * 8f + fish.phase) * 7.5f;
                    fish.gameObject.transform.rotation = lookRot * Quaternion.Euler(0, wiggle, 0);
                }
            }
        }

        public static Mesh CreateProceduralFishMesh()
        {
            var mesh = new Mesh { name = "Freshwater River Trout Mesh" };

            // 12 body cross-sections along fish length (0.35m long, 0.12m deep, 0.07m wide)
            const int rings = 10;
            const int segments = 8;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int r = 0; r <= rings; r++)
            {
                float fr = r / (float)rings;
                float z = (fr - 0.5f) * 0.35f;

                // Spindle profile: tapered head (fr=0) and tail (fr=1), wide belly (fr=0.35)
                float profile = Mathf.Sin(fr * Mathf.PI);
                float radiusY = profile * 0.06f + 0.008f;
                float radiusX = profile * 0.035f + 0.004f;

                for (int s = 0; s <= segments; s++)
                {
                    float angle = (s / (float)segments) * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * radiusX;
                    float y = Mathf.Sin(angle) * radiusY;

                    Vector3 pos = new Vector3(x, y, z);
                    vertices.Add(pos);
                    normals.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0).normalized);
                    uvs.Add(new Vector2(s / (float)segments, fr));
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int curr = r * (segments + 1) + s;
                    int next = curr + (segments + 1);

                    triangles.Add(curr);
                    triangles.Add(next);
                    triangles.Add(curr + 1);

                    triangles.Add(curr + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }

            // Caudal (tail) fin
            int tailBase = vertices.Count;
            float tailZ = 0.175f;
            vertices.Add(new Vector3(0, 0.055f, tailZ + 0.075f));  // Upper fin tip
            vertices.Add(new Vector3(0, -0.055f, tailZ + 0.075f)); // Lower fin tip
            vertices.Add(new Vector3(0, 0.015f, tailZ));           // Base top
            vertices.Add(new Vector3(0, -0.015f, tailZ));          // Base bottom
            for (int i = 0; i < 4; i++)
            {
                normals.Add(Vector3.right);
                uvs.Add(new Vector2(0.5f, 0.5f));
            }

            triangles.Add(tailBase); triangles.Add(tailBase + 2); triangles.Add(tailBase + 3);
            triangles.Add(tailBase); triangles.Add(tailBase + 3); triangles.Add(tailBase + 1);
            triangles.Add(tailBase + 3); triangles.Add(tailBase + 2); triangles.Add(tailBase);
            triangles.Add(tailBase + 1); triangles.Add(tailBase + 3); triangles.Add(tailBase);

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Material CreateFishMaterial()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");
            var mat = new Material(litShader)
            {
                name = "Freshwater River Trout Silvery Scales"
            };
            // Silvery greenish-slate back with pale iridescent belly
            mat.SetColor("_BaseColor", new Color(0.48f, 0.62f, 0.65f, 1.0f));
            mat.SetFloat("_Smoothness", 0.72f);
            mat.SetFloat("_Metallic", 0.35f);
            return mat;
        }

        public static List<GameObject> SpawnFishSchool(Transform parent, string worldId, out RiverFishSchool schoolComp)
        {
            var schoolGo = new GameObject("River Freshwater Fish School");
            schoolGo.transform.SetParent(parent, false);
            schoolComp = schoolGo.AddComponent<RiverFishSchool>();

            var fishMesh = CreateFishMeshAsset(fishMesh: null);
            var fishMat = CreateFishMaterial();

            // Authored pool centers along the freshwater river run
            var poolSpawns = new (Vector3 center, float radius, float speed, float depth)[]
            {
                (new Vector3(2f, -2.18f, -15f), 3.2f, 0.45f, 0.12f),     // Ford crossing shallow pool
                (new Vector3(-4f, -2.5f, -32f), 4.5f, 0.55f, 0.35f),     // South feed river eddy
                (new Vector3(6f, -2.8f, -55f), 5.2f, 0.50f, 0.45f),      // Upper south cascade run
                (new Vector3(-12f, -2.3f, 8f), 4.8f, 0.40f, 0.25f),      // Central meander bend
                (new Vector3(14f, -2.6f, 32f), 5.5f, 0.52f, 0.38f),      // North outlet gravel pool
                (new Vector3(0f, -2.2f, -12f), 2.8f, 0.48f, 0.10f),      // Ford gravel run
                (new Vector3(-8f, -2.7f, -85f), 6.0f, 0.60f, 0.50f),     // Deep south gorge pool
                (new Vector3(18f, -3.0f, -145f), 6.5f, 0.58f, 0.65f)     // Plunge pool approach
            };

            var spawnedObjects = new List<GameObject>();
            for (int i = 0; i < poolSpawns.Length; i++)
            {
                var sp = poolSpawns[i];
                string fishId = $"river-fish-{i + 1:D2}";

                var fishGo = new GameObject(fishId);
                fishGo.transform.SetParent(schoolGo.transform, false);
                fishGo.transform.position = sp.center;
                fishGo.layer = 11; // Interactable

                var mf = fishGo.AddComponent<MeshFilter>();
                mf.sharedMesh = fishMesh;
                var mr = fishGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = fishMat;

                var col = fishGo.AddComponent<SphereCollider>();
                col.radius = 0.25f;
                col.isTrigger = true;

                var approach = new GameObject(fishId + " approach");
                approach.transform.SetParent(fishGo.transform, false);
                approach.transform.localPosition = Vector3.zero;

                var ni = fishGo.AddComponent<NpcInteractable>();
                ni.StableId = fishId;
                ni.WorldId = worldId;
                ni.Kind = NpcObjectKind.Item;
                ni.Permission = true;
                ni.Approach = approach.transform;

                var phys = fishGo.AddComponent<PhysicalItem>();
                phys.itemId = fishId;
                phys.itemTypeId = "food-river-fish";
                phys.massKg = 0.65f;
                phys.dimensions = new PhysicalDimensions(0.35f, 0.12f, 0.08f);
                phys.ConfigureComponents();

                var instance = new RiverFishInstance
                {
                    gameObject = fishGo,
                    swimCenter = sp.center,
                    swimRadius = sp.radius,
                    swimSpeed = sp.speed,
                    swimDepth = sp.depth,
                    phase = i * (Mathf.PI * 2f / poolSpawns.Length),
                    bodyTransform = fishGo.transform,
                    interactable = ni,
                    physicalItem = phys
                };
                schoolComp.ActiveFish.Add(instance);
                spawnedObjects.Add(fishGo);
            }

            return spawnedObjects;
        }

        private static Mesh CreateFishMeshAsset(Mesh fishMesh)
        {
            if (fishMesh != null) return fishMesh;
            return CreateProceduralFishMesh();
        }

        public static bool VerifyFishEcology(out string receipt)
        {
            var mesh = CreateProceduralFishMesh();
            if (mesh == null || mesh.vertexCount < 50 || mesh.triangles.Length < 60)
            {
                receipt = "River fish mesh failed procedural vertex generation.";
                return false;
            }

            // Verify bounds dimensions match standard river trout
            var bounds = mesh.bounds;
            if (bounds.size.z < 0.25f || bounds.size.z > 0.50f)
            {
                receipt = $"Fish length {bounds.size.z:F2}m out of realistic bounds [0.25, 0.50].";
                return false;
            }

            // Verify all key river coordinates are freshwater river channel
            var testCoords = new[]
            {
                new Vector3(2f, -2.18f, -15f),
                new Vector3(-4f, -2.5f, -32f),
                new Vector3(6f, -2.8f, -55f)
            };
            foreach (var c in testCoords)
            {
                if (!CoastalTerrain.IsFreshwaterRiver(c.x, c.z, c.y, CoastalWater.Level))
                {
                    receipt = $"Fish spawn point ({c.x}, {c.z}) rejected as non-freshwater river.";
                    return false;
                }
            }

            receipt = $"Verified river fish ecology with {mesh.vertexCount} verts, {mesh.triangles.Length / 3} triangles, and valid freshwater river coordinates.";
            return true;
        }
    }
}
