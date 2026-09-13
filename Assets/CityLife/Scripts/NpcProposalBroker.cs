using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CityLife.World
{
    public interface INpcProposalProvider
    {
        string Name { get; }
        Task<string> ProposeAsync(NpcProposalContext context, CancellationToken cancellation);
    }
    [Serializable] public sealed class NpcProposalAudit
    {
        public int requestId, tick;
        public string provider, outcome, proposedGoal, proposedTarget, dialogue, reflection;
        public string cargo, lastOutcome;
        public string[] eligibleIds, plan;
        public long milliseconds;
        public bool schemaValid;
        public bool rawReplyReceived;
        public int replyBytes;
    }
    public sealed class NpcProposalResult
    {
        public NpcProposal Proposal;
        public NpcProposalAudit Audit;
    }
    public static class NpcProposalBroker
    {
        // Timeout wins even when a provider ignores cancellation. A late reply has no continuation into the world.
        public static Task<NpcProposalResult> Request(INpcProposalProvider provider, NpcProposalContext context,
            int timeoutMilliseconds, CancellationToken cancellation)
            => RequestCore(provider, context, timeoutMilliseconds, 5000, cancellation);

        // Separate diagnostic entry point; ordinary gameplay requests retain their existing bound.
        public static Task<NpcProposalResult> RequestDiagnostic(INpcProposalProvider provider, NpcProposalContext context,
            int timeoutMilliseconds, CancellationToken cancellation)
        {
            if (timeoutMilliseconds != 5000 && timeoutMilliseconds != 30000) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            return RequestCore(provider, context, timeoutMilliseconds, 30000, cancellation);
        }
        private static async Task<NpcProposalResult> RequestCore(INpcProposalProvider provider, NpcProposalContext context,
            int timeoutMilliseconds, int maximumMilliseconds, CancellationToken cancellation)
        {
            if (timeoutMilliseconds < 20 || timeoutMilliseconds > maximumMilliseconds) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            var audit = new NpcProposalAudit { requestId = context.RequestId, tick = context.Tick, provider = provider.Name,
                cargo = context.Cargo, lastOutcome = context.LastOutcome, eligibleIds = Array.ConvertAll(context.Eligible, x => x.id) };
            var result = new NpcProposalResult { Audit = audit }; var timer = Stopwatch.StartNew();
            using (var local = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                Task<string> work = null;
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    work = provider.ProposeAsync(context, local.Token);
                    var deadline = Task.Delay(timeoutMilliseconds, local.Token);
                    if (await Task.WhenAny(work, deadline).ConfigureAwait(false) != work)
                    { audit.outcome = cancellation.IsCancellationRequested ? "cancelled" : "timeout"; }
                    else
                    {
                        string raw = await work.ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
                        audit.rawReplyReceived = raw != null; audit.replyBytes = raw == null ? 0 : System.Text.Encoding.UTF8.GetByteCount(raw);
                        audit.schemaValid = NpcProposalValidator.TryParse(raw, context.RequestId, out var parsed, out string code);
                        audit.outcome = code; result.Proposal = parsed;
                        if (parsed != null) { audit.proposedGoal = parsed.goal; audit.proposedTarget = parsed.target_id; audit.dialogue = parsed.dialogue; audit.reflection = parsed.reflection; audit.plan = parsed.plan; }
                    }
                }
                catch (OperationCanceledException) { audit.outcome = "cancelled"; }
                catch (Exception) { audit.outcome = "provider-unavailable"; } // Never log arbitrary response bodies or credentials.
                finally
                {
                    local.Cancel(); audit.milliseconds = timer.ElapsedMilliseconds;
                    if (work != null) _ = work.ContinueWith(t => { var observed = t.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
            return result;
        }
    }
}
