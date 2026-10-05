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
    /// Staged, unaccepted PH02 attachment experiment. No scene, asset, shader or source mutation.
    /// Crown scale/origin are retained. The support touches the actual cut rim but is a separate
    /// mesh, not a welded whole tree. Caller owns both returned meshes. Inspect the underside.
    /// </summary>
    public static class PH02FittedSupportCandidate
    {
        [Serializable] public sealed class Options
        {
            public float transitionMetres = .25f, lowerExtensionMetres = .18f;
            public float roundRadiusScale = 1.12f, bottomRadiusScale = 1.12f;
            public Vector2 lowerBendMetres = new Vector2(.045f, .020f);
            public int transitionSegments = 16, lowerSegments = 8;
            public bool flatLowerTangent;
        }
        public sealed class Result : IDisposable
        {
            public Mesh Crown, Support;
            public Report Metrics;
            public void Dispose()
            {
                if (Crown != null) UnityEngine.Object.DestroyImmediate(Crown);
                if (Support != null) UnityEngine.Object.DestroyImmediate(Support);
                Crown = null; Support = null;
            }
        }
        public sealed class SupportData
        {
            public Vector3[] Positions, Normals;
            public Vector2[] AtlasUv, BranchUv;
            public Vector4[] Tangents;
            public Color[] BlendWeights;
            public int[] Triangles;
            public Report Metrics;
        }
        [Serializable] public sealed class Report
        {
            public string schema = "starfall.ph02-fitted-support-candidate.v1";
            public string status = "Unaccepted separate-mesh attachment candidate; no whole-tree or material acceptance";
            public string sourceAsset = PH02CrownCandidate.SourceAssetPath, sourceSha256 = PH02CrownCandidate.SourceSha256;
            public string crownMeshSha256Before, crownMeshSha256After;
            public string crownInput="Pinned raw PH02 clipper", callerCrownSha256Before, callerCrownSha256After;
            public bool callerCloneUsed, callerCrownUnchanged, cloneMatchesCaller;
            public string coordinates = "Original PH02 metre-scale Y-up coordinates retained. Crown positions, scale, attributes and indices are never altered.";
            public string seamPolicy = "Directed geometric rim ordered by source triangle connectivity. Attribute-identical support vertices may share indices; source UV/normal/tangent differences and the branch UV wrap remain explicit splits. No recalculated crown normals or tangents.";
            public string materialPlan = "At the top, UV0/normals/tangents exactly match each adjacent crown triangle. Below, UV0 extrapolates local source atlas gradients. Gradients are shared only at identical position/UV/normal endpoints with matching UV handedness; actual seams remain split. This is not a semantic stem mask. Extrapolation may cross atlas islands and needs textured review. Vertex colour R is proposed pale-procedural-bark weight: 0 at the source rim, smoothly 1 at the end of the transition. UV1 is branch circumference fraction and depth in metres. Root-owned shader must blend albedo/normal/roughness coherently; no shader or mask interpretation is implemented here.";
            public string topologyScope = "Support is an open tube at both ends, geometrically continuous across its own attribute seams. Crown and support remain separately indexed; zero boundary gap is not a welded whole-tree claim. No collar, socket lip, overlapping cap or decorative cover ring is added.";
            public string visualAcceptance = "Pending actual neutral, textured and underside comparison; numeric checks do not award a score.";
            public float cutHeightMetres, transitionMetres, lowerExtensionMetres, sourceMeanRimRadius, roundRadiusMetres;
            public Vector2 lowerBendMetres;
            public Vector3 rimCentre, lowerCentre;
            public int crownVertices, crownTriangles, rimVertices, rimSegments, rimComponents;
            public int rimUvSeamPositions, rimNormalSeamPositions, rimTangentSeamPositions;
            public int sharedAtlasGradientPositions, preservedAtlasGradientSeams;
            public int supportVertices, supportTriangles, ringCount, copiedTopCorners, tangentFallbacks;
            public int geometricBoundaryEdges, geometricNonManifoldEdges, geometricWindingConflicts, indexedBoundaryEdges;
            public int nonfiniteAttributes, nonunitNormals, degenerateTriangles, windingAgainstNormals;
            public float boundaryFitMaximumGap, topNormalMaximumError, topUvMaximumError, topTangentMaximumError;
            public float minimumFaceNormalDot = 1f, maximumRadiusOverEndpointEnvelope;
            public double minimumTriangleArea = double.PositiveInfinity;
            public bool sourceTangentsAvailable, ringOrderedConnectivity, allCutEdgesReversed, crownUnchanged;
            public bool finiteNormals, nondegenerate, windingConsistent, numericChecksPassed, flatLowerTangent;
        }

        public static Result Create(Options options = null)
        {
            return CreateOwned(PH02CrownCandidate.CreateCrown(.65f),.65f,options);
        }

        /// <summary>
        /// Builds from an owned clone of a caller-supplied clipped crown. The caller retains
        /// ownership of its input; the returned Result owns only the clone and new support.
        /// Hashes cover P/N/UV/tangent4 and all submesh indices. Source identity belongs to the
        /// caller's adapter report; this generic overload does not certify arbitrary input.
        /// </summary>
        public static Result Create(Mesh callerCrown,float cutHeight,Options options = null)
        {
            Require(callerCrown!=null,"Caller crown is required.");
            string before=MeshHash(callerCrown);
            Result result=CreateOwned(UnityEngine.Object.Instantiate(callerCrown),cutHeight,options);
            try
            {
            result.Metrics.crownInput="Owned clone of caller-supplied crown; source identity requires the separate adapter report";
            result.Metrics.sourceAsset="";result.Metrics.sourceSha256="";
            result.Metrics.coordinates="Caller mesh coordinates/scale retained; source correspondence is established by the separate adapter report.";
            result.Metrics.callerCloneUsed=true;
            result.Metrics.callerCrownSha256Before=before;
            result.Metrics.callerCrownSha256After=MeshHash(callerCrown);
            result.Metrics.callerCrownUnchanged=before==result.Metrics.callerCrownSha256After;
            result.Metrics.cloneMatchesCaller=before==result.Metrics.crownMeshSha256Before;
            result.Metrics.numericChecksPassed&=result.Metrics.callerCrownUnchanged&&result.Metrics.cloneMatchesCaller;
            return result;
            }
            catch{result.Dispose();throw;}
        }

        private static Result CreateOwned(Mesh ownedCrown,float cutHeight,Options options)
        {
            var result = new Result { Crown=ownedCrown };
            try
            {
                string before = MeshHash(result.Crown);
                SupportData data = BuildData(result.Crown.vertices, result.Crown.normals, result.Crown.uv,
                    result.Crown.tangents, result.Crown.triangles, cutHeight, options);
                result.Support = new Mesh { name = "PH02 fitted irregular-rim support - unaccepted", indexFormat = IndexFormat.UInt32 };
                result.Support.vertices = data.Positions; result.Support.normals = data.Normals;
                result.Support.uv = data.AtlasUv; result.Support.uv2 = data.BranchUv;
                result.Support.tangents = data.Tangents; result.Support.colors = data.BlendWeights;
                result.Support.triangles = data.Triangles; result.Support.RecalculateBounds();
                // Do not call RecalculateNormals/Tangents: that would overwrite the exact top seam.
                data.Metrics.crownMeshSha256Before = before;
                data.Metrics.crownMeshSha256After = MeshHash(result.Crown);
                data.Metrics.crownUnchanged = before == data.Metrics.crownMeshSha256After;
                data.Metrics.numericChecksPassed &= data.Metrics.crownUnchanged;
                result.Metrics = data.Metrics;
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        /// <summary>
        /// Pure buffer construction for independent CPU checks; arrays are read only. The caller
        /// supplies actual Crown mesh buffers. Missing source tangents are explicitly reported.
        /// This method does not create a Unity Mesh or claim source identity for arbitrary arrays.
        /// </summary>
        public static SupportData BuildData(Vector3[] p, Vector3[] n, Vector2[] uv, Vector4[] tangent,
            int[] triangles, float cutHeight, Options settings = null)
        {
            Options o = settings ?? new Options();
            Require(o.transitionMetres >= .2f && o.transitionMetres <= .3f && Finite(o.transitionMetres), "Transition must be 0.20-0.30 m.");
            Require(o.lowerExtensionMetres > 0 && o.lowerExtensionMetres <= .5f && Finite(o.lowerExtensionMetres), "Invalid lower extension.");
            Require(o.transitionSegments >= 4 && o.transitionSegments <= 64 && o.lowerSegments >= 2 && o.lowerSegments <= 64, "Invalid ring density.");
            Require(Finite(o.roundRadiusScale) && o.roundRadiusScale >= .8f && o.roundRadiusScale <= 1.5f &&
                Finite(o.bottomRadiusScale) && o.bottomRadiusScale >= 1f && o.bottomRadiusScale <= 1.4f, "Invalid gentle taper.");
            Require(Finite(o.lowerBendMetres) && o.lowerBendMetres.magnitude <= .12f, "Bend exceeds this bounded experiment.");
            Require(p != null && n != null && uv != null && triangles != null && p.Length == n.Length && p.Length == uv.Length && triangles.Length % 3 == 0, "Incomplete crown buffers.");
            var report = new Report {
                cutHeightMetres = cutHeight, transitionMetres = o.transitionMetres, lowerExtensionMetres = o.lowerExtensionMetres,
                lowerBendMetres = o.lowerBendMetres, crownVertices = p.Length, crownTriangles = triangles.Length / 3,
                sourceTangentsAvailable = tangent != null && tangent.Length == p.Length
            };
            var outgoing = new Dictionary<Vector3, Segment>();
            var incoming = new Dictionary<Vector3, Segment>();
            for (int f = 0; f < triangles.Length; f += 3)
            {
                int ia = triangles[f], ib = triangles[f + 1], ic = triangles[f + 2];
                Require(ia >= 0 && ib >= 0 && ic >= 0 && ia < p.Length && ib < p.Length && ic < p.Length, "Invalid crown triangle.");
                int[] corners = { ia, ib, ic };
                for (int edge = 0; edge < 3; edge++)
                {
                    int a = corners[edge], b = corners[(edge + 1) % 3];
                    // The source clipper sets crossings to exactly cutHeight. Do not weld nearby
                    // nonboundary geometry or infer a circular rim from its bounding box.
                    if (p[a].y != cutHeight || p[b].y != cutHeight) continue;
                    Require(!p[a].Equals(p[b]), "Collapsed source cut edge.");
                    Require(!outgoing.ContainsKey(p[a]) && !incoming.ContainsKey(p[b]), "Cut rim is not a consistently directed simple loop.");
                    Vector3 gu, gv; AtlasGradients(p[ia], p[ib], p[ic], uv[ia], uv[ib], uv[ic], out gu, out gv);
                    var segment = new Segment {
                        A = CornerAt(a), B = CornerAt(b), GradientU = gu, GradientV = gv
                    };
                    outgoing.Add(p[a], segment); incoming.Add(p[b], segment);
                }
            }
            Require(outgoing.Count >= 3 && outgoing.Count == incoming.Count, "No complete cut boundary found.");
            foreach (Vector3 point in outgoing.Keys) Require(incoming.ContainsKey(point), "Cut rim contains an unmatched endpoint.");
            Vector3 first = outgoing.Keys.OrderBy(v => v.x).ThenBy(v => v.z).First();
            var ring = new List<Segment>(); var visited = new HashSet<Vector3>(); Vector3 at = first;
            do
            {
                Require(visited.Add(at), "Cut boundary repeats before closing.");
                Segment edge = outgoing[at]; ring.Add(edge); at = edge.B.P;
            } while (!at.Equals(first));
            Require(ring.Count == outgoing.Count, "More than one cut boundary: do not connect separate leaf/stem sections.");
            int count = ring.Count;
            report.rimVertices = report.rimSegments = count; report.rimComponents = 1; report.ringOrderedConnectivity = true;
            foreach (Segment edge in ring)
            {
                edge.A.GradientU = edge.B.GradientU = edge.GradientU;
                edge.A.GradientV = edge.B.GradientV = edge.GradientV;
            }
            for (int i = 0; i < count; i++)
            {
                Segment edge = ring[i], previous = ring[(i + count - 1) % count];
                float firstHand = Vector3.Dot(Vector3.Cross(edge.GradientU, edge.GradientV), edge.A.N);
                float secondHand = Vector3.Dot(Vector3.Cross(previous.GradientU, previous.GradientV), previous.B.N);
                // Keep extrapolated UVs continuous below a source chart's shared endpoint.
                // Never average across distinct UVs, hard normals or mirrored chart orientation.
                if (edge.A.Uv.Equals(previous.B.Uv) && edge.A.N.Equals(previous.B.N) && firstHand * secondHand > 0)
                {
                    Vector3 gu = (edge.GradientU + previous.GradientU) * .5f;
                    Vector3 gv = (edge.GradientV + previous.GradientV) * .5f;
                    edge.A.GradientU = previous.B.GradientU = gu; edge.A.GradientV = previous.B.GradientV = gv;
                    report.sharedAtlasGradientPositions++;
                }
                else report.preservedAtlasGradientSeams++;
            }
            double cx = 0, cz = 0, area = 0, perimeter = 0;
            var arc = new float[count];
            for (int i = 0; i < count; i++)
            {
                Segment edge = ring[i]; arc[i] = (float)perimeter;
                perimeter += Vector3.Distance(edge.A.P, edge.B.P);
                cx += edge.A.P.x; cz += edge.A.P.z;
                area += (double)edge.A.P.x * edge.B.P.z - (double)edge.B.P.x * edge.A.P.z;
                Corner previous = ring[(i + count - 1) % count].B;
                if (!previous.Uv.Equals(edge.A.Uv)) report.rimUvSeamPositions++;
                if (!previous.N.Equals(edge.A.N)) report.rimNormalSeamPositions++;
                if (report.sourceTangentsAvailable && !previous.T.Equals(edge.A.T)) report.rimTangentSeamPositions++;
            }
            Require(Math.Abs(area) > 1e-8 && perimeter > .01, "Cut cross-section has no usable area/perimeter.");
            Vector3 centre = new Vector3((float)(cx / count), cutHeight, (float)(cz / count)); report.rimCentre = centre;
            double meanRadius = 0;
            for (int i = 0; i < count; i++) meanRadius += (ring[i].A.P - centre).magnitude;
            report.sourceMeanRimRadius = (float)(meanRadius / count);
            float radius = report.sourceMeanRimRadius * o.roundRadiusScale;
            report.roundRadiusMetres = radius;
            float length = o.transitionMetres + o.lowerExtensionMetres;
            float orientation = area > 0 ? 1f : -1f;
            float phase = (float)Math.Atan2(first.z - centre.z, first.x - centre.x);
            var radial = new Vector3[count]; var topDerivative = new Vector3[count];
            var targets = new Vector3[count]; var targetDerivative = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = phase + orientation * (float)(Math.PI * 2 * arc[i] / perimeter);
                radial[i] = new Vector3((float)Math.Cos(angle), 0, (float)Math.Sin(angle));
                Vector3 normal = ring[i].A.N.normalized;
                Require(Finite(normal) && Math.Abs(normal.y) < .95f, "Rim normal cannot support a bounded downward tangent continuation.");
                float denominator = 1f - normal.y * normal.y;
                topDerivative[i] = new Vector3(normal.x * normal.y / denominator, -1, normal.z * normal.y / denominator);
                targets[i] = LowerPoint(i, o.transitionMetres);
                const float epsilon = .0001f;
                targetDerivative[i] = (LowerPoint(i, o.transitionMetres + epsilon) - LowerPoint(i, o.transitionMetres - epsilon)) / (2 * epsilon);
            }
            int rows = o.transitionSegments + o.lowerSegments + 1; report.ringCount = rows;
            report.flatLowerTangent=o.flatLowerTangent;
            var depths = new float[rows]; var positions = new Vector3[rows, count]; var normals = new Vector3[rows, count];
            for (int row = 0; row < rows; row++)
            {
                float depth = row <= o.transitionSegments ? o.transitionMetres * row / o.transitionSegments :
                    o.transitionMetres + o.lowerExtensionMetres * (row - o.transitionSegments) / o.lowerSegments;
                depths[row] = depth;
                for (int i = 0; i < count; i++)
                {
                    if (row == 0) positions[row, i] = ring[i].A.P;
                    else if (row >= o.transitionSegments) positions[row, i] = LowerPoint(i, depth);
                    else
                    {
                        float t = depth / o.transitionMetres, t2 = t * t, t3 = t2 * t;
                        positions[row, i] = (2 * t3 - 3 * t2 + 1) * ring[i].A.P +
                            (t3 - 2 * t2 + t) * o.transitionMetres * topDerivative[i] +
                            (-2 * t3 + 3 * t2) * targets[i] + (t3 - t2) * o.transitionMetres * targetDerivative[i];
                    }
                    float radialDistance = (positions[row, i] - Centre(depth)).magnitude;
                    float envelope = Math.Max((ring[i].A.P - centre).magnitude, radius * o.bottomRadiusScale);
                    report.maximumRadiusOverEndpointEnvelope = Math.Max(report.maximumRadiusOverEndpointEnvelope, radialDistance - envelope);
                }
            }
            for (int row = 0; row < rows; row++) for (int i = 0; i < count; i++)
            {
                Vector3 across = positions[row, (i + 1) % count] - positions[row, (i + count - 1) % count];
                Vector3 down = positions[Math.Min(rows - 1, row + 1), i] - positions[Math.Max(0, row - 1), i];
                Vector3 normal = Vector3.Cross(down, across).normalized;
                // Ring follows the crown's directed lower boundary. The support reverses that
                // edge, so this normal must agree with the copied source normal at the rim.
                if (Vector3.Dot(normal, positions[row, i] - Centre(depths[row])) < 0) normal = -normal;
                if(o.flatLowerTangent&&row==rows-1)
                    normal=(positions[row,i]-Centre(depths[row])).normalized;
                Require(normal.sqrMagnitude > .9f, "Collapsed support differential.");
                normals[row, i] = normal;
            }
            var output = new Builder();
            for (int segment = 0; segment < count; segment++)
            {
                int next = (segment + 1) % count; Segment edge = ring[segment];
                var side = new int[rows, 2];
                for (int row = 0; row < rows; row++)
                {
                    side[row, 0] = Vertex(edge.A, segment, row, arc[segment] / (float)perimeter);
                    side[row, 1] = Vertex(edge.B, next, row, next == 0 ? 1f : arc[next] / (float)perimeter);
                }
                for (int row = 0; row < rows - 1; row++)
                {
                    // Source edge A->B is paired with support B->A, never a same-winding cap.
                    output.Indices.Add(side[row, 1]); output.Indices.Add(side[row, 0]); output.Indices.Add(side[row + 1, 0]);
                    output.Indices.Add(side[row, 1]); output.Indices.Add(side[row + 1, 0]); output.Indices.Add(side[row + 1, 1]);
                }
                int Vertex(Corner source, int point, int row, float circumference)
                {
                    float depth = depths[row], blend = Smooth(Math.Min(1f, depth / o.transitionMetres));
                    Vector3 position = positions[row, point];
                    Vector3 normal = row == 0 ? source.N : ((1f - blend) * source.N + blend * normals[row, point]).normalized;
                    Vector3 offset = position - source.P;
                    Vector2 atlas = row == 0 ? source.Uv : source.Uv + new Vector2(Vector3.Dot(source.GradientU, offset), Vector3.Dot(source.GradientV, offset));
                    Vector4 t = row == 0 && report.sourceTangentsAvailable ? source.T : DeriveTangent(normal, source.GradientU, source.GradientV, source.T, report);
                    if (row == 0)
                    {
                        report.copiedTopCorners++;
                        report.boundaryFitMaximumGap = Math.Max(report.boundaryFitMaximumGap, Vector3.Distance(position, source.P));
                        report.topNormalMaximumError = Math.Max(report.topNormalMaximumError, Vector3.Distance(normal, source.N));
                        report.topUvMaximumError = Math.Max(report.topUvMaximumError, Vector2.Distance(atlas, source.Uv));
                        if (report.sourceTangentsAvailable) report.topTangentMaximumError = Math.Max(report.topTangentMaximumError, (t - source.T).magnitude);
                    }
                    return output.Add(new VertexData { P = position, N = normal, Uv = atlas, Branch = new Vector2(circumference, depth), T = t, Weight = blend });
                }
            }
            report.lowerCentre = Centre(length);
            SupportData answer = output.Finish(); answer.Metrics = report;
            Assess(answer, count, outgoing);
            return answer;

            Corner CornerAt(int index)
            {
                Require(Finite(p[index]) && Finite(n[index]) && Finite(uv[index]), "Nonfinite source rim attribute.");
                return new Corner { P = p[index], N = n[index], Uv = uv[index], T = report.sourceTangentsAvailable ? tangent[index] : new Vector4(1, 0, 0, 1) };
            }
            Vector3 Centre(float depth)
            {
                float t = depth / length;
                return centre + new Vector3(o.lowerBendMetres.x * t * t, -depth, o.lowerBendMetres.y * t * t);
            }
            Vector3 LowerPoint(int point, float depth)
            {
                Vector3 axis = new Vector3(2 * o.lowerBendMetres.x * depth / (length * length), -1, 2 * o.lowerBendMetres.y * depth / (length * length)).normalized;
                Vector3 right = (Vector3.right - Vector3.Dot(Vector3.right, axis) * axis).normalized;
                Vector3 forward = Vector3.Cross(axis, right).normalized;
                float taper=(depth-o.transitionMetres)/o.lowerExtensionMetres;
                // Family attachment only: a zero radial slope at the lower rim
                // lets the wood endpoint cap sit inside the tube. The original
                // R13 component path retains its recorded linear taper.
                if(o.flatLowerTangent)taper=Smooth(Mathf.Clamp01(taper));
                float r = radius * (1f + (o.bottomRadiusScale - 1f) * taper);
                return Centre(depth) + (right * radial[point].x + forward * radial[point].z) * r;
            }
        }

        static Vector4 DeriveTangent(Vector3 normal, Vector3 gu, Vector3 gv, Vector4 fallback, Report report)
        {
            Vector3 t = Vector3.Cross(gv, normal);
            if (t.sqrMagnitude < 1e-14f || gu.sqrMagnitude < 1e-14f)
            {
                report.tangentFallbacks++;
                t = new Vector3(fallback.x, fallback.y, fallback.z);
                t -= normal * Vector3.Dot(t, normal);
                if (t.sqrMagnitude < 1e-14f) t = Vector3.Cross(normal, Math.Abs(normal.y) < .9f ? Vector3.up : Vector3.right);
                t.Normalize(); return new Vector4(t.x, t.y, t.z, fallback.w == 0 ? 1 : fallback.w);
            }
            if (Vector3.Dot(gu, t) < 0) t = -t;
            t.Normalize(); Vector3 b = Vector3.Cross(normal, gu);
            if (Vector3.Dot(gv, b) < 0) b = -b;
            float w = Vector3.Dot(Vector3.Cross(normal, t), b) < 0 ? -1f : 1f;
            return new Vector4(t.x, t.y, t.z, w);
        }
        static void AtlasGradients(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, out Vector3 gu, out Vector3 gv)
        {
            Vector3 x = b - a, y = c - a; double xx = Vector3.Dot(x, x), xy = Vector3.Dot(x, y), yy = Vector3.Dot(y, y);
            double determinant = xx * yy - xy * xy;
            Require(determinant > 1e-22, "Degenerate source triangle at cut.");
            gu = Gradient(ub.x - ua.x, uc.x - ua.x); gv = Gradient(ub.y - ua.y, uc.y - ua.y);
            Vector3 Gradient(double du, double dv) => x * (float)((du * yy - dv * xy) / determinant) + y * (float)((dv * xx - du * xy) / determinant);
        }
        static void Assess(SupportData data, int rimCount, Dictionary<Vector3, Segment> sourceEdges)
        {
            Report r = data.Metrics; r.supportVertices = data.Positions.Length; r.supportTriangles = data.Triangles.Length / 3;
            var geometricIds = new Dictionary<Vector3, int>(); var ids = new int[data.Positions.Length];
            for (int i = 0; i < data.Positions.Length; i++)
            {
                Vector3 p = data.Positions[i], n = data.Normals[i]; Vector4 t = data.Tangents[i];
                if (!Finite(p) || !Finite(n) || !Finite(data.AtlasUv[i]) || !Finite(data.BranchUv[i]) || !Finite(t) || !Finite(data.BlendWeights[i].r)) r.nonfiniteAttributes++;
                if (Math.Abs(n.sqrMagnitude - 1f) > .001f) r.nonunitNormals++;
                if (!geometricIds.TryGetValue(p, out int id)) { id = geometricIds.Count; geometricIds.Add(p, id); } ids[i] = id;
            }
            var geometry = new Dictionary<ulong, EdgeUse>(); var indexed = new Dictionary<ulong, EdgeUse>();
            int matchingTopEdges = 0, mismatchedTopEdges = 0;
            for (int i = 0; i < data.Triangles.Length; i += 3)
            {
                int a = data.Triangles[i], b = data.Triangles[i + 1], c = data.Triangles[i + 2];
                Vector3 cross = Vector3.Cross(data.Positions[b] - data.Positions[a], data.Positions[c] - data.Positions[a]);
                double area = .5 * Math.Sqrt((double)cross.x * cross.x + (double)cross.y * cross.y + (double)cross.z * cross.z);
                r.minimumTriangleArea = Math.Min(r.minimumTriangleArea, area);
                if (!(area > 1e-12)) r.degenerateTriangles++;
                // Vector3.normalized deliberately returns zero below a 1e-5 vector length.
                // Small valid triangles need a scale-independent face normal for this test.
                Vector3 faceNormal = area > 0 ? cross / (float)(2 * area) : Vector3.zero;
                float dot = Vector3.Dot(faceNormal, (data.Normals[a] + data.Normals[b] + data.Normals[c]).normalized);
                r.minimumFaceNormalDot = Math.Min(r.minimumFaceNormalDot, dot); if (!(dot > 0)) r.windingAgainstNormals++;
                Edge(geometry, ids[a], ids[b]); Edge(geometry, ids[b], ids[c]); Edge(geometry, ids[c], ids[a]);
                Edge(indexed, a, b); Edge(indexed, b, c); Edge(indexed, c, a);
                CompareTop(a, b); CompareTop(b, c); CompareTop(c, a);
            }
            foreach (EdgeUse edge in geometry.Values)
            {
                if (edge.Count == 1) r.geometricBoundaryEdges++;
                if (edge.Count > 2) r.geometricNonManifoldEdges++;
                if (edge.Count == 2 && edge.Direction != 0) r.geometricWindingConflicts++;
            }
            foreach (EdgeUse edge in indexed.Values) if (edge.Count == 1) r.indexedBoundaryEdges++;
            r.allCutEdgesReversed = matchingTopEdges == rimCount && mismatchedTopEdges == 0;
            r.finiteNormals = r.nonfiniteAttributes == 0 && r.nonunitNormals == 0;
            r.nondegenerate = r.degenerateTriangles == 0;
            r.windingConsistent = r.windingAgainstNormals == 0 && r.geometricWindingConflicts == 0;
            r.numericChecksPassed = r.ringOrderedConnectivity && r.allCutEdgesReversed && r.boundaryFitMaximumGap == 0 &&
                r.topNormalMaximumError == 0 && r.topUvMaximumError == 0 && r.topTangentMaximumError == 0 && r.finiteNormals &&
                r.nondegenerate && r.windingConsistent && r.geometricNonManifoldEdges == 0 && r.geometricBoundaryEdges == 2 * rimCount;
            void CompareTop(int a, int b)
            {
                Vector3 first = data.Positions[a], second = data.Positions[b];
                if (first.y != r.cutHeightMetres || second.y != r.cutHeightMetres) return;
                if (sourceEdges.TryGetValue(second, out Segment source) && source.B.P.Equals(first)) matchingTopEdges++;
                else mismatchedTopEdges++;
            }
        }
        static void Edge(Dictionary<ulong, EdgeUse> table, int a, int b)
        {
            ulong key = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            if (!table.TryGetValue(key, out EdgeUse edge)) { edge = new EdgeUse(); table.Add(key, edge); }
            edge.Count++; edge.Direction += a < b ? 1 : -1;
        }
        static string MeshHash(Mesh mesh)
        {
            using (var bytes = new MemoryStream()) using (var writer = new BinaryWriter(bytes))
            {
                Vector3[] p = mesh.vertices, n = mesh.normals; Vector2[] uv = mesh.uv; Vector4[] t = mesh.tangents;
                writer.Write(p.Length); foreach (Vector3 v in p) V(v); writer.Write(n.Length); foreach (Vector3 v in n) V(v);
                writer.Write(uv.Length); foreach (Vector2 v in uv) { writer.Write(v.x); writer.Write(v.y); }
                writer.Write(t.Length); foreach (Vector4 v in t) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); writer.Write(v.w); }
                writer.Write(mesh.subMeshCount); for (int s = 0; s < mesh.subMeshCount; s++) { int[] index = mesh.GetIndices(s); writer.Write(index.Length); foreach (int i in index) writer.Write(i); }
                writer.Flush(); using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
                void V(Vector3 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
            }
        }
        sealed class EdgeUse { public int Count, Direction; }
        struct Corner { public Vector3 P, N, GradientU, GradientV; public Vector2 Uv; public Vector4 T; }
        sealed class Segment { public Corner A, B; public Vector3 GradientU, GradientV; }
        struct VertexData : IEquatable<VertexData>
        {
            public Vector3 P, N; public Vector2 Uv, Branch; public Vector4 T; public float Weight;
            public bool Equals(VertexData other) => P.Equals(other.P) && N.Equals(other.N) && Uv.Equals(other.Uv) && Branch.Equals(other.Branch) && T.Equals(other.T) && Weight.Equals(other.Weight);
            public override bool Equals(object other) => other is VertexData value && Equals(value);
            public override int GetHashCode() { unchecked { int h = P.GetHashCode(); h = h * 397 ^ N.GetHashCode(); h = h * 397 ^ Uv.GetHashCode(); h = h * 397 ^ Branch.GetHashCode(); h = h * 397 ^ T.GetHashCode(); return h * 397 ^ Weight.GetHashCode(); } }
        }
        sealed class Builder
        {
            readonly List<VertexData> vertices = new List<VertexData>(); readonly Dictionary<VertexData, int> unique = new Dictionary<VertexData, int>();
            public readonly List<int> Indices = new List<int>();
            public int Add(VertexData v) { if (!unique.TryGetValue(v, out int id)) { id = vertices.Count; unique.Add(v, id); vertices.Add(v); } return id; }
            public SupportData Finish() => new SupportData {
                Positions = vertices.Select(v => v.P).ToArray(), Normals = vertices.Select(v => v.N).ToArray(), AtlasUv = vertices.Select(v => v.Uv).ToArray(),
                BranchUv = vertices.Select(v => v.Branch).ToArray(), Tangents = vertices.Select(v => v.T).ToArray(), BlendWeights = vertices.Select(v => new Color(v.Weight, 0, 0, 1)).ToArray(), Triangles = Indices.ToArray()
            };
        }
        static float Smooth(float t) => t * t * (3f - 2f * t);
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static bool Finite(Vector2 v) => Finite(v.x) && Finite(v.y);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static bool Finite(Vector4 v) => Finite(v.x) && Finite(v.y) && Finite(v.z) && Finite(v.w);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException("PH02 fitted support: " + message); }
    }
}
#endif
