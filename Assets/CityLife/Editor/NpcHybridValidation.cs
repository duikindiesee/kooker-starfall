using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CityLife.World.Editor
{
    public static class NpcHybridValidation
    {
        private sealed class Fake : INpcProposalProvider
        {
            public string Name => "deterministic-test-provider";
            public Func<NpcProposalContext, CancellationToken, Task<string>> Handler;
            public Task<string> ProposeAsync(NpcProposalContext context, CancellationToken cancellation) => Handler(context, cancellation);
        }
        [Serializable] public sealed class Report { public string status, utc; public List<string> checks = new List<string>(); }
        public static void Run()
        {
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            void Need(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); report.checks.Add(name); }
            const string good = "{\"version\":1,\"request_id\":7,\"goal\":\"collect\",\"target_id\":\"blue\",\"plan\":[\"observe\",\"collect\",\"deliver\"],\"dialogue\":\"I will collect the blue crystal.\",\"reflection\":\"The previous delivery succeeded.\"}";
            bool Parsed(string raw) => NpcProposalValidator.TryParse(raw, 7, out _, out _);
            Need(NpcProposalValidator.TryParse(good, 7, out var proposal, out _), "strict-proposal-accepts-complete-schema");
            foreach (var item in new Dictionary<string, string> {
                ["unknown-field"] = good.Replace("{", "{\"execute\":\"delete\","),
                ["duplicate-field"] = good.Replace("{", "{\"version\":1,"),
                ["missing-field"] = good.Replace("\"version\":1,", ""),
                ["wrong-type"] = good.Replace("\"version\":1", "\"version\":\"1\""),
                ["decimal-id"] = good.Replace("\"request_id\":7", "\"request_id\":7.0"),
                ["wrong-request"] = good.Replace("\"request_id\":7", "\"request_id\":8"),
                ["wrong-version"] = good.Replace("\"version\":1", "\"version\":2"),
                ["code-fence"] = "```json\n" + good + "\n```",
                ["trailing-data"] = good + "{}",
                ["truncated-json"] = good.Substring(0, good.Length - 2),
                ["leading-zero"] = good.Replace("\"version\":1", "\"version\":01"),
                ["unknown-goal"] = good.Replace("\"goal\":\"collect\"", "\"goal\":\"teleport\""),
                ["target-code"] = good.Replace("\"target_id\":\"blue\"", "\"target_id\":\"../file\""),
                ["unbounded-plan"] = good.Replace("[\"observe\",\"collect\",\"deliver\"]", "[\"observe\",\"observe\",\"collect\",\"deliver\"]"),
                ["plan-command"] = good.Replace("[\"observe\",\"collect\",\"deliver\"]", "[\"execute-script\"]"),
                ["long-dialogue"] = good.Replace("I will collect the blue crystal.", new string('a', 161)),
                ["empty-dialogue"] = good.Replace("I will collect the blue crystal.", ""),
                ["empty-reflection"] = good.Replace("The previous delivery succeeded.", ""),
                ["blank-dialogue"] = good.Replace("I will collect the blue crystal.", "   "),
                ["escaped-control"] = good.Replace("I will collect the blue crystal.", "hello\\nworld"),
                ["oversize"] = new string(' ', 4097) + good,
                ["depth-limit"] = new string('[', 12) + "0" + new string(']', 12),
                ["wait-with-target"] = good.Replace("\"goal\":\"collect\"", "\"goal\":\"wait\"") })
                Need(!Parsed(item.Value), "reject-" + item.Key);
            var blue = new NpcObservation { id = "blue", kind = NpcObjectKind.Item, permission = true, available = true, seenAtTick = 100 };
            var current = new List<NpcObservation> { blue };
            var context = new NpcProposalContext { RequestId = 7, Tick = 100, Cargo = "", Eligible = new[] { blue }, LastOutcome = "delivered" };
            var retry = new Dictionary<string, int>();
            bool Live(string cargo = "", int tick = 100) => NpcProposalValidator.ValidateLive(proposal, context, current, cargo, retry, tick, out _);
            Need(Live(), "live-valid-target-accepted-without-executing-action");
            Need(!Live("amber"), "changed-cargo-rejected");
            Need(!Live("", 351), "expired-proposal-rejected");
            blue.permission = false; Need(!Live(), "revoked-permission-rejected"); blue.permission = true;
            blue.available = false; Need(!Live(), "lost-capacity-or-ownership-rejected"); blue.available = true;
            blue.seenAtTick = 80; Need(!Live(), "stale-perception-rejected"); blue.seenAtTick = 100;
            retry["blue"] = 101; Need(!Live(), "live-cooldown-rejected"); retry.Clear();
            current.Clear(); Need(!Live(), "occluded-or-removed-target-rejected"); current.Add(blue);
            context.Eligible = Array.Empty<NpcObservation>(); Need(!Live(), "unsolicited-target-rejected"); context.Eligible = new[] { blue };
            context.Cargo = "amber"; Need(!Live("amber"), "goal-kind-must-match-cargo"); context.Cargo = "";
            Need(NpcProposalValidator.TryParse(good.Replace("\"goal\":\"collect\"", "\"goal\":\"wait\"").Replace("\"target_id\":\"blue\"", "\"target_id\":\"\""), 7, out var wait, out _) &&
                NpcProposalValidator.ValidateLive(wait, context, current, "", retry, 100, out _), "bounded-wait-proposal");
            var fake = new Fake { Handler = (c, token) => Task.FromResult(good) };
            var success = NpcProposalBroker.Request(fake, context, 1000, CancellationToken.None).GetAwaiter().GetResult();
            Need(success.Proposal.target_id == "blue" && success.Audit.schemaValid && success.Audit.requestId == 7 && success.Audit.tick == 100 &&
                success.Audit.provider == fake.Name && success.Audit.dialogue == proposal.dialogue && success.Audit.reflection == proposal.reflection,
                "fake-provider-success-and-inspectable-audit");
            fake.Handler = (c, token) => Task.FromResult("invalid");
            var invalid = NpcProposalBroker.Request(fake, context, 1000, CancellationToken.None).GetAwaiter().GetResult();
            Need(invalid.Proposal == null && !invalid.Audit.schemaValid, "invalid-provider-reply-yields-fallback");
            fake.Handler = (c, token) => Task.FromException<string>(new IOException("private response must not appear in audit"));
            var unavailable = NpcProposalBroker.Request(fake, context, 1000, CancellationToken.None).GetAwaiter().GetResult();
            Need(unavailable.Proposal == null && unavailable.Audit.outcome == "provider-unavailable", "offline-provider-fallback-redacts-error-body");
            var late = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            fake.Handler = (c, token) => late.Task;
            var timeout = NpcProposalBroker.Request(fake, context, 40, CancellationToken.None).GetAwaiter().GetResult();
            Need(timeout.Proposal == null && timeout.Audit.outcome == "timeout", "timeout-even-if-provider-ignores-cancellation");
            late.SetResult(good);
            Need(timeout.Proposal == null && timeout.Audit.outcome == "timeout", "late-response-cannot-change-timed-out-result");
            using (var cancel = new CancellationTokenSource())
            {
                fake.Handler = (c, token) => new TaskCompletionSource<string>().Task;
                var work = NpcProposalBroker.Request(fake, context, 1000, cancel.Token); cancel.Cancel();
                var cancelled = work.GetAwaiter().GetResult();
                Need(cancelled.Proposal == null && cancelled.Audit.outcome == "cancelled", "cancellation-discards-inflight-proposal");
            }
            foreach (string endpoint in new[] { "https://example.com", "http://localhost:1234", "http://127.0.0.1:1234/private", "http://user:secret@127.0.0.1:1234" })
            {
                bool rejected = false;
                try { using (var p = new NpcLocalProposalProvider(endpoint, "explicit-model")) { } } catch (ArgumentException) { rejected = true; }
                Need(rejected, "endpoint-boundary-" + report.checks.Count);
            }
            var request = (Dictionary<string, object>)NpcBoundedJson.Parse(NpcLocalProposalProvider.BuildRequest(context, "explicit-model"), 16384);
            Need((string)request["model"] == "explicit-model" && (bool)request["stream"] == false && request.ContainsKey("response_format") && !request.ContainsKey("tools"),
                "adapter-request-explicit-model-structured-output-no-tools");
            bool normalRejectsDiagnostic = false;
            Need(!request.ContainsKey("reasoning_effort"), "ordinary-requests-preserve-model-reasoning-default");
            var noReasoning = (Dictionary<string, object>)NpcBoundedJson.Parse(NpcLocalProposalProvider.BuildRequest(context, "explicit-model", true), 16384);
            Need((string)noReasoning["reasoning_effort"] == "none" && Convert.ToInt64(noReasoning["max_tokens"]) == 256 && noReasoning.ContainsKey("response_format"),
                "diagnostic-reasoning-off-keeps-token-cap-and-schema");
            using (var guardedProvider = new NpcLocalProposalProvider("http://127.0.0.1:1234", "explicit-model"))
            {
                bool guardedOverride = false;
                try { guardedProvider.UseSingleDiagnosticReasoningOff(); } catch (InvalidOperationException) { guardedOverride = true; }
                Need(guardedOverride, "reasoning-override-requires-diagnostic-flags");
            }
            try { NpcProposalBroker.Request(fake, context, 30000, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (ArgumentOutOfRangeException) { normalRejectsDiagnostic = true; }
            Need(normalRejectsDiagnostic, "normal-provider-path-still-rejects-30-second-timeout");
            fake.Handler = (c, token) => Task.FromResult(good);
            var diagnostic = NpcProposalBroker.RequestDiagnostic(fake, context, 30000, CancellationToken.None).GetAwaiter().GetResult();
            Need(diagnostic.Proposal != null && diagnostic.Audit.schemaValid, "diagnostic-path-validates-the-same-proposal-schema");
            using (var cancel = new CancellationTokenSource())
            {
                fake.Handler = (c, token) => new TaskCompletionSource<string>().Task;
                var pendingDiagnostic = NpcProposalBroker.RequestDiagnostic(fake, context, 30000, cancel.Token); cancel.Cancel();
                Need(pendingDiagnostic.GetAwaiter().GetResult().Audit.outcome == "cancelled", "long-diagnostic-remains-cancellable");
            }
            var plannerObject = new GameObject("Diagnostic boundary validation");
            try
            {
                var planner = plannerObject.AddComponent<NpcOptionalPlanner>();
                Need(planner.TimeoutMilliseconds == 1500, "gameplay-default-remains-1500ms");
                bool guarded = false;
                try { planner.UseDiagnosticProbeDeadline(30000); } catch (InvalidOperationException) { guarded = true; }
                Need(guarded && planner.TimeoutMilliseconds == 1500, "diagnostic-cannot-be-enabled-without-smoke-flags");
            }
            finally { UnityEngine.Object.DestroyImmediate(plannerObject); }
            report.status = "PASS"; Directory.CreateDirectory("evidence/local/hybrid");
            const string thought = "\"Amber delivered.\"";
            Need(StarfallMemoryThought.Parse(thought, 1, out _, out _), "memory-thought-strict-valid-text");
            foreach (string bad in new[] { thought + "{}", "null", "1", "{}", "[]", "\"\"", "\"   \"", "\"hello\\nworld\"",
                "{\"execute\":\"move\"}", thought.Replace("Amber delivered.", new string('x', 65)), "Amber delivered.", "\"unterminated" })
                Need(!StarfallMemoryThought.Parse(bad, 1, out _, out _), "memory-thought-reject-" + report.checks.Count);
            Need(!StarfallMemoryThought.Parse(thought, 2, out _, out _), "memory-thought-unknown-local-protocol-context");
            Need(StarfallMemoryThought.DeadlineMilliseconds == 1500, "memory-thought-gameplay-deadline-unchanged");
            Need(StarfallLivingMemoryClient.HashEvent("{\"z\":2,\"a\":1}") == StarfallLivingMemoryClient.HashEvent("{\"a\":1,\"z\":2}"), "memory-event-hash-order-independent");
            var delivery = new StarfallLivingMemoryClient.Evidence { World = "starfall.integrated-coastal.v1", Actor = "inhabitant-01", Item = "amber", Target = "depot-west" };
            StarfallMemoryThought.Result Result(string text) => new StarfallMemoryThought.Result { schemaValid = true, reflection = text, milliseconds = 500 };
            Need(StarfallLivingMemoryRuntime.CanAdmitDeliveryThought(Result("Delivered amber"), delivery, true, true, true), "runtime-memory-admits-grounded-current-delivery");
            Need(!StarfallLivingMemoryRuntime.CanAdmitDeliveryThought(Result("Delivered amber"), delivery, false, true, true), "runtime-memory-rejects-paused-possessed-or-stopped");
            Need(!StarfallLivingMemoryRuntime.CanAdmitDeliveryThought(Result("Delivered amber"), delivery, true, false, true), "runtime-memory-rejects-stale-identity");
            Need(!StarfallLivingMemoryRuntime.CanAdmitDeliveryThought(Result("Delivered amber"), delivery, true, true, false), "runtime-memory-rejects-stale-registry-delivery");
            Need(!StarfallLivingMemoryRuntime.CanAdmitDeliveryThought(Result("Delivered blue"), delivery, true, true, true), "runtime-memory-rejects-wrong-item");
            Need(!StarfallLivingMemoryRuntime.CanAdmitDeliveryThought(Result("Collected amber"), delivery, true, true, true), "runtime-memory-rejects-wrong-action");
            Need(!StarfallLivingMemoryRuntime.CanAdmitDeliveryThought(Result("Delivered amber today"), delivery, true, true, true), "runtime-memory-rejects-extra-tokens");
            Need(!NpcProposalValidator.TryParse(good.Replace("I will collect the blue crystal.", ""), 7, out var empty, out _) && empty == null,
                "invalid-empty-proposal-cannot-escape-through-broker");
            File.WriteAllText("evidence/local/hybrid/validation.json", JsonUtility.ToJson(report, true));
            Debug.Log("NPC_HYBRID_VALIDATION_PASSED " + report.checks.Count);
        }
    }
}
