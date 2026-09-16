using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityLife.Items.Editor
{
    /// <summary>
    /// Checks-only Unity Editor entry point for physical items validation.
    /// Manages an explicit Play Mode lifecycle via a guarded EditorApplication.update
    /// and playModeStateChanged state machine, completely independent of delayCall.
    /// Executes exactly once, logs and flushes stage transitions to stages.txt,
    /// includes an internal 120s progress watchdog, and writes passed.txt or failed.txt
    /// with stage and partial counts before cleanly terminating the Editor process.
    /// </summary>
    [InitializeOnLoad]
    public static class PhysicalItemValidation
    {
        private const string ActiveKey = "PhysicalItemValidation.Active";
        private const string StageKey = "PhysicalItemValidation.Stage";
        private const string OutputDirKey = "PhysicalItemValidation.OutputDir";
        private const string ExitCodeKey = "PhysicalItemValidation.ExitCode";
        private const string ComponentCountKey = "PhysicalItemValidation.ComponentCount";
        private const string RuntimeCountKey = "PhysicalItemValidation.RuntimeCount";
        private const string WatchdogDeadlineKey = "PhysicalItemValidation.WatchdogDeadline";
        private const string ExecutedKey = "PhysicalItemValidation.Executed";

        [Serializable]
        private sealed class SummaryReport
        {
            public string status;
            public int count;
            public int componentCount;
            public int runtimeCount;
            public string utc;
            public string error;
        }

        static PhysicalItemValidation()
        {
            // Only hook events if an explicit validation run is active across domain reload
            if (SessionState.GetBool(ActiveKey, false))
            {
                HookEvents();
            }
        }

        private static void HookEvents()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void UnhookEvents()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private static void RecordStage(string stageName)
        {
            try
            {
                SessionState.SetString(StageKey, stageName);
                Debug.Log($"[PhysicalItemValidation] Stage: {stageName}");

                string outDir = SessionState.GetString(OutputDirKey, null);
                if (!string.IsNullOrEmpty(outDir))
                {
                    Directory.CreateDirectory(outDir);
                    string line = $"{DateTime.UtcNow:O} {stageName}{Environment.NewLine}";
                    File.AppendAllText(Path.Combine(outDir, "stages.txt"), line);
                }
            }
            catch
            {
                // Never let logging/stage recording interrupt execution
            }
        }

        private static double GetWatchdogDeadline()
        {
            string val = SessionState.GetString(WatchdogDeadlineKey, "");
            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double deadline))
            {
                return deadline;
            }
            return 0;
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
                outputDirectory = Path.GetFullPath(args[idx + 1]);

                Directory.CreateDirectory(outputDirectory);

                // Initialize session state flags
                SessionState.SetBool(ActiveKey, true);
                SessionState.SetBool(ExecutedKey, false);
                SessionState.SetString(OutputDirKey, outputDirectory);
                SessionState.SetInt(ExitCodeKey, 1);
                SessionState.SetInt(ComponentCountKey, 0);
                SessionState.SetInt(RuntimeCountKey, 0);

                // Set 120-second internal watchdog deadline
                double deadline = EditorApplication.timeSinceStartup + 120.0;
                SessionState.SetString(WatchdogDeadlineKey, deadline.ToString("R", CultureInfo.InvariantCulture));

                RecordStage("launch");

                HookEvents();

                // Create empty unsaved fixture scene (avoids world bootstrap, terrain generation, or LLM services)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                RecordStage("entering-play");

                if (EditorApplication.isPlaying)
                {
                    // Tolerates configurable enter play mode without domain/scene reload if already playing
                    RecordStage("entered-play");
                    SessionState.SetBool(ExecutedKey, true);
                    ExecuteSuiteInPlayMode();
                }
                else
                {
                    EditorApplication.EnterPlaymode();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"PHYSICAL_ITEM_VALIDATION_LAUNCH_FAIL: {ex.Message}\n{ex.StackTrace}");
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDirectory);
                        File.WriteAllText(Path.Combine(outputDirectory, "failed.txt"),
                            $"Launch failure at stage 'launch':\n{ex}");
                        var failSummary = new SummaryReport
                        {
                            status = "FAIL",
                            count = 0,
                            componentCount = 0,
                            runtimeCount = 0,
                            utc = DateTime.UtcNow.ToString("O"),
                            error = $"[launch] {ex.Message}"
                        };
                        File.WriteAllText(Path.Combine(outputDirectory, "summary.json"), JsonUtility.ToJson(failSummary, true));
                    }
                    catch { }
                }

                RecordStage("failed");
                CleanupAndExit(1);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;

            try
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    if (!SessionState.GetBool(ExecutedKey, false))
                    {
                        SessionState.SetBool(ExecutedKey, true);
                        RecordStage("entered-play");
                        ExecuteSuiteInPlayMode();
                    }
                }
                else if (state == PlayModeStateChange.EnteredEditMode)
                {
                    string stage = SessionState.GetString(StageKey, "");
                    if (stage == "leaving-play")
                    {
                        RecordStage("exited");
                        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
                        CleanupAndExit(exitCode);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhysicalItemValidation] StateChanged error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void OnEditorUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;

            try
            {
                // 1. Watchdog evaluation
                double deadline = GetWatchdogDeadline();
                if (deadline > 0 && EditorApplication.timeSinceStartup > deadline)
                {
                    FailWithWatchdogTimeout();
                    return;
                }

                // 2. Exactly-once execution trigger fallback if EnteredPlayMode event was missed
                if (EditorApplication.isPlaying)
                {
                    string stage = SessionState.GetString(StageKey, "");
                    if (stage == "entering-play" || stage == "entered-play")
                    {
                        if (!SessionState.GetBool(ExecutedKey, false))
                        {
                            SessionState.SetBool(ExecutedKey, true);
                            RecordStage("entered-play");
                            ExecuteSuiteInPlayMode();
                        }
                    }
                }
                else
                {
                    // 3. Exit trigger fallback if EnteredEditMode event was missed
                    string stage = SessionState.GetString(StageKey, "");
                    if (stage == "leaving-play")
                    {
                        RecordStage("exited");
                        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
                        CleanupAndExit(exitCode);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhysicalItemValidation] EditorUpdate error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void ExecuteSuiteInPlayMode()
        {
            string outputDirectory = SessionState.GetString(OutputDirKey, null);
            int componentCount = 0;
            int runtimeCount = 0;
            var passed = new List<string>();

            try
            {
                if (string.IsNullOrEmpty(outputDirectory))
                {
                    throw new InvalidOperationException("Validation output directory missing from SessionState.");
                }

                Directory.CreateDirectory(outputDirectory);

                // 1. Component suite (58 in-memory assertions)
                RecordStage("component-start");
                List<string> componentPassed = ItemChecks.Run();
                componentCount = componentPassed.Count;
                passed.AddRange(componentPassed);
                SessionState.SetInt(ComponentCountKey, componentCount);
                RecordStage("component-done");

                // 2. Runtime suite (46 isolated scene physics assertions)
                RecordStage("runtime-start");
                List<string> runtimePassed = PhysicalItemRuntimeChecks.Run();
                runtimeCount = runtimePassed.Count;
                passed.AddRange(runtimePassed);
                SessionState.SetInt(RuntimeCountKey, runtimeCount);
                RecordStage("runtime-done");

                // 3. Success evidence
                File.WriteAllLines(Path.Combine(outputDirectory, "passed.txt"), passed);

                var summary = new SummaryReport
                {
                    status = "PASS",
                    count = passed.Count,
                    componentCount = componentCount,
                    runtimeCount = runtimeCount,
                    utc = DateTime.UtcNow.ToString("O")
                };
                File.WriteAllText(Path.Combine(outputDirectory, "summary.json"), JsonUtility.ToJson(summary, true));

                Debug.Log($"PHYSICAL_ITEM_CHECKS_PASS {passed.Count}");
                SessionState.SetInt(ExitCodeKey, 0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"PHYSICAL_ITEM_CHECKS_FAIL: {ex.Message}\n{ex.StackTrace}");
                SessionState.SetInt(ExitCodeKey, 1);
                string currentStage = SessionState.GetString(StageKey, "unknown");

                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDirectory);
                        File.WriteAllText(Path.Combine(outputDirectory, "failed.txt"),
                            $"Stage: {currentStage}\nComponentCount: {componentCount}\nRuntimeCount: {runtimeCount}\nException:\n{ex}");
                        if (passed.Count > 0)
                        {
                            File.WriteAllLines(Path.Combine(outputDirectory, "passed.txt"), passed);
                        }

                        var failSummary = new SummaryReport
                        {
                            status = "FAIL",
                            count = passed.Count,
                            componentCount = componentCount,
                            runtimeCount = runtimeCount,
                            utc = DateTime.UtcNow.ToString("O"),
                            error = $"[{currentStage}] {ex.Message}"
                        };
                        File.WriteAllText(Path.Combine(outputDirectory, "summary.json"), JsonUtility.ToJson(failSummary, true));
                    }
                    catch { }
                }
            }
            finally
            {
                RecordStage("leaving-play");
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                }
                else
                {
                    RecordStage("exited");
                    int exitCode = SessionState.GetInt(ExitCodeKey, 1);
                    CleanupAndExit(exitCode);
                }
            }
        }

        private static void FailWithWatchdogTimeout()
        {
            string stage = SessionState.GetString(StageKey, "unknown");
            string outDir = SessionState.GetString(OutputDirKey, null);
            int compCount = SessionState.GetInt(ComponentCountKey, 0);
            int runCount = SessionState.GetInt(RuntimeCountKey, 0);

            Debug.LogError($"[PhysicalItemValidation] WATCHDOG TIMEOUT after 120s stalled at stage '{stage}'. Terminating.");

            if (!string.IsNullOrEmpty(outDir))
            {
                try
                {
                    Directory.CreateDirectory(outDir);
                    File.WriteAllText(Path.Combine(outDir, "failed.txt"),
                        $"Watchdog timeout after 120 seconds. Stalled at stage: {stage}\nComponentCount: {compCount}\nRuntimeCount: {runCount}");
                    var timeoutSummary = new SummaryReport
                    {
                        status = "FAIL",
                        count = compCount,
                        componentCount = compCount,
                        runtimeCount = runCount,
                        utc = DateTime.UtcNow.ToString("O"),
                        error = $"Watchdog timeout at stage: {stage}"
                    };
                    File.WriteAllText(Path.Combine(outDir, "summary.json"), JsonUtility.ToJson(timeoutSummary, true));
                }
                catch { }
            }

            RecordStage("failed");
            CleanupAndExit(1);
        }

        private static void CleanupAndExit(int exitCode)
        {
            try
            {
                UnhookEvents();
                SessionState.SetBool(ActiveKey, false);
            }
            catch { }

            EditorApplication.Exit(exitCode);
        }
    }
}
