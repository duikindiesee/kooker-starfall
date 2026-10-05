#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World.Editor
{
    /// <summary>
    /// STAGED ONLY: combines an original-scale PH02 crown and its measured R13 fitted support.
    /// No scene, generation configuration, material mutation, renderer or asset write is performed.
    /// The compound surface uses one material and one submesh; crown/support spans remain explicit.
    /// </summary>
    public static class PH02FamilyComponent
    {
        const float Cut = .65f, Transition = .25f, Extension = .18f, RoundScale = 1.12f, BottomScale = 1.12f;
        static readonly Vector2 Bend = new Vector2(.045f, .020f);

        public sealed class Result : IDisposable
        {
            public Mesh Component;
            public float BottomRadiusMetres;
            public Report Metrics;
            public void Dispose()
            {
                if (Component != null) UnityEngine.Object.DestroyImmediate(Component);
                Component = null;
            }
        }
        public sealed class CrownData
        {
            public Vector3[] Positions, Normals;
            public Vector2[] Uvs;
            public Vector4[] Tangents;
            public int[] Triangles;
        }
        public sealed class ComponentData
        {
            public Vector3[] Positions, Normals;
            public Vector2[] AtlasUv, BranchUv;
            public Vector4[] Tangents;
            public Color[] Weights;
            public int[] Triangles;
            public Report Metrics;
        }

        /// <summary>
        /// Caller owns only Result.Component. Temporary input, owned fit clone and support
        /// are destroyed on success or failure. Imported mode must pass its numeric identity/
        /// topology gates before a fitted support or compound mesh is constructed.
        /// </summary>
        public static Result Create(bool useImportedTuples = false)
        {
            return Create(useImportedTuples, out _);
        }

        /// <summary>The report is assigned before work, including the failed stage and any
        /// completed adapter/fit diagnostics when a later gate throws.</summary>
        public static Result Create(bool useImportedTuples, out Report report)
        {
            report = new Report { importedTupleMode = useImportedTuples, buildStage = "source crown" };
            Mesh input = null;
            PH02FittedSupportCandidate.Result fit = null;
            var result = new Result();
            PH02ImportedCrownCandidate.Report imported = null;
            try
            {
                if (useImportedTuples)
                {
                    input = PH02ImportedCrownCandidate.Create(Cut, out imported, true);
                    Require(ImportedGate(imported), "Actual imported crown did not pass numeric identity/tuple/topology gates.");
                }
                else input = PH02CrownCandidate.CreateCrown(Cut);

                string before = HashCrown(ReadCrown(input));
                report.buildStage = "fitted support";
                var familyOptions=R13Options();
                familyOptions.flatLowerTangent=true;
                fit = PH02FittedSupportCandidate.Create(input, Cut, familyOptions);
                Require(fit.Metrics.numericChecksPassed && fit.Metrics.crownUnchanged &&
                    fit.Metrics.callerCloneUsed && fit.Metrics.callerCrownUnchanged && fit.Metrics.cloneMatchesCaller,
                    "Fitted support numeric or caller-clone preservation checks failed.");
                var support = new PH02FittedSupportCandidate.SupportData
                {
                    Positions = fit.Support.vertices, Normals = fit.Support.normals,
                    AtlasUv = fit.Support.uv, BranchUv = fit.Support.uv2,
                    Tangents = fit.Support.tangents, BlendWeights = fit.Support.colors,
                    Triangles = fit.Support.triangles, Metrics = fit.Metrics
                };
                report.buildStage = "compound buffers";
                ComponentData data = BuildData(ReadCrown(fit.Crown), support);
                report = data.Metrics;
                data.Metrics.importedTupleMode = useImportedTuples;
                data.Metrics.importedChecks = imported;
                data.Metrics.actualImportedGatePassed = useImportedTuples && ImportedGate(imported);
                data.Metrics.callerMeshSha256Before = before;
                data.Metrics.callerMeshSha256After = HashCrown(ReadCrown(input));
                Require(before == data.Metrics.callerMeshSha256After, "Caller input was changed.");

                report.buildStage = "compound Mesh";
                result.Component = new Mesh { name = "PH02 compound crown and R13 fitted support - family inspection only", indexFormat = data.Positions.Length <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32 };
                result.Component.vertices = data.Positions; result.Component.normals = data.Normals;
                result.Component.uv = data.AtlasUv; result.Component.uv2 = data.BranchUv;
                result.Component.tangents = data.Tangents; result.Component.colors = data.Weights;
                result.Component.triangles = data.Triangles; result.Component.RecalculateBounds();
                // Do not recalculate normals/tangents or weld source/support attribute seams.
                result.BottomRadiusMetres = data.Metrics.bottomRadiusMetres;
                result.Metrics = data.Metrics; result.Metrics.actualMeshCreated = true;
                report.buildStage = "complete";
                return result;
            }
            catch (Exception e) { report.failure = e.GetType().Name + ": " + e.Message; result.Dispose(); throw; }
            finally
            {
                report.importedChecks = imported;
                if (fit != null) report.fittedChecks = fit.Metrics;
                if (fit != null) fit.Dispose();
                if (input != null) UnityEngine.Object.DestroyImmediate(input);
            }
        }

        /// <summary>
        /// Pure managed construction. Does not claim these buffers came from an actual imported
        /// mesh. No source buffer is changed. Uses the exact R13 measured frame and support scale.
        /// </summary>
        public static ComponentData BuildData(CrownData crown, PH02FittedSupportCandidate.SupportData support)
        {
            Require(crown != null && support != null && support.Metrics != null, "Missing compound inputs.");
            var fit = support.Metrics;
            Require(fit.numericChecksPassed && fit.sourceTangentsAvailable && fit.ringOrderedConnectivity && fit.allCutEdgesReversed,
                "Support buffers have not passed their numeric geometry checks.");
            Require(fit.cutHeightMetres == Cut && fit.transitionMetres == Transition && fit.lowerExtensionMetres == Extension &&
                fit.lowerBendMetres.Equals(Bend) && fit.ringCount == 25 && fit.rimSegments == 69 && fit.rimComponents == 1,
                "This component requires the unchanged R13 .65 cut and .25/.18m, 16/8-ring support settings.");
            Require(Math.Abs(fit.roundRadiusMetres - fit.sourceMeanRimRadius * RoundScale) < 1e-7f, "Unexpected R13 radius scale.");
            ValidateArrays(crown.Positions, crown.Normals, crown.Uvs, crown.Tangents, crown.Triangles);
            ValidateArrays(support.Positions, support.Normals, support.AtlasUv, support.Tangents, support.Triangles);
            Require(support.BranchUv.Length == support.Positions.Length && support.BlendWeights.Length == support.Positions.Length, "Missing support UV1/blend weights.");

            var report = new Report
            {
                fittedChecks = fit,
                sourceLowerCentre = fit.lowerCentre, sourceRimCentre = fit.rimCentre,
                transitionMetres = Transition, lowerExtensionMetres = Extension,
                roundRadiusMetres = fit.roundRadiusMetres, bottomRadiusScale = BottomScale,
                bottomRadiusMetres = fit.roundRadiusMetres * BottomScale,
                crownVertices = crown.Positions.Length, supportVertices = support.Positions.Length,
                crownTriangles = crown.Triangles.Length / 3, supportTriangles = support.Triangles.Length / 3,
                crownInputSha256Before = HashCrown(crown), supportInputSha256Before = HashSupport(support)
            };
            float length = Transition + Extension;
            Vector3 axis = new Vector3(-2 * Bend.x / length, 1, -2 * Bend.y / length).normalized;
            Quaternion rotation = UpRotation(axis);
            report.sourceLowerAxis = axis; report.componentRotation = rotation;
            report.transformedAxisError = ((rotation * axis) - Vector3.up).magnitude;
            report.rotationDeterminant = Vector3.Dot(rotation * Vector3.right, Vector3.Cross(rotation * Vector3.up, rotation * Vector3.forward));
            Require(report.transformedAxisError < 1e-6f && Math.Abs(report.rotationDeterminant - 1f) < 1e-6f, "Rotation is not a proper +Y alignment.");

            int count = crown.Positions.Length + support.Positions.Length;
            var data = new ComponentData
            {
                Positions = new Vector3[count], Normals = new Vector3[count],
                AtlasUv = new Vector2[count], BranchUv = new Vector2[count],
                Tangents = new Vector4[count], Weights = new Color[count],
                Triangles = new int[crown.Triangles.Length + support.Triangles.Length], Metrics = report
            };
            Quaternion inverse = new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w);
            for (int i = 0; i < count; i++)
            {
                bool isCrown = i < crown.Positions.Length; int j = isCrown ? i : i - crown.Positions.Length;
                Vector3 p = isCrown ? crown.Positions[j] : support.Positions[j];
                Vector3 n = isCrown ? crown.Normals[j] : support.Normals[j];
                Vector4 tangent = isCrown ? crown.Tangents[j] : support.Tangents[j];
                Vector3 t = new Vector3(tangent.x, tangent.y, tangent.z);
                data.Positions[i] = rotation * (p - fit.lowerCentre);
                data.Normals[i] = rotation * n;
                Vector3 rotatedTangent = rotation * t;
                data.Tangents[i] = new Vector4(rotatedTangent.x, rotatedTangent.y, rotatedTangent.z, tangent.w);
                data.AtlasUv[i] = isCrown ? crown.Uvs[j] : support.AtlasUv[j];
                data.BranchUv[i] = isCrown ? Vector2.zero : support.BranchUv[j];
                if (isCrown)
                {
                    float radial = new Vector2(p.x - fit.rimCentre.x, p.z - fit.rimCentre.z).magnitude;
                    float weight = Clamp(Smooth(.10f, .18f, radial) * Smooth(.65f, .80f, p.y) + Smooth(.92f, 1.04f, p.y));
                    data.Weights[i] = new Color(0, weight, 0, 1);
                    if (weight > 0) report.crownVerticesWithFoliageWeight++;
                }
                else
                {
                    Color weight = support.BlendWeights[j];
                    Require(Finite(weight.r) && weight.r >= 0 && weight.r <= 1, "Invalid support bark-blend weight.");
                    data.Weights[i] = new Color(weight.r, 0, weight.b, weight.a);
                    if (Math.Abs(support.BranchUv[j].y - length) < 1e-6f)
                    {
                        report.bottomRingAttributeVertices++;
                        float radius = (p - fit.lowerCentre).magnitude;
                        report.minimumBottomRadius = Math.Min(report.minimumBottomRadius, radius);
                        report.maximumBottomRadius = Math.Max(report.maximumBottomRadius, radius);
                        report.maximumBottomRadiusError = Math.Max(report.maximumBottomRadiusError, Math.Abs(radius - report.bottomRadiusMetres));
                        report.maximumBottomPlaneError = Math.Max(report.maximumBottomPlaneError, Math.Abs(data.Positions[i].y));
                    }
                }
                report.maximumPositionRoundTripError = Math.Max(report.maximumPositionRoundTripError, ((inverse * data.Positions[i]) + fit.lowerCentre - p).magnitude);
                report.maximumNormalRoundTripError = Math.Max(report.maximumNormalRoundTripError, ((inverse * data.Normals[i]) - n).magnitude);
                report.maximumTangentRoundTripError = Math.Max(report.maximumTangentRoundTripError, ((inverse * rotatedTangent) - t).magnitude);
                report.maximumPivotDistanceError = Math.Max(report.maximumPivotDistanceError, Math.Abs(data.Positions[i].magnitude - (p - fit.lowerCentre).magnitude));
                Require(Finite(data.Positions[i]) && Finite(data.Normals[i]) && Finite(rotatedTangent), "Nonfinite compound attribute.");
                Require(Math.Abs(data.Normals[i].magnitude - n.magnitude) < 1e-6f && Math.Abs(rotatedTangent.magnitude - t.magnitude) < 1e-6f, "Rigid rotation changed normal/tangent length.");
            }
            Array.Copy(crown.Triangles, data.Triangles, crown.Triangles.Length);
            for (int i = 0; i < support.Triangles.Length; i++) data.Triangles[crown.Triangles.Length + i] = support.Triangles[i] + crown.Positions.Length;
            report.crownFirstIndex = 0; report.crownIndexCount = crown.Triangles.Length;
            report.supportFirstIndex = crown.Triangles.Length; report.supportIndexCount = support.Triangles.Length;
            for (int i = 0; i < data.Triangles.Length; i += 3)
            {
                Vector3 a = data.Positions[data.Triangles[i]], b = data.Positions[data.Triangles[i + 1]], c = data.Positions[data.Triangles[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                double area = .5 * Math.Sqrt((double)cross.x * cross.x + (double)cross.y * cross.y + (double)cross.z * cross.z);
                Require(area > 0 && !double.IsNaN(area) && !double.IsInfinity(area), "Compound contains a degenerate triangle.");
                report.minimumTriangleArea = Math.Min(report.minimumTriangleArea, area);
            }
            Require(report.bottomRingAttributeVertices >= 69 && report.maximumBottomRadiusError < 2e-6f &&
                report.maximumBottomPlaneError < 2e-6f, "Measured bottom does not agree with the R13 radius/frame.");
            Require(report.maximumPositionRoundTripError < 2e-6f && report.maximumNormalRoundTripError < 1e-6f &&
                report.maximumTangentRoundTripError < 1e-6f && report.maximumPivotDistanceError < 2e-6f, "Compound rigid transform exceeded float tolerances.");
            report.crownInputSha256After = HashCrown(crown); report.supportInputSha256After = HashSupport(support);
            report.inputsUnchanged = report.crownInputSha256Before == report.crownInputSha256After && report.supportInputSha256Before == report.supportInputSha256After;
            Require(report.inputsUnchanged, "Input buffers changed during combination.");
            report.componentSha256 = HashComponent(data);
            report.componentVertices = count; report.componentTriangles = data.Triangles.Length / 3;
            report.numericChecksPassed = true;
            return data;
        }

        static PH02FittedSupportCandidate.Options R13Options() => new PH02FittedSupportCandidate.Options
        {
            transitionMetres = Transition, lowerExtensionMetres = Extension,
            roundRadiusScale = RoundScale, bottomRadiusScale = BottomScale,
            lowerBendMetres = Bend, transitionSegments = 16, lowerSegments = 8
        };
        static bool ImportedGate(PH02ImportedCrownCandidate.Report r) => r != null && r.actualUnityImportedData && r.meshCreated &&
            r.mappingComplete && r.matchedSourceTriangles == 82074 && r.unmatchedSourceTriangles == 0 && r.ambiguousSourceTriangles == 0 &&
            r.exactTupleDedup && r.exactExpandedTuplePreservation && r.outputTriangles == 71207 &&
            r.boundaryEdges == 69 && r.boundaryPositions == 69 && r.boundaryComponents == 1 && r.boundaryAllDegreeTwo;
        static CrownData ReadCrown(Mesh mesh) => new CrownData { Positions = mesh.vertices, Normals = mesh.normals, Uvs = mesh.uv, Tangents = mesh.tangents, Triangles = mesh.triangles };
        static Quaternion UpRotation(Vector3 from)
        {
            // Pure managed shortest-arc quaternion; no native FromToRotation call in CPU checks.
            Vector3 cross = Vector3.Cross(from, Vector3.up); float w = 1f + from.y;
            double norm = Math.Sqrt((double)cross.x * cross.x + (double)cross.y * cross.y + (double)cross.z * cross.z + (double)w * w);
            Require(norm > 1e-12, "Lower axis is opposite +Y.");
            return new Quaternion((float)(cross.x / norm), (float)(cross.y / norm), (float)(cross.z / norm), (float)(w / norm));
        }
        static float Clamp(float t) => Math.Max(0, Math.Min(1, t));
        static float Smooth(float a, float b, float x) { float t = Clamp((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException("PH02 family component: " + message); }
        static void ValidateArrays(Vector3[] p, Vector3[] n, Vector2[] u, Vector4[] t, int[] indices)
        {
            Require(p != null && n != null && u != null && t != null && indices != null && p.Length > 0 &&
                n.Length == p.Length && u.Length == p.Length && t.Length == p.Length && indices.Length % 3 == 0, "Incomplete input arrays.");
            for (int i = 0; i < p.Length; i++)
                Require(Finite(p[i]) && Finite(n[i]) && Finite(u[i].x) && Finite(u[i].y) && Finite(t[i].x) && Finite(t[i].y) && Finite(t[i].z) && Finite(t[i].w), "Nonfinite input.");
            foreach (int i in indices) Require(i >= 0 && i < p.Length, "Invalid triangle index.");
        }
        static string HashCrown(CrownData d) => Hash(w => WriteGeometry(w, d.Positions, d.Normals, d.Uvs, d.Tangents, d.Triangles));
        static string HashSupport(PH02FittedSupportCandidate.SupportData d) => Hash(w => { WriteGeometry(w, d.Positions, d.Normals, d.AtlasUv, d.Tangents, d.Triangles); WriteAdditional(w, d.BranchUv, d.BlendWeights); });
        static string HashComponent(ComponentData d) => Hash(w => { WriteGeometry(w, d.Positions, d.Normals, d.AtlasUv, d.Tangents, d.Triangles); WriteAdditional(w, d.BranchUv, d.Weights); });
        static string Hash(Action<BinaryWriter> write)
        {
            using (var sha = SHA256.Create())
            using (var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
            using (var writer = new BinaryWriter(stream)) { write(writer); writer.Flush(); stream.FlushFinalBlock(); return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant(); }
        }
        static void WriteGeometry(BinaryWriter w, Vector3[] p, Vector3[] n, Vector2[] uv, Vector4[] t, int[] indices)
        {
            w.Write(p.Length);
            for (int i = 0; i < p.Length; i++) { w.Write(p[i].x); w.Write(p[i].y); w.Write(p[i].z); w.Write(n[i].x); w.Write(n[i].y); w.Write(n[i].z); w.Write(uv[i].x); w.Write(uv[i].y); w.Write(t[i].x); w.Write(t[i].y); w.Write(t[i].z); w.Write(t[i].w); }
            w.Write(indices.Length); foreach (int i in indices) w.Write(i);
        }
        static void WriteAdditional(BinaryWriter w, Vector2[] uv, Color[] colors)
        {
            w.Write(uv.Length); foreach (Vector2 u in uv) { w.Write(u.x); w.Write(u.y); }
            w.Write(colors.Length); foreach (Color c in colors) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
        }

        [Serializable] public sealed class Report
        {
            public string schema = "starfall.ph02-family-component.v1";
            public string status = "Unaccepted compound component for full-family inspection; no full-family numeric or visual acceptance.";
            public string buildStage = "managed buffers", failure;
            public string sourceAsset = PH02CrownCandidate.SourceAssetPath, sourceSha256 = PH02CrownCandidate.SourceSha256;
            public string coordinates = "Original metric scale. Subtract measured fitted-support lowerCentre; rotate actual upward lower tangent (-2*bend.x/length,1,-2*bend.y/length) to +Y. Proper rigid rotation only; no source leaf/crown rescaling.";
            public string topology = "One submesh, original crown triangles first then support triangles with vertex offset. Every source vertex/UV/tangent seam retained. Crown/support remain separately indexed; this is not a welded whole-tree claim.";
            public string colourPolicy = "Crown R=0; support R=existing procedural-bark blend. Crown G=saturate(smoothstep(.10,.18,radialXZFromOriginalRimCentre)*smoothstep(.65,.80,originalY)+smoothstep(.92,1.04,originalY)); support G=0. G is an explicit geometric art-tint heuristic, not publisher semantic data or a mask-file interpretation.";
            public string materialContract = "Compound component uses one PH02 fitted surface material. Procedurally generated supporting wood is a separate mesh/material in the later family generator.";
            public bool importedTupleMode, actualImportedGatePassed, actualMeshCreated, numericChecksPassed, inputsUnchanged;
            public PH02ImportedCrownCandidate.Report importedChecks;
            public PH02FittedSupportCandidate.Report fittedChecks;
            public Vector3 sourceLowerCentre, sourceRimCentre, sourceLowerAxis;
            public Quaternion componentRotation;
            public float transitionMetres, lowerExtensionMetres, roundRadiusMetres, bottomRadiusScale, bottomRadiusMetres;
            public float transformedAxisError, rotationDeterminant, maximumPositionRoundTripError, maximumNormalRoundTripError, maximumTangentRoundTripError, maximumPivotDistanceError;
            public float minimumBottomRadius = float.PositiveInfinity, maximumBottomRadius, maximumBottomRadiusError, maximumBottomPlaneError;
            public int crownVertices, supportVertices, crownTriangles, supportTriangles, componentVertices, componentTriangles;
            public int crownFirstIndex, crownIndexCount, supportFirstIndex, supportIndexCount, bottomRingAttributeVertices, crownVerticesWithFoliageWeight;
            public double minimumTriangleArea = double.PositiveInfinity;
            public string crownInputSha256Before, crownInputSha256After, supportInputSha256Before, supportInputSha256After, callerMeshSha256Before, callerMeshSha256After, componentSha256;
        }
    }
}
#endif
