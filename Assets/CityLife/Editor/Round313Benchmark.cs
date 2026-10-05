using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEditor;
using Starfall.Food;
using CityLife.World;

namespace CityLife.World.Editor
{
    [Serializable]
    public sealed class BenchmarkResult
    {
        public string timestamp;
        public string stage;
        public int exploredCells;
        public int observedPlaces;
        public float validMinMs;
        public float validMaxMs;
        public float validMeanMs;
        public float validMedianMs;
        public float viewModelMinMs;
        public float viewModelMaxMs;
        public float viewModelMeanMs;
        public float viewModelMedianMs;
        public float normalTickMinMs;
        public float normalTickMaxMs;
        public float normalTickMeanMs;
        public float normalTickMedianMs;
    }

    public static class Round313Benchmark
    {
        [Serializable]
        private sealed class SaveEnvelope
        {
            public string schema;
            public string payload;
            public string sha256;
        }

        public static void RunBaseline()
        {
            Run("baseline");
        }

        public static void InspectHandGrip()
        {
            var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetImport.Body);
            if (bodyPrefab == null)
            {
                UnityEngine.Debug.LogError("Body prefab not found");
                EditorApplication.Exit(1);
                return;
            }

            var instance = UnityEngine.Object.Instantiate(bodyPrefab);
            var anim = instance.GetComponent<Animator>();
            Transform rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            Transform lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);

            UnityEngine.Debug.Log($"[HandGrip] RightHand name={rHand?.name}, localScale={rHand?.localScale}, lossyScale={rHand?.lossyScale}");
            UnityEngine.Debug.Log($"[HandGrip] LeftHand name={lHand?.name}, localScale={lHand?.localScale}, lossyScale={lHand?.lossyScale}");

            if (rHand != null)
            {
                for (int i = 0; i < rHand.childCount; i++)
                {
                    var child = rHand.GetChild(i);
                    UnityEngine.Debug.Log($"[HandGrip] RightHand Child {i}: {child.name} localPos={child.localPosition}");
                }
            }
            if (lHand != null)
            {
                for (int i = 0; i < lHand.childCount; i++)
                {
                    var child = lHand.GetChild(i);
                    UnityEngine.Debug.Log($"[HandGrip] LeftHand Child {i}: {child.name} localPos={child.localPosition}");
                }
            }

            UnityEngine.Object.DestroyImmediate(instance);
            EditorApplication.Exit(0);
        }

        public static void RunPostOptimization()
        {
            Run("post-optimization");
        }

        public static void Run(string stage)
        {
            string backupPath = @"C:\bots\reflection\artifacts\fishing-review-20260920\pre-round313-saves\snap-bb8053e617f45d196e8a8dbdd8a4670944cee6a9426c859642dd78140102ba05.json";
            if (!File.Exists(backupPath))
            {
                UnityEngine.Debug.LogError($"[Round313Benchmark] Backup snapshot not found at: {backupPath}");
                EditorApplication.Exit(1);
                return;
            }

            string json = File.ReadAllText(backupPath);
            var envelope = JsonUtility.FromJson<SaveEnvelope>(json);
            if (envelope == null || string.IsNullOrEmpty(envelope.payload))
            {
                UnityEngine.Debug.LogError("[Round313Benchmark] Failed to deserialize envelope");
                EditorApplication.Exit(1);
                return;
            }

            var matureState = JsonUtility.FromJson<FoodState>(envelope.payload);
            if (matureState == null)
            {
                UnityEngine.Debug.LogError("[Round313Benchmark] Failed to deserialize FoodState payload");
                EditorApplication.Exit(1);
                return;
            }

            int cells = matureState.exploredCells != null ? matureState.exploredCells.Count : 0;
            int places = matureState.observedPlaces != null ? matureState.observedPlaces.Count : 0;
            UnityEngine.Debug.Log($"[Round313Benchmark] Loaded mature save: {cells} explored cells, {places} observed places, world={matureState.world}, gen={matureState.generation}");

            // Warmup
            bool warmupValid = FoodModel.Valid(matureState, matureState.world, matureState.generation);
            if (!warmupValid)
            {
                UnityEngine.Debug.LogError("[Round313Benchmark] Warmup FoodModel.Valid returned FALSE on mature state!");
                EditorApplication.Exit(2);
                return;
            }

            // Benchmark FoodModel.Valid
            const int iterations = 10;
            var validTimes = new List<float>();
            var sw = new Stopwatch();

            for (int i = 0; i < iterations; i++)
            {
                sw.Restart();
                bool ok = FoodModel.Valid(matureState, matureState.world, matureState.generation);
                sw.Stop();
                if (!ok) throw new InvalidOperationException("FoodModel.Valid failed during benchmark loop");
                validTimes.Add((float)sw.Elapsed.TotalMilliseconds);
            }

            validTimes.Sort();
            float vMin = validTimes[0];
            float vMax = validTimes[validTimes.Count - 1];
            float vSum = 0f;
            for (int i = 0; i < validTimes.Count; i++) vSum += validTimes[i];
            float vMean = vSum / validTimes.Count;
            float vMed = validTimes[validTimes.Count / 2];

            // Benchmark StarfallMapViewModel.Update
            var vm = new StarfallMapViewModel(matureState.world, matureState.generation, matureState.actorId);
            var vmTimes = new List<float>();

            for (int i = 0; i < iterations; i++)
            {
                sw.Restart();
                bool ok = vm.Update(matureState, matureState.actorPosition, forceRevalidate: true);
                sw.Stop();
                if (!ok) throw new InvalidOperationException("ViewModel.Update failed during benchmark loop");
                vmTimes.Add((float)sw.Elapsed.TotalMilliseconds);
            }

            vmTimes.Sort();
            float vmMin = vmTimes[0];
            float vmMax = vmTimes[vmTimes.Count - 1];
            float vmSum = 0f;
            for (int i = 0; i < vmTimes.Count; i++) vmSum += vmTimes[i];
            float vmMean = vmSum / vmTimes.Count;
            float vmMed = vmTimes[vmTimes.Count / 2];

            // Benchmark StarfallMapViewModel normal play tick (forceRevalidate: false)
            var normalTimes = new List<float>();
            for (int i = 0; i < iterations; i++)
            {
                sw.Restart();
                bool ok = vm.Update(matureState, matureState.actorPosition, forceRevalidate: false);
                sw.Stop();
                if (!ok) throw new InvalidOperationException("ViewModel.Update normal tick failed during benchmark loop");
                normalTimes.Add((float)sw.Elapsed.TotalMilliseconds);
            }

            normalTimes.Sort();
            float nMin = normalTimes[0];
            float nMax = normalTimes[normalTimes.Count - 1];
            float nSum = 0f;
            for (int i = 0; i < normalTimes.Count; i++) nSum += normalTimes[i];
            float nMean = nSum / normalTimes.Count;
            float nMed = normalTimes[normalTimes.Count / 2];

            var result = new BenchmarkResult
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                stage = stage,
                exploredCells = cells,
                observedPlaces = places,
                validMinMs = vMin,
                validMaxMs = vMax,
                validMeanMs = vMean,
                validMedianMs = vMed,
                viewModelMinMs = vmMin,
                viewModelMaxMs = vmMax,
                viewModelMeanMs = vmMean,
                viewModelMedianMs = vmMed,
                normalTickMinMs = nMin,
                normalTickMaxMs = nMax,
                normalTickMeanMs = nMean,
                normalTickMedianMs = nMed
            };

            string outDir = Path.Combine(Application.dataPath, "..", "evidence", "local", "benchmarks");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"{stage}-mature-benchmark.json");
            File.WriteAllText(outPath, JsonUtility.ToJson(result, true));

            UnityEngine.Debug.Log($"[Round313Benchmark] {stage.ToUpperInvariant()} COMPLETE:\n" +
                $"FoodModel.Valid ({cells} cells, {places} places): min={vMin:F2}ms, max={vMax:F2}ms, mean={vMean:F2}ms, median={vMed:F2}ms\n" +
                $"StarfallMapViewModel.Update: min={vmMin:F2}ms, max={vmMax:F2}ms, mean={vmMean:F2}ms, median={vmMed:F2}ms\n" +
                $"Recorded to: {outPath}");

            EditorApplication.Exit(0);
        }
    }
}
