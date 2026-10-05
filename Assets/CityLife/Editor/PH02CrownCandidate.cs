#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World.Editor
{
    /// <summary>
    /// Unaccepted, editor-only PH02 trial, staged after the prior batch ended.
    /// Reads the pinned original; writes nothing and starts no renderer. Caller owns returned meshes.
    /// No leaf classification, cap, socket, shape normalization, decimation or material tint is added.
    /// </summary>
    public static class PH02CrownCandidate
    {
        public const string SourceAssetPath = "Assets/CityLife/Art/PolyHaven/QuiverTree02/quiver_tree_02_2k.fbx";
        public const string SourceSha256 = "350984fbf9fc2168357ed812913aa937524c3935ca3263cb95877b6504c951f8";
        public const string DiffusePath = "Assets/CityLife/Art/PolyHaven/QuiverTree02/textures/quiver_tree_02_diff_2k.jpg";
        public const string NormalPath = "Assets/CityLife/Art/PolyHaven/QuiverTree02/textures/quiver_tree_02_nor_gl_2k.exr";
        public const string RoughnessPath = "Assets/CityLife/Art/PolyHaven/QuiverTree02/textures/quiver_tree_02_rough_2k.exr";
        public const string MaskPath = "Assets/CityLife/Art/PolyHaven/QuiverTree02/textures/quiver_tree_02_mask_2k.png";
        // Measurements: .65 has one ~.148 x .150 m stem loop; .74 already intersects two loops.
        // This is a trial parameter, not a verified semantic leaf boundary or accepted attachment.
        public const float ProposedCutHeight = .65f;
        private static Data cached;

        public static Mesh CreateFullOriginalForComparison()
        {
            return BuildMesh(Build(null), "PH02 full original source - comparison only");
        }

        /// <summary>
        /// Keeps source geometry at Y >= cutHeight metres. Both outputs retain the SAME source
        /// origin and metre coordinates (-X,Z,-Y), so no hidden pivot/scale change affects comparison.
        /// To attach, use Describe(cutHeight).boundaryCentre as an explicit caller-chosen pivot.
        /// The clipped boundary is open and unaccepted. Arbitrary cuts may cut through leaves.
        /// </summary>
        public static Mesh CreateCrown(float cutHeight)
        {
            return BuildMesh(Build(cutHeight), "PH02 clipped crown candidate - open boundary");
        }

        public static CutReport Describe(float cutHeight) { return Build(cutHeight).Report; }
        public static CutReport DescribeFullOriginal() { return Build(null).Report; }
        public static string DescribeJson(float cutHeight) { return JsonUtility.ToJson(Describe(cutHeight), true); }

        private static Data Load()
        {
            if (cached != null) return cached;
            byte[] bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), SourceAssetPath));
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            Require(hash == SourceSha256, "Source hash changed; previous profile/cut measurements no longer apply.");
            Node geometry = FbxReader.Read(bytes).Single(n => n.Name == "Objects").Children.Single(n => n.Name == "Geometry");
            Node normals = geometry.Child("LayerElementNormal"), uv = geometry.Child("LayerElementUV"), material = geometry.Child("LayerElementMaterial");
            Require(Text(normals, "MappingInformationType") == "ByVertice" && Text(normals, "ReferenceInformationType") == "IndexToDirect", "Expected PH02 control-vertex normal mapping, not PH01 corner-normal mapping.");
            Require(Text(uv, "MappingInformationType") == "ByPolygonVertex" && Text(uv, "ReferenceInformationType") == "IndexToDirect", "Unexpected corner UV mapping.");
            Require(Text(material, "MappingInformationType") == "AllSame", "Unexpected material mapping.");
            int[] materials = ArrayOf<int>(material, "Materials");
            Require(materials.Length == 1 && materials[0] == 0, "Expected one source material.");
            var data = new Data
            {
                Positions = ArrayOf<double>(geometry, "Vertices"), Indices = ArrayOf<int>(geometry, "PolygonVertexIndex"),
                Normals = ArrayOf<double>(normals, "Normals"), NormalIndices = ArrayOf<int>(normals, "NormalsIndex"),
                Uvs = ArrayOf<double>(uv, "UV"), UvIndices = ArrayOf<int>(uv, "UVIndex")
            };
            Require(data.Positions.Length == 41073 * 3 && data.Indices.Length == 82074 * 3 && data.NormalIndices.Length == 41073 && data.UvIndices.Length == data.Indices.Length, "Unexpected PH02 geometry dimensions.");
            Require(data.Normals.Length % 3 == 0 && data.Uvs.Length % 2 == 0, "Incomplete normal/UV vectors.");
            for (int i = 0; i < data.Indices.Length; i++)
            {
                Require((data.Indices[i] < 0) == (i % 3 == 2), "Pinned PH02 must contain only triangle polygons.");
                int vertex = data.Vertex(i);
                Require(vertex >= 0 && vertex < 41073, "Invalid control vertex index.");
                int n = data.NormalIndices[vertex], u = data.UvIndices[i];
                Require(n >= 0 && n < data.Normals.Length / 3 && u >= 0 && u < data.Uvs.Length / 2, "Invalid mapped normal/UV index.");
            }
            for (int i = 0; i < 41073; i++) data.Bounds.Encapsulate(data.Position(i));
            cached = data;
            return data;
        }

        private static Built Build(float? cutHeight)
        {
            Data data = Load();
            if (cutHeight.HasValue)
                Require(Finite(cutHeight.Value) && cutHeight.Value > data.Bounds.min.y && cutHeight.Value < data.Bounds.max.y, "Cut must be finite and strictly inside source height bounds.");
            var built = new Built();
            CutReport report = built.Report;
            report.sourceBounds = data.Bounds;
            report.isClipped = cutHeight.HasValue;
            report.cutHeightMetres = cutHeight.GetValueOrDefault();
            var boundary = new Dictionary<ulong, Vector3>();
            var graph = new Dictionary<ulong, HashSet<ulong>>();
            for (int face = 0; face < 82074; face++)
            {
                Corner[] source = { data.Corner(face * 3), data.Corner(face * 3 + 1), data.Corner(face * 3 + 2) };
                if (!cutHeight.HasValue || source.All(c => c.Position.y >= cutHeight.Value))
                {
                    report.fullyKeptSourceTriangles++;
                    Emit(built, source[0], source[1], source[2]);
                    continue;
                }
                float height = cutHeight.Value;
                if (source.All(c => c.Position.y < height)) { report.discardedSourceTriangles++; continue; }
                report.clippedSourceTriangles++;
                var polygon = new List<Corner>(4);
                var crossings = new List<Corner>(2);
                for (int i = 0; i < 3; i++)
                {
                    Corner previous = source[(i + 2) % 3], current = source[i];
                    bool prevIn = previous.Position.y >= height, currentIn = current.Position.y >= height;
                    if (prevIn != currentIn)
                    {
                        Corner crossing = Intersection(previous, current, height);
                        polygon.Add(crossing); crossings.Add(crossing);
                    }
                    if (currentIn) polygon.Add(current);
                }
                // A plane through an original vertex can emit that endpoint twice. Remove ONLY
                // exact duplicate positions; never remove merely small source detail triangles.
                for (int i = polygon.Count - 1; i >= 0 && polygon.Count > 1; i--)
                    if ((polygon[i].Position - polygon[(i + 1) % polygon.Count].Position).sqrMagnitude == 0f) polygon.RemoveAt(i);
                for (int i = 1; i + 1 < polygon.Count; i++) Emit(built, polygon[0], polygon[i], polygon[i + 1]);
                if (crossings.Count == 2 && crossings[0].BoundaryKey != crossings[1].BoundaryKey)
                {
                    report.cutBoundarySegments++;
                    Corner a = crossings[0], b = crossings[1];
                    boundary[a.BoundaryKey] = a.Position; boundary[b.BoundaryKey] = b.Position;
                    if (!graph.ContainsKey(a.BoundaryKey)) graph.Add(a.BoundaryKey, new HashSet<ulong>());
                    if (!graph.ContainsKey(b.BoundaryKey)) graph.Add(b.BoundaryKey, new HashSet<ulong>());
                    graph[a.BoundaryKey].Add(b.BoundaryKey); graph[b.BoundaryKey].Add(a.BoundaryKey);
                }
            }
            report.outputTriangles = built.Indices.Count / 3; report.outputCornerVertices = built.Positions.Count;
            report.outputBounds = new Bounds(built.Positions[0], Vector3.zero);
            foreach (Vector3 p in built.Positions) report.outputBounds.Encapsulate(p);
            report.boundaryVertices = boundary.Count;
            report.boundaryAllDegreeTwo = graph.Count > 0 && graph.Values.All(n => n.Count == 2);
            if (boundary.Count > 0)
            {
                report.boundaryBounds = new Bounds(boundary.Values.First(), Vector3.zero);
                double x = 0, z = 0;
                foreach (Vector3 p in boundary.Values) { report.boundaryBounds.Encapsulate(p); x += p.x; z += p.z; }
                report.boundaryCentre = new Vector3((float)(x / boundary.Count), cutHeight.Value, (float)(z / boundary.Count));
                var unseen = new HashSet<ulong>(graph.Keys);
                while (unseen.Count > 0)
                {
                    report.boundaryComponents++;
                    ulong first = unseen.First(); unseen.Remove(first); var stack = new Stack<ulong>(); stack.Push(first);
                    while (stack.Count > 0) foreach (ulong next in graph[stack.Pop()]) if (unseen.Remove(next)) stack.Push(next);
                }
            }
            return built;
        }

        private static Corner Intersection(Corner a, Corner b, float height)
        {
            // Canonical control-vertex order makes the same geometric edge cut bit-identical
            // across adjacent polygons; each polygon still interpolates its own seam UVs.
            if (a.ControlVertex > b.ControlVertex) { Corner temp = a; a = b; b = temp; }
            double t = ((double)height - a.Position.y) / ((double)b.Position.y - a.Position.y);
            if (t == 0) { a.BoundaryKey = VertexKey(a.ControlVertex); return a; }
            if (t == 1) { b.BoundaryKey = VertexKey(b.ControlVertex); return b; }
            Vector3 position = Lerp(a.Position, b.Position, t); position.y = height;
            Vector3 normal = Lerp(a.Normal, b.Normal, t);
            Require(normal.sqrMagnitude > 1e-16f, "Interpolated cut normal cancels; cannot invent a replacement normal.");
            return new Corner
            {
                Position = position, Normal = normal.normalized,
                Uv = new Vector2((float)(a.Uv.x + (b.Uv.x - (double)a.Uv.x) * t), (float)(a.Uv.y + (b.Uv.y - (double)a.Uv.y) * t)),
                ControlVertex = -1, BoundaryKey = ((ulong)(uint)a.ControlVertex << 32) | (uint)b.ControlVertex
            };
        }

        private static void Emit(Built built, Corner a, Corner b, Corner c)
        {
            Vector3 cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            double area = .5 * Math.Sqrt((double)cross.x * cross.x + (double)cross.y * cross.y + (double)cross.z * cross.z);
            if (area == 0) { built.Report.exactZeroAreaTrianglesOmitted++; return; }
            built.Report.minimumOutputTriangleArea = Math.Min(built.Report.minimumOutputTriangleArea, area);
            int first = built.Positions.Count;
            foreach (Corner corner in new[] { a, b, c })
            {
                Require(Finite(corner.Position.x) && Finite(corner.Position.y) && Finite(corner.Position.z) && Finite(corner.Normal.x) && Finite(corner.Normal.y) && Finite(corner.Normal.z) && Finite(corner.Uv.x) && Finite(corner.Uv.y), "Nonfinite emitted attribute.");
                built.Positions.Add(corner.Position); built.Normals.Add(corner.Normal); built.Uvs.Add(corner.Uv);
            }
            // Reflection in (-X,Z,-Y) reverses handedness. Keep source normal vectors and
            // reverse winding, rather than recalculating normals or relying on double-sidedness.
            built.Indices.Add(first); built.Indices.Add(first + 2); built.Indices.Add(first + 1);
        }

        private static Mesh BuildMesh(Built data, string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(data.Positions); mesh.SetNormals(data.Normals); mesh.SetUVs(0, data.Uvs);
            mesh.SetTriangles(data.Indices, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }

        private static ulong VertexKey(int vertex) { return 0x8000000000000000UL | (uint)vertex; }
        private static Vector3 Lerp(Vector3 a, Vector3 b, double t) { return new Vector3((float)(a.x + (b.x - (double)a.x) * t), (float)(a.y + (b.y - (double)a.y) * t), (float)(a.z + (b.z - (double)a.z) * t)); }
        private static Vector3 ToUnity(Vector3 value) { return new Vector3(-value.x, value.z, -value.y); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static T[] ArrayOf<T>(Node node, string name) { return (T[])node.Child(name).Properties[0]; }
        private static string Text(Node node, string name) { return (string)node.Child(name).Properties[0]; }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException("PH02 candidate: " + message); }

        [Serializable] public sealed class CutReport
        {
            public string schema = "citylife.ph02-crown-candidate.v1";
            public string status = "Unaccepted open-boundary candidate; semantic leaf completeness and attachment unverified";
            public string sourceAsset = SourceAssetPath, sourceSha256 = SourceSha256;
            public string sourceUrl = "https://polyhaven.com/a/quiver_tree_02", licenseUrl = "https://polyhaven.com/license";
            public string coordinates = "Metres/Y-up, (-X,Z,-Y); original source origin retained for full and cut meshes. FBX model scale100 cancels centimetre units; source X rotation is -90 degrees (exporter error below 0.000003 degrees). No pivot shift.";
            public string preservation = "Control-vertex normals mapped through NormalsIndex[controlVertex]; UVs through UVIndex[polygonCorner]. Unchanged corner attributes copied; cut positions/UVs linearly interpolated, cut normals interpolated then normalized. Reversed winding; derived tangents. Per-corner output vertices preserve seams; no cap/socket/weld.";
            public string limitation = "One stem-like section is geometric evidence, not proof of intact botanical leaves. Inspect source/cut/attached textured 3D views. No full-family score or previous procedural validation applies.";
            public int sourceControlVertices = 41073, sourceTriangles = 82074;
            public bool isClipped, boundaryAllDegreeTwo;
            public float cutHeightMetres;
            public int fullyKeptSourceTriangles, discardedSourceTriangles, clippedSourceTriangles, outputTriangles, outputCornerVertices, exactZeroAreaTrianglesOmitted;
            public int cutBoundarySegments, boundaryVertices, boundaryComponents;
            public Bounds sourceBounds, outputBounds, boundaryBounds;
            public Vector3 boundaryCentre;
            public double minimumOutputTriangleArea = double.PositiveInfinity;
        }
        private sealed class Built
        {
            public readonly List<Vector3> Positions = new List<Vector3>(), Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Indices = new List<int>();
            public readonly CutReport Report = new CutReport();
        }
        private struct Corner { public Vector3 Position, Normal; public Vector2 Uv; public int ControlVertex; public ulong BoundaryKey; }
        private sealed class Data
        {
            public double[] Positions, Normals, Uvs;
            public int[] Indices, NormalIndices, UvIndices;
            public Bounds Bounds;
            public int Vertex(int corner) { int encoded = Indices[corner]; return encoded < 0 ? -encoded - 1 : encoded; }
            public Vector3 Position(int vertex) { return ToUnity(new Vector3((float)Positions[vertex * 3], (float)Positions[vertex * 3 + 1], (float)Positions[vertex * 3 + 2])); }
            public Corner Corner(int corner)
            {
                int vertex = Vertex(corner), n = NormalIndices[vertex], u = UvIndices[corner];
                return new Corner { Position = Position(vertex), Normal = ToUnity(new Vector3((float)Normals[n * 3], (float)Normals[n * 3 + 1], (float)Normals[n * 3 + 2])), Uv = new Vector2((float)Uvs[u * 2], (float)Uvs[u * 2 + 1]), ControlVertex = vertex, BoundaryKey = VertexKey(vertex) };
            }
        }
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
