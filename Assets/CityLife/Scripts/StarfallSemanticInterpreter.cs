using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CityLife.World
{
    public enum SemanticActionKind
    {
        None = 0,
        GoToRiver = 1,
        CatchFish = 2,
        RoastCatch = 3,
        StoreFishInBasket = 4,
        EatCatch = 5,
        CatchThenEat = 6,
        CatchThenStore = 7,
        GoRiverThenCatch = 8,
        CatchThenRoast = 9,
        Cancel = 10,
        Rejected = 11
    }

    public struct SemanticInterpretationResult
    {
        public bool Success;
        public SemanticActionKind Action;
        public string ActionName;
        public string Reason;
        public string Source; // "model", "deterministic", "negation-filter", "cancelled", "timeout"
        public int Generation;
        public long Milliseconds;
    }

    /// <summary>
    /// Asynchronous bounded semantic interpreter for natural language inhabitant directives.
    /// Interacts with local loopback chat/completions endpoint (e.g. 127.0.0.1:1234) with
    /// realistic inference deadline (25.0s for local 26B model), strict JSON schema enforcement,
    /// deterministic negation rejection, exact canonical shortcut dispatch, and
    /// stale-response generation cancellation.
    ///
    /// Never falls back to loose substring contains matching on timeout or error;
    /// reports truthful timeout/rejection and supported shortcut guidance.
    /// </summary>
    public static class StarfallSemanticInterpreter
    {
        public const int DeadlineMilliseconds = 25000;

        private static readonly HashSet<string> NegationKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "not", "don't", "dont", "never", "refrain", "cannot", "cant", "can't", "mustn't", "must not", "shouldn't", "avoid"
        };

        private static readonly Dictionary<string, SemanticActionKind> ExactShortcuts = new Dictionary<string, SemanticActionKind>(StringComparer.OrdinalIgnoreCase)
        {
            // Cancel / Stop
            { "cancel", SemanticActionKind.Cancel },
            { "stop", SemanticActionKind.Cancel },
            { "halt", SemanticActionKind.Cancel },
            { "abort", SemanticActionKind.Cancel },

            // Single Actions
            { "go to river", SemanticActionKind.GoToRiver },
            { "go river", SemanticActionKind.GoToRiver },
            { "river", SemanticActionKind.GoToRiver },
            { "river bank", SemanticActionKind.GoToRiver },

            { "go fish", SemanticActionKind.CatchFish },
            { "go fishing", SemanticActionKind.CatchFish },
            { "fishing", SemanticActionKind.CatchFish },
            { "catch fish", SemanticActionKind.CatchFish },
            { "catch a fish", SemanticActionKind.CatchFish },
            { "fish", SemanticActionKind.CatchFish },

            { "roast catch", SemanticActionKind.RoastCatch },
            { "roast fish", SemanticActionKind.RoastCatch },
            { "roast", SemanticActionKind.RoastCatch },
            { "cook catch", SemanticActionKind.RoastCatch },

            { "store catch", SemanticActionKind.StoreFishInBasket },
            { "store fish", SemanticActionKind.StoreFishInBasket },
            { "store fish in basket", SemanticActionKind.StoreFishInBasket },
            { "store catch in basket", SemanticActionKind.StoreFishInBasket },
            { "store in basket", SemanticActionKind.StoreFishInBasket },
            { "store", SemanticActionKind.StoreFishInBasket },

            { "eat catch", SemanticActionKind.EatCatch },
            { "eat fish", SemanticActionKind.EatCatch },
            { "eat roasted catch", SemanticActionKind.EatCatch },
            { "eat", SemanticActionKind.EatCatch },

            // Chained Actions
            { "go river then catch", SemanticActionKind.GoRiverThenCatch },
            { "go to river then catch", SemanticActionKind.GoRiverThenCatch },
            { "go river and catch", SemanticActionKind.GoRiverThenCatch },
            { "go to river and catch", SemanticActionKind.GoRiverThenCatch },
            { "go to river and catch a fish", SemanticActionKind.GoRiverThenCatch },
            { "go fish and catch", SemanticActionKind.CatchFish },
            { "go fishing and catch", SemanticActionKind.CatchFish },

            { "catch then roast", SemanticActionKind.CatchThenRoast },
            { "catch fish then roast", SemanticActionKind.CatchThenRoast },
            { "catch and roast", SemanticActionKind.CatchThenRoast },
            { "catch a fish then roast", SemanticActionKind.CatchThenRoast },
            { "go fish then roast", SemanticActionKind.CatchThenRoast },
            { "go fishing then roast", SemanticActionKind.CatchThenRoast },
            { "go fish and roast", SemanticActionKind.CatchThenRoast },
            { "go fishing and roast", SemanticActionKind.CatchThenRoast },

            { "catch then eat", SemanticActionKind.CatchThenEat },
            { "catch fish then eat", SemanticActionKind.CatchThenEat },
            { "catch and eat", SemanticActionKind.CatchThenEat },
            { "catch a fish then eat", SemanticActionKind.CatchThenEat },
            { "go fish then eat", SemanticActionKind.CatchThenEat },
            { "go fishing then eat", SemanticActionKind.CatchThenEat },
            { "go fish and eat", SemanticActionKind.CatchThenEat },
            { "go fishing and eat", SemanticActionKind.CatchThenEat },

            { "catch then store", SemanticActionKind.CatchThenStore },
            { "catch fish then store", SemanticActionKind.CatchThenStore },
            { "catch and store", SemanticActionKind.CatchThenStore },
            { "catch a fish then store", SemanticActionKind.CatchThenStore },
            { "go fish then store", SemanticActionKind.CatchThenStore },
            { "go fishing then store", SemanticActionKind.CatchThenStore },
            { "go fish and store", SemanticActionKind.CatchThenStore },
            { "go fishing and store", SemanticActionKind.CatchThenStore }
        };

        /// <summary>
        /// Escapes a raw string into strict valid JSON format.
        /// </summary>
        public static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ') sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Evaluates deterministic exact shortcuts and strict negation rejection.
        /// Never uses loose substring matching.
        /// </summary>
        public static SemanticInterpretationResult InterpretDeterministic(string rawText, int generation = 0)
        {
            var res = new SemanticInterpretationResult
            {
                Generation = generation,
                Source = "deterministic"
            };

            if (string.IsNullOrWhiteSpace(rawText))
            {
                res.Success = false;
                res.Action = SemanticActionKind.Rejected;
                res.ActionName = "rejected";
                res.Reason = "empty-command";
                return res;
            }

            string text = rawText.Trim().ToLowerInvariant();

            // 1. Strict Negation Detection: 'do not eat fish', 'don't catch', 'never roast'
            string[] words = text.Split(new[] { ' ', ',', '.', ';', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (NegationKeywords.Contains(words[i]))
                {
                    res.Success = false;
                    res.Action = SemanticActionKind.Rejected;
                    res.ActionName = "rejected";
                    res.Reason = "negation-not-supported: " + words[i];
                    res.Source = "negation-filter";
                    return res;
                }
            }

            // Negation prefix phrases like "stop doing that", "stop eating"
            if (text.StartsWith("stop ") && text != "stop")
            {
                res.Success = false;
                res.Action = SemanticActionKind.Rejected;
                res.ActionName = "rejected";
                res.Reason = "negation-not-supported: stop-directive";
                res.Source = "negation-filter";
                return res;
            }

            // 2. Exact Canonical Shortcut Matching
            if (ExactShortcuts.TryGetValue(text, out var action))
            {
                res.Success = true;
                res.Action = action;
                res.ActionName = ActionToSchemaName(action);
                res.Reason = "exact-shortcut: " + text;
                res.Source = "deterministic";
                return res;
            }

            // 3. Not an exact shortcut: mark as requiring model interpretation
            res.Success = false;
            res.Action = SemanticActionKind.None;
            res.ActionName = "none";
            res.Reason = "not-exact-shortcut";
            return res;
        }

        /// <summary>
        /// Asynchronously interprets directive via local loopback endpoint.
        /// Never falls back to loose contains matching on timeout or error.
        /// </summary>
        public static async Task<SemanticInterpretationResult> InterpretAsync(
            string rawText,
            string endpoint,
            string model,
            int generation,
            CancellationToken cancellationToken)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();

            // Check cancellation before any work or fast-path
            if (cancellationToken.IsCancellationRequested)
            {
                return new SemanticInterpretationResult
                {
                    Success = false,
                    Action = SemanticActionKind.Rejected,
                    ActionName = "cancelled",
                    Reason = "cancelled-stale-generation",
                    Source = "cancelled",
                    Generation = generation,
                    Milliseconds = timer.ElapsedMilliseconds
                };
            }

            // 1. Fast deterministic & negation check
            var fastCheck = InterpretDeterministic(rawText, generation);
            if (fastCheck.Action == SemanticActionKind.Rejected && fastCheck.Source == "negation-filter")
            {
                fastCheck.Milliseconds = timer.ElapsedMilliseconds;
                return fastCheck;
            }
            if (fastCheck.Success && fastCheck.Action != SemanticActionKind.None)
            {
                fastCheck.Milliseconds = timer.ElapsedMilliseconds;
                return fastCheck;
            }

            // 2. If no valid endpoint provided, report truthful unavailable error
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return new SemanticInterpretationResult
                {
                    Success = false,
                    Action = SemanticActionKind.Rejected,
                    ActionName = "rejected",
                    Reason = "endpoint-unavailable: use documented shortcut (e.g. 'go fish', 'go river', 'eat catch', 'store catch')",
                    Source = "endpoint-unavailable",
                    Generation = generation,
                    Milliseconds = timer.ElapsedMilliseconds
                };
            }

            Uri origin;
            try
            {
                origin = StarfallLivingMemoryClient.Loopback(endpoint);
            }
            catch (Exception ex)
            {
                return new SemanticInterpretationResult
                {
                    Success = false,
                    Action = SemanticActionKind.Rejected,
                    ActionName = "rejected",
                    Reason = "endpoint-invalid: " + ex.Message,
                    Source = "endpoint-invalid",
                    Generation = generation,
                    Milliseconds = timer.ElapsedMilliseconds
                };
            }

            string requestJson = BuildModelPrompt(rawText, model);

            using (var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan })
            using (var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                try
                {
                    var msg = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "v1/chat/completions"))
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                    };

                    Task<string> sendTask = StarfallLivingMemoryClient.Send(http, msg, localCts.Token);
                    Task timeoutTask = Task.Delay(DeadlineMilliseconds, localCts.Token);

                    if (await Task.WhenAny(sendTask, timeoutTask).ConfigureAwait(false) != sendTask)
                    {
                        // Endpoint timed out: check if externally cancelled
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return new SemanticInterpretationResult
                            {
                                Success = false,
                                Action = SemanticActionKind.Rejected,
                                ActionName = "cancelled",
                                Reason = "cancelled-stale-generation",
                                Source = "cancelled",
                                Generation = generation,
                                Milliseconds = timer.ElapsedMilliseconds
                            };
                        }

                        localCts.Cancel();
                        // Report truthful timeout error without arbitrary prose fallback!
                        return new SemanticInterpretationResult
                        {
                            Success = false,
                            Action = SemanticActionKind.Rejected,
                            ActionName = "timeout",
                            Reason = "endpoint-timeout: retry or use documented shortcut (e.g. 'go fish', 'go river', 'eat catch', 'store catch')",
                            Source = "timeout",
                            Generation = generation,
                            Milliseconds = timer.ElapsedMilliseconds
                        };
                    }

                    string responseJson = await sendTask.ConfigureAwait(false);
                    var body = StarfallLivingMemoryClient.Map(responseJson);

                    var choices = body["choices"] as List<object>;
                    if (choices != null && choices.Count > 0)
                    {
                        var choice = choices[0] as Dictionary<string, object>;
                        string finishReason = choice != null ? choice["finish_reason"] as string : null;
                        var messageObj = choice != null ? choice["message"] as Dictionary<string, object> : null;
                        string content = messageObj != null ? messageObj["content"] as string : null;

                        if (finishReason == "length")
                        {
                            return new SemanticInterpretationResult
                            {
                                Success = false,
                                Action = SemanticActionKind.Rejected,
                                ActionName = "rejected",
                                Reason = "model-response-truncated",
                                Source = "model-truncated",
                                Generation = generation,
                                Milliseconds = timer.ElapsedMilliseconds
                            };
                        }

                        if (finishReason == "stop" && !string.IsNullOrWhiteSpace(content))
                        {
                            if (TryParseModelJson(content, out var actionRes, out string explanation))
                            {
                                bool isSuccess = actionRes != SemanticActionKind.Rejected && actionRes != SemanticActionKind.None;
                                return new SemanticInterpretationResult
                                {
                                    Success = isSuccess,
                                    Action = actionRes,
                                    ActionName = ActionToSchemaName(actionRes),
                                    Reason = explanation ?? (isSuccess ? "classified-by-model" : "unsupported-intent-rejected"),
                                    Source = "model",
                                    Generation = generation,
                                    Milliseconds = timer.ElapsedMilliseconds
                                };
                            }
                        }
                    }

                    return new SemanticInterpretationResult
                    {
                        Success = false,
                        Action = SemanticActionKind.Rejected,
                        ActionName = "rejected",
                        Reason = "model-output-invalid-schema",
                        Source = "model-schema-error",
                        Generation = generation,
                        Milliseconds = timer.ElapsedMilliseconds
                    };
                }
                catch (OperationCanceledException)
                {
                    return new SemanticInterpretationResult
                    {
                        Success = false,
                        Action = SemanticActionKind.Rejected,
                        ActionName = "cancelled",
                        Reason = "cancelled-stale-generation",
                        Source = "cancelled",
                        Generation = generation,
                        Milliseconds = timer.ElapsedMilliseconds
                    };
                }
                catch (Exception ex)
                {
                    return new SemanticInterpretationResult
                    {
                        Success = false,
                        Action = SemanticActionKind.Rejected,
                        ActionName = "rejected",
                        Reason = "endpoint-connection-error: " + ex.Message,
                        Source = "endpoint-error",
                        Generation = generation,
                        Milliseconds = timer.ElapsedMilliseconds
                    };
                }
            }
        }

        private static string BuildModelPrompt(string rawText, string model)
        {
            string systemPrompt = "You are a gameplay command interpreter for an autonomous inhabitant in a survival game. " +
                                  "Classify the player's natural language directive into exact JSON with keys 'action' and 'reason'. " +
                                  "Allowed 'action' values: " +
                                  "'go-to-river', 'catch-fish', 'roast-catch', 'store-fish-in-basket', 'eat-catch', " +
                                  "'catch-then-eat', 'catch-then-store', 'go-river-then-catch', 'catch-then-roast', 'cancel', 'rejected'. " +
                                  "Negations (e.g. 'do not eat', 'don't fish') MUST be classified as 'rejected'. " +
                                  "Unsupported requests (e.g. 'build shelter', 'look at fish', 'sing song') MUST be classified as 'rejected'. " +
                                  "Output ONLY valid JSON.";

            string escapedSystem = EscapeJsonString(systemPrompt);
            string escapedInput = EscapeJsonString(rawText);
            string modelId = !string.IsNullOrEmpty(model) ? EscapeJsonString(model) : "google/gemma-4-26b-a4b-qat";

            return "{" +
                   $"\"model\":\"{modelId}\"," +
                   "\"messages\":[" +
                   $"{{\"role\":\"system\",\"content\":\"{escapedSystem}\"}}," +
                   $"{{\"role\":\"user\",\"content\":\"Directive: {escapedInput}\"}}" +
                   "]," +
                   "\"temperature\":0.0," +
                   "\"max_tokens\":256," +
                   "\"reasoning_effort\":\"none\"" +
                   "}";
        }

        private static bool TryParseModelJson(string content, out SemanticActionKind action, out string explanation)
        {
            action = SemanticActionKind.Rejected;
            explanation = null;

            if (string.IsNullOrWhiteSpace(content)) return false;
            string trimmed = content.Trim();
            if (!trimmed.StartsWith("{") || !trimmed.EndsWith("}")) return false;

            try
            {
                var dict = StarfallLivingMemoryClient.Map(trimmed);
                if (dict == null) return false;

                // Strict schema: only 'action' and 'reason' keys allowed
                foreach (var key in dict.Keys)
                {
                    if (key != "action" && key != "reason") return false;
                }

                if (!dict.TryGetValue("action", out var actionObj) || !(actionObj is string actStr) || string.IsNullOrWhiteSpace(actStr))
                    return false;

                if (!dict.TryGetValue("reason", out var reasonObj) || !(reasonObj is string rStr) || string.IsNullOrWhiteSpace(rStr))
                    return false;

                explanation = rStr;

                switch (actStr.Trim().ToLowerInvariant())
                {
                    case "go-to-river": action = SemanticActionKind.GoToRiver; return true;
                    case "catch-fish": action = SemanticActionKind.CatchFish; return true;
                    case "roast-catch": action = SemanticActionKind.RoastCatch; return true;
                    case "store-fish-in-basket": action = SemanticActionKind.StoreFishInBasket; return true;
                    case "eat-catch": action = SemanticActionKind.EatCatch; return true;
                    case "catch-then-eat": action = SemanticActionKind.CatchThenEat; return true;
                    case "catch-then-store": action = SemanticActionKind.CatchThenStore; return true;
                    case "go-river-then-catch": action = SemanticActionKind.GoRiverThenCatch; return true;
                    case "catch-then-roast": action = SemanticActionKind.CatchThenRoast; return true;
                    case "cancel": action = SemanticActionKind.Cancel; return true;
                    case "rejected": action = SemanticActionKind.Rejected; return true;
                    case "unsupported": action = SemanticActionKind.Rejected; return true;
                    default: return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string ActionToSchemaName(SemanticActionKind action)
        {
            switch (action)
            {
                case SemanticActionKind.GoToRiver: return "go-to-river";
                case SemanticActionKind.CatchFish: return "catch-fish";
                case SemanticActionKind.RoastCatch: return "roast-catch";
                case SemanticActionKind.StoreFishInBasket: return "store-fish-in-basket";
                case SemanticActionKind.EatCatch: return "eat-catch";
                case SemanticActionKind.CatchThenEat: return "catch-then-eat";
                case SemanticActionKind.CatchThenStore: return "catch-then-store";
                case SemanticActionKind.GoRiverThenCatch: return "go-river-then-catch";
                case SemanticActionKind.CatchThenRoast: return "catch-then-roast";
                case SemanticActionKind.Cancel: return "cancel";
                default: return "rejected";
            }
        }
    }
}
