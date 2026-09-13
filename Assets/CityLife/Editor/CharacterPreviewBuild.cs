using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CityLife.World.Editor
{
    public static class CharacterPreviewBuild
    {
        public const string Version = "0.0.3-preview.1";
        private static string folder;
        private static int materialCount;
        private static Material Lit(string name, Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", .15f);
            AssetDatabase.CreateAsset(m, folder + "/Material-" + materialCount++ + ".mat"); return m;
        }
        private static GameObject Shape(string name, PrimitiveType kind, Vector3 position, Vector3 scale, Material material, bool solid = true)
        {
            var o = GameObject.CreatePrimitive(kind); o.name = name; o.transform.position = position; o.transform.localScale = scale;
            o.layer = solid ? 8 : 0; o.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) Object.DestroyImmediate(o.GetComponent<Collider>());
            return o;
        }
        private static Transform Marker(string name, Vector3 position)
        { var o = new GameObject(name); o.transform.position = position; return o.transform; }

        public static void Run() => Build(false);
        public static void RunNpc() => Build(true);
        private static void Build(bool npc)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated batch build.");
            IslandValidation.Run();
            if (npc) NpcMilestoneValidation.Run();
            string versionName = npc ? "0.0.4-preview.1" : Version;
            string executable = npc ? "KookerStarfallNpc" : "KookerStarfallCharacter";
            string id = executable + "-" + versionName + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            folder = "Assets/CityLife/GeneratedPreview-Character-" + id; materialCount = 0;
            string output = "Builds/" + id + "/" + executable + ".exe";
            if (Directory.Exists(folder) || Directory.Exists(Path.GetDirectoryName(output))) throw new IOException("Unique output already exists.");
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.35f, .46f, .66f);
            RenderSettings.ambientEquatorColor = new Color(.3f, .31f, .34f);
            RenderSettings.ambientGroundColor = new Color(.32f, .23f, .16f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(.06f, .12f, .21f);
            RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = .012f;
            var sun = new GameObject("Warm starlight").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1, .81f, .6f); sun.intensity = 2;
            sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(42, -30, 0);
            var fill = new GameObject("Cool fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(.3f, .58f, 1); fill.intensity = .55f;
            fill.transform.rotation = Quaternion.Euler(25, 155, 0);
            Material sand = Lit("Warm desert stone", new Color(.5f, .30f, .16f));
            Material rock = Lit("Weathered obstacle", new Color(.28f, .19f, .14f));
            Material pale = Lit("Interaction plinths", new Color(.54f, .47f, .32f));
            Material blue = Lit("Turquoise crystal", new Color(.06f, .75f, .89f));
            blue.EnableKeyword("_EMISSION"); blue.SetColor("_EmissionColor", new Color(.01f, .35f, .42f));
            Shape("Test ground", PrimitiveType.Cube, new Vector3(0, -.25f, 0), new Vector3(26, .5f, 26), sand);
            Shape("Route obstacle", PrimitiveType.Cube, new Vector3(0, .9f, 0), new Vector3(3, 1.8f, .8f), rock);
            Shape("Source plinth", PrimitiveType.Cylinder, new Vector3(0, .43f, 4), new Vector3(.85f, .43f, .85f), pale);
            Shape("Destination plinth", PrimitiveType.Cylinder, new Vector3(6, .43f, 3), new Vector3(.85f, .43f, .85f), pale);
            foreach (int side in new[] { -1, 1 })
            {
                Shape("Stage edge X " + side, PrimitiveType.Cube, new Vector3(side * 12, .6f, 0), new Vector3(.5f, 1.2f, 24), rock);
                Shape("Stage edge Z " + side, PrimitiveType.Cube, new Vector3(0, .6f, side * 12), new Vector3(24, 1.2f, .5f), rock);
            }
            for (int i = 0; i < 9; i++)
            {
                var b = Shape("Distant rock " + i, PrimitiveType.Sphere, new Vector3(-22 + i * 5, 1, 23 + i % 3 * 2),
                    new Vector3(5 + i % 3, 3 + i % 4, 4), rock, false);
                b.transform.rotation = Quaternion.Euler(i * 21, i * 17, i * 13);
            }
            var crystal = Shape("Carryable crystal", PrimitiveType.Sphere, new Vector3(0, 1.08f, 4),
                new Vector3(.22f, .4f, .22f), blue, false).transform;

            var actorObject = new GameObject("First inhabitant"); actorObject.layer = 9;
            actorObject.transform.position = new Vector3(0, .1f, -5);
            var actor = actorObject.AddComponent<CharacterPreviewActor>();
            var capsule = actorObject.AddComponent<CharacterController>();
            capsule.height = 1.85f; capsule.center = new Vector3(0, .93f, 0); capsule.radius = .3f;
            capsule.skinWidth = .025f; capsule.stepOffset = .25f; capsule.slopeLimit = 45; capsule.minMoveDistance = 0;
            actor.Capsule = capsule;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetImport.Body));
            model.transform.SetParent(actorObject.transform, false);
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            foreach (Transform t in model.GetComponentsInChildren<Transform>()) t.gameObject.layer = 9;
            actor.Animator = model.GetComponent<Animator>();
            if (actor.Animator.avatar == null || !actor.Animator.avatar.isValid || !actor.Animator.avatar.isHuman)
                throw new InvalidOperationException("The selected adult body must map to a valid humanoid avatar.");
            actor.Animator.applyRootMotion = false; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var clips = AssetDatabase.LoadAllAssetsAtPath(CharacterAssetImport.Motions).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToArray();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Humanoid.controller");
            var states = controller.layers[0].stateMachine;
            foreach (var pair in new[] { ("Idle", "Idle_Loop"), ("Walk", "Walk_Loop"), ("Interact", "Interact") })
            {
                var clip = clips.Single(c => c.name == pair.Item2 || c.name == "Armature|" + pair.Item2);
                if (!clip.humanMotion) throw new InvalidOperationException("Required clip is not humanoid: " + clip.name);
                var state = states.AddState(pair.Item1); state.motion = clip;
                if (pair.Item1 == "Idle") states.defaultState = state;
            }
            actor.Animator.runtimeAnimatorController = controller;
            Material skin = Lit("Original Quaternius skin", Color.white);
            skin.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterAssetImport.Root + "Skin_Dark.png"));
            skin.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterAssetImport.Root + "Skin_Normal.png"));
            skin.EnableKeyword("_NORMALMAP"); skin.SetFloat("_BumpScale", .55f);
            Material eyes = Lit("Original brown eyes", Color.white);
            eyes.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterAssetImport.Root + "Eye_Brown.png"));
            Material brows = Lit("Dark eyebrows", new Color(.045f, .027f, .017f));
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.sharedMaterial = r.name == "Eyes" ? eyes : r.name == "Eyebrows" ? brows : skin;
                r.updateWhenOffscreen = true;
            }
            var cameraObject = new GameObject("Following camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .055f, .105f);
            camera.fieldOfView = 48; camera.nearClipPlane = .04f; camera.farClipPlane = 150;
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            actor.View = cameraObject.AddComponent<CharacterPreviewCamera>(); actor.View.Target = actorObject.transform; actor.View.Follow();
            var roamer = actorObject.AddComponent<CharacterPreviewRoamer>(); actor.Roamer = roamer; roamer.Actor = actor;
            roamer.Crystal = crystal; roamer.SourceStand = Marker("Source interaction stand", new Vector3(0, 0, 3));
            roamer.DestinationStand = Marker("Destination interaction stand", new Vector3(6, 0, 2));
            roamer.DestinationSocket = Marker("Destination crystal socket", new Vector3(6, 1.08f, 3));
            var smoke = cameraObject.AddComponent<CharacterPreviewSmoke>(); smoke.Actor = actor; smoke.View = actor.View; smoke.Roamer = roamer;
            Directory.CreateDirectory("evidence/local/character");
            File.WriteAllText("evidence/local/character/selected-clips.json", JsonUtility.ToJson(new ClipEvidence {
                body = "Superhero_Male_FullBody", selectedClips = new[] { "Idle_Loop", "Walk_Loop", "Interact" },
                allImportedClips = clips.Select(x => x.name).ToArray(), humanoid = actor.Animator.avatar.isValid && actor.Animator.avatar.isHuman
            }, true));
            if (npc) NpcPreviewStage.Configure(actor, camera, folder);

            var oldGraphics = GraphicsSettings.defaultRenderPipeline;
            var sourcePipeline = oldGraphics as UniversalRenderPipelineAsset ??
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var pipeline = Object.Instantiate(sourcePipeline); pipeline.hideFlags = HideFlags.None;
            var settings = new SerializedObject(pipeline); var renderers = settings.FindProperty("m_RendererDataList");
            for (int i = 0; i < renderers.arraySize; i++)
            {
                var clone = Object.Instantiate(renderers.GetArrayElementAtIndex(i).objectReferenceValue); clone.hideFlags = HideFlags.None;
                var rendererSettings = new SerializedObject(clone);
                rendererSettings.FindProperty("m_RendererFeatures").ClearArray();
                var mode = rendererSettings.FindProperty("m_RenderingMode"); if (mode != null) mode.intValue = 0;
                rendererSettings.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(clone, folder + "/Renderer-" + i + ".asset"); renderers.GetArrayElementAtIndex(i).objectReferenceValue = clone;
            }
            settings.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.CreateAsset(pipeline, folder + "/Pipeline.asset");
            int quality = QualitySettings.GetQualityLevel();
            var previous = new RenderPipelineAsset[QualitySettings.names.Length];
            for (int i = 0; i < previous.Length; i++) { QualitySettings.SetQualityLevel(i); previous[i] = QualitySettings.renderPipeline; }
            QualitySettings.SetQualityLevel(quality);
            string company = PlayerSettings.companyName, product = PlayerSettings.productName, version = PlayerSettings.bundleVersion;
            int width = PlayerSettings.defaultScreenWidth, height = PlayerSettings.defaultScreenHeight;
            var full = PlayerSettings.fullScreenMode; bool background = PlayerSettings.runInBackground;
            bool resize = PlayerSettings.resizableWindow;
            try
            {
                PlayerSettings.companyName = "Kooker"; PlayerSettings.productName = npc ? "Kooker Starfall - Inhabitant decisions" : "Kooker Starfall - First inhabitant";
                PlayerSettings.bundleVersion = versionName; PlayerSettings.defaultScreenWidth = npc ? 1600 : 1280; PlayerSettings.defaultScreenHeight = npc ? 900 : 720;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.runInBackground = true; PlayerSettings.resizableWindow = true;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                for (int i = 0; i < previous.Length; i++) { QualitySettings.SetQualityLevel(i); QualitySettings.renderPipeline = pipeline; }
                QualitySettings.SetQualityLevel(quality);
                string scenePath = folder + (npc ? "/InhabitantDecisions.unity" : "/FirstInhabitant.unity"); EditorSceneManager.SaveScene(scene, scenePath); AssetDatabase.SaveAssets();
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath }, locationPathName = Path.GetFullPath(output),
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
                var evidence = new BuildEvidence { buildId = id, version = versionName, output = output, scene = scenePath,
                    status = report.summary.result.ToString(), errors = (int)report.summary.totalErrors, warnings = (int)report.summary.totalWarnings,
                    seconds = report.summary.totalTime.TotalSeconds, bytes = (long)report.summary.totalSize,
                    utc = DateTime.UtcNow.ToString("O"), scope = "Separate character courtyard; free Standard assets; scripted bounded roaming; no saved world or learning." };
                if (npc) evidence.scope = "Separate deterministic NPC perception, autonomous goals and validated actions; no LLM, learning, saved world or network.";
                File.WriteAllText(npc ? "evidence/local/npc/build.json" : "evidence/local/character/build.json", JsonUtility.ToJson(evidence, true));
                if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Character build failed.");
                Debug.Log("CHARACTER_PREVIEW_BUILD_SUCCEEDED " + output);
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = oldGraphics;
                for (int i = 0; i < previous.Length; i++) { QualitySettings.SetQualityLevel(i); QualitySettings.renderPipeline = previous[i]; }
                QualitySettings.SetQualityLevel(quality);
                PlayerSettings.companyName = company; PlayerSettings.productName = product; PlayerSettings.bundleVersion = version;
                PlayerSettings.defaultScreenWidth = width; PlayerSettings.defaultScreenHeight = height; PlayerSettings.fullScreenMode = full;
                PlayerSettings.runInBackground = background; PlayerSettings.resizableWindow = resize; AssetDatabase.SaveAssets();
            }
        }
        [Serializable] private sealed class ClipEvidence { public string body; public string[] selectedClips, allImportedClips; public bool humanoid; }
        [Serializable] private sealed class BuildEvidence { public string buildId, version, output, scene, status, utc, scope; public int errors, warnings; public long bytes; public double seconds; }
    }
}
