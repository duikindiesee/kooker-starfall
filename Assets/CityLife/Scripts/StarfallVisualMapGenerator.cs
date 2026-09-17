using System;
using UnityEngine;

namespace CityLife.World
{
    /// <summary>
    /// Procedurally generates a high-resolution topographical relief texture and dynamic Fog-of-War
    /// mask for the canyon world, inspired by tactical game maps in Ghost Recon Wildlands and God of War.
    /// </summary>
    public static class StarfallVisualMapGenerator
    {
        public const float MapMinX = -250f;
        public const float MapMaxX = 250f;
        public const float MapMinZ = -250f;
        public const float MapMaxZ = 250f;
        public const int TextureWidth = 384;
        public const int TextureHeight = 384;

        // Biome and elevation palette
        private static readonly Color WaterDeep = new Color(0.04f, 0.22f, 0.32f, 1f);
        private static readonly Color WaterShallow = new Color(0.10f, 0.48f, 0.54f, 1f);
        private static readonly Color WetSand = new Color(0.42f, 0.40f, 0.35f, 1f);
        private static readonly Color CanyonFloor = new Color(0.68f, 0.56f, 0.42f, 1f);
        private static readonly Color MesaShoulder = new Color(0.58f, 0.44f, 0.32f, 1f);
        private static readonly Color MesaCliff = new Color(0.44f, 0.32f, 0.24f, 1f);
        private static readonly Color MesaTop = new Color(0.52f, 0.40f, 0.30f, 1f);
        private static readonly Color OasisGreen = new Color(0.24f, 0.42f, 0.28f, 1f);
        private static readonly Color ContourLineColor = new Color(0.25f, 0.18f, 0.12f, 0.35f);
        private static readonly Color GridLineColor = new Color(0.85f, 0.75f, 0.60f, 0.12f);

        public static Vector2 WorldToMapUV(Vector3 worldPos)
        {
            float u = Mathf.InverseLerp(MapMinX, MapMaxX, worldPos.x);
            float v = Mathf.InverseLerp(MapMinZ, MapMaxZ, worldPos.z);
            return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
        }

        public static Vector3 MapUVToWorld(Vector2 uv)
        {
            float x = Mathf.Lerp(MapMinX, MapMaxX, uv.x);
            float z = Mathf.Lerp(MapMinZ, MapMaxZ, uv.y);
            float y = CoastalTerrain.Height(x, z);
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Generates the base topographical relief map texture with elevation shading,
        /// river bathymetry, cliff hillshading, contour lines, and tactical grid.
        /// </summary>
        public static Texture2D GenerateTopographicalTexture()
        {
            var tex = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false)
            {
                name = "Starfall_Topographical_Map",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var colors = new Color32[TextureWidth * TextureHeight];
            Vector3 sunDir = new Vector3(-0.6f, 0.7f, 0.4f).normalized;

            for (int y = 0; y < TextureHeight; y++)
            {
                float v = (float)y / (TextureHeight - 1);
                float wz = Mathf.Lerp(MapMinZ, MapMaxZ, v);

                for (int x = 0; x < TextureWidth; x++)
                {
                    float u = (float)x / (TextureWidth - 1);
                    float wx = Mathf.Lerp(MapMinX, MapMaxX, u);

                    // Sample height and neighbours for slope/normal
                    float h = CoastalTerrain.Height(wx, wz);
                    float hR = CoastalTerrain.Height(wx + 1.2f, wz);
                    float hU = CoastalTerrain.Height(wx, wz + 1.2f);

                    Vector3 normal = Vector3.Cross(new Vector3(1.2f, hR - h, 0), new Vector3(0, hU - h, 1.2f)).normalized;
                    if (normal.y < 0) normal = -normal;

                    float slope = 1f - Mathf.Clamp01(normal.y);
                    float hillshade = Mathf.Clamp(Vector3.Dot(normal, sunDir) * 0.5f + 0.5f, 0.3f, 1.2f);

                    Color baseColor;
                    float waterLevel = CoastalWater.Level;

                    if (h < waterLevel)
                    {
                        // Water depth gradient
                        float depth = Mathf.Clamp01((waterLevel - h) / 2.5f);
                        baseColor = Color.Lerp(WaterShallow, WaterDeep, depth);
                        // Subtle specular sheen on water
                        baseColor *= (0.85f + 0.25f * hillshade);
                    }
                    else if (h < waterLevel + 0.6f)
                    {
                        // Wet shoreline / pebble bed
                        float t = (h - waterLevel) / 0.6f;
                        baseColor = Color.Lerp(WetSand, CanyonFloor, t);
                        baseColor *= hillshade;
                    }
                    else if (h < 8f)
                    {
                        // Canyon valley floor
                        float t = (h - (waterLevel + 0.6f)) / (8f - waterLevel - 0.6f);
                        baseColor = Color.Lerp(CanyonFloor, MesaShoulder, t);

                        // Oasis vegetation tint near spring (121, -58) and berry patches (126, -80)
                        float distSpring = Vector2.Distance(new Vector2(wx, wz), new Vector2(121f, -58f));
                        float distBerry = Vector2.Distance(new Vector2(wx, wz), new Vector2(126f, -80f));
                        if (distSpring < 24f || distBerry < 20f)
                        {
                            float vegT = Mathf.Max(Mathf.Clamp01(1f - distSpring / 24f), Mathf.Clamp01(1f - distBerry / 20f)) * 0.45f;
                            baseColor = Color.Lerp(baseColor, OasisGreen, vegT);
                        }

                        baseColor *= hillshade;
                    }
                    else if (h < 40f)
                    {
                        // Mesa cliffs / slopes
                        float t = (h - 8f) / 32f;
                        Color cliffTone = slope > 0.45f ? MesaCliff : MesaShoulder;
                        baseColor = Color.Lerp(cliffTone, MesaTop, t);
                        baseColor *= hillshade;
                    }
                    else
                    {
                        // High mesa top plateau
                        baseColor = MesaTop * hillshade;
                    }

                    // Subtle elevation contour lines (every 6 metres)
                    float contourMod = Mathf.Repeat(h, 6f);
                    if (contourMod < 0.35f && h >= waterLevel)
                    {
                        baseColor = Color.Lerp(baseColor, ContourLineColor, 0.4f);
                    }

                    // Tactical military grid lines (every 50 metres in world space)
                    float gridModX = Mathf.Abs(Mathf.Repeat(wx + 250f, 50f));
                    float gridModZ = Mathf.Abs(Mathf.Repeat(wz + 250f, 50f));
                    if (gridModX < 1.0f || gridModZ < 1.0f)
                    {
                        baseColor = Color.Lerp(baseColor, GridLineColor, 0.6f);
                    }

                    colors[y * TextureWidth + x] = (Color32)baseColor;
                }
            }

            tex.SetPixels32(colors);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Creates a Fog of War texture mask where unexplored is dark atmospheric mist
        /// and explored cells are burned away with soft radial transparency.
        /// </summary>
        public static Texture2D CreateFogOfWarTexture()
        {
            var tex = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false)
            {
                name = "Starfall_FogOfWar_Mask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var fogColor = new Color32(10, 16, 24, 248); // Dark tactical parchment fog
            var colors = new Color32[TextureWidth * TextureHeight];
            for (int i = 0; i < colors.Length; i++) colors[i] = fogColor;

            tex.SetPixels32(colors);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Clears Fog of War around the given world cell coordinates with smooth radial falloff.
        /// </summary>
        public static void RevealCell(Color32[] fogPixels, int cellX, int cellZ, float revealRadiusMetres = 22f)
        {
            float wx = cellX * 3f;
            float wz = cellZ * 3f;
            Vector2 uv = WorldToMapUV(new Vector3(wx, 0, wz));

            int cx = Mathf.RoundToInt(uv.x * (TextureWidth - 1));
            int cy = Mathf.RoundToInt(uv.y * (TextureHeight - 1));
            int pixelRadius = Mathf.RoundToInt(revealRadiusMetres / (MapMaxX - MapMinX) * TextureWidth);
            pixelRadius = Mathf.Clamp(pixelRadius, 6, 40);

            int minPx = Mathf.Max(0, cx - pixelRadius);
            int maxPx = Mathf.Min(TextureWidth - 1, cx + pixelRadius);
            int minPy = Mathf.Max(0, cy - pixelRadius);
            int maxPy = Mathf.Min(TextureHeight - 1, cy + pixelRadius);

            float rSq = pixelRadius * pixelRadius;

            for (int py = minPy; py <= maxPy; py++)
            {
                int dy = py - cy;
                int dySq = dy * dy;

                for (int px = minPx; px <= maxPx; px++)
                {
                    int dx = px - cx;
                    int distSq = dx * dx + dySq;
                    if (distSq >= rSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float normalizedDist = dist / pixelRadius;
                    // Smooth quadratic falloff: completely transparent at center, fading out to perimeter
                    float targetAlpha = Mathf.SmoothStep(0f, 1f, normalizedDist) * 248f;

                    int idx = py * TextureWidth + px;
                    byte currentA = fogPixels[idx].a;
                    if (targetAlpha < currentA)
                    {
                        fogPixels[idx].a = (byte)targetAlpha;
                    }
                }
            }
        }
    }
}
