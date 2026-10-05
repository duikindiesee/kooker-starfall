using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.Fire
{
    /// <summary>
    /// Parameters defining the deterministic procedural generation of a small dry brush / tinder bundle.
    /// Uses metre dimensions and pure integer PRNG.
    /// </summary>
    [Serializable]
    public struct TinderParameters
    {
        public int Seed;
        public int PrimaryTwigCount;
        public int SecondaryTwigCount;
        public int FineFiberCount;
        public float BundleRadiusMetres;
        public float BundleHeightMetres;
        public float PrimaryTwigRadiusMetres;
        public float SecondaryTwigRadiusMetres;
        public float FiberWidthMetres;
        public int LodLevel;

        public static TinderParameters Default => new TinderParameters
        {
            Seed = 4217,
            PrimaryTwigCount = 10,
            SecondaryTwigCount = 16,
            FineFiberCount = 24,
            BundleRadiusMetres = 0.15f,
            BundleHeightMetres = 0.14f,
            PrimaryTwigRadiusMetres = 0.0045f,
            SecondaryTwigRadiusMetres = 0.0022f,
            FiberWidthMetres = 0.0018f,
            LodLevel = 0
        };

        public static TinderParameters ForLod(int lod, int seed = 4217)
        {
            var p = Default;
            p.Seed = seed;
            p.LodLevel = Mathf.Clamp(lod, 0, 2);
            if (p.LodLevel == 1)
            {
                p.PrimaryTwigCount = 7;
                p.SecondaryTwigCount = 10;
                p.FineFiberCount = 12;
            }
            else if (p.LodLevel == 2)
            {
                p.PrimaryTwigCount = 5;
                p.SecondaryTwigCount = 6;
                p.FineFiberCount = 0;
            }
            return p;
        }
    }

    /// <summary>
    /// Self-contained, deterministic procedural mesh generator for a small dry brush / tinder bundle visual.
    /// Built using public Unity mesh/material APIs without gameplay or project dependencies.
    /// Strictly complies with determinism rules: no clock time, no frame count, no UnityEngine.Random.
    /// Local pivot is aligned to base centre (ground contact at Y=0).
    ///
    /// AUTHORING & DETERMINISM STATUS:
    /// - Authoring status: AUTHOR-COMPLETED SOURCE-ONLY.
    /// - Compilation and rendering are unverified in this authoring pass.
    /// - PRNG sequence uses pure integer 32-bit Xorshift arithmetic.
    /// - Geometry mathematics use standard IEEE 754 single-precision floating point (trigonometry and vector normalization).
    ///   While deterministic within the same platform/architecture/runtime, cross-platform bit-identical float coordinates
    ///   are not claimed without standardized fixed-point math.
    /// </summary>
    public static class TinderGeometry
    {
        public const string Version = "citylife.fire.tinder.dry-brush.v1-source";
        public const string AuthorStatus = "AUTHOR-COMPLETED SOURCE-ONLY";
        public const string VerificationStatus = "COMPILATION_AND_RENDERING_UNVERIFIED";

        /// <summary>
        /// Pure integer deterministic random number generator (Xorshift32).
        /// Guarantees bit-identical sequence across platforms independent of UnityEngine.Random or system time.
        /// </summary>
        private struct SeededRng
        {
            private uint _state;

            public SeededRng(int seed)
            {
                _state = unchecked((uint)seed) ^ 0x9E3779B9u;
                if (_state == 0) _state = 0x85EBCA6Bu;
            }

            public uint NextUInt()
            {
                unchecked
                {
                    _state ^= _state << 13;
                    _state ^= _state >> 17;
                    _state ^= _state << 5;
                    return _state;
                }
            }

            public float NextFloat01()
            {
                return (NextUInt() & 0x00FFFFFFu) / 16777215.0f;
            }

            public float Range(float min, float max)
            {
                return min + NextFloat01() * (max - min);
            }
        }

        /// <summary>
        /// Internal mesh buffer holding pre-allocated vertex attributes.
        /// </summary>
        private sealed class MeshBuffer
        {
            public readonly List<Vector3> Vertices;
            public readonly List<Vector3> Normals;
            public readonly List<Vector2> UVs;
            public readonly List<Color> Colors;
            public readonly List<int> Triangles;

            public MeshBuffer(int vertexCapacity, int triangleCapacity)
            {
                Vertices = new List<Vector3>(vertexCapacity);
                Normals = new List<Vector3>(vertexCapacity);
                UVs = new List<Vector2>(vertexCapacity);
                Colors = new List<Color>(vertexCapacity);
                Triangles = new List<int>(triangleCapacity * 3);
            }

            public Mesh BuildMesh(string meshName)
            {
                var mesh = new Mesh
                {
                    name = meshName,
                    indexFormat = Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, UVs);
                mesh.SetColors(Colors);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        /// <summary>
        /// Validates generator parameters before any allocations are made.
        /// Rejects non-finite, negative, out-of-bound, or excessive parameters without partial object creation.
        /// </summary>
        public static void ValidateParameters(TinderParameters parameters)
        {
            if (parameters.LodLevel < 0 || parameters.LodLevel > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters.LodLevel),
                    $"LodLevel must be 0, 1, or 2, received {parameters.LodLevel}.");
            }

            if (parameters.PrimaryTwigCount < 0 || parameters.PrimaryTwigCount > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters.PrimaryTwigCount),
                    $"PrimaryTwigCount must be between 0 and 100, received {parameters.PrimaryTwigCount}.");
            }

            if (parameters.SecondaryTwigCount < 0 || parameters.SecondaryTwigCount > 200)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters.SecondaryTwigCount),
                    $"SecondaryTwigCount must be between 0 and 200, received {parameters.SecondaryTwigCount}.");
            }

            if (parameters.FineFiberCount < 0 || parameters.FineFiberCount > 500)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters.FineFiberCount),
                    $"FineFiberCount must be between 0 and 500, received {parameters.FineFiberCount}.");
            }

            if (parameters.PrimaryTwigCount == 0 && parameters.SecondaryTwigCount == 0 && parameters.FineFiberCount == 0)
            {
                throw new ArgumentException("At least one twig or fiber count must be greater than zero.");
            }

            if (!IsFinite(parameters.BundleRadiusMetres) || parameters.BundleRadiusMetres <= 0.001f || parameters.BundleRadiusMetres > 5.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters.BundleRadiusMetres),
                    $"BundleRadiusMetres must be finite and in range (0.001, 5.0], received {parameters.BundleRadiusMetres}.");
            }

            if (!IsFinite(parameters.BundleHeightMetres) || parameters.BundleHeightMetres <= 0.001f || parameters.BundleHeightMetres > 5.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters.BundleHeightMetres),
                    $"BundleHeightMetres must be finite and in range (0.001, 5.0], received {parameters.BundleHeightMetres}.");
            }

            if (!IsFinite(parameters.PrimaryTwigRadiusMetres) || parameters.PrimaryTwigRadiusMetres <= 0.0001f || parameters.PrimaryTwigRadiusMetres > parameters.BundleRadiusMetres)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters.PrimaryTwigRadiusMetres),
                    $"PrimaryTwigRadiusMetres must be finite, positive, and <= BundleRadiusMetres, received {parameters.PrimaryTwigRadiusMetres}.");
            }

            if (!IsFinite(parameters.SecondaryTwigRadiusMetres) || parameters.SecondaryTwigRadiusMetres <= 0.0001f || parameters.SecondaryTwigRadiusMetres > parameters.BundleRadiusMetres)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters.SecondaryTwigRadiusMetres),
                    $"SecondaryTwigRadiusMetres must be finite, positive, and <= BundleRadiusMetres, received {parameters.SecondaryTwigRadiusMetres}.");
            }

            if (!IsFinite(parameters.FiberWidthMetres) || parameters.FiberWidthMetres <= 0.0001f || parameters.FiberWidthMetres > parameters.BundleRadiusMetres)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters.FiberWidthMetres),
                    $"FiberWidthMetres must be finite, positive, and <= BundleRadiusMetres, received {parameters.FiberWidthMetres}.");
            }

            // Verify total geometry workload is bounded
            CalculateWorkload(parameters, out int expectedVertices, out _);
            if (expectedVertices > 65534)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters),
                    $"Total geometry workload ({expectedVertices} vertices) exceeds standard 16-bit mesh allocation limit (65534).");
            }
        }

        /// <summary>
        /// Retrieves the exact segment and polygonal side configuration for a given LOD level.
        /// Primary twigs: LOD0 (5 sides, 6 segments), LOD1 (4 sides, 4 segments), LOD2 (4 sides, 4 segments).
        /// Secondary twigs: 4 sides; LOD0 (5 segments), LOD1 (3 segments), LOD2 (3 segments).
        /// Fine fibers: 4 segments (when present).
        /// </summary>
        public static void GetLodSegmentConfiguration(int lodLevel,
            out int primarySides, out int primarySegments,
            out int secondarySides, out int secondarySegments,
            out int fiberSegments)
        {
            primarySides = lodLevel == 0 ? 5 : 4;
            primarySegments = lodLevel == 0 ? 6 : 4;
            secondarySides = 4;
            secondarySegments = lodLevel == 0 ? 5 : 3;
            fiberSegments = 4; // Always 4 segments when fibers are present
        }

        /// <summary>
        /// Pure analytical calculation of expected vertex and triangle counts for given parameters.
        /// Computed before any allocations:
        /// Primary twig: ((segments + 1) * sides + 2) vertices, 2 * (segments + 1) * sides triangles.
        /// Secondary twig: ((segments + 1) * sides + 2) vertices, 2 * (segments + 1) * sides triangles.
        /// Fine fiber: ((segments + 1) * 2) vertices, segments * 4 triangles.
        /// </summary>
        public static void CalculateWorkload(TinderParameters parameters, out int vertexCount, out int triangleCount)
        {
            GetLodSegmentConfiguration(parameters.LodLevel,
                out int primarySides, out int primarySegments,
                out int secondarySides, out int secondarySegments,
                out int fiberSegments);

            int primaryVertsPerTwig = (primarySegments + 1) * primarySides + 2;
            int primaryTrisPerTwig = 2 * (primarySegments + 1) * primarySides;

            int secondaryVertsPerTwig = (secondarySegments + 1) * secondarySides + 2;
            int secondaryTrisPerTwig = 2 * (secondarySegments + 1) * secondarySides;

            int fiberVertsPerRibbon = (fiberSegments + 1) * 2;
            int fiberTrisPerRibbon = fiberSegments * 4;

            vertexCount = parameters.PrimaryTwigCount * primaryVertsPerTwig
                        + parameters.SecondaryTwigCount * secondaryVertsPerTwig
                        + (parameters.FineFiberCount > 0 ? parameters.FineFiberCount * fiberVertsPerRibbon : 0);

            triangleCount = parameters.PrimaryTwigCount * primaryTrisPerTwig
                          + parameters.SecondaryTwigCount * secondaryTrisPerTwig
                          + (parameters.FineFiberCount > 0 ? parameters.FineFiberCount * fiberTrisPerRibbon : 0);
        }

        private static void ClampRadialXZ(ref Vector3 p, float maxRadius)
        {
            float rSq = p.x * p.x + p.z * p.z;
            if (rSq > maxRadius * maxRadius && rSq > 1e-8f)
            {
                float scale = maxRadius / Mathf.Sqrt(rSq);
                p.x *= scale;
                p.z *= scale;
            }
        }

        /// <summary>
        /// Generates a standalone Unity Mesh representing a small dry brush / tinder bundle.
        /// Validates parameters, pre-allocates buffers, and strictly adheres to determinism.
        /// </summary>
        public static Mesh GenerateMesh(TinderParameters parameters)
        {
            // 0. Validate parameters before any allocations; fails immediately on invalid input
            ValidateParameters(parameters);

            // Calculate exact workload and pre-allocate capacity
            CalculateWorkload(parameters, out int expectedVertices, out int expectedTriangles);
            var buffer = new MeshBuffer(expectedVertices, expectedTriangles);
            var rng = new SeededRng(parameters.Seed);

            // Color palette for dry arid brush and kindling
            Color baseWoodDark = new Color(0.42f, 0.34f, 0.24f, 1f);      // Inner weathered bark
            Color baseWoodLight = new Color(0.62f, 0.54f, 0.42f, 1f);     // Sun-bleached dry wood
            Color dryStrawLight = new Color(0.78f, 0.70f, 0.46f, 1f);     // Fine dry kindling / thatch
            Color dryStrawDark = new Color(0.65f, 0.55f, 0.35f, 1f);      // Aged straw wisp

            GetLodSegmentConfiguration(parameters.LodLevel,
                out int primarySides, out int primarySegments,
                out int secondarySides, out int secondarySegments,
                out int fiberSegments);

            // 1. Primary structural twigs (curved interlocking branches forming cradle)
            float maxPrimaryRadius = Mathf.Max(0.001f, parameters.BundleRadiusMetres - parameters.PrimaryTwigRadiusMetres);
            for (int i = 0; i < parameters.PrimaryTwigCount; i++)
            {
                float angleStart = (i / (float)parameters.PrimaryTwigCount) * Mathf.PI * 2f + rng.Range(-0.25f, 0.25f);
                float radiusStart = parameters.BundleRadiusMetres * rng.Range(0.70f, 0.95f);
                Vector3 pStart = new Vector3(Mathf.Cos(angleStart) * radiusStart, rng.Range(0.005f, 0.025f), Mathf.Sin(angleStart) * radiusStart);

                // Opposite side endpoint (curved across or around the bundle)
                float angleEnd = angleStart + Mathf.PI * rng.Range(0.65f, 1.35f);
                float radiusEnd = parameters.BundleRadiusMetres * rng.Range(0.50f, 0.90f);
                Vector3 pEnd = new Vector3(Mathf.Cos(angleEnd) * radiusEnd, rng.Range(0.02f, 0.06f), Mathf.Sin(angleEnd) * radiusEnd);

                // Control point arches inward and upward
                Vector3 mid = (pStart + pEnd) * 0.5f;
                mid.y = parameters.BundleHeightMetres * rng.Range(0.55f, 0.95f);
                mid += new Vector3(rng.Range(-0.03f, 0.03f), 0f, rng.Range(-0.03f, 0.03f));

                // Enforce radial and height bounds
                ClampRadialXZ(ref pStart, maxPrimaryRadius);
                ClampRadialXZ(ref pEnd, maxPrimaryRadius);
                ClampRadialXZ(ref mid, maxPrimaryRadius);
                mid.y = Mathf.Clamp(mid.y, 0.005f, parameters.BundleHeightMetres);

                Color twigColor = Color.Lerp(baseWoodDark, baseWoodLight, rng.NextFloat01());
                AddExtrudedSplineTube(buffer, pStart, mid, pEnd, parameters.PrimaryTwigRadiusMetres, primarySegments, primarySides, twigColor, 0f);
            }

            // 2. Secondary brittle twigs (interlacing kindling twigs)
            float maxSecondaryRadius = Mathf.Max(0.001f, parameters.BundleRadiusMetres - parameters.SecondaryTwigRadiusMetres);
            for (int i = 0; i < parameters.SecondaryTwigCount; i++)
            {
                float angle = rng.Range(0f, Mathf.PI * 2f);
                float radius = parameters.BundleRadiusMetres * rng.Range(0.20f, 0.85f);
                Vector3 pStart = new Vector3(Mathf.Cos(angle) * radius, rng.Range(0.01f, 0.04f), Mathf.Sin(angle) * radius);

                float spreadAngle = angle + rng.Range(-1.2f, 1.2f);
                float length = parameters.BundleRadiusMetres * rng.Range(0.5f, 1.0f);
                Vector3 pEnd = pStart + new Vector3(Mathf.Cos(spreadAngle) * length, rng.Range(0.03f, 0.10f), Mathf.Sin(spreadAngle) * length);

                Vector3 mid = (pStart + pEnd) * 0.5f + new Vector3(rng.Range(-0.02f, 0.02f), rng.Range(0.02f, 0.05f), rng.Range(-0.02f, 0.02f));

                // Enforce radial footprint envelope and vertical bounds
                ClampRadialXZ(ref pStart, maxSecondaryRadius);
                ClampRadialXZ(ref pEnd, maxSecondaryRadius);
                ClampRadialXZ(ref mid, maxSecondaryRadius);
                pEnd.y = Mathf.Clamp(pEnd.y, 0.005f, parameters.BundleHeightMetres);
                mid.y = Mathf.Clamp(mid.y, 0.005f, parameters.BundleHeightMetres);

                Color twigColor = Color.Lerp(baseWoodDark, baseWoodLight, rng.NextFloat01() * 0.8f);
                AddExtrudedSplineTube(buffer, pStart, mid, pEnd, parameters.SecondaryTwigRadiusMetres, secondarySegments, secondarySides, twigColor, 0.3f);
            }

            // 3. Fine combustible fibers / dry grass curls (inner combustible bowl)
            if (parameters.FineFiberCount > 0)
            {
                float maxFiberRadius = Mathf.Max(0.001f, parameters.BundleRadiusMetres - parameters.FiberWidthMetres);
                for (int i = 0; i < parameters.FineFiberCount; i++)
                {
                    float angle = rng.Range(0f, Mathf.PI * 2f);
                    float r = parameters.BundleRadiusMetres * rng.Range(0.05f, 0.65f);
                    Vector3 pStart = new Vector3(Mathf.Cos(angle) * r, rng.Range(0.015f, 0.05f), Mathf.Sin(angle) * r);

                    float curlAngle = angle + rng.Range(0.8f, 2.0f);
                    float curlRadius = r * rng.Range(0.6f, 1.2f);
                    Vector3 pEnd = new Vector3(Mathf.Cos(curlAngle) * curlRadius, rng.Range(0.03f, 0.08f), Mathf.Sin(curlAngle) * curlRadius);

                    Vector3 mid = (pStart + pEnd) * 0.5f + new Vector3(rng.Range(-0.015f, 0.015f), rng.Range(0.02f, 0.06f), rng.Range(-0.015f, 0.015f));

                    // Enforce radial footprint and vertical bounds
                    ClampRadialXZ(ref pStart, maxFiberRadius);
                    ClampRadialXZ(ref pEnd, maxFiberRadius);
                    ClampRadialXZ(ref mid, maxFiberRadius);
                    pEnd.y = Mathf.Clamp(pEnd.y, 0.005f, parameters.BundleHeightMetres);
                    mid.y = Mathf.Clamp(mid.y, 0.005f, parameters.BundleHeightMetres);

                    Color fiberColor = Color.Lerp(dryStrawDark, dryStrawLight, rng.NextFloat01());
                    AddCurvedRibbon(buffer, pStart, mid, pEnd, parameters.FiberWidthMetres, fiberSegments, fiberColor);
                }
            }

            // 4. Coherent geometric fit and base-center alignment:
            // Scales down uniformly if actual prefit bounds exceed target envelope (0.30 x 0.15 x 0.28m),
            // and translates to place local pivot at base centre (horizontal center at origin (0, 0), minY = 0.00m).
            FitToTargetEnvelopeAndBaseCenter(buffer, parameters);

            string meshName = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "TinderDryBush_Seed{0}_Lod{1}", parameters.Seed, parameters.LodLevel);
            return buffer.BuildMesh(meshName);
        }

        private static void AddExtrudedSplineTube(MeshBuffer buffer, Vector3 p0, Vector3 p1, Vector3 p2,
            float baseRadius, int segments, int sides, Color baseColor, float colorOffset)
        {
            int startIndex = buffer.Vertices.Count;

            // Generate spline rings
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                float u = 1f - t;
                // Quadratic Bezier position
                Vector3 center = u * u * p0 + 2f * u * t * p1 + t * t * p2;

                // Tangent vector
                Vector3 tangent = (2f * u * (p1 - p0) + 2f * t * (p2 - p1)).normalized;
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.up;

                // Orthogonal basis vectors (Binormal and Normal)
                Vector3 refUp = Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right;
                Vector3 binormal = Vector3.Cross(tangent, refUp).normalized;
                Vector3 normal = Vector3.Cross(binormal, tangent).normalized;

                // Taper from base to tip (60% taper)
                float currentRadius = baseRadius * Mathf.Lerp(1.0f, 0.40f, t);

                // Slight color modulation along twig length
                Color ringColor = Color.Lerp(baseColor, baseColor * 1.15f, t + colorOffset * 0.2f);

                for (int side = 0; side < sides; side++)
                {
                    float angle = (side / (float)sides) * Mathf.PI * 2f;
                    Vector3 offset = (Mathf.Cos(angle) * binormal + Mathf.Sin(angle) * normal) * currentRadius;
                    Vector3 vertexPos = center + offset;
                    Vector3 vertexNormal = offset.normalized;

                    buffer.Vertices.Add(vertexPos);
                    buffer.Normals.Add(vertexNormal);
                    buffer.UVs.Add(new Vector2(t, side / (float)sides));
                    buffer.Colors.Add(ringColor);
                }
            }

            // Tube side triangles
            for (int s = 0; s < segments; s++)
            {
                int r0 = startIndex + s * sides;
                int r1 = startIndex + (s + 1) * sides;
                for (int side = 0; side < sides; side++)
                {
                    int nextSide = (side + 1) % sides;
                    int i00 = r0 + side;
                    int i01 = r0 + nextSide;
                    int i10 = r1 + side;
                    int i11 = r1 + nextSide;

                    buffer.Triangles.Add(i00);
                    buffer.Triangles.Add(i10);
                    buffer.Triangles.Add(i01);

                    buffer.Triangles.Add(i01);
                    buffer.Triangles.Add(i10);
                    buffer.Triangles.Add(i11);
                }
            }

            // Start cap
            int startCenterIndex = buffer.Vertices.Count;
            buffer.Vertices.Add(p0);
            Vector3 startNormal = -(p1 - p0).normalized;
            buffer.Normals.Add(startNormal);
            buffer.UVs.Add(new Vector2(0f, 0.5f));
            buffer.Colors.Add(baseColor);
            for (int side = 0; side < sides; side++)
            {
                int nextSide = (side + 1) % sides;
                buffer.Triangles.Add(startCenterIndex);
                buffer.Triangles.Add(startIndex + nextSide);
                buffer.Triangles.Add(startIndex + side);
            }

            // End cap
            int endCenterIndex = buffer.Vertices.Count;
            buffer.Vertices.Add(p2);
            Vector3 endNormal = (p2 - p1).normalized;
            buffer.Normals.Add(endNormal);
            buffer.UVs.Add(new Vector2(1f, 0.5f));
            buffer.Colors.Add(baseColor * 1.1f);
            int lastRingStart = startIndex + segments * sides;
            for (int side = 0; side < sides; side++)
            {
                int nextSide = (side + 1) % sides;
                buffer.Triangles.Add(endCenterIndex);
                buffer.Triangles.Add(lastRingStart + side);
                buffer.Triangles.Add(lastRingStart + nextSide);
            }
        }

        private static void AddCurvedRibbon(MeshBuffer buffer, Vector3 p0, Vector3 p1, Vector3 p2,
            float width, int segments, Color ribbonColor)
        {
            int startIndex = buffer.Vertices.Count;
            float halfWidth = width * 0.5f;

            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                float u = 1f - t;
                Vector3 center = u * u * p0 + 2f * u * t * p1 + t * t * p2;
                Vector3 tangent = (2f * u * (p1 - p0) + 2f * t * (p2 - p1)).normalized;
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.up;

                Vector3 refUp = Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right;
                Vector3 binormal = Vector3.Cross(tangent, refUp).normalized;
                Vector3 normal = Vector3.Cross(binormal, tangent).normalized;

                Vector3 vLeft = center - binormal * halfWidth;
                Vector3 vRight = center + binormal * halfWidth;

                buffer.Vertices.Add(vLeft);
                buffer.Vertices.Add(vRight);

                buffer.Normals.Add(normal);
                buffer.Normals.Add(normal);

                buffer.UVs.Add(new Vector2(t, 0f));
                buffer.UVs.Add(new Vector2(t, 1f));

                buffer.Colors.Add(ribbonColor);
                buffer.Colors.Add(ribbonColor);
            }

            for (int s = 0; s < segments; s++)
            {
                int r0 = startIndex + s * 2;
                int r1 = startIndex + (s + 1) * 2;

                // Front face
                buffer.Triangles.Add(r0);
                buffer.Triangles.Add(r1);
                buffer.Triangles.Add(r0 + 1);

                buffer.Triangles.Add(r0 + 1);
                buffer.Triangles.Add(r1);
                buffer.Triangles.Add(r1 + 1);

                // Back face (double-sided thin ribbon for natural dried straw look)
                buffer.Triangles.Add(r0);
                buffer.Triangles.Add(r0 + 1);
                buffer.Triangles.Add(r1);

                buffer.Triangles.Add(r0 + 1);
                buffer.Triangles.Add(r1 + 1);
                buffer.Triangles.Add(r1);
            }
        }

        private static void AlignToGroundPlane(MeshBuffer buffer)
        {
            if (buffer.Vertices.Count == 0) return;

            float minY = float.MaxValue;
            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                if (buffer.Vertices[i].y < minY)
                {
                    minY = buffer.Vertices[i].y;
                }
            }

            // Offset all vertices vertically so that the bottom-most point rests exactly at Y = 0.00m
            // Preserves base-centered pivot at (0, 0, 0)
            Vector3 offset = new Vector3(0f, -minY, 0f);
            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                buffer.Vertices[i] += offset;
            }
        }

        /// <summary>
        /// Measures the axis-aligned local bounds of a generated Mesh.
        /// </summary>
        public static Bounds MeasureMeshBounds(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            return mesh.bounds;
        }

        /// <summary>
        /// Calculates target bounding dimensions in metres for given parameters,
        /// mapping consistently to TinderMetadata.TargetDimensionsMetres (0.30 x 0.15 x 0.28m at default scale).
        /// </summary>
        public static Vector3 GetTargetDimensions(TinderParameters parameters)
        {
            // Consistent with TinderMetadata.TargetDimensionsMetres (0.30m Width X, 0.15m Height Y, 0.28m Depth Z).
            // BundleRadiusMetres at default (0.15m) produces a diameter of 0.30m, matching target width.
            float radiusScale = parameters.BundleRadiusMetres / 0.15f;
            float targetX = TinderMetadata.TargetDimensionsMetres.x * radiusScale;
            float targetY = Mathf.Max(parameters.BundleHeightMetres, TinderMetadata.TargetDimensionsMetres.y * radiusScale);
            float targetZ = TinderMetadata.TargetDimensionsMetres.z * radiusScale;
            return new Vector3(targetX, targetY, targetZ);
        }

        /// <summary>
        /// Calculates the intended target bounding box for given parameters,
        /// consistent with TinderMetadata.TargetDimensionsMetres.
        /// Base pivot is at origin (0, 0, 0) with minY at 0.00m.
        /// </summary>
        public static Bounds CalculateTargetBounds(TinderParameters parameters)
        {
            Vector3 target = GetTargetDimensions(parameters);
            return new Bounds(
                new Vector3(0f, target.y * 0.5f, 0f),
                target);
        }

        /// <summary>
        /// Performs the smallest coherent deterministic geometric fit and base-center alignment:
        /// 1. Measures actual prefit bounds across all generated vertices.
        /// 2. Validates finite coordinates and nondegenerate bounds.
        /// 3. Computes positive uniform scale-down factor s = min(1.0, min(target.x/size.x, target.y/size.y, target.z/size.z)).
        ///    Uniform scaling preserves organic aspect ratios, curvature, and twig cross-sections.
        ///    Under positive uniform scaling (s > 0), the surface normal transformation (s*I)^(-T) = (1/s)*I,
        ///    which normalizes back to the original normal vector, avoiding anisotropic distortion.
        ///    Does not upscale unnecessarily (s <= 1.0) and does not modify vertex/triangle counts or topology.
        /// 4. Translates all vertices so the horizontal bounding box center is exactly at (0, 0)
        ///    and bottom-most vertex rests precisely at ground contact Y = 0.00m.
        /// </summary>
        private static void FitToTargetEnvelopeAndBaseCenter(MeshBuffer buffer, TinderParameters parameters)
        {
            if (buffer.Vertices.Count == 0) return;

            // 1. Guard against non-finite vertex coordinates before any mutation
            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                Vector3 v = buffer.Vertices[i];
                if (!IsFinite(v.x) || !IsFinite(v.y) || !IsFinite(v.z))
                {
                    return;
                }
            }

            // 2. Measure actual prefit bounds
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                Vector3 v = buffer.Vertices[i];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
                if (v.z < minZ) minZ = v.z;
                if (v.z > maxZ) maxZ = v.z;
            }

            float prefitSizeX = maxX - minX;
            float prefitSizeY = maxY - minY;
            float prefitSizeZ = maxZ - minZ;

            // 3. Nondegenerate guard: ensure geometry has strictly positive extent in all dimensions
            if (prefitSizeX <= 1e-6f || prefitSizeY <= 1e-6f || prefitSizeZ <= 1e-6f)
            {
                return;
            }

            // 4. Retrieve target envelope consistent with validated metadata contract
            Vector3 target = GetTargetDimensions(parameters);
            if (target.x <= 1e-6f || target.y <= 1e-6f || target.z <= 1e-6f)
            {
                return;
            }

            // 5. Smallest coherent deterministic uniform scale-down factor.
            // Bounded by declared envelope: only scale down if exceeding target (s <= 1.0f); do not upscale.
            float scaleX = target.x / prefitSizeX;
            float scaleY = target.y / prefitSizeY;
            float scaleZ = target.z / prefitSizeZ;
            float s = Mathf.Min(1.0f, Mathf.Min(scaleX, Mathf.Min(scaleY, scaleZ)));

            if (!IsFinite(s) || s <= 0f)
            {
                return;
            }

            // Apply positive uniform scale-down
            if (s < 1.0f)
            {
                for (int i = 0; i < buffer.Vertices.Count; i++)
                {
                    buffer.Vertices[i] *= s;
                }
            }

            // 6. Measure bounds of scaled geometry to compute exact base-center translation
            minX = float.MaxValue; maxX = float.MinValue;
            minY = float.MaxValue; maxY = float.MinValue;
            minZ = float.MaxValue; maxZ = float.MinValue;

            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                Vector3 v = buffer.Vertices[i];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
                if (v.z < minZ) minZ = v.z;
                if (v.z > maxZ) maxZ = v.z;
            }

            // Guard against single-precision float roundoff exceeding target dimensions
            float scaledSizeX = maxX - minX;
            float scaledSizeY = maxY - minY;
            float scaledSizeZ = maxZ - minZ;
            if (scaledSizeX > target.x || scaledSizeY > target.y || scaledSizeZ > target.z)
            {
                float rescale = Mathf.Min(
                    scaledSizeX > target.x ? target.x / scaledSizeX : 1.0f,
                    Mathf.Min(
                        scaledSizeY > target.y ? target.y / scaledSizeY : 1.0f,
                        scaledSizeZ > target.z ? target.z / scaledSizeZ : 1.0f));
                if (rescale < 1.0f && rescale > 0f && IsFinite(rescale))
                {
                    for (int i = 0; i < buffer.Vertices.Count; i++)
                    {
                        buffer.Vertices[i] *= rescale;
                    }

                    // Re-measure after rescale
                    minX = float.MaxValue; maxX = float.MinValue;
                    minY = float.MaxValue; maxY = float.MinValue;
                    minZ = float.MaxValue; maxZ = float.MinValue;
                    for (int i = 0; i < buffer.Vertices.Count; i++)
                    {
                        Vector3 v = buffer.Vertices[i];
                        if (v.x < minX) minX = v.x;
                        if (v.x > maxX) maxX = v.x;
                        if (v.y < minY) minY = v.y;
                        if (v.y > maxY) maxY = v.y;
                        if (v.z < minZ) minZ = v.z;
                        if (v.z > maxZ) maxZ = v.z;
                    }
                }
            }

            // 7. Base-centre translation:
            // Translates all vertices so horizontal center (X, Z) is aligned to (0, 0)
            // and ground contact (min Y) rests precisely at Y = 0.00m.
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;

            Vector3 offset = new Vector3(centerX, minY, centerZ);
            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                buffer.Vertices[i] -= offset;
            }

            // 8. Residual correction: verify horizontal center and ground contact within 1e-8m
            // to guard against single-precision float roundoff
            float postMinX = float.MaxValue, postMaxX = float.MinValue;
            float postMinY = float.MaxValue;
            float postMinZ = float.MaxValue, postMaxZ = float.MinValue;
            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                Vector3 v = buffer.Vertices[i];
                if (v.x < postMinX) postMinX = v.x;
                if (v.x > postMaxX) postMaxX = v.x;
                if (v.y < postMinY) postMinY = v.y;
                if (v.z < postMinZ) postMinZ = v.z;
                if (v.z > postMaxZ) postMaxZ = v.z;
            }
            float residualCenterX = (postMinX + postMaxX) * 0.5f;
            float residualCenterZ = (postMinZ + postMaxZ) * 0.5f;
            if (Mathf.Abs(residualCenterX) > 1e-8f || Mathf.Abs(residualCenterZ) > 1e-8f || Mathf.Abs(postMinY) > 1e-8f)
            {
                Vector3 residual = new Vector3(residualCenterX, postMinY, residualCenterZ);
                for (int i = 0; i < buffer.Vertices.Count; i++)
                {
                    buffer.Vertices[i] -= residual;
                }
            }
        }

        /// <summary>
        /// Creates an explicit project-compatible vertex-color Material for Tinder / dry-brush rendering.
        /// Uses CityLife/Fire/TinderVertexColor shader (URP 17.6.0) to display the baked vertex color palette.
        /// </summary>
        public static Material CreateVertexColorMaterial()
        {
            Shader shader = Shader.Find("CityLife/Fire/TinderVertexColor");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null) return null;

            var mat = new Material(shader)
            {
                name = "CityLife_DryTinder_VertexColorMat"
            };

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.12f);

            return mat;
        }

        /// <summary>
        /// Creates a standalone Material appropriate for arid wood / tinder fibers.
        /// Searches for the project-compatible vertex-color shader first, then falls back to stock URP/Lit.
        /// Note: Stock URP/Lit or Standard shaders require custom bindings to display vertex colors.
        /// </summary>
        public static Material CreateDefaultMaterial()
        {
            Material mat = CreateVertexColorMaterial();
            if (mat != null) return mat;

            Shader fallbackShader = Shader.Find("Mobile/Diffuse");
            if (fallbackShader == null) fallbackShader = Shader.Find("Diffuse");
            if (fallbackShader == null) fallbackShader = Shader.Find("Unlit/Color");
            if (fallbackShader == null) return null;

            return new Material(fallbackShader)
            {
                name = "CityLife_DryTinder_FallbackMat",
                color = new Color(0.60f, 0.52f, 0.40f, 1.0f)
            };
        }

        /// <summary>
        /// Instantiates an isolated standalone GameObject with MeshFilter and MeshRenderer.
        /// Does NOT register into any gameplay system or scene manager.
        /// </summary>
        public static GameObject CreateStandaloneGameObject(TinderParameters parameters, Material customMaterial = null)
        {
            Mesh mesh = GenerateMesh(parameters);
            Material material = customMaterial != null ? customMaterial : CreateDefaultMaterial();

            var go = new GameObject("DryBush_Tinder_Visual");
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            return go;
        }
    }
}
