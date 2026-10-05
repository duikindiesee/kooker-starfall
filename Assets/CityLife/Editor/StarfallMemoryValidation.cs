using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityLife.World.Editor
{
    public static class StarfallMemoryValidation
    {
        [Serializable] private sealed class Report
        {
            public string schema = "starfall.memory.unity-proof.v1", status, sourceCommit, exportSha256;
            public string limitation = "Isolated Unity editor fixture; actual action API mutations and explicit test sleep transitions. No running player integration or visual acceptance claim.";
            public int eventCount;
            public List<string> checks = new List<string>();
        }
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Argument(string name) { int i = Array.IndexOf(args, name); if (i < 0 || i + 1 >= args.Length) throw new ArgumentException(name); return args[i + 1]; }
            string output = Argument("-starfallMemoryEvidence"), source = Argument("-starfallMemorySource");
            if (!Path.IsPathRooted(output) || Directory.Exists(output)) throw new ArgumentException("Use a new absolute evidence directory.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(source, @"\A[0-9a-f]{40}\z")) throw new ArgumentException("Source commit required.");
            IslandValidation.Run();
            NpcMilestoneValidation.Run();
            var report = new Report { sourceCommit = source };
            void Need(bool value, string name) { if (!value) throw new InvalidOperationException(name); report.checks.Add(name); }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            const string world = "starfall.memory-proof.v1", actorId = "inhabitant-01";
            var actor = new GameObject("Memory evidence actor").transform;
            var hand = new GameObject("Memory evidence hand").transform;
            hand.SetParent(actor); hand.localPosition = new Vector3(0, 1.2f, 0);
            NpcInteractable Make(string id, Vector3 at, Vector3 approach)
            {
                var item = new GameObject(id).AddComponent<NpcInteractable>();
                item.WorldId = world; item.StableId = id; item.transform.position = at;
                item.Approach = new GameObject(id + " approach").transform; item.Approach.position = approach;
                return item;
            }
            var cargo = Make("amber", new Vector3(2, 1.1f, 0), new Vector3(1, 0, 0));
            var depot = Make("depot-west", new Vector3(2, .43f, 2), new Vector3(1, 0, 2));
            depot.Kind = NpcObjectKind.Destination;
            depot.Socket = new GameObject("depot socket").transform; depot.Socket.position = new Vector3(2, 1.1f, 2);
            var api = new NpcActionApi(actorId, world, actor, hand, new[] { cargo, depot });
            Directory.CreateDirectory(output);
            string exportPath = Path.Combine(output, "unity-events.jsonl");
            using (var export = new StarfallMemoryExport(exportPath, world, "unity-local", "memory-proof-01", source))
            {
                export.RegisterIdentity(actorId, "Aster", 0);
                export.RegisterIdentity("inhabitant-02", "Mira", 0);
                Need(export.Count == 2, "separate-stable-identities-exported");
                var denied = api.Execute(1, NpcActionKind.Pickup, "amber");
                Need(!denied.success && api.Held == null, "actual-remote-pickup-rejected");
                Need(!export.CompletedAction(actorId, 1, 1, NpcActionKind.Pickup, "amber", "amber", denied) && export.Count == 2, "denied-receipt-is-not-confirmed-event");
                actor.position = cargo.Approach.position;
                var picked = api.Execute(2, NpcActionKind.Pickup, "amber");
                Need(picked.success && api.Held == cargo && cargo.HeldBy == actorId, "actual-authorized-pickup-and-ownership");
                Need(export.CompletedAction(actorId, 10, 2, NpcActionKind.Pickup, "amber", "amber", picked), "successful-pickup-exported");
                var duplicate = api.Execute(2, NpcActionKind.Pickup, "amber");
                Need(!export.CompletedAction(actorId, 10, 2, NpcActionKind.Pickup, "amber", "amber", duplicate) && export.Count == 3, "duplicate-action-not-exported-twice");
                actor.position = depot.Approach.position;
                string held = api.Held.StableId;
                var delivered = api.Execute(3, NpcActionKind.Deliver, "depot-west");
                Need(delivered.success && api.Held == null && api.Deliveries == 1 && depot.Occupant == "amber", "actual-delivery-updated-world");
                Need(export.CompletedAction(actorId, 20, 3, NpcActionKind.Deliver, "depot-west", held, delivered), "delivery-receipt-exported");
                export.SleepTransition(actorId, 30, true);
                export.SleepTransition("inhabitant-02", 30, true);
                export.SleepTransition(actorId, 40, false);
                export.SleepTransition(actorId, 50, true);
                Need(export.Count == 8, "explicit-fixture-sleep-wake-events-exported");
                try { export.SleepTransition(actorId, 49, false); Need(false, "tick-regression-must-throw"); }
                catch (ArgumentException) { Need(export.Count == 8, "tick-regression-does-not-append"); }
                report.eventCount = export.Count;
            }
            using (var hash = SHA256.Create()) report.exportSha256 = string.Concat(hash.ComputeHash(File.ReadAllBytes(exportPath)).Select(x => x.ToString("x2")));
            Need(File.ReadAllLines(exportPath).Length == 8, "eight-durable-jsonl-records");
            try { using (var duplicate = new StarfallMemoryExport(exportPath, world, "unity-local", "memory-proof-01", source)) { } Need(false, "overwrite-must-throw"); }
            catch (IOException) { Need(true, "prior-export-overwrite-refused"); }
            foreach (var root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
            report.status = "PASS";
            File.WriteAllText(Path.Combine(output, "unity-memory-validation.json"), JsonUtility.ToJson(report, true));
            Debug.Log("STARFALL_MEMORY_UNITY_PASS " + report.checks.Count);
        }
    }
}
