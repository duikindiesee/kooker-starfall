using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CityLife.World
{
    // Reflection-only protocol v3: two bounded plain-text words; correlation stays in the request closure.
    // This request never controls an action; the existing 1500 ms gameplay deadline remains unchanged.
    public static class StarfallMemoryThought
    {
        public const int DeadlineMilliseconds = NpcOptionalPlanner.DefaultGameplayTimeoutMilliseconds;
        [Serializable] public sealed class Result
        {
            public string status = "fallback", rawAnswer, dialogue, reflection, requestJson, responseJson, inventoryJson,
                finishReason, eventId, world, actor, model;
            public long milliseconds, inventoryMilliseconds, completionMilliseconds;
            public bool schemaValid, rawReceived;
            public int completionAttempts;
        }
        public static bool Parse(string raw, int requestId, out string dialogue, out string reflection)
        {
            dialogue = reflection = null;
            try
            {
                if (requestId != 1 || string.IsNullOrWhiteSpace(raw) || raw.Length > 64 || !raw.All(c => c >= 32 && c <= 126)) return false;
                string thought = raw.Trim();
                string[] words = thought.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length != 2 || !string.Equals(words[0], "Delivered", StringComparison.OrdinalIgnoreCase) ||
                    words.Any(word => !word.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))) return false;
                dialogue = reflection = thought; return true;
            }
            catch (Exception) { return false; }
        }
        public static string BuildRequest(StarfallLivingMemoryClient.Evidence memory, string model, int requestId)
        {
            if (requestId != 1) throw new ArgumentOutOfRangeException(nameof(requestId));
            return NpcBoundedJson.Encode(new Dictionary<string, object> {
                ["model"] = model, ["stream"] = false, ["temperature"] = 0, ["max_tokens"] = 8, ["reasoning_effort"] = "none",
                ["messages"] = new object[] {
                    new Dictionary<string, object> { ["role"] = "system", ["content"] = "Output exactly two plain words and nothing else. First word Delivered. Second word is the delivered item named in verified memory. No quotes, braces, punctuation, explanation, goals, or commands." },
                    // Only the item from the already verified delivery enters the
                    // tiny reflection prompt. World/actor/event bindings remain in
                    // the trusted closure and are rechecked before display.
                    new Dictionary<string, object> { ["role"] = "user", ["content"] = "Verified delivery item: " + memory.Item + "." } } });
        }
        public static async Task<Result> Request(StarfallLivingMemoryClient.Evidence memory, string endpoint, string model, CancellationToken cancellation)
        {
            var result = new Result { eventId = memory.EventId, world = memory.World, actor = memory.Actor, model = model, requestJson = BuildRequest(memory, model, 1) };
            var origin = StarfallLivingMemoryClient.Loopback(endpoint);
            using (var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan })
            using (var local = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                var timer = Stopwatch.StartNew();
                async Task<string> Work()
                {
                    result.inventoryJson = await StarfallLivingMemoryClient.Send(http, new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "api/v0/models")), local.Token).ConfigureAwait(false);
                    var inventory = StarfallLivingMemoryClient.Map(result.inventoryJson);
                    result.inventoryMilliseconds = timer.ElapsedMilliseconds;
                    if (!((List<object>)inventory["data"]).Cast<Dictionary<string, object>>().Any(x => (string)x["id"] == model && (string)x["state"] == "loaded")) throw new FormatException("model-not-loaded");
                    local.Token.ThrowIfCancellationRequested();
                    var message = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "v1/chat/completions")) { Content = new StringContent(result.requestJson, Encoding.UTF8, "application/json") };
                    result.completionAttempts++;
                    result.responseJson = await StarfallLivingMemoryClient.Send(http, message, local.Token).ConfigureAwait(false);
                    var body = StarfallLivingMemoryClient.Map(result.responseJson);
                    if(!NpcBoundedJson.ExactCompletionModel(body,model))
                        throw new FormatException("response-model-mismatch");
                    result.completionMilliseconds = timer.ElapsedMilliseconds - result.inventoryMilliseconds;
                    var choices = (List<object>)body["choices"];
                    if (choices.Count != 1) throw new FormatException("single-choice-required");
                    var choice = (Dictionary<string, object>)choices[0];
                    result.finishReason = (string)choice["finish_reason"];
                    if (result.finishReason != "stop") throw new FormatException("incomplete-answer");
                    return (string)((Dictionary<string, object>)choice["message"])["content"];
                }
                Task<string> work = null;
                try
                {
                    cancellation.ThrowIfCancellationRequested(); work = Work();
                    if (await Task.WhenAny(work, Task.Delay(DeadlineMilliseconds, local.Token)).ConfigureAwait(false) != work) result.status = cancellation.IsCancellationRequested ? "cancelled" : "timeout";
                    else
                    {
                        result.rawAnswer = await work.ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
                        result.rawReceived = result.rawAnswer != null; result.schemaValid = Parse(result.rawAnswer, 1, out result.dialogue, out result.reflection);
                        result.status = result.schemaValid ? "parsed-awaiting-live-admission" : "schema-rejected";
                    }
                }
                catch (OperationCanceledException) { result.status = "cancelled"; }
                catch (Exception) { result.status = "provider-unavailable"; }
                finally
                {
                    local.Cancel(); result.milliseconds = timer.ElapsedMilliseconds;
                    if (result.milliseconds > DeadlineMilliseconds && result.schemaValid) { result.schemaValid = false; result.status = "timeout"; }
                    if (work != null) _ = work.ContinueWith(t => { var observed = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
            return result;
        }
    }
}
