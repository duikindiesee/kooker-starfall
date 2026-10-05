using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace CityLife.World.Editor
{
    public static class FishingPlayModeProbe
    {
        public static void AuthorUpdatedScene()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-fishingScene");
            if (index < 0 || index + 1 >= args.Length || !args[index + 1].StartsWith("Assets/CityLife/GeneratedPreview-", StringComparison.Ordinal))
                throw new InvalidOperationException("Explicit source scene required.");
            EditorSceneManager.OpenScene(args[index + 1], OpenSceneMode.Single);
            var rod = UnityEngine.Object.FindFirstObjectByType<FishingRodItem>();
            rod.transform.position = FishingRodItem.InitialWorldPosition;
            UnityEngine.Physics.SyncTransforms();
            var nav = UnityEngine.Object.FindFirstObjectByType<NpcTerrainNavigation>();
            var brain = UnityEngine.Object.FindFirstObjectByType<NpcAutonomy>();
            var supplies = brain.GetComponent<FishingCampSupplies>() ?? brain.gameObject.AddComponent<FishingCampSupplies>();
            supplies.Brain = brain;
            var path = nav.Plan(brain.transform.position, rod.transform.position);
            if (!nav.Walkable(rod.transform.position, out _) || path == null || path.Count == 0 ||
                UnityEngine.Vector3.Distance(System.Linq.Enumerable.Last(path), rod.transform.position) > 1.25f)
                throw new InvalidOperationException("Authored starter rod must have a complete pickup route.");
            const string folder = "Assets/CityLife/GeneratedPreview-CodexFishing";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/CityLife", "GeneratedPreview-CodexFishing");
            if (!EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), folder + "/CosmicPreview.unity", true))
                throw new InvalidOperationException("Could not save updated authored scene copy.");
            UnityEngine.Debug.Log("STARTER_ROD_AUTHORING_PASSED: reachable shore, production InitialWorldPosition, original scene preserved.");
            EditorApplication.Exit(0);
        }

        public static void InspectWorld()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-fishingScene");
            EditorSceneManager.OpenScene(args[index + 1], OpenSceneMode.Single);
            UnityEngine.Physics.SyncTransforms();
            var brain = UnityEngine.Object.FindFirstObjectByType<NpcAutonomy>();
            var nav = UnityEngine.Object.FindFirstObjectByType<NpcTerrainNavigation>();
            var rod = UnityEngine.Object.FindFirstObjectByType<FishingRodItem>();
            foreach (var point in new[] { rod.transform.position, new UnityEngine.Vector3(40, 0, -30), new UnityEngine.Vector3(34, 0, -26), new UnityEngine.Vector3(25, 0, -32) })
            {
                bool walkable = nav.Walkable(point, out var floor);
                var path = nav.Plan(brain.transform.position, point);
                var last = path != null && path.Count > 0 ? System.Linq.Enumerable.Last(path) : brain.transform.position;
                UnityEngine.Debug.Log($"FISHING_WORLD_ROUTE origin={brain.transform.position} target={point} floor={floor} walkable={walkable} depth={nav.WaterDepth(point)} steps={path?.Count ?? 0} last={last} distance={UnityEngine.Vector3.Distance(last, point)}");
            }
            EditorApplication.Exit(0);
        }

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-fishingScene");
            if (!UnityEngine.Application.isBatchMode || index < 0 || index + 1 >= args.Length ||
                !args[index + 1].StartsWith("Assets/CityLife/GeneratedPreview-", StringComparison.Ordinal) ||
                !File.Exists(args[index + 1])) throw new InvalidOperationException("An explicit generated scene is required.");
            EditorSceneManager.OpenScene(args[index + 1], OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
    }
}
