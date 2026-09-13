using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CityLife.World
{
    // Explicit export adapter for trusted Unity code. Never attached to a scene automatically.
    // No HTTP, personal archive access, model callback, or game-command deserialization.
    public sealed class StarfallMemoryExport : IDisposable
    {
        [Serializable] private sealed class Source
        { public string producer_id, session_id, build_id; public int sequence; }
        [Serializable] private sealed class Envelope
        {
            public string schema = "starfall.event.v1", world_id, inhabitant_id;
            public Source source;
            public int tick;
            public string kind;
        }
        [Serializable] private sealed class Identity { public string display_name; }
        [Serializable] private sealed class ActionData
        { public string action, target_id, item_id, outcome; public int request_id; }
        private readonly StreamWriter writer;
        private readonly FileStream file;
        private readonly string world, producer, session, build;
        private int sequence, lastTick;
        public int Count => sequence;

        public StarfallMemoryExport(string newPath, string worldId, string producerId, string sessionId, string buildId)
        {
            world = Id(worldId); producer = Id(producerId); session = Id(sessionId); build = Id(buildId);
            if (!Path.IsPathRooted(newPath)) throw new ArgumentException("An explicit absolute new export path is required.");
            file = new FileStream(newPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            writer = new StreamWriter(file, new UTF8Encoding(false));
        }

        private static string Id(string value)
        {
            if (value == null || !Regex.IsMatch(value, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,95}\z"))
                throw new ArgumentException("Invalid Starfall identifier.");
            return value;
        }

        private void Append(string actor, int tick, string kind, string data)
        {
            Id(actor);
            if (tick < lastTick || tick < 0) throw new ArgumentException("Source ticks must not regress.");
            var envelope = new Envelope { world_id = world, inhabitant_id = actor, tick = tick, kind = kind,
                source = new Source { producer_id = producer, session_id = session, build_id = build, sequence = sequence + 1 } };
            string json = JsonUtility.ToJson(envelope);
            writer.WriteLine(json.Substring(0, json.Length - 1) + ",\"data\":" + data + "}");
            writer.Flush(); file.Flush(true);
            sequence++; lastTick = tick;
        }

        public void RegisterIdentity(string actor, string displayName, int tick)
        {
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 80) throw new ArgumentException("Invalid display name.");
            foreach (char c in displayName) if (char.IsControl(c)) throw new ArgumentException("Control character in display name.");
            Append(actor, tick, "identity_registered", JsonUtility.ToJson(new Identity { display_name = displayName }));
        }

        // Call only with the receipt just returned by NpcActionApi.Execute and the carried item snapshot.
        // This records facts about completed actions; it has no authority to perform an action.
        public bool CompletedAction(string actor, int tick, int requestId, NpcActionKind action,
            string targetId, string itemId, NpcActionResult receipt)
        {
            if (!receipt.success || receipt.duplicate) return false;
            if (requestId <= 0 || (action != NpcActionKind.Pickup && action != NpcActionKind.Deliver))
                throw new ArgumentException("Invalid completed action.");
            string outcome = action == NpcActionKind.Pickup ? "picked-up" : "delivered";
            if (receipt.code != outcome) throw new ArgumentException("Receipt outcome mismatch.");
            Id(targetId); Id(itemId);
            if (action == NpcActionKind.Pickup && targetId != itemId) throw new ArgumentException("Pickup item mismatch.");
            Append(actor, tick, "action_completed", JsonUtility.ToJson(new ActionData {
                action = action == NpcActionKind.Pickup ? "pickup" : "deliver", target_id = targetId,
                item_id = itemId, request_id = requestId, outcome = outcome }));
            return true;
        }

        // A future sleep controller owns this signal. Model text must never call this adapter.
        public void SleepTransition(string actor, int tick, bool asleep) =>
            Append(actor, tick, asleep ? "sleep_started" : "sleep_ended", "{}");

        public void Dispose() { writer.Dispose(); }
    }
}
