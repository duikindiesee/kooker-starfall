using System;
using UnityEngine;

namespace CityLife.World
{
    /// <summary>Authored offshore silhouettes, not accessible terrain or boat gameplay.</summary>
    public static class CoastalDistantIslands
    {
        public static GameObject Create(Transform parent)
        {
            var shader = Shader.Find("CityLife/CoastalTerrain");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Offshore islands require the coastal terrain shader.");
            var root = new GameObject("Distant islands - visual only - outside playable boundary");
            root.transform.SetParent(parent, false);
            var material = new Material(shader) { name = "Offshore weathered ochre stone" };
            material.SetFloat("_SeaLevel", CoastalWater.Level);
            Add(root.transform, material, "Western split mesa", -340, 1110, 145, 105, 68, 19);
            Add(root.transform, material, "Eastern narrow stack", 275, 1230, 95, 130, 94, 47);
            Add(root.transform, material, "Far low island", -50, 1580, 210, 120, 48, 83);
            return root;
        }

        private static void Add(Transform parent, Material material, string name,
            float cx, float cz, float rx, float rz, float peak, float seed)
        {
            const int cells = 48;
            int stride = cells + 1;
            var vertices = new Vector3[stride * stride];
            var triangles = new int[cells * cells * 6];
            int index = 0;
            for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
            {
                float u = x * 2f / cells - 1f, v = z * 2f / cells - 1f;
                float noise = Mathf.PerlinNoise(u * 3.3f + seed, v * 3.8f + seed * .37f);
                // An irregular elongated footprint and recessed saddle avoid round cones.
                float radius = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 2.6f) + Mathf.Pow(Mathf.Abs(v), 2.6f), 1f / 2.6f);
                radius += (noise - .5f) * .15f;
                float shoulder = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.67f, .95f, radius));
                float crest = .80f + .20f * Mathf.PerlinNoise(u * 5 + seed, v * 4 + 12);
                float cleft = Mathf.Exp(-Mathf.Pow((u + .13f + v * .30f) / .13f, 2)) * .23f;
                float height = CoastalWater.Level - 12f + (peak + 12f) * shoulder * (crest - cleft);
                int at = z * stride + x;
                vertices[at] = new Vector3(cx + u * rx, height, cz + v * rz);
                if (x == cells || z == cells) continue;
                triangles[index++] = at; triangles[index++] = at + stride; triangles[index++] = at + 1;
                triangles[index++] = at + 1; triangles[index++] = at + stride; triangles[index++] = at + stride + 1;
            }
            var mesh = new Mesh { name = name + " deterministic offshore mesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var island = new GameObject(name + " - inaccessible visual landform");
            island.transform.SetParent(parent, false);
            island.AddComponent<MeshFilter>().sharedMesh = mesh;
            island.AddComponent<MeshRenderer>().sharedMaterial = material;
            // No collider, food, save identity or navigation registration: outside this slice.
        }
    }
}
