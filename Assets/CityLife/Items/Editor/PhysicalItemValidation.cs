using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityLife.Items.Editor
{
    /// <summary>
    /// Checks-only Unity Editor entry point for physical items validation.
    /// Runs pure deterministic ItemChecks without building a player or altering world scenes.
    /// </summary>
    public static class PhysicalItemValidation
    {
        [Serializable]
        private sealed class SummaryReport
        {
            public string status;
            public int count;
            public string utc;
            public string error;
        }

        public static void Run()
        {
            string outputDirectory = null;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int idx = Array.IndexOf(args, "-physicalItemEvidence");
                if (idx < 0 || idx + 1 >= args.Length || string.IsNullOrEmpty(args[idx + 1]))
                {
                    throw new ArgumentException("Missing required command-line argument: -physicalItemEvidence <output-directory>");
                }
                outputDirectory = args[idx + 1];

                Directory.CreateDirectory(outputDirectory);

                List<string> passed = ItemChecks.Run();

                File.WriteAllLines(Path.Combine(outputDirectory, "passed.txt"), passed);

                var summary = new SummaryReport
                {
                    status = "PASS",
                    count = passed.Count,
                    utc = DateTime.UtcNow.ToString("O")
                };
                File.WriteAllText(Path.Combine(outputDirectory, "summary.json"), JsonUtility.ToJson(summary, true));

                Debug.Log($"PHYSICAL_ITEM_CHECKS_PASS {passed.Count}");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"PHYSICAL_ITEM_CHECKS_FAIL: {ex.Message}\n{ex.StackTrace}");

                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDirectory);
                        File.WriteAllText(Path.Combine(outputDirectory, "failed.txt"), ex.ToString());
                        var failSummary = new SummaryReport
                        {
                            status = "FAIL",
                            count = 0,
                            utc = DateTime.UtcNow.ToString("O"),
                            error = ex.Message
                        };
                        File.WriteAllText(Path.Combine(outputDirectory, "summary.json"), JsonUtility.ToJson(failSummary, true));
                    }
                    catch
                    {
                        // Ignore secondary I/O failure during failure reporting
                    }
                }

                EditorApplication.Exit(1);
            }
        }
    }
}
