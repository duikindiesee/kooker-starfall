using System;
using System.Globalization;
using UnityEngine;

namespace CityLife.Fire
{
    /// <summary>
    /// Checks-only validation helper suite for Tinder / Dry-Brush procedural geometry and material assets.
    /// Callable later by coordinator in Unity Editor or test runner (do NOT execute during authoring pass).
    /// Performs numeric parameter validation, LOD metric verification, ground-alignment and pivot verification,
    /// bounds measurement against exact targets without relaxation, determinism verification, and aggregation self-tests.
    /// </summary>
    public static class TinderValidation
    {
        /// <summary>
        /// Small numerical floating-point roundoff tolerance for bounds comparison (metres).
        /// Justified for metre-scale IEEE 754 single-precision float computation (~1e-5m / 10 micrometres
        /// at default 30cm scale). Strictly replaces the prior material 5mm allowance to prevent
        /// acceptance-budget inflation while accommodating legitimate floating-point roundoff.
        /// </summary>
        public const float BoundsEpsilonMetres = 1e-5f; // 0.00001m (10 micrometres / 1e-5m)

        /// <summary>
        /// Ground alignment tolerance (metres).
        /// Bottom-most vertex must rest on the ground plane at Y = 0.00m within floating-point roundoff (1e-5m).
        /// </summary>
        public const float GroundEpsilonMetres = 1e-5f; // 0.00001m (10 micrometres / 1e-5m)

        /// <summary>
        /// Horizontal pivot alignment tolerance (metres).
        /// Center of mass / bounding center horizontal offset from (0,0) must not exceed floating-point roundoff (1e-5m).
        /// Strictly replaces the prior material 5cm allowance to evaluate exact design center alignment.
        /// </summary>
        public const float PivotEpsilonMetres = 1e-5f; // 0.00001m (10 micrometres / 1e-5m)

        [Serializable]
        public sealed class LodMetricReport
        {
            public int lodLevel;
            public int expectedVertices;
            public int actualVertices;
            public int expectedTriangles;
            public int actualTriangles;
            public int calculatedVertices;
            public int calculatedTriangles;
            public Vector3 boundsMin;
            public Vector3 boundsMax;
            public Vector3 boundsSize;
            public Vector3 boundsCenter;
            public bool boundsFinite;
            public bool boundsWithinTarget;
            public bool groundAligned;
            public bool pivotAligned;
            public bool vertexColorsPopulated;
            public bool passed;
        }

        [Serializable]
        public sealed class ValidationReport
        {
            public string schema = "citylife.fire.tinder-validation.v1";
            public string status = "UNKNOWN";
            public string authorStatus = "AUTHOR-COMPLETED SOURCE-ONLY";
            public string verificationNotice = "Compilation and rendering unverified until coordinator execution";
            public int totalAssertions;
            public int passedAssertions;
            public bool parameterValidationPassed;
            public bool lodMetricsPassed;
            public bool boundsTargetCheckPassed;
            public bool determinismCheckPassed;
            public bool shaderAssetCheckPassed;
            public LodMetricReport lod0 = new LodMetricReport();
            public LodMetricReport lod1 = new LodMetricReport();
            public LodMetricReport lod2 = new LodMetricReport();
            public string summary;
        }

        [Serializable]
        public sealed class SelfTestReport
        {
            public string schema = "citylife.fire.tinder-validation-selftest.v1";
            public string status = "UNKNOWN";
            public string authorStatus = "AUTHOR-COMPLETED SOURCE-ONLY";
            public string verificationNotice = "Compilation and execution unverified until coordinator execution in Unity";
            public int totalSelfTests;
            public int passedSelfTests;
            public bool noThrowHandledCorrectly;
            public bool wrongThrowHandledCorrectly;
            public bool missingShaderHandledCorrectly;
            public bool badPivotHandledCorrectly;
            public bool badCountHandledCorrectly;
            public bool badBoundsHandledCorrectly;
            public bool deliberateFailureHandledCorrectly;
            public string summary;
        }

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);

        /// <summary>
        /// Destroys a Unity Object immediately in edit mode or at end of frame in play mode.
        /// Never throws if object is null or already destroyed.
        /// </summary>
        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            try
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(obj);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
            catch
            {
                // Suppress secondary cleanup exceptions
            }
        }

        /// <summary>
        /// Records an assertion outcome, propagating failure into scopeFlag.
        /// </summary>
        private static bool AssertTrue(bool condition, string message, ref int assertions, ref int passed, ref bool scopeFlag)
        {
            assertions++;
            if (condition)
            {
                passed++;
                return true;
            }
            else
            {
                scopeFlag = false;
                Debug.LogError($"[TinderValidation] Assertion FAILED: {message}");
                return false;
            }
        }

        /// <summary>
        /// Verifies that an action throws the expected exception type.
        /// Returns false for no exception or wrong exception type.
        /// Propagates failure into scopeFlag.
        /// </summary>
        private static bool AssertThrows<TException>(Action action, string message, ref int assertions, ref int passed, ref bool scopeFlag) where TException : Exception
        {
            assertions++;
            try
            {
                action();
                scopeFlag = false;
                Debug.LogError($"[TinderValidation] Expected {typeof(TException).Name} but NO exception was thrown: {message}");
                return false;
            }
            catch (TException)
            {
                passed++;
                return true;
            }
            catch (Exception ex)
            {
                scopeFlag = false;
                Debug.LogError($"[TinderValidation] Expected {typeof(TException).Name} but caught WRONG exception ({ex.GetType().Name}: {ex.Message}): {message}");
                return false;
            }
        }

        /// <summary>
        /// Overload for AssertThrows without scope flag (for standalone or self-test callers).
        /// </summary>
        public static bool AssertThrows<TException>(Action action, ref int assertions, ref int passed) where TException : Exception
        {
            bool dummy = true;
            return AssertThrows<TException>(action, string.Empty, ref assertions, ref passed, ref dummy);
        }

        /// <summary>
        /// Evaluates overall aggregation decision from a validation report.
        /// Invariant: requires totalAssertions > 0 and passedAssertions == totalAssertions,
        /// in addition to every explicit category check passing.
        /// </summary>
        public static bool EvaluateAggregationDecision(ValidationReport report, out int exitCode)
        {
            if (report == null)
            {
                exitCode = 1;
                return false;
            }

            bool allExplicitPassed = report.parameterValidationPassed &&
                                     report.lodMetricsPassed &&
                                     report.boundsTargetCheckPassed &&
                                     report.determinismCheckPassed &&
                                     report.shaderAssetCheckPassed &&
                                     report.lod0 != null && report.lod0.passed &&
                                     report.lod1 != null && report.lod1.passed &&
                                     report.lod2 != null && report.lod2.passed;

            bool invariantPassed = report.totalAssertions > 0 && (report.passedAssertions == report.totalAssertions);

            bool overallPassed = allExplicitPassed && invariantPassed && report.status == "PASSED";

            exitCode = overallPassed ? 0 : 1;
            return overallPassed;
        }

        /// <summary>
        /// Executes full production validation checks on Tinder / Dry-Brush assets and returns a structured report.
        /// Does NOT modify scenes or save assets.
        /// </summary>
        public static ValidationReport RunValidation()
        {
            var report = new ValidationReport();
            int assertions = 0;
            int passed = 0;

            // 1. Parameter validation tests (invalid inputs must throw before allocations)
            bool paramPassed = true;
            try
            {
                // Negative count rejection
                var badParams = TinderParameters.Default;
                badParams.PrimaryTwigCount = -1;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "PrimaryTwigCount = -1", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.SecondaryTwigCount = -1;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "SecondaryTwigCount = -1", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.FineFiberCount = -1;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "FineFiberCount = -1", ref assertions, ref passed, ref paramPassed);

                // Upper bounds rejection
                badParams = TinderParameters.Default;
                badParams.PrimaryTwigCount = 101;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "PrimaryTwigCount = 101", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.SecondaryTwigCount = 201;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "SecondaryTwigCount = 201", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.FineFiberCount = 501;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "FineFiberCount = 501", ref assertions, ref passed, ref paramPassed);

                // All zero counts rejection
                badParams = TinderParameters.Default;
                badParams.PrimaryTwigCount = 0;
                badParams.SecondaryTwigCount = 0;
                badParams.FineFiberCount = 0;
                AssertThrows<ArgumentException>(() => TinderGeometry.ValidateParameters(badParams), "All zero counts", ref assertions, ref passed, ref paramPassed);

                // Nonfinite dimension rejection (NaN)
                badParams = TinderParameters.Default;
                badParams.BundleRadiusMetres = float.NaN;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "BundleRadius = NaN", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.BundleHeightMetres = float.NaN;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "BundleHeight = NaN", ref assertions, ref passed, ref paramPassed);

                // Nonfinite dimension rejection (Infinity)
                badParams = TinderParameters.Default;
                badParams.BundleRadiusMetres = float.PositiveInfinity;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "BundleRadius = +Inf", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.BundleHeightMetres = float.PositiveInfinity;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "BundleHeight = +Inf", ref assertions, ref passed, ref paramPassed);

                // Zero / negative dimension rejection
                badParams = TinderParameters.Default;
                badParams.BundleRadiusMetres = 0.0001f;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "BundleRadius <= 0.001", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.BundleHeightMetres = 0.0001f;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "BundleHeight <= 0.001", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.PrimaryTwigRadiusMetres = -0.01f;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "PrimaryTwigRadius = -0.01", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.PrimaryTwigRadiusMetres = badParams.BundleRadiusMetres * 1.1f;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "PrimaryTwigRadius > BundleRadius", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.SecondaryTwigRadiusMetres = -0.01f;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "SecondaryTwigRadius = -0.01", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.SecondaryTwigRadiusMetres = badParams.BundleRadiusMetres * 1.1f;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "SecondaryTwigRadius > BundleRadius", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.FiberWidthMetres = -0.01f;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "FiberWidth = -0.01", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.FiberWidthMetres = badParams.BundleRadiusMetres * 1.1f;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "FiberWidth > BundleRadius", ref assertions, ref passed, ref paramPassed);

                // Invalid LOD rejection
                badParams = TinderParameters.Default;
                badParams.LodLevel = -1;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "LodLevel = -1", ref assertions, ref passed, ref paramPassed);

                badParams = TinderParameters.Default;
                badParams.LodLevel = 5;
                AssertThrows<ArgumentOutOfRangeException>(() => TinderGeometry.ValidateParameters(badParams), "LodLevel = 5", ref assertions, ref passed, ref paramPassed);

                // Valid presets pass without exception
                try
                {
                    TinderGeometry.ValidateParameters(TinderParameters.Default);
                    AssertTrue(true, "ValidateParameters(Default) succeeds", ref assertions, ref passed, ref paramPassed);
                }
                catch (Exception ex)
                {
                    AssertTrue(false, $"ValidateParameters(Default) threw: {ex.Message}", ref assertions, ref passed, ref paramPassed);
                }

                try
                {
                    TinderGeometry.ValidateParameters(TinderParameters.ForLod(0));
                    AssertTrue(true, "ValidateParameters(ForLod(0)) succeeds", ref assertions, ref passed, ref paramPassed);
                }
                catch (Exception ex)
                {
                    AssertTrue(false, $"ValidateParameters(ForLod(0)) threw: {ex.Message}", ref assertions, ref passed, ref paramPassed);
                }

                try
                {
                    TinderGeometry.ValidateParameters(TinderParameters.ForLod(1));
                    AssertTrue(true, "ValidateParameters(ForLod(1)) succeeds", ref assertions, ref passed, ref paramPassed);
                }
                catch (Exception ex)
                {
                    AssertTrue(false, $"ValidateParameters(ForLod(1)) threw: {ex.Message}", ref assertions, ref passed, ref paramPassed);
                }

                try
                {
                    TinderGeometry.ValidateParameters(TinderParameters.ForLod(2));
                    AssertTrue(true, "ValidateParameters(ForLod(2)) succeeds", ref assertions, ref passed, ref paramPassed);
                }
                catch (Exception ex)
                {
                    AssertTrue(false, $"ValidateParameters(ForLod(2)) threw: {ex.Message}", ref assertions, ref passed, ref paramPassed);
                }
            }
            catch (Exception ex)
            {
                paramPassed = false;
                Debug.LogError($"[TinderValidation] Parameter validation unexpected failure: {ex.Message}");
            }
            report.parameterValidationPassed = paramPassed;

            // 2. Validate LOD metrics & geometry (LOD0: 1026/1852, LOD1: 454/792, LOD2: 218/392)
            bool lodPassed = true;
            Vector3 targetDimensions = TinderMetadata.TargetDimensionsMetres;
            try
            {
                report.lod0 = ValidateLodLevel(0, 1026, 1852, targetDimensions, ref assertions, ref passed, ref lodPassed);
                report.lod1 = ValidateLodLevel(1, 454, 792, targetDimensions, ref assertions, ref passed, ref lodPassed);
                report.lod2 = ValidateLodLevel(2, 218, 392, targetDimensions, ref assertions, ref passed, ref lodPassed);

                if (!report.lod0.passed || !report.lod1.passed || !report.lod2.passed)
                {
                    lodPassed = false;
                }
            }
            catch (Exception ex)
            {
                lodPassed = false;
                Debug.LogError($"[TinderValidation] LOD geometry validation failed: {ex.Message}");
            }
            report.lodMetricsPassed = lodPassed;

            // 3. Exact target envelope comparison across all LODs (strictly removing 1.25x relaxation)
            bool boundsTargetPassed = true;
            bool allLodsWithinBounds = report.lod0.boundsWithinTarget &&
                                       report.lod1.boundsWithinTarget &&
                                       report.lod2.boundsWithinTarget;
            AssertTrue(allLodsWithinBounds,
                $"All LODs fit exact declared target envelope ({targetDimensions.x:F3}m x {targetDimensions.y:F3}m x {targetDimensions.z:F3}m) +/- {BoundsEpsilonMetres:0.#####}m without relaxation",
                ref assertions, ref passed, ref boundsTargetPassed);
            report.boundsTargetCheckPassed = allLodsWithinBounds && boundsTargetPassed;

            // 4. Determinism check: verify identical generation across multiple runs on same platform
            bool determinismPassed = true;
            Mesh meshA = null;
            Mesh meshB = null;
            try
            {
                meshA = TinderGeometry.GenerateMesh(TinderParameters.Default);
                meshB = TinderGeometry.GenerateMesh(TinderParameters.Default);

                bool countsMatch = meshA != null && meshB != null &&
                                   meshA.vertexCount == meshB.vertexCount &&
                                   meshA.triangles.Length == meshB.triangles.Length;
                AssertTrue(countsMatch, "Determinism: vertex and triangle counts match across repeated generations", ref assertions, ref passed, ref determinismPassed);

                if (countsMatch)
                {
                    var va = meshA.vertices;
                    var vb = meshB.vertices;
                    bool vertsEqual = true;
                    for (int i = 0; i < va.Length; i++)
                    {
                        if (va[i] != vb[i])
                        {
                            vertsEqual = false;
                            break;
                        }
                    }
                    AssertTrue(vertsEqual, "Determinism: all vertex positions are identical", ref assertions, ref passed, ref determinismPassed);

                    var na = meshA.normals;
                    var nb = meshB.normals;
                    bool normalsEqual = na != null && nb != null && na.Length == nb.Length;
                    if (normalsEqual)
                    {
                        for (int i = 0; i < na.Length; i++)
                        {
                            if (na[i] != nb[i])
                            {
                                normalsEqual = false;
                                break;
                            }
                        }
                    }
                    AssertTrue(normalsEqual, "Determinism: all vertex normals are identical", ref assertions, ref passed, ref determinismPassed);

                    var ca = meshA.colors;
                    var cb = meshB.colors;
                    bool colorsEqual = ca != null && cb != null && ca.Length == cb.Length;
                    if (colorsEqual)
                    {
                        for (int i = 0; i < ca.Length; i++)
                        {
                            if (ca[i] != cb[i])
                            {
                                colorsEqual = false;
                                break;
                            }
                        }
                    }
                    AssertTrue(colorsEqual, "Determinism: all vertex colors are identical", ref assertions, ref passed, ref determinismPassed);

                    var ta = meshA.triangles;
                    var tb = meshB.triangles;
                    bool trisEqual = ta != null && tb != null && ta.Length == tb.Length;
                    if (trisEqual)
                    {
                        for (int i = 0; i < ta.Length; i++)
                        {
                            if (ta[i] != tb[i])
                            {
                                trisEqual = false;
                                break;
                            }
                        }
                    }
                    AssertTrue(trisEqual, "Determinism: all triangle indices are identical", ref assertions, ref passed, ref determinismPassed);
                }
            }
            catch (Exception ex)
            {
                determinismPassed = false;
                Debug.LogError($"[TinderValidation] Determinism check threw exception: {ex.Message}");
            }
            finally
            {
                SafeDestroy(meshA);
                SafeDestroy(meshB);
            }
            report.determinismCheckPassed = determinismPassed;

            // 5. Shader asset availability, custom identity, and supported status check
            bool shaderPassed = true;
            const string expectedShaderName = "CityLife/Fire/TinderVertexColor";
            Shader vShader = Shader.Find(expectedShaderName);
            AssertTrue(vShader != null, $"Shader '{expectedShaderName}' found via Shader.Find", ref assertions, ref passed, ref shaderPassed);

            if (vShader != null)
            {
                bool identityMatches = vShader.name == expectedShaderName;
                AssertTrue(identityMatches, $"Shader identity matches '{expectedShaderName}' (got '{vShader.name}')", ref assertions, ref passed, ref shaderPassed);

                bool isSupported = vShader.isSupported;
                AssertTrue(isSupported, $"Shader '{expectedShaderName}' isSupported == true on current graphics device", ref assertions, ref passed, ref shaderPassed);
            }

            Material vMat = null;
            try
            {
                vMat = TinderGeometry.CreateVertexColorMaterial();
                bool matCreated = vMat != null;
                AssertTrue(matCreated, "CreateVertexColorMaterial returns valid Material", ref assertions, ref passed, ref shaderPassed);
                if (vMat != null)
                {
                    bool matShaderMatches = vMat.shader != null && vMat.shader.name == expectedShaderName;
                    AssertTrue(matShaderMatches, $"Material shader matches '{expectedShaderName}'", ref assertions, ref passed, ref shaderPassed);
                }
            }
            catch (Exception ex)
            {
                shaderPassed = false;
                Debug.LogError($"[TinderValidation] Material helper check threw exception: {ex.Message}");
            }
            finally
            {
                SafeDestroy(vMat);
            }
            report.shaderAssetCheckPassed = shaderPassed;

            // Final aggregate evaluation: requires all explicit categories AND the invariant (total > 0 && passed == total)
            report.totalAssertions = assertions;
            report.passedAssertions = passed;

            bool allExplicitPassed = report.parameterValidationPassed &&
                                     report.lodMetricsPassed &&
                                     report.boundsTargetCheckPassed &&
                                     report.determinismCheckPassed &&
                                     report.shaderAssetCheckPassed &&
                                     report.lod0.passed &&
                                     report.lod1.passed &&
                                     report.lod2.passed;

            bool invariantPassed = (report.totalAssertions > 0) && (report.passedAssertions == report.totalAssertions);

            report.status = (allExplicitPassed && invariantPassed) ? "PASSED" : "FAILED";
            report.summary = string.Format(CultureInfo.InvariantCulture,
                "Tinder Validation {0}: {1}/{2} assertions passed. LOD0: {3}v/{4}t (target bounds pass: {5}), LOD1: {6}v/{7}t (pass: {8}), LOD2: {9}v/{10}t (pass: {11}). Shader: {12}. Notice: actual rendered palette remains a later Unity acceptance gate, not proven by Shader.Find alone.",
                report.status, passed, assertions,
                report.lod0.actualVertices, report.lod0.actualTriangles, report.lod0.boundsWithinTarget,
                report.lod1.actualVertices, report.lod1.actualTriangles, report.lod1.boundsWithinTarget,
                report.lod2.actualVertices, report.lod2.actualTriangles, report.lod2.boundsWithinTarget,
                report.shaderAssetCheckPassed ? "VERIFIED" : "FAILED");

            return report;
        }

        private static LodMetricReport ValidateLodLevel(int lod, int expectedVerts, int expectedTris, Vector3 targetDimensions, ref int assertions, ref int passed, ref bool scopeFlag)
        {
            var p = TinderParameters.ForLod(lod);
            TinderGeometry.CalculateWorkload(p, out int calculatedVerts, out int calculatedTris);

            var metric = new LodMetricReport
            {
                lodLevel = lod,
                expectedVertices = expectedVerts,
                expectedTriangles = expectedTris,
                calculatedVertices = calculatedVerts,
                calculatedTriangles = calculatedTris
            };

            AssertTrue(calculatedVerts == expectedVerts, $"LOD{lod} calculated vertices ({calculatedVerts}) == expected ({expectedVerts})", ref assertions, ref passed, ref scopeFlag);
            AssertTrue(calculatedTris == expectedTris, $"LOD{lod} calculated triangles ({calculatedTris}) == expected ({expectedTris})", ref assertions, ref passed, ref scopeFlag);

            Mesh mesh = null;
            try
            {
                mesh = TinderGeometry.GenerateMesh(p);
                bool meshNonNull = mesh != null;
                AssertTrue(meshNonNull, $"LOD{lod} mesh generated non-null", ref assertions, ref passed, ref scopeFlag);

                if (meshNonNull)
                {
                    metric.actualVertices = mesh.vertexCount;
                    metric.actualTriangles = mesh.triangles != null ? mesh.triangles.Length / 3 : 0;

                    AssertTrue(metric.actualVertices > 0, $"LOD{lod} mesh has non-empty vertices", ref assertions, ref passed, ref scopeFlag);
                    AssertTrue(metric.actualTriangles > 0, $"LOD{lod} mesh has non-empty triangles", ref assertions, ref passed, ref scopeFlag);
                    AssertTrue(metric.actualVertices == expectedVerts, $"LOD{lod} actual vertices ({metric.actualVertices}) == expected ({expectedVerts})", ref assertions, ref passed, ref scopeFlag);
                    AssertTrue(metric.actualTriangles == expectedTris, $"LOD{lod} actual triangles ({metric.actualTriangles}) == expected ({expectedTris})", ref assertions, ref passed, ref scopeFlag);
                    AssertTrue(metric.actualVertices == calculatedVerts, $"LOD{lod} actual vertices ({metric.actualVertices}) == calculated ({calculatedVerts})", ref assertions, ref passed, ref scopeFlag);
                    AssertTrue(metric.actualTriangles == calculatedTris, $"LOD{lod} actual triangles ({metric.actualTriangles}) == calculated ({calculatedTris})", ref assertions, ref passed, ref scopeFlag);

                    Bounds bounds = TinderGeometry.MeasureMeshBounds(mesh);
                    metric.boundsMin = bounds.min;
                    metric.boundsMax = bounds.max;
                    metric.boundsSize = bounds.size;
                    metric.boundsCenter = bounds.center;

                    bool boundsFinite = IsFinite(bounds.min) && IsFinite(bounds.max) && IsFinite(bounds.size) && IsFinite(bounds.center);
                    metric.boundsFinite = boundsFinite;
                    AssertTrue(boundsFinite, $"LOD{lod} measured mesh bounds are finite", ref assertions, ref passed, ref scopeFlag);

                    bool boundsPositive = bounds.size.x > 0f && bounds.size.y > 0f && bounds.size.z > 0f;
                    AssertTrue(boundsPositive, $"LOD{lod} measured mesh bounds dimensions are strictly positive", ref assertions, ref passed, ref scopeFlag);

                    // Ground alignment: minimum Y must rest at 0.00m (+/- GroundEpsilonMetres)
                    bool groundAligned = Mathf.Abs(bounds.min.y) <= GroundEpsilonMetres;
                    metric.groundAligned = groundAligned;
                    AssertTrue(groundAligned, $"LOD{lod} ground contact at Y = 0.00m (+/- {GroundEpsilonMetres:0.#####}m, actual min Y = {bounds.min.y:0.#####}m)", ref assertions, ref passed, ref scopeFlag);

                    // Pivot alignment: horizontal center X, Z should be close to 0 (+/- PivotEpsilonMetres)
                    bool pivotAligned = Mathf.Abs(bounds.center.x) <= PivotEpsilonMetres && Mathf.Abs(bounds.center.z) <= PivotEpsilonMetres;
                    metric.pivotAligned = pivotAligned;
                    AssertTrue(pivotAligned, $"LOD{lod} horizontal center within pivot tolerance (+/- {PivotEpsilonMetres:0.#####}m, actual X={bounds.center.x:0.#####}m, Z={bounds.center.z:0.#####}m)", ref assertions, ref passed, ref scopeFlag);

                    // Target envelope comparison against exact declared target envelope with documented small epsilon
                    bool withinEnvelopeX = bounds.size.x <= targetDimensions.x + BoundsEpsilonMetres;
                    bool withinEnvelopeY = bounds.size.y <= targetDimensions.y + BoundsEpsilonMetres;
                    bool withinEnvelopeZ = bounds.size.z <= targetDimensions.z + BoundsEpsilonMetres;
                    bool withinTargetEnvelope = withinEnvelopeX && withinEnvelopeY && withinEnvelopeZ;
                    metric.boundsWithinTarget = withinTargetEnvelope;
                    AssertTrue(withinTargetEnvelope,
                        $"LOD{lod} bounds size ({bounds.size.x:F3}m, {bounds.size.y:F3}m, {bounds.size.z:F3}m) <= exact target envelope ({targetDimensions.x:F3}m, {targetDimensions.y:F3}m, {targetDimensions.z:F3}m) + {BoundsEpsilonMetres:0.#####}m epsilon",
                        ref assertions, ref passed, ref scopeFlag);

                    // Vertex colors populated & valid
                    Color[] colors = mesh.colors;
                    bool colorsPopulated = colors != null && colors.Length == metric.actualVertices && metric.actualVertices > 0;
                    metric.vertexColorsPopulated = colorsPopulated;
                    AssertTrue(colorsPopulated, $"LOD{lod} vertex colors populated matching vertex count ({metric.actualVertices})", ref assertions, ref passed, ref scopeFlag);

                    if (colorsPopulated)
                    {
                        bool channelsFinite = true;
                        for (int i = 0; i < colors.Length; i++)
                        {
                            if (!IsFinite(colors[i].r) || !IsFinite(colors[i].g) || !IsFinite(colors[i].b) || !IsFinite(colors[i].a))
                            {
                                channelsFinite = false;
                                break;
                            }
                        }
                        AssertTrue(channelsFinite, $"LOD{lod} vertex color channels are all finite", ref assertions, ref passed, ref scopeFlag);
                    }

                    metric.passed = (metric.actualVertices == expectedVerts) &&
                                    (metric.actualTriangles == expectedTris) &&
                                    (calculatedVerts == expectedVerts) &&
                                    (calculatedTris == expectedTris) &&
                                    boundsFinite && boundsPositive &&
                                    groundAligned &&
                                    pivotAligned &&
                                    withinTargetEnvelope &&
                                    colorsPopulated;
                }
                else
                {
                    metric.passed = false;
                    scopeFlag = false;
                }
            }
            finally
            {
                SafeDestroy(mesh);
            }

            return metric;
        }

        /// <summary>
        /// Explicit failure-path verification suite for the aggregation engine itself.
        /// Verifies that no-throw, wrong-throw, missing shader, bad pivot, bad count, bad bounds,
        /// and deliberately failed assertion each cause a FAILED status and nonzero exit decision.
        /// Kept strictly distinct from production RunValidation.
        /// </summary>
        public static SelfTestReport RunAggregationSelfTests()
        {
            var report = new SelfTestReport();
            int selfTests = 0;
            int passed = 0;

            // 1. Failure-path check: no-throw (action expected to throw does not throw)
            {
                selfTests++;
                int a = 0, p = 0;
                bool scope = true;
                bool throwResult = AssertThrows<ArgumentOutOfRangeException>(() => { /* intentionally no throw */ }, "self-test no-throw canary", ref a, ref p, ref scope);
                var mock = CreateMockPassingReport();
                mock.totalAssertions += a;
                mock.passedAssertions += p;
                mock.parameterValidationPassed = scope;
                bool mockPassed = EvaluateAggregationDecision(mock, out int exitCode);
                bool testPassed = (!throwResult) && (!scope) && (p == 0) && (!mockPassed) && (exitCode != 0);
                if (testPassed) passed++;
                report.noThrowHandledCorrectly = testPassed;
            }

            // 2. Failure-path check: wrong-throw (action throws wrong exception type)
            {
                selfTests++;
                int a = 0, p = 0;
                bool scope = true;
                bool throwResult = AssertThrows<ArgumentOutOfRangeException>(() => throw new InvalidOperationException("wrong exception canary"), "self-test wrong-throw canary", ref a, ref p, ref scope);
                var mock = CreateMockPassingReport();
                mock.totalAssertions += a;
                mock.passedAssertions += p;
                mock.parameterValidationPassed = scope;
                bool mockPassed = EvaluateAggregationDecision(mock, out int exitCode);
                bool testPassed = (!throwResult) && (!scope) && (p == 0) && (!mockPassed) && (exitCode != 0);
                if (testPassed) passed++;
                report.wrongThrowHandledCorrectly = testPassed;
            }

            // 3. Failure-path check: missing shader (non-existent shader lookup)
            {
                selfTests++;
                int a = 0, p = 0;
                bool scope = true;
                Shader missingShader = Shader.Find("CityLife/Fire/NonExistentShaderTestCanary");
                AssertTrue(missingShader != null, "self-test missing shader canary", ref a, ref p, ref scope);
                var mock = CreateMockPassingReport();
                mock.totalAssertions += a;
                mock.passedAssertions += p;
                mock.shaderAssetCheckPassed = scope;
                bool mockPassed = EvaluateAggregationDecision(mock, out int exitCode);
                bool testPassed = (!scope) && (!mockPassed) && (exitCode != 0);
                if (testPassed) passed++;
                report.missingShaderHandledCorrectly = testPassed;
            }

            // 4. Failure-path check: bad pivot (pivot offset exceeds tolerance)
            {
                selfTests++;
                int a = 0, p = 0;
                bool scope = true;
                Vector3 badCenter = new Vector3(0.50f, 0.05f, 0.50f);
                bool pivotOk = Mathf.Abs(badCenter.x) <= PivotEpsilonMetres && Mathf.Abs(badCenter.z) <= PivotEpsilonMetres;
                AssertTrue(pivotOk, "self-test bad pivot canary", ref a, ref p, ref scope);
                var mock = CreateMockPassingReport();
                mock.totalAssertions += a;
                mock.passedAssertions += p;
                mock.lod0.pivotAligned = pivotOk;
                mock.lod0.passed = false;
                mock.lodMetricsPassed = false;
                bool mockPassed = EvaluateAggregationDecision(mock, out int exitCode);
                bool testPassed = (!pivotOk) && (!scope) && (!mockPassed) && (exitCode != 0);
                if (testPassed) passed++;
                report.badPivotHandledCorrectly = testPassed;
            }

            // 5. Failure-path check: bad count (calculated or actual counts mismatch)
            {
                selfTests++;
                int a = 0, p = 0;
                bool scope = true;
                int actualVerts = 999;
                int expectedVerts = 1026;
                AssertTrue(actualVerts == expectedVerts, "self-test bad count canary", ref a, ref p, ref scope);
                var mock = CreateMockPassingReport();
                mock.totalAssertions += a;
                mock.passedAssertions += p;
                mock.lod0.actualVertices = actualVerts;
                mock.lod0.passed = false;
                mock.lodMetricsPassed = false;
                bool mockPassed = EvaluateAggregationDecision(mock, out int exitCode);
                bool testPassed = (!scope) && (!mockPassed) && (exitCode != 0);
                if (testPassed) passed++;
                report.badCountHandledCorrectly = testPassed;
            }

            // 6. Failure-path check: bad bounds (bounds exceed declared target envelope)
            {
                selfTests++;
                int a = 0, p = 0;
                bool scope = true;
                Vector3 badSize = new Vector3(1.50f, 0.50f, 1.50f);
                Vector3 target = TinderMetadata.TargetDimensionsMetres;
                bool boundsOk = badSize.x <= target.x + BoundsEpsilonMetres &&
                                badSize.y <= target.y + BoundsEpsilonMetres &&
                                badSize.z <= target.z + BoundsEpsilonMetres;
                AssertTrue(boundsOk, "self-test bad bounds canary", ref a, ref p, ref scope);
                var mock = CreateMockPassingReport();
                mock.totalAssertions += a;
                mock.passedAssertions += p;
                mock.lod0.boundsSize = badSize;
                mock.lod0.boundsWithinTarget = boundsOk;
                mock.lod0.passed = false;
                mock.boundsTargetCheckPassed = false;
                bool mockPassed = EvaluateAggregationDecision(mock, out int exitCode);
                bool testPassed = (!boundsOk) && (!scope) && (!mockPassed) && (exitCode != 0);
                if (testPassed) passed++;
                report.badBoundsHandledCorrectly = testPassed;
            }

            // 7. Failure-path check: deliberately failed assertion
            {
                selfTests++;
                int a = 0, p = 0;
                bool scope = true;
                AssertTrue(false, "self-test deliberate failure canary", ref a, ref p, ref scope);
                var mock = CreateMockPassingReport();
                mock.totalAssertions += a;
                mock.passedAssertions += p;
                bool mockPassed = EvaluateAggregationDecision(mock, out int exitCode);
                bool testPassed = (!scope) && (p < a) && (!mockPassed) && (exitCode != 0);
                if (testPassed) passed++;
                report.deliberateFailureHandledCorrectly = testPassed;
            }

            report.totalSelfTests = selfTests;
            report.passedSelfTests = passed;
            report.status = (selfTests > 0 && passed == selfTests) ? "PASSED" : "FAILED";
            report.summary = string.Format(CultureInfo.InvariantCulture,
                "Aggregation Self-Tests {0}: {1}/{2} failure paths verified. Handled correctly: no-throw={3}, wrong-throw={4}, missing-shader={5}, bad-pivot={6}, bad-count={7}, bad-bounds={8}, deliberate-failure={9}.",
                report.status, passed, selfTests,
                report.noThrowHandledCorrectly, report.wrongThrowHandledCorrectly,
                report.missingShaderHandledCorrectly, report.badPivotHandledCorrectly,
                report.badCountHandledCorrectly, report.badBoundsHandledCorrectly,
                report.deliberateFailureHandledCorrectly);

            return report;
        }

        private static ValidationReport CreateMockPassingReport()
        {
            return new ValidationReport
            {
                schema = "citylife.fire.tinder-validation.v1",
                status = "PASSED",
                totalAssertions = 10,
                passedAssertions = 10,
                parameterValidationPassed = true,
                lodMetricsPassed = true,
                boundsTargetCheckPassed = true,
                determinismCheckPassed = true,
                shaderAssetCheckPassed = true,
                lod0 = new LodMetricReport { passed = true, boundsWithinTarget = true, groundAligned = true, pivotAligned = true, vertexColorsPopulated = true },
                lod1 = new LodMetricReport { passed = true, boundsWithinTarget = true, groundAligned = true, pivotAligned = true, vertexColorsPopulated = true },
                lod2 = new LodMetricReport { passed = true, boundsWithinTarget = true, groundAligned = true, pivotAligned = true, vertexColorsPopulated = true }
            };
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("CityLife/Fire/Validate Tinder Geometry (Source Check)")]
        public static void RunFromEditorMenu()
        {
            var report = RunValidation();
            Debug.Log($"[TinderValidation] Result: {report.summary}");
        }

        [UnityEditor.MenuItem("CityLife/Fire/Run Tinder Validation Self-Tests (Aggregation Check)")]
        public static void RunSelfTestsFromEditorMenu()
        {
            var report = RunAggregationSelfTests();
            Debug.Log($"[TinderValidation] Self-Tests Result: {report.summary}");
        }

        public static void RunCommandLine()
        {
            try
            {
                var report = RunValidation();
                string json = JsonUtility.ToJson(report, true);
                Debug.Log($"[TinderValidation] CommandLine Result:\n{json}");
                bool passed = EvaluateAggregationDecision(report, out int exitCode);
                if (!passed || exitCode != 0)
                {
                    UnityEditor.EditorApplication.Exit(1);
                }
                else
                {
                    UnityEditor.EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TinderValidation] CommandLine fatal exception: {ex}");
                UnityEditor.EditorApplication.Exit(1);
            }
        }

        public static void RunSelfTestsCommandLine()
        {
            try
            {
                var report = RunAggregationSelfTests();
                string json = JsonUtility.ToJson(report, true);
                Debug.Log($"[TinderValidation] Self-Tests CommandLine Result:\n{json}");
                if (report == null || report.status != "PASSED" || report.passedSelfTests != report.totalSelfTests || report.totalSelfTests == 0)
                {
                    UnityEditor.EditorApplication.Exit(1);
                }
                else
                {
                    UnityEditor.EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TinderValidation] Self-Tests CommandLine fatal exception: {ex}");
                UnityEditor.EditorApplication.Exit(1);
            }
        }
#endif
    }
}
