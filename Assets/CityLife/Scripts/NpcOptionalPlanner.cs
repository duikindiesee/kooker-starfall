using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CityLife.World
{
    // The sole integration point is advisory goal selection. The provider never receives Unity objects or action APIs.
    public sealed class NpcOptionalPlanner : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public bool EnabledByUser { get; private set; }
        public bool Configured => provider != null;
        public bool Pending => pending != null;
        public bool ReplyReady => pending != null && pending.IsCompleted;
        public int TimeoutMilliseconds = 1500;
        public const int SessionRequestLimit = 12;
        public string Status { get; private set; } = "Local thoughts off";
        public string Dialogue { get; private set; } = "";
        public string Reflection { get; private set; } = "";
        public string Plan { get; private set; } = "";
        public List<NpcProposalAudit> Audit = new List<NpcProposalAudit>();
        private INpcProposalProvider provider;
        private Task<NpcProposalResult> pending;
        private CancellationTokenSource cancellation;
        private NpcProposalContext context;
        private int sequence, count, nextRequestTick, waitUntil;
        private void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Argument(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            string model = Argument("-npcLocalModel"), endpoint = Argument("-npcLocalEndpoint");
            if (model == null && endpoint == null) return;
            try { Configure(new NpcLocalProposalProvider(endpoint, model)); }
            catch (ArgumentException) { Status = "Local configuration invalid; rules active"; }
            // Supplying configuration does not enable requests. User explicitly toggles Local thoughts in the menu.
        }
        public void Configure(INpcProposalProvider configured)
        {
            Cancel("provider-reconfigured"); if (provider is IDisposable disposable) disposable.Dispose();
            provider = configured; EnabledByUser = false; count = 0; nextRequestTick = 0;
            Status = configured == null ? "Local thoughts unavailable; rules active" : "Local thoughts ready / off";
        }
        public void SetEnabled(bool enabled)
        {
            Cancel("local-thoughts-disabled"); EnabledByUser = enabled && Configured;
            Status = EnabledByUser ? "Local thoughts on" : Configured ? "Local thoughts off" : "Local thoughts unconfigured; rules active";
        }
        private void Update()
        {
            if (Pending && (Brain.MenuPaused || Brain.Possessed || !Brain.Running)) Cancel("control-interruption");
        }
        public void Cancel(string reason)
        {
            waitUntil = 0;
            if (pending == null) return;
            cancellation.Cancel(); cancellation.Dispose(); cancellation = null;
            Record(new NpcProposalAudit { requestId = context.RequestId, tick = context.Tick, provider = provider.Name, outcome = reason });
            pending = null; context = null; nextRequestTick = Brain.Tick + 250;
            Status = "Thought cancelled; rules active";
        }
        public void ResetSession()
        { Cancel("world-reset"); nextRequestTick = waitUntil = count = 0; Dialogue = Reflection = Plan = ""; }
        private void OnDisable() => Cancel("planner-disabled");
        private void OnDestroy() { Cancel("planner-destroyed"); if (provider is IDisposable disposable) disposable.Dispose(); }
        private void Record(NpcProposalAudit row)
        { if (Audit.Count >= 128) Audit.RemoveAt(0); Audit.Add(row); }

        // true = optional layer handled this boundary, including bounded waiting. false = deterministic policy immediately.
        public bool Choose(IReadOnlyDictionary<string, int> retry, out NpcObservation chosen)
        {
            chosen = null;
            if (!EnabledByUser || !Configured) return false;
            if (Brain.Tick < waitUntil) return true;
            string cargo = Brain.Actions.Held == null ? "" : Brain.Actions.Held.StableId;
            if (pending != null)
            {
                if (!pending.IsCompleted) return true;
                var result = pending.GetAwaiter().GetResult(); pending = null; cancellation.Dispose(); cancellation = null;
                nextRequestTick = Brain.Tick + 250;
                // Sense again now; target permission, availability and LOS may have changed while the model worked.
                var current = Brain.Perception.Sense(Brain.Tick);
                string code = result.Audit.outcome;
                bool accepted = result.Proposal != null && NpcProposalValidator.ValidateLive(result.Proposal, context, current, cargo, retry, Brain.Tick, out code);
                result.Audit.outcome = code; Record(result.Audit); context = null;
                Status = accepted ? provider.Name + " / " + result.Proposal.goal : "Deterministic fallback / " + code;
                Brain.Log.Record(Brain.Tick, "proposal", Brain.DescribePerception(), accepted ? result.Proposal.target_id : "",
                    "validate high-level proposal", code, accepted ? "" : "deterministic nearest eligible goal");
                if (!accepted)
                {
                    // A slow/absent service must not stall every later goal. Explicit menu opt-in retries it.
                    if (code == "timeout" || code == "provider-unavailable") EnabledByUser = false;
                    return false;
                }
                Dialogue = result.Proposal.dialogue; Reflection = result.Proposal.reflection; Plan = string.Join(" > ", result.Proposal.plan);
                if (result.Proposal.goal == "wait") { waitUntil = Brain.Tick + 50; return true; }
                chosen = current.Single(x => x.id == result.Proposal.target_id); return true;
            }
            if (Brain.Tick < nextRequestTick || count >= SessionRequestLimit)
            { if (count >= SessionRequestLimit) Status = "Session thought limit; rules active"; return false; }
            var eligible = Brain.Perception.Current.Where(x => x.permission && x.available &&
                x.kind == (cargo.Length == 0 ? NpcObjectKind.Item : NpcObjectKind.Destination) &&
                (!retry.TryGetValue(x.id, out int until) || until <= Brain.Tick)).Take(16).Select(x => new NpcObservation {
                    id = x.id, kind = x.kind, permission = true, available = true, distanceMillimetres = x.distanceMillimetres, seenAtTick = x.seenAtTick }).ToArray();
            if (eligible.Length == 0) return false;
            context = new NpcProposalContext { RequestId = ++sequence, Tick = Brain.Tick, Cargo = cargo, Eligible = eligible, LastOutcome = Brain.LastResult };
            cancellation = new CancellationTokenSource(); count++;
            pending = NpcProposalBroker.Request(provider, context, TimeoutMilliseconds, cancellation.Token);
            Status = provider.Name + " / thinking; body waiting"; return true;
        }
    }
}
