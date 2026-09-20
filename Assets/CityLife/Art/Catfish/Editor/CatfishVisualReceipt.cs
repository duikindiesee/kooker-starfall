using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CityLife.Art.Catfish.Editor
{
    public static class CatfishVisualReceipt
    {
        private static Camera camera;
        private static GameObject fish;
        private static GameObject lightObj;
        private static GameObject fillObj;
        private static string output;
        private static int frames;

        public static void Run()
        {
            try
            {
                output = Path.GetFullPath(@"C:\Users\irwin\.gemini\antigravity\brain\470c6136-22f2-449e-a500-0cb51f109f77");
                Directory.CreateDirectory(output);

                var camObj = new GameObject("InspectionCamera");
                camera = camObj.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.04f, 0.09f, 0.12f, 1f); // Deep river water studio backdrop
                camera.fieldOfView = 35f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 50f;

                lightObj = new GameObject("InspectionLight");
                var light = lightObj.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1.0f, 0.97f, 0.92f);
                light.intensity = 1.4f;
                lightObj.transform.rotation = Quaternion.Euler(38f, -40f, 0f);

                fillObj = new GameObject("FillLight");
                var fill = fillObj.AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.color = new Color(0.35f, 0.65f, 0.85f);
                fill.intensity = 0.85f;
                fillObj.transform.rotation = Quaternion.Euler(-25f, 140f, 0f);

                var fbxPath = "Assets/CityLife/Art/Catfish/catfish_swim.fbx";
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (fbx == null) throw new Exception("Could not load catfish_swim.fbx");

                var matPath = "Assets/CityLife/Art/Catfish/Catfish_Material.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                fish = Object.Instantiate(fbx);
                fish.name = "Catfish_Barber_Inspection";
                fish.transform.position = Vector3.zero;
                fish.transform.rotation = Quaternion.identity;
                fish.transform.localScale = Vector3.one;

                var smrs = fish.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in smrs)
                {
                    if (mat != null) smr.sharedMaterial = mat;
                    smr.updateWhenOffscreen = true;
                }

                // Sample swim animation pose
                var anim = fish.GetComponent<Animation>() ?? fish.GetComponentInChildren<Animation>();
                if (anim != null && anim.clip != null)
                {
                    anim.clip.SampleAnimation(fish, 0.28f);
                }

                frames = 0;
                EditorApplication.update += Tick;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void Tick()
        {
            if (++frames < 10) return;
            EditorApplication.update -= Tick;
            try
            {
                // Shot 1: Dynamic swimming 3D perspective showing whiskered face, pectoral fins, dorsal fin, and undulating body
                fish.transform.rotation = Quaternion.Euler(12f, 42f, -8f);
                Shot("2026-09-19-catfish-barber-swimming-3d.png", new Vector3(-1.15f, 0.52f, 1.30f), new Vector3(0.05f, 0.02f, 0.10f));

                // Shot 2: Full lateral profile showing full length from whiskers to tail fin
                fish.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                Shot("2026-09-19-catfish-barber-side-profile.png", new Vector3(-1.85f, 0.05f, 0f), new Vector3(0f, 0.02f, 0f));

                // Shot 3: Top-down dorsal view showing broad flattened skull, whisker array and mottled skin
                fish.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                Shot("2026-09-19-catfish-barber-dorsal.png", new Vector3(0f, 1.85f, 0f), new Vector3(0f, 0f, 0f));

                Debug.Log("[CatfishVisualReceipt] SUCCESS: Rendered all 3 shots to artifacts folder!");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void Shot(string name, Vector3 position, Vector3 target)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target);

            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new Exception("URP render request unsupported");

            var previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                for (int i = 0; i < 4; i++) RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = rt;
                image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null) Object.DestroyImmediate(image);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
