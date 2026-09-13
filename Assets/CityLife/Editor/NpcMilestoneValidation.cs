using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityLife.World.Editor
{
    public static class NpcMilestoneValidation
    {
        [Serializable] public sealed class Result { public string status, utc; public List<string> checks = new List<string>(); }
        public static void Run()
        {
            var report = new Result { utc = DateTime.UtcNow.ToString("O") };
            void Need(bool condition, string name)
            { if (!condition) throw new InvalidOperationException(name); report.checks.Add(name); }
            NpcObservation Item(string id, int distance, bool permission = true) =>
                new NpcObservation { id = id, distanceMillimetres = distance, permission = permission, available = true, kind = NpcObjectKind.Item };
            var banned = new Dictionary<string, int>();
            var a = Item("alpha", 3000); var b = Item("beta", 1000); var denied = Item("denied", 100, false);
            var candidates = new List<NpcObservation> { a, b, denied };
            Need(NpcDecisionPolicy.Choose(candidates, false, banned, 0) == b, "nearest-permitted-perceived-item");
            candidates.Reverse();
            Need(NpcDecisionPolicy.Choose(candidates, false, banned, 0) == b, "enumeration-order-independent-choice");
            a.distanceMillimetres = 1000;
            Need(NpcDecisionPolicy.Choose(candidates, false, banned, 0) == a, "stable-id-breaks-distance-ties");
            banned["alpha"] = 50;
            Need(NpcDecisionPolicy.Choose(candidates, false, banned, 49) == b, "temporary-failure-cooldown");
            Need(NpcDecisionPolicy.Choose(candidates, false, banned, 50) == a, "cooldown-expires-at-explicit-tick");
            Need(NpcDecisionPolicy.Choose(candidates, true, banned, 50) == null, "carried-item-needs-a-perceived-destination");
            var destination = new NpcObservation { id = "depot", kind = NpcObjectKind.Destination, permission = true, available = true };
            candidates.Add(destination);
            Need(NpcDecisionPolicy.Choose(candidates, true, banned, 50) == destination, "carrying-changes-goal-kind");
            destination.available = false;
            Need(NpcDecisionPolicy.Choose(candidates, true, banned, 50) == null, "occupied-destination-ineligible");
            Need(NpcDecisionPolicy.Choose(new List<NpcObservation>(), false, banned, 0) == null, "no-perceptions-yields-wait");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var observer = new GameObject("Validation observer").AddComponent<NpcPerception>();
            NpcInteractable Make(string id, Vector3 position)
            {
                var o = new GameObject(id); o.layer = 11; o.transform.position = position;
                var item = o.AddComponent<NpcInteractable>(); item.StableId = id;
                var collider = o.AddComponent<SphereCollider>(); collider.radius = .2f; collider.isTrigger = true;
                return item;
            }
            Make("visible", new Vector3(2, 1.1f, 0));
            Make("far", new Vector3(30, 1.1f, 0));
            Make("hidden", new Vector3(0, 1.1f, 4));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.layer = 8;
            wall.transform.position = new Vector3(0, 1, 2); wall.transform.localScale = new Vector3(2, 2, .4f);
            var observations = observer.Sense(20);
            Need(observations.Any(x => x.id == "visible"), "physics-perception-detects-nearby-object");
            Need(!observations.Any(x => x.id == "far"), "radius-excludes-distant-object");
            Need(!observations.Any(x => x.id == "hidden") && observer.OccludedCount > 0, "solid-wall-occludes-perception");
            UnityEngine.Object.DestroyImmediate(wall);
            Need(observer.Sense(30).Any(x => x.id == "hidden"), "removing-occluder-reveals-object");
            var log = observer.gameObject.AddComponent<NpcDecisionLog>();
            for (int i = 0; i < 140; i++) log.Record(i, "test", "visible", "goal", "action", "result");
            Need(log.Entries.Count == 128 && log.Total == 140 && log.Entries[0].sequence == 13, "decision-log-is-bounded-and-ordered");
            var cargo = Make("api-cargo", new Vector3(2, 1.1f, 0));
            cargo.Approach = new GameObject("cargo approach").transform; cargo.Approach.position = new Vector3(1, 0, 0);
            var depot = Make("api-depot", new Vector3(2, .43f, 2)); depot.Kind = NpcObjectKind.Destination;
            depot.Approach = new GameObject("depot approach").transform; depot.Approach.position = new Vector3(1, 0, 2);
            depot.Socket = new GameObject("depot socket").transform; depot.Socket.position = new Vector3(2, 1.1f, 2);
            var hand = new GameObject("mapped-hand-fixture").transform; hand.SetParent(observer.transform);
            hand.localPosition = new Vector3(0, 1.2f, 0);
            var api = new NpcActionApi("fixture-agent", NpcAutonomy.WorldId, observer.transform, hand, new[] { cargo, depot });
            Need(api.Execute(1, NpcActionKind.Pickup, cargo.StableId).code == "out-of-reach" && api.Held == null,
                "pickup-rejects-remote-target-without-mutation");
            observer.transform.position = cargo.Approach.position;
            cargo.Permission = false;
            Need(api.Execute(2, NpcActionKind.Pickup, cargo.StableId).code == "permission-denied" && cargo.transform.parent == null,
                "pickup-rechecks-live-permission");
            cargo.Permission = true;
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.layer = 8;
            blocker.transform.position = new Vector3(1.5f, 1.3f, 0); blocker.transform.localScale = new Vector3(.2f, .6f, .6f);
            Need(api.Execute(3, NpcActionKind.Pickup, cargo.StableId).code == "line-of-sight-blocked",
                "pickup-rechecks-live-occlusion");
            UnityEngine.Object.DestroyImmediate(blocker);
            Need(api.Execute(4, NpcActionKind.Pickup, cargo.StableId).success && api.Held == cargo && cargo.transform.parent == hand,
                "validated-pickup-owns-and-attaches-cargo");
            Need(api.Execute(4, NpcActionKind.Pickup, cargo.StableId).duplicate && api.Held == cargo,
                "duplicate-request-is-idempotent");
            Need(api.Execute(4, NpcActionKind.Deliver, depot.StableId).code == "request-id-conflict",
                "request-id-cannot-be-reused-for-another-action");
            observer.transform.position = depot.Approach.position; depot.Occupant = "someone-else";
            Need(api.Execute(5, NpcActionKind.Deliver, depot.StableId).code == "destination-full-or-invalid" &&
                api.Held == cargo && cargo.transform.parent == hand, "failed-delivery-retains-cargo-and-ownership");
            depot.Occupant = ""; api.AllowDelivery = false;
            Need(api.Execute(6, NpcActionKind.Deliver, depot.StableId).code == "permission-denied",
                "agent-action-permission-enforced");
            api.AllowDelivery = true;
            Need(api.Execute(7, NpcActionKind.Deliver, depot.StableId).success && api.Held == null &&
                depot.Occupant == cargo.StableId && cargo.DeliveredTo == depot.StableId && api.Deliveries == 1,
                "validated-delivery-updates-one-item-and-one-destination");
            Need(api.Execute(7, NpcActionKind.Deliver, depot.StableId).duplicate && api.Deliveries == 1,
                "duplicate-delivery-does-not-count-twice");
            Need(api.Execute(8, (NpcActionKind)99, depot.StableId).code == "unsupported-action", "unlisted-action-rejected");
            Need(api.Execute(9, NpcActionKind.Pickup, "unknown").code == "target-unavailable", "unregistered-target-rejected");
            foreach (var root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
            report.status = "PASS";
            Directory.CreateDirectory("evidence/local/npc");
            File.WriteAllText("evidence/local/npc/validation.json", JsonUtility.ToJson(report, true));
            Debug.Log("NPC_MILESTONE_VALIDATION_PASSED " + report.checks.Count);
        }
    }
}
