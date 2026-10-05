using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CityLife.World
{
    public static class NpcHybridAcceptance
    {
        private sealed class Fake : INpcProposalProvider
        {
            public string Name => "fake-provider / acceptance only";
            public Func<NpcProposalContext, CancellationToken, Task<string>> Handler;
            public Task<string> ProposeAsync(NpcProposalContext context, CancellationToken cancellation) => Handler(context, cancellation);
        }
        private static string Reply(NpcProposalContext c, string target = null)
        {
            string goal = string.IsNullOrEmpty(c.Cargo) ? "collect" : "deliver";
            target = target ?? (c.Eligible.Any(x => x.id == "blue") ? "blue" : c.Eligible.First().id);
            return "{\"version\":1,\"request_id\":" + c.RequestId + ",\"goal\":\"" + goal + "\",\"target_id\":\"" + target +
                "\",\"plan\":[\"observe\",\"" + goal + "\"],\"dialogue\":\"I will visit the selected crystal or depot.\",\"reflection\":\"This is scripted test text, not learning.\"}";
        }
        [Serializable] private sealed class AuditReport { public string source, realInference; public NpcProposalAudit[] events; }
        public static IEnumerator Verify(NpcAutonomy brain, NpcPlayerControls controls, NpcDecisionHud hud,
            Action<string, bool, string> need, Action<string> capture, string directory)
        {
            var planner = brain.OptionalPlanner;
            need("hybrid-disabled-by-default", planner != null && !planner.EnabledByUser && !planner.Pending && planner.Audit.Count == 0,
                "All preceding deterministic baseline and control checks executed with optional thoughts off and no provider requests.");
            var fake = new Fake { Handler = (c, token) => Task.FromResult(Reply(c)) };
            brain.ResetState(); planner.Configure(fake); hud.Detailed = true;
            var keyboard = InputSystem.AddDevice<Keyboard>("HybridMenuAcceptance"); controls.TestKeyboard = keyboard; controls.SuppressInput = false;
            IEnumerator Tap(Key key)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            }
            try
            {
                yield return Tap(Key.P); yield return Tap(Key.DownArrow); yield return Tap(Key.Enter);
                yield return Tap(Key.DownArrow); yield return Tap(Key.Enter);
                need("local-thoughts-menu-default-off", controls.Page == "Thoughts" && !planner.EnabledByUser,
                    "Actual keyboard events open Controls > Local thoughts; configured provider is still off.");
                capture("29-local-thoughts-options"); yield return Tap(Key.Enter);
                need("local-thoughts-explicit-menu-opt-in", planner.EnabledByUser && brain.MenuPaused && !planner.Pending,
                    "The menu enables configured local thoughts explicitly; paused simulation launches no request.");
                yield return Tap(Key.P);
            }
            finally { controls.TestKeyboard = null; controls.SuppressInput = true; InputSystem.RemoveDevice(keyboard); }
            brain.StepTick();
            need("provider-does-not-mutate-world", brain.GoalId == "" && brain.Actions.Held == null && brain.Actions.Deliveries == 0,
                "Provider reply alone cannot navigate, pickup, deliver or mutate inventory; the decision boundary must consume and validate it.");
            brain.StepTick(); yield return null;
            need("fake-provider-selects-eligible-nonnearest-goal", brain.GoalId == "blue" && planner.Audit.Last().outcome == "accepted-high-level-goal",
                "A deterministic fake provider selects blue instead of nearest amber; fresh eligibility validation admits only the high-level goal.");
            need("hybrid-HUD-source-and-text", planner.Status.Contains("fake-provider") && planner.Dialogue.Length > 0 && planner.Reflection.Contains("test text") && planner.Plan.Contains("collect"),
                "The live HUD distinguishes the fake source and exposes advisory plan, fictional dialogue and generated reflection.");
            capture("30-fake-proposal-and-dialogue");
            for (int i = 0; i < 2400 && brain.Actions.Deliveries == 0; i++) { brain.StepTick(); yield return null; }
            need("fake-goal-executes-through-validated-actions", brain.Actions.Deliveries == 1 &&
                brain.Log.Entries.Any(x => x.goal == "blue" && x.result == "picked-up") && brain.Log.Entries.Any(x => x.result == "delivered"),
                "Actual humanoid navigates, carries blue and delivers using the unchanged deterministic NpcActionApi.");
            capture("31-fake-goal-delivered");

            brain.ResetState(); fake.Handler = (c, token) => Task.FromResult(Reply(c, "reserved-red")); planner.SetEnabled(true);
            brain.StepTick(); brain.StepTick(); yield return null;
            need("invalid-live-proposal-falls-back", brain.GoalId == "amber" && planner.Audit.Last().outcome == "target-not-in-request" && brain.Actions.Held == null,
                "Proposed denied target is rejected without pickup; deterministic policy selects amber immediately.");
            capture("32-rejected-proposal-fallback");

            brain.ResetState(); planner.Configure(new NpcLocalProposalProvider("http://127.0.0.1:1", "unavailable-test-model")); planner.SetEnabled(true);
            float deadline = Time.realtimeSinceStartup + 5;
            while (brain.GoalId.Length == 0 && Time.realtimeSinceStartup < deadline) { brain.StepTick(); yield return null; }
            string offlineOutcome = planner.Audit.Last().outcome;
            need("actual-unavailable-endpoint-fallback", brain.GoalId == "amber" && (offlineOutcome == "provider-unavailable" || offlineOutcome == "timeout"),
                "Actual closed-loopback adapter outcome=" + offlineOutcome + "; goal=" + brain.GoalId + ". An unavailable endpoint may fail immediately or hit the bounded deadline; both resume deterministic choice.");
            need("unavailable-circuit-prevents-repeated-stalls", !planner.EnabledByUser && !planner.Pending,
                "The first unavailable/timeout reply disables optional thoughts until explicit retry, so later goals do not repeatedly wait for the service.");
            for (int i = 0; i < 2200 && brain.Actions.Deliveries == 0; i++) { brain.StepTick(); yield return null; }
            need("offline-fallback-still-delivers", brain.Actions.Deliveries == 1, "The unavailable model never prevents the actual deterministic delivery flow.");
            capture("33-offline-provider-delivery");

            brain.ResetState(); var late = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            NpcProposalContext requested = null;
            fake.Handler = (c, token) => { requested = c; return late.Task; };
            planner.Configure(fake); planner.TimeoutMilliseconds = 40; planner.SetEnabled(true);
            deadline = Time.realtimeSinceStartup + 5;
            while (brain.GoalId.Length == 0 && Time.realtimeSinceStartup < deadline) { brain.StepTick(); yield return null; }
            need("runtime-timeout-fallback", brain.GoalId == "amber" && planner.Audit.Last().outcome == "timeout", "A noncooperative delayed provider times out and deterministic choice resumes.");
            late.SetResult(Reply(requested)); yield return null;
            need("runtime-late-reply-discarded", brain.GoalId == "amber" && brain.Actions.Held == null,
                "Late blue proposal cannot replace the fallback amber goal or acquire cargo.");
            capture("34-timeout-and-late-discard");

            brain.ResetState(); late = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            planner.TimeoutMilliseconds = 1500; planner.Configure(fake); planner.SetEnabled(true); brain.StepTick();
            var identity = brain.gameObject.GetEntityId(); var position = brain.transform.position;
            controls.OpenMenu();
            need("pause-cancels-pending-proposal", !planner.Pending && brain.MenuPaused && brain.Tick == 1 && brain.gameObject.GetEntityId() == identity,
                "Opening P/options cancels the pending request immediately and preserves the same paused actor.");
            late.SetResult(Reply(requested)); yield return null; brain.StepTick();
            need("cancelled-reply-cannot-mutate-paused-world", brain.GoalId == "" && brain.Actions.Held == null && brain.transform.position == position && brain.Tick == 1,
                "A reply after cancellation cannot affect the paused world.");
            controls.Resume(); planner.SetEnabled(false); brain.StepTick();
            need("disable-resumes-deterministic-goal", brain.GoalId == "amber" && !planner.EnabledByUser, "Disabling thoughts resumes deterministic selection on the same NPC.");
            capture("35-cancelled-model-rules-resumed");
            File.WriteAllText(Path.Combine(directory, "hybrid-audit.json"), JsonUtility.ToJson(new AuditReport {
                source = "Actual Unity player; deterministic fake and unavailable local HTTP provider", realInference = "not executed in this acceptance run", events = planner.Audit.ToArray() }, true));
            planner.Configure(null); brain.ResetState();
        }
    }
}
