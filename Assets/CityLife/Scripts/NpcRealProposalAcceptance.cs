using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CityLife.World
{
    // Opt-in evidence only, after the full fake/offline acceptance. Exactly one proposal attempt.
    public static class NpcRealProposalAcceptance
    {
        [Serializable] private sealed class Report
        {
            public string status, model, execution, limitation;
            public int deliveries;
            public int completionRequestsAttempted, deadlineMilliseconds;
            public bool providerReached, completionHttpResponseReceived;
            public NpcProposalAudit audit;
        }
        public static IEnumerator Verify(NpcAutonomy brain, NpcDecisionHud hud, Action<string, bool, string> need, Action<string> capture, string directory)
        {
            string[] args = Environment.GetCommandLineArgs();
            string Argument(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var planner = brain.OptionalPlanner;
            brain.ResetState();
            string model = Argument("-npcLocalModel");
            var provider = new NpcLocalProposalProvider(Argument("-npcLocalEndpoint"), model);
            planner.Configure(provider);
            planner.TimeoutMilliseconds = 5000; planner.SetEnabled(true); hud.Detailed = true;
            int before = planner.Audit.Count; brain.StepTick();
            float deadline = Time.realtimeSinceStartup + 7;
            // Keep the synthetic scene snapshot stable while real wall-clock inference runs. No accelerated tick aging.
            while (!planner.ReplyReady && Time.realtimeSinceStartup < deadline) yield return null;
            need("real-probe-bounded-completion", planner.ReplyReady, "One actual local provider attempt completes or times out within the bounded probe window.");
            need("real-provider-cannot-mutate-world", brain.GoalId == "" && brain.Actions.Held == null && brain.Actions.Deliveries == 0,
                "A completed HTTP/model reply has not yet passed the deterministic goal boundary and cannot touch the world.");
            brain.StepTick(); yield return null;
            need("real-probe-one-audited-attempt", planner.Audit.Count == before + 1 && !planner.Pending,
                "Exactly one local provider attempt was consumed and audited.");
            var audit = planner.Audit.Last();
            bool accepted = audit.outcome == "accepted-high-level-goal" || audit.outcome == "accepted-bounded-wait";
            capture("40-real-local-proposal-outcome");
            planner.SetEnabled(false); // No second model call, even at delivery or on fallback.
            for (int i = 0; i < 2500 && brain.Actions.Deliveries == 0; i++) { brain.StepTick(); yield return null; }
            need("real-probe-deterministic-execution-or-fallback", brain.Actions.Deliveries == 1 && planner.Audit.Count == before + 1 &&
                brain.Log.Entries.Any(x => x.result == "picked-up") && brain.Log.Entries.Any(x => x.result == "delivered"),
                "Actual deterministic pickup/delivery completed after the one proposal. Outcome=" + audit.outcome + "; measured=" + audit.milliseconds + "ms.");
            capture("41-real-probe-delivery-or-fallback");
            File.WriteAllText(Path.Combine(directory, "real-local-probe.json"), JsonUtility.ToJson(new Report {
                status = accepted ? "VALIDATED_PROPOSAL_AND_DELIVERY" : "SAFE_FALLBACK_AND_DELIVERY", model = model, audit = audit,
                execution = "Existing deterministic goal/navigation/action boundary only; one provider attempt; no load/unload/settings calls",
                limitation = "Single synthetic local probe with 5000ms diagnostic budget; normal play uses 1500ms. Not a reliability benchmark. A sent HTTP attempt alone does not prove completed inference.",
                deliveries = brain.Actions.Deliveries, completionRequestsAttempted = provider.CompletionRequestsAttempted, deadlineMilliseconds = 5000,
                providerReached = provider.InventoryResponseReceived, completionHttpResponseReceived = provider.CompletionResponseReceived }, true));
            planner.Configure(null);
        }
    }
}
