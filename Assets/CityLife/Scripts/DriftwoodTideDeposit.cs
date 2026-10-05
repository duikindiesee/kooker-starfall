using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    /// <summary>
    /// Spawns and manages tidal driftwood washed ashore along the river delta and coastal sandbars.
    /// Inflowing driftwood provides rich fuel for the refuge hearth and building material.
    /// </summary>
    public sealed class DriftwoodTideDeposit : MonoBehaviour
    {
        public const int DefaultDriftwoodCount = 8;

        [Serializable]
        public struct DriftwoodSpawnDef
        {
            public Vector3 position;
            public float scale;
            public float yaw;
            public float pitch;
        }

        public static readonly DriftwoodSpawnDef[] AuthoredLocations = new DriftwoodSpawnDef[]
        {
            new DriftwoodSpawnDef { position = new Vector3(-8f, -2.5f, 48f), scale = 1.0f, yaw = 25f, pitch = 4f },
            new DriftwoodSpawnDef { position = new Vector3(14f, -2.6f, 65f), scale = 1.15f, yaw = 110f, pitch = -3f },
            new DriftwoodSpawnDef { position = new Vector3(-20f, -2.4f, 82f), scale = 0.95f, yaw = 70f, pitch = 6f },
            new DriftwoodSpawnDef { position = new Vector3(6f, -2.7f, 102f), scale = 1.25f, yaw = 195f, pitch = -2f },
            new DriftwoodSpawnDef { position = new Vector3(-14f, -2.5f, 118f), scale = 1.05f, yaw = 315f, pitch = 5f },
            new DriftwoodSpawnDef { position = new Vector3(22f, -2.5f, 132f), scale = 0.9f, yaw = 40f, pitch = -4f },
            new DriftwoodSpawnDef { position = new Vector3(-4f, -2.6f, 146f), scale = 1.2f, yaw = 160f, pitch = 3f },
            new DriftwoodSpawnDef { position = new Vector3(12f, -2.5f, 160f), scale = 1.1f, yaw = 275f, pitch = -5f }
        };

        private static Mesh sharedDriftwoodMesh;
        private static Material sharedDriftwoodMaterial;

        public static Mesh GetOrCreateDriftwoodMesh()
        {
            if (sharedDriftwoodMesh != null) return sharedDriftwoodMesh;

            // Generate procedural weathered driftwood cylinder trunk with broken bark ends
            int radialSegments = 7;
            int lengthSegments = 5;
            float length = 0.95f;
            float radius = 0.11f;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();

            Color32 barkColor = new Color32(138, 126, 112, 255);    // Salt-bleached weathered wood
            Color32 coreColor = new Color32(165, 150, 130, 255);    // Exposed interior wood grain
            Color32 mossColor = new Color32(95, 110, 85, 255);      // Tidal algae/seaweed stain

            for (int l = 0; l <= lengthSegments; l++)
            {
                float t = (float)l / lengthSegments;
                float z = (t - 0.5f) * length;
                float taper = 1.0f - t * 0.25f; // Slight taper toward end

                for (int r = 0; r < radialSegments; r++)
                {
                    float angle = (float)r / radialSegments * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * radius * taper;
                    float y = Mathf.Sin(angle) * radius * taper;

                    // Subtle undulating bumpiness
                    float bump = Mathf.Sin(t * 8f + angle * 2f) * 0.012f;
                    Vector3 normal = new Vector3(x, y, 0).normalized;
                    Vector3 pos = new Vector3(x + normal.x * bump, y + normal.y * bump, z);

                    vertices.Add(pos);
                    normals.Add(normal);

                    // Marine algae tinting on underside/ends
                    Color32 vertexColor = (normal.y < -0.3f || t > 0.8f) ? Color32.Lerp(barkColor, mossColor, 0.45f) : barkColor;
                    colors.Add(vertexColor);
                }
            }

            for (int l = 0; l < lengthSegments; l++)
            {
                int r1 = l * radialSegments;
                int r2 = (l + 1) * radialSegments;
                for (int r = 0; r < radialSegments; r++)
                {
                    int next = (r + 1) % radialSegments;
                    triangles.Add(r1 + r);
                    triangles.Add(r2 + r);
                    triangles.Add(r1 + next);

                    triangles.Add(r1 + next);
                    triangles.Add(r2 + r);
                    triangles.Add(r2 + next);
                }
            }

            // End caps
            int cap1 = vertices.Count;
            vertices.Add(new Vector3(0, 0, -length * 0.5f));
            normals.Add(Vector3.back);
            colors.Add(coreColor);
            for (int r = 0; r < radialSegments; r++)
            {
                int next = (r + 1) % radialSegments;
                triangles.Add(cap1);
                triangles.Add(next);
                triangles.Add(r);
            }

            int cap2 = vertices.Count;
            vertices.Add(new Vector3(0, 0, length * 0.5f));
            normals.Add(Vector3.forward);
            colors.Add(coreColor);
            int lastRing = lengthSegments * radialSegments;
            for (int r = 0; r < radialSegments; r++)
            {
                int next = (r + 1) % radialSegments;
                triangles.Add(cap2);
                triangles.Add(lastRing + r);
                triangles.Add(lastRing + next);
            }

            sharedDriftwoodMesh = new Mesh
            {
                name = "Starfall_Driftwood_Mesh",
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                colors32 = colors.ToArray(),
                triangles = triangles.ToArray()
            };
            sharedDriftwoodMesh.RecalculateBounds();
            return sharedDriftwoodMesh;
        }

        public static Material GetOrCreateDriftwoodMaterial()
        {
            if (sharedDriftwoodMaterial != null) return sharedDriftwoodMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
            sharedDriftwoodMaterial = new Material(shader)
            {
                name = "Starfall_Driftwood_Material",
                color = new Color(0.55f, 0.50f, 0.44f, 1f)
            };
            if (sharedDriftwoodMaterial.HasProperty("_Smoothness")) sharedDriftwoodMaterial.SetFloat("_Smoothness", 0.15f);
            return sharedDriftwoodMaterial;
        }

        public static List<GameObject> SpawnDriftwood(Transform parent)
        {
            var spawned = new List<GameObject>();
            var mesh = GetOrCreateDriftwoodMesh();
            var mat = GetOrCreateDriftwoodMaterial();

            for (int i = 0; i < AuthoredLocations.Length; i++)
            {
                var def = AuthoredLocations[i];
                var woodGo = new GameObject($"Driftwood_Log_{i + 1}", typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
                woodGo.transform.SetParent(parent, false);

                float h = CoastalTerrain.Height(def.position.x, def.position.z);
                woodGo.transform.position = new Vector3(def.position.x, h + 0.08f, def.position.z);
                woodGo.transform.rotation = Quaternion.Euler(def.pitch, def.yaw, 0);
                woodGo.transform.localScale = Vector3.one * def.scale;

                woodGo.GetComponent<MeshFilter>().sharedMesh = mesh;
                woodGo.GetComponent<MeshRenderer>().sharedMaterial = mat;

                var col = woodGo.GetComponent<BoxCollider>();
                col.center = Vector3.zero;
                col.size = new Vector3(0.24f, 0.24f, 0.95f);

                woodGo.layer = 9; // Interactive item layer
                spawned.Add(woodGo);
            }

            return spawned;
        }

        public static bool VerifyDriftwoodEcology(out string receipt)
        {
            if (AuthoredLocations.Length < 6)
            {
                receipt = "Driftwood authored locations insufficient (< 6).";
                return false;
            }

            var mesh = GetOrCreateDriftwoodMesh();
            if (mesh == null || mesh.vertexCount == 0 || mesh.triangles.Length == 0)
            {
                receipt = "Driftwood procedural mesh generation failed or empty.";
                return false;
            }

            receipt = $"Driftwood ecology verified: {AuthoredLocations.Length} logs defined, mesh vertices={mesh.vertexCount}, triangles={mesh.triangles.Length / 3}.";
            return true;
        }
    }
}
