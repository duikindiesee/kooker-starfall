using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World.Editor
{
    // Explicit development runtime, never loads or launches the blocked standalone executable.
    public static class StarfallMemoryPlayMode
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !NpcPreviewSmoke.Requested)
                throw new InvalidOperationException("Dedicated batch Editor acceptance required.");
            var identity = NpcPreviewSmoke.RuntimeBuildId;
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-npcEditorScene");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Exact scene required.");
            string scene = args[index + 1].Replace('\\', '/');
            if (!scene.StartsWith("Assets/CityLife/GeneratedPreview-Character-", StringComparison.Ordinal) ||
                !scene.EndsWith("/InhabitantDecisions.unity", StringComparison.Ordinal) || scene.Contains("..") || !File.Exists(scene))
                throw new ArgumentException("Preserved generated courtyard scene required.");
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(Path.GetDirectoryName(scene).Replace('\\', '/') + "/Pipeline.asset");
            if (!pipeline) throw new InvalidOperationException("Exact scene pipeline missing.");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int quality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i); QualitySettings.renderPipeline = pipeline; }
            QualitySettings.SetQualityLevel(quality);
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            Debug.Log("STARFALL_EDITOR_PLAY_MODE " + identity + " scene=" + scene + "; not standalone acceptance");
            // Let queued Editor startup/indexing and scene imports finish before runtime error capture starts.
            double settledAt = EditorApplication.timeSinceStartup + 5;
            void EnterWhenSettled()
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) settledAt = EditorApplication.timeSinceStartup + 5;
                if (EditorApplication.timeSinceStartup < settledAt) return;
                EditorApplication.update -= EnterWhenSettled;
                EditorApplication.isPlaying = true;
            }
            EditorApplication.update += EnterWhenSettled;
        }
    }
}
