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
    /// Editor-only extraction of the five measured PH01 source rosettes. Reads the pinned FBX;
    /// writes no assets, settings, images or saves and does not start a render. Returned meshes
    /// belong to the caller. Geometry is an unaccepted hybrid experiment, not species certification.
    /// </summary>
    public static class KokerboomSourceRosettes
    {
        public const string SourceAssetPath = "Assets/CityLife/Art/PolyHaven/QuiverTree01/quiver_tree_01_2k.fbx";
        public const string SourceSha256 = "1ecf41e21e4e42ab55e5374f8302d08533d40540507314966f01f5c393e851c5";
        public const string SourceMaterialName = "quiver_tree_01_leaf";
        public const string DiffusePath = "Assets/CityLife/Art/PolyHaven/QuiverTree01/textures/quiver_tree_01_leaf_diff_2k.png";
        public const string NormalPath = "Assets/CityLife/Art/PolyHaven/QuiverTree01/textures/quiver_tree_01_leaf_nor_gl_2k.png";
        public const string RoughnessPath = "Assets/CityLife/Art/PolyHaven/QuiverTree01/textures/quiver_tree_01_leaf_rough_2k.png";

        // A-E are the stable five groups found from the means of 12-edge open leaf bases.
        // Source geometry is Z-up. These are measurements, not newly generated sockets.
        private static readonly Vector3[] MeasuredRoots =
        {
            new Vector3(-.177026f, -.238597f, 2.205152f),
            new Vector3(-.112955f, -.050617f, 2.205615f),
            new Vector3(-.308169f, -.097827f, 2.223721f),
            new Vector3(.014842f, .124948f, 2.375137f),
            new Vector3(.279492f, .192154f, 2.394056f)
        };
        private static Data cached;

        /// <summary>
        /// Returns A-E at original source scale, each with 21 complete leaves. Each group's
        /// measured mean leaf base is the origin. includeBasal adds nearby closed source pieces
        /// whose semantic identity remains unverified; it creates no caps or connecting geometry.
        /// </summary>
        public static Mesh[] CreateRosettes(bool includeBasal = false)
        {
            Data data = Load();
            var meshes = new Mesh[5];
            for (int i = 0; i < meshes.Length; i++)
            {
                IEnumerable<Component> selected = data.Groups[i].Leaves;
                if (includeBasal) selected = selected.Concat(data.Groups[i].Basal);
                meshes[i] = BuildMesh(data, i, selected, includeBasal ? "leaves and unreviewed basal pieces" : "21 original leaves");
            }
            return meshes;
        }

        /// <summary>
        /// Returns only the five associated basal groups, using exactly the same pivots as
        /// CreateRosettes. Assignment is nearest source-component centroid, not botanical identity.
        /// </summary>
        public static Mesh[] CreateBasalMeshes()
        {
            Data data = Load();
            var meshes = new Mesh[5];
            for (int i = 0; i < meshes.Length; i++) meshes[i] = BuildMesh(data, i, data.Groups[i].Basal, "unreviewed basal pieces only");
            return meshes;
        }

        public static ExtractionReport Describe()
        {
            Data data = Load();
            return new ExtractionReport
            {
                schema = "citylife.ph01-rosette-extraction.v1",
                status = "Extractable source geometry; hybrid appearance and botanical identity not accepted",
                sourceAsset = SourceAssetPath, sourceSha256 = SourceSha256,
                sourceUrl = "https://polyhaven.com/a/quiver_tree_01", licenseUrl = "https://polyhaven.com/license",
                sourceMaterial = SourceMaterialName, diffuse = DiffusePath, normalOpenGL = NormalPath, roughness = RoughnessPath,
                coordinates = "Source FBX mesh coordinates are Z-up. Output (-X,Z,-Y) matches the observed Unity FBX handedness/orientation at metre scale; each measured root mean is subtracted before conversion. Source model scale100 and centimetre file units cancel. No shape normalization or per-rosette straightening.",
                preservation = "Whole source polygons; quad fan triangulation; source polygon-corner UVs and normals copied. Reflection reverses winding. Tangents are derived for normal mapping; normals are not recalculated. No caps, sockets, welds or replacement leaves.",
                basalPolicy = "11 closed leaf-material components assigned by nearest component centroid to a measured leaf-root group. Optional/separate; their semantic identity and suitability remain unverified.",
                sourceLeafComponents = 105, sourceClosedComponents = 11,
                groups = data.Groups.Select((g, i) => DescribeGroup(data, i)).ToArray()
            };
        }

        public static string DescribeJson() => JsonUtility.ToJson(Describe(), true);

        private static Data Load()
        {
            if (cached != null) return cached;
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), SourceAssetPath);
            byte[] bytes = File.ReadAllBytes(path);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            Require(hash == SourceSha256, "PH01 source hash changed; re-inspect clustering instead of applying old measurements.");
            List<Node> nodes = FbxReader.Read(bytes);
            Node geometry = nodes.Single(n => n.Name == "Objects").Children.Single(n => n.Name == "Geometry");
            var data = new Data
            {
                Positions = ArrayOf<double>(geometry, "Vertices"),
                PolygonIndices = ArrayOf<int>(geometry, "PolygonVertexIndex")
            };
            Node normal = geometry.Child("LayerElementNormal"), uv = geometry.Child("LayerElementUV"), material = geometry.Child("LayerElementMaterial");
            Require(Text(normal, "MappingInformationType") == "ByPolygonVertex" && Text(normal, "ReferenceInformationType") == "IndexToDirect", "Unexpected source normal mapping.");
            Require(Text(uv, "MappingInformationType") == "ByPolygonVertex" && Text(uv, "ReferenceInformationType") == "IndexToDirect", "Unexpected source UV mapping.");
            Require(Text(material, "MappingInformationType") == "ByPolygon", "Unexpected source material mapping.");
            data.Normals = ArrayOf<double>(normal, "Normals"); data.NormalIndices = ArrayOf<int>(normal, "NormalsIndex");
            data.Uv = ArrayOf<double>(uv, "UV"); data.UvIndices = ArrayOf<int>(uv, "UVIndex");
            int[] materials = ArrayOf<int>(material, "Materials");
            Require(data.Positions.Length == 75812 * 3 && data.NormalIndices.Length == data.PolygonIndices.Length && data.UvIndices.Length == data.PolygonIndices.Length, "Unexpected PH01 geometry dimensions.");

            var leafFaces = new List<Face>();
            var polygon = new List<int>(4);
            int firstCorner = 0, polygonId = 0;
            for (int corner = 0; corner < data.PolygonIndices.Length; corner++)
            {
                int encoded = data.PolygonIndices[corner], vertex = encoded < 0 ? -encoded - 1 : encoded;
                Require(vertex >= 0 && vertex < data.Positions.Length / 3, "Invalid source control vertex.");
                polygon.Add(vertex);
                if (encoded >= 0) continue;
                Require(polygonId < materials.Length && polygon.Count >= 3 && polygon.Count <= 4, "Unexpected source polygon.");
                if (materials[polygonId] == 1) leafFaces.Add(new Face { Vertices = polygon.ToArray(), FirstCorner = firstCorner, PolygonId = polygonId });
                polygon.Clear(); polygonId++; firstCorner = corner + 1;
            }
            Require(polygon.Count == 0 && polygonId == materials.Length && leafFaces.Count == 89610, "Incomplete leaf polygon selection.");

            var union = new UnionFind(data.Positions.Length / 3);
            foreach (Face face in leafFaces) for (int i = 1; i < face.Vertices.Length; i++) union.Join(face.Vertices[0], face.Vertices[i]);
            var components = new Dictionary<int, Component>();
            foreach (Face face in leafFaces)
            {
                int root = union.Find(face.Vertices[0]);
                if (!components.TryGetValue(root, out Component component)) { component = new Component(); components.Add(root, component); }
                component.Faces.Add(face);
            }
            var ordered = components.Values.OrderBy(c => c.Faces[0].PolygonId).ToArray();
            data.Groups = Enumerable.Range(0, 5).Select(_ => new Group()).ToArray();
            int openCount = 0, closedCount = 0;
            for (int i = 0; i < ordered.Length; i++)
            {
                Component component = ordered[i]; component.Id = i;
                InspectComponent(data, component);
                if (component.BaseVertices.Length == 0)
                {
                    data.Groups[Nearest(component.Centroid)].Basal.Add(component); closedCount++;
                }
                else
                {
                    Require(component.BaseVertices.Length == 12 && component.BoundaryEdges == 12, "A source leaf is no longer an open 12-edge-base component.");
                    int group = Nearest(component.Root);
                    Require(Vector3.Distance(component.Root, MeasuredRoots[group]) < .07f, "Source leaf root lies outside its measured rosette.");
                    data.Groups[group].Leaves.Add(component); openCount++;
                }
            }
            Require(openCount == 105 && closedCount == 11, "Expected 105 open-base leaves and 11 closed source components.");
            int[] basalCounts = { 2, 2, 2, 3, 2 };
            for (int i = 0; i < data.Groups.Length; i++)
            {
                Group group = data.Groups[i];
                Require(group.Leaves.Count == 21 && group.Basal.Count == basalCounts[i], "Measured source rosette membership changed.");
                group.Root = Mean(group.Leaves.Select(c => c.Root));
                Require(Vector3.Distance(group.Root, MeasuredRoots[i]) < .00001f, "Measured source root centre changed.");
                Require(group.Leaves.Sum(c => c.Triangles) == (i == 0 ? 9456 : 9408), "Source leaf triangle count changed.");
            }
            cached = data;
            return data;
        }

        private static void InspectComponent(Data data, Component component)
        {
            var vertices = new HashSet<int>();
            var edges = new Dictionary<ulong, int>();
            foreach (Face face in component.Faces)
            {
                component.Triangles += face.Vertices.Length - 2;
                for (int i = 0; i < face.Vertices.Length; i++)
                {
                    int a = face.Vertices[i], b = face.Vertices[(i + 1) % face.Vertices.Length];
                    vertices.Add(a);
                    ulong key = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                    edges.TryGetValue(key, out int count); edges[key] = count + 1;
                }
            }
            Require(edges.Values.All(n => n <= 2), "Nonmanifold source leaf component.");
            var boundary = new HashSet<int>();
            foreach (var pair in edges) if (pair.Value == 1)
            {
                boundary.Add((int)(pair.Key >> 32)); boundary.Add((int)(pair.Key & uint.MaxValue)); component.BoundaryEdges++;
            }
            component.Vertices = vertices.OrderBy(x => x).ToArray(); component.BaseVertices = boundary.OrderBy(x => x).ToArray();
            component.Centroid = Mean(component.Vertices.Select(data.Position));
            if (component.BaseVertices.Length > 0) component.Root = Mean(component.BaseVertices.Select(data.Position));
        }

        private static Mesh BuildMesh(Data data, int groupIndex, IEnumerable<Component> components, string suffix)
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            Vector3 root = data.Groups[groupIndex].Root;
            foreach (Component component in components) foreach (Face face in component.Faces)
            {
                int first = positions.Count;
                for (int i = 0; i < face.Vertices.Length; i++)
                {
                    int corner = face.FirstCorner + i, normalIndex = data.NormalIndices[corner], uvIndex = data.UvIndices[corner];
                    Require(normalIndex >= 0 && normalIndex * 3 + 2 < data.Normals.Length && uvIndex >= 0 && uvIndex * 2 + 1 < data.Uv.Length, "Invalid polygon-corner normal/UV index.");
                    positions.Add(ToUnity(data.Position(face.Vertices[i]) - root));
                    normals.Add(ToUnity(new Vector3((float)data.Normals[normalIndex * 3], (float)data.Normals[normalIndex * 3 + 1], (float)data.Normals[normalIndex * 3 + 2])));
                    uv.Add(new Vector2((float)data.Uv[uvIndex * 2], (float)data.Uv[uvIndex * 2 + 1]));
                }
                for (int i = 1; i < face.Vertices.Length - 1; i++)
                {
                    // The X reflection reverses handedness; reverse each source triangle.
                    triangles.Add(first); triangles.Add(first + i + 1); triangles.Add(first + i);
                }
            }
            var mesh = new Mesh { name = "PH01 source rosette " + (char)('A' + groupIndex) + " - " + suffix };
            if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }

        private static GroupReport DescribeGroup(Data data, int index)
        {
            Group group = data.Groups[index];
            Vector3 first = ToUnity(data.Position(group.Leaves[0].Vertices[0]) - group.Root);
            var bounds = new Bounds(first, Vector3.zero);
            foreach (Component c in group.Leaves) foreach (int vertex in c.Vertices) bounds.Encapsulate(ToUnity(data.Position(vertex) - group.Root));
            return new GroupReport
            {
                id = ((char)('A' + index)).ToString(), sourceRootFbx = group.Root, sourceRootUnity = ToUnity(group.Root), rootedLeafBounds = bounds,
                leafComponents = group.Leaves.Count, leafTriangles = group.Leaves.Sum(c => c.Triangles),
                sourceLeafControlVertices = group.Leaves.Sum(c => c.Vertices.Length),
                emittedLeafCornerVertices = group.Leaves.Sum(c => c.Faces.Sum(f => f.Vertices.Length)),
                sourceLeafComponentIds = group.Leaves.Select(c => c.Id).ToArray(),
                basalComponents = group.Basal.Count, basalTriangles = group.Basal.Sum(c => c.Triangles),
                sourceBasalComponentIds = group.Basal.Select(c => c.Id).ToArray()
            };
        }

        private static Vector3 ToUnity(Vector3 p) => new Vector3(-p.x, p.z, -p.y);
        private static Vector3 Mean(IEnumerable<Vector3> points)
        {
            double x = 0, y = 0, z = 0; int count = 0;
            foreach (Vector3 p in points) { x += p.x; y += p.y; z += p.z; count++; }
            Require(count > 0, "Cannot average an empty source group.");
            return new Vector3((float)(x / count), (float)(y / count), (float)(z / count));
        }
        private static int Nearest(Vector3 point)
        {
            int nearest = 0; float distance = float.PositiveInfinity;
            for (int i = 0; i < MeasuredRoots.Length; i++) { float candidate = (MeasuredRoots[i] - point).sqrMagnitude; if (candidate < distance) { distance = candidate; nearest = i; } }
            return nearest;
        }
        private static T[] ArrayOf<T>(Node parent, string child) => (T[])parent.Child(child).Properties[0];
        private static string Text(Node parent, string child) => (string)parent.Child(child).Properties[0];
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException("PH01 extraction: " + message); }

        [Serializable] public sealed class ExtractionReport
        {
            public string schema, status, sourceAsset, sourceSha256, sourceUrl, licenseUrl, sourceMaterial, diffuse, normalOpenGL, roughness, coordinates, preservation, basalPolicy;
            public int sourceLeafComponents, sourceClosedComponents;
            public GroupReport[] groups;
        }
        [Serializable] public sealed class GroupReport
        {
            public string id;
            public Vector3 sourceRootFbx, sourceRootUnity;
            public Bounds rootedLeafBounds;
            public int leafComponents, leafTriangles, sourceLeafControlVertices, emittedLeafCornerVertices, basalComponents, basalTriangles;
            public int[] sourceLeafComponentIds, sourceBasalComponentIds;
        }
        private sealed class Data
        {
            public double[] Positions, Normals, Uv;
            public int[] PolygonIndices, NormalIndices, UvIndices;
            public Group[] Groups;
            public Vector3 Position(int vertex) => new Vector3((float)Positions[vertex * 3], (float)Positions[vertex * 3 + 1], (float)Positions[vertex * 3 + 2]);
        }
        private sealed class Group { public Vector3 Root; public readonly List<Component> Leaves = new List<Component>(); public readonly List<Component> Basal = new List<Component>(); }
        private sealed class Face { public int[] Vertices; public int FirstCorner, PolygonId; }
        private sealed class Component
        {
            public int Id, BoundaryEdges, Triangles;
            public int[] Vertices, BaseVertices;
            public Vector3 Root, Centroid;
            public readonly List<Face> Faces = new List<Face>();
        }
        private sealed class UnionFind
        {
            private readonly int[] parents;
            public UnionFind(int count) { parents = Enumerable.Range(0, count).ToArray(); }
            public int Find(int x) { int root = x; while (parents[root] != root) root = parents[root]; while (parents[x] != x) { int next = parents[x]; parents[x] = root; x = next; } return root; }
            public void Join(int a, int b) { parents[Find(a)] = Find(b); }
        }
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
