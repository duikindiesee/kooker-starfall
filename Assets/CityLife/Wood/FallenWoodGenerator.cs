using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.Wood
{
    /// <summary>
    /// Standalone procedural mesh generator for fallen branches and logs (Geometry Version 2).
    /// Authored in local metre units, unit transform scale, deterministic explicit seed,
    /// sensible physical bounding dimensions and authored dry mass metadata (staging estimates).
    /// Geometry Version 2 ensures outward triangle winding, exact seam vertex coincidence,
    /// rough cap perimeter alignment, and bounded finite input validation before allocation.
    /// Uses existing Unity APIs only; dimensions conversion bridge depends on CityLife.Items.
    /// </summary>
    public static class FallenWoodGenerator
    {
        /// <summary>
        /// Pure deterministic pseudo-random number generator (32-bit XorShift).
        /// Zero dependency on UnityEngine.Random, clock time, render frames, or chunk travel order.
        /// </summary>
        public struct DeterministicRng
        {
            private uint state;

            public DeterministicRng(uint seed)
            {
                // Ensure non-zero seed state for xorshift
                state = (seed == 0) ? 0x8545A25Bu : seed;
            }

            public uint NextUInt()
            {
                uint x = state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                state = x;
                return x;
            }

            public float NextFloat()
            {
                return (NextUInt() & 0x00FFFFFF) / (float)0x01000000;
            }

            public float Range(float min, float max)
            {
                return min + (max - min) * NextFloat();
            }

            public float NextAngle()
            {
                return NextFloat() * Mathf.PI * 2f;
            }
        }

        /// <summary>
        /// Generates a fallen-wood mesh and outputs complete asset metadata.
        /// </summary>
        public static Mesh GenerateMesh(FallenWoodProfile profile, uint seed, out FallenWoodAssetMetadata metadata)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            if (!profile.IsValid())
            {
                throw new ArgumentException("Provided FallenWoodProfile contains invalid, non-finite, or out-of-bounds parameters.", nameof(profile));
            }

            // Geometry Version 2: Outward-facing winding, rough cap perimeter alignment,
            // and exact seam vertex coincidence with explicit RNG consumption.
            var rng = new DeterministicRng(seed);

            int radialSegments = profile.radialSegments;
            int lengthSegments = profile.lengthSegments;
            float length = profile.length;
            float baseRadius = profile.baseRadius;
            float tipRadius = profile.tipRadius;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();
            var triangles = new List<int>();

            // 1. Compute Spine Points and Local Ring Coordinate Frames
            int ringCount = lengthSegments + 1;
            var spinePoints = new Vector3[ringCount];
            var spineTangents = new Vector3[ringCount];
            var spineRights = new Vector3[ringCount];
            var spineUps = new Vector3[ringCount];
            var ringRadii = new float[ringCount];

            // Deterministic natural bow & wobble
            float bowAngle = rng.NextAngle();
            float bowMag = rng.Range(0f, profile.curvature);
            Vector2 bowDir = new Vector2(Mathf.Cos(bowAngle), Mathf.Sin(bowAngle)) * bowMag;

            float wobbleAngle = rng.NextAngle();
            float wobbleMag = rng.Range(0f, profile.curvature * 0.35f);
            Vector2 wobbleDir = new Vector2(Mathf.Cos(wobbleAngle), Mathf.Sin(wobbleAngle)) * wobbleMag;

            float halfLength = length * 0.5f;

            for (int i = 0; i < ringCount; i++)
            {
                float t = i / (float)lengthSegments;
                // Natural bow (parabolic curve peaking at center)
                float bowWeight = 4f * t * (1f - t);
                // Subtle S-curve wobble
                float wobbleWeight = Mathf.Sin(t * Mathf.PI * 2f);

                Vector2 offsetXY = bowDir * bowWeight + wobbleDir * wobbleWeight;
                float z = -halfLength + t * length;

                spinePoints[i] = new Vector3(offsetXY.x, offsetXY.y, z);
                ringRadii[i] = Mathf.Lerp(baseRadius, tipRadius, t);
            }

            // Compute frame orientations along spine
            for (int i = 0; i < ringCount; i++)
            {
                Vector3 tangent;
                if (i == 0)
                    tangent = (spinePoints[1] - spinePoints[0]).normalized;
                else if (i == ringCount - 1)
                    tangent = (spinePoints[ringCount - 1] - spinePoints[ringCount - 2]).normalized;
                else
                    tangent = (spinePoints[i + 1] - spinePoints[i - 1]).normalized;

                spineTangents[i] = tangent;

                Quaternion rot = Quaternion.FromToRotation(Vector3.forward, tangent);
                spineRights[i] = rot * Vector3.right;
                spineUps[i] = rot * Vector3.up;
            }

            // 2. Authored Trunk Tube Vertices & Bark Colors
            // Ring vertices: (radialSegments + 1) per ring to cleanly wrap UV seams
            int radialVertsPerRing = radialSegments + 1;

            for (int i = 0; i < ringCount; i++)
            {
                float t = i / (float)lengthSegments;
                Vector3 center = spinePoints[i];
                Vector3 right = spineRights[i];
                Vector3 up = spineUps[i];
                float radius = ringRadii[i];
                int ringStartVertIndex = vertices.Count;

                for (int j = 0; j < radialSegments; j++)
                {
                    float angleFraction = j / (float)radialSegments;
                    float angle = angleFraction * Mathf.PI * 2f;

                    // Deterministic bark surface roughness displacement
                    float rough = rng.Range(-profile.barkRoughness, profile.barkRoughness);
                    float displacedRadius = Mathf.Max(0.001f, radius + rough);

                    Vector3 radialDir = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    Vector3 pos = center + radialDir * displacedRadius;

                    // Smooth radial normal
                    Vector3 norm = radialDir.normalized;

                    // Physical metre UVs along length
                    Vector2 uv = new Vector2(angleFraction, t * length);

                    // Authored bark vertex color with subtle weathered variation
                    float barkTone = rng.Range(0f, 1f);
                    Color32 barkColor = Color32.Lerp(profile.barkColorBase, profile.barkColorVariation, barkTone);

                    vertices.Add(pos);
                    normals.Add(norm);
                    uvs.Add(uv);
                    colors.Add(barkColor);
                }

                // Seam duplicate vertex at j = radialSegments:
                // Reuses position, normal, and color from j = 0 ring vertex to guarantee exact geometric coincidence,
                // while splitting UV.x to 1.0f for seamless wrapping. Zero redundant RNG consumption.
                vertices.Add(vertices[ringStartVertIndex]);
                normals.Add(normals[ringStartVertIndex]);
                uvs.Add(new Vector2(1f, t * length));
                colors.Add(colors[ringStartVertIndex]);
            }

            // 3. Trunk Tube Triangles (Outward-facing winding)
            for (int i = 0; i < lengthSegments; i++)
            {
                int rowA = i * radialVertsPerRing;
                int rowB = (i + 1) * radialVertsPerRing;

                for (int j = 0; j < radialSegments; j++)
                {
                    int v00 = rowA + j;
                    int v01 = rowA + j + 1;
                    int v10 = rowB + j;
                    int v11 = rowB + j + 1;

                    // Quad split into 2 outward-facing triangles
                    triangles.Add(v00);
                    triangles.Add(v01);
                    triangles.Add(v10);

                    triangles.Add(v01);
                    triangles.Add(v11);
                    triangles.Add(v10);
                }
            }

            // 4. Base End-Grain Cap (Backwards facing at z = -halfLength)
            {
                int baseCenterIndex = vertices.Count;
                Vector3 baseCenter = spinePoints[0];
                Vector3 baseNormal = -spineTangents[0];

                // Center vertex: core heartwood color
                vertices.Add(baseCenter);
                normals.Add(baseNormal);
                uvs.Add(new Vector2(0.5f, 0.5f));
                colors.Add(profile.endGrainCoreColor);

                // Cap perimeter copies actual rough rim vertices from trunk ring 0
                int baseTubeRingStart = 0;
                int baseRingStart = vertices.Count;
                for (int j = 0; j <= radialSegments; j++)
                {
                    float angleFraction = j / (float)radialSegments;
                    float angle = angleFraction * Mathf.PI * 2f;
                    Vector3 pos = vertices[baseTubeRingStart + j];

                    // Sapwood perimeter color with slight ring nuance
                    float ringNuance = rng.Range(0f, 0.25f);
                    Color32 sapColor = Color32.Lerp(profile.endGrainSapColor, profile.endGrainCoreColor, ringNuance);

                    vertices.Add(pos);
                    normals.Add(baseNormal);
                    uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(angle), 0.5f + 0.5f * Mathf.Sin(angle)));
                    colors.Add(sapColor);
                }

                // Base fan triangles (facing backwards along baseNormal)
                for (int j = 0; j < radialSegments; j++)
                {
                    triangles.Add(baseCenterIndex);
                    triangles.Add(baseRingStart + j + 1);
                    triangles.Add(baseRingStart + j);
                }
            }

            // 5. Tip End-Grain Cap (Forward facing at z = +halfLength)
            {
                int tipCenterIndex = vertices.Count;
                Vector3 tipCenter = spinePoints[ringCount - 1];
                Vector3 tipNormal = spineTangents[ringCount - 1];

                // Center vertex: core heartwood color
                vertices.Add(tipCenter);
                normals.Add(tipNormal);
                uvs.Add(new Vector2(0.5f, 0.5f));
                colors.Add(profile.endGrainCoreColor);

                // Cap perimeter copies actual rough rim vertices from trunk tip ring
                int tipTubeRingStart = (ringCount - 1) * radialVertsPerRing;
                int tipRingStart = vertices.Count;
                for (int j = 0; j <= radialSegments; j++)
                {
                    float angleFraction = j / (float)radialSegments;
                    float angle = angleFraction * Mathf.PI * 2f;
                    Vector3 pos = vertices[tipTubeRingStart + j];

                    float ringNuance = rng.Range(0f, 0.25f);
                    Color32 sapColor = Color32.Lerp(profile.endGrainSapColor, profile.endGrainCoreColor, ringNuance);

                    vertices.Add(pos);
                    normals.Add(tipNormal);
                    uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(angle), 0.5f + 0.5f * Mathf.Sin(angle)));
                    colors.Add(sapColor);
                }

                // Tip fan triangles (facing forward along tipNormal)
                for (int j = 0; j < radialSegments; j++)
                {
                    triangles.Add(tipCenterIndex);
                    triangles.Add(tipRingStart + j);
                    triangles.Add(tipRingStart + j + 1);
                }
            }

            // 6. Optional Side Branch Fork
            bool hasBranchFork = false;
            float branchVolume = 0f;
            if (profile.branchChance > 0f && rng.NextFloat() < profile.branchChance && profile.branchLength > 0.02f)
            {
                hasBranchFork = true;
                int attachRing = Mathf.Clamp(lengthSegments / 2, 1, lengthSegments - 1);
                Vector3 attachCenter = spinePoints[attachRing];
                Vector3 attachTangent = spineTangents[attachRing];
                Vector3 attachRight = spineRights[attachRing];
                Vector3 attachUp = spineUps[attachRing];

                float forkAzimuth = rng.NextAngle();
                Vector3 forkRadial = (attachRight * Mathf.Cos(forkAzimuth) + attachUp * Mathf.Sin(forkAzimuth)).normalized;

                // Branch fork direction
                float forkAngleRad = profile.branchAngleDeg * Mathf.Deg2Rad;
                Vector3 forkDir = (attachTangent * Mathf.Cos(forkAngleRad) + forkRadial * Mathf.Sin(forkAngleRad)).normalized;

                Vector3 branchStart = attachCenter + forkRadial * (ringRadii[attachRing] * 0.8f);
                float bLength = profile.branchLength;
                float bBaseRad = Mathf.Max(0.002f, profile.branchRadius);
                float bTipRad = Mathf.Max(0.001f, bBaseRad * 0.45f);

                int bLengthSegs = 4;
                int bRadialSegs = Mathf.Max(4, radialSegments / 2);
                int bVertsPerRing = bRadialSegs + 1;
                int branchStartVert = vertices.Count;

                Quaternion bRot = Quaternion.FromToRotation(Vector3.forward, forkDir);
                Vector3 bRight = bRot * Vector3.right;
                Vector3 bUp = bRot * Vector3.up;

                for (int bi = 0; bi <= bLengthSegs; bi++)
                {
                    float bt = bi / (float)bLengthSegs;
                    Vector3 bCenter = branchStart + forkDir * (bt * bLength);
                    float bRadius = Mathf.Lerp(bBaseRad, bTipRad, bt);
                    int bRingStartVertIndex = vertices.Count;

                    for (int bj = 0; bj < bRadialSegs; bj++)
                    {
                        float bAngleFrac = bj / (float)bRadialSegs;
                        float bAngle = bAngleFrac * Mathf.PI * 2f;
                        Vector3 bRadDir = bRight * Mathf.Cos(bAngle) + bUp * Mathf.Sin(bAngle);
                        Vector3 bPos = bCenter + bRadDir * bRadius;

                        vertices.Add(bPos);
                        normals.Add(bRadDir.normalized);
                        uvs.Add(new Vector2(bAngleFrac, bt * bLength));

                        float bTone = rng.Range(0f, 1f);
                        colors.Add(Color32.Lerp(profile.barkColorBase, profile.barkColorVariation, bTone));
                    }

                    // Seam duplicate for branch ring
                    vertices.Add(vertices[bRingStartVertIndex]);
                    normals.Add(normals[bRingStartVertIndex]);
                    uvs.Add(new Vector2(1f, bt * bLength));
                    colors.Add(colors[bRingStartVertIndex]);
                }

                // Branch tube triangles (outward-facing winding)
                for (int bi = 0; bi < bLengthSegs; bi++)
                {
                    int bRowA = branchStartVert + bi * bVertsPerRing;
                    int bRowB = branchStartVert + (bi + 1) * bVertsPerRing;

                    for (int bj = 0; bj < bRadialSegs; bj++)
                    {
                        int bv00 = bRowA + bj;
                        int bv01 = bRowA + bj + 1;
                        int bv10 = bRowB + bj;
                        int bv11 = bRowB + bj + 1;

                        triangles.Add(bv00);
                        triangles.Add(bv01);
                        triangles.Add(bv10);

                        triangles.Add(bv01);
                        triangles.Add(bv11);
                        triangles.Add(bv10);
                    }
                }

                // Branch tip cap
                int bTipCenterIndex = vertices.Count;
                Vector3 bTipCenter = branchStart + forkDir * bLength;
                vertices.Add(bTipCenter);
                normals.Add(forkDir);
                uvs.Add(new Vector2(0.5f, 0.5f));
                colors.Add(profile.endGrainCoreColor);

                int bTipTubeRingStart = branchStartVert + bLengthSegs * bVertsPerRing;
                int bTipRingStart = vertices.Count;
                for (int bj = 0; bj <= bRadialSegs; bj++)
                {
                    float bAngleFrac = bj / (float)bRadialSegs;
                    float bAngle = bAngleFrac * Mathf.PI * 2f;
                    Vector3 bPos = vertices[bTipTubeRingStart + bj];

                    vertices.Add(bPos);
                    normals.Add(forkDir);
                    uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(bAngle), 0.5f + 0.5f * Mathf.Sin(bAngle)));
                    colors.Add(profile.endGrainSapColor);
                }

                for (int bj = 0; bj < bRadialSegs; bj++)
                {
                    triangles.Add(bTipCenterIndex);
                    triangles.Add(bTipRingStart + bj);
                    triangles.Add(bTipRingStart + bj + 1);
                }

                // Branch conical frustum volume
                branchVolume = (Mathf.PI / 3f) * bLength * (bBaseRad * bBaseRad + bBaseRad * bTipRad + bTipRad * bTipRad);
            }

            // 7. Calculate Stated Volume & Dry Mass
            // Trunk volume: sum of conical frustums between adjacent rings
            float trunkVolume = 0f;
            for (int i = 0; i < lengthSegments; i++)
            {
                float h = Vector3.Distance(spinePoints[i], spinePoints[i + 1]);
                float r1 = ringRadii[i];
                float r2 = ringRadii[i + 1];
                trunkVolume += (Mathf.PI / 3f) * h * (r1 * r1 + r1 * r2 + r2 * r2);
            }

            float totalVolumeM3 = trunkVolume + branchVolume;

            // 8. Create Unity Mesh
            var mesh = new Mesh();
            mesh.name = $"FallenWood_{profile.profileName}_{seed}";

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            // 9. Assemble Stated Asset Metadata
            metadata = new FallenWoodAssetMetadata(
                profile.profileName,
                seed,
                mesh.bounds,
                totalVolumeM3,
                profile.dryDensityKgM3,
                mesh.vertexCount,
                mesh.triangles.Length / 3,
                hasBranchFork);

            return mesh;
        }

        /// <summary>
        /// Convenience overload returning only the generated mesh.
        /// </summary>
        public static Mesh GenerateMesh(FallenWoodProfile profile, uint seed)
        {
            return GenerateMesh(profile, seed, out _);
        }

        /// <summary>
        /// Generates an authored procedural bark texture in code using Unity APIs.
        /// Standalone texture generation with deterministic noise; no external image assets required.
        /// </summary>
        public static Texture2D GenerateProceduralBarkTexture(uint seed, int width = 128, int height = 128)
        {
            if (width < 2 || height < 2 || width > 4096 || height > 4096 || (long)width * height > 4096 * 4096)
            {
                throw new ArgumentOutOfRangeException($"Invalid texture dimensions ({width}x{height}). Width and height must be within [2, 4096].");
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
            texture.name = $"ProceduralBark_{seed}";
            texture.wrapMode = TextureWrapMode.Repeat;

            var rng = new DeterministicRng(seed);
            float seedOffsetX = rng.Range(0f, 1000f);
            float seedOffsetY = rng.Range(0f, 1000f);

            var pixels = new Color32[width * height];
            Color32 darkBark = new Color32(72, 56, 42, 255);
            Color32 lightBark = new Color32(118, 98, 76, 255);
            Color32 greyLichen = new Color32(138, 126, 106, 255);

            for (int y = 0; y < height; y++)
            {
                float v = (float)y / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (float)x / width;

                    // Anisotropic longitudinal bark striations
                    float n1 = Mathf.PerlinNoise(seedOffsetX + u * 4f, seedOffsetY + v * 32f);
                    float n2 = Mathf.PerlinNoise(seedOffsetX + u * 12f, seedOffsetY + v * 64f) * 0.5f;
                    float combined = Mathf.Clamp01(n1 * 0.7f + n2 * 0.3f);

                    Color32 woodColor = Color32.Lerp(darkBark, lightBark, combined);

                    // Occasional weathered lichen patch
                    float lichenNoise = Mathf.PerlinNoise(seedOffsetX + u * 8f + 50f, seedOffsetY + v * 8f + 50f);
                    if (lichenNoise > 0.68f)
                    {
                        woodColor = Color32.Lerp(woodColor, greyLichen, (lichenNoise - 0.68f) * 3f);
                    }

                    pixels[y * width + x] = woodColor;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Generates an authored procedural end-grain texture in code using Unity APIs.
        /// Generates concentric growth rings and subtle radial rays.
        /// </summary>
        public static Texture2D GenerateProceduralEndGrainTexture(uint seed, int size = 128)
        {
            if (size < 2 || size > 4096 || (long)size * size > 4096 * 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(size), $"Invalid texture size ({size}). Size must be within [2, 4096].");
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.name = $"ProceduralEndGrain_{seed}";
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            Color32 heartwood = new Color32(178, 146, 108, 255);
            Color32 ringDark = new Color32(148, 118, 84, 255);
            Color32 sapwood = new Color32(212, 190, 155, 255);

            float center = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                float dy = (y - center) / center;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > 1f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // Concentric growth rings
                    float ringFreq = dist * 18f;
                    float ringPattern = (Mathf.Sin(ringFreq) + 1f) * 0.5f;

                    // Base gradient from core heartwood to outer sapwood
                    Color32 baseTone = Color32.Lerp(heartwood, sapwood, dist);
                    Color32 finalColor = Color32.Lerp(baseTone, ringDark, ringPattern * 0.28f);

                    pixels[y * size + x] = finalColor;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
