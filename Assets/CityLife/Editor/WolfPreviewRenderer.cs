using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityLife.World.Editor
{
    public static class WolfPreviewRenderer
    {
        public static void RenderWolfPreviews()
        {
            Debug.Log("[WolfPreviewRenderer] Starting wolf preview renders...");

            string outDir = Path.GetFullPath("evidence/reviews/round331/wolf");
            Directory.CreateDirectory(outDir);

            // 1. Root scene container
            var root = new GameObject("Wolf_Preview_Scene");

            try
            {
                // 2. Setup Lighting
                var lightGo = new GameObject("Sun Light");
                lightGo.transform.SetParent(root.transform);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.color = new Color(1.0f, 0.96f, 0.90f);
                lightGo.transform.rotation = Quaternion.Euler(42f, -38f, 0f);

                var fillLightGo = new GameObject("Fill Light");
                fillLightGo.transform.SetParent(root.transform);
                var fillLight = fillLightGo.AddComponent<Light>();
                fillLight.type = LightType.Directional;
                fillLight.intensity = 0.5f;
                fillLight.color = new Color(0.70f, 0.82f, 0.95f);
                fillLightGo.transform.rotation = Quaternion.Euler(-25f, 140f, 0f);

                // 3. Ground pedestal / rock
                var groundGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                groundGo.name = "Ground";
                groundGo.transform.SetParent(root.transform);
                groundGo.transform.position = new Vector3(0f, -0.2f, 0f);
                groundGo.transform.localScale = new Vector3(6f, 0.4f, 6f);
                var groundMr = groundGo.GetComponent<MeshRenderer>();
                Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var groundMat = new Material(litShader);
                groundMat.color = new Color(0.55f, 0.48f, 0.38f); // Sandstone / desert rock tone
                groundMat.SetFloat("_Smoothness", 0.08f);
                groundMr.sharedMaterial = groundMat;

                // 4. Wolf mesh and material setup
                var wolfMesh = CoastalWolfEcology.GetOrCreateWolfMesh();
                Shader wolfShader = Shader.Find("CityLife/Fire/TinderVertexColor") ?? litShader;
                var wolfMat = new Material(wolfShader) { name = "Wolf Material Preview" };
                wolfMat.color = Color.white;
                wolfMat.SetFloat("_Smoothness", 0.12f);

                var wolfGo = new GameObject("TimberWolf");
                wolfGo.transform.SetParent(root.transform);
                wolfGo.transform.position = Vector3.zero;
                wolfGo.transform.rotation = Quaternion.identity;

                var mf = wolfGo.AddComponent<MeshFilter>();
                mf.sharedMesh = wolfMesh;
                var mr = wolfGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = wolfMat;

                // 5. Setup Camera
                var camGo = new GameObject("RenderCamera");
                camGo.transform.SetParent(root.transform);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Color;
                cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f); // Studio dark slate backdrop
                cam.fieldOfView = 36f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 50f;

                Vector3 wolfCenter = new Vector3(0f, 0.55f, 0.15f);

                // Shot 1: Hero 3/4 perspective view
                RenderShot(cam, wolfCenter + new Vector3(-1.8f, 0.75f, 2.3f), wolfCenter,
                    Path.Combine(outDir, "wolf-01-hero-perspective.png"));

                // Shot 2: Full side profile view
                RenderShot(cam, wolfCenter + new Vector3(-3.0f, 0.25f, 0.0f), wolfCenter,
                    Path.Combine(outDir, "wolf-02-side-profile.png"));

                // Shot 3: Close-up of head, alert ears & muzzle
                Vector3 headCenter = new Vector3(0f, 0.78f, 0.58f);
                RenderShot(cam, headCenter + new Vector3(-0.85f, 0.35f, 1.25f), headCenter + Vector3.forward * 0.1f,
                    Path.Combine(outDir, "wolf-03-close-head.png"));

                // Shot 4: Desert outdoor lighting view
                cam.backgroundColor = new Color(0.42f, 0.58f, 0.78f); // Coastal blue sky
                RenderShot(cam, wolfCenter + new Vector3(1.9f, 0.65f, 2.1f), wolfCenter,
                    Path.Combine(outDir, "wolf-04-canyon-light.png"));

                Debug.Log("[WolfPreviewRenderer] Successfully rendered all 4 wolf views to " + outDir);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void RenderShot(Camera cam, Vector3 eyePos, Vector3 targetPos, string filePath)
        {
            cam.transform.position = eyePos;
            cam.transform.LookAt(targetPos);

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var prevRt = cam.targetTexture;
            var prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            RenderTexture.active = rt;
            cam.Render();

            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();

            File.WriteAllBytes(filePath, tex.EncodeToPNG());

            cam.targetTexture = prevRt;
            RenderTexture.active = prevActive;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);

            Debug.Log("[WolfPreviewRenderer] Wrote " + filePath);
        }
    }
}
