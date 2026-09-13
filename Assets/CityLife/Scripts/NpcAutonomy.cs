using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityLife.World
{
    public sealed class NpcAutonomy : MonoBehaviour
    {
        public const float StepSeconds = .02f;
        public const string AgentId = "inhabitant-01", WorldId = "starfall.npc-courtyard.v1";
        public CharacterPreviewActor Actor;
        public NpcPerception Perception;
        public NpcDecisionLog Log;
        public NpcOptionalPlanner OptionalPlanner;
        public NpcInteractable[] Registry;
        public bool ManualSimulation, Running = true;
        public bool MenuPaused { get; set; }
        public bool Possessed { get; private set; }
        public Vector3 ManualDirection { get; set; }
        public int Tick { get; private set; }
        public string Phase { get; private set; } = "Observe";
        public string GoalId => goal != null ? goal.id : "";
        public NpcActionApi Actions { get; private set; }
        public int FailureCount { get; private set; }
        public bool Ready { get; private set; }
        public string LastResult { get; private set; } = "Waiting for perception";
        public List<string> ChosenGoals = new List<string>();
        public StarfallMemoryExport MemoryExport;
        public string MemoryExportFailure { get; private set; } = "";
        private NpcObservation goal;
        private readonly Dictionary<string, int> retryAfter = new Dictionary<string, int>(StringComparer.Ordinal);
        private Queue<Vector3> route;
        private int gestureTicks, requestId, stalledTicks, lastSeenTick;
        private string perceptionSignature = "", previousWait = "";
        private Vector3 previousPosition;

        private void Start()
        {
            foreach (var item in Registry) item.RememberInitial();
            ResetState(); Ready = true;
        }
        public void ResetState()
        {
            if (OptionalPlanner != null) OptionalPlanner.ResetSession();
            foreach (var item in Registry) item.RestoreInitial();
            Tick = requestId = gestureTicks = stalledTicks = FailureCount = 0;
            goal = null; route = null; retryAfter.Clear(); ChosenGoals.Clear(); Log.ResetLog();
            perceptionSignature = previousWait = ""; Phase = "Observe"; LastResult = "Waiting for perception";
            Running = true; MenuPaused = false; Possessed = false; ManualDirection = Vector3.zero;
            Actor.Place(new Vector3(0, .02f, -5)); Actor.transform.rotation = Quaternion.identity;
            Actions = new NpcActionApi(AgentId, WorldId, transform, Actor.Animator.GetBoneTransform(HumanBodyBones.RightHand), Registry);
            Physics.SyncTransforms();
        }
        private void FixedUpdate() { if (Ready && !ManualSimulation) StepTick(); }
        public void SetPossession(bool possessed)
        {
            if (Possessed == possessed) return;
            if (OptionalPlanner != null) OptionalPlanner.Cancel("possession-change");
            Possessed = possessed; ManualDirection = Vector3.zero; gestureTicks = 0; Actor.CancelGesture();
            if (!possessed) Running = true;
            if (!possessed && goal != null)
            {
                route = NpcGridNavigation.Plan(transform.position, goal.approach);
                previousPosition = transform.position; stalledTicks = 0; lastSeenTick = Tick;
                if (route == null) Fail("no-route-after-possession");
            }
            Log.Record(Tick, "control", DescribePerception(), GoalId, possessed ? "possess-same-inhabitant" : "release-to-autonomy",
                "identity, cargo, goal context and decision history retained");
        }
        public void Pause()
        {
            if (OptionalPlanner != null) OptionalPlanner.Cancel("autonomy-paused");
            Running = false;
        }
        public void ToggleAutonomy()
        {
            if (Possessed) return;
            Running = !Running;
            if (!Running && OptionalPlanner != null) OptionalPlanner.Cancel("autonomy-paused");
            Log.Record(Tick, "control", DescribePerception(), GoalId, Running ? "resume-autonomy" : "pause-autonomy", "current goal and cargo retained");
        }
        public void StepTick()
        {
            if (MenuPaused) return;
            Tick++;
            if (Tick % 10 == 1)
            {
                Perception.Sense(Tick);
                string signature = string.Join("|", Perception.Current.Select(x => x.id + ":" + x.permission + ":" + x.available));
                if (signature != perceptionSignature || Tick == 1)
                {
                    perceptionSignature = signature;
                    Log.Record(Tick, "perception", DescribePerception(), GoalId, "sense-radius-and-line-of-sight",
                        Perception.Current.Count + " visible; " + Perception.OccludedCount + " occluded");
                }
                var remembered = goal == null ? null : Perception.Current.Find(x => x.id == goal.id);
                if (remembered != null) { lastSeenTick = Tick; }
            }
            if (Possessed) { Actor.Step(ManualDirection, StepSeconds); return; }
            if (!Running) { Actor.Step(Vector3.zero, StepSeconds); return; }
            if (goal != null && Tick - lastSeenTick > 250)
            { Fail("perception-stale"); Actor.Step(Vector3.zero, StepSeconds); return; }
            if (gestureTicks > 0)
            {
                Phase = "Act";
                gestureTicks--;
                if (gestureTicks == 0)
                {
                    var kind = Actions.Held == null ? NpcActionKind.Pickup : NpcActionKind.Deliver;
                    string carriedItem = Actions.Held == null ? GoalId : Actions.Held.StableId;
                    var result = Actions.Execute(++requestId, kind, GoalId);
                    if (MemoryExport != null && result.success && !result.duplicate)
                    {
                        try { MemoryExport.CompletedAction(AgentId, Tick, requestId, kind, GoalId, carriedItem, result); }
                        catch (Exception) { MemoryExportFailure = "memory-export-failed"; }
                    }
                    LastResult = result.code;
                    Log.Record(Tick, result.success ? "result" : "failure", DescribePerception(), GoalId, kind.ToString(), result.code,
                        result.success ? "" : "keep current cargo; exclude goal for 250 ticks; choose again");
                    if (result.success) { goal = null; route = null; Phase = "Observe"; Perception.Sense(Tick); }
                    else Fail(result.code, false);
                }
                Actor.Step(Vector3.zero, StepSeconds); return;
            }
            if (goal == null)
            {
                bool proposed = OptionalPlanner != null && OptionalPlanner.Choose(retryAfter, out goal);
                if (proposed && goal == null) { Phase = "Think / wait"; Actor.Step(Vector3.zero, StepSeconds); return; }
                if (!proposed) goal = NpcDecisionPolicy.Choose(Perception.Current, Actions.Held != null, retryAfter, Tick);
                if (goal == null)
                {
                    Phase = "Wait";
                    string reason = Actions.Held == null ? "no eligible perceived item" : "no eligible perceived destination; retain cargo";
                    if (reason != previousWait)
                    { Log.Record(Tick, "fallback", DescribePerception(), "", "wait-and-resense", reason, "sense again every 10 ticks"); previousWait = reason; }
                    Actor.Step(Vector3.zero, StepSeconds); return;
                }
                previousWait = ""; lastSeenTick = goal.seenAtTick; ChosenGoals.Add(goal.id);
                Log.Record(Tick, "goal", DescribePerception(), goal.id,
                    Actions.Held == null ? "collect" : "deliver", proposed ? "validated optional proposal; deterministic navigation and action checks" : "nearest eligible perception; distance=" + goal.distanceMillimetres + "mm; stable-id tie break");
                route = NpcGridNavigation.Plan(transform.position, goal.approach);
                if (route == null) { Fail("no-route"); Actor.Step(Vector3.zero, StepSeconds); return; }
                previousPosition = transform.position; stalledTicks = 0;
            }
            Phase = "Navigate";
            while (route.Count > 0 && FlatDistance(transform.position, route.Peek()) < .12f) route.Dequeue();
            if (route.Count == 0)
            {
                Vector3 facing = goal.position - transform.position; facing.y = 0;
                if (facing.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(facing);
                gestureTicks = 102; Phase = "Act"; Actor.Gesture();
                Log.Record(Tick, "action", DescribePerception(), GoalId, "Interact animation; then validate current world state", "in progress");
                Actor.Step(Vector3.zero, StepSeconds); return;
            }
            Vector3 difference = route.Peek() - transform.position; difference.y = 0;
            Vector3 direction = difference.normalized * Mathf.Min(1, difference.magnitude / (Actor.WalkSpeed * StepSeconds));
            Actor.Step(direction, StepSeconds);
            stalledTicks = FlatDistance(previousPosition, transform.position) < .0005f ? stalledTicks + 1 : 0;
            previousPosition = transform.position;
            if (stalledTicks >= 100) Fail("movement-stalled");
        }
        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        private void Fail(string code, bool record = true)
        {
            FailureCount++; LastResult = code;
            if (record) Log.Record(Tick, "failure", DescribePerception(), GoalId, "stop-current-action", code,
                "keep cargo; exclude goal for 250 ticks; choose again");
            if (goal != null) retryAfter[goal.id] = Tick + 250;
            goal = null; route = null; gestureTicks = 0; stalledTicks = 0; Phase = "Fallback";
        }
        public string DescribePerception() => string.Join(", ", Perception.Current.Select(x =>
            x.id + (x.permission ? "" : " [denied]") + (x.available ? "" : " [unavailable]")));
    }
}
