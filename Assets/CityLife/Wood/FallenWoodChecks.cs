using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.Wood
{
    /// <summary>
    /// Authored in-memory verification suite for procedural fallen-wood mesh generation,
    /// outward triangle winding, seam and cap coincidence, finite input validation,
    /// hierarchy fail-closed scale, owned mesh lifecycle, and bridge invariants.
    /// Authored pure C# verification suite (unrun in this source-only pass).
    /// </summary>
    public static class FallenWoodChecks
    {
        public static List<string> Run()
        {
            var passed = new List<string>();

            void Check(bool condition, string name)
            {
                if (!condition)
                    throw new InvalidOperationException("FALLEN WOOD CHECK FAILED: " + name);
                passed.Add(name);
            }

            // -------------------------------------------------------------
            // 1. Algorithmic PRNG stream determinism on same runtime
            // -------------------------------------------------------------
            {
                var profile = FallenWoodProfile.CreateBranchPreset();
                const uint testSeed = 0x4A8C193Eu;

                Mesh mesh1 = FallenWoodGenerator.GenerateMesh(profile, testSeed, out var meta1);
                Mesh mesh2 = FallenWoodGenerator.GenerateMesh(profile, testSeed, out var meta2);

                try
                {
                    Check(mesh1.vertexCount == mesh2.vertexCount, "determinism-vertex-count-identical");
                    Check(mesh1.triangles.Length == mesh2.triangles.Length, "determinism-triangle-count-identical");

                    var v1 = mesh1.vertices;
                    var v2 = mesh2.vertices;
                    bool vertsEqual = true;
                    for (int i = 0; i < v1.Length; i++)
                    {
                        if (v1[i] != v2[i])
                        {
                            vertsEqual = false;
                            break;
                        }
                    }
                    Check(vertsEqual, "determinism-vertices-identical");

                    var c1 = mesh1.colors32;
                    var c2 = mesh2.colors32;
                    bool colorsEqual = true;
                    for (int i = 0; i < c1.Length; i++)
                    {
                        if (c1[i].r != c2[i].r || c1[i].g != c2[i].g || c1[i].b != c2[i].b || c1[i].a != c2[i].a)
                        {
                            colorsEqual = false;
                            break;
                        }
                    }
                    Check(colorsEqual, "determinism-colors-identical");

                    Check(Mathf.Approximately(meta1.massKg, meta2.massKg), "determinism-mass-identical");
                    Check(Mathf.Approximately(meta1.volumeM3, meta2.volumeM3), "determinism-volume-identical");
                    Check(meta1.width == meta2.width && meta1.height == meta2.height && meta1.depth == meta2.depth, "determinism-bounds-identical");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(mesh1);
                    UnityEngine.Object.DestroyImmediate(mesh2);
                }
            }

            // -------------------------------------------------------------
            // 2. Seed variation produces distinct morphology
            // -------------------------------------------------------------
            {
                var profile = FallenWoodProfile.CreateBranchPreset();
                Mesh meshA = FallenWoodGenerator.GenerateMesh(profile, 101u, out _);
                Mesh meshB = FallenWoodGenerator.GenerateMesh(profile, 9999u, out _);

                try
                {
                    var vA = meshA.vertices;
                    var vB = meshB.vertices;
                    bool anyDiff = false;
                    int minCount = Mathf.Min(vA.Length, vB.Length);
                    for (int i = 0; i < minCount; i++)
                    {
                        if (vA[i] != vB[i])
                        {
                            anyDiff = true;
                            break;
                        }
                    }
                    Check(anyDiff, "seed-variation-produces-distinct-vertices");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(meshA);
                    UnityEngine.Object.DestroyImmediate(meshB);
                }
            }

            // -------------------------------------------------------------
            // 3. Preset validation: Branch, Log, Kindling
            // -------------------------------------------------------------
            {
                // Branch Preset
                var branchProfile = FallenWoodProfile.CreateBranchPreset();
                Mesh branchMesh = FallenWoodGenerator.GenerateMesh(branchProfile, 42u, out var branchMeta);
                try
                {
                    Check(branchProfile.IsValid(), "branch-profile-is-valid");
                    Check(branchMeta.IsValid(), "branch-preset-metadata-valid");
                    Check(branchMeta.depth >= 0.5f && branchMeta.depth <= 0.8f, "branch-depth-within-metric-range");
                    Check(branchMeta.massKg >= 0.1f && branchMeta.massKg <= 2.5f, "branch-mass-sensible");
                    Check(branchMeta.width > 0.02f && branchMeta.height > 0.02f, "branch-cross-section-sensible");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(branchMesh);
                }

                // Log Preset (Zero-branch preset with zero fork parameters)
                var logProfile = FallenWoodProfile.CreateLogProfile();
                Mesh logMesh = FallenWoodGenerator.GenerateMesh(logProfile, 42u, out var logMeta);
                try
                {
                    Check(logProfile.IsValid(), "log-profile-zero-fork-is-valid");
                    Check(logMeta.IsValid(), "log-preset-metadata-valid");
                    Check(logMeta.depth >= 1.0f && logMeta.depth <= 1.3f, "log-depth-within-metric-range");
                    Check(logMeta.massKg >= 8f && logMeta.massKg <= 30f, "log-mass-sensible");
                    Check(logMeta.width >= 0.1f && logMeta.height >= 0.1f, "log-cross-section-sensible");
                    Check(!logMeta.hasBranchFork, "log-preset-has-no-branch-fork");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(logMesh);
                }

                // Kindling Preset
                var kindlingProfile = FallenWoodProfile.CreateKindlingPreset();
                Mesh kindlingMesh = FallenWoodGenerator.GenerateMesh(kindlingProfile, 42u, out var kindlingMeta);
                try
                {
                    Check(kindlingProfile.IsValid(), "kindling-profile-is-valid");
                    Check(kindlingMeta.IsValid(), "kindling-preset-metadata-valid");
                    Check(kindlingMeta.depth >= 0.3f && kindlingMeta.depth <= 0.45f, "kindling-depth-within-metric-range");
                    Check(kindlingMeta.massKg >= 0.02f && kindlingMeta.massKg <= 0.5f, "kindling-mass-sensible");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(kindlingMesh);
                }
            }

            // -------------------------------------------------------------
            // 4. Outward triangle winding & face-normal dot-product assertions
            // -------------------------------------------------------------
            {
                // 4a. Straight cylinder preset (no curvature, no roughness, no fork)
                var straightProfile = new FallenWoodProfile
                {
                    profileName = "Straight Test Tube",
                    length = 1.0f,
                    baseRadius = 0.1f,
                    tipRadius = 0.1f,
                    radialSegments = 8,
                    lengthSegments = 4,
                    curvature = 0f,
                    barkRoughness = 0f,
                    branchChance = 0f,
                    branchLength = 0f,
                    branchRadius = 0f,
                    branchAngleDeg = 0f,
                    dryDensityKgM3 = 680f
                };

                Mesh straightMesh = FallenWoodGenerator.GenerateMesh(straightProfile, 123u, out _);
                try
                {
                    var verts = straightMesh.vertices;
                    var norms = straightMesh.normals;
                    var tris = straightMesh.triangles;

                    int nonDegenerateTris = 0;
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        Vector3 v0 = verts[tris[t]];
                        Vector3 v1 = verts[tris[t + 1]];
                        Vector3 v2 = verts[tris[t + 2]];

                        Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0);
                        if (faceNormal.sqrMagnitude > 1e-12f)
                        {
                            nonDegenerateTris++;
                            Vector3 avgVertNormal = ((norms[tris[t]] + norms[tris[t + 1]] + norms[tris[t + 2]]) / 3f).normalized;
                            float dot = Vector3.Dot(faceNormal.normalized, avgVertNormal);
                            Check(dot > 0.2f, "straight-tube-triangle-winding-outward-dot-positive");
                        }
                    }
                    Check(nonDegenerateTris > 0, "straight-tube-has-nondegenerate-triangles");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(straightMesh);
                }

                // 4b. Curved preset with branch fork
                var curvedProfile = FallenWoodProfile.CreateBranchPreset();
                curvedProfile.branchChance = 1f; // Force fork generation
                Mesh curvedMesh = FallenWoodGenerator.GenerateMesh(curvedProfile, 555u, out var curvedMeta);
                try
                {
                    Check(curvedMeta.hasBranchFork, "curved-preset-branch-fork-present");
                    var verts = curvedMesh.vertices;
                    var norms = curvedMesh.normals;
                    var tris = curvedMesh.triangles;

                    int checkedTris = 0;
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        Vector3 v0 = verts[tris[t]];
                        Vector3 v1 = verts[tris[t + 1]];
                        Vector3 v2 = verts[tris[t + 2]];

                        Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0);
                        if (faceNormal.sqrMagnitude > 1e-12f)
                        {
                            checkedTris++;
                            Vector3 avgVertNormal = ((norms[tris[t]] + norms[tris[t + 1]] + norms[tris[t + 2]]) / 3f).normalized;
                            float dot = Vector3.Dot(faceNormal.normalized, avgVertNormal);
                            Check(dot > 0.05f, "curved-fork-triangle-winding-outward-dot-positive");
                        }
                    }
                    Check(checkedTris > 0, "curved-fork-has-nondegenerate-triangles");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(curvedMesh);
                }
            }

            // -------------------------------------------------------------
            // 5. Seam duplicate and cap perimeter coincidence checks
            // -------------------------------------------------------------
            {
                var roughProfile = FallenWoodProfile.CreateBranchPreset();
                roughProfile.barkRoughness = 0.008f; // Ensure prominent roughness
                Check(roughProfile.barkRoughness > 0f, "bark-roughness-is-non-zero");

                Mesh mesh = FallenWoodGenerator.GenerateMesh(roughProfile, 789u, out _);
                try
                {
                    var verts = mesh.vertices;
                    var uvs = mesh.uv;
                    int radialSegments = roughProfile.radialSegments;
                    int lengthSegments = roughProfile.lengthSegments;
                    int radialVertsPerRing = radialSegments + 1;
                    int ringCount = lengthSegments + 1;

                    // 5a. Check each trunk ring seam coincidence (j = 0 and j = radialSegments)
                    for (int i = 0; i < ringCount; i++)
                    {
                        int rowStart = i * radialVertsPerRing;
                        Vector3 v0 = verts[rowStart];
                        Vector3 vSeam = verts[rowStart + radialSegments];

                        Check(Vector3.Distance(v0, vSeam) < 1e-6f, $"trunk-ring-{i}-seam-positions-coincide");
                        Check(Mathf.Approximately(uvs[rowStart + radialSegments].x - uvs[rowStart].x, 1f), $"trunk-ring-{i}-uv-split-correct");
                    }

                    // 5b. Verify roughness actually displaces radii (not flattened or removed)
                    bool hasRoughVariation = false;
                    for (int j = 1; j < radialSegments; j++)
                    {
                        float d0 = Vector3.Distance(verts[0], verts[radialVertsPerRing / 2]);
                        float d1 = Vector3.Distance(verts[j], verts[(j + radialVertsPerRing / 2) % radialSegments]);
                        if (Mathf.Abs(d0 - d1) > 1e-5f)
                        {
                            hasRoughVariation = true;
                            break;
                        }
                    }
                    Check(hasRoughVariation, "roughness-actually-varies-ring-geometry");

                    // 5c. Base cap perimeter vertices coincide with base ring 0 vertices
                    int totalTrunkVerts = ringCount * radialVertsPerRing;
                    int baseCenterIdx = totalTrunkVerts;
                    int baseCapRingStart = baseCenterIdx + 1;

                    for (int j = 0; j <= radialSegments; j++)
                    {
                        Vector3 trunkVert = verts[j];
                        Vector3 capVert = verts[baseCapRingStart + j];
                        Check(Vector3.Distance(trunkVert, capVert) < 1e-6f, $"base-cap-perimeter-{j}-coincides-with-trunk-base");
                    }

                    // 5d. Tip cap perimeter vertices coincide with trunk tip ring vertices
                    int totalBaseCapVerts = 1 + (radialSegments + 1);
                    int tipCenterIdx = baseCenterIdx + totalBaseCapVerts;
                    int tipCapRingStart = tipCenterIdx + 1;
                    int tipTubeRingStart = (ringCount - 1) * radialVertsPerRing;

                    for (int j = 0; j <= radialSegments; j++)
                    {
                        Vector3 trunkVert = verts[tipTubeRingStart + j];
                        Vector3 capVert = verts[tipCapRingStart + j];
                        Check(Vector3.Distance(trunkVert, capVert) < 1e-6f, $"tip-cap-perimeter-{j}-coincides-with-trunk-tip");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }

            // -------------------------------------------------------------
            // 6. Input validation: finite bounds, NaN/infinity rejection, fork guards
            // -------------------------------------------------------------
            {
                // 6a. Null profile rejection
                bool threwNull = false;
                try
                {
                    FallenWoodGenerator.GenerateMesh(null, 42u, out _);
                }
                catch (ArgumentNullException)
                {
                    threwNull = true;
                }
                Check(threwNull, "validation-rejects-null-profile");

                // 6b. NaN / Infinity input rejections
                void CheckInvalidProfile(Action<FallenWoodProfile> corrupt, string testName)
                {
                    var p = FallenWoodProfile.CreateBranchPreset();
                    corrupt(p);
                    Check(!p.IsValid(), testName + "-profile-is-invalid");
                    bool threwArg = false;
                    try
                    {
                        FallenWoodGenerator.GenerateMesh(p, 42u, out _);
                    }
                    catch (ArgumentException)
                    {
                        threwArg = true;
                    }
                    Check(threwArg, testName + "-generator-throws-argument-exception");
                }

                CheckInvalidProfile(p => p.length = float.NaN, "reject-nan-length");
                CheckInvalidProfile(p => p.baseRadius = float.PositiveInfinity, "reject-infinity-base-radius");
                CheckInvalidProfile(p => p.tipRadius = -0.05f, "reject-negative-tip-radius");
                CheckInvalidProfile(p => p.radialSegments = 2, "reject-too-few-radial-segments");
                CheckInvalidProfile(p => p.radialSegments = 65, "reject-too-many-radial-segments");
                CheckInvalidProfile(p => p.lengthSegments = 1, "reject-too-few-length-segments");
                CheckInvalidProfile(p => p.curvature = -1f, "reject-negative-curvature");
                CheckInvalidProfile(p => p.barkRoughness = float.NaN, "reject-nan-roughness");
                CheckInvalidProfile(p => p.dryDensityKgM3 = 50f, "reject-under-density");
                CheckInvalidProfile(p => p.dryDensityKgM3 = 3000f, "reject-over-density");

                // 6c. Fork parameters: zero-branch remains valid; enabled fork requires valid finite parameters
                var zeroForkProfile = FallenWoodProfile.CreateLogProfile();
                zeroForkProfile.branchChance = 0f;
                zeroForkProfile.branchLength = 0f;
                zeroForkProfile.branchRadius = 0f;
                zeroForkProfile.branchAngleDeg = 0f;
                Check(zeroForkProfile.IsValid(), "zero-branch-with-zero-fork-params-valid");

                CheckInvalidProfile(p => { p.branchChance = 0.5f; p.branchLength = 0f; }, "reject-enabled-fork-zero-length");
                CheckInvalidProfile(p => { p.branchChance = 0.5f; p.branchRadius = -0.01f; }, "reject-enabled-fork-negative-radius");
                CheckInvalidProfile(p => { p.branchChance = 0.5f; p.branchAngleDeg = 10f; }, "reject-enabled-fork-angle-out-of-range");
                CheckInvalidProfile(p => { p.branchChance = 0.5f; p.branchLength = float.NaN; }, "reject-enabled-fork-nan-length");

                // 6d. Texture dimension bounds checks
                bool barkDimGuarded = false;
                try
                {
                    FallenWoodGenerator.GenerateProceduralBarkTexture(1u, -1, 128);
                }
                catch (ArgumentOutOfRangeException)
                {
                    barkDimGuarded = true;
                }
                Check(barkDimGuarded, "bark-texture-guards-negative-dimension");

                bool endGrainDimGuarded = false;
                try
                {
                    FallenWoodGenerator.GenerateProceduralEndGrainTexture(1u, 5000);
                }
                catch (ArgumentOutOfRangeException)
                {
                    endGrainDimGuarded = true;
                }
                Check(endGrainDimGuarded, "endgrain-texture-guards-excessive-dimension");
            }

            // -------------------------------------------------------------
            // 7. Hierarchy scale validation (Fail Closed on unsupported transforms)
            // -------------------------------------------------------------
            {
                var rootGo = new GameObject("CheckRoot");
                var childGo = new GameObject("CheckChild");
                childGo.transform.SetParent(rootGo.transform, false);

                try
                {
                    // Root without parent is supported
                    Check(FallenWoodInstance.IsSupportedHierarchy(rootGo.transform, out _), "root-hierarchy-supported");

                    // Child under unit scale parent is supported
                    rootGo.transform.localScale = Vector3.one;
                    Check(FallenWoodInstance.IsSupportedHierarchy(childGo.transform, out _), "unit-parent-hierarchy-supported");

                    // Non-uniform parent scale rejected
                    rootGo.transform.localScale = new Vector3(1f, 2f, 1f);
                    Check(!FallenWoodInstance.IsSupportedHierarchy(childGo.transform, out string nonUniformReason), "non-uniform-parent-rejected");
                    Check(!string.IsNullOrEmpty(nonUniformReason), "non-uniform-reason-provided");

                    // Uniform non-unit parent scale rejected
                    rootGo.transform.localScale = new Vector3(2f, 2f, 2f);
                    Check(!FallenWoodInstance.IsSupportedHierarchy(childGo.transform, out string nonUnitReason), "uniform-non-unit-parent-rejected");
                    Check(!string.IsNullOrEmpty(nonUnitReason), "uniform-non-unit-reason-provided");

                    // Negative/mirrored parent scale rejected
                    rootGo.transform.localScale = new Vector3(-1f, 1f, 1f);
                    Check(!FallenWoodInstance.IsSupportedHierarchy(childGo.transform, out string mirroredReason), "mirrored-parent-rejected");
                    Check(!string.IsNullOrEmpty(mirroredReason), "mirrored-reason-provided");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(childGo);
                    UnityEngine.Object.DestroyImmediate(rootGo);
                }
            }

            // -------------------------------------------------------------
            // 8. Owned mesh lifecycle, atomic replacement, and failed generation preservation
            // -------------------------------------------------------------
            {
                var testGo = new GameObject("InstanceTestGo");
                testGo.SetActive(false);
                var meshFilter = testGo.AddComponent<MeshFilter>();
                testGo.AddComponent<MeshRenderer>();
                var instance = testGo.AddComponent<FallenWoodInstance>();
                instance.autoGenerateOnEnable = false;

                // Create an external dummy mesh representing imported/external asset
                var externalMesh = new Mesh();
                externalMesh.name = "ExternalImportedMesh";
                meshFilter.sharedMesh = externalMesh;

                testGo.SetActive(true);

                GameObject parentGo = null;
                try
                {
                    Check(instance.OwnedMesh == null, "initial-instance-has-no-owned-mesh");
                    Check(meshFilter.sharedMesh == externalMesh, "initial-displayed-mesh-is-external-mesh");
                    Check(!instance.IsMeshOwned(externalMesh), "external-mesh-is-not-owned");

                    // 8a. Failed generation with invalid profile preserves existing external mesh
                    instance.profile = new FallenWoodProfile { profileName = "Invalid", length = -5f };
                    Mesh failedMesh = instance.Generate();
                    Check(failedMesh == null, "failed-profile-generation-returns-null");
                    Check(meshFilter.sharedMesh == externalMesh, "failed-profile-preserves-existing-displayed-mesh");
                    Check(instance.OwnedMesh == null, "failed-generation-does-not-own-mesh");

                    // 8b. Failed generation with unsupported parent hierarchy preserves existing external mesh
                    parentGo = new GameObject("Scale2Parent");
                    parentGo.transform.localScale = new Vector3(2f, 2f, 2f);
                    testGo.transform.SetParent(parentGo.transform, false);

                    instance.profile = FallenWoodProfile.CreateBranchPreset();
                    Mesh failedHierMesh = instance.Generate();
                    Check(failedHierMesh == null, "failed-hierarchy-generation-returns-null");
                    Check(meshFilter.sharedMesh == externalMesh, "failed-hierarchy-preserves-existing-displayed-mesh");
                    Check(parentGo.transform.localScale == new Vector3(2f, 2f, 2f), "parent-scale-unmutated-on-failure");
                    Check(testGo.transform.localScale == Vector3.one, "child-scale-unmutated-on-failure");

                    // Restore to root and generate valid procedural mesh
                    testGo.transform.SetParent(null, false);
                    Mesh genMesh = instance.Generate();
                    Check(genMesh != null, "valid-generation-succeeds");
                    Check(meshFilter.sharedMesh == genMesh, "shared-mesh-updated-to-generated-mesh");
                    Check(instance.IsMeshOwned(genMesh), "generated-mesh-is-owned");
                    Check(externalMesh != null, "external-mesh-was-not-destroyed-by-replacement");

                    // 8c. Regeneration replaces mesh and releases prior owned mesh
                    Mesh priorGen = genMesh;
                    instance.seed = 999u;
                    Mesh regenMesh = instance.Generate();
                    Check(regenMesh != null && regenMesh != priorGen, "regeneration-produces-new-mesh");
                    Check(instance.IsMeshOwned(regenMesh), "new-mesh-is-owned");
                    Check(!instance.IsMeshOwned(priorGen), "prior-mesh-ownership-released");

                    // 8d. Component destruction cleans up owned mesh
                    instance.ReleaseOwnedMesh();
                    Check(instance.OwnedMesh == null, "owned-mesh-released-on-cleanup");
                    Check(meshFilter.sharedMesh == null, "shared-mesh-cleared-when-owned-released");

                    // External mesh is still valid and untouched
                    Check(externalMesh != null, "external-mesh-survived-instance-cleanup");
                }
                finally
                {
                    if (testGo != null)
                    {
                        testGo.transform.SetParent(null, false);
                        UnityEngine.Object.DestroyImmediate(testGo);
                    }
                    if (parentGo != null) UnityEngine.Object.DestroyImmediate(parentGo);
                    if (externalMesh != null) UnityEngine.Object.DestroyImmediate(externalMesh);
                }
            }

            // -------------------------------------------------------------
            // 8e. Enabled auto-generation lifecycle, metadata retention, and rejection preservation
            // -------------------------------------------------------------
            {
                var autoGo = new GameObject("AutoGenerateInstanceGo");
                var autoMeshFilter = autoGo.AddComponent<MeshFilter>();
                autoGo.AddComponent<MeshRenderer>();
                // Active GameObject with default autoGenerateOnEnable=true generates owned mesh on enable
                var autoInstance = autoGo.AddComponent<FallenWoodInstance>();

                GameObject autoParentGo = null;
                var autoExternalMesh = new Mesh();
                autoExternalMesh.name = "AutoExternalSurvivalMesh";
                try
                {
                    // 1. Capture good owned/displayed mesh and metadata
                    Mesh initialOwnedMesh = autoInstance.OwnedMesh;
                    Check(initialOwnedMesh != null, "autogen-produces-owned-mesh");
                    Check(autoMeshFilter.sharedMesh == initialOwnedMesh, "autogen-displays-owned-mesh");
                    Check(autoInstance.IsMeshOwned(initialOwnedMesh), "autogen-mesh-is-owned");
                    FallenWoodAssetMetadata initialMetadata = autoInstance.Metadata;
                    Check(initialMetadata != null, "autogen-produces-metadata");

                    // 2. Reject invalid profile: assert unchanged same reference ownership/display/metadata and mesh survival
                    autoInstance.profile = new FallenWoodProfile { profileName = "InvalidAutoProfile", length = -5f };
                    Mesh failedProfileMesh = autoInstance.Generate();
                    Check(failedProfileMesh == null, "autogen-failed-profile-returns-null");
                    Check(autoInstance.OwnedMesh == initialOwnedMesh, "autogen-failed-profile-preserves-owned-mesh");
                    Check(autoMeshFilter.sharedMesh == initialOwnedMesh, "autogen-failed-profile-preserves-displayed-mesh");
                    Check(autoInstance.Metadata == initialMetadata, "autogen-failed-profile-preserves-metadata");
                    Check(initialOwnedMesh != null, "autogen-failed-profile-mesh-survived");

                    // 3. Reject unsupported parent hierarchy: assert unchanged same reference ownership/display/metadata and mesh survival
                    autoParentGo = new GameObject("AutoScale2Parent");
                    autoParentGo.transform.localScale = new Vector3(2f, 2f, 2f);
                    autoGo.transform.SetParent(autoParentGo.transform, false);

                    autoInstance.profile = FallenWoodProfile.CreateBranchPreset();
                    Mesh failedHierMesh = autoInstance.Generate();
                    Check(failedHierMesh == null, "autogen-failed-hierarchy-returns-null");
                    Check(autoInstance.OwnedMesh == initialOwnedMesh, "autogen-failed-hierarchy-preserves-owned-mesh");
                    Check(autoMeshFilter.sharedMesh == initialOwnedMesh, "autogen-failed-hierarchy-preserves-displayed-mesh");
                    Check(autoInstance.Metadata == initialMetadata, "autogen-failed-hierarchy-preserves-metadata");
                    Check(initialOwnedMesh != null, "autogen-failed-hierarchy-mesh-survived");
                    Check(autoParentGo.transform.localScale == new Vector3(2f, 2f, 2f), "autogen-parent-scale-unmutated-on-failure");
                    Check(autoGo.transform.localScale == Vector3.one, "autogen-child-scale-unmutated-on-failure");

                    // 4. Valid regeneration replaces mesh and releases prior owned mesh
                    autoGo.transform.SetParent(null, false);
                    Mesh priorAutoMesh = initialOwnedMesh;
                    autoInstance.seed = 888u;
                    Mesh regenMesh = autoInstance.Generate();
                    Check(regenMesh != null && regenMesh != priorAutoMesh, "autogen-regeneration-produces-new-mesh");
                    Check(autoMeshFilter.sharedMesh == regenMesh, "autogen-shared-mesh-updated-to-regenerated-mesh");
                    Check(autoInstance.IsMeshOwned(regenMesh), "autogen-new-mesh-is-owned");
                    Check(!autoInstance.IsMeshOwned(priorAutoMesh), "autogen-prior-mesh-ownership-released");

                    // 5. External asset survival check alongside auto-instance
                    Check(!autoInstance.IsMeshOwned(autoExternalMesh), "autogen-external-mesh-is-not-owned");
                    autoInstance.ReleaseOwnedMesh();
                    Check(autoInstance.OwnedMesh == null, "autogen-owned-mesh-released-on-cleanup");
                    Check(autoMeshFilter.sharedMesh == null, "autogen-shared-mesh-cleared-when-owned-released");
                    Check(autoExternalMesh != null, "autogen-external-mesh-survived-instance-cleanup");
                }
                finally
                {
                    if (autoGo != null)
                    {
                        autoGo.transform.SetParent(null, false);
                        UnityEngine.Object.DestroyImmediate(autoGo);
                    }
                    if (autoParentGo != null) UnityEngine.Object.DestroyImmediate(autoParentGo);
                    if (autoExternalMesh != null) UnityEngine.Object.DestroyImmediate(autoExternalMesh);
                }
            }

            // -------------------------------------------------------------
            // 9. Topological invariants & finite coordinates
            // -------------------------------------------------------------
            {
                var profile = FallenWoodProfile.CreateBranchPreset();
                Mesh mesh = FallenWoodGenerator.GenerateMesh(profile, 777u, out var meta);
                try
                {
                    var verts = mesh.vertices;
                    var norms = mesh.normals;
                    var uvs = mesh.uv;
                    var tris = mesh.triangles;

                    Check(verts.Length > 0, "topology-non-empty-vertices");
                    Check(tris.Length > 0 && (tris.Length % 3 == 0), "topology-valid-triangle-count");

                    bool finiteGeometry = true;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 v = verts[i];
                        Vector3 n = norms[i];
                        Vector2 u = uvs[i];
                        if (float.IsNaN(v.x) || float.IsInfinity(v.x) ||
                            float.IsNaN(v.y) || float.IsInfinity(v.y) ||
                            float.IsNaN(v.z) || float.IsInfinity(v.z) ||
                            float.IsNaN(n.x) || float.IsInfinity(n.x) ||
                            float.IsNaN(u.x) || float.IsInfinity(u.x))
                        {
                            finiteGeometry = false;
                            break;
                        }
                    }
                    Check(finiteGeometry, "topology-finite-coordinates-and-normals");

                    bool indicesInRange = true;
                    for (int i = 0; i < tris.Length; i++)
                    {
                        if (tris[i] < 0 || tris[i] >= verts.Length)
                        {
                            indicesInRange = false;
                            break;
                        }
                    }
                    Check(indicesInRange, "topology-indices-strictly-in-vertex-range");

                    // PhysicalDimensions bridge check (real source dependency on Assets/CityLife/Items/ItemDefinition.cs)
                    var physDim = meta.ToPhysicalDimensions();
                    Check(physDim.IsValid(), "physical-dimensions-bridge-valid");
                    Check(Mathf.Approximately(physDim.width, meta.width), "physical-dimensions-bridge-width-matches");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }

            // -------------------------------------------------------------
            // 10. Procedural texture generation (code-only Unity APIs)
            // -------------------------------------------------------------
            {
                Texture2D barkTex = FallenWoodGenerator.GenerateProceduralBarkTexture(42u, 32, 32);
                Texture2D endGrainTex = FallenWoodGenerator.GenerateProceduralEndGrainTexture(42u, 32);
                try
                {
                    Check(barkTex != null && barkTex.width == 32 && barkTex.height == 32, "procedural-bark-texture-valid");
                    Check(endGrainTex != null && endGrainTex.width == 32 && endGrainTex.height == 32, "procedural-endgrain-texture-valid");
                }
                finally
                {
                    if (barkTex != null) UnityEngine.Object.DestroyImmediate(barkTex);
                    if (endGrainTex != null) UnityEngine.Object.DestroyImmediate(endGrainTex);
                }
            }

            return passed;
        }
    }
}
