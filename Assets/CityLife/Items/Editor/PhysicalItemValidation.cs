using System;
using System.Collections;
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
        private const string IntegrationCountKey = "PhysicalItemValidation.IntegrationCount";
        private const string BasketCountKey = "PhysicalItemValidation.BasketCount";
        private const string WatchdogDeadlineKey = "PhysicalItemValidation.WatchdogDeadline";
        private const string ExecutedKey = "PhysicalItemValidation.Executed";

        private static Stack<IEnumerator> asyncExecutionStack;
        private static List<string> cumulativePassed;
        private static int lastAsyncFrame = -1;
        private static Action asyncCleanupAction;

        [Serializable]
        private sealed class SummaryReport
        {
            public string status;
            public int count;
            public int componentCount;
            public int runtimeCount;
            public int integrationCount;
            public int basketCount;
            public string utc;
            public string error;
        }

        private static void DisposeAsyncStack()
        {
            if (asyncExecutionStack != null)
            {
                while (asyncExecutionStack.Count > 0)
                {
                    var iter = asyncExecutionStack.Pop();
                    if (iter is IDisposable disp)
                    {
                        try { disp.Dispose(); } catch { }
                    }
                }
                asyncExecutionStack = null;
            }
        }

        private static void SafeCleanupAsyncExecution()
        {
            try
            {
                if (asyncCleanupAction != null)
                {
                    var cleanup = asyncCleanupAction;
                    asyncCleanupAction = null;
                    cleanup();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhysicalItemValidation] Exception during async cleanup callback: {ex.Message}");
            }
            finally
            {
                DisposeAsyncStack();
                try
                {
                    Time.timeScale = 1.0f;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                catch { }
            }
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
                SessionState.SetInt(IntegrationCountKey, 0);
                SessionState.SetInt(BasketCountKey, 0);

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
                        SafeCleanupAsyncExecution();
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

                // 2. Drive active async suite over genuine PlayMode frame updates
                if (asyncExecutionStack != null && asyncExecutionStack.Count > 0 && EditorApplication.isPlaying)
                {
                    int currentFrame = Time.frameCount;
                    if (currentFrame > lastAsyncFrame)
                    {
                        lastAsyncFrame = currentFrame;
                        try
                        {
                            while (asyncExecutionStack.Count > 0)
                            {
                                var currentIter = asyncExecutionStack.Peek();
                                bool hasMore = currentIter.MoveNext();
                                if (!hasMore)
                                {
                                    if (currentIter is IDisposable disp)
                                    {
                                        try { disp.Dispose(); } catch { }
                                    }
                                    asyncExecutionStack.Pop();
                                    if (asyncExecutionStack.Count == 0)
                                    {
                                        HandleExecutionSuccess(
                                            SessionState.GetInt(ComponentCountKey, 0),
                                            SessionState.GetInt(RuntimeCountKey, 0),
                                            cumulativePassed ?? new List<string>());
                                        return;
                                    }
                                    continue;
                                }

                                object yielded = currentIter.Current;
                                if (yielded == null)
                                {
                                    // Exactly null frame wait: await real next PlayMode frame
                                    break;
                                }
                                else if (yielded is IEnumerator nested)
                                {
                                    // Drive nested enumerator (e.g. Tap)
                                    asyncExecutionStack.Push(nested);
                                    continue;
                                }
                                else
                                {
                                    throw new InvalidOperationException(
                                        $"Unsupported yield type in async suite: {yielded.GetType().FullName}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            HandleExecutionFailure(ex,
                                SessionState.GetInt(ComponentCountKey, 0),
                                SessionState.GetInt(RuntimeCountKey, 0),
                                cumulativePassed ?? new List<string>());
                            return;
                        }
                    }
                }

                // 3. Exactly-once execution trigger fallback if EnteredPlayMode event was missed
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
                    // 4. Exit trigger fallback if EnteredEditMode event was missed
                    string stage = SessionState.GetString(StageKey, "");
                    if (stage == "leaving-play")
                    {
                        RecordStage("exited");
                        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
                        SafeCleanupAsyncExecution();
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
            cumulativePassed = new List<string>();

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
                cumulativePassed.AddRange(componentPassed);
                SessionState.SetInt(ComponentCountKey, componentCount);
                RecordStage("component-done");

                // 2. Runtime suite (46 isolated scene physics assertions)
                RecordStage("runtime-start");
                List<string> runtimePassed = PhysicalItemRuntimeChecks.Run();
                runtimeCount = runtimePassed.Count;
                cumulativePassed.AddRange(runtimePassed);
                SessionState.SetInt(RuntimeCountKey, runtimeCount);
                RecordStage("runtime-done");

                // 3. Additive asynchronous Basket Player Integration Suite
                RecordStage("basket-player-start");
                var runner = BasketPlayerIntegrationChecks.RunAsync(
                    checkRaw: (condition, name, diagnostic) =>
                    {
                        if (!condition)
                        {
                            string msg = !string.IsNullOrEmpty(diagnostic)
                                ? $"BASKET PLAYER CHECK FAILED: {name} ({diagnostic})"
                                : $"BASKET PLAYER CHECK FAILED: {name}";
                            throw new InvalidOperationException(msg);
                        }
                        cumulativePassed.Add(name);
                    },
                    passed: cumulativePassed,
                    setCleanup: cleanup => { asyncCleanupAction = cleanup; }
                );
                asyncExecutionStack = new Stack<IEnumerator>();
                asyncExecutionStack.Push(runner);
                lastAsyncFrame = Time.frameCount;
            }
            catch (Exception ex)
            {
                HandleExecutionFailure(ex, componentCount, runtimeCount, cumulativePassed);
            }
        }

        private static void HandleExecutionSuccess(int componentCount, int runtimeCount, List<string> passed)
        {
            RecordStage("basket-player-done");
            string outputDirectory = SessionState.GetString(OutputDirKey, null);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                var ex = new InvalidOperationException("Validation output directory missing from SessionState.");
                HandleExecutionFailure(ex, componentCount, runtimeCount, passed);
                return;
            }

            int integrationCount = Math.Max(0, (passed != null ? passed.Count : 0) - (componentCount + runtimeCount));
            int totalCount = passed != null ? passed.Count : (componentCount + runtimeCount + integrationCount);

            SessionState.SetInt(IntegrationCountKey, integrationCount);
            SessionState.SetInt(BasketCountKey, integrationCount);

            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllLines(Path.Combine(outputDirectory, "passed.txt"), passed ?? new List<string>());
                var summary = new SummaryReport
                {
                    status = "PASS",
                    count = totalCount,
                    componentCount = componentCount,
                    runtimeCount = runtimeCount,
                    integrationCount = integrationCount,
                    basketCount = integrationCount,
                    utc = DateTime.UtcNow.ToString("O")
                };
                File.WriteAllText(Path.Combine(outputDirectory, "summary.json"), JsonUtility.ToJson(summary, true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhysicalItemValidation] Failed to write success evidence to '{outputDirectory}': {ex.Message}\n{ex.StackTrace}");
                HandleExecutionFailure(ex, componentCount, runtimeCount, passed);
                return;
            }

            Debug.Log($"PHYSICAL_ITEM_CHECKS_PASS {totalCount}");
            SessionState.SetInt(ExitCodeKey, 0);
            ExitPlayMode();
        }

        private static void HandleExecutionFailure(Exception ex, int componentCount, int runtimeCount, List<string> passed)
        {
            Debug.LogError($"PHYSICAL_ITEM_CHECKS_FAIL: {ex.Message}\n{ex.StackTrace}");
            SessionState.SetInt(ExitCodeKey, 1);
            string currentStage = SessionState.GetString(StageKey, "unknown");
            string outputDirectory = SessionState.GetString(OutputDirKey, null);

            int integrationCount = Math.Max(0, (passed != null ? passed.Count : 0) - (componentCount + runtimeCount));
            int totalCount = passed != null ? passed.Count : (componentCount + runtimeCount + integrationCount);

            SessionState.SetInt(IntegrationCountKey, integrationCount);
            SessionState.SetInt(BasketCountKey, integrationCount);

            if (!string.IsNullOrEmpty(outputDirectory))
            {
                try
                {
                    Directory.CreateDirectory(outputDirectory);
                    File.WriteAllText(Path.Combine(outputDirectory, "failed.txt"),
                        $"Stage: {currentStage}\nComponentCount: {componentCount}\nRuntimeCount: {runtimeCount}\nIntegrationCount: {integrationCount}\nException:\n{ex}");
                    if (passed != null && passed.Count > 0)
                    {
                        File.WriteAllLines(Path.Combine(outputDirectory, "passed.txt"), passed);
                    }

                    var failSummary = new SummaryReport
                    {
                        status = "FAIL",
                        count = totalCount,
                        componentCount = componentCount,
                        runtimeCount = runtimeCount,
                        integrationCount = integrationCount,
                        basketCount = integrationCount,
                        utc = DateTime.UtcNow.ToString("O"),
                        error = $"[{currentStage}] {ex.Message}"
                    };
                    File.WriteAllText(Path.Combine(outputDirectory, "summary.json"), JsonUtility.ToJson(failSummary, true));
                }
                catch { }
            }

            SafeCleanupAsyncExecution();
            ExitPlayMode();
        }

        private static void ExitPlayMode()
        {
            RecordStage("leaving-play");
            SafeCleanupAsyncExecution();
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

        private static void FailWithWatchdogTimeout()
        {
            string stage = SessionState.GetString(StageKey, "unknown");
            string outDir = SessionState.GetString(OutputDirKey, null);
            int compCount = SessionState.GetInt(ComponentCountKey, 0);
            int runCount = SessionState.GetInt(RuntimeCountKey, 0);
            int integrationCount = Math.Max(0, (cumulativePassed != null ? cumulativePassed.Count : 0) - (compCount + runCount));
            int totalCount = cumulativePassed != null ? cumulativePassed.Count : (compCount + runCount + integrationCount);

            SessionState.SetInt(IntegrationCountKey, integrationCount);
            SessionState.SetInt(BasketCountKey, integrationCount);

            Debug.LogError($"[PhysicalItemValidation] WATCHDOG TIMEOUT after 120s stalled at stage '{stage}'. Terminating.");

            if (!string.IsNullOrEmpty(outDir))
            {
                try
                {
                    Directory.CreateDirectory(outDir);
                    File.WriteAllText(Path.Combine(outDir, "failed.txt"),
                        $"Watchdog timeout after 120 seconds. Stalled at stage: {stage}\nComponentCount: {compCount}\nRuntimeCount: {runCount}\nIntegrationCount: {integrationCount}");
                    if (cumulativePassed != null && cumulativePassed.Count > 0)
                    {
                        File.WriteAllLines(Path.Combine(outDir, "passed.txt"), cumulativePassed);
                    }
                    var timeoutSummary = new SummaryReport
                    {
                        status = "FAIL",
                        count = totalCount,
                        componentCount = compCount,
                        runtimeCount = runCount,
                        integrationCount = integrationCount,
                        basketCount = integrationCount,
                        utc = DateTime.UtcNow.ToString("O"),
                        error = $"Watchdog timeout at stage: {stage}"
                    };
                    File.WriteAllText(Path.Combine(outDir, "summary.json"), JsonUtility.ToJson(timeoutSummary, true));
                }
                catch { }
            }

            RecordStage("failed");
            SafeCleanupAsyncExecution();
            CleanupAndExit(1);
        }

        private static void CleanupAndExit(int exitCode)
        {
            try
            {
                SafeCleanupAsyncExecution();
                UnhookEvents();
                SessionState.SetBool(ActiveKey, false);
            }
            catch { }

            EditorApplication.Exit(exitCode);
        }
    }
}
