using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.Stones
{
    /// <summary>
    /// Pure deterministic pseudo-random number generator for stone geometry.
    /// Has no dependency on UnityEngine.Random or global static state.
    /// </summary>
    public struct DeterministicPrng
    {
        private uint _state;

        public DeterministicPrng(int seed, int variantIndex = 0)
        {
            uint s = (uint)seed;
            uint v = (uint)variantIndex;
            // Murmur3 32-bit finalizer hash mix to seed internal state
            uint h = s ^ (v * 0x9E3779B9u);
            h ^= h >> 16;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h *= 0xc2b2ae35u;
            h ^= h >> 16;
            _state = h == 0 ? 0x6a09e667u : h;
        }

        public uint NextUInt()
        {
            // Xorshift32 generator
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public float NextFloat()
        {
            return (NextUInt() & 0x00FFFFFF) / 16777216.0f;
        }

        public float Range(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        public Vector3 RandomUnitVector()
        {
            float z = Range(-1.0f, 1.0f);
            float a = Range(0.0f, Mathf.PI * 2.0f);
            float r = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - z * z));
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }
    }

    /// <summary>
    /// Standalone deterministic procedural stone mesh and metadata generator.
    /// Produces bounded, metre-scale, outward-wound original stone meshes with stable bottom-center pivots.
    /// Supports both flat-shaded and smooth-shaded topologies with nondegenerate spatial UVs and MikkTSpace tangents.
    /// Does not alter diagnostic canyon-stone or create shared runtime dependencies.
    /// </summary>
    public static class StoneMeshGenerator
    {
        /// <summary>
        /// Standard lithic density in kg/m³ used as design baseline (dense sandstone/granite range: 2500 - 2700 kg/m³).
        /// </summary>
        public const float StandardLithicDensityKgPerM3 = 2600.0f;

        /// <summary>
        /// Generates a stone mesh and corresponding design metadata for the given configuration.
        /// Rejects invalid configurations and malformed geometry prior to allocating Unity Mesh resources.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if config fails validation.</exception>
        /// <exception cref="InvalidOperationException">Thrown if geometric output invariants are violated.</exception>
        public static StoneGenerationResult Generate(StoneGenerationConfig config)
        {
            // 1. Strict entrypoint configuration validation
            config.Validate();

            DeterministicPrng prng = new DeterministicPrng(config.seed, config.variantIndex);

            // 2. Generate base subdivided icosphere directions and triangles
            List<Vector3> directions = BuildSubdividedIcoDirections(out List<int[]> baseTriangles);

            // 3. Shape-specific deformation parameters
            float formFactor;
            Vector3[] deformedVertices = DeformVertices(config.shape, directions, ref prng, config.uniformScale, out formFactor);

            // 4. Align pivot to ground contact plane (y = 0 at base, centered in XZ)
            Vector3 boundsMin, boundsMax, centerOffset;
            AlignUsablePivot(deformedVertices, out boundsMin, out boundsMax, out centerOffset);

            // 5. Pre-mesh geometry validation (reject invalid geometry before allocating Mesh)
            ValidateDeformedGeometry(deformedVertices, boundsMin, boundsMax);

            // 6. Build Mesh geometry with verified outward winding, spatial UVs, and tangents
            Mesh mesh = BuildMesh(config, deformedVertices, directions, baseTriangles, boundsMin, boundsMax);

            // 7. Compute metadata
            float width = boundsMax.x - boundsMin.x;
            float height = boundsMax.y - boundsMin.y;
            float depth = boundsMax.z - boundsMin.z;
            float boundingVol = width * height * depth;
            float estimatedVol = boundingVol * formFactor;
            float intendedMass = estimatedVol * StandardLithicDensityKgPerM3;

            StoneMetadata metadata = new StoneMetadata
            {
                shape = config.shape,
                seed = config.seed,
                variantIndex = config.variantIndex,
                uniformScale = config.uniformScale,
                flatShaded = config.flatShaded,
                widthMetres = width,
                heightMetres = height,
                depthMetres = depth,
                boundingVolumeM3 = boundingVol,
                formFactor = formFactor,
                estimatedVolumeM3 = estimatedVol,
                assumedDensityKgPerM3 = StandardLithicDensityKgPerM3,
                intendedMassKg = intendedMass,
                vertexCount = mesh.vertexCount,
                triangleCount = mesh.triangles.Length / 3,
                boundsMin = boundsMin,
                boundsMax = boundsMax,
                pivotOffset = centerOffset
            };

            return new StoneGenerationResult(mesh, metadata);
        }

        /// <summary>
        /// Convenience method returning only the generated Mesh.
        /// </summary>
        public static Mesh GenerateMesh(StoneShapeKind shape, int seed = 0, int variantIndex = 0, float uniformScale = 1.0f, bool flatShaded = true)
        {
            var config = new StoneGenerationConfig(shape, seed, variantIndex, uniformScale, flatShaded);
            return Generate(config).mesh;
        }

        /// <summary>
        /// Computes design metadata analytically for a given configuration without creating a Unity Mesh.
        /// Accurately predicts vertex count taking into account flat shading or smooth UV seam splits.
        /// Useful for headless validation and asset estimation.
        /// </summary>
        public static StoneMetadata EstimateMetadata(StoneGenerationConfig config)
        {
            // 1. Strict entrypoint configuration validation
            config.Validate();

            DeterministicPrng prng = new DeterministicPrng(config.seed, config.variantIndex);
            List<Vector3> directions = BuildSubdividedIcoDirections(out List<int[]> baseTriangles);
            float formFactor;
            Vector3[] deformed = DeformVertices(config.shape, directions, ref prng, config.uniformScale, out formFactor);

            Vector3 min, max, centerOffset;
            AlignUsablePivot(deformed, out min, out max, out centerOffset);
            ValidateDeformedGeometry(deformed, min, max);

            float width = max.x - min.x;
            float height = max.y - min.y;
            float depth = max.z - min.z;
            float boundingVol = width * height * depth;
            float estimatedVol = boundingVol * formFactor;
            float intendedMass = estimatedVol * StandardLithicDensityKgPerM3;

            int expectedVertexCount = config.flatShaded
                ? baseTriangles.Count * 3
                : ComputeSmoothVertexCount(directions, baseTriangles);

            return new StoneMetadata
            {
                shape = config.shape,
                seed = config.seed,
                variantIndex = config.variantIndex,
                uniformScale = config.uniformScale,
                flatShaded = config.flatShaded,
                widthMetres = width,
                heightMetres = height,
                depthMetres = depth,
                boundingVolumeM3 = boundingVol,
                formFactor = formFactor,
                estimatedVolumeM3 = estimatedVol,
                assumedDensityKgPerM3 = StandardLithicDensityKgPerM3,
                intendedMassKg = intendedMass,
                vertexCount = expectedVertexCount,
                triangleCount = baseTriangles.Count,
                boundsMin = min,
                boundsMax = max,
                pivotOffset = centerOffset
            };
        }

        // =========================================================================================
        // Internal Procedural Geometry Helpers
        // =========================================================================================

        private static List<Vector3> BuildSubdividedIcoDirections(out List<int[]> triangles)
        {
            float t = (1.0f + Mathf.Sqrt(5.0f)) * 0.5f;

            var baseVerts = new Vector3[]
            {
                new Vector3(-1f,  t,  0f).normalized,
                new Vector3( 1f,  t,  0f).normalized,
                new Vector3(-1f, -t,  0f).normalized,
                new Vector3( 1f, -t,  0f).normalized,
                new Vector3( 0f, -1f,  t).normalized,
                new Vector3( 0f,  1f,  t).normalized,
                new Vector3( 0f, -1f, -t).normalized,
                new Vector3( 0f,  1f, -t).normalized,
                new Vector3( t,  0f, -1f).normalized,
                new Vector3( t,  0f,  1f).normalized,
                new Vector3(-t,  0f, -1f).normalized,
                new Vector3(-t,  0f,  1f).normalized
            };

            var initialTriangles = new int[][]
            {
                new int[] { 0, 11, 5 }, new int[] { 0, 5, 1 },   new int[] { 0, 1, 7 },   new int[] { 0, 7, 10 },  new int[] { 0, 10, 11 },
                new int[] { 1, 5, 9 },  new int[] { 5, 11, 4 },  new int[] { 11, 10, 2 }, new int[] { 10, 7, 6 }, new int[] { 7, 1, 8 },
                new int[] { 3, 9, 4 },  new int[] { 3, 4, 2 },   new int[] { 3, 2, 6 },   new int[] { 3, 6, 8 },   new int[] { 3, 8, 9 },
                new int[] { 4, 9, 5 },  new int[] { 2, 4, 11 },  new int[] { 6, 2, 10 },  new int[] { 8, 6, 7 },   new int[] { 9, 8, 1 }
            };

            var verts = new List<Vector3>(baseVerts);
            var currentTriangles = new List<int[]>(initialTriangles);

            // Subdivide twice: 20 -> 80 -> 320 triangles
            var midCache = new Dictionary<long, int>();
            for (int sub = 0; sub < 2; sub++)
            {
                midCache.Clear();
                var nextTriangles = new List<int[]>(currentTriangles.Count * 4);

                foreach (var tri in currentTriangles)
                {
                    int a = tri[0];
                    int b = tri[1];
                    int c = tri[2];

                    int ab = GetMidpointIndex(verts, midCache, a, b);
                    int bc = GetMidpointIndex(verts, midCache, b, c);
                    int ca = GetMidpointIndex(verts, midCache, c, a);

                    nextTriangles.Add(new int[] { a, ab, ca });
                    nextTriangles.Add(new int[] { b, bc, ab });
                    nextTriangles.Add(new int[] { c, ca, bc });
                    nextTriangles.Add(new int[] { ab, bc, ca });
                }

                currentTriangles = nextTriangles;
            }

            triangles = currentTriangles;
            return verts;
        }

        private static int GetMidpointIndex(List<Vector3> verts, Dictionary<long, int> cache, int i1, int i2)
        {
            int smaller = i1 < i2 ? i1 : i2;
            int greater = i1 < i2 ? i2 : i1;
            long key = ((long)smaller << 32) | (uint)greater;

            if (cache.TryGetValue(key, out int index))
            {
                return index;
            }

            Vector3 mid = ((verts[i1] + verts[i2]) * 0.5f).normalized;
            index = verts.Count;
            verts.Add(mid);
            cache[key] = index;
            return index;
        }

        private static Vector3[] DeformVertices(
            StoneShapeKind shape,
            List<Vector3> directions,
            ref DeterministicPrng prng,
            float scale,
            out float formFactor)
        {
            Vector3[] result = new Vector3[directions.Count];
            float phaseX = prng.Range(0f, Mathf.PI * 2f);
            float phaseY = prng.Range(0f, Mathf.PI * 2f);
            float phaseZ = prng.Range(0f, Mathf.PI * 2f);

            switch (shape)
            {
                case StoneShapeKind.RiverCobble:
                {
                    // Water-tumbled, rounded, slightly flattened pebble/cobble (~0.18m x 0.12m x 0.15m)
                    float rx = 0.090f * prng.Range(0.92f, 1.08f) * scale;
                    float ry = 0.060f * prng.Range(0.92f, 1.08f) * scale;
                    float rz = 0.075f * prng.Range(0.92f, 1.08f) * scale;
                    formFactor = 0.523f; // Ellipsoidal volume ratio: (4/3*pi*a*b*c) / (8*a*b*c) = pi/6 ≈ 0.5236

                    for (int i = 0; i < directions.Count; i++)
                    {
                        Vector3 u = directions[i];
                        float h1 = Mathf.Sin(u.x * 2.1f + phaseX) * Mathf.Cos(u.y * 2.3f + phaseY) * 0.055f;
                        float h2 = Mathf.Sin(u.z * 2.7f + phaseZ) * 0.040f;
                        float r = 1.0f + h1 + h2;
                        result[i] = new Vector3(u.x * rx * r, u.y * ry * r, u.z * rz * r);
                    }
                    break;
                }

                case StoneShapeKind.Fieldstone:
                {
                    // Sharp, angular, faceted quarry stone (~0.22m x 0.18m x 0.20m)
                    float rx = 0.110f * prng.Range(0.92f, 1.08f) * scale;
                    float ry = 0.090f * prng.Range(0.92f, 1.08f) * scale;
                    float rz = 0.100f * prng.Range(0.92f, 1.08f) * scale;
                    formFactor = 0.580f;

                    // Authored planar cleavage facets
                    Vector3[] cleavageNormals = new Vector3[4];
                    float[] cleavageThresholds = new float[4];
                    for (int k = 0; k < 4; k++)
                    {
                        cleavageNormals[k] = prng.RandomUnitVector();
                        cleavageThresholds[k] = prng.Range(0.68f, 0.82f);
                    }

                    for (int i = 0; i < directions.Count; i++)
                    {
                        Vector3 u = directions[i];
                        float ShapePow(float v) => Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), 0.78f);
                        Vector3 p = new Vector3(ShapePow(u.x) * rx, ShapePow(u.y) * ry, ShapePow(u.z) * rz);

                        float avgR = (rx + ry + rz) * 0.333f;
                        for (int k = 0; k < 4; k++)
                        {
                            float proj = Vector3.Dot(p, cleavageNormals[k]);
                            float limit = cleavageThresholds[k] * avgR;
                            if (proj > limit)
                            {
                                p -= cleavageNormals[k] * ((proj - limit) * 0.82f);
                            }
                        }

                        result[i] = p;
                    }
                    break;
                }

                case StoneShapeKind.FlatSlab:
                {
                    // Broad, tabular slab / paver (~0.28m x 0.07m x 0.24m)
                    float rx = 0.140f * prng.Range(0.92f, 1.08f) * scale;
                    float ry = 0.035f * prng.Range(0.92f, 1.08f) * scale;
                    float rz = 0.120f * prng.Range(0.92f, 1.08f) * scale;
                    formFactor = 0.680f;

                    float maxH = ry * 0.82f;
                    for (int i = 0; i < directions.Count; i++)
                    {
                        Vector3 u = directions[i];
                        float angle = Mathf.Atan2(u.z, u.x);
                        float edgeNoise = Mathf.Sin(angle * 5.0f + phaseX) * 0.075f + Mathf.Cos(angle * 7.0f + phaseZ) * 0.045f;
                        float px = u.x * rx * (1.0f + edgeNoise);
                        float pz = u.z * rz * (1.0f + edgeNoise);

                        float py = u.y * ry;
                        if (py > maxH) py = maxH + (py - maxH) * 0.18f;
                        else if (py < -maxH) py = -maxH + (py + maxH) * 0.18f;

                        result[i] = new Vector3(px, py, pz);
                    }
                    break;
                }

                case StoneShapeKind.Handstone:
                {
                    // Asymmetric, tapered teardrop wedge tool stone (~0.16m x 0.09m x 0.11m)
                    float rx = 0.080f * prng.Range(0.92f, 1.08f) * scale;
                    float ry = 0.045f * prng.Range(0.92f, 1.08f) * scale;
                    float rz = 0.055f * prng.Range(0.92f, 1.08f) * scale;
                    formFactor = 0.460f;

                    for (int i = 0; i < directions.Count; i++)
                    {
                        Vector3 u = directions[i];
                        // Taper along X: thick at u.x = -1, narrower wedge at u.x = +1
                        float tNorm = Mathf.Clamp01((u.x + 1.0f) * 0.5f);
                        float taper = Mathf.Lerp(1.22f, 0.62f, tNorm);

                        float px = u.x * rx;
                        float py = u.y * ry * taper;
                        float pz = u.z * rz * taper;

                        // Subtle ergonomic grip indentation near center-top
                        float dx = u.x + 0.15f;
                        float dy = u.y - 0.30f;
                        float gripIndent = Mathf.Exp(-(dx * dx + dy * dy) * 6.0f) * (0.010f * scale);
                        py -= gripIndent;

                        result[i] = new Vector3(px, py, pz);
                    }
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), shape, $"Unsupported shape kind: {shape}");
            }

            return result;
        }

        private static void AlignUsablePivot(
            Vector3[] vertices,
            out Vector3 boundsMin,
            out Vector3 boundsMax,
            out Vector3 centerOffset)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }

            // Usable resting pivot: ground contact plane at y = 0, centered in X and Z
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            Vector3 shift = new Vector3(-centerX, -minY, -centerZ);

            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] += shift;
            }

            boundsMin = new Vector3(minX - centerX, 0.0f, minZ - centerZ);
            boundsMax = new Vector3(maxX - centerX, maxY - minY, maxZ - centerZ);
            centerOffset = (boundsMin + boundsMax) * 0.5f;
        }

        private static void ValidateDeformedGeometry(Vector3[] vertices, Vector3 boundsMin, Vector3 boundsMax)
        {
            if (float.IsNaN(boundsMin.x) || float.IsInfinity(boundsMin.x) ||
                float.IsNaN(boundsMax.x) || float.IsInfinity(boundsMax.x))
            {
                throw new InvalidOperationException("Deformed stone geometry produced non-finite bounds.");
            }

            float width = boundsMax.x - boundsMin.x;
            float height = boundsMax.y - boundsMin.y;
            float depth = boundsMax.z - boundsMin.z;

            if (width <= 0.001f || height <= 0.001f || depth <= 0.001f ||
                width > 20.0f || height > 20.0f || depth > 20.0f)
            {
                throw new InvalidOperationException($"Deformed stone dimensions ({width:F3}m x {height:F3}m x {depth:F3}m) outside supported [0.001, 20.0]m item envelope.");
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                if (float.IsNaN(v.x) || float.IsInfinity(v.x) ||
                    float.IsNaN(v.y) || float.IsInfinity(v.y) ||
                    float.IsNaN(v.z) || float.IsInfinity(v.z))
                {
                    throw new InvalidOperationException("Deformed stone vertex contained NaN or Infinity.");
                }
            }
        }

        // =========================================================================================
        // Spatial UV Unwrapping & Seam Invariant Helpers
        // =========================================================================================

        private static Vector2 ComputeRawUv(Vector3 dir)
        {
            // Spatial spherical unwrap from normalized direction:
            // Longitude theta in [-pi, pi] mapped to u in [0, 1]
            float u = Mathf.Atan2(dir.z, dir.x) / (Mathf.PI * 2.0f) + 0.5f;
            // Latitude phi in [-pi/2, pi/2] mapped to v in [0, 1]
            float v = Mathf.Asin(Mathf.Clamp(dir.y, -1.0f, 1.0f)) / Mathf.PI + 0.5f;
            return new Vector2(u, v);
        }

        private static bool TriangleCrossesSeam(Vector2 uv0, Vector2 uv1, Vector2 uv2)
        {
            float maxU = Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x));
            float minU = Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x));
            return (maxU - minU) > 0.5f;
        }

        private static int ComputeSmoothVertexCount(List<Vector3> directions, List<int[]> triangles)
        {
            // Each unique 3D position has a base vertex (shift = 0).
            // When referenced in a seam-crossing triangle with u < 0.5, it requires a seam-split vertex (shift = 1).
            bool[] splitPositions = new bool[directions.Count];

            for (int t = 0; t < triangles.Count; t++)
            {
                int i0 = triangles[t][0];
                int i1 = triangles[t][1];
                int i2 = triangles[t][2];

                Vector2 uv0 = ComputeRawUv(directions[i0]);
                Vector2 uv1 = ComputeRawUv(directions[i1]);
                Vector2 uv2 = ComputeRawUv(directions[i2]);

                if (TriangleCrossesSeam(uv0, uv1, uv2))
                {
                    if (uv0.x < 0.5f) splitPositions[i0] = true;
                    if (uv1.x < 0.5f) splitPositions[i1] = true;
                    if (uv2.x < 0.5f) splitPositions[i2] = true;
                }
            }

            int count = directions.Count;
            for (int i = 0; i < splitPositions.Length; i++)
            {
                if (splitPositions[i]) count++;
            }
            return count;
        }

        private static Vector3[] ComputeSmoothNormals(Vector3[] deformedVertices, List<int[]> triangles)
        {
            Vector3[] smoothNormals = new Vector3[deformedVertices.Length];
            for (int t = 0; t < triangles.Count; t++)
            {
                int i0 = triangles[t][0];
                int i1 = triangles[t][1];
                int i2 = triangles[t][2];

                Vector3 a = deformedVertices[i0];
                Vector3 b = deformedVertices[i1];
                Vector3 c = deformedVertices[i2];

                Vector3 fn = Vector3.Cross(b - a, c - a);
                smoothNormals[i0] += fn;
                smoothNormals[i1] += fn;
                smoothNormals[i2] += fn;
            }

            for (int i = 0; i < smoothNormals.Length; i++)
            {
                Vector3 n = smoothNormals[i];
                smoothNormals[i] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            }

            return smoothNormals;
        }

        private static Mesh BuildMesh(
            StoneGenerationConfig config,
            Vector3[] vertices,
            List<Vector3> directions,
            List<int[]> triangles,
            Vector3 boundsMin,
            Vector3 boundsMax)
        {
            Vector3 centroid = (boundsMin + boundsMax) * 0.5f;

            // 1. Ensure coherent outward winding and record face normals
            var orientedTriangles = new List<int[]>(triangles.Count);
            var faceNormals = new Vector3[triangles.Count];

            for (int t = 0; t < triangles.Count; t++)
            {
                int i0 = triangles[t][0];
                int i1 = triangles[t][1];
                int i2 = triangles[t][2];

                Vector3 a = vertices[i0];
                Vector3 b = vertices[i1];
                Vector3 c = vertices[i2];

                Vector3 fn = Vector3.Cross(b - a, c - a);
                if (fn.sqrMagnitude < 1e-12f)
                {
                    throw new InvalidOperationException($"Triangle {t} in stone generation has degenerate zero area.");
                }

                Vector3 faceCenter = (a + b + c) * 0.33333333f;
                Vector3 outwardDir = faceCenter - centroid;

                if (Vector3.Dot(fn, outwardDir) < 0.0f)
                {
                    // Invert triangle winding to point outward
                    orientedTriangles.Add(new int[] { i0, i2, i1 });
                    faceNormals[t] = -fn.normalized;
                }
                else
                {
                    orientedTriangles.Add(new int[] { i0, i1, i2 });
                    faceNormals[t] = fn.normalized;
                }
            }

            // 2. Authored palette tint per shape
            Color baseColor;
            Color highlightColor;
            switch (config.shape)
            {
                case StoneShapeKind.RiverCobble:
                    baseColor = new Color(0.56f, 0.52f, 0.46f, 1f);       // Tan/buff river siltstone
                    highlightColor = new Color(0.64f, 0.60f, 0.54f, 1f);
                    break;
                case StoneShapeKind.Fieldstone:
                    baseColor = new Color(0.46f, 0.36f, 0.28f, 1f);       // Iron-brown fractured sandstone
                    highlightColor = new Color(0.55f, 0.44f, 0.35f, 1f);
                    break;
                case StoneShapeKind.FlatSlab:
                    baseColor = new Color(0.38f, 0.40f, 0.42f, 1f);       // Layered slate blue-grey
                    highlightColor = new Color(0.46f, 0.48f, 0.50f, 1f);
                    break;
                case StoneShapeKind.Handstone:
                default:
                    baseColor = new Color(0.32f, 0.33f, 0.35f, 1f);       // Dark charcoal quartzite
                    highlightColor = new Color(0.42f, 0.43f, 0.44f, 1f);
                    break;
            }

            // 3. Assemble buffers according to shading mode
            List<Vector3> finalVerts;
            List<Vector3> finalNormals;
            List<Vector2> finalUvs;
            List<Color> finalColors;
            List<int> finalIndices;

            if (config.flatShaded)
            {
                // Flat shaded: 3 independent vertices per triangle with crisp face normals
                finalVerts = new List<Vector3>(triangles.Count * 3);
                finalNormals = new List<Vector3>(triangles.Count * 3);
                finalUvs = new List<Vector2>(triangles.Count * 3);
                finalColors = new List<Color>(triangles.Count * 3);
                finalIndices = new List<int>(triangles.Count * 3);

                int indexCounter = 0;
                for (int t = 0; t < orientedTriangles.Count; t++)
                {
                    int[] tri = orientedTriangles[t];
                    Vector3 a = vertices[tri[0]];
                    Vector3 b = vertices[tri[1]];
                    Vector3 c = vertices[tri[2]];

                    Vector3 fn = faceNormals[t];

                    Vector2 uv0 = ComputeRawUv(directions[tri[0]]);
                    Vector2 uv1 = ComputeRawUv(directions[tri[1]]);
                    Vector2 uv2 = ComputeRawUv(directions[tri[2]]);

                    if (TriangleCrossesSeam(uv0, uv1, uv2))
                    {
                        if (uv0.x < 0.5f) uv0.x += 1.0f;
                        if (uv1.x < 0.5f) uv1.x += 1.0f;
                        if (uv2.x < 0.5f) uv2.x += 1.0f;
                    }

                    float shade = Mathf.Clamp01(0.70f + 0.30f * Vector3.Dot(fn, Vector3.up));
                    Color col = Color.Lerp(baseColor, highlightColor, shade);

                    finalVerts.Add(a); finalVerts.Add(b); finalVerts.Add(c);
                    finalNormals.Add(fn); finalNormals.Add(fn); finalNormals.Add(fn);
                    finalUvs.Add(uv0); finalUvs.Add(uv1); finalUvs.Add(uv2);
                    finalColors.Add(col); finalColors.Add(col); finalColors.Add(col);

                    finalIndices.Add(indexCounter++);
                    finalIndices.Add(indexCounter++);
                    finalIndices.Add(indexCounter++);
                }
            }
            else
            {
                // Smooth shaded: shared positions with area-weighted smooth normals,
                // splitting vertices only along the UV texture seam.
                Vector3[] smoothNormals = ComputeSmoothNormals(vertices, orientedTriangles);
                int expectedCount = ComputeSmoothVertexCount(directions, orientedTriangles);

                finalVerts = new List<Vector3>(expectedCount);
                finalNormals = new List<Vector3>(expectedCount);
                finalUvs = new List<Vector2>(expectedCount);
                finalColors = new List<Color>(expectedCount);
                finalIndices = new List<int>(triangles.Count * 3);

                int[] baseIndices = new int[directions.Count];
                int[] splitIndices = new int[directions.Count];
                for (int i = 0; i < directions.Count; i++)
                {
                    baseIndices[i] = -1;
                    splitIndices[i] = -1;
                }

                for (int t = 0; t < orientedTriangles.Count; t++)
                {
                    int[] tri = orientedTriangles[t];
                    Vector2 uv0 = ComputeRawUv(directions[tri[0]]);
                    Vector2 uv1 = ComputeRawUv(directions[tri[1]]);
                    Vector2 uv2 = ComputeRawUv(directions[tri[2]]);

                    bool crosses = TriangleCrossesSeam(uv0, uv1, uv2);

                    for (int corner = 0; corner < 3; corner++)
                    {
                        int p = tri[corner];
                        Vector2 rawUv = ComputeRawUv(directions[p]);
                        bool isShifted = crosses && (rawUv.x < 0.5f);

                        int assignedIndex;
                        if (!isShifted)
                        {
                            if (baseIndices[p] == -1)
                            {
                                assignedIndex = finalVerts.Count;
                                baseIndices[p] = assignedIndex;
                                finalVerts.Add(vertices[p]);
                                finalNormals.Add(smoothNormals[p]);
                                finalUvs.Add(rawUv);
                                float shade = Mathf.Clamp01(0.70f + 0.30f * Vector3.Dot(smoothNormals[p], Vector3.up));
                                finalColors.Add(Color.Lerp(baseColor, highlightColor, shade));
                            }
                            else
                            {
                                assignedIndex = baseIndices[p];
                            }
                        }
                        else
                        {
                            if (splitIndices[p] == -1)
                            {
                                assignedIndex = finalVerts.Count;
                                splitIndices[p] = assignedIndex;
                                finalVerts.Add(vertices[p]);
                                // Smooth normals maintain shared-position smoothing even across the seam split
                                finalNormals.Add(smoothNormals[p]);
                                finalUvs.Add(new Vector2(rawUv.x + 1.0f, rawUv.y));
                                float shade = Mathf.Clamp01(0.70f + 0.30f * Vector3.Dot(smoothNormals[p], Vector3.up));
                                finalColors.Add(Color.Lerp(baseColor, highlightColor, shade));
                            }
                            else
                            {
                                assignedIndex = splitIndices[p];
                            }
                        }

                        finalIndices.Add(assignedIndex);
                    }
                }
            }

            // 4. Create and populate Unity Mesh
            Mesh mesh = new Mesh
            {
                name = $"Stone_{config.shape}_s{config.seed}_v{config.variantIndex}_{(config.flatShaded ? "flat" : "smooth")}"
            };

            mesh.SetVertices(finalVerts);
            mesh.SetNormals(finalNormals);
            mesh.SetUVs(0, finalUvs);
            mesh.SetColors(finalColors);
            mesh.SetTriangles(finalIndices, 0);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            // 5. Final output integrity validation
            ValidateGeneratedMesh(mesh, finalUvs, finalIndices);

            return mesh;
        }

        private static void ValidateGeneratedMesh(Mesh mesh, List<Vector2> uvs, List<int> indices)
        {
            Vector4[] tangents = mesh.tangents;
            if (tangents == null || tangents.Length != mesh.vertexCount)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw new InvalidOperationException("Failed to calculate tangents on generated stone mesh.");
            }

            Vector3[] normals = mesh.normals;
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                Vector4 tan = tangents[i];
                Vector3 n = normals[i];

                if (float.IsNaN(tan.x) || float.IsInfinity(tan.x) ||
                    float.IsNaN(tan.y) || float.IsInfinity(tan.y) ||
                    float.IsNaN(tan.z) || float.IsInfinity(tan.z) ||
                    float.IsNaN(tan.w) || float.IsInfinity(tan.w) ||
                    float.IsNaN(n.x) || float.IsInfinity(n.x) ||
                    float.IsNaN(n.y) || float.IsInfinity(n.y) ||
                    float.IsNaN(n.z) || float.IsInfinity(n.z))
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                    throw new InvalidOperationException("Generated stone mesh contained NaN or infinite normal/tangent values.");
                }
            }

            // Validate non-degenerate UV triangles
            for (int t = 0; t < indices.Count; t += 3)
            {
                Vector2 uv0 = uvs[indices[t]];
                Vector2 uv1 = uvs[indices[t + 1]];
                Vector2 uv2 = uvs[indices[t + 2]];

                float det = (uv1.x - uv0.x) * (uv2.y - uv0.y) - (uv2.x - uv0.x) * (uv1.y - uv0.y);
                if (Mathf.Abs(det) < 1e-8f)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                    throw new InvalidOperationException($"Triangle {t / 3} produced degenerate UV determinant {det}.");
                }
            }
        }
    }
}
