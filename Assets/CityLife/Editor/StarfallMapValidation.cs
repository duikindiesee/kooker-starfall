using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Starfall.Food;

namespace CityLife.World.Editor
{
    public static class StarfallMapValidation
    {
        [Serializable]
        private sealed class Report
        {
            public string schema = "starfall.spatial-map-validation.v1";
            public string status = "PENDING";
            public string evidenceFolder;
            public int foodChecksCount;
            public int mapChecksCount;
            public int totalPassed;
            public string failureException;
            public string failureMessage;
            public List<string> passedChecks = new List<string>();
        }

        // Minimal Editor runner for spatial memory view-model and place ledger checks.
        // Requires -starfallMapEvidence <new_absolute_path> argument.
        // Runs existing FoodChecks and new StarfallMapChecks, writing structured evidence.
        // On failure, records the failure exception and partial completed suite counts
        // before propagating the exception for consistent caller -quit semantics.
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Argument(string name)
            {
                int i = Array.IndexOf(args, name);
                if (i < 0 || i + 1 >= args.Length)
                    throw new ArgumentException("Missing required command argument: " + name);
                return args[i + 1];
            }

            string output = Argument("-starfallMapEvidence");
            if (!Path.IsPathRooted(output) || Directory.Exists(output))
                throw new ArgumentException("Evidence folder must be a new absolute path: " + output);

            Directory.CreateDirectory(output);
            var report = new Report { evidenceFolder = output };

            try
            {
                // 1. Run existing FoodChecks to verify place ledger foundation remains intact
                string foodFolder = Path.Combine(output, "food-checks");
                var foodPassed = FoodChecks.Run(foodFolder);
                report.foodChecksCount = foodPassed.Count;
                report.passedChecks.AddRange(foodPassed);

                // 2. Run focused StarfallMapChecks for view-model and presentation properties
                string mapFolder = Path.Combine(output, "map-checks");
                var mapPassed = StarfallMapChecks.Run(mapFolder);
                report.mapChecksCount = mapPassed.Count;
                report.passedChecks.AddRange(mapPassed);

                report.totalPassed = report.passedChecks.Count;
                report.status = "PASS";

                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Path.Combine(output, "validation-report.json"), json);
                File.WriteAllLines(Path.Combine(output, "passed.txt"), report.passedChecks);

                Debug.Log($"STARFALL_MAP_VALIDATION_PASS foodChecks={report.foodChecksCount} mapChecks={report.mapChecksCount} total={report.totalPassed} output={output}");
            }
            catch (Exception ex)
            {
                report.totalPassed = report.passedChecks.Count;
                report.status = "FAILED";
                report.failureException = ex.ToString();
                report.failureMessage = ex.Message;

                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Path.Combine(output, "validation-report.json"), json);
                File.WriteAllLines(Path.Combine(output, "passed.txt"), report.passedChecks);

                Debug.LogError($"STARFALL_MAP_VALIDATION_FAILED: {ex.Message} partialFoodChecks={report.foodChecksCount} partialMapChecks={report.mapChecksCount} totalPassed={report.totalPassed} output={output}");
                throw;
            }
        }
    }
}
