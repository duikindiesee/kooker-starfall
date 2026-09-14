using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CityLife.World
{
    // Opt-in normal-play adapter. It records facts and displays bounded reflection;
    // it never resets the world, drives an action, adds test input, or quits.
    public sealed class StarfallLivingMemoryRuntime : MonoBehaviour
    {
        public const int MaximumPendingEvents = 32;
        public NpcAutonomy Brain;
        public NpcDecisionHud Hud;
        private readonly Queue<string> pending = new Queue<string>();
        private readonly HashSet<string> queued = new HashSet<string>(StringComparer.Ordinal);
        private StarfallMemoryExport export;
        private StarfallLivingMemoryClient client;
        private CancellationTokenSource lifetime;
        private string endpoint, model, build;
        private string evidenceDirectory;
        private bool enabledForSession, thoughtAttempted;
        [Serializable] private sealed class RuntimeEvidence
        {
            public string status, world, inhabitant, build, item, destination, thought, model;
            public long milliseconds;
            public int deliveries, tick;
            public bool normalPlay, persisted, admitted;
        }

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-npcLivingMemoryRuntime") < 0) yield break;
            string Arg(string name) { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
            string config = Arg("-npcMemoryClient"), outbox = Arg("-npcMemoryOutbox"); build = Arg("-npcMemoryBuild"); endpoint = Arg("-npcLocalEndpoint"); model = Arg("-npcLocalModel");
            evidenceDirectory = Arg("-npcLivingMemoryEvidence");
            while (Brain != null && !Brain.Ready) yield return null;
            if (Brain == null || Hud == null || string.IsNullOrEmpty(config) || string.IsNullOrEmpty(build) || string.IsNullOrEmpty(outbox) || !Path.IsPathFullyQualified(outbox))
            { SetStatus("LIVING MEMORY\nUnavailable: scoped runtime configuration is incomplete.\nDeterministic autonomy continues."); yield break; }
            try
            {
                if (!Regex.IsMatch(Brain.InstanceWorldId ?? "", @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,95}\z")) throw new FormatException("invalid-world-scope");
                if (!string.IsNullOrEmpty(evidenceDirectory))
                {
                    if (!Path.IsPathFullyQualified(evidenceDirectory) || (Directory.Exists(evidenceDirectory) && Directory.GetFileSystemEntries(evidenceDirectory).Length != 0))
                        throw new IOException("normal-memory-evidence-must-be-absolute-and-empty");
                    Directory.CreateDirectory(evidenceDirectory);
                }
                string root = Path.Combine(outbox, Brain.InstanceWorldId, NpcAutonomy.AgentId, "sessions");
                Directory.CreateDirectory(root);
                string session = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
                export = new StarfallMemoryExport(Path.Combine(root, session + ".jsonl"), Brain.InstanceWorldId, "unity-local", session, build);
                export.Appended += QueueEvent;
                client = new StarfallLivingMemoryClient(config, Brain.InstanceWorldId, NpcAutonomy.AgentId, build);
                lifetime = new CancellationTokenSource(); enabledForSession = true;
                export.RegisterIdentity(NpcAutonomy.AgentId, "Inhabitant 01", Brain.Tick);
                Brain.MemoryExport = export; Hud.Detailed = true;
                SetStatus("LIVING MEMORY\nRecording real actions locally.\nWaiting for a verified delivery.");
                StartCoroutine(ProcessEvents());
            }
            catch (Exception error)
            {
                Shutdown(); SetStatus("LIVING MEMORY\nUnavailable: " + Safe(error.Message) + "\nDeterministic autonomy continues.");
            }
        }

        private void QueueEvent(string row)
        {
            string id;
            try { id = StarfallLivingMemoryClient.HashEvent(row); }
            catch (Exception) { SetStatus("LIVING MEMORY\nLocal receipt could not be queued.\nDeterministic autonomy continues."); return; }
            if (queued.Contains(id)) return;
            if (pending.Count >= MaximumPendingEvents)
            { SetStatus("LIVING MEMORY\nService backlog reached its bounded limit; receipts remain in the local session log.\nDeterministic autonomy continues."); return; }
            queued.Add(id); pending.Enqueue(row);
        }

        private IEnumerator ProcessEvents()
        {
            using (var recallCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                recallCancellation.CancelAfter(5000);
                var prior = client.RecallLatestConfirmedDelivery(recallCancellation.Token);
                while (!prior.IsCompleted) yield return null;
                if (!prior.IsFaulted && !prior.IsCanceled)
                {
                    var memory = prior.GetAwaiter().GetResult();
                    if (memory != null)
                    {
                        SetStatus("LIVING MEMORY / PRIOR JOURNEY\n" + memory.Summary + "\nVerified scoped recall; waiting for the next real action.");
                        WritePriorEvidence(memory);
                    }
                }
                else { var observed = prior.Exception; SetStatus("LIVING MEMORY\nPrior recall unavailable; new receipts still record locally.\nDeterministic autonomy continues."); }
            }
            while (enabledForSession)
            {
                if (pending.Count == 0) { yield return null; continue; }
                string row = pending.Dequeue(); string eventId = StarfallLivingMemoryClient.HashEvent(row); queued.Remove(eventId);
                using (var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    request.CancelAfter(5000);
                    var publish = client.PublishEvent(row, request.Token);
                    while (!publish.IsCompleted) yield return null;
                    if (publish.IsFaulted || publish.IsCanceled)
                    { var observed = publish.Exception; SetStatus("LIVING MEMORY\nPersistence unavailable; receipt remains in the local session log.\nDeterministic autonomy continues."); continue; }
                    eventId = publish.GetAwaiter().GetResult();
                }
                if (!IsDelivery(row)) continue;
                StarfallLivingMemoryClient.Evidence memory = null;
                using (var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    request.CancelAfter(5000);
                    var recall = client.RetrieveConfirmedDelivery(row, eventId, request.Token);
                    while (!recall.IsCompleted) yield return null;
                    if (!recall.IsFaulted && !recall.IsCanceled) memory = recall.GetAwaiter().GetResult();
                    else { var observed = recall.Exception; }
                }
                if (memory == null)
                { SetStatus("LIVING MEMORY\nDelivery persisted; verified recall is unavailable.\nDeterministic autonomy continues."); continue; }
                SetStatus("LIVING MEMORY\nVerified delivery: " + memory.Item + " -> " + memory.Target + "\nMemory persisted; model reflection is optional.");
                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(model) || thoughtAttempted) continue;
                thoughtAttempted = true;
                int requestedTick = Brain.Tick; var identity = Brain.gameObject.GetEntityId();
                using (var thoughtCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    var thoughtTask = StarfallMemoryThought.Request(memory, endpoint, model, thoughtCancellation.Token);
                    while (!thoughtTask.IsCompleted)
                    {
                        if (Brain.MenuPaused || Brain.Possessed || !Brain.Running || Brain.gameObject.GetEntityId() != identity) thoughtCancellation.Cancel();
                        yield return null;
                    }
                    var thought = thoughtTask.GetAwaiter().GetResult();
                    bool current = CanAdmitDeliveryThought(thought, memory, !Brain.MenuPaused && !Brain.Possessed && Brain.Running,
                        Brain.gameObject.GetEntityId() == identity && Brain.InstanceWorldId == memory.World && Brain.Tick >= requestedTick,
                        Brain.Registry != null && Array.Exists(Brain.Registry, x => x.StableId == memory.Target && x.Occupant == memory.Item));
                    SetStatus(current ? "REMEMBERED INSIGHT\nVerified delivery: " + memory.Item + " -> " + memory.Target + "\nThought: " + thought.reflection +
                        "\nMemory records facts; Unity controls actions." : "LIVING MEMORY\nVerified delivery: " + memory.Item + " -> " + memory.Target +
                        "\nDeterministic fallback / " + thought.status + ". Unity controls actions.");
                    WriteEvidence(memory, thought, current);
                }
            }
        }

        private static bool IsDelivery(string row)
        {
            try
            {
                var value = StarfallLivingMemoryClient.Map(row, 8192); if ((string)value["kind"] != "action_completed") return false;
                var data = (Dictionary<string, object>)value["data"];
                return (string)data["action"] == "deliver" && (string)data["outcome"] == "delivered";
            }
            catch (Exception) { return false; }
        }
        public static bool CanAdmitDeliveryThought(StarfallMemoryThought.Result thought, StarfallLivingMemoryClient.Evidence memory,
            bool controlsPermit, bool identityCurrent, bool deliveryCurrent)
        {
            if (thought == null || memory == null || !thought.schemaValid || thought.milliseconds > StarfallMemoryThought.DeadlineMilliseconds ||
                !controlsPermit || !identityCurrent || !deliveryCurrent || string.IsNullOrWhiteSpace(thought.reflection) || string.IsNullOrWhiteSpace(memory.Item)) return false;
            string[] words = thought.reflection.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 2 && (string.Equals(words[0], "delivered", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(words[0], "delivery", StringComparison.OrdinalIgnoreCase)) && string.Equals(words[1], memory.Item, StringComparison.OrdinalIgnoreCase);
        }
        private void SetStatus(string value) { if (Hud != null) Hud.LivingMemoryText = value; }
        private void WriteEvidence(StarfallLivingMemoryClient.Evidence memory, StarfallMemoryThought.Result thought, bool admitted)
        {
            if (string.IsNullOrEmpty(evidenceDirectory)) return;
            var report = new RuntimeEvidence { status = admitted ? "PASS" : "SAFE_FALLBACK", world = Brain.InstanceWorldId, inhabitant = NpcAutonomy.AgentId, build = build,
                item = memory.Item, destination = memory.Target, thought = thought.reflection, model = thought.model, milliseconds = thought.milliseconds,
                deliveries = Brain.Actions.Deliveries, tick = Brain.Tick, normalPlay = true, persisted = true, admitted = admitted };
            WriteEvidenceFiles("normal-living-memory", report);
        }
        private void WritePriorEvidence(StarfallLivingMemoryClient.Evidence memory)
        {
            if (string.IsNullOrEmpty(evidenceDirectory)) return;
            var report = new RuntimeEvidence { status = "PRIOR_JOURNEY_RECALLED", world = Brain.InstanceWorldId, inhabitant = NpcAutonomy.AgentId, build = build,
                item = memory.Item, destination = memory.Target, deliveries = Brain.Actions.Deliveries, tick = Brain.Tick, normalPlay = true, persisted = true };
            WriteEvidenceFiles("normal-prior-journey", report);
        }
        private void WriteEvidenceFiles(string name, RuntimeEvidence report)
        {
            File.WriteAllText(Path.Combine(evidenceDirectory, name + ".json"), JsonUtility.ToJson(report, true));
            Hud.Refresh(); UnityEngine.Canvas.ForceUpdateCanvases();
            var camera = Hud.View; var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var priorTarget = camera.targetTexture; var priorActive = RenderTexture.active;
            camera.targetTexture = target; RenderTexture.active = target; camera.Render();
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); texture.Apply();
            camera.targetTexture = priorTarget; RenderTexture.active = priorActive;
            File.WriteAllBytes(Path.Combine(evidenceDirectory, name + ".png"), texture.EncodeToPNG());
            Destroy(texture); target.Release(); Destroy(target);
        }
        private static string Safe(string value) => string.IsNullOrEmpty(value) ? "unknown error" : value.Length > 120 ? value.Substring(0, 120) : value;
        private void OnDestroy() { Shutdown(); }
        private void Shutdown()
        {
            enabledForSession = false;
            if (Brain != null && Brain.MemoryExport == export) Brain.MemoryExport = null;
            if (export != null) export.Appended -= QueueEvent;
            lifetime?.Cancel(); client?.Dispose(); export?.Dispose();
            client = null; export = null; lifetime?.Dispose(); lifetime = null;
        }
    }
}
