using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CityLife.World.Editor
{
    public static class CityLifeBuild
    {
        [MenuItem("CityLife/Prepare island scene")]
        public static void Prepare()
        {
            PlayerSettings.companyName = "CityLife"; PlayerSettings.productName = "CityLife Island";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true; PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (pipeline == null) throw new InvalidOperationException("Bundled URP pipeline asset is missing.");
            pipeline.renderScale = 1; pipeline.msaaSampleCount = 2; pipeline.shadowDistance = 200;
            pipeline.supportsHDR = true;
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            // In URP17.6 even inactive features are constructed, but their resources may be
            // stripped from players. Remove unused template SSAO entirely to avoid a black player.
            rendererData.rendererFeatures.Clear();
            var serializedRenderer = new SerializedObject(rendererData);
            serializedRenderer.FindProperty("m_RenderingMode").intValue = 0;
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rendererData);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i); QualitySettings.renderPipeline = pipeline; }
            QualitySettings.vSyncCount = 0;
            EditorUtility.SetDirty(pipeline);
            string scenePath = "Assets/CityLife/Scenes/Island.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("CityLife Island").AddComponent<IslandBootstrap>();
            var cameraObject = new GameObject("Explorer Camera"); cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>(); cameraObject.AddComponent<AudioListener>();
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            // Runtime-created materials must keep their shaders in standalone builds.
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
            foreach (string path in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/CityLife/Shaders" }))
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(path));
                bool exists = false;
                for (int i = 0; i < shaders.arraySize; i++) if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) exists = true;
                if (!exists) { shaders.InsertArrayElementAtIndex(shaders.arraySize); shaders.GetArrayElementAtIndex(shaders.arraySize - 1).objectReferenceValue = shader; }
            }
            graphics.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
            Debug.Log("CITYLIFE_SCENE_PREPARED " + scenePath);
        }
        public static void BuildWindows()
        {
            Prepare(); IslandValidation.Run();
            string buildName = "Windows";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-citylifeBuildName") buildName = args[i + 1];
            if (!System.Text.RegularExpressions.Regex.IsMatch(buildName, @"\A[A-Za-z0-9_-]+\z"))
                throw new ArgumentException("Build name must contain only letters, digits, underscores or hyphens.");
            string relativeOutput = "Builds/" + buildName + "/CityLife.exe";
            string output = Path.GetFullPath(relativeOutput); Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/CityLife/Scenes/Island.unity" }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            Directory.CreateDirectory("evidence/local");
            File.WriteAllText("evidence/local/build-result.json", JsonUtility.ToJson(new BuildEvidence {
                result = report.summary.result.ToString(), output = relativeOutput, sizeBytes = (long)report.summary.totalSize,
                seconds = report.summary.totalTime.TotalSeconds, errors = (int)report.summary.totalErrors,
                warnings = (int)report.summary.totalWarnings, editor = Application.unityVersion,
                utc = DateTime.UtcNow.ToString("O")
            }, true));
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Windows build failed: " + report.summary.result);
            Debug.Log("CITYLIFE_BUILD_SUCCEEDED " + output);
        }
        [Serializable] private sealed class BuildEvidence { public string result, output, editor, utc; public long sizeBytes; public double seconds; public int errors, warnings; }
    }
}
