using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World
{
    /// <summary>
    /// A bounded surface study for the separate coastal slice. Geometry is fixed; shader waves
    /// are local visual motion. This does not implement swimming, water physics or sea life.
    /// </summary>
    public static class CoastalWater
    {
        public const float Level = -2f;
        public const float VisualSeaHalfWidth = 1800f;
        public const float VisualSeaMinZ = CoastalTerrain.MaxZ;
        public const float VisualSeaMaxZ = 2200f;

        /// <summary>
        /// Creates the 1200 x 1600 metre surface at world y=-2. Enable the scene camera's URP depth
        /// texture for measured shallow colour and shore contact. A sea-direction colour fallback
        /// remains usable without a depth texture. An additional coarse distant sea
        /// is visual-only: no collider, navigation or bathymetry claim beyond the active slice.
        /// The caller owns both generated meshes and their shared material.
        /// </summary>
        public static GameObject Create(Transform parent)
        {
            const int columns = 121, rows = 161;
            var positions = new Vector3[columns * rows];
            var normals = new Vector3[positions.Length];
            var uv = new Vector2[positions.Length];
            var indices = new int[(columns - 1) * (rows - 1) * 6];
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < columns; x++)
            {
                int i = z * columns + x;
                positions[i] = new Vector3(CoastalTerrain.MinX + x * 10f, 0, CoastalTerrain.MinZ + z * 10f);
                normals[i] = Vector3.up;
                uv[i] = new Vector2(x / (float)(columns - 1), z / (float)(rows - 1));
            }
            int index = 0;
            for (int z = 0; z < rows - 1; z++)
            for (int x = 0; x < columns - 1; x++)
            {
                int a = z * columns + x, b = a + 1, c = a + columns, d = c + 1;
                indices[index++] = a; indices[index++] = c; indices[index++] = b;
                indices[index++] = b; indices[index++] = c; indices[index++] = d;
            }
            var mesh = new Mesh { name = "Starfall river and sea 1200x1600m - 10m grid" };
            mesh.vertices = positions; mesh.normals = normals; mesh.uv = uv; mesh.triangles = indices;
            // Vertex waves stay inside this vertical envelope; no per-frame CPU mesh update.
            mesh.bounds = new Bounds(new Vector3(0, 0, 100), new Vector3(1200, .6f, 1600));

            Shader shader = Shader.Find("CityLife/CoastalWater");
            bool fallback = shader == null || !shader.isSupported;
            if (fallback) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null || !shader.isSupported)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw new InvalidOperationException("No supported coastal-water or URP Unlit shader is available.");
            }
            var material = new Material(shader) { name = "Coastal luminous turquoise - surface study" };
            if (fallback)
            {
                // Explicit opaque turquoise fallback avoids a pink error material. Its simplified
                // appearance is reported; it is not evidence of the intended depth/wave shader.
                material.SetColor("_BaseColor", new Color(.008f, .64f, .60f, 1));
                Debug.LogWarning("COASTAL_WATER_FALLBACK: custom shader unavailable; using opaque URP Unlit turquoise. Depth/wave appearance remains unverified.");
            }
            else
            {
                material.SetFloat("_WaterLevel", Level);
                material.SetFloat("_UseSceneDepth", 1);
            }
            var surface = new GameObject("Coastal water - luminous river and sea");
            surface.transform.position = new Vector3(0, Level, 0);
            surface.transform.SetParent(parent, true);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            AddVisualSea(surface.transform, material);
            SetLayerRecursively(surface,4); // built-in Water layer; excluded from its own probe
            AddEnvironmentProbe(parent);
            return surface;
        }

        private static void AddEnvironmentProbe(Transform parent)
        {
            // A bounded realtime probe captures the actual canyon, sky and celestial geometry
            // once when the scene starts. The water layer is excluded to avoid self-reflection.
            var go=new GameObject("Coastal water environment reflection probe");
            go.transform.SetParent(parent,false);
            go.transform.localPosition=new Vector3(0,18,105);
            var probe=go.AddComponent<ReflectionProbe>();
            probe.mode=UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode=UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode=UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.boxProjection=true;
            probe.size=new Vector3(520,300,820);
            probe.nearClipPlane=.5f;
            probe.farClipPlane=1200;
            probe.resolution=128;
            probe.intensity=1;
            probe.cullingMask=~(1<<4);
        }

        private static void SetLayerRecursively(GameObject root,int layer)
        {
            root.layer=layer;
            foreach(Transform child in root.transform) SetLayerRecursively(child.gameObject,layer);
        }

        private static void AddVisualSea(Transform parent, Material material)
        {
            // Keep the active edge's exact 10 m vertex positions, avoiding a wave crack where the
            // two meshes meet. Outer X spans and rows farther out are coarse.
            var xs = new List<float>();
            for (float x = -VisualSeaHalfWidth; x < CoastalTerrain.MinX; x += 40) xs.Add(x);
            for (float x = CoastalTerrain.MinX; x <= CoastalTerrain.MaxX; x += 10) xs.Add(x);
            for (float x = CoastalTerrain.MaxX + 40; x <= VisualSeaHalfWidth; x += 40) xs.Add(x);
            var zs = new List<float> { VisualSeaMinZ, VisualSeaMinZ + 2 };
            for (float z = VisualSeaMinZ + 22; z < VisualSeaMaxZ; z += 20) zs.Add(z);
            zs.Add(VisualSeaMaxZ);
            var positions = new Vector3[xs.Count * zs.Count];
            var normals = new Vector3[positions.Length];
            var indices = new int[(xs.Count - 1) * (zs.Count - 1) * 6];
            for (int z = 0; z < zs.Count; z++)
            for (int x = 0; x < xs.Count; x++)
            {
                int i = z * xs.Count + x;
                positions[i] = new Vector3(xs[x], 0, zs[z]); normals[i] = Vector3.up;
            }
            int index = 0;
            for (int z = 0; z < zs.Count - 1; z++)
            for (int x = 0; x < xs.Count - 1; x++)
            {
                int a = z * xs.Count + x, b = a + 1, c = a + xs.Count, d = c + 1;
                indices[index++] = a; indices[index++] = c; indices[index++] = b;
                indices[index++] = b; indices[index++] = c; indices[index++] = d;
            }
            var mesh = new Mesh { name = "Coastal distant sea - visual only 1920x870m" };
            mesh.vertices = positions; mesh.normals = normals; mesh.triangles = indices;
            mesh.bounds = new Bounds(new Vector3(0, 0, (VisualSeaMinZ + VisualSeaMaxZ) * .5f),
                new Vector3(VisualSeaHalfWidth * 2, .6f, VisualSeaMaxZ - VisualSeaMinZ));
            var distant = new GameObject("Distant sea continuation - visual only, no navigation or collider");
            distant.transform.SetParent(parent, false);
            distant.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = distant.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        }
    }
}
