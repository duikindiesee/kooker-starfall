using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    public enum NpcActionKind { Pickup, Deliver }
    [Serializable] public struct NpcActionResult
    { public bool success, duplicate; public string code; }

    // The only pickup/delivery mutation boundary. Requests name registered objects, never code or arbitrary transforms.
    public sealed class NpcActionApi
    {
        private readonly string agentId, worldId;
        private readonly Transform actor, hand;
        private readonly Dictionary<string, NpcInteractable> objects = new Dictionary<string, NpcInteractable>(StringComparer.Ordinal);
        private readonly Dictionary<int, Receipt> receipts = new Dictionary<int, Receipt>();
        private sealed class Receipt { public string signature; public NpcActionResult result; }
        public NpcInteractable Held { get; private set; }
        public bool AllowPickup = true, AllowDelivery = true;
        public int Deliveries { get; private set; }
        public NpcActionApi(string agentId, string worldId, Transform actor, Transform hand, IEnumerable<NpcInteractable> registry)
        {
            this.agentId = agentId; this.worldId = worldId; this.actor = actor; this.hand = hand;
            foreach (var item in registry)
            {
                if (item == null || string.IsNullOrEmpty(item.StableId) || objects.ContainsKey(item.StableId))
                    throw new InvalidOperationException("A unique, nonempty id is required for every registered interactable.");
                objects.Add(item.StableId, item);
            }
        }
        public NpcActionResult Execute(int requestId, NpcActionKind action, string targetId)
        {
            string signature = action + ":" + targetId;
            NpcActionResult Deny(string code) => new NpcActionResult { code = code, success = false };
            if (requestId <= 0) return Deny("invalid-request-id");
            if (action != NpcActionKind.Pickup && action != NpcActionKind.Deliver) return Deny("unsupported-action");
            if (receipts.TryGetValue(requestId, out var receipt))
            {
                if (receipt.signature != signature) return Deny("request-id-conflict");
                var repeat = receipt.result; repeat.duplicate = true; return repeat;
            }
            if (receipts.Count >= 256) return Deny("bounded-ledger-full");
            NpcActionResult Finish(NpcActionResult result)
            { receipts.Add(requestId, new Receipt { signature = signature, result = result }); return result; }
            if (!objects.TryGetValue(targetId ?? "", out var target) || target == null || !target.isActiveAndEnabled)
                return Finish(Deny("target-unavailable"));
            if (target.WorldId != worldId || target.gameObject.scene != actor.gameObject.scene)
                return Finish(Deny("world-mismatch"));
            if (!target.Permission || (action == NpcActionKind.Pickup ? !AllowPickup : !AllowDelivery))
                return Finish(Deny("permission-denied"));
            if (target.Approach == null || Vector3.Distance(actor.position, target.Approach.position) > .65f ||
                Vector3.Distance(actor.position + Vector3.up, target.SightPoint) > 1.7f)
                return Finish(Deny("out-of-reach"));
            Physics.SyncTransforms();
            Vector3 eye = actor.position + Vector3.up * 1.6f, delta = target.SightPoint - eye;
            if (Physics.Raycast(eye, delta.normalized, delta.magnitude, 1 << 8, QueryTriggerInteraction.Ignore))
                return Finish(Deny("line-of-sight-blocked"));
            if (action == NpcActionKind.Pickup)
            {
                if (target.Kind != NpcObjectKind.Item) return Finish(Deny("wrong-target-kind"));
                if (Held != null) return Finish(Deny("hands-full"));
                if (!target.Available || hand == null) return Finish(Deny("item-unavailable"));
                // All gates precede mutation. Moving a whole rigid prop does not claim a fitted finger grip.
                target.transform.SetParent(hand, false); target.transform.localPosition = new Vector3(.06f, .04f, 0);
                target.transform.localRotation = Quaternion.identity;
                target.HeldBy = agentId; Held = target;
                return Finish(new NpcActionResult { success = true, code = "picked-up" });
            }
            if (target.Kind != NpcObjectKind.Destination) return Finish(Deny("wrong-target-kind"));
            if (Held == null || Held.HeldBy != agentId || Held.transform.parent != hand)
                return Finish(Deny("cargo-ownership-mismatch"));
            if (!target.Available || target.Socket == null) return Finish(Deny("destination-full-or-invalid"));
            Held.transform.SetParent(target.Socket, false); Held.transform.localPosition = Vector3.zero;
            Held.transform.localRotation = Quaternion.identity;
            target.Occupant = Held.StableId; Held.DeliveredTo = target.StableId; Held.HeldBy = "";
            Held = null; Deliveries++;
            return Finish(new NpcActionResult { success = true, code = "delivered" });
        }
    }
}
