using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    /// <summary>
    /// Spawns an integrated, coherent environment slice along the walk from the First Refuge to the river:
    /// 1. Weathered Sandstone Boulders: Framing the rocky terrace descent from the refuge (-147, 118) down toward the river.
    /// 2. Riverbank Hollow Driftwood Log: Natural hollow log resting on the terrace fringe with authentic splintered ends and compound colliders preserving line-of-sight through the open cavity.
    /// 3. Riparian River Sedges: Clustered densely along the damp riverbank waterline (X ~ -35 to -30, Z ~ 44 to 80) and shallows, non-blocking for player and NPC navigation.
    /// </summary>
    public static class RiverbankPilotSlice
    {
        public static GameObject SpawnPilotSlice(
            Transform parent,
            GameObject boulderPrefab = null, Material boulderMat = null,
            GameObject sedgePrefab = null, Material sedgeMat = null,
            GameObject logPrefab = null, Material logMat = null)
        {
            var root = new GameObject("Riverbank Pilot Slice");
            if (parent != null) root.transform.SetParent(parent, false);

            // Load assets if not explicitly passed
#if UNITY_EDITOR
            if (boulderPrefab == null)
                boulderPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CityLife/Art/RiverbankPilot/Boulder/Boulder.fbx");
            if (boulderMat == null)
                boulderMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/CityLife/Art/RiverbankPilot/Boulder/Boulder_Material.mat");

            if (sedgePrefab == null)
                sedgePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CityLife/Art/RiverbankPilot/Sedge/Sedge.fbx");
            if (sedgeMat == null)
                sedgeMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/CityLife/Art/RiverbankPilot/Sedge/Sedge_Material.mat");

            if (logPrefab == null)
                logPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CityLife/Art/RiverbankPilot/Log/Log.fbx");
            if (logMat == null)
                logMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/CityLife/Art/RiverbankPilot/Log/Log_Material.mat");
#endif

            // Fallback materials if still null
            if (boulderMat == null)
            {
                var s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                boulderMat = new Material(s) { name = "Fallback_Boulder_Mat", color = new Color(0.52f, 0.48f, 0.44f) };
            }
            if (sedgeMat == null)
            {
                var s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                sedgeMat = new Material(s) { name = "Fallback_Sedge_Mat", color = new Color(0.32f, 0.48f, 0.22f) };
                sedgeMat.SetFloat("_Cull", 0f);
            }
            if (logMat == null)
            {
                var s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                logMat = new Material(s) { name = "Fallback_Log_Mat", color = new Color(0.38f, 0.28f, 0.20f) };
                logMat.SetFloat("_Cull", 0f);
            }

            // -----------------------------------------------------------------
            // 1. Weathered Boulders Along Refuge Descent & River Edge
            // -----------------------------------------------------------------
            var boulderSpecs = new[]
            {
                // Descent Path Framing
                new { pos = new Vector2(-118f, 108f), scale = 1.15f, rot = new Vector3(4f, 38f, -6f), desc = "Refuge terrace descent boulder" },
                new { pos = new Vector2(-96f, 94f), scale = 0.95f, rot = new Vector3(-8f, 142f, 3f), desc = "Mid-slope transition boulder" },
                // Terrace Fringe & Log Framing
                new { pos = new Vector2(-64f, 72f), scale = 1.05f, rot = new Vector3(5f, 210f, -4f), desc = "Terrace fringe boulder near hollow log" },
                new { pos = new Vector2(-51f, 62f), scale = 0.85f, rot = new Vector3(-3f, 65f, 8f), desc = "Lower terrace bank boulder" },
                // Riverbed Shallows & Plunge Basin
                new { pos = new Vector2(-28f, 62f), scale = 1.10f, rot = new Vector3(2f, 180f, 0f), desc = "Partially submerged riverbed boulder" },
                new { pos = new Vector2(-18f, -85f), scale = 1.25f, rot = new Vector3(-6f, 310f, 4f), desc = "South waterfall basin rim boulder" }
            };

            for (int i = 0; i < boulderSpecs.Length; i++)
            {
                var spec = boulderSpecs[i];
                var bGo = boulderPrefab != null ? UnityEngine.Object.Instantiate(boulderPrefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bGo.name = $"Pilot_Boulder_{i + 1}_{spec.desc.Replace(' ', '_')}";
                bGo.layer = 8; // Obstacle / environment
                bGo.transform.SetParent(root.transform, false);

                float y = CoastalTerrain.Height(spec.pos.x, spec.pos.y) - 0.08f; // Embed slightly into terrain
                bGo.transform.position = new Vector3(spec.pos.x, y, spec.pos.y);
                bGo.transform.rotation = Quaternion.Euler(spec.rot);
                bGo.transform.localScale = Vector3.one * spec.scale;

                // Configure renderers and material
                var renderers = bGo.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    r.sharedMaterial = boulderMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                // Add convex mesh collider if not present
                var meshFilter = bGo.GetComponentInChildren<MeshFilter>();
                if (meshFilter != null && bGo.GetComponentInChildren<Collider>() == null)
                {
                    var mc = meshFilter.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = meshFilter.sharedMesh;
                    mc.convex = true;
                }
            }

            // -----------------------------------------------------------------
            // 2. Hollow Driftwood Log V2 on Terrace Fringe with Open Cavity
            // -----------------------------------------------------------------
            {
                float logX = -58f;
                float logZ = 68f;
                float logY = CoastalTerrain.Height(logX, logZ) - 0.05f;

                var lGo = logPrefab != null ? UnityEngine.Object.Instantiate(logPrefab) : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                lGo.name = "Pilot_Hollow_Log_V2_Terrace";
                lGo.layer = 8;
                lGo.transform.SetParent(root.transform, false);

                lGo.transform.position = new Vector3(logX, logY, logZ);
                lGo.transform.rotation = Quaternion.Euler(-2f, 32f, 4f);
                lGo.transform.localScale = Vector3.one;

                var renderers = lGo.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    r.sharedMaterial = logMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                // Clean out any default convex collider that would seal the cavity
                var existingCols = lGo.GetComponentsInChildren<Collider>(true);
                foreach (var c in existingCols) UnityEngine.Object.DestroyImmediate(c);

                // Add 4 compound perimeter BoxColliders to preserve line-of-sight through the hollow cavity
                // Length is along local X (~1.90m), diameter is ~1.0m (Y and Z).
                var colParent = new GameObject("Log_Hollow_Compound_Colliders");
                colParent.transform.SetParent(lGo.transform, false);
                colParent.layer = 8;

                // Top arch wall
                var topCol = colParent.AddComponent<BoxCollider>();
                topCol.center = new Vector3(0f, 0.42f, 0f);
                topCol.size = new Vector3(1.88f, 0.22f, 0.72f);

                // Bottom arch wall
                var botCol = colParent.AddComponent<BoxCollider>();
                botCol.center = new Vector3(0f, -0.42f, 0f);
                botCol.size = new Vector3(1.88f, 0.22f, 0.72f);

                // Side wall 1 (+Z)
                var sideCol1 = colParent.AddComponent<BoxCollider>();
                sideCol1.center = new Vector3(0f, 0f, 0.42f);
                sideCol1.size = new Vector3(1.88f, 0.64f, 0.22f);

                // Side wall 2 (-Z)
                var sideCol2 = colParent.AddComponent<BoxCollider>();
                sideCol2.center = new Vector3(0f, 0f, -0.42f);
                sideCol2.size = new Vector3(1.88f, 0.64f, 0.22f);
            }

            // -----------------------------------------------------------------
            // 3. Riparian Sedges Clustered Along Damp River Waterline
            // -----------------------------------------------------------------
            var sedgeSpecs = new[]
            {
                // Main riparian waterline cluster along river terrace toe
                new { pos = new Vector2(-35f, 72f), scale = 0.75f, yaw = 45f },
                new { pos = new Vector2(-33f, 66f), scale = 0.85f, yaw = 110f },
                new { pos = new Vector2(-34f, 58f), scale = 0.70f, yaw = 205f },
                new { pos = new Vector2(-30f, 52f), scale = 0.80f, yaw = 290f },
                new { pos = new Vector2(-36f, 80f), scale = 0.65f, yaw = 75f },
                new { pos = new Vector2(-31f, 44f), scale = 0.75f, yaw = 160f },
                // Upstream shallow ford cluster
                new { pos = new Vector2(-24f, -10f), scale = 0.78f, yaw = 330f },
                // Moist rocky shade nook along upper descent
                new { pos = new Vector2(-98f, 98f), scale = 0.60f, yaw = 15f }
            };

            for (int i = 0; i < sedgeSpecs.Length; i++)
            {
                var spec = sedgeSpecs[i];
                var sGo = sedgePrefab != null ? UnityEngine.Object.Instantiate(sedgePrefab) : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                sGo.name = $"Pilot_Riparian_Sedge_{i + 1}";
                sGo.layer = 2; // Ignore Raycast - soft foliage never blocks navigation or line of sight
                sGo.transform.SetParent(root.transform, false);

                float y = CoastalTerrain.Height(spec.pos.x, spec.pos.y);
                sGo.transform.position = new Vector3(spec.pos.x, y, spec.pos.y);
                sGo.transform.rotation = Quaternion.Euler(0f, spec.yaw, 0f);
                sGo.transform.localScale = Vector3.one * spec.scale;

                var renderers = sGo.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    r.sharedMaterial = sedgeMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                // Ensure foliage has no solid blocking colliders so path navigability is 100% unimpeded
                var colliders = sGo.GetComponentsInChildren<Collider>(true);
                foreach (var c in colliders) UnityEngine.Object.DestroyImmediate(c);

                // Add restrained, organic wind sway
                var wind = sGo.AddComponent<RiverbankFoliageWind>();
                wind.SwayDegrees = 2.4f;
                wind.WindFrequency = 1.9f;
            }

            // -----------------------------------------------------------------
            // 4. Believable Ground Clutter & Micro-Foliage Clusters along Walk Corridor
            // -----------------------------------------------------------------
            var tussockMesh = RiverbankDecoration.GetOrCreateTussockMesh();
            var pebbleMesh = RiverbankDecoration.GetOrCreatePebbleMesh();
            var reedMesh = RiverbankDecoration.GetOrCreateReedMesh();
            var foliageMat = RiverbankDecoration.GetOrCreateFoliageMaterial();
            var stoneMat = RiverbankDecoration.GetOrCreateStoneMaterial();

            var clutterSpecs = new[]
            {
                // Boulder 1 lee shelter (-118, 108): dry bunchgrass & river pebbles
                new { pos = new Vector2(-116.2f, 107.2f), mesh = tussockMesh, mat = foliageMat, scale = 1.05f, isWind = true, desc = "Boulder1_Lee_Tussock" },
                new { pos = new Vector2(-117.1f, 106.8f), mesh = pebbleMesh, mat = stoneMat, scale = 1.15f, isWind = false, desc = "Boulder1_Lee_Pebbles" },

                // Boulder 2 slope shelter (-96, 94): dry bunchgrass & small gravel bed
                new { pos = new Vector2(-94.4f, 93.4f), mesh = tussockMesh, mat = foliageMat, scale = 0.95f, isWind = true, desc = "Boulder2_Shelter_Tussock" },
                new { pos = new Vector2(-95.2f, 92.8f), mesh = pebbleMesh, mat = stoneMat, scale = 1.0f, isWind = false, desc = "Boulder2_Shelter_Pebbles" },

                // Hollow Log terrace flank (-58, 68): bunchgrass along distressed bark & wood litter
                new { pos = new Vector2(-59.4f, 70.3f), mesh = tussockMesh, mat = foliageMat, scale = 1.10f, isWind = true, desc = "HollowLog_Flank_Tussock" },
                new { pos = new Vector2(-56.4f, 65.9f), mesh = tussockMesh, mat = foliageMat, scale = 0.90f, isWind = true, desc = "HollowLog_Toe_Tussock" },
                new { pos = new Vector2(-57.8f, 69.8f), mesh = pebbleMesh, mat = stoneMat, scale = 0.95f, isWind = false, desc = "HollowLog_WoodPebbles" },

                // Boulder 3 terrace fringe (-64, 72): bunchgrass & pebbles
                new { pos = new Vector2(-62.6f, 73.4f), mesh = tussockMesh, mat = foliageMat, scale = 1.0f, isWind = true, desc = "Boulder3_Fringe_Tussock" },
                new { pos = new Vector2(-65.1f, 73.8f), mesh = pebbleMesh, mat = stoneMat, scale = 1.1f, isWind = false, desc = "Boulder3_Fringe_Pebbles" },

                // Boulder 4 lower bank (-51, 62): gravel wash
                new { pos = new Vector2(-49.6f, 61.2f), mesh = pebbleMesh, mat = stoneMat, scale = 1.25f, isWind = false, desc = "Boulder4_GravelWash" },

                // Transition toward damp river waterline (-42, 68): reeds and wet pebbles
                new { pos = new Vector2(-41.5f, 66.8f), mesh = tussockMesh, mat = foliageMat, scale = 0.85f, isWind = true, desc = "Waterline_Transition_Tussock" },
                new { pos = new Vector2(-37.8f, 63.2f), mesh = reedMesh, mat = foliageMat, scale = 1.05f, isWind = true, desc = "Waterline_Riparian_Reeds" },
                new { pos = new Vector2(-36.5f, 62.0f), mesh = pebbleMesh, mat = stoneMat, scale = 1.30f, isWind = false, desc = "Waterline_Wet_PebbleBed" }
            };

            var clutterParent = new GameObject("Pilot_Corridor_Ground_Clutter");
            clutterParent.transform.SetParent(root.transform, false);

            for (int i = 0; i < clutterSpecs.Length; i++)
            {
                var spec = clutterSpecs[i];
                var cGo = new GameObject($"Pilot_Clutter_{i + 1}_{spec.desc}", typeof(MeshFilter), typeof(MeshRenderer));
                cGo.layer = 2; // Ignore Raycast - non-blocking ambient micro-detail
                cGo.transform.SetParent(clutterParent.transform, false);

                float y = CoastalTerrain.Height(spec.pos.x, spec.pos.y);
                cGo.transform.position = new Vector3(spec.pos.x, y, spec.pos.y);
                cGo.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                cGo.transform.localScale = Vector3.one * spec.scale;

                cGo.GetComponent<MeshFilter>().sharedMesh = spec.mesh;
                var mr = cGo.GetComponent<MeshRenderer>();
                mr.sharedMaterial = spec.mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;

                if (spec.isWind)
                {
                    var w = cGo.AddComponent<RiverbankFoliageWind>();
                    w.SwayDegrees = 1.8f;
                    w.WindFrequency = 1.7f;
                }
            }

            return root;
        }

        /// <summary>
        /// Verifies that a ray cast straight along the log's longitudinal cylinder axis passes
        /// cleanly through the open cavity, while rays offset into the outer shell hit the compound colliders.
        /// </summary>
        public static bool VerifyHollowCavityPassage(GameObject logRoot, out string detail)
        {
            if (logRoot == null)
            {
                detail = "Log root is null";
                return false;
            }

            Physics.SyncTransforms();

            Vector3 center = logRoot.transform.position;
            Vector3 logAxis = logRoot.transform.right; // FBX length is along X

            // 1. Ray through center cavity: from 2m outside end 1, through center, to 2m outside end 2
            Vector3 rayStart = center - logAxis * 2.0f;
            Vector3 rayDir = logAxis;
            float rayLen = 4.0f;

            if (Physics.Raycast(rayStart, rayDir, out RaycastHit centerHit, rayLen))
            {
                // If it hits something, verify it didn't hit inside the hollow log tunnel
                if (centerHit.collider.transform.IsChildOf(logRoot.transform))
                {
                    detail = $"Central raycast was blocked by {centerHit.collider.name} at distance {centerHit.distance:F2}m (cavity sealed!)";
                    return false;
                }
            }

            // 2. Off-center ray hitting the top arch wall: offset +0.42m along local Up
            Vector3 upOffset = logRoot.transform.up * 0.42f;
            Vector3 wallRayStart = rayStart + upOffset;

            if (!Physics.Raycast(wallRayStart, rayDir, out RaycastHit wallHit, rayLen))
            {
                detail = "Upper wall raycast failed to hit top arch collider";
                return false;
            }

            if (!wallHit.collider.transform.IsChildOf(logRoot.transform))
            {
                detail = $"Upper wall raycast hit foreign collider {wallHit.collider.name}";
                return false;
            }

            detail = $"Central cavity unobstructed (ray passed {rayLen:F1}m), upper wall collider engaged at {wallHit.distance:F2}m.";
            return true;
        }

        /// <summary>
        /// Validates full grounding, collider integrity, hollow cavity line-of-sight, and waterline clustering.
        /// </summary>
        public static bool VerifyPilotSlice(out string receipt)
        {
            var tempRoot = new GameObject("Temp_Pilot_Verification_Root");
            try
            {
                var slice = SpawnPilotSlice(tempRoot.transform);
                if (slice == null)
                {
                    receipt = "SpawnPilotSlice returned null";
                    return false;
                }

                // Verify Boulders
                var boulders = new List<Transform>();
                var sedges = new List<Transform>();
                Transform logTr = null;

                for (int i = 0; i < slice.transform.childCount; i++)
                {
                    var child = slice.transform.GetChild(i);
                    if (child.name.StartsWith("Pilot_Boulder")) boulders.Add(child);
                    else if (child.name.StartsWith("Pilot_Riparian_Sedge")) sedges.Add(child);
                    else if (child.name.StartsWith("Pilot_Hollow_Log")) logTr = child;
                }

                if (boulders.Count != 6)
                {
                    receipt = $"Expected 6 boulders, found {boulders.Count}";
                    return false;
                }

                foreach (var b in boulders)
                {
                    var col = b.GetComponentInChildren<Collider>();
                    if (col == null)
                    {
                        receipt = $"Boulder {b.name} lacks collider";
                        return false;
                    }
                    float expectedY = CoastalTerrain.Height(b.position.x, b.position.z) - 0.08f;
                    if (Mathf.Abs(b.position.y - expectedY) > 0.02f)
                    {
                        receipt = $"Boulder {b.name} Y mismatch: got {b.position.y:F2}, expected {expectedY:F2}";
                        return false;
                    }
                }

                // Verify Hollow Log
                if (logTr == null)
                {
                    receipt = "Hollow log not found in spawned slice";
                    return false;
                }

                var colliders = logTr.GetComponentsInChildren<BoxCollider>();
                if (colliders.Length != 4)
                {
                    receipt = $"Expected 4 compound BoxColliders on hollow log, found {colliders.Length}";
                    return false;
                }

                if (!VerifyHollowCavityPassage(logTr.gameObject, out string cavDetail))
                {
                    receipt = $"Hollow cavity verification failed: {cavDetail}";
                    return false;
                }

                // Verify Sedge
                if (sedges.Count != 8)
                {
                    receipt = $"Expected 8 sedges, found {sedges.Count}";
                    return false;
                }

                int waterlineClusterCount = 0;
                foreach (var s in sedges)
                {
                    // Verify no blocking colliders
                    if (s.GetComponentsInChildren<Collider>().Length > 0)
                    {
                        receipt = $"Sedge {s.name} has solid blocking colliders";
                        return false;
                    }

                    // Check clustering near damp river corridor (X in [-40, -20])
                    if (s.position.x >= -40f && s.position.x <= -20f)
                    {
                        waterlineClusterCount++;
                    }
                }

                if (waterlineClusterCount < 6)
                {
                    receipt = $"Expected at least 6 sedge clumps along damp river corridor, found {waterlineClusterCount}";
                    return false;
                }

                receipt = $"Pilot slice verified: 6 boulders grounded with convex colliders, hollow log with 4 compound colliders and open cavity, 8 sedge clumps with {waterlineClusterCount}/8 along damp waterline and 0 blocking colliders.";
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempRoot);
            }
        }
    }
}
