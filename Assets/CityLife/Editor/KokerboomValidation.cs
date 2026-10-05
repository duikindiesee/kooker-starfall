using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CityLife.World.Editor
{
    /// <summary>Numeric mesh checks. These do not award botanical or visual acceptance.</summary>
    public static class KokerboomValidation
    {
        [Serializable] public sealed class Specimen
        {
            public int seed, lod, vertices, triangles;
            public float age01;
            public long creationMilliseconds;
            public string meshSha256, descriptorJson;
            public Vector3 actualMinimum, actualMaximum;
        }
        [Serializable] public sealed class SourceFile { public string path, sha256, sha256Before; public bool comparedBeforeAfter, unchanged; }
        [Serializable] public sealed class Report
        {
            public string schema = "citylife.kokerboom-validation.v1";
            public string utc, status, geometryVersion, unityVersion, platform, failure;
            public string componentMode, leafTopologyPolicy, cleanupFailure;
            public bool ph02ComponentsConfigured, ph02CleanupComplete, ph02ComponentReadbackPassed;
            public string ph02ActualComponentSha256;
            public PH02FamilyComponent.Report ph02Component;
            public WoodTopology ph02OpenComponentTopology;
            public List<PH02Placement> ph02Placements = new List<PH02Placement>();
            public bool hybridComponentsConfigured, sourceInputsUnchanged, hybridCleanupComplete;
            public KokerboomSourceRosettes.ExtractionReport sourceExtraction;
            public KokerboomSourceIdentity.Report sourceIdentity;
            public int assertions;
            public bool independentRegeneration;
            public string descriptorMeasurement = "Skeleton estimates; independently measured actual mesh bounds are recorded separately. Leaf-length metadata is not independently measured by this check.";
            public string visualAcceptance = "Unverified: independent rendered-image critique is required.";
            public List<Specimen> specimens = new List<Specimen>();
            public List<WoodTopology> woodTopology = new List<WoodTopology>();
            public List<SourceFile> sources = new List<SourceFile>();
            public string[] checks;
        }
        static int assertions;
        static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new InvalidOperationException("KOKERBOOM VALIDATION FAILED: " + message); }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        [MenuItem("CityLife/Validate kokerboom geometry")]
        public static void Run() => RunInternal(false);

        [MenuItem("CityLife/Validate hybrid kokerboom geometry")]
        public static void RunHybrid() => RunInternal(true);

        [MenuItem("CityLife/Validate PH02 fitted-crown family")]
        public static void RunPH02Family() => RunInternal(false, true);

        static void RunInternal(bool hybrid, bool ph02 = false)
        {
            assertions = 0;
            string project = Path.GetDirectoryName(Application.dataPath);
            var report = new Report {
                utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), status = "RUNNING",
                geometryVersion = KokerboomGeometry.Version, unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                componentMode = ph02 ? "hybrid-ph02-fitted-crown-v2-preview" : hybrid ? "hybrid-ph01-source-rosettes" : "procedural-default",
                leafTopologyPolicy = ph02
                    ? "Actual imported PH02 open crown plus R13 support, separately indexed at UV/normal/tangent seams and the fitted join. Compound open topology is reported, not asserted closed. Single closed indexed manifold checks apply only to the separately generated procedural wood. Sampled placement/radius checks do not prove a welded whole tree or visual acceptance."
                    : hybrid
                    ? "105 original open-base leaves in five groups; 12 source boundary edges per leaf. Source polygon corners retain UV/normal seams; leaves are not subjected to the closed-wood topology test. Eleven closed basal pieces are excluded. Original position/normal/UV identity is checked; tangent differences remain diagnostic."
                    : "Procedural leaf surfaces; the closed indexed manifold assertions apply to wood only.",
                checks = new[] {
                    "Invalid age and LOD rejection", "Finite mesh attributes and bounds", "Triangle index and area validity",
                    "Unit vertex normals", "Source and actual mesh hashes", "Seed diversity", "Development and metre scale",
                    "LOD triangle reduction and envelope preservation", "Repeat creation unaffected by intervening seed",
                    "Independent regeneration after explicit cache reset", "World definition asset unchanged",
                    "Wood-only indexed connected components, closed manifold edges and vertex fans", "Known topology canaries"
                }
            };
            var created = new List<GameObject>();
            HybridFixture hybridFixture = null;
            PH02Fixture ph02Fixture = null;
            var sourceBefore = new Dictionary<string, string>();
            string definitionPath = Path.Combine(project, "Assets/CityLife/Resources/IslandDefinition.json");
            string definitionBefore = FileHash(definitionPath);
            try
            {
                Check(!Application.isPlaying, "validation runs outside Play mode");
                if (hybrid)
                {
                    report.checks = new List<string>(report.checks) {
                        "Configured PH01 hybrid component path", "Pinned FBX and extractor/identity source hashes",
                        "Source leaf positions/normals/UVs match actual Unity import", "Source inputs unchanged across validation",
                        "Owned hybrid meshes/materials and static configuration cleanup"
                    }.ToArray();
                    foreach (string relative in HybridSourcePaths) sourceBefore.Add(relative, FileHash(Path.Combine(project, relative)));
                    hybridFixture = new HybridFixture();
                    hybridFixture.Configure(report, project);
                }
                if (ph02)
                {
                    report.checks = new List<string>(report.checks) {
                        "Actual PH02 imported adapter and fitted-component numeric gates", "Independent complete PNUT/UV1/colour Mesh readback",
                        "Full-age R16 specimen seed4242 age1 LOD0", "Generated component UV/colour/UV1/handedness preservation",
                        "Sampled proper similarity placement and actual lower-frame/radius", "Pinned PH02 source inputs and cleanup"
                    }.ToArray();
                    foreach (string relative in PH02SourcePaths) sourceBefore.Add(relative, FileHash(Path.Combine(project, relative)));
                    ph02Fixture = new PH02Fixture(); ph02Fixture.Configure(report, project);
                }
                RunTopologyCanaries();
                foreach (float invalid in new[] { -0.01f, 1.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    MustReject(() => KokerboomGeometry.Create(4242, invalid), "Create rejects invalid age");
                    MustReject(() => KokerboomGeometry.Describe(4242, invalid), "Describe rejects invalid age");
                }
                MustReject(() => KokerboomGeometry.Create(4242, .6f, -1), "negative LOD rejected");
                MustReject(() => KokerboomGeometry.Create(4242, .6f, 3), "unsupported LOD rejected");

                var lods = new Specimen[3];
                for (int lod = 0; lod < 3; lod++) lods[lod] = Inspect(4242, .6f, lod, report, created);
                for (int lod = 1; lod < 3; lod++)
                {
                    Check(lods[lod].triangles < lods[lod - 1].triangles, "successive LOD triangle counts decrease");
                    Vector3 size0 = lods[0].actualMaximum - lods[0].actualMinimum;
                    Vector3 sizeN = lods[lod].actualMaximum - lods[lod].actualMinimum;
                    for (int axis = 0; axis < 3; axis++)
                        Check(Mathf.Abs(size0[axis] - sizeN[axis]) <= Mathf.Max(.03f, size0[axis] * .05f),
                            "LOD preserves measured envelope within 5% or 3 cm numerical allowance");
                }

                float previousHeight = 0;
                var ages = new List<Specimen>();
                foreach (float age in new[] { .05f, .3f, .6f, .9f })
                {
                    Specimen s = age == .6f ? lods[1] : Inspect(4242, age, 2, report, created);
                    ages.Add(s);
                    Check(s.actualMaximum.y > previousHeight, "fixed-seed development increases height above ground");
                    previousHeight = s.actualMaximum.y;
                }
                var young = KokerboomGeometry.Describe(4242, .05f);
                var old = KokerboomGeometry.Describe(4242, .9f);
                Check(young.forkGenerations < old.forkGenerations && young.terminalRosettes < old.terminalRosettes,
                    "development changes branching and rosette structure as well as scale");
                Check(ages[0].triangles != ages[3].triangles, "juvenile and mature meshes differ structurally");

                Specimen alternative = Inspect(314, .6f, 2, report, created);
                Check(alternative.meshSha256 != lods[2].meshSha256, "two representative seeds produce distinct geometry at the same age and LOD");
                // Descriptor-only probes keep initial validation bounded while covering integer seeds.
                foreach (int seed in new[] { 0, -1, int.MinValue, int.MaxValue })
                {
                    var description = KokerboomGeometry.Describe(seed, .6f);
                    Check(description.seed == seed && description.generator == KokerboomGeometry.Version &&
                        Finite(description.heightMetres) && description.heightMetres > 0 && description.terminalRosettes > 0,
                        "extreme/zero seed descriptor is valid; full mesh not generated for this probe");
                }

                Specimen repeated = Inspect(4242, .6f, 1, report, created);
                Check(repeated.meshSha256 == lods[1].meshSha256, "repeat mesh is unchanged after other seeds/ages/LODs");
                Check(repeated.descriptorJson == lods[1].descriptorJson, "repeat description is deterministic");

                DestroyInstances(created);
                KokerboomGeometry.ClearCacheForValidation();
                Specimen rebuilt = Inspect(4242, .6f, 1, report, created);
                Check(rebuilt.meshSha256 == lods[1].meshSha256, "independently regenerated geometry matches exactly");
                Check(rebuilt.descriptorJson == lods[1].descriptorJson, "independently regenerated description matches exactly");
                report.independentRegeneration = true;
                if (ph02)
                {
                    // Keep the original nine representative checks, then release those caches
                    // before the larger R16 seed4242/age1/LOD0 specimen is generated cold.
                    DestroyInstances(created); KokerboomGeometry.ClearCacheForValidation();
                    Specimen r16 = Inspect(4242, 1f, 0, report, created);
                    Check(r16.actualMaximum.y > ages[3].actualMaximum.y, "PH02 full-age R16 specimen is taller than the .9 age sample");
                    Check(report.woodTopology.Count == 10, "PH02 run retained all nine original wood samples plus the full-age R16 sample");
                }
                Check(FileHash(definitionPath) == definitionBefore, "world definition is unchanged by flora generation");
                if (hybrid || ph02)
                {
                    foreach (var pair in sourceBefore)
                        Check(FileHash(Path.Combine(project, pair.Key)) == pair.Value, "hybrid source input is unchanged: " + pair.Key);
                    report.sourceInputsUnchanged = true;
                }
                report.status = "PASS";
            }
            catch (Exception error)
            {
                report.status = "FAIL"; report.failure = error.Message; throw;
            }
            finally
            {
                DestroyInstances(created);
                Exception cleanupError = null;
                try
                {
                    if (hybridFixture != null) { hybridFixture.Dispose(); report.hybridCleanupComplete = true; }
                    if (ph02Fixture != null) { ph02Fixture.Dispose(); report.ph02CleanupComplete = true; }
                }
                catch (Exception error)
                {
                    report.status = "FAIL"; report.cleanupFailure = error.Message; cleanupError = error;
                }
                report.assertions = assertions;
                foreach (string relative in ph02 ? PH02SourcePaths : hybrid ? HybridSourcePaths : new[] { "Assets/CityLife/Scripts/KokerboomGeometry.cs", "Assets/CityLife/Shaders/KokerboomSurface.shader", "Assets/CityLife/Editor/KokerboomValidation.cs" })
                {
                    string path = Path.Combine(project, relative);
                    string hash = File.Exists(path) ? FileHash(path) : "MISSING";
                    bool compared = sourceBefore.TryGetValue(relative, out string before);
                    report.sources.Add(new SourceFile { path = relative, sha256 = hash, sha256Before = before, comparedBeforeAfter = compared, unchanged = compared && hash == before });
                }
                string directory = Path.Combine(project, "evidence/local"); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, ph02 ? "kokerboom-ph02-family-validation.json" : hybrid ? "kokerboom-hybrid-validation.json" : "kokerboom-validation.json"), JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
                Debug.Log("Kokerboom numerical validation " + report.status + ": " + assertions + " assertions; " + report.specimens.Count + " measured meshes; componentMode=" + report.componentMode + ". Visual acceptance is unverified.");
                if (cleanupError != null) throw new InvalidOperationException("Hybrid validation cleanup failed; the report retains both validation and cleanup failures.", cleanupError);
            }
        }


        // Owns only assets allocated for this validation; an existing cache is never cleared
        // if ConfigureSourceRosettesForInspection rejected the fresh-run requirement.
        sealed class HybridFixture : IDisposable
        {
            Mesh[] rosettes;
            Material leaves, wood;
            bool configured;
            public void Configure(Report report, string project)
            {
                Check(Application.isBatchMode, "hybrid validation requires an isolated fresh batch editor");
                Check(FileHash(Path.Combine(project, KokerboomSourceRosettes.SourceAssetPath)) == KokerboomSourceRosettes.SourceSha256,
                    "hybrid source FBX matches the inspected pinned hash");
                report.sourceExtraction = KokerboomSourceRosettes.Describe();
                Check(report.sourceExtraction.sourceLeafComponents == 105 && report.sourceExtraction.sourceClosedComponents == 11 &&
                    report.sourceExtraction.groups.Length == 5, "five original source groups distinguish 105 open leaves from 11 excluded closed basal pieces");
                rosettes = KokerboomSourceRosettes.CreateRosettes(false);
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                Check(shader != null, "URP Lit shader is available for numeric-only hybrid materials");
                leaves = new Material(shader) { name = "Hybrid validation dummy leaves - no appearance claim", hideFlags = HideFlags.HideAndDontSave };
                wood = new Material(shader) { name = "Hybrid validation dummy wood - no appearance claim", hideFlags = HideFlags.HideAndDontSave };
                // This public API rejects nonempty caches before changing any configuration.
                KokerboomGeometry.ConfigureSourceRosettesForInspection(rosettes, leaves, wood);
                configured = true;
                report.hybridComponentsConfigured = true;
                for (int i = 0; i < rosettes.Length; i++)
                {
                    var group = report.sourceExtraction.groups[i];
                    Check(group.leafComponents == 21 && rosettes[i] != null && rosettes[i].isReadable,
                        "each configured original source rosette has 21 complete readable leaves");
                    Check(rosettes[i].vertexCount == group.emittedLeafCornerVertices &&
                        rosettes[i].triangles.Length / 3 == group.leafTriangles,
                        "configured source rosette buffers match extractor measurements");
                }
                report.sourceIdentity = KokerboomSourceIdentity.Collect();
                Check(report.sourceIdentity.allCornersMatchImportedPositionNormalUv,
                    "all configured source leaf corner positions, normals and UVs match the actual Unity import within recorded tolerances");
                // Tangent differences are retained as diagnostics, not silently treated as
                // identical. Dummy materials make this a geometry test, not material acceptance.
            }
            public void Dispose()
            {
                try
                {
                    if (configured)
                    {
                        KokerboomGeometry.ClearCacheForValidation();
                        KokerboomGeometry.ResetSourceRosettesAfterInspection();
                        configured = false;
                    }
                }
                finally
                {
                    if (rosettes != null) foreach (Mesh mesh in rosettes) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                    if (leaves != null) UnityEngine.Object.DestroyImmediate(leaves);
                    if (wood != null) UnityEngine.Object.DestroyImmediate(wood);
                }
            }
        }

        static readonly string[] HybridSourcePaths =
        {
            "Assets/CityLife/Scripts/KokerboomGeometry.cs",
            "Assets/CityLife/Shaders/KokerboomSurface.shader",
            "Assets/CityLife/Editor/KokerboomValidation.cs",
            "Assets/CityLife/Editor/KokerboomSourceRosettes.cs",
            "Assets/CityLife/Editor/KokerboomSourceIdentity.cs",
            KokerboomSourceRosettes.SourceAssetPath,
            KokerboomSourceRosettes.SourceAssetPath + ".meta"
        };

        static Specimen Inspect(int seed, float age, int lod, Report report, List<GameObject> created)
        {
            var creationTimer = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log("Kokerboom numeric Create begin: seed=" + seed + " age=" + age.ToString("R", CultureInfo.InvariantCulture) + " lod=" + lod + " mode=" + report.componentMode);
            GameObject root = KokerboomGeometry.Create(seed, age, lod); creationTimer.Stop(); created.Add(root);
            Debug.Log("Kokerboom numeric Create complete: elapsedMs=" + creationTimer.ElapsedMilliseconds);
            Check(root != null, "Create returns an object");
            Check(root.transform.localPosition == Vector3.zero && root.transform.localScale == Vector3.one && root.transform.localRotation == Quaternion.identity,
                "specimen uses metre-scale identity root transform");
            KokerboomDescriptor d = KokerboomGeometry.Describe(seed, age);
            Check(d != null && d.generator == KokerboomGeometry.Version && d.seed == seed && d.age01 == age, "descriptor identity matches request");
            Check(d.terminalRosettes > 0 && d.branchSegments > 0 && d.forkGenerations >= 0, "descriptor contains positive supported geometry counts");
            Check(Finite(d.heightMetres) && d.heightMetres > 0 && Finite(d.crownDiameterMetres) && d.crownDiameterMetres > 0,
                "descriptor dimensions are finite and positive");
            Check(Finite(d.leafLengthMin) && Finite(d.leafLengthMax) && d.leafLengthMin > 0 && d.leafLengthMax >= d.leafLengthMin,
                "reported leaf range is finite and ordered; this alone does not prove measurement");
            Check(Finite(d.branchUnion) && Finite(d.trunkBark) && Finite(d.terminalRosette), "inspection anchors are finite");
            var filters = root.GetComponentsInChildren<MeshFilter>();
            Array.Sort(filters, (a, b) => StringComparer.Ordinal.Compare(a.name, b.name));
            Check(filters.Length == 2, "separate wood and succulent meshes are present");
            var sample = new Specimen { seed = seed, age01 = age, lod = lod, descriptorJson = JsonUtility.ToJson(d), creationMilliseconds = creationTimer.ElapsedMilliseconds,
                actualMinimum = Vector3.one * float.PositiveInfinity, actualMaximum = Vector3.one * float.NegativeInfinity };
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes))
            {
                writer.Write(KokerboomGeometry.Version); writer.Write(filters.Length);
                int topologyCountBefore = report.woodTopology.Count;
                foreach (var filter in filters)
                {
                    var mesh = filter.sharedMesh;
                    Check(mesh != null && mesh.isReadable && mesh.vertexCount > 0, "readable nonempty generated mesh");
                    if (filter.name == "Bark")
                    {
                        // Analyse the welded index topology only. Do not merge coincident positions:
                        // separate indices at the same place are still an unwelded seam.
                        WoodTopology topology = AnalyseWoodTopology(mesh.vertexCount, mesh.triangles);
                        topology.mesh = mesh.name; topology.seed = seed; topology.age01 = age; topology.lod = lod;
                        report.woodTopology.Add(topology);
                        Debug.Log("Wood topology before assertion: " + JsonUtility.ToJson(topology));
                        Check(topology.IsSingleClosedManifold,
                            "wood is one closed indexed 2-manifold; " + JsonUtility.ToJson(topology));
                    }
                    if (report.hybridComponentsConfigured && filter.name == "Succulent rosettes")
                        Check(mesh.name.StartsWith("Hybrid PH01 source rosettes - experimental ", StringComparison.Ordinal),
                            "generated succulent buffer uses the configured hybrid source path");
                    if (!report.hybridComponentsConfigured && filter.name == "Succulent rosettes")
                        Check(!mesh.name.StartsWith("Hybrid PH01 source rosettes - experimental ", StringComparison.Ordinal),
                            "procedural validation does not silently measure a previously configured hybrid family");
                    if (report.ph02ComponentsConfigured && filter.name == "Succulent rosettes")
                        Check(mesh.name.StartsWith("Hybrid PH02 fitted crowns - experimental ", StringComparison.Ordinal) && d.componentBasis == "PH02 fitted crown comparison",
                            "PH02 numerical run measures the actual configured fitted-crown family");
                    if (!report.ph02ComponentsConfigured && !report.hybridComponentsConfigured && filter.name == "Succulent rosettes")
                        Check(!mesh.name.StartsWith("Hybrid PH02 fitted crowns", StringComparison.Ordinal), "default mode cannot silently reuse a PH02 family configuration");
                    var renderer = filter.GetComponent<MeshRenderer>();
                    Check(renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null,
                        "mesh has a material and shader");
                    Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                    Color[] colors = mesh.colors; Vector2[] uv = mesh.uv;
                    Check(normals.Length == vertices.Length && colors.Length == vertices.Length && uv.Length == vertices.Length, "complete normal, colour and UV channels");
                    Check(Finite(mesh.bounds.center) && Finite(mesh.bounds.size), "finite mesh bounds");
                    bool finite = true, unitNormals = true, contained = true;
                    Bounds measured = new Bounds(vertices[0], Vector3.zero);
                    writer.Write(filter.name); writer.Write(vertices.Length);
                    Matrix4x4 toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 p = toRoot.MultiplyPoint3x4(vertices[i]);
                        finite &= Finite(p) && Finite(normals[i]) && Finite(uv[i].x) && Finite(uv[i].y) &&
                            Finite(colors[i].r) && Finite(colors[i].g) && Finite(colors[i].b) && Finite(colors[i].a);
                        unitNormals &= Mathf.Abs(normals[i].sqrMagnitude - 1f) <= .01f;
                        measured.Encapsulate(vertices[i]);
                        sample.actualMinimum = Vector3.Min(sample.actualMinimum, p); sample.actualMaximum = Vector3.Max(sample.actualMaximum, p);
                        Write(writer, p); Write(writer, normals[i]); writer.Write(uv[i].x); writer.Write(uv[i].y);
                        writer.Write(colors[i].r); writer.Write(colors[i].g); writer.Write(colors[i].b); writer.Write(colors[i].a);
                    }
                    if (report.ph02ComponentsConfigured && filter.name == "Succulent rosettes")
                    {
                        Vector4[] tangent = mesh.tangents; Vector2[] uv1 = mesh.uv2;
                        VerifyPH02Placement(root, d, mesh, vertices, normals, uv, colors, tangent, uv1, seed, age, lod, report);
                        // Include actual carried tangent4/UV1 in PH02 determinism hashes; legacy
                        // procedural/PH01 hash byte layout remains unchanged.
                        writer.Write(tangent.Length);
                        foreach (Vector4 t in tangent) { writer.Write(t.x); writer.Write(t.y); writer.Write(t.z); writer.Write(t.w); }
                        writer.Write(uv1.Length); foreach (Vector2 u in uv1) { writer.Write(u.x); writer.Write(u.y); }
                    }
                    Check(finite, "all vertex attributes are finite"); Check(unitNormals, "all vertex normals have unit length within 1%");
                    for (int axis = 0; axis < 3; axis++) contained &= Mathf.Abs(measured.min[axis] - mesh.bounds.min[axis]) <= .00001f && Mathf.Abs(measured.max[axis] - mesh.bounds.max[axis]) <= .00001f;
                    Check(contained, "Unity bounds match independently measured vertices within 10 micrometres");
                    sample.vertices += vertices.Length; writer.Write(mesh.subMeshCount);
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        Check(mesh.GetTopology(sub) == MeshTopology.Triangles, "generated surfaces use triangle topology");
                        int[] indices = mesh.GetTriangles(sub);
                        Check(indices.Length > 0 && indices.Length % 3 == 0, "nonempty complete triangle list");
                        writer.Write(indices.Length); bool valid = true;
                        foreach (int index in indices) { valid &= index >= 0 && index < vertices.Length; writer.Write(index); }
                        Check(valid, "triangle indices remain inside vertex buffer");
                        float minCrossSquared = float.PositiveInfinity;
                        int firstSmallTriangle = -1, smallTriangleCount = 0;
                        for (int i = 0; i < indices.Length; i += 3)
                        {
                            float crossSquared = Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]).sqrMagnitude;
                            minCrossSquared = Mathf.Min(minCrossSquared, crossSquared);
                            if (!(crossSquared > 1e-18f))
                            { if (firstSmallTriangle < 0) firstSmallTriangle = i; smallTriangleCount++; }
                        }
                        string areaMessage = "triangle area exceeds 5e-10 square metres; mesh=" + mesh.name +
                            "; seed=" + seed + "; age=" + age.ToString("R", CultureInfo.InvariantCulture) + "; LOD=" + lod +
                            "; submesh=" + sub + "; minCrossSquared=" + minCrossSquared.ToString("R", CultureInfo.InvariantCulture) +
                            "; smallTriangleCount=" + smallTriangleCount;
                        if (firstSmallTriangle >= 0)
                        {
                            int ia = indices[firstSmallTriangle], ib = indices[firstSmallTriangle + 1], ic = indices[firstSmallTriangle + 2];
                            areaMessage += "; firstTriangle=" + (firstSmallTriangle / 3) + "; indices=" + ia + "," + ib + "," + ic +
                                "; a=" + PointText(vertices[ia]) + "; b=" + PointText(vertices[ib]) + "; c=" + PointText(vertices[ic]);
                        }
                        Check(firstSmallTriangle < 0, areaMessage); sample.triangles += indices.Length / 3;
                    }
                }
                Check(report.woodTopology.Count == topologyCountBefore + 1, "exactly one wood topology check was executed for this specimen");
                writer.Flush(); using (var sha = SHA256.Create()) sample.meshSha256 = Hex(sha.ComputeHash(bytes.ToArray()));
            }
            Check(Finite(sample.actualMinimum) && Finite(sample.actualMaximum) && sample.actualMaximum.y > .1f && sample.actualMaximum.y <= 9f,
                "whole specimen is finite and within the chosen 9 m above-ground scale ceiling");
            Check(sample.actualMinimum.y >= -1f && sample.actualMinimum.y <= .02f, "root reaches ground without an excessive buried base; actualMinimum=" + PointText(sample.actualMinimum));
            report.specimens.Add(sample); return sample;
        }


        static PH02Snapshot ph02Snapshot;
        sealed class PH02Snapshot
        {
            public Vector3[] P, N; public Vector2[] U, U1; public Vector4[] T; public Color[] C; public int[] Indices;
            public int anchorA, anchorB, anchorC;
            public int[] bottomVertices;
            public float bottomRadius;
        }
        [Serializable] public sealed class PH02Placement
        {
            public int seed, lod, copies, checkedAttributeVertices, sampledPositionVertices, sampledTrianglePatterns;
            public float age01, minimumScale = float.PositiveInfinity, maximumScale;
            public bool attributesPreserved, trianglePatternSamplesPreserved, properSimilaritySamples;
            public float maximumPositionResidual, minimumNormalDot = 1, minimumTangentDot = 1;
            public Vector3 meshDerivedFocusOrigin, meshDerivedFocusAxis;
            public float markerOriginError, markerAxisDot, measuredFocusRadius, expectedFocusRadius, maximumFocusRadiusError, maximumFocusPlaneError;
            public string scope = "Every copied UV0/UV1/colour/tangent-W checked. At least 32 position/normal/tangent samples and 64 triangle-pattern samples per component, plus mesh-derived lower frame/radius against marker. No wood surface contact, welded join or visual acceptance is inferred.";
        }

        sealed class PH02Fixture : IDisposable
        {
            PH02FamilyComponent.Result component;
            Material surface, wood;
            bool configured;
            public void Configure(Report report, string project)
            {
                Check(Application.isBatchMode, "PH02 family validation requires an isolated fresh batch editor");
                Check(ph02Snapshot == null, "PH02 validation has no previous component snapshot");
                Check(KokerboomGeometry.Version == "citylife.aloidendron-dichotomum.v2-preview", "PH02 mode explicitly targets the current v2-preview family");
                Check(FileHash(Path.Combine(project, PH02CrownCandidate.SourceAssetPath)) == PH02CrownCandidate.SourceSha256, "PH02 source FBX matches its pinned source hash");
                PH02FamilyComponent.Report componentReport = null;
                try { component = PH02FamilyComponent.Create(true, out componentReport); }
                finally { report.ph02Component = componentReport; }
                Check(component != null && component.Component != null && component.Component.isReadable,
                    "actual PH02 compound Mesh is readable and caller-owned");
                Check(componentReport.actualImportedGatePassed && componentReport.actualMeshCreated && componentReport.numericChecksPassed &&
                    componentReport.inputsUnchanged, "actual imported component and immutable-input checks passed");
                Check(componentReport.importedChecks != null && componentReport.importedChecks.actualUnityImportedData &&
                    componentReport.importedChecks.mappingComplete && componentReport.importedChecks.exactExpandedTuplePreservation &&
                    componentReport.importedChecks.matchedSourceTriangles == 82074 && componentReport.importedChecks.outputTriangles == 71207,
                    "PH02 adapter establishes actual source correspondence and complete-tuple preservation");
                var snapshot = ReadPH02Snapshot(component.Component, component.BottomRadiusMetres);
                report.ph02ActualComponentSha256 = HashPH02Snapshot(snapshot);
                report.ph02ComponentReadbackPassed = report.ph02ActualComponentSha256 == componentReport.componentSha256;
                Check(report.ph02ComponentReadbackPassed, "independent complete Mesh PNUT/index/UV1/colour readback matches the compound report hash");
                Check(snapshot.P.Length == componentReport.componentVertices && snapshot.Indices.Length == componentReport.componentTriangles * 3 &&
                    componentReport.crownTriangles == 71207 && componentReport.supportTriangles == 3312,
                    "actual compound contains all original crown/support triangle spans");
                bool weights = true;
                for (int i = 0; i < snapshot.P.Length; i++)
                {
                    if (i < componentReport.crownVertices) weights &= snapshot.C[i].r == 0 && snapshot.C[i].g >= 0 && snapshot.C[i].g <= 1;
                    else weights &= snapshot.C[i].g == 0 && snapshot.C[i].r >= 0 && snapshot.C[i].r <= 1;
                }
                Check(weights, "compound distinguishes crown and support blend channels without interpreting a publisher mask");
                report.ph02OpenComponentTopology = AnalyseWoodTopology(snapshot.P.Length, snapshot.Indices);
                report.ph02OpenComponentTopology.mesh = component.Component.name;
                // Do NOT assert closedness on imported foliage or separately indexed join/UV
                // seams. Index validity and actual triangle area are checked independently.
                Check(report.ph02OpenComponentTopology.invalidIndexTriangles == 0 &&
                    report.ph02OpenComponentTopology.degenerateIndexTriangles == 0 &&
                    report.ph02OpenComponentTopology.trailingIndices == 0,
                    "PH02 open source component has valid complete indexed triangles; its boundary topology remains diagnostic");

                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                Check(shader != null, "URP Lit is available for PH02 numeric-only materials");
                surface = new Material(shader) { name = "PH02 numeric dummy component - no appearance claim", hideFlags = HideFlags.HideAndDontSave };
                wood = new Material(shader) { name = "PH02 numeric dummy wood - no appearance claim", hideFlags = HideFlags.HideAndDontSave };
                RejectPH02Configuration(() => KokerboomGeometry.ConfigureFittedCrownForInspection(null, surface, wood, component.BottomRadiusMetres), "missing PH02 component rejected");
                RejectPH02Configuration(() => KokerboomGeometry.ConfigureFittedCrownForInspection(component.Component, null, wood, component.BottomRadiusMetres), "missing PH02 material rejected");
                RejectPH02Configuration(() => KokerboomGeometry.ConfigureFittedCrownForInspection(component.Component, surface, wood, float.NaN), "nonfinite PH02 bottom radius rejected");
                RejectPH02Configuration(() => KokerboomGeometry.ConfigureFittedCrownForInspection(component.Component, surface, wood, .03f), "out-of-range PH02 bottom radius rejected");
                KokerboomGeometry.ConfigureFittedCrownForInspection(component.Component, surface, wood, component.BottomRadiusMetres);
                configured = true; ph02Snapshot = snapshot; report.ph02ComponentsConfigured = true;
            }
            public void Dispose()
            {
                try
                {
                    if (configured)
                    {
                        KokerboomGeometry.ClearCacheForValidation();
                        KokerboomGeometry.ResetSourceRosettesAfterInspection();
                        ph02Snapshot = null; configured = false;
                    }
                }
                finally
                {
                    if (component != null) component.Dispose();
                    if (surface != null) UnityEngine.Object.DestroyImmediate(surface);
                    if (wood != null) UnityEngine.Object.DestroyImmediate(wood);
                }
            }
        }
        static void RejectPH02Configuration(Action action, string reason)
        {
            bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, reason);
        }
        static readonly string[] PH02SourcePaths =
        {
            "Assets/CityLife/Scripts/KokerboomGeometry.cs",
            "Assets/CityLife/Shaders/KokerboomSurface.shader",
            "Assets/CityLife/Shaders/KokerboomBranchSkin.hlsl",
            "Assets/CityLife/Shaders/PH02FittedSupport.shader",
            "Assets/CityLife/Editor/KokerboomValidation.cs",
            "Assets/CityLife/Editor/PH02FamilyComponent.cs",
            "Assets/CityLife/Editor/PH02ImportedCrownCandidate.cs",
            "Assets/CityLife/Editor/PH02FittedSupportCandidate.cs",
            "Assets/CityLife/Editor/PH02CrownCandidate.cs",
            PH02CrownCandidate.SourceAssetPath,
            PH02CrownCandidate.SourceAssetPath + ".meta"
        };
        static PH02Snapshot ReadPH02Snapshot(Mesh mesh, float radius)
        {
            var s = new PH02Snapshot { P = mesh.vertices, N = mesh.normals, U = mesh.uv, U1 = mesh.uv2,
                T = mesh.tangents, C = mesh.colors, Indices = mesh.triangles, bottomRadius = radius };
            Check(mesh.subMeshCount == 1 && s.P.Length > 0 && s.N.Length == s.P.Length && s.U.Length == s.P.Length &&
                s.U1.Length == s.P.Length && s.T.Length == s.P.Length && s.C.Length == s.P.Length, "compound readback has complete P/N/UV/tangent4/colour/UV1 arrays and one submesh");
            bool finite = true, bases = true, indices = s.Indices.Length > 0 && s.Indices.Length % 3 == 0;
            for (int i = 0; i < s.P.Length; i++)
            {
                finite &= Finite(s.P[i]) && Finite(s.N[i]) && Finite(s.U[i].x) && Finite(s.U[i].y) && Finite(s.U1[i].x) && Finite(s.U1[i].y) &&
                    Finite(s.T[i].x) && Finite(s.T[i].y) && Finite(s.T[i].z) && Finite(s.T[i].w) &&
                    Finite(s.C[i].r) && Finite(s.C[i].g) && Finite(s.C[i].b) && Finite(s.C[i].a);
                bases &= Mathf.Abs(s.N[i].magnitude - 1) < .001f && Mathf.Abs(Tangent3(s.T[i]).magnitude - 1) < .001f && Mathf.Abs(s.T[i].w) == 1;
            }
            foreach (int i in s.Indices) indices &= i >= 0 && i < s.P.Length;
            Check(finite && bases && indices, "compound readback attributes are finite with unit N/T and valid indices");
            s.anchorA = 0;
            for (int i = 1; i < s.P.Length; i++) if ((s.P[i] - s.P[0]).sqrMagnitude > (s.P[s.anchorB] - s.P[0]).sqrMagnitude) s.anchorB = i;
            Vector3 direction = s.P[s.anchorB] - s.P[0]; float area = -1;
            for (int i = 0; i < s.P.Length; i++)
            {
                float value = Vector3.Cross(direction, s.P[i] - s.P[0]).sqrMagnitude;
                if (value > area) { area = value; s.anchorC = i; }
            }
            Check(direction.magnitude > .1f && area > .001f, "compound provides non-collinear metre-scale anchors for independent placement fitting");
            float bottomDepth = s.U1.Max(u => u.y);
            s.bottomVertices = Enumerable.Range(0, s.P.Length).Where(i => s.U1[i].y == bottomDepth).ToArray();
            Check(s.bottomVertices.Length >= 69 && Mathf.Abs(bottomDepth - .43f) < 1e-6f, "actual source UV1 identifies the full R13 lower ring");
            return s;
        }
        static string HashPH02Snapshot(PH02Snapshot s)
        {
            using (var sha = SHA256.Create())
            using (var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(s.P.Length);
                for (int i = 0; i < s.P.Length; i++)
                {
                    Write(writer, s.P[i]); Write(writer, s.N[i]); writer.Write(s.U[i].x); writer.Write(s.U[i].y);
                    writer.Write(s.T[i].x); writer.Write(s.T[i].y); writer.Write(s.T[i].z); writer.Write(s.T[i].w);
                }
                writer.Write(s.Indices.Length); foreach (int i in s.Indices) writer.Write(i);
                writer.Write(s.U1.Length); foreach (Vector2 u in s.U1) { writer.Write(u.x); writer.Write(u.y); }
                writer.Write(s.C.Length); foreach (Color c in s.C) { writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a); }
                writer.Flush(); stream.FlushFinalBlock(); return Hex(sha.Hash);
            }
        }
        static Vector3 Tangent3(Vector4 t) => new Vector3(t.x, t.y, t.z);
        static void VerifyPH02Placement(GameObject root, KokerboomDescriptor description, Mesh mesh,
            Vector3[] p, Vector3[] n, Vector2[] uv, Color[] colours, Vector4[] tangent, Vector2[] uv1,
            int seed, float age, int lod, Report report)
        {
            PH02Snapshot source = ph02Snapshot;
            Check(source != null && p.Length % source.P.Length == 0, "generated PH02 array is composed of complete input components");
            int copies = p.Length / source.P.Length;
            Check(copies == description.terminalRosettes && copies > 0 && tangent.Length == p.Length && uv1.Length == p.Length,
                "actual PH02 component multiplicity and tangent/UV1 channels match the requested family descriptor");
            var record = new PH02Placement { seed = seed, age01 = age, lod = lod, copies = copies };
            report.ph02Placements.Add(record);
            bool attributes = true;
            for (int i = 0; i < p.Length; i++)
            {
                int local = i % source.P.Length;
                attributes &= uv[i].Equals(source.U[local]) && uv1[i].Equals(source.U1[local]) && colours[i].Equals(source.C[local]) &&
                    tangent[i].w.Equals(source.T[local].w) && Finite(tangent[i].x) && Finite(tangent[i].y) && Finite(tangent[i].z) &&
                    Mathf.Abs(Tangent3(tangent[i]).magnitude - 1) < .001f;
            }
            record.checkedAttributeVertices = p.Length; record.attributesPreserved = attributes;
            Check(attributes, "all generated PH02 UV0/UV1/colour/tangent-W values preserve their source component");
            int[] indices = mesh.triangles;
            Check(indices.Length == source.Indices.Length * copies, "generated PH02 copies retain the full input triangle count");
            Vector3 sx = (source.P[source.anchorB] - source.P[source.anchorA]).normalized;
            Vector3 sz = Vector3.Cross(sx, source.P[source.anchorC] - source.P[source.anchorA]).normalized;
            Vector3 sy = Vector3.Cross(sz, sx);
            float sourceSpan = Vector3.Distance(source.P[source.anchorB], source.P[source.anchorA]);
            float closestFocus = float.PositiveInfinity; int closestCopy = -1;
            Similarity focus = null; bool patterns = true, proper = true;
            Transform marker = root.transform.Find("Inspection/LowerCrownJoin");
            Check(marker != null && Finite(marker.localPosition), "actual lower crown marker exists");
            for (int copy = 0; copy < copies; copy++)
            {
                int offset = copy * source.P.Length;
                Vector3 a = p[offset + source.anchorA], b = p[offset + source.anchorB], c = p[offset + source.anchorC];
                Vector3 gx = (b - a).normalized, gz = Vector3.Cross(gx, c - a).normalized, gy = Vector3.Cross(gz, gx);
                float scale = Vector3.Distance(a, b) / sourceSpan;
                var fit = new Similarity { sx = sx, sy = sy, sz = sz, gx = gx, gy = gy, gz = gz, scale = scale };
                fit.origin = a - fit.Rotate(source.P[source.anchorA]) * scale;
                proper &= Finite(fit.origin) && scale > 0 && Vector3.Dot(gx, Vector3.Cross(gy, gz)) > .99999f;
                record.minimumScale = Mathf.Min(record.minimumScale, scale); record.maximumScale = Mathf.Max(record.maximumScale, scale);
                for (int local = 0; local < source.P.Length; local += Math.Max(1, source.P.Length / 32))
                {
                    record.sampledPositionVertices++;
                    record.maximumPositionResidual = Mathf.Max(record.maximumPositionResidual, Vector3.Distance(p[offset + local], fit.origin + fit.Rotate(source.P[local]) * scale));
                    record.minimumNormalDot = Mathf.Min(record.minimumNormalDot, Vector3.Dot(n[offset + local].normalized, fit.Rotate(source.N[local]).normalized));
                    record.minimumTangentDot = Mathf.Min(record.minimumTangentDot, Vector3.Dot(Tangent3(tangent[offset + local]).normalized, fit.Rotate(Tangent3(source.T[local])).normalized));
                }
                for (int triangle = 0; triangle < source.Indices.Length / 3; triangle += Math.Max(1, source.Indices.Length / 3 / 64))
                {
                    for (int corner = 0; corner < 3; corner++) patterns &= indices[copy * source.Indices.Length + triangle * 3 + corner] == source.Indices[triangle * 3 + corner] + offset;
                    record.sampledTrianglePatterns++;
                }
                float distance = Vector3.Distance(fit.origin, marker.localPosition);
                if (distance < closestFocus) { closestFocus = distance; closestCopy = copy; focus = fit; }
            }
            record.trianglePatternSamplesPreserved = patterns;
            record.properSimilaritySamples = proper && record.maximumPositionResidual <= .00003f && record.minimumNormalDot >= .99999f && record.minimumTangentDot >= .99999f;
            Check(patterns, "sampled triangle patterns retain source winding and per-copy index offsets");
            Check(record.properSimilaritySamples, "sampled PH02 copy P/N/T follows a proper uniform similarity derived from actual vertices");
            Check(closestCopy >= 0 && focus != null, "actual copied mesh contains a lower-frame candidate for the focus marker");
            record.meshDerivedFocusOrigin = focus.origin; record.meshDerivedFocusAxis = focus.Rotate(Vector3.up).normalized;
            record.markerOriginError = closestFocus; record.markerAxisDot = Vector3.Dot(record.meshDerivedFocusAxis, marker.localRotation * Vector3.up);
            record.expectedFocusRadius = source.bottomRadius * focus.scale;
            double radii = 0;
            foreach (int local in source.bottomVertices)
            {
                Vector3 delta = p[closestCopy * source.P.Length + local] - focus.origin;
                float plane = Vector3.Dot(delta, record.meshDerivedFocusAxis);
                float radius = (delta - record.meshDerivedFocusAxis * plane).magnitude;
                radii += radius; record.maximumFocusRadiusError = Mathf.Max(record.maximumFocusRadiusError, Mathf.Abs(radius - record.expectedFocusRadius));
                record.maximumFocusPlaneError = Mathf.Max(record.maximumFocusPlaneError, Mathf.Abs(plane));
            }
            record.measuredFocusRadius = (float)(radii / source.bottomVertices.Length);
            Check(record.markerOriginError < .00003f && record.markerAxisDot > .99999f, "actual lower marker agrees with mesh-derived component origin and axis");
            Check(record.maximumFocusRadiusError < .00003f && record.maximumFocusPlaneError < .00003f, "placed lower support ring preserves measured radius and plane");
        }
        sealed class Similarity
        {
            public Vector3 sx, sy, sz, gx, gy, gz, origin; public float scale;
            public Vector3 Rotate(Vector3 v) => gx * Vector3.Dot(v, sx) + gy * Vector3.Dot(v, sy) + gz * Vector3.Dot(v, sz);
        }

        // BEGIN PURE WOOD TOPOLOGY: no Unity dependency; canaries can also run in a local C# host.
        [Serializable] public sealed class WoodTopology
        {
            public string mesh;
            public int seed, lod;
            public float age01;
            public int vertices, usedVertices, triangles, edges, connectedComponents;
            public int boundaryEdges, nonManifoldEdges, inconsistentWindingEdges, nonManifoldVertices;
            public int duplicateTriangles, degenerateIndexTriangles, invalidIndexTriangles, trailingIndices;
            public int firstBoundaryA = -1, firstBoundaryB = -1, firstNonManifoldA = -1, firstNonManifoldB = -1;
            public bool IsSingleClosedManifold => connectedComponents == 1 && boundaryEdges == 0 &&
                nonManifoldEdges == 0 && inconsistentWindingEdges == 0 && nonManifoldVertices == 0 &&
                duplicateTriangles == 0 && degenerateIndexTriangles == 0 && invalidIndexTriangles == 0 && trailingIndices == 0;
        }
        sealed class DisjointSet
        {
            readonly int[] parent, size;
            public DisjointSet(int count)
            { parent = new int[count]; size = new int[count]; for (int i = 0; i < count; i++) { parent[i] = i; size[i] = 1; } }
            public int Find(int item)
            { while (item != parent[item]) { parent[item] = parent[parent[item]]; item = parent[item]; } return item; }
            public void Join(int a, int b)
            {
                a = Find(a); b = Find(b); if (a == b) return;
                if (size[a] < size[b]) { int swap = a; a = b; b = swap; }
                parent[b] = a; size[a] += size[b];
            }
        }

        // Packed edge keys preserve all 64 identity bits. UInt64's default high^low hash
        // collides heavily for neighbouring vertex indices; avalanche before folding to int.
        sealed class PackedEdgeComparer : IEqualityComparer<ulong>
        {
            public static readonly PackedEdgeComparer Instance = new PackedEdgeComparer();
            public bool Equals(ulong x, ulong y) => x == y;
            public int GetHashCode(ulong key)
            {
                unchecked
                {
                    key ^= key >> 30; key *= 0xbf58476d1ce4e5b9UL;
                    key ^= key >> 27; key *= 0x94d049bb133111ebUL;
                    key ^= key >> 31;
                    return (int)(key ^ (key >> 32));
                }
            }
        }

        sealed class EdgeUse
        {
            public int count, directionBalance, firstTriangle, firstCornerA, firstCornerB;
        }
        struct TriangleKey : IEquatable<TriangleKey>
        {
            readonly int a, b, c;
            public TriangleKey(int x, int y, int z)
            {
                if (x > y) { int t = x; x = y; y = t; }
                if (y > z) { int t = y; y = z; z = t; }
                if (x > y) { int t = x; x = y; y = t; }
                a = x; b = y; c = z;
            }
            public bool Equals(TriangleKey other) => a == other.a && b == other.b && c == other.c;
            public override bool Equals(object other) => other is TriangleKey && Equals((TriangleKey)other);
            public override int GetHashCode() { unchecked { return (a * 397 ^ b) * 397 ^ c; } }
        }
        static WoodTopology AnalyseWoodTopology(int vertexCount, int[] indices)
        {
            var result = new WoodTopology { vertices = vertexCount, triangles = indices.Length / 3, trailingIndices = indices.Length % 3 };
            var faces = new DisjointSet(result.triangles);
            var corners = new DisjointSet(indices.Length);
            var edgeUses = new Dictionary<ulong, EdgeUse>(PackedEdgeComparer.Instance);
            var distinctFaces = new HashSet<TriangleKey>();
            var validFaces = new bool[result.triangles];
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2], triangle = i / 3;
                if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                { result.invalidIndexTriangles++; continue; }
                if (a == b || b == c || c == a) { result.degenerateIndexTriangles++; continue; }
                validFaces[triangle] = true;
                if (!distinctFaces.Add(new TriangleKey(a, b, c))) result.duplicateTriangles++;
                AddEdge(a, b, i, i + 1, triangle); AddEdge(b, c, i + 1, i + 2, triangle); AddEdge(c, a, i + 2, i, triangle);
            }
            result.edges = edgeUses.Count;
            foreach (var entry in edgeUses)
            {
                EdgeUse edge = entry.Value;
                if (edge.count == 1)
                {
                    result.boundaryEdges++;
                    if (result.firstBoundaryA < 0) { result.firstBoundaryA = (int)(entry.Key >> 32); result.firstBoundaryB = (int)(uint)entry.Key; }
                }
                if (edge.count > 2)
                {
                    result.nonManifoldEdges++;
                    if (result.firstNonManifoldA < 0) { result.firstNonManifoldA = (int)(entry.Key >> 32); result.firstNonManifoldB = (int)(uint)entry.Key; }
                }
                if (edge.count == 2 && edge.directionBalance != 0) result.inconsistentWindingEdges++;
            }
            var componentRoots = new HashSet<int>();
            var vertexFanRoot = new Dictionary<int, int>();
            var splitFanVertices = new HashSet<int>();
            for (int triangle = 0; triangle < result.triangles; triangle++)
            {
                if (!validFaces[triangle]) continue;
                componentRoots.Add(faces.Find(triangle));
                for (int k = 0; k < 3; k++)
                {
                    int corner = triangle * 3 + k, vertex = indices[corner], fanRoot = corners.Find(corner);
                    if (!vertexFanRoot.TryGetValue(vertex, out int firstRoot)) vertexFanRoot.Add(vertex, fanRoot);
                    else if (firstRoot != fanRoot) splitFanVertices.Add(vertex);
                }
            }
            result.usedVertices = vertexFanRoot.Count; result.connectedComponents = componentRoots.Count;
            // With exactly two faces per edge, a connected incident-face fan is a single cycle.
            // This catches two closed shells pinched together at one vertex, which edge counts miss.
            result.nonManifoldVertices = splitFanVertices.Count;
            return result;

            void AddEdge(int a, int b, int cornerA, int cornerB, int triangle)
            {
                int direction = a < b ? 1 : -1;
                if (a > b) { int t = a; a = b; b = t; t = cornerA; cornerA = cornerB; cornerB = t; }
                ulong key = ((ulong)(uint)a << 32) | (uint)b;
                if (!edgeUses.TryGetValue(key, out EdgeUse use))
                {
                    use = new EdgeUse { firstTriangle = triangle, firstCornerA = cornerA, firstCornerB = cornerB };
                    edgeUses.Add(key, use);
                }
                else
                {
                    faces.Join(use.firstTriangle, triangle);
                    corners.Join(use.firstCornerA, cornerA); corners.Join(use.firstCornerB, cornerB);
                }
                use.count++; use.directionBalance += direction;
            }
        }
        static void RunTopologyCanaries()
        {
            int[] tetra = { 0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3 };
            WoodTopology closed = AnalyseWoodTopology(4, tetra);
            Check(closed.IsSingleClosedManifold && closed.edges == 6, "topology canary: closed tetrahedron passes");
            WoodTopology open = AnalyseWoodTopology(4, new[] { 0, 2, 1, 0, 1, 3, 1, 2, 3 });
            Check(!open.IsSingleClosedManifold && open.boundaryEdges == 3, "topology canary: missing face exposes three boundary edges");
            var twoShells = new int[tetra.Length * 2];
            Array.Copy(tetra, twoShells, tetra.Length);
            for (int i = 0; i < tetra.Length; i++) twoShells[tetra.Length + i] = tetra[i] + 4;
            WoodTopology separate = AnalyseWoodTopology(8, twoShells);
            Check(!separate.IsSingleClosedManifold && separate.connectedComponents == 2 && separate.boundaryEdges == 0,
                "topology canary: two individually closed shells fail connectedness");
            for (int i = 0; i < tetra.Length; i++) twoShells[tetra.Length + i] = tetra[i] == 0 ? 0 : tetra[i] + 3;
            WoodTopology pinched = AnalyseWoodTopology(7, twoShells);
            Check(!pinched.IsSingleClosedManifold && pinched.nonManifoldVertices == 1 && pinched.nonManifoldEdges == 0,
                "topology canary: vertex-pinched closed shells fail despite two faces per edge");
            WoodTopology crowded = AnalyseWoodTopology(5, new[] { 0, 1, 2, 1, 0, 3, 0, 1, 4 });
            Check(!crowded.IsSingleClosedManifold && crowded.nonManifoldEdges == 1, "topology canary: three faces sharing an edge fail");
            int[] reversed = (int[])tetra.Clone(); int swap = reversed[0]; reversed[0] = reversed[1]; reversed[1] = swap;
            WoodTopology winding = AnalyseWoodTopology(4, reversed);
            Check(!winding.IsSingleClosedManifold && winding.inconsistentWindingEdges == 3, "topology canary: one inverted face fails winding");
        }
        // END PURE WOOD TOPOLOGY

        static void Write(BinaryWriter writer, Vector3 p) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
        static string PointText(Vector3 p) => "(" + p.x.ToString("R", CultureInfo.InvariantCulture) + "," + p.y.ToString("R", CultureInfo.InvariantCulture) + "," + p.z.ToString("R", CultureInfo.InvariantCulture) + ")";
        static string FileHash(string path) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(File.ReadAllBytes(path))); }
        static void DestroyInstances(List<GameObject> created)
        { foreach (var instance in created) if (instance != null) UnityEngine.Object.DestroyImmediate(instance); created.Clear(); }
        static void MustReject(Action action, string reason)
        {
            bool rejected = false; try { action(); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, reason);
        }
    }
}
