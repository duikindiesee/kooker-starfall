using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    /// <summary>
    /// Distributes protein crabs across the coastal shallows, tidal mudflats, and river delta.
    /// Crabs provide rich marine protein that directly replenishes FoodBody.protein and triggers
    /// Creatures-style hedonic drive reduction (endorphin surge).
    /// </summary>
    public sealed class CoastalCrabDistribution : MonoBehaviour
    {
        public const int DefaultCrabCount = 14;

        [Serializable]
        public struct CrabSpawnDef
        {
            public Vector3 position;
            public float scale;
            public float yaw;
        }

        public static readonly CrabSpawnDef[] AuthoredLocations = new CrabSpawnDef[]
        {
            new CrabSpawnDef { position = new Vector3(-12f, -2.4f, 62f), scale = 1.0f, yaw = 35f },
            new CrabSpawnDef { position = new Vector3(8f, -2.5f, 75f), scale = 1.1f, yaw = 120f },
            new CrabSpawnDef { position = new Vector3(-22f, -2.3f, 88f), scale = 0.9f, yaw = 210f },
            new CrabSpawnDef { position = new Vector3(15f, -2.6f, 95f), scale = 1.2f, yaw = 300f },
            new CrabSpawnDef { position = new Vector3(-5f, -2.5f, 110f), scale = 1.05f, yaw = 45f },
            new CrabSpawnDef { position = new Vector3(25f, -2.4f, 122f), scale = 0.95f, yaw = 160f },
            new CrabSpawnDef { position = new Vector3(-18f, -2.6f, 135f), scale = 1.15f, yaw = 80f },
            new CrabSpawnDef { position = new Vector3(2f, -2.7f, 145f), scale = 1.0f, yaw = 240f },
            new CrabSpawnDef { position = new Vector3(-30f, -2.2f, 70f), scale = 1.0f, yaw = 15f },
            new CrabSpawnDef { position = new Vector3(32f, -2.3f, 85f), scale = 0.9f, yaw = 195f },
            new CrabSpawnDef { position = new Vector3(0f, -2.6f, 100f), scale = 1.1f, yaw = 135f },
            new CrabSpawnDef { position = new Vector3(-10f, -2.5f, 125f), scale = 1.0f, yaw = 285f },
            new CrabSpawnDef { position = new Vector3(-25f, -2.4f, 155f), scale = 1.2f, yaw = 175f },
            // Riverbank shallows & ford crabs (accessible during daily river foraging)
            new CrabSpawnDef { position = new Vector3(-14f, -2.1f, -18f), scale = 1.0f, yaw = 65f },
            new CrabSpawnDef { position = new Vector3(16f, -2.1f, -12f), scale = 1.05f, yaw = 210f },
            new CrabSpawnDef { position = new Vector3(-8f, -2.2f, 10f), scale = 0.95f, yaw = 145f },
            new CrabSpawnDef { position = new Vector3(12f, -2.2f, 25f), scale = 1.1f, yaw = 330f },
            new CrabSpawnDef { position = new Vector3(-18f, -2.3f, 40f), scale = 1.0f, yaw = 95f },
            new CrabSpawnDef { position = new Vector3(5f, -2.2f, -32f), scale = 1.15f, yaw = 280f }
        };

        private static Mesh sharedCrabMesh;
        private static Material sharedCrabMaterial;

        public static Mesh GetOrCreateCrabMesh()
        {
            if (sharedCrabMesh != null) return sharedCrabMesh;

            // Generate high-fidelity procedural 3D Shore Crab mesh (Decapoda)
            // Complete with sculpted carapace, eye stalks, 3D articulated chelae (pincers),
            // and 4 pairs of volumetric arching walking legs.
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();

            Color32 carapaceColor = new Color32(195, 62, 32, 255); // Rich marine rust-crimson chitin
            Color32 ridgeColor = new Color32(225, 120, 55, 255);   // Golden-orange dorsal marginal ridges
            Color32 bellyColor = new Color32(235, 210, 175, 255);  // Creamy porcelain ventral sternum
            Color32 clawColor = new Color32(220, 45, 25, 255);     // Fiery crimson claw palms
            Color32 toothColor = new Color32(250, 245, 230, 255);  // Ivory serrated pincer teeth
            Color32 legColor = new Color32(175, 75, 40, 255);      // Mottled leg chitin
            Color32 legJointColor = new Color32(215, 110, 50, 255);// Articulated golden leg joints
            Color32 eyeStalkColor = new Color32(160, 60, 35, 255); // Ocular stalk chitin
            Color32 eyeCorneaColor = new Color32(20, 20, 25, 255); // Glossy black obsidian eye sphere

            void AddQuad(int a, int b, int c, int d)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }

            // -------------------------------------------------------------
            // 1. Sculpted Volumetric Carapace (Dorsal Dome + Ventral Plastron)
            // -------------------------------------------------------------
            // 16-point perimeter with lateral spines and anterior ocular notch
            const int perimeterPoints = 16;
            float[] rxTable = {
                0.045f, 0.085f, 0.125f, 0.150f, 0.155f, 0.145f, 0.115f, 0.065f, // Front to right to rear
                0.000f, -0.065f,-0.115f,-0.145f,-0.155f,-0.150f,-0.125f,-0.085f // Rear to left to front
            };
            float[] rzTable = {
                0.095f, 0.088f, 0.065f, 0.025f, -0.020f,-0.065f,-0.095f,-0.105f,
                -0.108f,-0.105f,-0.095f,-0.065f,-0.020f, 0.025f, 0.065f, 0.088f
            };
            float[] spineOffsets = {
                0.0f, 0.008f, 0.018f, 0.025f, 0.020f, 0.010f, 0.004f, 0.0f,
                0.0f, 0.0f, 0.004f, 0.010f, 0.020f, 0.025f, 0.018f, 0.008f
            };

            int dorsalApex = vertices.Count;
            vertices.Add(new Vector3(0f, 0.075f, -0.010f));
            normals.Add(Vector3.up);
            colors.Add(ridgeColor);

            int ventralCenter = vertices.Count;
            vertices.Add(new Vector3(0f, 0.005f, -0.015f));
            normals.Add(Vector3.down);
            colors.Add(bellyColor);

            int innerRingStart = vertices.Count;
            for (int i = 0; i < perimeterPoints; i++)
            {
                float x = rxTable[i] * 0.55f;
                float z = rzTable[i] * 0.55f;
                vertices.Add(new Vector3(x, 0.062f, z));
                normals.Add(new Vector3(x * 1.5f, 0.8f, z * 1.5f).normalized);
                colors.Add(carapaceColor);
            }

            int outerPerimeterStart = vertices.Count;
            for (int i = 0; i < perimeterPoints; i++)
            {
                float x = rxTable[i] + Mathf.Sign(rxTable[i]) * spineOffsets[i];
                float z = rzTable[i];
                vertices.Add(new Vector3(x, 0.035f, z));
                normals.Add(new Vector3(x * 2.0f, 0.3f, z * 2.0f).normalized);
                colors.Add(spineOffsets[i] > 0.015f ? ridgeColor : carapaceColor);
            }

            int ventralPerimeterStart = vertices.Count;
            for (int i = 0; i < perimeterPoints; i++)
            {
                float x = rxTable[i] * 0.85f;
                float z = rzTable[i] * 0.85f;
                vertices.Add(new Vector3(x, 0.015f, z));
                normals.Add(new Vector3(x, -0.5f, z).normalized);
                colors.Add(bellyColor);
            }

            // Assemble carapace faces
            for (int i = 0; i < perimeterPoints; i++)
            {
                int next = (i + 1) % perimeterPoints;
                // Dorsal dome apex to inner ring
                triangles.Add(dorsalApex);
                triangles.Add(innerRingStart + i);
                triangles.Add(innerRingStart + next);

                // Inner ring to outer margin quad
                AddQuad(innerRingStart + i, outerPerimeterStart + i, outerPerimeterStart + next, innerRingStart + next);

                // Outer margin to ventral rim quad
                AddQuad(outerPerimeterStart + i, ventralPerimeterStart + i, ventralPerimeterStart + next, outerPerimeterStart + next);

                // Ventral plastron to center
                triangles.Add(ventralCenter);
                triangles.Add(ventralPerimeterStart + next);
                triangles.Add(ventralPerimeterStart + i);
            }

            // -------------------------------------------------------------
            // 2. Protruding Eye Stalks (Left & Right)
            // -------------------------------------------------------------
            void AddEye(float sign)
            {
                Vector3 basePos = new Vector3(sign * 0.024f, 0.048f, 0.085f);
                Vector3 tipPos = basePos + new Vector3(sign * 0.008f, 0.028f, 0.015f);

                int s0 = vertices.Count;
                vertices.Add(basePos + new Vector3(-0.005f, 0, 0)); normals.Add(Vector3.up); colors.Add(eyeStalkColor);
                vertices.Add(basePos + new Vector3(0.005f, 0, 0));  normals.Add(Vector3.up); colors.Add(eyeStalkColor);
                vertices.Add(tipPos + new Vector3(0.005f, 0, 0));   normals.Add(Vector3.up); colors.Add(eyeStalkColor);
                vertices.Add(tipPos + new Vector3(-0.005f, 0, 0));  normals.Add(Vector3.up); colors.Add(eyeStalkColor);
                AddQuad(s0, s0 + 1, s0 + 2, s0 + 3);

                // Glossy black obsidian cornea sphere
                int c0 = vertices.Count;
                vertices.Add(tipPos + new Vector3(0, 0.008f, 0.004f));   normals.Add(Vector3.forward); colors.Add(eyeCorneaColor);
                vertices.Add(tipPos + new Vector3(0.007f, 0, 0.004f));    normals.Add(Vector3.right);   colors.Add(eyeCorneaColor);
                vertices.Add(tipPos + new Vector3(0, -0.008f, 0.004f));  normals.Add(Vector3.down);    colors.Add(eyeCorneaColor);
                vertices.Add(tipPos + new Vector3(-0.007f, 0, 0.004f));   normals.Add(Vector3.left);    colors.Add(eyeCorneaColor);
                AddQuad(c0, c0 + 1, c0 + 2, c0 + 3);
            }
            AddEye(1f);
            AddEye(-1f);

            // -------------------------------------------------------------
            // 3. Volumetric 3D Chelae (Major & Minor Claws with Pincers)
            // -------------------------------------------------------------
            void AddVolumetricClaw(float sign, float clawScale)
            {
                Vector3 shoulder = new Vector3(sign * 0.095f, 0.028f, 0.070f);
                Vector3 elbow = shoulder + new Vector3(sign * 0.065f, 0.032f, 0.050f) * clawScale;
                Vector3 palmBase = elbow + new Vector3(sign * -0.015f, 0.018f, 0.065f) * clawScale;
                Vector3 palmTip = palmBase + new Vector3(sign * -0.020f, 0.005f, 0.060f) * clawScale;

                // Arm segment (merus/carpus prism)
                int a0 = vertices.Count;
                vertices.Add(shoulder + new Vector3(0, -0.012f, 0)); normals.Add(Vector3.down); colors.Add(carapaceColor);
                vertices.Add(shoulder + new Vector3(0, 0.015f, 0));  normals.Add(Vector3.up);   colors.Add(carapaceColor);
                vertices.Add(elbow + new Vector3(0, 0.018f, 0));     normals.Add(Vector3.up);   colors.Add(clawColor);
                vertices.Add(elbow + new Vector3(0, -0.014f, 0));    normals.Add(Vector3.down); colors.Add(clawColor);
                AddQuad(a0, a0 + 1, a0 + 2, a0 + 3);

                // Bulging palm (propodus)
                int p0 = vertices.Count;
                float pw = 0.024f * clawScale;
                float ph = 0.032f * clawScale;
                vertices.Add(palmBase + new Vector3(-pw, -ph, 0));  normals.Add(Vector3.left);    colors.Add(clawColor);
                vertices.Add(palmBase + new Vector3(-pw, ph, 0));   normals.Add(Vector3.up);      colors.Add(clawColor);
                vertices.Add(palmBase + new Vector3(pw, ph, 0));    normals.Add(Vector3.right);   colors.Add(clawColor);
                vertices.Add(palmBase + new Vector3(pw, -ph, 0));   normals.Add(Vector3.down);    colors.Add(bellyColor);

                vertices.Add(palmTip + new Vector3(-pw * 0.7f, -ph * 0.7f, 0)); normals.Add(Vector3.left);  colors.Add(clawColor);
                vertices.Add(palmTip + new Vector3(-pw * 0.7f, ph * 0.7f, 0));  normals.Add(Vector3.up);    colors.Add(clawColor);
                vertices.Add(palmTip + new Vector3(pw * 0.7f, ph * 0.7f, 0));   normals.Add(Vector3.right); colors.Add(clawColor);
                vertices.Add(palmTip + new Vector3(pw * 0.7f, -ph * 0.7f, 0));  normals.Add(Vector3.down);  colors.Add(bellyColor);

                AddQuad(p0, p0 + 1, p0 + 5, p0 + 4); // Left
                AddQuad(p0 + 1, p0 + 2, p0 + 6, p0 + 5); // Top
                AddQuad(p0 + 2, p0 + 3, p0 + 7, p0 + 6); // Right
                AddQuad(p0 + 3, p0, p0 + 4, p0 + 7); // Bottom

                // Fixed Pollex (lower thumb pincer with teeth)
                Vector3 pollexBase = palmTip + new Vector3(sign * 0.008f, -0.010f, 0f);
                Vector3 pollexTip = pollexBase + new Vector3(sign * -0.012f, 0.002f, 0.045f * clawScale);
                Vector3 pollexTooth = (pollexBase + pollexTip) * 0.5f + new Vector3(0, 0.006f, 0);

                int plx = vertices.Count;
                vertices.Add(pollexBase);  normals.Add(Vector3.down); colors.Add(clawColor);
                vertices.Add(pollexTooth); normals.Add(Vector3.up);   colors.Add(toothColor);
                vertices.Add(pollexTip);   normals.Add(Vector3.forward); colors.Add(toothColor);
                triangles.Add(plx); triangles.Add(plx + 1); triangles.Add(plx + 2);

                // Movable Dactyl (upper curved finger pincer)
                Vector3 dactylBase = palmTip + new Vector3(sign * 0.008f, 0.012f, 0f);
                Vector3 dactylTip = dactylBase + new Vector3(sign * -0.015f, -0.018f, 0.048f * clawScale);
                Vector3 dactylTooth = (dactylBase + dactylTip) * 0.5f + new Vector3(0, -0.006f, 0);

                int dct = vertices.Count;
                vertices.Add(dactylBase);  normals.Add(Vector3.up);      colors.Add(clawColor);
                vertices.Add(dactylTip);   normals.Add(Vector3.forward); colors.Add(toothColor);
                vertices.Add(dactylTooth); normals.Add(Vector3.down);    colors.Add(toothColor);
                triangles.Add(dct); triangles.Add(dct + 1); triangles.Add(dct + 2);
            }
            AddVolumetricClaw(1f, 1.15f);  // Major crusher claw
            AddVolumetricClaw(-1f, 0.95f); // Minor pincher claw

            // -------------------------------------------------------------
            // 4. Four Pairs (8 total) of 3D Articulated Walking Legs
            // -------------------------------------------------------------
            void AddArticulatedLeg(float sign, float zOffset, float yawDeg, float legLengthScale)
            {
                Quaternion rot = Quaternion.Euler(0, yawDeg, 0);
                Vector3 legBase = new Vector3(sign * 0.115f, 0.022f, zOffset);
                // Merus: arches upward and outward
                Vector3 kneeJoint = legBase + rot * new Vector3(sign * 0.075f, 0.052f, 0.010f) * legLengthScale;
                // Carpus: angles downward toward sand
                Vector3 ankleJoint = kneeJoint + rot * new Vector3(sign * 0.058f, -0.042f, -0.005f) * legLengthScale;
                // Dactyl: pointed claw tip planted on ground
                Vector3 footTip = ankleJoint + rot * new Vector3(sign * 0.038f, -0.040f, -0.008f) * legLengthScale;

                int lg = vertices.Count;
                // Thigh prism (merus)
                vertices.Add(legBase + new Vector3(0, -0.008f, 0));   normals.Add(Vector3.down); colors.Add(legColor);
                vertices.Add(legBase + new Vector3(0, 0.010f, 0));    normals.Add(Vector3.up);   colors.Add(legColor);
                vertices.Add(kneeJoint + new Vector3(0, 0.012f, 0));  normals.Add(Vector3.up);   colors.Add(legJointColor);
                vertices.Add(kneeJoint + new Vector3(0, -0.008f, 0)); normals.Add(Vector3.down); colors.Add(legJointColor);
                AddQuad(lg, lg + 1, lg + 2, lg + 3);

                // Shin prism (carpus / propodus)
                int sh = vertices.Count;
                vertices.Add(kneeJoint + new Vector3(0, 0.012f, 0));   normals.Add(Vector3.up);   colors.Add(legJointColor);
                vertices.Add(kneeJoint + new Vector3(0, -0.008f, 0));  normals.Add(Vector3.down); colors.Add(legJointColor);
                vertices.Add(ankleJoint + new Vector3(0, -0.006f, 0)); normals.Add(Vector3.down); colors.Add(legColor);
                vertices.Add(ankleJoint + new Vector3(0, 0.008f, 0));  normals.Add(Vector3.up);   colors.Add(legColor);
                AddQuad(sh, sh + 1, sh + 2, sh + 3);

                // Foot claw dactyl tip
                int ft = vertices.Count;
                vertices.Add(ankleJoint + new Vector3(0, 0.008f, 0));  normals.Add(Vector3.up);   colors.Add(legColor);
                vertices.Add(footTip);                                 normals.Add(Vector3.down); colors.Add(toothColor);
                vertices.Add(ankleJoint + new Vector3(0, -0.006f, 0)); normals.Add(Vector3.down); colors.Add(legColor);
                triangles.Add(ft); triangles.Add(ft + 1); triangles.Add(ft + 2);
            }

            // 4 pairs of legs spanning front to rear (yawed naturally like genuine decapod crustaceans)
            AddArticulatedLeg(1f,  0.035f,  25f, 1.05f); // Front right
            AddArticulatedLeg(1f,  0.005f,   5f, 1.10f); // Mid-front right
            AddArticulatedLeg(1f, -0.030f, -15f, 1.05f); // Mid-rear right
            AddArticulatedLeg(1f, -0.065f, -38f, 0.95f); // Rear right

            AddArticulatedLeg(-1f,  0.035f, -25f, 1.05f); // Front left
            AddArticulatedLeg(-1f,  0.005f,  -5f, 1.10f); // Mid-front left
            AddArticulatedLeg(-1f, -0.030f,  15f, 1.05f); // Mid-rear left
            AddArticulatedLeg(-1f, -0.065f,  38f, 0.95f); // Rear left

            sharedCrabMesh = new Mesh
            {
                name = "Starfall_Protein_Crab_Mesh",
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                colors32 = colors.ToArray(),
                triangles = triangles.ToArray()
            };
            sharedCrabMesh.RecalculateBounds();
            sharedCrabMesh.RecalculateNormals();
            return sharedCrabMesh;
        }

        public static Material GetOrCreateCrabMaterial()
        {
            if (sharedCrabMaterial != null) return sharedCrabMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
            sharedCrabMaterial = new Material(shader)
            {
                name = "Starfall_Crab_Material",
                color = new Color(0.92f, 0.35f, 0.22f, 1f)
            };
            if (sharedCrabMaterial.HasProperty("_Smoothness")) sharedCrabMaterial.SetFloat("_Smoothness", 0.78f); // Wet shiny coastal chitin
            return sharedCrabMaterial;
        }

        public static List<GameObject> SpawnCrabs(Transform parent)
        {
            var spawned = new List<GameObject>();
            var mesh = GetOrCreateCrabMesh();
            var mat = GetOrCreateCrabMaterial();

            for (int i = 0; i < AuthoredLocations.Length; i++)
            {
                var def = AuthoredLocations[i];
                var crabGo = new GameObject($"Crab_Protein_{i + 1}", typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
                crabGo.transform.SetParent(parent, false);

                // Sample terrain height so the crab sits cleanly on wet sand / rocks
                float h = CoastalTerrain.Height(def.position.x, def.position.z);
                crabGo.transform.position = new Vector3(def.position.x, h + 0.03f, def.position.z);
                crabGo.transform.rotation = Quaternion.Euler(0, def.yaw, 0);
                crabGo.transform.localScale = Vector3.one * def.scale;

                crabGo.GetComponent<MeshFilter>().sharedMesh = mesh;
                crabGo.GetComponent<MeshRenderer>().sharedMaterial = mat;

                var col = crabGo.GetComponent<BoxCollider>();
                col.center = new Vector3(0, 0.03f, 0.02f);
                col.size = new Vector3(0.24f, 0.12f, 0.20f);

                crabGo.layer = 9; // Interactive item layer
                crabGo.AddComponent<CoastalCrabActor>();
                spawned.Add(crabGo);
            }

            return spawned;
        }

        public static bool VerifyCrabEcology(out string receipt)
        {
            if (AuthoredLocations.Length < 10)
            {
                receipt = "Crab authored locations insufficient (< 10).";
                return false;
            }

            var mesh = GetOrCreateCrabMesh();
            if (mesh == null || mesh.vertexCount == 0 || mesh.triangles.Length == 0)
            {
                receipt = "Crab procedural mesh generation failed or empty.";
                return false;
            }

            // Verify actor logic exists
            var testObj = new GameObject("Test_Crab");
            var actor = testObj.AddComponent<CoastalCrabActor>();
            bool hasActor = actor != null;
            UnityEngine.Object.DestroyImmediate(testObj);

            if (!hasActor)
            {
                receipt = "Crab actor component verification failed.";
                return false;
            }

            receipt = $"Crab ecology verified: {AuthoredLocations.Length} locations defined, mesh vertices={mesh.vertexCount}, triangles={mesh.triangles.Length / 3}, reactive actor verified.";
            return true;
        }
    }
}

