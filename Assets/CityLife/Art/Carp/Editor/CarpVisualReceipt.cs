using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CityLife.Art.Carp.Editor
{
    public static class CarpVisualReceipt
    {
        private static Camera camera;
        private static GameObject fish;
        private static GameObject lightObj;
        private static GameObject fillObj;
        private static GameObject rimObj;
        private static string output;
        private static int frames;

        public static void Run()
        {
            try
            {
                output = Path.GetFullPath(@"C:\Users\irwin\.gemini\antigravity\brain\470c6136-22f2-449e-a500-0cb51f109f77");
                Directory.CreateDirectory(output);

                ConfigureAssets();

                var camObj = new GameObject("CarpInspectionCamera");
                camera = camObj.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.04f, 0.09f, 0.12f, 1f); // Deep river water studio backdrop
                camera.fieldOfView = 34f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 50f;

                // Key light
                lightObj = new GameObject("KeyLight");
                var keyLight = lightObj.AddComponent<Light>();
                keyLight.type = LightType.Directional;
                keyLight.color = new Color(1.0f, 0.96f, 0.88f);
                keyLight.intensity = 1.45f;
                lightObj.transform.rotation = Quaternion.Euler(38f, -42f, 0f);

                // Water ambient fill
                fillObj = new GameObject("FillLight");
                var fill = fillObj.AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.color = new Color(0.32f, 0.62f, 0.80f);
                fill.intensity = 0.85f;
                fillObj.transform.rotation = Quaternion.Euler(-25f, 138f, 0f);

                // Caustic / rim light
                rimObj = new GameObject("RimLight");
                var rim = rimObj.AddComponent<Light>();
                rim.type = LightType.Directional;
                rim.color = new Color(0.75f, 0.92f, 0.88f);
                rim.intensity = 0.70f;
                rimObj.transform.rotation = Quaternion.Euler(-65f, -110f, 0f);

                var fbxPath = "Assets/CityLife/Art/Carp/carp_swim.fbx";
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (fbx == null) throw new Exception("Could not load Assets/CityLife/Art/Carp/carp_swim.fbx");

                var matPath = "Assets/CityLife/Art/Carp/Carp_Material.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                fish = Object.Instantiate(fbx);
                fish.name = "Gauteng_Common_Carp_Inspection";
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
                    anim.clip.SampleAnimation(fish, 0.35f);
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

        private static void ConfigureAssets()
        {
            // Configure Normal Map texture
            string normalPath = "Assets/CityLife/Art/Carp/Textures/Carp_Normal.png";
            var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }

            // Create or configure URP Lit Material
            string matPath = "Assets/CityLife/Art/Carp/Carp_Material.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader) { name = "Gauteng Common Carp Material" };
                AssetDatabase.CreateAsset(mat, matPath);
            }

            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CityLife/Art/Carp/Textures/Carp_BaseColor.png");
            var normTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CityLife/Art/Carp/Textures/Carp_Normal.png");

            if (baseTex != null)
            {
                mat.SetTexture("_BaseMap", baseTex);
                mat.SetTexture("_MainTex", baseTex);
            }
            if (normTex != null)
            {
                mat.SetTexture("_BumpMap", normTex);
                mat.EnableKeyword("_NORMALMAP");
            }
            mat.SetFloat("_Smoothness", 0.72f);
            mat.SetFloat("_Metallic", 0.12f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }

        private static void Tick()
        {
            if (++frames < 10) return;
            EditorApplication.update -= Tick;
            try
            {
                // Shot 1: Dynamic swimming 3D perspective showing undulating body, pectoral fins, dorsal fin, and deep golden flank
                fish.transform.rotation = Quaternion.Euler(14f, 42f, -8f);
                Shot("2026-09-19-carp-gauteng-swimming-3d.png", new Vector3(-1.25f, 0.55f, 1.45f), new Vector3(0.05f, 0.02f, 0.10f));

                // Shot 2: Full lateral anatomical profile showing arch of back, dorsal fin ray structure, ventral fins, and caudal fin
                fish.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                Shot("2026-09-19-carp-gauteng-side-profile.png", new Vector3(-2.10f, 0.05f, 0f), new Vector3(0f, 0.02f, 0f));

                // Shot 3: Dorsal swimming wave view showing traveling sinusoidal spine curve and bilateral pectoral fin trim
                fish.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                Shot("2026-09-19-carp-gauteng-dorsal.png", new Vector3(0f, 2.10f, 0f), new Vector3(0f, 0f, 0f));

                // Shot 4: Front three-quarter view showing mouth barbels, opercular gill plates, and wide deep chest
                fish.transform.rotation = Quaternion.Euler(8f, -145f, 4f);
                Shot("2026-09-19-carp-gauteng-front-threequarter.png", new Vector3(0.95f, 0.35f, -1.25f), new Vector3(0f, 0.02f, -0.45f));

                Debug.Log("[CarpVisualReceipt] SUCCESS: Rendered all 4 carp inspection shots to artifacts folder!");
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
