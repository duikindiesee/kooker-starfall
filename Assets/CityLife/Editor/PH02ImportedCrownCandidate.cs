#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World.Editor
{
    /// <summary>
    /// STAGED ONLY. Read-only imported-PH02 clipping experiment. Does not configure a scene,
    /// mutate an importer, launch rendering or persist assets. All returned meshes are caller-owned.
    /// Preserves the source triangle correspondence and actual imported complete vertex tuples.
    /// </summary>
    public static class PH02ImportedCrownCandidate
    {
        public const string SourceAssetPath = "Assets/CityLife/Art/PolyHaven/QuiverTree02/quiver_tree_02_2k.fbx";
        public const string SourceSha256 = "350984fbf9fc2168357ed812913aa937524c3935ca3263cb95877b6504c951f8";
        const float PositionTolerance = .00002f, UvTolerance = .00001f, CellSize = .0001f, NormalDot = .9999f;

        /// <summary>Uses actual Unity read-only imported mesh buffers. No normal/tangent recalculation.
        /// The report is assigned before mapping so a caller can retain a failed gate's diagnostics.</summary>
        public static Mesh Create(float cutHeight, out Report report, bool exactTupleDedup = true)
        {
            report = new Report { actualUnityImportedData = true, unityVersion = Application.unityVersion };
            string sourcePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), SourceAssetPath);
            Input imported = ReadImported(report);
            Buffers data = BuildInternal(sourcePath, imported, cutHeight, exactTupleDedup, report);
            Mesh mesh = null;
            try
            {
                mesh = new Mesh { name = "PH02 actual imported tuples - clipped comparison candidate", indexFormat = data.Positions.Length <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32 };
                mesh.vertices = data.Positions; mesh.normals = data.Normals; mesh.uv = data.Uvs; mesh.tangents = data.Tangents;
                mesh.triangles = data.Indices; mesh.RecalculateBounds();
                report.meshCreated = true; return mesh;
            }
            catch { if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); throw; }
        }

        /// <summary>Pure managed buffers, suitable for bounded CPU fixtures. This API deliberately
        /// does not claim its input came from Unity. Only Create sets actualUnityImportedData=true.</summary>
        public static Buffers BuildData(string absoluteSourcePath, Input imported, float cutHeight, out Report report, bool exactTupleDedup = true)
        {
            report = new Report();
            return BuildInternal(absoluteSourcePath, imported, cutHeight, exactTupleDedup, report);
        }

        /// <summary>Pinned raw P/N/UV corner oracle. Tangents are absent because the pinned FBX
        /// does not define the importer-generated tangent basis. No mesh or native call is used.</summary>
        public static Input ReadPinnedSourceCornerOracle(string absoluteSourcePath)
        {
            Raw data = Load(absoluteSourcePath);
            var result = new Input { Positions = new Vector3[data.Indices.Length], Normals = new Vector3[data.Indices.Length], Uvs = new Vector2[data.Indices.Length], Tangents = Array.Empty<Vector4>(), Indices = new int[data.Indices.Length], provenance = "Pinned FBX raw transformed corner oracle; NOT actual Unity imported tangents" };
            for (int i = 0; i < data.Indices.Length; i++)
            {
                Corner c = data.Corner(i); result.Positions[i] = c.P; result.Normals[i] = c.N; result.Uvs[i] = c.U;
                result.Indices[i] = (i / 3) * 3 + (i % 3 == 1 ? 2 : i % 3 == 2 ? 1 : 0);
            }
            return result;
        }

        static Buffers BuildInternal(string sourcePath, Input imported, float cutHeight, bool dedup, Report report)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Raw raw = Load(sourcePath);
            Require(Finite(cutHeight) && cutHeight > raw.MinY && cutHeight < raw.MaxY, "Cut must lie strictly inside the source bounds.");
            ValidateInput(imported);
            report.inputProvenance = imported.provenance;
            report.importedVertices = imported.Positions.Length; report.importedTriangles = imported.Indices.Length / 3;
            report.cutHeightMetres = cutHeight; report.exactTupleDedup = dedup;
            Require(imported.Indices.Length == raw.Indices.Length, "Imported triangle count differs from the pinned source.");
            Corner[] mapped = MapTriangles(raw, imported, report);
            report.mappingComplete = true;
            var p = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<Vector4>(); var indices = new List<int>();
            var tuples = new Dictionary<ExactTuple, int>();
            var boundary = new List<Tuple<Vector3, Vector3>>();
            var expected = SHA256.Create();
            using (expected)
            using (var hashStream = new CryptoStream(Stream.Null, expected, CryptoStreamMode.Write))
            using (var writer = new BinaryWriter(hashStream))
            {
                for (int face = 0; face < raw.Indices.Length / 3; face++)
                {
                    Corner[] source = { mapped[face * 3], mapped[face * 3 + 1], mapped[face * 3 + 2] };
                    int inside = source.Count(c => c.P.y >= cutHeight);
                    if (inside == 0) { report.discardedSourceTriangles++; continue; }
                    List<Corner> polygon;
                    if (inside == 3) { report.fullyKeptSourceTriangles++; polygon = new List<Corner>(source); }
                    else
                    {
                        report.clippedSourceTriangles++; polygon = new List<Corner>(4);
                        for (int i = 0; i < 3; i++)
                        {
                            Corner previous = source[(i + 2) % 3], current = source[i];
                            bool aIn = previous.P.y >= cutHeight, bIn = current.P.y >= cutHeight;
                            if (aIn != bIn) polygon.Add(Intersection(previous, current, cutHeight, report));
                            if (bIn) polygon.Add(current);
                        }
                    }
                    for (int i = polygon.Count - 1; i >= 0 && polygon.Count > 1; i--)
                        if (polygon[i].P.Equals(polygon[(i + 1) % polygon.Count].P)) polygon.RemoveAt(i);
                    for (int i = 1; i + 1 < polygon.Count; i++)
                    {
                        // Source FBX order becomes reversed after (-X,Z,-Y). Import mapping has
                        // already verified that this is the actual imported winding, not a guess.
                        Corner[] triangle = { polygon[0], polygon[i + 1], polygon[i] };
                        Vector3 cross = Vector3.Cross(triangle[1].P - triangle[0].P, triangle[2].P - triangle[0].P);
                        double area = .5 * Math.Sqrt((double)cross.x * cross.x + (double)cross.y * cross.y + (double)cross.z * cross.z);
                        Require(area > 0 && !double.IsNaN(area) && !double.IsInfinity(area), "Clipping produced a degenerate triangle.");
                        report.minimumTriangleArea = Math.Min(report.minimumTriangleArea, area);
                        for (int c = 0; c < 3; c++)
                        {
                            Corner v = triangle[c]; WriteTuple(writer, v);
                            var tuple = new ExactTuple(v);
                            if (!dedup || !tuples.TryGetValue(tuple, out int index))
                            {
                                index = p.Count; p.Add(v.P); n.Add(v.N); u.Add(v.U); t.Add(v.T);
                                if (dedup) tuples.Add(tuple, index);
                            }
                            indices.Add(index);
                            Corner next = triangle[(c + 1) % 3];
                            if (v.P.y == cutHeight && next.P.y == cutHeight) boundary.Add(Tuple.Create(v.P, next.P));
                        }
                    }
                }
                writer.Flush(); hashStream.FlushFinalBlock(); report.expandedBeforeDedupSha256 = Hex(expected.Hash);
            }
            var result = new Buffers { Positions = p.ToArray(), Normals = n.ToArray(), Uvs = u.ToArray(), Tangents = t.ToArray(), Indices = indices.ToArray() };
            report.outputVertices = p.Count; report.outputTriangles = indices.Count / 3; report.outputCornerCount = indices.Count;
            report.vertexReductionFraction = 1d - (double)p.Count / indices.Count;
            report.vertexBytesAt48ByteStride = p.Count * 48L;
            report.cornerBytesAt48ByteStride = indices.Count * 48L;
            report.expandedAfterDedupSha256 = HashExpanded(result);
            report.exactExpandedTuplePreservation = report.expandedBeforeDedupSha256 == report.expandedAfterDedupSha256;
            Require(report.exactExpandedTuplePreservation, "Exact complete-tuple dedup changed an expanded triangle corner.");
            AssessBoundary(boundary, result, cutHeight, report);
            timer.Stop(); report.managedBuildMilliseconds = timer.ElapsedMilliseconds;
            return result;
        }

        static Corner[] MapTriangles(Raw source, Input input, Report report)
        {
            var grid = new Dictionary<Vector3Int, List<int>>();
            var incident = new List<int>[input.Positions.Length];
            for (int i = 0; i < input.Positions.Length; i++)
            {
                Vector3Int cell = Cell(input.Positions[i]);
                if (!grid.TryGetValue(cell, out List<int> list)) { list = new List<int>(); grid.Add(cell, list); }
                list.Add(i); incident[i] = new List<int>();
            }
            for (int i = 0; i < input.Indices.Length; i++) incident[input.Indices[i]].Add(i / 3);
            var result = new Corner[source.Indices.Length]; var used = new HashSet<int>();
            for (int face = 0; face < source.Indices.Length / 3; face++)
            {
                Corner[] c = { source.Corner(face * 3), source.Corner(face * 3 + 1), source.Corner(face * 3 + 2) };
                var candidates = new HashSet<int>(); Vector3Int cell = Cell(c[0].P);
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    if (!grid.TryGetValue(cell + new Vector3Int(x, y, z), out List<int> list)) continue;
                    foreach (int vertex in list) if (Matches(c[0], input, vertex)) foreach (int f in incident[vertex]) if (!used.Contains(f)) candidates.Add(f);
                }
                int bestFace = -1, bestRotation = -1; Corner[] selected = null;
                foreach (int f in candidates.OrderBy(v => v))
                for (int rotation = 0; rotation < 3; rotation++)
                {
                    int[] order = { input.Indices[f * 3 + rotation], input.Indices[f * 3 + (rotation + 2) % 3], input.Indices[f * 3 + (rotation + 1) % 3] };
                    if (!Enumerable.Range(0, 3).All(i => Matches(c[i], input, order[i]))) continue;
                    Corner[] match = Enumerable.Range(0, 3).Select(i => new Corner { P = input.Positions[order[i]], N = input.Normals[order[i]], U = input.Uvs[order[i]], T = input.Tangents[order[i]], Control = c[i].Control }).ToArray();
                    if (selected == null) { selected = match; bestFace = f; bestRotation = rotation; }
                    else
                    {
                        // Coincident topology is allowed only when all twelve copied floats
                        // are exactly equal. Never average/choose a different tangent basis.
                        bool equal = Enumerable.Range(0, 3).All(i => SameTuple(selected[i], match[i]));
                        if (!equal) { report.ambiguousSourceTriangles++; throw new InvalidDataException("PH02 imported candidate: source face " + face + " has distinct complete-tuple matches."); }
                        report.equivalentDuplicateFaceMatches++;
                    }
                }
                if (selected == null) { report.unmatchedSourceTriangles++; throw new InvalidDataException("PH02 imported candidate: source face " + face + " has no winding-preserving imported triangle match."); }
                Require(used.Add(bestFace) && bestRotation >= 0, "Imported triangle was reused.");
                for (int i = 0; i < 3; i++)
                {
                    result[face * 3 + i] = selected[i];
                    report.maximumImportedPositionDelta = Math.Max(report.maximumImportedPositionDelta, Vector3.Distance(c[i].P, selected[i].P));
                    report.maximumImportedUvDelta = Math.Max(report.maximumImportedUvDelta, Vector2.Distance(c[i].U, selected[i].U));
                    report.minimumImportedNormalDot = Math.Min(report.minimumImportedNormalDot, Vector3.Dot(c[i].N.normalized, selected[i].N.normalized));
                }
                report.matchedSourceTriangles++;
            }
            Require(used.Count == input.Indices.Length / 3, "Not all imported triangles were accounted for.");
            return result;
        }

        static Corner Intersection(Corner a, Corner b, float height, Report report)
        {
            if (a.Control > b.Control) { Corner swap = a; a = b; b = swap; }
            double fraction = ((double)height - a.P.y) / ((double)b.P.y - a.P.y);
            if (fraction == 0) return a;
            if (fraction == 1) return b;
            // An edge crossing a tangent-handedness discontinuity has no unique interpolated
            // tangent4. Fail rather than guess a side or silently erase the seam.
            Require(a.T.w == b.T.w, "Cut edge has differing tangent handedness; explicit seam-aware subdivision is required.");
            Vector3 position = Lerp(a.P, b.P, fraction); position.y = height;
            Vector3 normal = Lerp(a.N, b.N, fraction), tangent = Lerp(Tangent3(a.T), Tangent3(b.T), fraction);
            Require(normal.sqrMagnitude > 1e-12f && tangent.sqrMagnitude > 1e-12f, "Cut normal/tangent interpolation cancels.");
            report.cutInterpolationCalls++;
            return new Corner { P = position, N = normal.normalized, U = new Vector2(Lerp(a.U.x, b.U.x, fraction), Lerp(a.U.y, b.U.y, fraction)), T = new Vector4(tangent.normalized.x, tangent.normalized.y, tangent.normalized.z, a.T.w), Control = -1 };
        }

        static void AssessBoundary(List<Tuple<Vector3, Vector3>> edges, Buffers data, float height, Report report)
        {
            var graph = new Dictionary<Vector3, HashSet<Vector3>>();
            foreach (var edge in edges)
            {
                if (!graph.TryGetValue(edge.Item1, out HashSet<Vector3> a)) { a = new HashSet<Vector3>(); graph.Add(edge.Item1, a); }
                if (!graph.TryGetValue(edge.Item2, out HashSet<Vector3> b)) { b = new HashSet<Vector3>(); graph.Add(edge.Item2, b); }
                a.Add(edge.Item2); b.Add(edge.Item1);
            }
            report.boundaryEdges = edges.Count; report.boundaryPositions = graph.Count; report.boundaryAllDegreeTwo = graph.Values.All(v => v.Count == 2);
            var unseen = new HashSet<Vector3>(graph.Keys);
            while (unseen.Count > 0)
            {
                report.boundaryComponents++; Vector3 first = unseen.First(); unseen.Remove(first); var stack = new Stack<Vector3>(); stack.Push(first);
                while (stack.Count > 0) foreach (Vector3 next in graph[stack.Pop()]) if (unseen.Remove(next)) stack.Push(next);
            }
            var boundaryTuples = new Dictionary<Vector3, HashSet<Tuple<Vector3, Vector2, Vector4>>>();
            for (int i = 0; i < data.Positions.Length; i++) if (data.Positions[i].y == height)
            {
                Vector3 point = data.Positions[i];
                if (!boundaryTuples.TryGetValue(point, out var tuples)) { tuples = new HashSet<Tuple<Vector3, Vector2, Vector4>>(); boundaryTuples.Add(point, tuples); }
                tuples.Add(Tuple.Create(data.Normals[i], data.Uvs[i], data.Tangents[i]));
            }
            report.cutPlaneCompleteTuples = boundaryTuples.Values.Sum(v => v.Count);
            report.cutPlaneAttributeSeamPositions = boundaryTuples.Values.Count(v => v.Count > 1);
        }

        static Input ReadImported(Report report)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(SourceAssetPath);
            Require(asset != null, "PH02 imported asset unavailable.");
            var importer = AssetImporter.GetAtPath(SourceAssetPath) as ModelImporter;
            if (importer != null) { report.normalImportMode = importer.importNormals.ToString(); report.tangentImportMode = importer.importTangents.ToString(); report.globalScale = importer.globalScale; report.useFileUnits = importer.useFileUnits; report.weldVertices = importer.weldVertices; }
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var tangents = new List<Vector4>(); var indices = new List<int>();
            foreach (MeshFilter filter in asset.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh; if (mesh == null) continue;
                using (var arrays = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    Mesh.MeshData data = arrays[0]; Require(data.subMeshCount == 1, "Expected the pinned single PH02 material submesh.");
                    Require(data.HasVertexAttribute(VertexAttribute.Normal) && data.HasVertexAttribute(VertexAttribute.TexCoord0) && data.HasVertexAttribute(VertexAttribute.Tangent), "Imported PH02 lacks required P/N/UV/T attributes.");
                    using (var p = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    using (var n = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    using (var u = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp))
                    using (var t = new NativeArray<Vector4>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetVertices(p); data.GetNormals(n); data.GetUVs(0, u); data.GetTangents(t);
                        Matrix4x4 matrix = filter.transform.localToWorldMatrix, normalMatrix = matrix.inverse.transpose;
                        float determinant = matrix.determinant; Require(Mathf.Abs(determinant) > 1e-12f, "Singular imported hierarchy.");
                        int offset = positions.Count;
                        for (int i = 0; i < data.vertexCount; i++)
                        {
                            positions.Add(matrix.MultiplyPoint3x4(p[i])); normals.Add(normalMatrix.MultiplyVector(n[i]).normalized); uvs.Add(u[i]);
                            Vector3 tangent = matrix.MultiplyVector(Tangent3(t[i])).normalized;
                            tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, t[i].w * Mathf.Sign(determinant)));
                        }
                        SubMeshDescriptor sub = data.GetSubMesh(0); Require(sub.topology == MeshTopology.Triangles, "Imported PH02 is not triangle topology.");
                        using (var rawIndices = new NativeArray<int>(sub.indexCount, Allocator.Temp))
                        {
                            data.GetIndices(rawIndices, 0, true);
                            for (int i = 0; i < rawIndices.Length; i += 3)
                            {
                                indices.Add(offset + rawIndices[i]);
                                indices.Add(offset + rawIndices[i + (determinant < 0 ? 2 : 1)]);
                                indices.Add(offset + rawIndices[i + (determinant < 0 ? 1 : 2)]);
                            }
                        }
                        report.importedMeshNames.Add(mesh.name); report.importedWorldMatrices.Add(matrix);
                    }
                }
            }
            return new Input { Positions = positions.ToArray(), Normals = normals.ToArray(), Uvs = uvs.ToArray(), Tangents = tangents.ToArray(), Indices = indices.ToArray(), provenance = "Unity MeshUtility.AcquireReadOnlyMeshData; source hierarchy transformed into metres/Y-up; normal inverse-transpose; tangent xyz transform and determinant handedness" };
        }

        static void ValidateInput(Input data)
        {
            Require(data != null && data.Positions != null && data.Normals != null && data.Uvs != null && data.Tangents != null && data.Indices != null, "Missing input arrays.");
            int count = data.Positions.Length;
            Require(count > 0 && data.Normals.Length == count && data.Uvs.Length == count && data.Tangents.Length == count && data.Indices.Length % 3 == 0, "Incomplete input P/N/UV/T arrays.");
            for (int i = 0; i < count; i++)
            {
                var c = new Corner { P = data.Positions[i], N = data.Normals[i], U = data.Uvs[i], T = data.Tangents[i] };
                Require(Finite(c.P.x) && Finite(c.P.y) && Finite(c.P.z) && Finite(c.N.x) && Finite(c.N.y) && Finite(c.N.z) && Finite(c.U.x) && Finite(c.U.y) && Finite(c.T.x) && Finite(c.T.y) && Finite(c.T.z) && Finite(c.T.w), "Nonfinite input attribute.");
                Require(Mathf.Abs(c.N.magnitude - 1f) < .001f && Mathf.Abs(Tangent3(c.T).magnitude - 1f) < .001f && Mathf.Abs(c.T.w) == 1f, "Input normal/tangent is not a unit basis.");
            }
            foreach (int i in data.Indices) Require(i >= 0 && i < count, "Invalid imported index.");
        }
        static bool Matches(Corner c, Input input, int i) => (c.P - input.Positions[i]).sqrMagnitude <= PositionTolerance * PositionTolerance && (c.U - input.Uvs[i]).sqrMagnitude <= UvTolerance * UvTolerance && Vector3.Dot(c.N.normalized, input.Normals[i].normalized) >= NormalDot;
        static bool SameTuple(Corner a, Corner b) => new ExactTuple(a).Equals(new ExactTuple(b));
        static Vector3Int Cell(Vector3 p) => new Vector3Int((int)Math.Floor(p.x / CellSize), (int)Math.Floor(p.y / CellSize), (int)Math.Floor(p.z / CellSize));
        static Vector3 Tangent3(Vector4 t) => new Vector3(t.x, t.y, t.z);
        static Vector3 Lerp(Vector3 a, Vector3 b, double f) => new Vector3(Lerp(a.x, b.x, f), Lerp(a.y, b.y, f), Lerp(a.z, b.z, f));
        static float Lerp(float a, float b, double f) => (float)(a + (b - (double)a) * f);
        static Vector3 ToUnity(Vector3 p) => new Vector3(-p.x, p.z, -p.y);
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static string Hex(byte[] data) => BitConverter.ToString(data).Replace("-", "").ToLowerInvariant();
        static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException("PH02 imported candidate: " + message); }
        static void WriteTuple(BinaryWriter w, Corner c) { w.Write(c.P.x); w.Write(c.P.y); w.Write(c.P.z); w.Write(c.N.x); w.Write(c.N.y); w.Write(c.N.z); w.Write(c.U.x); w.Write(c.U.y); w.Write(c.T.x); w.Write(c.T.y); w.Write(c.T.z); w.Write(c.T.w); }
        static string HashExpanded(Buffers data)
        {
            using (var sha = SHA256.Create())
            using (var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
            using (var writer = new BinaryWriter(stream))
            {
                foreach (int i in data.Indices) WriteTuple(writer, new Corner { P = data.Positions[i], N = data.Normals[i], U = data.Uvs[i], T = data.Tangents[i] });
                writer.Flush(); stream.FlushFinalBlock(); return Hex(sha.Hash);
            }
        }

        public sealed class Input
        {
            public Vector3[] Positions, Normals; public Vector2[] Uvs; public Vector4[] Tangents; public int[] Indices; public string provenance;
        }
        public sealed class Buffers
        {
            public Vector3[] Positions, Normals; public Vector2[] Uvs; public Vector4[] Tangents; public int[] Indices;
        }
        [Serializable] public sealed class Report
        {
            public string schema = "citylife.ph02-imported-crown-candidate.v1";
            public string status = "Unaccepted staged optimisation; actual imported identity/render comparison pending unless explicitly recorded";
            public string sourceAsset = SourceAssetPath, sourceSha256 = SourceSha256, unityVersion, inputProvenance, normalImportMode, tangentImportMode;
            public string preservation = "Source-face/winding-aware P/N/UV identity gate; uncut P/N/UV/tangent4 copied from the actual imported tuple. New cut P/UV linearly interpolated, N/Txyz interpolated then normalized, tangent w retained only when both endpoints agree. No recalculation, seam smoothing, UV flips, decimation, pivot/scale change or cap.";
            public string exactDedup = "All twelve IEEE float bit patterns must match, including signed zero; no quantization or tolerance is used for dedup. Expanded triangle-corner byte hashes must agree before/after.";
            public string limitation = "Matching tolerance only verifies source/import identity; output uses actual imported float coordinates. New cut tangents have no original imported counterpart. Visual seam/material continuity and runtime performance remain unverified.";
            public float globalScale, cutHeightMetres, maximumImportedPositionDelta, maximumImportedUvDelta, minimumImportedNormalDot = 1f;
            public bool useFileUnits, weldVertices, actualUnityImportedData, exactTupleDedup, mappingComplete, meshCreated, exactExpandedTuplePreservation, boundaryAllDegreeTwo;
            public int importedVertices, importedTriangles, matchedSourceTriangles, unmatchedSourceTriangles, ambiguousSourceTriangles, equivalentDuplicateFaceMatches, fullyKeptSourceTriangles, discardedSourceTriangles, clippedSourceTriangles, outputVertices, outputTriangles, outputCornerCount, cutInterpolationCalls, boundaryEdges, boundaryPositions, boundaryComponents, cutPlaneCompleteTuples, cutPlaneAttributeSeamPositions;
            public long vertexBytesAt48ByteStride, cornerBytesAt48ByteStride, managedBuildMilliseconds;
            public double vertexReductionFraction, minimumTriangleArea = double.PositiveInfinity;
            public string expandedBeforeDedupSha256, expandedAfterDedupSha256;
            public List<string> importedMeshNames = new List<string>(); public List<Matrix4x4> importedWorldMatrices = new List<Matrix4x4>();
        }
        struct Corner { public Vector3 P, N; public Vector2 U; public Vector4 T; public int Control; }
        [StructLayout(LayoutKind.Explicit)] struct FloatBits
        {
            [FieldOffset(0)] public float Value;
            [FieldOffset(0)] public int Bits;
        }
        struct ExactTuple : IEquatable<ExactTuple>
        {
            readonly Corner value;
            public ExactTuple(Corner corner) { value = corner; }
            static int Bits(float f) => new FloatBits { Value = f }.Bits;
            static bool Same(float a, float b) => Bits(a) == Bits(b);
            public bool Equals(ExactTuple other)
            {
                Corner a = value, b = other.value;
                return Same(a.P.x,b.P.x) && Same(a.P.y,b.P.y) && Same(a.P.z,b.P.z)
                    && Same(a.N.x,b.N.x) && Same(a.N.y,b.N.y) && Same(a.N.z,b.N.z)
                    && Same(a.U.x,b.U.x) && Same(a.U.y,b.U.y)
                    && Same(a.T.x,b.T.x) && Same(a.T.y,b.T.y) && Same(a.T.z,b.T.z) && Same(a.T.w,b.T.w);
            }
            public override bool Equals(object other) => other is ExactTuple tuple && Equals(tuple);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h=h*31+Bits(value.P.x); h=h*31+Bits(value.P.y); h=h*31+Bits(value.P.z);
                    h=h*31+Bits(value.N.x); h=h*31+Bits(value.N.y); h=h*31+Bits(value.N.z);
                    h=h*31+Bits(value.U.x); h=h*31+Bits(value.U.y);
                    h=h*31+Bits(value.T.x); h=h*31+Bits(value.T.y); h=h*31+Bits(value.T.z); h=h*31+Bits(value.T.w);
                    return h;
                }
            }
        }
        sealed class Raw
        {
            public double[] Positions, Normals, Uvs; public int[] Indices, NormalIndices, UvIndices; public float MinY = float.PositiveInfinity, MaxY = float.NegativeInfinity;
            public int Vertex(int corner) => Indices[corner] < 0 ? -Indices[corner] - 1 : Indices[corner];
            public Corner Corner(int index)
            {
                int v = Vertex(index), n = NormalIndices[v], u = UvIndices[index];
                return new Corner { P = ToUnity(new Vector3((float)Positions[v * 3], (float)Positions[v * 3 + 1], (float)Positions[v * 3 + 2])), N = ToUnity(new Vector3((float)Normals[n * 3], (float)Normals[n * 3 + 1], (float)Normals[n * 3 + 2])), U = new Vector2((float)Uvs[u * 2], (float)Uvs[u * 2 + 1]), Control = v };
            }
        }
        static Raw Load(string absolutePath)
        {
            Require(Path.IsPathRooted(absolutePath), "An absolute pinned-source path is required.");
            byte[] bytes = File.ReadAllBytes(absolutePath);
            using (var sha = SHA256.Create()) Require(Hex(sha.ComputeHash(bytes)) == SourceSha256, "Pinned source hash changed.");
            Node geometry = FbxReader.Read(bytes).Single(n => n.Name == "Objects").Children.Single(n => n.Name == "Geometry");
            Node normals = geometry.Child("LayerElementNormal"), uv = geometry.Child("LayerElementUV"), material = geometry.Child("LayerElementMaterial");
            Require(Text(normals, "MappingInformationType") == "ByVertice" && Text(normals, "ReferenceInformationType") == "IndexToDirect", "Unexpected PH02 normal mapping.");
            Require(Text(uv, "MappingInformationType") == "ByPolygonVertex" && Text(uv, "ReferenceInformationType") == "IndexToDirect", "Unexpected PH02 UV mapping.");
            Require(Text(material, "MappingInformationType") == "AllSame" && ArrayOf<int>(material, "Materials").SequenceEqual(new[] { 0 }), "Unexpected PH02 material mapping.");
            var data = new Raw { Positions = ArrayOf<double>(geometry, "Vertices"), Indices = ArrayOf<int>(geometry, "PolygonVertexIndex"), Normals = ArrayOf<double>(normals, "Normals"), NormalIndices = ArrayOf<int>(normals, "NormalsIndex"), Uvs = ArrayOf<double>(uv, "UV"), UvIndices = ArrayOf<int>(uv, "UVIndex") };
            Require(data.Positions.Length == 41073 * 3 && data.Indices.Length == 82074 * 3 && data.NormalIndices.Length == 41073 && data.UvIndices.Length == data.Indices.Length, "Unexpected pinned source dimensions.");
            for (int i = 0; i < data.Indices.Length; i++)
            {
                Require((data.Indices[i] < 0) == (i % 3 == 2), "Pinned source is not triangle-only.");
                Corner c = data.Corner(i); data.MinY = Math.Min(data.MinY, c.P.y); data.MaxY = Math.Max(data.MaxY, c.P.y);
            }
            return data;
        }
        static T[] ArrayOf<T>(Node n, string name) => (T[])n.Child(name).Properties[0];
        static string Text(Node n, string name) => (string)n.Child(name).Properties[0];
        // The strict pinned-FBX reader is appended from the existing project reader, unchanged.
        private sealed class Node
        {
            public string Name;
            public object[] Properties;
            public readonly List<Node> Children = new List<Node>();
            public Node Child(string name) => Children.Single(n => n.Name == name);
        }

        /// <summary>Small strict reader for the pinned FBX7400 file, not a general FBX importer.</summary>
        private sealed class FbxReader
        {
            private readonly BinaryReader reader;
            private FbxReader(byte[] bytes) { reader = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8); }
            public static List<Node> Read(byte[] bytes)
            {
                Require(bytes.Length > 27 && Encoding.ASCII.GetString(bytes, 0, 21) == "Kaydara FBX Binary  \0", "Not a binary FBX file.");
                using (var reader = new BinaryReader(new MemoryStream(bytes, false))) { reader.BaseStream.Position = 23; Require(reader.ReadUInt32() == 7400, "Only the inspected FBX7400 version is supported."); }
                var parser = new FbxReader(bytes);
                try
                {
                    parser.reader.BaseStream.Position = 27;
                    var nodes = new List<Node>();
                    while (parser.reader.BaseStream.Position + 13 <= bytes.Length) { Node node = parser.ReadNode(); if (node == null) break; nodes.Add(node); }
                    return nodes;
                }
                finally { parser.reader.Dispose(); }
            }
            private Node ReadNode()
            {
                long end = reader.ReadUInt32(); uint count = reader.ReadUInt32(); uint propertyBytes = reader.ReadUInt32(); int nameLength = reader.ReadByte();
                if (end == 0) { Require(count == 0 && propertyBytes == 0 && nameLength == 0, "Malformed null record."); return null; }
                Require(end > reader.BaseStream.Position && end <= reader.BaseStream.Length && count < 1000000, "Invalid FBX node bounds.");
                var node = new Node { Name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength)), Properties = new object[count] };
                long propertiesStart = reader.BaseStream.Position;
                for (int i = 0; i < count; i++) node.Properties[i] = Property();
                Require(reader.BaseStream.Position - propertiesStart == propertyBytes, "FBX property length mismatch.");
                while (reader.BaseStream.Position < end) { Node child = ReadNode(); if (child == null) break; node.Children.Add(child); }
                Require(reader.BaseStream.Position == end, "FBX node end mismatch.");
                return node;
            }
            private object Property()
            {
                char type = (char)reader.ReadByte();
                switch (type)
                {
                    case 'Y': return reader.ReadInt16(); case 'C': return reader.ReadByte(); case 'I': return reader.ReadInt32();
                    case 'F': return reader.ReadSingle(); case 'D': return reader.ReadDouble(); case 'L': return reader.ReadInt64();
                    case 'S': return Encoding.UTF8.GetString(reader.ReadBytes(checked((int)reader.ReadUInt32())));
                    case 'R': return reader.ReadBytes(checked((int)reader.ReadUInt32()));
                    case 'd': case 'f': case 'i': case 'l': case 'b': case 'c': return ArrayProperty(type);
                    default: throw new InvalidDataException("Unsupported FBX property type " + type);
                }
            }
            private object ArrayProperty(char type)
            {
                int count = checked((int)reader.ReadUInt32()), encoding = checked((int)reader.ReadUInt32()), length = checked((int)reader.ReadUInt32());
                Require(count >= 0 && count <= 10000000 && length >= 0 && length <= reader.BaseStream.Length - reader.BaseStream.Position, "Invalid FBX array size.");
                byte[] encoded = reader.ReadBytes(length), decoded;
                if (encoding == 0) decoded = encoded;
                else
                {
                    Require(encoding == 1 && encoded.Length >= 6, "Unsupported FBX array encoding.");
                    // FBX uses zlib. Unity's .NET Standard DeflateStream reads the raw payload,
                    // so omit the two-byte zlib header and four-byte Adler checksum.
                    using (var input = new MemoryStream(encoded, 2, encoded.Length - 6, false))
                    using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream()) { deflate.CopyTo(output); decoded = output.ToArray(); }
                }
                int size = type == 'd' || type == 'l' ? 8 : type == 'f' || type == 'i' ? 4 : 1;
                Require(decoded.Length == count * size, "FBX decompressed array size mismatch.");
                using (var data = new BinaryReader(new MemoryStream(decoded, false)))
                {
                    if (type == 'd') { var result = new double[count]; for (int i = 0; i < count; i++) result[i] = data.ReadDouble(); return result; }
                    if (type == 'f') { var result = new float[count]; for (int i = 0; i < count; i++) result[i] = data.ReadSingle(); return result; }
                    if (type == 'i') { var result = new int[count]; for (int i = 0; i < count; i++) result[i] = data.ReadInt32(); return result; }
                    if (type == 'l') { var result = new long[count]; for (int i = 0; i < count; i++) result[i] = data.ReadInt64(); return result; }
                    return decoded;
                }
            }
        }

    }
}
#endif
