using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CityLife.World
{
    // Trusted engine transport; capabilities never enter the inference payload.
    public sealed class StarfallLivingMemoryClient : IDisposable
    {
        public sealed class Evidence
        {
            public string World, Actor, EventId, Item, Target, Summary, RecordJson;
            public int Tick;
        }
        private readonly HttpClient http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        private readonly Dictionary<string, object> config;
        private readonly Uri origin;
        public StarfallLivingMemoryClient(string path, string world, string actor, string build)
        {
            if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > 8192) throw new FormatException("memory-config-path");
            config = Map(File.ReadAllText(path), 8192);
            string[] keys = { "world_id", "inhabitant_id", "publisher_id", "build_id", "memory_endpoint", "publisher_token", "reader_token" };
            if (config.Count != keys.Length || keys.Any(k => !config.ContainsKey(k)) || (string)config["world_id"] != world ||
                (string)config["inhabitant_id"] != actor || (string)config["build_id"] != build || (string)config["publisher_id"] != "unity-local") throw new FormatException("memory-config-scope");
            origin = Loopback((string)config["memory_endpoint"]);
            foreach (string key in new[] { "publisher_token", "reader_token" })
                if (!(config[key] is string token) || token.Length < 40 || token.Length > 128 || token.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-')) throw new FormatException("memory-capability");
            if ((string)config["publisher_token"] == (string)config["reader_token"]) throw new FormatException("memory-distinct-capabilities");
        }
        public static Uri Loopback(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" ||
                uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0) throw new FormatException("loopback-origin-required");
            return uri;
        }
        public static Dictionary<string, object> Map(string text, int bound = 16384) => NpcBoundedJson.Parse(text, bound) as Dictionary<string, object> ?? throw new FormatException("object-required");
        public static string HashEvent(string text)
        {
            object Sorted(object value) => value is Dictionary<string, object> m ? new SortedDictionary<string, object>(m.ToDictionary(x => x.Key, x => Sorted(x.Value)), StringComparer.Ordinal) : value;
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(NpcBoundedJson.Encode(Sorted(Map(text)))))).Replace("-", "").ToLowerInvariant();
        }
        public static async Task<string> Send(HttpClient client, HttpRequestMessage request, CancellationToken token)
        {
            using (request)
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 16384) throw new IOException("response-bound");
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var body = new MemoryStream())
                {
                    byte[] bytes = new byte[1024]; int count;
                    while ((count = await stream.ReadAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false)) > 0)
                    { if (body.Length + count > 16384) throw new IOException("response-bound"); body.Write(bytes, 0, count); }
                    return new UTF8Encoding(false, true).GetString(body.ToArray());
                }
            }
        }
        private Task<string> Memory(string route, string body, string role, CancellationToken token)
        {
            var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, new Uri(origin, route));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (string)config[role]);
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return Send(http, request, token);
        }
        private Dictionary<string, object> ValidateEvent(string row)
        {
            if (Encoding.UTF8.GetByteCount(row) > 8192) throw new FormatException("event-bound");
            var parsed = Map(row); var source = (Dictionary<string, object>)parsed["source"];
            if ((string)parsed["world_id"] != (string)config["world_id"] || (string)parsed["inhabitant_id"] != (string)config["inhabitant_id"] ||
                (string)source["build_id"] != (string)config["build_id"] || (string)source["producer_id"] != "unity-local") throw new FormatException("event-scope");
            return parsed;
        }
        public async Task<string> PublishEvent(string row, CancellationToken token)
        {
            ValidateEvent(row);
            var receipt = Map(await Memory("v1/events", row, "publisher_token", token).ConfigureAwait(false));
            string eventId = HashEvent(row);
            if ((string)receipt["event_id"] != eventId) throw new FormatException("event-hash-mismatch");
            return eventId;
        }
        public async Task<Evidence> RetrieveConfirmedDelivery(string row, string eventId, CancellationToken token)
        {
            var last = ValidateEvent(row); var data = (Dictionary<string, object>)last["data"];
            if ((string)last["kind"] != "action_completed" || (string)data["action"] != "deliver" || (string)data["outcome"] != "delivered") throw new FormatException("delivery-required");
            if (eventId != HashEvent(row)) throw new FormatException("event-id-mismatch");
            var result = new Evidence { World = (string)last["world_id"], Actor = (string)last["inhabitant_id"], EventId = eventId,
                Tick = checked((int)(long)last["tick"]), Item = (string)data["item_id"], Target = (string)data["target_id"] };
            result.Summary = result.Actor + " delivered " + result.Item + " to " + result.Target + " at tick " + result.Tick + ".";
            var page = Map(await Memory("v1/memories?after=0&limit=4", null, "reader_token", token).ConfigureAwait(false));
            var items = (List<object>)page["items"];
            if ((bool)page["has_more"] || items.Count > 4) throw new FormatException("retrieval-bound");
            foreach (var item in items.Cast<Dictionary<string, object>>())
                if ((string)item["world_id"] != result.World || (string)item["inhabitant_id"] != result.Actor ||
                    (string)item["schema"] != "starfall.memory.record.v1" || (string)item["kind"] != "episode" || (string)item["epistemic_status"] != "confirmed_event") throw new FormatException("retrieval-scope");
            var matches = items.Cast<Dictionary<string, object>>().Where(x => ((List<object>)x["evidence_ids"]).Count == 1 && (string)((List<object>)x["evidence_ids"])[0] == eventId).ToArray();
            if (matches.Length != 1 || (string)matches[0]["summary"] != result.Summary || (long)matches[0]["tick"] != result.Tick) throw new FormatException("retrieved-delivery-mismatch");
            result.RecordJson = NpcBoundedJson.Encode(matches[0]); return result;
        }
        public async Task<Evidence> PublishAndRetrieve(string[] events, CancellationToken token)
        {
            if (events.Length != 3 || events.Any(x => Encoding.UTF8.GetByteCount(x) > 8192)) throw new FormatException("three-receipts-required");
            string eventId = null;
            foreach (string row in events)
                eventId = await PublishEvent(row, token).ConfigureAwait(false);
            return await RetrieveConfirmedDelivery(events[events.Length - 1], eventId, token).ConfigureAwait(false);
        }
        public void Dispose() => http.Dispose();
    }
}
