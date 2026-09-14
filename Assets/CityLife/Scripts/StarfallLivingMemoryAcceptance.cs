using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace CityLife.World
{
    public static class StarfallLivingMemoryAcceptance
    {
        [Serializable] private sealed class Report
        {
            public string status, world, inhabitant, build, session, eventId, item, destination, verifiedSummary, perception, source;
            public int eventTick, admissionTick, exportedEvents, deliveries;
            public bool liveAdmitted, persistenceVerified;
            public StarfallMemoryThought.Result thought;
        }
        public static IEnumerator Verify(NpcAutonomy brain, NpcDecisionHud hud, Action<string, bool, string> need, Action<string> capture, string directory)
        {
            string[] args = Environment.GetCommandLineArgs();
            string Arg(string name) { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
            string build = NpcPreviewSmoke.RuntimeBuildId, session = "living-" + Guid.NewGuid().ToString("N");
            var report = new Report { status = "IN_PROGRESS", world = brain.InstanceWorldId, inhabitant = NpcAutonomy.AgentId, build = build, session = session,
                source = (Application.isEditor ? "Unity Editor Play Mode runtime; NOT standalone-player acceptance. " : "Actual compiled Unity world " + brain.InstanceWorldId + ". ") + "Real action receipts; isolated SQLite HTTP persistence. Scripted traversal is not physical keyboard acceptance." };
            string exportPath = Path.Combine(directory, "living-events.jsonl");
            brain.ResetState(); brain.OptionalPlanner.Configure(null); hud.Detailed = true;
            using (var export = new StarfallMemoryExport(exportPath, report.world, "unity-local", session, build))
            {
                export.RegisterIdentity(report.inhabitant, "Inhabitant 01", brain.Tick); brain.MemoryExport = export;
                try { for (int i = 0; i < 3000 && brain.Actions.Deliveries == 0; i++) { brain.StepTick(); yield return null; } }
                finally { brain.MemoryExport = null; }
                report.exportedEvents = export.Count;
            }
            report.deliveries = brain.Actions.Deliveries; report.eventTick = brain.Tick; report.perception = brain.DescribePerception();
            hud.LivingMemoryText = "LIVING MEMORY\n" + report.inhabitant + " / " + report.world + "\nCompleted delivery; persisting verified receipts.\nNo model thought accepted yet.";
            capture("50-real-delivery-before-memory");
            need("living-memory-real-receipts", report.exportedEvents == 3 && report.deliveries == 1 && brain.MemoryExportFailure.Length == 0,
                "Identity plus successful pickup/delivery receipts came directly from NpcActionApi during real player navigation.");
            using (var client = new StarfallLivingMemoryClient(Arg("-npcMemoryClient"), report.world, report.inhabitant, build))
            using (var memoryCancellation = new CancellationTokenSource(5000))
            {
                var memoryTask = client.PublishAndRetrieve(File.ReadAllLines(exportPath), memoryCancellation.Token);
                float memoryLimit = Time.realtimeSinceStartup + 6;
                while (!memoryTask.IsCompleted && Time.realtimeSinceStartup < memoryLimit) yield return null;
                bool memoryReady = memoryTask.IsCompleted && !memoryTask.IsFaulted && !memoryTask.IsCanceled;
                if (!memoryReady)
                {
                    memoryCancellation.Cancel(); report.status = "MEMORY_REJECTED_OR_TIMEOUT";
                    File.WriteAllText(Path.Combine(directory, "living-memory.json"), JsonUtility.ToJson(report, true));
                    need("living-memory-persist-retrieve", false, "Memory service did not return a verified matching scoped delivery within the bounded window.");
                    yield break;
                }
                var memory = memoryTask.GetAwaiter().GetResult(); report.persistenceVerified = true;
                report.eventId = memory.EventId; report.item = memory.Item; report.destination = memory.Target; report.verifiedSummary = memory.Summary;
                File.WriteAllText(Path.Combine(directory, "retrieved-delivery.json"), memory.RecordJson);
                need("living-memory-persist-retrieve", memory.World == report.world && memory.Actor == report.inhabitant && memory.Tick == report.eventTick,
                    "HTTP ingestion receipts match locally hashed events; reader capability returns only this inhabitant's confirmed delivery episode.");
                int requestedTick = brain.Tick; var identity = brain.gameObject.GetEntityId();
                using (var cancellation = new CancellationTokenSource())
                {
                    // One request only: timeout/error closes this diagnostic's circuit; no retry or warmup inference.
                    var thoughtTask = StarfallMemoryThought.Request(memory, Arg("-npcLocalEndpoint"), Arg("-npcLocalModel"), cancellation.Token);
                    float nextTick = Time.realtimeSinceStartup + NpcAutonomy.StepSeconds;
                    while (!thoughtTask.IsCompleted)
                    {
                        if (brain.MenuPaused || brain.Possessed || !brain.Running || brain.gameObject.GetEntityId() != identity) cancellation.Cancel();
                        if (Time.realtimeSinceStartup >= nextTick) { brain.StepTick(); nextTick += NpcAutonomy.StepSeconds; }
                        yield return null;
                    }
                    report.thought = thoughtTask.GetAwaiter().GetResult();
                    File.WriteAllText(Path.Combine(directory, "living-thought-request.json"), report.thought.requestJson);
                    if (report.thought.rawReceived) File.WriteAllText(Path.Combine(directory, "living-thought-raw.json"), report.thought.rawAnswer);
                }
                var thought = report.thought; report.admissionTick = brain.Tick;
                bool grounded = thought.schemaValid && thought.reflection.IndexOf(memory.Item, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    thought.reflection.IndexOf("deliver", StringComparison.OrdinalIgnoreCase) >= 0;
                report.liveAdmitted = grounded && !brain.MenuPaused && !brain.Possessed && brain.Running && brain.gameObject.GetEntityId() == identity &&
                    brain.Tick >= requestedTick && brain.Tick - requestedTick <= 75 && brain.Actions.Deliveries >= 1 &&
                    brain.Registry.Any(x => x.StableId == memory.Target && x.Occupant == memory.Item) && thought.milliseconds <= StarfallMemoryThought.DeadlineMilliseconds;
                report.status = report.liveAdmitted ? "PASS" : "THOUGHT_NOT_ADMITTED";
                hud.LivingMemoryText = (Application.isEditor ? "EDITOR PLAY MODE\n" : "") + "REMEMBERED INSIGHT\n" + report.inhabitant + " / " + report.world +
                    "\nVerified delivery: " + memory.Item + " -> " + memory.Target + "\nEvent: " + memory.EventId.Substring(0, 16) +
                    (report.liveAdmitted ? "\nModel: " + thought.model + "\nThought: " + thought.reflection : "\nDeterministic fallback / " + thought.status + "\nNo model thought admitted.") +
                    "\n" + thought.milliseconds + " ms / 1500 ms deadline\nMemory records facts; model text is interpretation.\nUnity alone controls actions.";
                brain.Log.Record(brain.Tick, "memory", report.perception, memory.Target, "read verified own delivery; bounded reflection only", report.status);
                capture("51-living-memory-thought-or-fallback");
                File.WriteAllText(Path.Combine(directory, "living-memory.json"), JsonUtility.ToJson(report, true));
                need("living-memory-one-model-request", thought.completionAttempts == 1, "Exactly one inference attempt, no retries, unchanged gameplay deadline.");
                need("living-memory-genuine-thought-required", report.liveAdmitted,
                    "Requires strict two-word text, relevant verified delivery, unchanged identity/current delivery state, and <=1500 ms; fallback never passes.");
            }
        }
    }
}
