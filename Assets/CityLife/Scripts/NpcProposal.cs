using System;
using System.Collections.Generic;
using System.Linq;

namespace CityLife.World
{
    [Serializable] public sealed class NpcProposal
    {
        public int version, request_id;
        public string goal, target_id, dialogue, reflection;
        public string[] plan;
    }
    public sealed class NpcProposalContext
    {
        public int RequestId, Tick;
        public string Cargo;
        public NpcObservation[] Eligible;
        public string LastOutcome;
    }
    public static class NpcProposalValidator
    {
        private static readonly string[] Fields = { "version", "request_id", "goal", "target_id", "plan", "dialogue", "reflection" };
        public static bool TryParse(string raw, int requestId, out NpcProposal proposal, out string code)
        {
            proposal = null;
            try
            {
                var map = NpcBoundedJson.Parse(raw) as Dictionary<string, object>;
                if (map == null || map.Count != Fields.Length || Fields.Any(x => !map.ContainsKey(x))) throw new FormatException("schema-fields");
                if (!(map["version"] is long version) || version != 1 || !(map["request_id"] is long id) || id != requestId || id < 1) throw new FormatException("schema-version-or-request");
                string Text(string key, int max)
                {
                    if (!(map[key] is string value) || value.Length > max || value.Any(c => c < 32 || c > 126)) throw new FormatException("schema-text-" + key);
                    return value;
                }
                string goal = Text("goal", 16), target = Text("target_id", 64);
                if (goal != "collect" && goal != "deliver" && goal != "wait") throw new FormatException("schema-goal");
                if (target.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_'))) throw new FormatException("schema-target");
                if ((goal == "wait") != (target.Length == 0)) throw new FormatException("schema-target-required");
                if (!(map["plan"] is List<object> steps) || steps.Count < 1 || steps.Count > 3 ||
                    steps.Any(x => !(x is string s) || (s != "observe" && s != "collect" && s != "deliver" && s != "wait"))) throw new FormatException("schema-plan");
                proposal = new NpcProposal { version = 1, request_id = requestId, goal = goal, target_id = target,
                    plan = steps.Cast<string>().ToArray(), dialogue = Text("dialogue", 160), reflection = Text("reflection", 160) };
                if (string.IsNullOrWhiteSpace(proposal.dialogue) || string.IsNullOrWhiteSpace(proposal.reflection))
                    throw new FormatException("schema-empty-thought-text");
                code = "schema-valid"; return true;
            }
            catch (FormatException error) { code = error.Message; return false; }
        }
        // Rechecked at consumption against fresh perception, cargo and cooldown. Never invokes an action API.
        public static bool ValidateLive(NpcProposal proposal, NpcProposalContext requested, IReadOnlyList<NpcObservation> current,
            string cargo, IReadOnlyDictionary<string, int> retry, int tick, out string code)
        {
            code = "stale-context";
            if (proposal == null || proposal.request_id != requested.RequestId || tick < requested.Tick || tick - requested.Tick > 250 || cargo != requested.Cargo) return false;
            if (proposal.goal == "wait") { code = "accepted-bounded-wait"; return true; }
            if (proposal.goal != (string.IsNullOrEmpty(cargo) ? "collect" : "deliver")) { code = "cargo-goal-mismatch"; return false; }
            if (!requested.Eligible.Any(x => x.id == proposal.target_id)) { code = "target-not-in-request"; return false; }
            var target = current.FirstOrDefault(x => x.id == proposal.target_id);
            if (target == null || !target.permission || !target.available || target.kind != (string.IsNullOrEmpty(cargo) ? NpcObjectKind.Item : NpcObjectKind.Destination) ||
                tick - target.seenAtTick > 10 || (retry.TryGetValue(target.id, out int until) && until > tick))
            { code = "target-no-longer-eligible"; return false; }
            code = "accepted-high-level-goal"; return true;
        }
    }
}
