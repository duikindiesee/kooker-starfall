using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

namespace CityLife.World
{
    // Trusted engine transport; capabilities never enter the inference payload.
    public sealed class StarfallLivingMemoryClient : IDisposable
    {
        public const int MaximumMemoryPages = 64;
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
            long after = 0;
            for (int pageNumber = 0; pageNumber < MaximumMemoryPages; pageNumber++)
            {
                var page = await MemoryPage(after, token).ConfigureAwait(false);
                var match = page.items.Where(x => ((List<object>)x["evidence_ids"])[0] as string == eventId).ToArray();
                if (match.Length == 1)
                {
                    if ((string)match[0]["summary"] != result.Summary || (long)match[0]["tick"] != result.Tick) throw new FormatException("retrieved-delivery-mismatch");
                    result.RecordJson = NpcBoundedJson.Encode(match[0]); return result;
                }
                if (match.Length > 1) throw new FormatException("retrieved-delivery-mismatch");
                if (!page.more) break;
                after = page.after;
            }
            throw new FormatException("retrieved-delivery-mismatch-or-page-bound");
        }
        private async Task<(Dictionary<string, object>[] items, bool more, long after)> MemoryPage(long after, CancellationToken token)
        {
            string route = "v1/memories?after=" + after.ToString(CultureInfo.InvariantCulture) + "&limit=4";
            var page = Map(await Memory(route, null, "reader_token", token).ConfigureAwait(false));
            var items = ((List<object>)page["items"]).Cast<Dictionary<string, object>>().ToArray();
            if (items.Length > 4) throw new FormatException("retrieval-bound");
            foreach (var item in items)
                if ((string)item["world_id"] != (string)config["world_id"] || (string)item["inhabitant_id"] != (string)config["inhabitant_id"] ||
                    (string)item["schema"] != "starfall.memory.record.v1" || (string)item["kind"] != "episode" || (string)item["epistemic_status"] != "confirmed_event" ||
                    ((List<object>)item["evidence_ids"]).Count != 1) throw new FormatException("retrieval-scope");
            long next = (long)page["next_after"];
            if (next < after || ((bool)page["has_more"] && (items.Length == 0 || next == after))) throw new FormatException("retrieval-cursor");
            return (items, (bool)page["has_more"], next);
        }
        public async Task<Evidence> RecallLatestConfirmedDelivery(CancellationToken token)
        {
            Dictionary<string, object> latest = null; long after = 0;
            for (int pageNumber = 0; pageNumber < MaximumMemoryPages; pageNumber++)
            {
                var page = await MemoryPage(after, token).ConfigureAwait(false);
                if (page.items.Length > 0) latest = page.items[page.items.Length - 1];
                if (!page.more)
                {
                    if (latest == null) return null;
                    var refs = (List<object>)latest["evidence_ids"];
                    return new Evidence { World = (string)latest["world_id"], Actor = (string)latest["inhabitant_id"], EventId = (string)refs[0],
                        Tick = checked((int)(long)latest["tick"]), Summary = (string)latest["summary"], RecordJson = NpcBoundedJson.Encode(latest) };
                }
                after = page.after;
            }
            throw new FormatException("retrieval-page-bound");
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
