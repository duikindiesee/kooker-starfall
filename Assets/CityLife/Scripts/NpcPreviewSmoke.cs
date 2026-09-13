using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CityLife.World
{
    [DefaultExecutionOrder(-200)]
    public sealed class NpcPreviewSmoke : MonoBehaviour
    {
        public static readonly bool Requested = Array.IndexOf(Environment.GetCommandLineArgs(), "-npcSmoke") >= 0;
        public NpcAutonomy Brain;
        public NpcDecisionHud Hud;
        public CharacterPreviewCamera View;
        public NpcPlayerControls Controls;
        private Camera cameraComponent;
        private RenderTexture target;
        private UniversalRenderPipeline.SingleCameraRequest request;
        private string directory;
        private bool done, ready;
        private float started;
        private Report report;
        private static readonly List<string> Errors = new List<string>();
        [Serializable] public sealed class Check { public string name, evidence; public bool passed; }
        [Serializable] public sealed class Scenario
        {
            public string name, firstGoal;
            public int ticks, deliveries, failures;
            public string[] chosenGoals;
            public List<NpcDecisionEvent> events;
        }
        [Serializable] public sealed class Report
        {
            public string status, utc, unityVersion, version, buildId, gpu, failure, limit;
            public float seconds;
            public List<Check> checks = new List<Check>();
            public List<Scenario> scenarios = new List<Scenario>();
            public List<string> captures = new List<string>(), errors = new List<string>();
            public List<NpcProposalAudit> proposalAudit;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Listen()
        {
            if (!Requested) return; Errors.Clear();
            Application.logMessageReceived += (message, stack, type) =>
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message); };
        }
        private void Awake()
        {
            if (!Requested) { enabled = false; return; }
            started = Time.realtimeSinceStartup;
            report = new Report { utc = DateTime.UtcNow.ToString("O"), version = Application.version,
                buildId = Path.GetFileName(Path.GetDirectoryName(Application.dataPath)),
                unityVersion = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                limit = "Actual offscreen standalone player. Fixed-tick deterministic rules, not learning. No native keyboard/mouse acceptance or cross-device bit-identical physics claim." };
            try
            {
                string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-npcEvidence");
                if (i < 0 || i + 1 >= args.Length || !Path.IsPathFullyQualified(args[i + 1])) throw new IOException("Absolute NPC evidence directory required.");
                directory = Path.GetFullPath(args[i + 1]);
                if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length > 0) throw new IOException("Preserve existing evidence.");
                Directory.CreateDirectory(directory);
                Brain.ManualSimulation = true; View.SuppressInput = true;
                Controls.AllowUnfocusedTestInput = true; Controls.SuppressInput = true; Hud.Detailed = true;
                Time.captureDeltaTime = NpcAutonomy.StepSeconds; Time.fixedDeltaTime = NpcAutonomy.StepSeconds;
                QualitySettings.vSyncCount = 0;
                cameraComponent = GetComponent<Camera>(); cameraComponent.aspect = 1600f / 900;
                target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Need("render-target", target.Create(), "1600 x 900 actual player view including camera-space HUD.");
                cameraComponent.targetTexture = target;
                request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            }
            catch (Exception e) { Finish(e); }
        }
        private IEnumerator Start()
        {
            if (!Requested || done) yield break;
            var routine = Verify();
            while (!done)
            {
                bool more = false; object next = null; Exception error = null;
                try
                {
                    if (Errors.Count > 0) throw new InvalidOperationException("Player reported runtime errors.");
                    if (Time.realtimeSinceStartup - started > 240) throw new TimeoutException("NPC proof exceeded four minutes.");
                    more = routine.MoveNext(); if (more) next = routine.Current;
                }
                catch (Exception e) { error = e; }
                if (error != null) { Finish(error); yield break; }
                if (!more) { Finish(null); yield break; }
                yield return next;
            }
        }
        private void Need(string name, bool condition, string evidence)
        {
            report.checks.Add(new Check { name = name, passed = condition, evidence = evidence });
            if (!condition) throw new InvalidOperationException(name + ": " + evidence);
        }
        private IEnumerator Verify()
        {
            for (int i = 0; i < 30 || !Brain.Ready; i++) yield return null;
            Need("urp-request", RenderPipeline.SupportsRenderRequest(cameraComponent, request), "Actual URP initialized.");
            ready = true;
            Need("valid-human-avatar", Brain.Actor.Animator.avatar.isHuman && Brain.Actor.Animator.avatar.isValid, "Verified adult body and existing mapped animation rig.");
            Brain.StepTick(); yield return null; Capture("01-perception-and-goal");
            Need("perception-visible-item", Brain.Perception.Current.Any(x => x.id == "amber"), "Nearby amber detected through actual overlap and ray queries.");
            Need("perception-hidden-item-excluded", !Brain.Perception.Current.Any(x => x.id == "hidden-green"), "Solid route obstacle blocks initial sight.");
            Need("permission-filter", Brain.Perception.Current.Any(x => x.id == "reserved-red" && !x.permission) && Brain.GoalId != "reserved-red",
                "The closer reserved object is perceived but cannot be chosen.");
            Need("nearest-goal-selected", Brain.GoalId == "amber", "Goal selected from observations rather than an authored crystal sequence.");
            Need("hud-populated", Hud.Summary != null && Hud.Summary.text.Contains("amber") && Hud.Perceptions.text.Contains("DENIED"),
                "The live camera-space decision panel contains the current goal and denied perception.");
            Directory.CreateDirectory(Path.Combine(directory, "baseline-frames"));
            bool capturedCarry = false;
            for (int i = 0; i < 2200 && Brain.Actions.Deliveries == 0; i++)
            {
                Brain.StepTick(); yield return null; Finite();
                if (i % 25 == 0) Capture("baseline-frames/frame-" + (i / 25).ToString("D4"), false);
                if (!capturedCarry && Brain.Actions.Held != null)
                { Capture("02-carrying-chosen-item"); capturedCarry = true; }
            }
            Need("autonomous-delivery", Brain.Actions.Deliveries == 1 && capturedCarry, "Perceived item picked up and delivered to a selected eligible depot.");
            Need("validated-result-journal", Brain.Log.Entries.Any(x => x.result == "picked-up") && Brain.Log.Entries.Any(x => x.result == "delivered"),
                "The live log records successful deterministic API outcomes.");
            Capture("03-delivered-and-log"); SaveScenario("baseline");
            string firstTrace = Trace();

            Brain.ResetState();
            for (int i = 0; i < 2200 && Brain.Actions.Deliveries == 0; i++) { Brain.StepTick(); yield return null; }
            Need("repeatable-decision-and-action-trace", Brain.Actions.Deliveries == 1 && Trace() == firstTrace,
                "Same scene reset reproduces the same goal/action/result/failure events at the same fixed ticks.");
            SaveScenario("identical-reset");

            Brain.ResetState();
            var amber = Brain.Registry.Single(x => x.StableId == "amber");
            amber.transform.position = new Vector3(25, 1.06f, -1);
            Brain.StepTick(); yield return null;
            Need("layout-changes-goal", Brain.GoalId == "blue", "Moving amber outside perception changes the first choice to blue without a sequence edit.");
            Capture("04-changed-perception-new-goal");
            SaveScenario("amber-outside-sensing-radius");

            Brain.ResetState(); bool revoked = false, capturedFailure = false; string failedGoal = "";
            for (int i = 0; i < 2500 && Brain.Actions.Deliveries == 0; i++)
            {
                Brain.StepTick();
                if (!revoked && Brain.Phase == "Act" && Brain.GoalId == "amber")
                { amber.Permission = false; revoked = true; }
                yield return null;
                if (!capturedFailure && Brain.FailureCount > 0)
                {
                    Need("permission-revocation-nonmutating", Brain.Actions.Held == null && amber.HeldBy == "" && amber.transform.parent == null,
                        "Revoked permission rejects pickup after navigation/gesture without acquiring the object.");
                    Need("failure-has-fallback", Brain.Log.Entries.Any(x => x.result == "permission-denied" && x.fallback.Length > 0),
                        "Failure and deterministic retry/goal fallback are recorded.");
                    failedGoal = Brain.Log.Entries.Last(x => x.result == "permission-denied").goal;
                    Capture("05-permission-failure-and-fallback"); capturedFailure = true;
                }
            }
            Need("alternate-goal-after-failure", revoked && capturedFailure && Brain.Actions.Deliveries == 1 &&
                Brain.ChosenGoals.Any(x => x != failedGoal && !x.StartsWith("depot", StringComparison.Ordinal)),
                "After denied pickup, the inhabitant selects another perceived item and completes a delivery.");
            Capture("06-fallback-delivery"); SaveScenario("permission-revoked-before-pickup");

            Brain.ResetState(); bool occupied = false, fullRejected = false; string fullDepot = "";
            for (int i = 0; i < 3000 && Brain.Actions.Deliveries == 0; i++)
            {
                Brain.StepTick();
                if (!occupied && Brain.Phase == "Act" && Brain.Actions.Held != null)
                {
                    fullDepot = Brain.GoalId; Brain.Registry.Single(x => x.StableId == fullDepot).Occupant = "external-fixture";
                    occupied = true;
                }
                yield return null;
                if (!fullRejected && Brain.Log.Entries.Any(x => x.result == "destination-full-or-invalid"))
                {
                    Need("full-destination-retains-cargo", Brain.Actions.Held != null && Brain.Actions.Held.HeldBy == NpcAutonomy.AgentId,
                        "Live capacity rejection preserves owned carried cargo.");
                    Capture("07-full-depot-fallback"); fullRejected = true;
                }
            }
            Need("delivery-fallback-chooses-another-depot", occupied && fullRejected && Brain.Actions.Deliveries == 1 &&
                Brain.Registry.Any(x => x.Kind == NpcObjectKind.Destination && x.StableId != fullDepot && x.Occupant == "amber"),
                "A newly full depot is rejected and another perceived empty depot receives the retained cargo.");
            Capture("08-alternate-depot-delivery"); SaveScenario("destination-filled-before-delivery");

            Brain.ResetState();
            foreach (var item in Brain.Registry) item.Permission = false;
            for (int i = 0; i < 30; i++) { Brain.StepTick(); yield return null; }
            Need("offline-no-goal-fallback", Brain.Phase == "Wait" && Brain.Actions.Held == null && Brain.Actions.Deliveries == 0,
                "No permitted goal produces a safe wait/resense state with no network or LLM dependency.");
            Capture("09-no-permitted-goal"); SaveScenario("all-permissions-denied");
            var controls = NpcControlAcceptance.Verify(Brain, Controls, Hud, View, Need, name => Capture(name));
            while (controls.MoveNext()) yield return controls.Current;
            if (Brain.OptionalPlanner != null)
            {
                var hybrid = NpcHybridAcceptance.Verify(Brain, Controls, Hud, Need, name => Capture(name), directory);
                while (hybrid.MoveNext()) yield return hybrid.Current;
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-npcRealProbe") >= 0)
                {
                    var real = NpcRealProposalAcceptance.Verify(Brain, Hud, Need, name => Capture(name), directory);
                    while (real.MoveNext()) yield return real.Current;
                }
            }
            Need("no-runtime-errors", Errors.Count == 0, "No player error/assert/exception.");
        }
        private void SaveScenario(string name)
        {
            report.scenarios.Add(new Scenario { name = name, ticks = Brain.Tick, deliveries = Brain.Actions.Deliveries,
                failures = Brain.FailureCount, firstGoal = Brain.ChosenGoals.FirstOrDefault() ?? "",
                chosenGoals = Brain.ChosenGoals.ToArray(), events = new List<NpcDecisionEvent>(Brain.Log.Entries) });
        }
        private string Trace() => string.Join("\n", Brain.Log.Entries.Where(x => x.phase != "perception")
            .Select(x => x.tick + ":" + x.phase + ":" + x.goal + ":" + x.action + ":" + x.result));
        private void Finite()
        {
            Vector3 p = Brain.transform.position;
            if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z) || p.y < -.15f || Mathf.Abs(p.x) > 11 || Mathf.Abs(p.z) > 11)
                throw new InvalidOperationException("Actor left the finite collision stage.");
        }
        private void Capture(string name, bool listed = true)
        {
            if (!ready) return; Hud.Refresh();
            RenderPipeline.SubmitRenderRequest(cameraComponent, request);
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
                if (listed) report.captures.Add(name + ".png");
            }
            finally { RenderTexture.active = previous; Destroy(texture); }
        }
        private void Finish(Exception error)
        {
            if (done) return; done = true;
            report.status = error == null ? "PASS" : "FAIL"; report.failure = error?.Message;
            report.seconds = Time.realtimeSinceStartup - started; report.errors = new List<string>(Errors);
            if (Brain.OptionalPlanner != null) report.proposalAudit = new List<NpcProposalAudit>(Brain.OptionalPlanner.Audit);
            if (directory != null) File.WriteAllText(Path.Combine(directory, "npc-runtime.json"), JsonUtility.ToJson(report, true));
            Time.captureDeltaTime = 0;
            if (target != null) target.Release();
            Application.Quit(error == null ? 0 : 3);
        }
    }
}
