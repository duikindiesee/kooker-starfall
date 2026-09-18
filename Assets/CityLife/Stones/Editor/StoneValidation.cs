using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.Stones.Editor
{
    /// <summary>
    /// Authored standalone validation suite for stone mesh geometry, determinism, shading modes, and metre-scale metadata.
    /// Follows project testing conventions and destroys all created Mesh resources in finally blocks.
    /// NOTE: Per asset-only dispatch scope, this validation is authored for CI/editor review and is NOT run during this pass.
    /// </summary>
    public static class StoneValidation
    {
        public static bool FloatBitsEqual(float a, float b)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(a), 0) == BitConverter.ToInt32(BitConverter.GetBytes(b), 0);
        }

        public static bool Vector2BitsEqual(Vector2 a, Vector2 b)
        {
            return FloatBitsEqual(a.x, b.x) && FloatBitsEqual(a.y, b.y);
        }

        public static bool Vector3BitsEqual(Vector3 a, Vector3 b)
        {
            return FloatBitsEqual(a.x, b.x) && FloatBitsEqual(a.y, b.y) && FloatBitsEqual(a.z, b.z);
        }

        public static bool Vector4BitsEqual(Vector4 a, Vector4 b)
        {
            return FloatBitsEqual(a.x, b.x) && FloatBitsEqual(a.y, b.y) && FloatBitsEqual(a.z, b.z) && FloatBitsEqual(a.w, b.w);
        }

        public static bool ColorBitsEqual(Color a, Color b)
        {
            return FloatBitsEqual(a.r, b.r) && FloatBitsEqual(a.g, b.g) && FloatBitsEqual(a.b, b.b) && FloatBitsEqual(a.a, b.a);
        }

        public static bool ComparePositions(Vector3[] a, Vector3[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!Vector3BitsEqual(a[i], b[i])) return false;
            }
            return true;
        }

        public static bool CompareNormals(Vector3[] a, Vector3[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!Vector3BitsEqual(a[i], b[i])) return false;
            }
            return true;
        }

        public static bool CompareUVs(Vector2[] a, Vector2[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!Vector2BitsEqual(a[i], b[i])) return false;
            }
            return true;
        }

        public static bool CompareColors(Color[] a, Color[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!ColorBitsEqual(a[i], b[i])) return false;
            }
            return true;
        }

        public static bool CompareTangents(Vector4[] a, Vector4[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!Vector4BitsEqual(a[i], b[i])) return false;
            }
            return true;
        }

        public static bool CompareTriangles(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        public static bool CompareMetadata(StoneMetadata a, StoneMetadata b)
        {
            return a.shape == b.shape
                && a.seed == b.seed
                && a.variantIndex == b.variantIndex
                && FloatBitsEqual(a.uniformScale, b.uniformScale)
                && a.flatShaded == b.flatShaded
                && FloatBitsEqual(a.widthMetres, b.widthMetres)
                && FloatBitsEqual(a.heightMetres, b.heightMetres)
                && FloatBitsEqual(a.depthMetres, b.depthMetres)
                && FloatBitsEqual(a.boundingVolumeM3, b.boundingVolumeM3)
                && FloatBitsEqual(a.formFactor, b.formFactor)
                && FloatBitsEqual(a.estimatedVolumeM3, b.estimatedVolumeM3)
                && FloatBitsEqual(a.assumedDensityKgPerM3, b.assumedDensityKgPerM3)
                && FloatBitsEqual(a.intendedMassKg, b.intendedMassKg)
                && a.vertexCount == b.vertexCount
                && a.triangleCount == b.triangleCount
                && Vector3BitsEqual(a.boundsMin, b.boundsMin)
                && Vector3BitsEqual(a.boundsMax, b.boundsMax)
                && Vector3BitsEqual(a.pivotOffset, b.pivotOffset);
        }

        public static List<string> Run()
        {
            var passed = new List<string>();
            var trackedMeshes = new List<Mesh>();

            void Check(bool condition, string name)
            {
                if (!condition)
                {
                    throw new Exception("STONE VALIDATION FAILED: " + name);
                }
                passed.Add(name);
            }

            StoneGenerationResult GenerateTracked(StoneGenerationConfig config)
            {
                var res = StoneMeshGenerator.Generate(config);
                if (res.mesh != null)
                {
                    trackedMeshes.Add(res.mesh);
                }
                return res;
            }

            try
            {
                // =========================================================================
                // 0. Comparison Helper Regression Checks (Bitwise Scalar IEEE754 & Signed Zero)
                // =========================================================================

                // Equal values match
                Check(FloatBitsEqual(1.0f, 1.0f), "FloatBitsEqual identifies identical positive float values");
                Check(FloatBitsEqual(-42.5f, -42.5f), "FloatBitsEqual identifies identical negative float values");
                Check(Vector2BitsEqual(new Vector2(1.5f, -2.5f), new Vector2(1.5f, -2.5f)),
                    "Vector2BitsEqual identifies identical Vector2 values");
                Check(Vector3BitsEqual(new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, 3f)),
                    "Vector3BitsEqual identifies identical Vector3 values");
                Check(Vector4BitsEqual(new Vector4(1f, 2f, 3f, 4f), new Vector4(1f, 2f, 3f, 4f)),
                    "Vector4BitsEqual identifies identical Vector4 values");
                Check(ColorBitsEqual(new Color(0.1f, 0.2f, 0.3f, 1f), new Color(0.1f, 0.2f, 0.3f, 1f)),
                    "ColorBitsEqual identifies identical Color values");

                // Positive zero vs negative zero differ in IEEE754 bits despite numeric equality
                float positiveZero = 0.0f;
                float negativeZero = -0.0f;
                Check(positiveZero == negativeZero,
                    "positive and negative zero satisfy numeric float equality (==)");
                Check(!FloatBitsEqual(positiveZero, negativeZero),
                    "FloatBitsEqual distinguishes positive zero (+0.0f) and negative zero (-0.0f) bit patterns");
                Check(!Vector3BitsEqual(new Vector3(positiveZero, 1f, 2f), new Vector3(negativeZero, 1f, 2f)),
                    "Vector3BitsEqual distinguishes positive and negative zero in components");

                // One-bit float change differs
                float baseFloat = 1.0f;
                int baseBits = BitConverter.ToInt32(BitConverter.GetBytes(baseFloat), 0);
                float oneBitDiffFloat = BitConverter.ToSingle(BitConverter.GetBytes(baseBits ^ 1), 0);
                Check(!FloatBitsEqual(baseFloat, oneBitDiffFloat),
                    "FloatBitsEqual detects single-bit float difference in scalar");
                Check(!Vector2BitsEqual(new Vector2(baseFloat, 0f), new Vector2(oneBitDiffFloat, 0f)),
                    "Vector2BitsEqual detects single-bit float difference in Vector2 component");
                Check(!Vector3BitsEqual(new Vector3(baseFloat, 0f, 0f), new Vector3(oneBitDiffFloat, 0f, 0f)),
                    "Vector3BitsEqual detects single-bit float difference in Vector3 component");
                Check(!Vector4BitsEqual(new Vector4(0f, 0f, 0f, baseFloat), new Vector4(0f, 0f, 0f, oneBitDiffFloat)),
                    "Vector4BitsEqual detects single-bit float difference in Vector4 component");
                Check(!ColorBitsEqual(new Color(baseFloat, 0f, 0f, 1f), new Color(oneBitDiffFloat, 0f, 0f, 1f)),
                    "ColorBitsEqual detects single-bit float difference in Color component");

                // Buffer comparison helper checks (lengths, mismatch, bit-exactness)
                Vector3[] testVecA = new Vector3[] { new Vector3(1f, 2f, 3f) };
                Vector3[] testVecB = new Vector3[] { new Vector3(1f, 2f, 3f) };
                Vector3[] testVecBitDiff = new Vector3[] { new Vector3(oneBitDiffFloat, 2f, 3f) };
                Vector3[] testVecLengthDiff = new Vector3[] { new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f) };

                Check(ComparePositions(testVecA, testVecB),
                    "ComparePositions returns true for identical arrays");
                Check(!ComparePositions(testVecA, testVecBitDiff),
                    "ComparePositions returns false for one-bit component difference");
                Check(!ComparePositions(testVecA, testVecLengthDiff),
                    "ComparePositions returns false for array length mismatch");
                Check(!ComparePositions(testVecA, null),
                    "ComparePositions returns false for null buffer");

                // Metadata comparison helper regression check
                StoneMetadata metaRegressionA = default;
                StoneMetadata metaRegressionB = default;
                Check(CompareMetadata(metaRegressionA, metaRegressionB),
                    "CompareMetadata returns true for identical metadata instances");
                metaRegressionB.uniformScale = oneBitDiffFloat;
                Check(!CompareMetadata(metaRegressionA, metaRegressionB),
                    "CompareMetadata detects single-bit float difference in metadata field");

                // =========================================================================
                // 1. Determinism & Same-Input Reproducibility across All Promised Buffers
                // =========================================================================

                // Flat-shaded reproducibility
                var cfgCobbleFlat = new StoneGenerationConfig(StoneShapeKind.RiverCobble, 1001, 0, 1.0f, true);
                var resCobbleFlat1 = GenerateTracked(cfgCobbleFlat);
                var resCobbleFlat2 = GenerateTracked(cfgCobbleFlat);

                Check(resCobbleFlat1.mesh.vertexCount == resCobbleFlat2.mesh.vertexCount,
                    "flat repeat generation has identical vertex count");
                Check(resCobbleFlat1.mesh.triangles.Length == resCobbleFlat2.mesh.triangles.Length,
                    "flat repeat generation has identical triangle count");

                Vector3[] v1 = resCobbleFlat1.mesh.vertices;
                Vector3[] v2 = resCobbleFlat2.mesh.vertices;
                Vector3[] n1 = resCobbleFlat1.mesh.normals;
                Vector3[] n2 = resCobbleFlat2.mesh.normals;
                Vector2[] uv1 = resCobbleFlat1.mesh.uv;
                Vector2[] uv2 = resCobbleFlat2.mesh.uv;
                Color[] c1 = resCobbleFlat1.mesh.colors;
                Color[] c2 = resCobbleFlat2.mesh.colors;
                Vector4[] t1 = resCobbleFlat1.mesh.tangents;
                Vector4[] t2 = resCobbleFlat2.mesh.tangents;
                int[] tri1 = resCobbleFlat1.mesh.triangles;
                int[] tri2 = resCobbleFlat2.mesh.triangles;

                Check(ComparePositions(v1, v2), "flat repeat generation produces bitwise identical vertex positions xyz");
                Check(CompareNormals(n1, n2), "flat repeat generation produces bitwise identical normals xyz");
                Check(CompareUVs(uv1, uv2), "flat repeat generation produces bitwise identical UVs xy");
                Check(CompareColors(c1, c2), "flat repeat generation produces bitwise identical colors rgba");
                Check(CompareTangents(t1, t2), "flat repeat generation produces bitwise identical tangents xyzw");
                Check(CompareTriangles(tri1, tri2), "flat repeat generation produces identical triangle index contents");

                // Flat metadata exact comparisons across all public deterministic fields
                Check(resCobbleFlat1.metadata.shape == resCobbleFlat2.metadata.shape,
                    "flat repeat generation produces identical shape metadata");
                Check(resCobbleFlat1.metadata.seed == resCobbleFlat2.metadata.seed,
                    "flat repeat generation produces identical seed metadata");
                Check(resCobbleFlat1.metadata.variantIndex == resCobbleFlat2.metadata.variantIndex,
                    "flat repeat generation produces identical variantIndex metadata");
                Check(FloatBitsEqual(resCobbleFlat1.metadata.uniformScale, resCobbleFlat2.metadata.uniformScale),
                    "flat repeat generation produces bitwise identical uniformScale metadata");
                Check(resCobbleFlat1.metadata.flatShaded == resCobbleFlat2.metadata.flatShaded,
                    "flat repeat generation produces identical flatShaded metadata");
                Check(FloatBitsEqual(resCobbleFlat1.metadata.widthMetres, resCobbleFlat2.metadata.widthMetres),
                    "flat repeat generation produces bitwise identical widthMetres metadata");
                Check(FloatBitsEqual(resCobbleFlat1.metadata.heightMetres, resCobbleFlat2.metadata.heightMetres),
                    "flat repeat generation produces bitwise identical heightMetres metadata");
                Check(FloatBitsEqual(resCobbleFlat1.metadata.depthMetres, resCobbleFlat2.metadata.depthMetres),
                    "flat repeat generation produces bitwise identical depthMetres metadata");
                Check(FloatBitsEqual(resCobbleFlat1.metadata.boundingVolumeM3, resCobbleFlat2.metadata.boundingVolumeM3),
                    "flat repeat generation produces bitwise identical boundingVolumeM3 metadata");
                Check(FloatBitsEqual(resCobbleFlat1.metadata.formFactor, resCobbleFlat2.metadata.formFactor),
                    "flat repeat generation produces bitwise identical formFactor metadata");
                Check(FloatBitsEqual(resCobbleFlat1.metadata.estimatedVolumeM3, resCobbleFlat2.metadata.estimatedVolumeM3),
                    "flat repeat generation produces bitwise identical estimatedVolumeM3 metadata");
                Check(FloatBitsEqual(resCobbleFlat1.metadata.assumedDensityKgPerM3, resCobbleFlat2.metadata.assumedDensityKgPerM3),
                    "flat repeat generation produces bitwise identical assumedDensityKgPerM3 metadata");
                Check(FloatBitsEqual(resCobbleFlat1.metadata.intendedMassKg, resCobbleFlat2.metadata.intendedMassKg),
                    "flat repeat generation produces bitwise identical intendedMassKg metadata");
                Check(resCobbleFlat1.metadata.vertexCount == resCobbleFlat2.metadata.vertexCount,
                    "flat repeat generation produces identical vertexCount metadata");
                Check(resCobbleFlat1.metadata.triangleCount == resCobbleFlat2.metadata.triangleCount,
                    "flat repeat generation produces identical triangleCount metadata");
                Check(Vector3BitsEqual(resCobbleFlat1.metadata.boundsMin, resCobbleFlat2.metadata.boundsMin),
                    "flat repeat generation produces bitwise identical boundsMin metadata");
                Check(Vector3BitsEqual(resCobbleFlat1.metadata.boundsMax, resCobbleFlat2.metadata.boundsMax),
                    "flat repeat generation produces bitwise identical boundsMax metadata");
                Check(Vector3BitsEqual(resCobbleFlat1.metadata.pivotOffset, resCobbleFlat2.metadata.pivotOffset),
                    "flat repeat generation produces bitwise identical pivotOffset metadata");
                Check(CompareMetadata(resCobbleFlat1.metadata, resCobbleFlat2.metadata),
                    "flat repeat generation produces bitwise identical complete metadata");

                // Smooth-shaded reproducibility
                var cfgCobbleSmooth = new StoneGenerationConfig(StoneShapeKind.RiverCobble, 1001, 0, 1.0f, false);
                var resCobbleSmooth1 = GenerateTracked(cfgCobbleSmooth);
                var resCobbleSmooth2 = GenerateTracked(cfgCobbleSmooth);

                Check(resCobbleSmooth1.mesh.vertexCount == resCobbleSmooth2.mesh.vertexCount,
                    "smooth repeat generation has identical vertex count");
                Check(resCobbleSmooth1.mesh.triangles.Length == resCobbleSmooth2.mesh.triangles.Length,
                    "smooth repeat generation has identical triangle count");

                Vector3[] sv1 = resCobbleSmooth1.mesh.vertices;
                Vector3[] sv2 = resCobbleSmooth2.mesh.vertices;
                Vector3[] sn1 = resCobbleSmooth1.mesh.normals;
                Vector3[] sn2 = resCobbleSmooth2.mesh.normals;
                Vector2[] suv1 = resCobbleSmooth1.mesh.uv;
                Vector2[] suv2 = resCobbleSmooth2.mesh.uv;
                Color[] sc1 = resCobbleSmooth1.mesh.colors;
                Color[] sc2 = resCobbleSmooth2.mesh.colors;
                Vector4[] st1 = resCobbleSmooth1.mesh.tangents;
                Vector4[] st2 = resCobbleSmooth2.mesh.tangents;
                int[] stri1 = resCobbleSmooth1.mesh.triangles;
                int[] stri2 = resCobbleSmooth2.mesh.triangles;

                Check(ComparePositions(sv1, sv2), "smooth repeat generation produces bitwise identical vertex positions xyz");
                Check(CompareNormals(sn1, sn2), "smooth repeat generation produces bitwise identical normals xyz");
                Check(CompareUVs(suv1, suv2), "smooth repeat generation produces bitwise identical UVs xy");
                Check(CompareColors(sc1, sc2), "smooth repeat generation produces bitwise identical colors rgba");
                Check(CompareTangents(st1, st2), "smooth repeat generation produces bitwise identical tangents xyzw");
                Check(CompareTriangles(stri1, stri2), "smooth repeat generation produces identical triangle index contents");

                // Smooth metadata exact comparisons across all public deterministic fields
                Check(resCobbleSmooth1.metadata.shape == resCobbleSmooth2.metadata.shape,
                    "smooth repeat generation produces identical shape metadata");
                Check(resCobbleSmooth1.metadata.seed == resCobbleSmooth2.metadata.seed,
                    "smooth repeat generation produces identical seed metadata");
                Check(resCobbleSmooth1.metadata.variantIndex == resCobbleSmooth2.metadata.variantIndex,
                    "smooth repeat generation produces identical variantIndex metadata");
                Check(FloatBitsEqual(resCobbleSmooth1.metadata.uniformScale, resCobbleSmooth2.metadata.uniformScale),
                    "smooth repeat generation produces bitwise identical uniformScale metadata");
                Check(resCobbleSmooth1.metadata.flatShaded == resCobbleSmooth2.metadata.flatShaded,
                    "smooth repeat generation produces identical flatShaded metadata");
                Check(FloatBitsEqual(resCobbleSmooth1.metadata.widthMetres, resCobbleSmooth2.metadata.widthMetres),
                    "smooth repeat generation produces bitwise identical widthMetres metadata");
                Check(FloatBitsEqual(resCobbleSmooth1.metadata.heightMetres, resCobbleSmooth2.metadata.heightMetres),
                    "smooth repeat generation produces bitwise identical heightMetres metadata");
                Check(FloatBitsEqual(resCobbleSmooth1.metadata.depthMetres, resCobbleSmooth2.metadata.depthMetres),
                    "smooth repeat generation produces bitwise identical depthMetres metadata");
                Check(FloatBitsEqual(resCobbleSmooth1.metadata.boundingVolumeM3, resCobbleSmooth2.metadata.boundingVolumeM3),
                    "smooth repeat generation produces bitwise identical boundingVolumeM3 metadata");
                Check(FloatBitsEqual(resCobbleSmooth1.metadata.formFactor, resCobbleSmooth2.metadata.formFactor),
                    "smooth repeat generation produces bitwise identical formFactor metadata");
                Check(FloatBitsEqual(resCobbleSmooth1.metadata.estimatedVolumeM3, resCobbleSmooth2.metadata.estimatedVolumeM3),
                    "smooth repeat generation produces bitwise identical estimatedVolumeM3 metadata");
                Check(FloatBitsEqual(resCobbleSmooth1.metadata.assumedDensityKgPerM3, resCobbleSmooth2.metadata.assumedDensityKgPerM3),
                    "smooth repeat generation produces bitwise identical assumedDensityKgPerM3 metadata");
                Check(FloatBitsEqual(resCobbleSmooth1.metadata.intendedMassKg, resCobbleSmooth2.metadata.intendedMassKg),
                    "smooth repeat generation produces bitwise identical intendedMassKg metadata");
                Check(resCobbleSmooth1.metadata.vertexCount == resCobbleSmooth2.metadata.vertexCount,
                    "smooth repeat generation produces identical vertexCount metadata");
                Check(resCobbleSmooth1.metadata.triangleCount == resCobbleSmooth2.metadata.triangleCount,
                    "smooth repeat generation produces identical triangleCount metadata");
                Check(Vector3BitsEqual(resCobbleSmooth1.metadata.boundsMin, resCobbleSmooth2.metadata.boundsMin),
                    "smooth repeat generation produces bitwise identical boundsMin metadata");
                Check(Vector3BitsEqual(resCobbleSmooth1.metadata.boundsMax, resCobbleSmooth2.metadata.boundsMax),
                    "smooth repeat generation produces bitwise identical boundsMax metadata");
                Check(Vector3BitsEqual(resCobbleSmooth1.metadata.pivotOffset, resCobbleSmooth2.metadata.pivotOffset),
                    "smooth repeat generation produces bitwise identical pivotOffset metadata");
                Check(CompareMetadata(resCobbleSmooth1.metadata, resCobbleSmooth2.metadata),
                    "smooth repeat generation produces bitwise identical complete metadata");

                // =========================================================================
                // 2. Distinct Seed & Variant Effect
                // =========================================================================

                var resCobbleSeedDiff = GenerateTracked(new StoneGenerationConfig(StoneShapeKind.RiverCobble, 2002, 0, 1.0f, true));
                bool seedDiffPositions = false;
                Vector3[] vSeedDiff = resCobbleSeedDiff.mesh.vertices;
                for (int i = 0; i < v1.Length; i++)
                {
                    if (Vector3.Distance(v1[i], vSeedDiff[i]) > 0.001f)
                    {
                        seedDiffPositions = true;
                        break;
                    }
                }
                Check(seedDiffPositions, "distinct seed produces differing vertex geometry");

                var resCobbleVariantDiff = GenerateTracked(new StoneGenerationConfig(StoneShapeKind.RiverCobble, 1001, 1, 1.0f, true));
                bool variantDiffPositions = false;
                Vector3[] vVarDiff = resCobbleVariantDiff.mesh.vertices;
                for (int i = 0; i < v1.Length; i++)
                {
                    if (Vector3.Distance(v1[i], vVarDiff[i]) > 0.001f)
                    {
                        variantDiffPositions = true;
                        break;
                    }
                }
                Check(variantDiffPositions, "distinct variantIndex produces differing vertex geometry");

                // =========================================================================
                // 3. Usable Resting Base Pivot
                // =========================================================================

                Check(Mathf.Abs(resCobbleFlat1.metadata.boundsMin.y) < 0.0001f,
                    "pivot rests exactly at base contact plane y=0 on horizontal surface");
                Check(Mathf.Abs(resCobbleFlat1.metadata.boundsMin.x + resCobbleFlat1.metadata.boundsMax.x) < 0.0001f,
                    "pivot is centered on X axis");
                Check(Mathf.Abs(resCobbleFlat1.metadata.boundsMin.z + resCobbleFlat1.metadata.boundsMax.z) < 0.0001f,
                    "pivot is centered on Z axis");

                // =========================================================================
                // 4. Shape Coverage, Bounds, and Mass at scale 1.0f for all 4 Archetypes
                // =========================================================================

                StoneShapeKind[] shapes = new StoneShapeKind[]
                {
                    StoneShapeKind.RiverCobble,
                    StoneShapeKind.Fieldstone,
                    StoneShapeKind.FlatSlab,
                    StoneShapeKind.Handstone
                };

                foreach (var shape in shapes)
                {
                    var cfg = new StoneGenerationConfig(shape, 4242, 1, 1.0f, true);
                    var res = GenerateTracked(cfg);
                    var meta = res.metadata;

                    Check(meta.HasValidDimensions, $"{shape} dimensions are finite positive metres <= 20m");
                    Check(meta.IsHandCarryable, $"{shape} intended mass ({meta.intendedMassKg:F2}kg) is within hand-carryable range (0.5-25kg)");
                    Check(meta.widthMetres >= 0.05f && meta.widthMetres <= 0.60f, $"{shape} width ({meta.widthMetres:F3}m) matches hand-carryable scale");
                    Check(meta.heightMetres >= 0.03f && meta.heightMetres <= 0.40f, $"{shape} height ({meta.heightMetres:F3}m) matches hand-carryable scale");
                    Check(meta.depthMetres >= 0.05f && meta.depthMetres <= 0.60f, $"{shape} depth ({meta.depthMetres:F3}m) matches hand-carryable scale");
                }

                // =========================================================================
                // 5. Metadata vs Mesh Counts and Bounds Coherence across Both Shading Modes
                // =========================================================================

                foreach (var shape in shapes)
                {
                    foreach (bool flat in new bool[] { true, false })
                    {
                        var cfg = new StoneGenerationConfig(shape, 5555, 0, 1.2f, flat);
                        var res = GenerateTracked(cfg);
                        var meta = res.metadata;
                        var mesh = res.mesh;
                        var estMeta = StoneMeshGenerator.EstimateMetadata(cfg);

                        // Mesh vs Metadata count coherence
                        Check(meta.vertexCount == mesh.vertexCount,
                            $"{shape} (flat={flat}) metadata vertexCount ({meta.vertexCount}) matches Mesh.vertexCount ({mesh.vertexCount})");
                        Check(meta.triangleCount == mesh.triangles.Length / 3,
                            $"{shape} (flat={flat}) metadata triangleCount ({meta.triangleCount}) matches Mesh triangle count");

                        // EstimateMetadata exact prediction coherence
                        Check(estMeta.vertexCount == mesh.vertexCount,
                            $"{shape} (flat={flat}) EstimateMetadata vertexCount ({estMeta.vertexCount}) matches actual Mesh.vertexCount ({mesh.vertexCount})");
                        Check(estMeta.triangleCount == meta.triangleCount,
                            $"{shape} (flat={flat}) EstimateMetadata triangleCount matches metadata triangleCount");

                        // Bounds coherence within 0.001m
                        Check(Mathf.Abs(meta.boundsMin.x - mesh.bounds.min.x) < 0.001f, $"{shape} (flat={flat}) boundsMin.x coherent with Mesh bounds");
                        Check(Mathf.Abs(meta.boundsMax.x - mesh.bounds.max.x) < 0.001f, $"{shape} (flat={flat}) boundsMax.x coherent with Mesh bounds");
                        Check(Mathf.Abs(meta.boundsMin.y - mesh.bounds.min.y) < 0.001f, $"{shape} (flat={flat}) boundsMin.y coherent with Mesh bounds");
                        Check(Mathf.Abs(meta.boundsMax.y - mesh.bounds.max.y) < 0.001f, $"{shape} (flat={flat}) boundsMax.y coherent with Mesh bounds");
                        Check(Mathf.Abs(meta.boundsMin.z - mesh.bounds.min.z) < 0.001f, $"{shape} (flat={flat}) boundsMin.z coherent with Mesh bounds");
                        Check(Mathf.Abs(meta.boundsMax.z - mesh.bounds.max.z) < 0.001f, $"{shape} (flat={flat}) boundsMax.z coherent with Mesh bounds");
                    }
                }

                // =========================================================================
                // 6. Geometry, Outward Winding, Non-Degenerate UVs, and Tangent Invariants
                // =========================================================================

                foreach (var shape in shapes)
                {
                    foreach (bool flat in new bool[] { true, false })
                    {
                        var cfg = new StoneGenerationConfig(shape, 8888, 0, 1.0f, flat);
                        var res = GenerateTracked(cfg);
                        var mesh = res.mesh;

                        Vector3[] verts = mesh.vertices;
                        Vector3[] normals = mesh.normals;
                        Vector2[] uvs = mesh.uv;
                        Vector4[] tangents = mesh.tangents;
                        int[] tris = mesh.triangles;
                        Vector3 centroid = (res.metadata.boundsMin + res.metadata.boundsMax) * 0.5f;

                        bool noDegenerate3D = true;
                        bool allOutward = true;
                        bool noDegenerateUV = true;

                        for (int t = 0; t < tris.Length; t += 3)
                        {
                            Vector3 a = verts[tris[t]];
                            Vector3 b = verts[tris[t + 1]];
                            Vector3 c = verts[tris[t + 2]];

                            // 3D Area check
                            Vector3 cross = Vector3.Cross(b - a, c - a);
                            if (cross.sqrMagnitude < 1e-12f)
                            {
                                noDegenerate3D = false;
                                break;
                            }

                            // Outward winding check
                            Vector3 faceCenter = (a + b + c) * 0.33333333f;
                            Vector3 outward = faceCenter - centroid;
                            if (Vector3.Dot(cross, outward) < 0f)
                            {
                                allOutward = false;
                                break;
                            }

                            // UV Non-degeneracy check
                            Vector2 uvA = uvs[tris[t]];
                            Vector2 uvB = uvs[tris[t + 1]];
                            Vector2 uvC = uvs[tris[t + 2]];
                            float uvDet = (uvB.x - uvA.x) * (uvC.y - uvA.y) - (uvC.x - uvA.x) * (uvB.y - uvA.y);
                            if (Mathf.Abs(uvDet) < 1e-8f)
                            {
                                noDegenerateUV = false;
                                break;
                            }
                        }

                        Check(noDegenerate3D, $"{shape} (flat={flat}) has zero degenerate 3D triangles");
                        Check(allOutward, $"{shape} (flat={flat}) has strictly coherent outward-facing triangle winding");
                        Check(noDegenerateUV, $"{shape} (flat={flat}) has non-zero per-triangle UV determinant on every face");

                        // Normals & Tangents validation
                        bool validNormals = true;
                        bool validTangents = true;
                        bool orthogonalTangents = true;
                        bool validHandedness = true;

                        for (int i = 0; i < verts.Length; i++)
                        {
                            Vector3 n = normals[i];
                            Vector4 tan = tangents[i];
                            Vector3 tVec = new Vector3(tan.x, tan.y, tan.z);

                            if (float.IsNaN(n.x) || n.sqrMagnitude < 0.98f || n.sqrMagnitude > 1.02f)
                            {
                                validNormals = false;
                                break;
                            }

                            if (float.IsNaN(tVec.x) || tVec.sqrMagnitude < 0.98f || tVec.sqrMagnitude > 1.02f)
                            {
                                validTangents = false;
                                break;
                            }

                            if (Mathf.Abs(Vector3.Dot(n, tVec)) > 0.02f)
                            {
                                orthogonalTangents = false;
                                break;
                            }

                            if (Mathf.Abs(Mathf.Abs(tan.w) - 1.0f) > 0.01f)
                            {
                                validHandedness = false;
                                break;
                            }
                        }

                        Check(validNormals, $"{shape} (flat={flat}) has finite unit-length normals on all vertices");
                        Check(validTangents, $"{shape} (flat={flat}) has finite unit-length tangent vectors on all vertices");
                        Check(orthogonalTangents, $"{shape} (flat={flat}) has tangent-normal orthogonality on all vertices");
                        Check(validHandedness, $"{shape} (flat={flat}) has valid tangent handedness (+1 or -1) on all vertices");
                    }
                }

                // =========================================================================
                // 7. Shape Differentiation
                // =========================================================================

                var cobbleMeta = StoneMeshGenerator.EstimateMetadata(new StoneGenerationConfig(StoneShapeKind.RiverCobble, 777));
                var slabMeta = StoneMeshGenerator.EstimateMetadata(new StoneGenerationConfig(StoneShapeKind.FlatSlab, 777));
                var fieldMeta = StoneMeshGenerator.EstimateMetadata(new StoneGenerationConfig(StoneShapeKind.Fieldstone, 777));
                var handMeta = StoneMeshGenerator.EstimateMetadata(new StoneGenerationConfig(StoneShapeKind.Handstone, 777));

                float slabAspect = slabMeta.heightMetres / slabMeta.widthMetres;
                float cobbleAspect = cobbleMeta.heightMetres / cobbleMeta.widthMetres;
                Check(slabAspect < 0.35f, "FlatSlab has low height-to-width aspect ratio (< 0.35)");
                Check(cobbleAspect > 0.45f, "RiverCobble has balanced rounded aspect ratio (> 0.45)");
                Check(fieldMeta.intendedMassKg > handMeta.intendedMassKg, "Fieldstone structural mass exceeds Handstone tool mass");

                // =========================================================================
                // 8. Input Validation & Struct Default Regression Checks
                // =========================================================================

                // default(StoneGenerationConfig) validation
                bool defaultThrowsOnValidate = false;
                try
                {
                    StoneGenerationConfig defConfig = default;
                    defConfig.Validate();
                }
                catch (ArgumentOutOfRangeException)
                {
                    defaultThrowsOnValidate = true;
                }
                Check(defaultThrowsOnValidate, "default(StoneGenerationConfig) throws ArgumentOutOfRangeException on Validate()");

                bool defaultThrowsOnGenerate = false;
                try
                {
                    StoneGenerationConfig defConfig = default;
                    StoneMeshGenerator.Generate(defConfig);
                }
                catch (ArgumentOutOfRangeException)
                {
                    defaultThrowsOnGenerate = true;
                }
                Check(defaultThrowsOnGenerate, "default(StoneGenerationConfig) throws ArgumentOutOfRangeException on Generate()");

                bool defaultThrowsOnEstimate = false;
                try
                {
                    StoneGenerationConfig defConfig = default;
                    StoneMeshGenerator.EstimateMetadata(defConfig);
                }
                catch (ArgumentOutOfRangeException)
                {
                    defaultThrowsOnEstimate = true;
                }
                Check(defaultThrowsOnEstimate, "default(StoneGenerationConfig) throws ArgumentOutOfRangeException on EstimateMetadata()");

                // Invalid enum values
                bool invalidEnumThrows = false;
                try
                {
                    new StoneGenerationConfig((StoneShapeKind)99, 0, 0, 1.0f);
                }
                catch (ArgumentOutOfRangeException)
                {
                    invalidEnumThrows = true;
                }
                Check(invalidEnumThrows, "invalid StoneShapeKind enum throws ArgumentOutOfRangeException in constructor");

                // Invalid scale values
                float[] invalidScales = new float[]
                {
                    0.0f,
                    -1.0f,
                    float.NaN,
                    float.PositiveInfinity,
                    float.NegativeInfinity,
                    0.10f, // below MinSupportedScale (0.25f)
                    4.00f  // above MaxSupportedScale (3.00f)
                };

                foreach (float badScale in invalidScales)
                {
                    bool badScaleThrows = false;
                    try
                    {
                        new StoneGenerationConfig(StoneShapeKind.RiverCobble, 0, 0, badScale);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        badScaleThrows = true;
                    }
                    Check(badScaleThrows, $"scale {badScale} throws ArgumentOutOfRangeException in constructor");
                }

                // Mutable struct field post-initialization validation
                bool mutatedScaleThrows = false;
                try
                {
                    var cfgMut = new StoneGenerationConfig(StoneShapeKind.RiverCobble, 0, 0, 1.0f);
                    cfgMut.uniformScale = -5.0f;
                    StoneMeshGenerator.Generate(cfgMut);
                }
                catch (ArgumentOutOfRangeException)
                {
                    mutatedScaleThrows = true;
                }
                Check(mutatedScaleThrows, "mutated negative uniformScale throws ArgumentOutOfRangeException at Generate entrypoint");

                bool mutatedEnumThrows = false;
                try
                {
                    var cfgMut = new StoneGenerationConfig(StoneShapeKind.RiverCobble, 0, 0, 1.0f);
                    cfgMut.shape = (StoneShapeKind)123;
                    StoneMeshGenerator.Generate(cfgMut);
                }
                catch (ArgumentOutOfRangeException)
                {
                    mutatedEnumThrows = true;
                }
                Check(mutatedEnumThrows, "mutated invalid shape enum throws ArgumentOutOfRangeException at Generate entrypoint");

                // Valid supported scale boundaries
                var resMinScale = GenerateTracked(new StoneGenerationConfig(StoneShapeKind.RiverCobble, 1001, 0, StoneGenerationConfig.MinSupportedScale));
                Check(resMinScale.mesh != null && resMinScale.metadata.HasValidDimensions,
                    "boundary MinSupportedScale (0.25f) produces valid mesh and metadata");

                var resMaxScale = GenerateTracked(new StoneGenerationConfig(StoneShapeKind.RiverCobble, 1001, 0, StoneGenerationConfig.MaxSupportedScale));
                Check(resMaxScale.mesh != null && resMaxScale.metadata.HasValidDimensions,
                    "boundary MaxSupportedScale (3.00f) produces valid mesh and metadata");
            }
            finally
            {
                // Guarantee cleanup of every Mesh created during validation, including failure paths
                for (int i = 0; i < trackedMeshes.Count; i++)
                {
                    if (trackedMeshes[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(trackedMeshes[i]);
                    }
                }
                trackedMeshes.Clear();
            }

            return passed;
        }
    }
}
