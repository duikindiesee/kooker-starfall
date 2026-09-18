using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    /// <summary>
    /// Distributes protein crabs across the coastal shallows, tidal mudflats, and river delta.
    /// Crabs provide rich marine protein that directly replenishes FoodBody.protein and triggers
    /// Creatures-style hedonic drive reduction (endorphin surge).
    /// </summary>
    public sealed class CoastalCrabDistribution : MonoBehaviour
    {
        public const int DefaultCrabCount = 14;

        [Serializable]
        public struct CrabSpawnDef
        {
            public Vector3 position;
            public float scale;
            public float yaw;
        }

        public static readonly CrabSpawnDef[] AuthoredLocations = new CrabSpawnDef[]
        {
            new CrabSpawnDef { position = new Vector3(-12f, -2.4f, 62f), scale = 1.0f, yaw = 35f },
            new CrabSpawnDef { position = new Vector3(8f, -2.5f, 75f), scale = 1.1f, yaw = 120f },
            new CrabSpawnDef { position = new Vector3(-22f, -2.3f, 88f), scale = 0.9f, yaw = 210f },
            new CrabSpawnDef { position = new Vector3(15f, -2.6f, 95f), scale = 1.2f, yaw = 300f },
            new CrabSpawnDef { position = new Vector3(-5f, -2.5f, 110f), scale = 1.05f, yaw = 45f },
            new CrabSpawnDef { position = new Vector3(25f, -2.4f, 122f), scale = 0.95f, yaw = 160f },
            new CrabSpawnDef { position = new Vector3(-18f, -2.6f, 135f), scale = 1.15f, yaw = 80f },
            new CrabSpawnDef { position = new Vector3(2f, -2.7f, 145f), scale = 1.0f, yaw = 240f },
            new CrabSpawnDef { position = new Vector3(-30f, -2.2f, 70f), scale = 1.0f, yaw = 15f },
            new CrabSpawnDef { position = new Vector3(32f, -2.3f, 85f), scale = 0.9f, yaw = 195f },
            new CrabSpawnDef { position = new Vector3(0f, -2.6f, 100f), scale = 1.1f, yaw = 135f },
            new CrabSpawnDef { position = new Vector3(-10f, -2.5f, 125f), scale = 1.0f, yaw = 285f },
            new CrabSpawnDef { position = new Vector3(18f, -2.5f, 140f), scale = 1.05f, yaw = 55f },
            new CrabSpawnDef { position = new Vector3(-25f, -2.4f, 155f), scale = 1.2f, yaw = 175f }
        };

        private static Mesh sharedCrabMesh;
        private static Material sharedCrabMaterial;

        public static Mesh GetOrCreateCrabMesh()
        {
            if (sharedCrabMesh != null) return sharedCrabMesh;

            // Generate procedural low-poly crustacean mesh
            // Carapace: flattened hexagon dome
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();

            Color32 carapaceColor = new Color32(185, 68, 42, 255); // Rich ochre-red marine chitin
            Color32 bellyColor = new Color32(215, 160, 110, 255);  // Paler underbelly
            Color32 clawColor = new Color32(205, 52, 30, 255);     // Crimson claw tips
            Color32 legColor = new Color32(160, 75, 45, 255);      // Walking leg chitin

            // Top apex of carapace
            int topIdx = vertices.Count;
            vertices.Add(new Vector3(0f, 0.07f, 0f));
            normals.Add(Vector3.up);
            colors.Add(carapaceColor);

            // Carapace perimeter ring (8 vertices)
            int ringStart = vertices.Count;
            float rx = 0.11f, rz = 0.08f;
            for (int i = 0; i < 8; i++)
            {
                float angle = (i / 8f) * Mathf.PI * 2f;
                float vx = Mathf.Cos(angle) * rx;
                float vz = Mathf.Sin(angle) * rz;
                vertices.Add(new Vector3(vx, 0.02f, vz));
                normals.Add(new Vector3(vx, 0.3f, vz).normalized);
                colors.Add(carapaceColor);
            }

            // Bottom center vertex
            int botIdx = vertices.Count;
            vertices.Add(new Vector3(0f, -0.01f, 0f));
            normals.Add(Vector3.down);
            colors.Add(bellyColor);

            // Triangles for carapace top & bottom
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                // Top cap
                triangles.Add(topIdx);
                triangles.Add(ringStart + i);
                triangles.Add(ringStart + next);

                // Bottom belly
                triangles.Add(botIdx);
                triangles.Add(ringStart + next);
                triangles.Add(ringStart + i);
            }

            // Left & Right Chelae Claws
            void AddClaw(float sign)
            {
                int cStart = vertices.Count;
                Vector3 clawBase = new Vector3(sign * 0.09f, 0.025f, 0.07f);
                Vector3 clawElbow = new Vector3(sign * 0.14f, 0.035f, 0.11f);
                Vector3 clawTip = new Vector3(sign * 0.10f, 0.04f, 0.17f);
                Vector3 clawInner = new Vector3(sign * 0.06f, 0.03f, 0.14f);

                vertices.Add(clawBase); normals.Add(Vector3.up); colors.Add(clawColor);
                vertices.Add(clawElbow); normals.Add(Vector3.up); colors.Add(clawColor);
                vertices.Add(clawTip); normals.Add(Vector3.up); colors.Add(clawColor);
                vertices.Add(clawInner); normals.Add(Vector3.up); colors.Add(clawColor);

                triangles.Add(cStart); triangles.Add(cStart + 1); triangles.Add(cStart + 2);
                triangles.Add(cStart); triangles.Add(cStart + 2); triangles.Add(cStart + 3);
            }
            AddClaw(1f);
            AddClaw(-1f);

            // Walking Legs (3 on each side)
            void AddLeg(float sign, float zOffset, float yawDeg)
            {
                int lStart = vertices.Count;
                Quaternion rot = Quaternion.Euler(0, yawDeg, 0);
                Vector3 lBase = new Vector3(sign * 0.08f, 0.01f, zOffset);
                Vector3 lJoint = lBase + rot * new Vector3(sign * 0.07f, 0.03f, 0f);
                Vector3 lFoot = lJoint + rot * new Vector3(sign * 0.06f, -0.05f, 0f);

                vertices.Add(lBase); normals.Add(Vector3.up); colors.Add(legColor);
                vertices.Add(lJoint); normals.Add(Vector3.up); colors.Add(legColor);
                vertices.Add(lFoot); normals.Add(Vector3.up); colors.Add(legColor);

                triangles.Add(lStart); triangles.Add(lStart + 1); triangles.Add(lStart + 2);
            }

            AddLeg(1f, 0.02f, 15f);
            AddLeg(1f, -0.02f, -10f);
            AddLeg(1f, -0.05f, -35f);

            AddLeg(-1f, 0.02f, -15f);
            AddLeg(-1f, -0.02f, 10f);
            AddLeg(-1f, -0.05f, 35f);

            sharedCrabMesh = new Mesh
            {
                name = "Starfall_Protein_Crab_Mesh",
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                colors32 = colors.ToArray(),
                triangles = triangles.ToArray()
            };
            sharedCrabMesh.RecalculateBounds();
            return sharedCrabMesh;
        }

        public static Material GetOrCreateCrabMaterial()
        {
            if (sharedCrabMaterial != null) return sharedCrabMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
            sharedCrabMaterial = new Material(shader)
            {
                name = "Starfall_Crab_Material",
                color = new Color(0.85f, 0.32f, 0.20f, 1f)
            };
            if (sharedCrabMaterial.HasProperty("_Smoothness")) sharedCrabMaterial.SetFloat("_Smoothness", 0.65f); // Wet shiny chitin
            return sharedCrabMaterial;
        }

        public static List<GameObject> SpawnCrabs(Transform parent)
        {
            var spawned = new List<GameObject>();
            var mesh = GetOrCreateCrabMesh();
            var mat = GetOrCreateCrabMaterial();

            for (int i = 0; i < AuthoredLocations.Length; i++)
            {
                var def = AuthoredLocations[i];
                var crabGo = new GameObject($"Crab_Protein_{i + 1}", typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
                crabGo.transform.SetParent(parent, false);

                // Sample terrain height so the crab sits cleanly on wet sand / rocks
                float h = CoastalTerrain.Height(def.position.x, def.position.z);
                crabGo.transform.position = new Vector3(def.position.x, h + 0.03f, def.position.z);
                crabGo.transform.rotation = Quaternion.Euler(0, def.yaw, 0);
                crabGo.transform.localScale = Vector3.one * def.scale;

                crabGo.GetComponent<MeshFilter>().sharedMesh = mesh;
                crabGo.GetComponent<MeshRenderer>().sharedMaterial = mat;

                var col = crabGo.GetComponent<BoxCollider>();
                col.center = new Vector3(0, 0.03f, 0.02f);
                col.size = new Vector3(0.24f, 0.12f, 0.20f);

                crabGo.layer = 9; // Interactive item layer
                spawned.Add(crabGo);
            }

            return spawned;
        }

        public static bool VerifyCrabEcology(out string receipt)
        {
            if (AuthoredLocations.Length < 10)
            {
                receipt = "Crab authored locations insufficient (< 10).";
                return false;
            }

            var mesh = GetOrCreateCrabMesh();
            if (mesh == null || mesh.vertexCount == 0 || mesh.triangles.Length == 0)
            {
                receipt = "Crab procedural mesh generation failed or empty.";
                return false;
            }

            receipt = $"Crab ecology verified: {AuthoredLocations.Length} locations defined, mesh vertices={mesh.vertexCount}, triangles={mesh.triangles.Length / 3}.";
            return true;
        }
    }
}
