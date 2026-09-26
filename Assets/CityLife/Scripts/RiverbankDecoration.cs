using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    /// <summary>
    /// Spawns rich natural riverbank world decorations along the canyon river corridor:
    /// - Smooth river pebble beds and rounded slate cobbles along the waterline and ford shallows.
    /// - Riparian coastal reeds and cattails emerging from wet mudflats.
    /// - Dry golden tussock bunchgrass and desert scrub along the upper sandbanks.
    /// Breaks up bare sandy expanses with authentic living wilderness details.
    /// </summary>
    public static class RiverbankDecoration
    {
        private static Mesh sharedPebbleMesh;
        private static Mesh sharedReedMesh;
        private static Mesh sharedTussockMesh;
        private static Material sharedStoneMaterial;
        private static Material sharedFoliageMaterial;

        public static Mesh GetOrCreatePebbleMesh()
        {
            if (sharedPebbleMesh != null) return sharedPebbleMesh;

            // Cluster of 4-6 smooth, rounded river stones
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();

            Color32 slateColor = new Color32(110, 115, 120, 255);
            Color32 quartziteColor = new Color32(155, 140, 125, 255);
            Color32 basaltColor = new Color32(75, 78, 82, 255);
            Color32 sandstoneColor = new Color32(170, 130, 95, 255);

            Color32[] stonePalettes = { slateColor, quartziteColor, basaltColor, sandstoneColor };

            Vector3[] stoneOffsets = {
                new Vector3(0f, 0f, 0f),
                new Vector3(0.22f, 0f, 0.14f),
                new Vector3(-0.18f, 0f, 0.18f),
                new Vector3(0.12f, 0f, -0.20f),
                new Vector3(-0.25f, 0f, -0.12f)
            };
            float[] stoneRadii = { 0.16f, 0.12f, 0.14f, 0.10f, 0.11f };
            float[] stoneHeights = { 0.08f, 0.06f, 0.07f, 0.05f, 0.06f };

            for (int s = 0; s < stoneOffsets.Length; s++)
            {
                int baseIdx = vertices.Count;
                Vector3 center = stoneOffsets[s];
                float r = stoneRadii[s];
                float h = stoneHeights[s];
                Color32 col = stonePalettes[s % stonePalettes.Length];

                // Top dome vertex
                vertices.Add(center + new Vector3(0, h, 0));
                normals.Add(Vector3.up);
                colors.Add(col);

                // 8-segment perimeter ring
                for (int i = 0; i < 8; i++)
                {
                    float angle = (i / 8f) * Mathf.PI * 2f;
                    float vx = Mathf.Cos(angle) * r;
                    float vz = Mathf.Sin(angle) * (r * 0.8f);
                    vertices.Add(center + new Vector3(vx, 0.01f, vz));
                    normals.Add(new Vector3(vx, 0.5f, vz).normalized);
                    colors.Add(col);
                }

                // Dome triangles
                for (int i = 0; i < 8; i++)
                {
                    int next = (i + 1) % 8;
                    triangles.Add(baseIdx);
                    triangles.Add(baseIdx + 1 + i);
                    triangles.Add(baseIdx + 1 + next);
                }
            }

            sharedPebbleMesh = new Mesh
            {
                name = "Starfall_River_Pebble_Cluster",
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                colors32 = colors.ToArray(),
                triangles = triangles.ToArray()
            };
            sharedPebbleMesh.RecalculateBounds();
            return sharedPebbleMesh;
        }

        public static Mesh GetOrCreateReedMesh()
        {
            if (sharedReedMesh != null) return sharedReedMesh;

            // Clump of 6 slender vertical river reeds with brown cattail plumes
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();

            Color32 reedGreen = new Color32(95, 135, 65, 255);
            Color32 reedGold = new Color32(165, 150, 70, 255);
            Color32 plumeBrown = new Color32(115, 75, 45, 255);

            Vector2[] stems = {
                new Vector2(0f, 0f),
                new Vector2(0.08f, 0.06f),
                new Vector2(-0.07f, 0.05f),
                new Vector2(0.05f, -0.07f),
                new Vector2(-0.06f, -0.06f),
                new Vector2(0.12f, -0.02f)
            };
            float[] stemHeights = { 0.75f, 0.85f, 0.68f, 0.92f, 0.72f, 0.80f };

            for (int s = 0; s < stems.Length; s++)
            {
                int b = vertices.Count;
                float sx = stems[s].x;
                float sz = stems[s].y;
                float sh = stemHeights[s];
                float lean = (s % 2 == 0 ? 1 : -1) * 0.04f;

                // 2 crossed quads per reed stem for volumetric visibility
                vertices.Add(new Vector3(sx - 0.025f, 0f, sz)); normals.Add(Vector3.up); colors.Add(reedGreen);
                vertices.Add(new Vector3(sx + 0.025f, 0f, sz)); normals.Add(Vector3.up); colors.Add(reedGreen);
                vertices.Add(new Vector3(sx + 0.015f + lean, sh, sz)); normals.Add(Vector3.up); colors.Add(reedGold);
                vertices.Add(new Vector3(sx - 0.015f + lean, sh, sz)); normals.Add(Vector3.up); colors.Add(reedGold);

                triangles.Add(b); triangles.Add(b + 1); triangles.Add(b + 2);
                triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 3);
                triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 1);
                triangles.Add(b); triangles.Add(b + 3); triangles.Add(b + 2);

                // Perpendicular quad
                int b2 = vertices.Count;
                vertices.Add(new Vector3(sx, 0f, sz - 0.025f)); normals.Add(Vector3.up); colors.Add(reedGreen);
                vertices.Add(new Vector3(sx, 0f, sz + 0.025f)); normals.Add(Vector3.up); colors.Add(reedGreen);
                vertices.Add(new Vector3(sx + lean, sh, sz + 0.015f)); normals.Add(Vector3.up); colors.Add(reedGold);
                vertices.Add(new Vector3(sx + lean, sh, sz - 0.015f)); normals.Add(Vector3.up); colors.Add(reedGold);

                triangles.Add(b2); triangles.Add(b2 + 1); triangles.Add(b2 + 2);
                triangles.Add(b2); triangles.Add(b2 + 2); triangles.Add(b2 + 3);
                triangles.Add(b2); triangles.Add(b2 + 2); triangles.Add(b2 + 1);
                triangles.Add(b2); triangles.Add(b2 + 3); triangles.Add(b2 + 2);

                // Cattail seedhead plume near top
                int cp = vertices.Count;
                float pY1 = sh * 0.70f;
                float pY2 = sh * 0.92f;
                vertices.Add(new Vector3(sx - 0.035f + lean * 0.8f, pY1, sz)); normals.Add(Vector3.up); colors.Add(plumeBrown);
                vertices.Add(new Vector3(sx + 0.035f + lean * 0.8f, pY1, sz)); normals.Add(Vector3.up); colors.Add(plumeBrown);
                vertices.Add(new Vector3(sx + 0.030f + lean * 0.9f, pY2, sz)); normals.Add(Vector3.up); colors.Add(plumeBrown);
                vertices.Add(new Vector3(sx - 0.030f + lean * 0.9f, pY2, sz)); normals.Add(Vector3.up); colors.Add(plumeBrown);

                triangles.Add(cp); triangles.Add(cp + 1); triangles.Add(cp + 2);
                triangles.Add(cp); triangles.Add(cp + 2); triangles.Add(cp + 3);
                triangles.Add(cp); triangles.Add(cp + 2); triangles.Add(cp + 1);
                triangles.Add(cp); triangles.Add(cp + 3); triangles.Add(cp + 2);
            }

            sharedReedMesh = new Mesh
            {
                name = "Starfall_River_Reeds_Clump",
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                colors32 = colors.ToArray(),
                triangles = triangles.ToArray()
            };
            sharedReedMesh.RecalculateBounds();
            return sharedReedMesh;
        }

        public static Mesh GetOrCreateTussockMesh()
        {
            if (sharedTussockMesh != null) return sharedTussockMesh;

            // Dry golden desert bunchgrass clump
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();

            Color32 grassBase = new Color32(145, 130, 75, 255);
            Color32 grassTip = new Color32(185, 170, 105, 255);

            for (int blade = 0; blade < 8; blade++)
            {
                int b = vertices.Count;
                float angle = (blade / 8f) * Mathf.PI * 2f;
                float bx = Mathf.Cos(angle) * 0.06f;
                float bz = Mathf.Sin(angle) * 0.06f;

                float tipX = Mathf.Cos(angle) * 0.22f;
                float tipZ = Mathf.Sin(angle) * 0.22f;
                float tipY = UnityEngine.Random.Range(0.28f, 0.42f);

                vertices.Add(new Vector3(bx - 0.02f, 0f, bz)); normals.Add(Vector3.up); colors.Add(grassBase);
                vertices.Add(new Vector3(bx + 0.02f, 0f, bz)); normals.Add(Vector3.up); colors.Add(grassBase);
                vertices.Add(new Vector3(tipX, tipY, tipZ)); normals.Add(Vector3.up); colors.Add(grassTip);

                triangles.Add(b); triangles.Add(b + 1); triangles.Add(b + 2);
                triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 1);
            }

            sharedTussockMesh = new Mesh
            {
                name = "Starfall_Dry_Tussock_Grass",
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                colors32 = colors.ToArray(),
                triangles = triangles.ToArray()
            };
            sharedTussockMesh.RecalculateBounds();
            return sharedTussockMesh;
        }

        public static Material GetOrCreateStoneMaterial()
        {
            if (sharedStoneMaterial != null) return sharedStoneMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
            sharedStoneMaterial = new Material(shader)
            {
                name = "Starfall_River_Stone_Material",
                color = new Color(0.65f, 0.62f, 0.58f, 1f)
            };
            if (sharedStoneMaterial.HasProperty("_Smoothness")) sharedStoneMaterial.SetFloat("_Smoothness", 0.45f); // Wet river stones
            return sharedStoneMaterial;
        }

        public static Material GetOrCreateFoliageMaterial()
        {
            if (sharedFoliageMaterial != null) return sharedFoliageMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
            sharedFoliageMaterial = new Material(shader)
            {
                name = "Starfall_Riparian_Foliage_Material",
                color = new Color(0.70f, 0.75f, 0.45f, 1f)
            };
            if (sharedFoliageMaterial.HasProperty("_Smoothness")) sharedFoliageMaterial.SetFloat("_Smoothness", 0.20f);
            return sharedFoliageMaterial;
        }

        public static GameObject SpawnDecorations(Transform parent)
        {
            var decorRoot = new GameObject("Canyon_Riverbank_World_Decoration");
            decorRoot.transform.SetParent(parent, false);

            var pebbleMesh = GetOrCreatePebbleMesh();
            var reedMesh = GetOrCreateReedMesh();
            var tussockMesh = GetOrCreateTussockMesh();
            var stoneMat = GetOrCreateStoneMaterial();
            var foliageMat = GetOrCreateFoliageMaterial();

            // 1. Waterline Pebble Beds (concentrated along shallow water margins and river ford)
            Vector2[] pebbleSites = {
                new Vector2(-6f, -18f), new Vector2(10f, -14f), new Vector2(-15f, -22f), new Vector2(18f, -20f), // Ford shallows
                new Vector2(25f, -45f), new Vector2(-20f, -55f), new Vector2(15f, -80f), new Vector2(-28f, -95f), // South riverbank
                new Vector2(8f, -120f), new Vector2(-12f, -150f), new Vector2(14f, -180f), // Waterfall canyon approach
                new Vector2(20f, 15f), new Vector2(-18f, 30f), new Vector2(12f, 65f), new Vector2(-15f, 90f),  // North channel
                new Vector2(24f, 120f), new Vector2(-8f, 140f), new Vector2(16f, 175f), new Vector2(-22f, 205f) // Delta flats
            };

            for (int i = 0; i < pebbleSites.Length; i++)
            {
                var site = pebbleSites[i];
                float y = CoastalTerrain.Height(site.x, site.y);
                var go = new GameObject($"River_Pebbles_{i + 1}", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(decorRoot.transform, false);
                go.transform.position = new Vector3(site.x, y + 0.01f, site.y);
                go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                go.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.85f, 1.35f);
                go.GetComponent<MeshFilter>().sharedMesh = pebbleMesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;
            }

            // 2. Riparian Shoreline Reeds (standing along the wet sand and muddy delta shallows)
            Vector2[] reedSites = {
                new Vector2(-12f, -15f), new Vector2(15f, -12f), new Vector2(-4f, -28f), // Ford margins
                new Vector2(28f, -50f), new Vector2(-24f, -70f), new Vector2(19f, -100f), // Mid-river banks
                new Vector2(-16f, 20f), new Vector2(22f, 40f), new Vector2(-10f, 75f),   // North shallows
                new Vector2(18f, 110f), new Vector2(-20f, 145f), new Vector2(14f, 190f)  // Delta reeds
            };

            for (int i = 0; i < reedSites.Length; i++)
            {
                var site = reedSites[i];
                float y = CoastalTerrain.Height(site.x, site.y);
                var go = new GameObject($"River_Reeds_{i + 1}", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(decorRoot.transform, false);
                go.transform.position = new Vector3(site.x, y, site.y);
                go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                go.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.9f, 1.2f);
                go.GetComponent<MeshFilter>().sharedMesh = reedMesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = foliageMat;
            }

            // 3. Dry Golden Tussock Grass (transitioning from sandy shore up the canyon banks)
            Vector2[] tussockSites = {
                new Vector2(32f, -25f), new Vector2(35f, -38f), new Vector2(-35f, -25f), new Vector2(-38f, -40f), // Ford terrace
                new Vector2(30f, -75f), new Vector2(-32f, -85f), new Vector2(25f, -130f), // South banks
                new Vector2(32f, 25f), new Vector2(-30f, 45f), new Vector2(28f, 85f),   // North banks
                new Vector2(36f, 130f), new Vector2(-34f, 160f), new Vector2(28f, 215f) // Delta scrub
            };

            for (int i = 0; i < tussockSites.Length; i++)
            {
                var site = tussockSites[i];
                float y = CoastalTerrain.Height(site.x, site.y);
                var go = new GameObject($"River_Tussock_{i + 1}", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(decorRoot.transform, false);
                go.transform.position = new Vector3(site.x, y, site.y);
                go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                go.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.8f, 1.3f);
                go.GetComponent<MeshFilter>().sharedMesh = tussockMesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = foliageMat;
            }

            return decorRoot;
        }
    }
}
