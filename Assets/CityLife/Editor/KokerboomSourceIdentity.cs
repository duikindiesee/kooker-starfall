using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World.Editor
{
    /// <summary>
    /// Read-only comparison of extracted PH01 leaves with Unity's actual imported mesh data.
    /// CollectJson starts no scene/render, changes no assets/importers and writes no files.
    /// The caller may retain its result beside the coordinated render evidence.
    /// </summary>
    public static class KokerboomSourceIdentity
    {
        private const float CellSize = .0001f, PositionTolerance = .00002f, UvTolerance = .00001f;

        public static string CollectJson() => JsonUtility.ToJson(Collect(), true);

        public static Report Collect()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(KokerboomSourceRosettes.SourceAssetPath);
            if (asset == null) throw new InvalidOperationException("PH01 imported asset is unavailable.");
            var report = new Report
            {
                sourceAsset = KokerboomSourceRosettes.SourceAssetPath,
                sourceSha256 = KokerboomSourceRosettes.SourceSha256,
                unityVersion = Application.unityVersion,
                rootPosition = asset.transform.position, rootEuler = asset.transform.eulerAngles, rootScale = asset.transform.lossyScale,
                positionToleranceMetres = PositionTolerance, uvTolerance = UvTolerance,
                normalDotThreshold = .9999f,
                scope = "Source identity diagnostic only; no appearance acceptance. Imported leaf slot includes closed basal pieces; extracted groups contain 21 open-base leaves each. Triangle statistics use each mesh's existing triangulation. Reconstructed source roots are added back before matching. Tangents may differ because extraction currently duplicates polygon corners before RecalculateTangents."
            };
            var importer = AssetImporter.GetAtPath(KokerboomSourceRosettes.SourceAssetPath) as ModelImporter;
            if (importer != null)
            {
                report.normalImportMode = importer.importNormals.ToString(); report.tangentImportMode = importer.importTangents.ToString();
                report.globalScale = importer.globalScale; report.useFileUnits = importer.useFileUnits;
                report.bakeAxisConversion = importer.bakeAxisConversion; report.weldVertices = importer.weldVertices;
            }
            report.textures = new[] { KokerboomSourceRosettes.DiffusePath, KokerboomSourceRosettes.NormalPath, KokerboomSourceRosettes.RoughnessPath }.Select(TextureSettings).ToArray();
            var imported = new List<Corner>(); var importedStats = new TriangleStats(); var meshes = new List<ImportedMesh>();
            foreach (var filter in asset.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh; var renderer = filter.GetComponent<Renderer>();
                if (mesh == null || renderer == null) continue;
                var materials = renderer.sharedMaterials;
                using (var dataArray = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    Mesh.MeshData data = dataArray[0];
                    if (!data.HasVertexAttribute(VertexAttribute.Normal) || !data.HasVertexAttribute(VertexAttribute.TexCoord0))
                        throw new InvalidOperationException("Imported mesh lacks normals or UV0.");
                    using (var p = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    using (var n = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    using (var u = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp))
                    using (var t = new NativeArray<Vector4>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetVertices(p); data.GetNormals(n); data.GetUVs(0, u);
                        bool hasTangents = data.HasVertexAttribute(VertexAttribute.Tangent);
                        if (hasTangents) data.GetTangents(t);
                        Matrix4x4 matrix = filter.transform.localToWorldMatrix, normalMatrix = matrix.inverse.transpose;
                        float handedness = Mathf.Sign(matrix.determinant);
                        var vertices = new Corner[data.vertexCount];
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            Vector3 tangent = matrix.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized;
                            vertices[i] = new Corner { P = matrix.MultiplyPoint3x4(p[i]), N = normalMatrix.MultiplyVector(n[i]).normalized, U = u[i], T = new Vector4(tangent.x, tangent.y, tangent.z, t[i].w * handedness) };
                        }
                        var selected = new HashSet<int>(); int leafTriangles = 0;
                        for (int slot = 0; slot < data.subMeshCount; slot++)
                        {
                            if (slot >= materials.Length || materials[slot] == null || materials[slot].name.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            SubMeshDescriptor sub = data.GetSubMesh(slot);
                            if (sub.topology != MeshTopology.Triangles) throw new InvalidOperationException("Imported PH01 leaf topology is not triangles.");
                            using (var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp))
                            {
                                data.GetIndices(indices, slot, true);
                                for (int i = 0; i < indices.Length; i++) selected.Add(indices[i]);
                                for (int i = 0; i < indices.Length; i += 3) importedStats.Add(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
                                leafTriangles += indices.Length / 3;
                            }
                        }
                        foreach (int i in selected) imported.Add(vertices[i]);
                        meshes.Add(new ImportedMesh { name = mesh.name, vertices = data.vertexCount, selectedLeafVertices = selected.Count, selectedLeafTriangles = leafTriangles, localBounds = mesh.bounds, worldMatrix = matrix, hasTangents = hasTangents, materialNames = materials.Select(m => m == null ? "" : m.name).ToArray() });
                    }
                }
            }
            if (imported.Count == 0) throw new InvalidOperationException("No imported PH01 leaf material vertices found.");
            var grid = new Dictionary<Vector3Int, List<Corner>>();
            foreach (Corner corner in imported)
            {
                Vector3Int cell = Cell(corner.P);
                if (!grid.TryGetValue(cell, out List<Corner> list)) { list = new List<Corner>(); grid.Add(cell, list); }
                list.Add(corner);
            }
            report.importedMeshes = meshes.ToArray(); report.importedLeafVertices = imported.Count; report.importedLeafTriangleStats = importedStats;
            Mesh[] extracted = KokerboomSourceRosettes.CreateRosettes(false);
            try
            {
                var description = KokerboomSourceRosettes.Describe();
                report.groups = extracted.Select((mesh, i) => Compare(mesh, description.groups[i].id, description.groups[i].sourceRootUnity, grid)).ToArray();
                report.allCornersMatchImportedPositionNormalUv = report.groups.All(g => g.positionNormalUvMatches == g.vertices);
            }
            finally { foreach (Mesh mesh in extracted) UnityEngine.Object.DestroyImmediate(mesh); }
            return report;
        }

        private static GroupReport Compare(Mesh mesh, string id, Vector3 root, Dictionary<Vector3Int, List<Corner>> grid)
        {
            Vector3[] p = mesh.vertices, n = mesh.normals; Vector2[] uv = mesh.uv; Vector4[] tangent = mesh.tangents;
            var report = new GroupReport { id = id, vertices = p.Length, triangles = mesh.triangles.Length / 3, sourceRootUnity = root, rootedBounds = mesh.bounds };
            var exact = new HashSet<Tuple<Vector3, Vector3, Vector2>>(); var corners = new Corner[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                exact.Add(Tuple.Create(p[i], n[i], uv[i]));
                corners[i] = new Corner { P = p[i] + root, N = n[i], U = uv[i], T = tangent[i] };
                report.minimumNormalLength = Mathf.Min(report.minimumNormalLength, n[i].magnitude);
                report.maximumNormalLength = Mathf.Max(report.maximumNormalLength, n[i].magnitude);
                Vector3Int center = Cell(corners[i].P); bool positionMatch = false, uvMatch = false, flipMatch = false;
                float nearest = float.PositiveInfinity, bestUv = float.PositiveInfinity, bestDot = -2f, bestTangent = -2f; bool handednessMatch = false;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    if (!grid.TryGetValue(center + new Vector3Int(x, y, z), out List<Corner> list)) continue;
                    foreach (Corner candidate in list)
                    {
                        float distance = Vector3.Distance(corners[i].P, candidate.P);
                        if (distance > PositionTolerance) continue;
                        positionMatch = true; nearest = Mathf.Min(nearest, distance);
                        float uvError = Vector2.Distance(uv[i], candidate.U); bestUv = Mathf.Min(bestUv, uvError);
                        if (Vector2.Distance(new Vector2(uv[i].x, 1f - uv[i].y), candidate.U) <= UvTolerance) flipMatch = true;
                        if (uvError > UvTolerance) continue;
                        uvMatch = true; float dot = Vector3.Dot(n[i].normalized, candidate.N.normalized); bestDot = Mathf.Max(bestDot, dot);
                        if (dot < .9999f) continue;
                        float tangentDot = Vector3.Dot(new Vector3(tangent[i].x, tangent[i].y, tangent[i].z), new Vector3(candidate.T.x, candidate.T.y, candidate.T.z));
                        bestTangent = Mathf.Max(bestTangent, tangentDot);
                        if (Mathf.Sign(tangent[i].w) == Mathf.Sign(candidate.T.w)) handednessMatch = true;
                    }
                }
                if (positionMatch) { report.positionMatches++; report.maximumNearestPositionError = Mathf.Max(report.maximumNearestPositionError, nearest); report.maximumMinimumUvErrorAtPosition = Mathf.Max(report.maximumMinimumUvErrorAtPosition, bestUv); }
                if (flipMatch) report.positionFlippedVMatches++;
                if (uvMatch) { report.positionUvMatches++; report.minimumBestNormalDot = Mathf.Min(report.minimumBestNormalDot, bestDot); if (bestDot < 0f) report.oppositeNormalCorners++; }
                if (bestDot >= .9999f)
                {
                    report.positionNormalUvMatches++;
                    report.minimumBestTangentDot = Mathf.Min(report.minimumBestTangentDot, bestTangent);
                    if (bestTangent >= .9999f) report.tangentDirectionMatches++;
                    if (handednessMatch) report.tangentHandednessMatches++;
                }
                if ((!positionMatch || !uvMatch || bestDot < .9999f) && report.firstMismatches.Count < 8)
                    report.firstMismatches.Add(new Mismatch { corner = i, position = corners[i].P, normal = n[i], uv = uv[i], positionMatched = positionMatch, uvMatched = uvMatch, bestNormalDot = bestDot });
            }
            report.exactPositionNormalUvVertices = exact.Count;
            int[] indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3) report.triangleStats.Add(corners[indices[i]], corners[indices[i + 1]], corners[indices[i + 2]]);
            return report;
        }

        private static Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize), Mathf.FloorToInt(p.z / CellSize));
        private static TextureReport TextureSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            return importer == null ? new TextureReport { path = path } : new TextureReport { path = path, type = importer.textureType.ToString(), srgb = importer.sRGBTexture, flipGreen = importer.flipGreenChannel, mipmaps = importer.mipmapEnabled, compression = importer.textureCompression.ToString(), filter = importer.filterMode.ToString(), aniso = importer.anisoLevel };
        }
        internal struct Corner { public Vector3 P, N; public Vector2 U; public Vector4 T; }
        [Serializable] public sealed class Report
        {
            public string schema = "citylife.ph01-source-identity.v1", sourceAsset, sourceSha256, unityVersion, scope, normalImportMode, tangentImportMode;
            public Vector3 rootPosition, rootEuler, rootScale;
            public float positionToleranceMetres, uvTolerance, normalDotThreshold, globalScale;
            public bool useFileUnits, bakeAxisConversion, weldVertices, allCornersMatchImportedPositionNormalUv;
            public int importedLeafVertices;
            public ImportedMesh[] importedMeshes; public TextureReport[] textures; public GroupReport[] groups; public TriangleStats importedLeafTriangleStats;
        }
        [Serializable] public sealed class ImportedMesh { public string name; public int vertices, selectedLeafVertices, selectedLeafTriangles; public Bounds localBounds; public Matrix4x4 worldMatrix; public bool hasTangents; public string[] materialNames; }
        [Serializable] public sealed class TextureReport { public string path, type, compression, filter; public bool srgb, flipGreen, mipmaps; public int aniso; }
        [Serializable] public sealed class GroupReport
        {
            public string id; public Vector3 sourceRootUnity; public Bounds rootedBounds;
            public int vertices, triangles, exactPositionNormalUvVertices, positionMatches, positionUvMatches, positionFlippedVMatches, positionNormalUvMatches, oppositeNormalCorners, tangentDirectionMatches, tangentHandednessMatches;
            public float maximumNearestPositionError, maximumMinimumUvErrorAtPosition, minimumBestNormalDot = 1f, minimumNormalLength = 2f, maximumNormalLength, minimumBestTangentDot = 1f;
            public TriangleStats triangleStats = new TriangleStats(); public List<Mismatch> firstMismatches = new List<Mismatch>();
        }
        [Serializable] public sealed class Mismatch { public int corner; public Vector3 position, normal; public Vector2 uv; public bool positionMatched, uvMatched; public float bestNormalDot; }
        [Serializable] public sealed class TriangleStats
        {
            public int triangles, degenerateTriangles, normalComparisons, negativeNormalDots; public float minimumDot = 1f, meanDot; private double sum;
            internal void Add(Corner a, Corner b, Corner c)
            {
                triangles++; Vector3 cross = Vector3.Cross(b.P - a.P, c.P - a.P);
                if (cross.sqrMagnitude < 1e-20f) { degenerateTriangles++; return; }
                Vector3 face = cross.normalized;
                foreach (Vector3 n in new[] { a.N, b.N, c.N })
                {
                    float dot = Vector3.Dot(face, n.normalized); minimumDot = Mathf.Min(minimumDot, dot);
                    if (dot < 0f) negativeNormalDots++; sum += dot; normalComparisons++;
                }
                meanDot = (float)(sum / normalComparisons);
            }
        }
    }
}
