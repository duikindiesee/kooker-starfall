using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Starfall.Food;

namespace CityLife.World
{
    public sealed class IntegratedAcceptance : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public NpcPlayerControls Controls;
        public IntegratedEnvironment Environment;
        public IntegratedFoodRuntime Food;
        private string directory;
        private readonly List<string> errors = new List<string>();
        private readonly Report report = new Report();
        private Keyboard keyboard;
        private Mouse mouse;
        [Serializable] public sealed class Check { public string name, evidence; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string status = "RUNNING", version, worldId, inputScope = "Actual compiled-player Input System devices; separate native mouse/window acceptance required.";
            public int deliveries, fullCycleDeliveries, memoryEvents; public List<Check> checks = new List<Check>(); public List<string> captures = new List<string>(), errors = new List<string>();
            public List<NpcDecisionEvent> decisionEvents = new List<NpcDecisionEvent>();
        }
        private void CheckThat(string name, bool pass, string evidence) => report.checks.Add(new Check { name = name, passed = pass, evidence = evidence });
        private IEnumerator Tap(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }
        private IEnumerator Capture(string name)
        {
            yield return CaptureWorld(name);
        }
        private IEnumerator CaptureWorld(string name)
        {
            // ScreenCapture is black for deliberately hidden acceptance players.
            // Render the actual world camera explicitly so visual evidence remains
            // inspectable without foregrounding over the user's running game.
            yield return new WaitForEndOfFrame();
            RenderWorldNow(name);
        }
        private void RenderWorldNow(string name)
        {
            // Living-memory verification updates the HUD and captures within the
            // same coroutine step. Flush the Canvas before the explicit camera
            // render so the retained frame contains the accepted thought text.
            Controls.Hud.Refresh();
            Canvas.ForceUpdateCanvases();
            var camera = Controls.View.GetComponent<Camera>();
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var priorTarget = camera.targetTexture; var priorActive = RenderTexture.active;
            camera.targetTexture = target; RenderTexture.active = target; camera.Render();
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            camera.targetTexture = priorTarget; RenderTexture.active = priorActive;
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            Destroy(texture); target.Release(); Destroy(target); report.captures.Add(name + ".png");
        }
        private void CaptureNow(string name)
        {
            RenderWorldNow(name);
        }
        private IEnumerator ClickMenuButton(int index)
        {
            var rect = GameObject.Find("Option " + index).GetComponent<RectTransform>();
            var point = RectTransformUtility.WorldToScreenPoint(Controls.View.GetComponent<Camera>(), rect.TransformPoint(rect.rect.center));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private IEnumerator Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-integratedSmoke") < 0) yield break;
            int at = Array.IndexOf(args, "-integratedEvidence");
            if (at < 0 || at + 1 >= args.Length || !Path.IsPathFullyQualified(args[at + 1])) throw new InvalidOperationException("Explicit absolute evidence directory required.");
            directory = args[at + 1];
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length != 0) throw new IOException("Evidence already exists.");
            Directory.CreateDirectory(directory);
            Application.logMessageReceived += Log;
            report.version = Application.version; report.worldId = Brain.InstanceWorldId;
            // Only the explicitly requested automated run keeps synthetic devices
            // enabled when another local player becomes foreground.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>("IntegratedAcceptanceKeyboard"); mouse = InputSystem.AddDevice<Mouse>("IntegratedAcceptanceMouse");
            Controls.TestKeyboard = keyboard; Controls.TestMouse = mouse; Controls.AllowUnfocusedTestInput = true;
            yield return null; yield return null;
            CheckThat("world-binding", Brain.Ready && Brain.Perception.WorldId == Brain.InstanceWorldId && Brain.InstanceWorldId == NpcTerrainNavigation.RegionId, Brain.InstanceWorldId);
            float sampledCliff = float.MinValue;
            for (float z = CoastalTerrain.MinZ + 100; z < 600; z += 100)
                for (float x = CoastalTerrain.MinX + 100; x < CoastalTerrain.MaxX; x += 100)
                    sampledCliff = Mathf.Max(sampledCliff, CoastalTerrain.Height(x, z));
            CheckThat("spacious-finite-canyon-world",
                CoastalTerrain.MaxX - CoastalTerrain.MinX >= 1200 && CoastalTerrain.MaxZ - CoastalTerrain.MinZ >= 1600 && sampledCliff >= 80,
                "bounds=" + (CoastalTerrain.MaxX-CoastalTerrain.MinX) + "x" + (CoastalTerrain.MaxZ-CoastalTerrain.MinZ) + "; sampledCliff=" + sampledCliff.ToString("F1") + "m");
            CheckThat("clothing-attached", Brain.GetComponentsInChildren<SkinnedMeshRenderer>().Length > 2 && Brain.transform.GetComponentsInChildren<Transform>().Length > 20, "Visual coverage inspected separately in retained frames.");
            string foodEvidence = "Integrated food adapter missing.";
            bool foodPassed = false;
            try { foodPassed = Food != null && Food.Berry != null && Food.Spring != null && Food.RunAcceptanceSequence(out foodEvidence); }
            catch (Exception exception) { foodEvidence = exception.GetType().Name + ": " + exception.Message; }
            CheckThat("food-model-and-world-targets", foodPassed, foodEvidence);
            float runtimeRockClearance = Food == null ? 0 : Food.MeasureRuntimeRockClearance();
            bool berryGrounded = Food != null && Physics.Raycast(Food.BerryPosition + Vector3.up * 170,
                Vector3.down, out RaycastHit berryGroundHit, 300, 1 << 10, QueryTriggerInteraction.Ignore) &&
                Mathf.Abs(berryGroundHit.point.y - Food.BerryPosition.y) < .08f;
            CheckThat("berry-bush-terrain-and-rock-clearance", Food != null && Food.Berry != null &&
                Mathf.Abs(Food.BerryPosition.y-CoastalTerrain.Height(Food.BerryPosition.x,Food.BerryPosition.z)) < .05f &&
                berryGrounded && Food.MinimumRockClearance >= 3f && runtimeRockClearance >= 3f && Food.RockColliderCount == 84,
                Food == null ? "food adapter missing" : "position=" + Food.BerryPosition + "; terrainDelta=" +
                Mathf.Abs(Food.BerryPosition.y-CoastalTerrain.Height(Food.BerryPosition.x,Food.BerryPosition.z)).ToString("F3") +
                "; bakedClearance=" + Food.MinimumRockClearance.ToString("F2") + "m; runtimeClearance=" +
                runtimeRockClearance.ToString("F2") + "m; meshGrounded=" + berryGrounded +
                "; layer8RockColliders=" + Food.RockColliderCount);
            yield return Capture("01-default-coastal-inhabitant");
            Controls.View.ExternalView = true; Controls.SuppressView = true;
            Controls.View.transform.SetPositionAndRotation(new Vector3(-250, 170, -360),
                Quaternion.LookRotation(new Vector3(0, 42, 500) - new Vector3(-250, 170, -360)));
            yield return CaptureWorld("01b-spacious-canyon-vista");
            if (Food != null)
            {
                var berryView = Food.BerryPosition + new Vector3(-7, 2.6f, -6);
                berryView.y = Mathf.Max(berryView.y, CoastalTerrain.Height(berryView.x, berryView.z) + 1.85f);
                bool berryVisible = !Physics.Linecast(berryView, Food.BerryPosition + Vector3.up * 1.1f,
                    (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore);
                CheckThat("berry-bush-camera-line-of-sight", berryVisible,
                    "camera=" + berryView + "; bush=" + Food.BerryPosition + "; authored terrain/rocks do not occlude the evidence view");
                Controls.View.transform.SetPositionAndRotation(berryView,
                    Quaternion.LookRotation(Food.BerryPosition + Vector3.up - berryView));
                yield return CaptureWorld("01c-readable-berry-bush");
            }
            Controls.SuppressView = false; Controls.View.ExternalView = true; Brain.Actor.View.Follow();
            string memoryPath = Path.Combine(directory, "combined-memory-events.jsonl");
            using (var memory = new StarfallMemoryExport(memoryPath, Brain.InstanceWorldId, "unity-combined", "combined-cycle", Application.version))
            {
                memory.RegisterIdentity(NpcAutonomy.AgentId, "Inhabitant 01", Brain.Tick); Brain.MemoryExport = memory;
                float until = Time.realtimeSinceStartup + 150;
                while (Brain.Actions.Deliveries < 3 && Time.realtimeSinceStartup < until) yield return null;
                Brain.MemoryExport = null; report.memoryEvents = memory.Count;
            }
            int occupied = 0, deliveredItems = 0;
            foreach (var item in Brain.Registry)
            {
                if (item.Kind == NpcObjectKind.Destination && item.Occupant.Length > 0) occupied++;
                if (item.Kind == NpcObjectKind.Item && item.DeliveredTo.Length > 0) deliveredItems++;
            }
            bool completeCycle = Brain.Actions.Deliveries == 3 && occupied == 3 && deliveredItems == 3 && Brain.Actions.Held == null;
            report.fullCycleDeliveries = Brain.Actions.Deliveries;
            CheckThat("complete-three-object-autonomy-cycle", completeCycle,
                "deliveries=" + Brain.Actions.Deliveries + "; occupied=" + occupied + "; deliveredItems=" + deliveredItems +
                "; held=" + (Brain.Actions.Held == null ? "none" : Brain.Actions.Held.StableId) + "; phase=" + Brain.Phase +
                "; result=" + Brain.LastResult + "; failures=" + Brain.FailureCount);
            CheckThat("remembered-action-receipts", report.memoryEvents == 7 && Brain.MemoryExportFailure.Length == 0,
                "identity plus six successful pickup/delivery receipts; events=" + report.memoryEvents + "; export=" + Brain.MemoryExportFailure);
            report.decisionEvents.AddRange(Brain.Log.Entries);
            yield return Capture("02-complete-autonomy-cycle");
            yield return Tap(Key.Tab);
            // Test traversal in the known starting corridor; the first delivery
            // finishes beside a solid depot, where backward motion may be blocked.
            Brain.Actor.Place(Brain.SpawnPosition); Physics.SyncTransforms();
            Controls.View.Yaw = 0; yield return null;
            var before = Brain.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return new WaitForSeconds(1);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return new WaitForEndOfFrame();
            CheckThat("possessed-body-traversal", Brain.Possessed && Vector3.Distance(before, Brain.transform.position) > .3f,
                "Starting corridor; same CharacterController. From " + before + " to " + Brain.transform.position);
            var lookBefore = Controls.View.transform.rotation;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(45, -12) }.WithButton(MouseButton.Right)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
            CheckThat("possessed-captured-look", Controls.Looking && Quaternion.Angle(lookBefore, Controls.View.transform.rotation) > 1, "Persistent capture survives button release; virtual input only.");
            yield return Capture("03-possessed-look");
            yield return Tap(Key.Escape); long tick = Environment.Clock.Tick; int npcTick = Brain.Tick;
            yield return new WaitForSecondsRealtime(.3f);
            CheckThat("pause-releases-and-stops-simulation", Controls.MenuOpen && !Controls.Looking && Brain.Tick == npcTick && Environment.Clock.Tick == tick, "Both clocks stopped while menu open.");
            yield return Capture("04-paused-options");
            yield return ClickMenuButton(1);
            CheckThat("pointer-opens-controls-menu", Controls.MenuOpen && Controls.Page == "Controls", "Actual UI pointer event routing through the canvas raycaster.");
            yield return Capture("04b-pointer-controls");
            if (Controls.Page == "Controls") yield return ClickMenuButton(3);
            CheckThat("pointer-returns-to-options", Controls.MenuOpen && Controls.Page == "Root", "Pointer back action keeps simulation paused.");
            yield return Tap(Key.P);
            CheckThat("resume-restores-capture", !Controls.MenuOpen && Controls.Looking, "Prior play capture intent restored.");
            var oldMode = Screen.fullScreenMode;
            yield return Tap(Key.F11); yield return new WaitForSecondsRealtime(1);
            CheckThat("fullscreen-transition", Screen.fullScreenMode == FullScreenMode.FullScreenWindow &&
                Screen.fullScreenMode != oldMode && !Controls.DisplayShortcutActive && Controls.Looking, Screen.fullScreenMode.ToString());
            yield return Capture("05-fullscreen");
            yield return Tap(Key.F11); yield return new WaitForSecondsRealtime(1);
            CheckThat("window-restoration", Screen.fullScreenMode == FullScreenMode.Windowed && Controls.Looking,
                Screen.fullScreenMode + "; initial=" + oldMode + "; resizable window restored");
            yield return Tap(Key.Tab); yield return Tap(Key.F);
            var spectator = Controls.View.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return new WaitForSeconds(1);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            // The camera copies SpectatorPosition in LateUpdate. Wait through the
            // rendered frame so a slower model-enabled run cannot sample the view
            // one LateUpdate behind the controller's authoritative position.
            yield return new WaitForEndOfFrame();
            var spectatorActual = Controls.View.transform.position;
            CheckThat("free-spectator-traversal", Controls.FreeSpectator && !Brain.Possessed &&
                Vector3.Distance(spectator, Controls.SpectatorPosition) > 1f &&
                Vector3.Distance(spectator, spectatorActual) > 1f &&
                Vector3.Distance(Controls.SpectatorPosition, spectatorActual) < .05f,
                "Same world and NPC; from " + spectator + " to internal=" + Controls.SpectatorPosition +
                "; actualCamera=" + spectatorActual + "; mode=" + Controls.Mode);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right)); yield return null; yield return null;
            lookBefore = Controls.View.transform.rotation;
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(-35, 10) }.WithButton(MouseButton.Right)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
            CheckThat("spectator-captured-look", Controls.Looking && Quaternion.Angle(lookBefore, Controls.View.transform.rotation) > 1, "Same sensitivity/capture path in free camera.");
            yield return Tap(Key.Escape); CheckThat("spectator-escape-release", Controls.MenuOpen && !Controls.Looking, "Pointer intent released."); yield return Tap(Key.P);
            yield return Tap(Key.F);
            Brain.ManualSimulation = true; Controls.SuppressInput = true; Controls.SuppressView = true; Controls.View.ExternalView = true;
            for (int weather = 0; weather < 4; weather++)
            {
                Environment.Clock.Tick = weather * 1500 + 300;
                Controls.View.transform.SetPositionAndRotation(new Vector3(-19, 8, -18), Quaternion.LookRotation(new Vector3(9, 4, 75) - new Vector3(-19, 8, -18)));
                yield return new WaitForSeconds(.4f); yield return Capture("06-weather-" + Environment.Weather);
            }
            var refugeObject = GameObject.Find("First refuge / authored v1");
            var refugeCentre = refugeObject == null ? Vector3.zero : refugeObject.transform.position + new Vector3(-10,2.2f,0);
            Controls.View.transform.position = refugeCentre + new Vector3(14,3,-9); Controls.View.transform.LookAt(refugeCentre);
            yield return CaptureWorld("07-refuge-entry");
            var refugeOutside = refugeCentre + new Vector3(18, 0, 0);
            refugeOutside.y = CoastalTerrain.Height(refugeOutside.x, refugeOutside.z);
            var refugeRamp = refugeCentre + new Vector3(8, 0, 0);
            refugeRamp.y = CoastalTerrain.Height(refugeRamp.x, refugeRamp.z);
            var refugeRoute = Brain.TerrainNavigation == null ? null : Brain.TerrainNavigation.Plan(refugeOutside, refugeRamp);
            CheckThat("refuge-approach-route", refugeRoute != null,
                "Navigation route from east-bank shelf to the authored entrance ramp; route=" +
                (refugeRoute == null ? "blocked" : refugeRoute.Count + " points"));
            var refugeRuntime = Controls.View.GetComponent<Starfall.Refuge.RefugeRuntime>();
            var refugeInterior = refugeObject == null ? Vector3.zero : refugeObject.transform.position + new Vector3(-11, 1.8f, 0);
            var interiorRoute = Brain.TerrainNavigation == null ? null : Brain.TerrainNavigation.Plan(refugeOutside, refugeInterior);
            Brain.Actor.Place(refugeOutside + Vector3.up * .02f); Physics.SyncTransforms();
            int refugeSteps = 0;
            if (interiorRoute != null)
                while (interiorRoute.Count > 0 && refugeSteps++ < 2400)
                {
                    var target = interiorRoute.Peek(); var delta = target - Brain.transform.position; delta.y = 0;
                    if (delta.magnitude < .13f) { interiorRoute.Dequeue(); continue; }
                    Brain.Actor.Step(delta.normalized, NpcAutonomy.StepSeconds); yield return new WaitForFixedUpdate();
                }
            bool reachedInterior = interiorRoute != null && interiorRoute.Count == 0 &&
                Vector2.Distance(new Vector2(Brain.transform.position.x, Brain.transform.position.z),
                    new Vector2(refugeInterior.x, refugeInterior.z)) < .5f;
            if (refugeRuntime != null) refugeRuntime.Clock.Tick = 4900;
            var sheltered = refugeRuntime == null ? default(Starfall.EnvironmentZones.ZoneWeather) :
                refugeRuntime.Sample(Brain.transform.position + Vector3.up);
            CheckThat("refuge-continuous-actor-entry-and-shelter", reachedInterior && refugeRuntime != null &&
                sheltered.Valid && sheltered.RainMultiplier < .02f,
                "same CharacterController; steps=" + refugeSteps + "; final=" + Brain.transform.position +
                "; interior=" + refugeInterior + "; rainMultiplier=" + sheltered.RainMultiplier.ToString("F3"));
            Controls.View.transform.position = Brain.transform.position + new Vector3(3, 2.5f, -2);
            Controls.View.transform.LookAt(Brain.transform.position + Vector3.up);
            yield return CaptureWorld("07b-refuge-actor-inside");
            CheckThat("refuge-discoverable", refugeObject != null && Array.Exists(Brain.Registry, x => x.StableId == "first-refuge" && x.Kind == NpcObjectKind.Place) &&
                refugeCentre.y > CoastalTerrain.Height(refugeCentre.x,refugeCentre.z),
                "Authored geometry is above sampled terrain and registered as a non-pickup place; live memory is not connected.");
            foreach (string pose in new[] { "Idle", "Crouch", "Sit" })
            {
                Brain.Actor.Animator.Play(pose, 0, 0); Brain.Actor.Animator.Update(.5f);
                for (int i = 0; i < 4; i++)
                {
                    var pivot = Brain.transform.position + Vector3.up * .9f;
                    Controls.View.transform.position = pivot + Quaternion.Euler(12, i * 90, 0) * Vector3.back * 3.2f; Controls.View.transform.LookAt(pivot);
                    yield return Capture("08-clothing-" + pose + "-" + i);
                }
            }
            if (Array.IndexOf(args, "-npcLivingMemory") >= 0)
            {
                var living = StarfallLivingMemoryAcceptance.Verify(Brain, Controls.Hud, CheckThat, CaptureNow, directory);
                while (true)
                {
                    // Keep the evidence camera with the inhabitant after Verify
                    // resets the actor for its fresh, real delivery journey.
                    Controls.View.transform.position = Brain.transform.position + new Vector3(3, 2.5f, -4);
                    Controls.View.transform.LookAt(Brain.transform.position + Vector3.up);
                    if (!living.MoveNext()) break;
                    yield return living.Current;
                }
            }
            report.deliveries = Brain.Actions.Deliveries; report.errors.AddRange(errors);
            CheckThat("no-runtime-errors", errors.Count == 0, errors.Count + " recorded errors");
            report.status = report.checks.Exists(x => !x.passed) ? "FAIL" : "PASS_AUTOMATED_NATIVE_AND_COVERAGE_REVIEW_PENDING";
            File.WriteAllText(Path.Combine(directory, "integrated-runtime.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.checks.Exists(x => !x.passed) ? 4 : 0);
        }
        private void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    }
}
