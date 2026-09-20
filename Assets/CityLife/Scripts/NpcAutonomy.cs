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
        public StarfallSurvivalAutonomy Survival;
        public NpcInteractable[] Registry;
        public string InstanceWorldId = WorldId;
        public Vector3 SpawnPosition = new Vector3(0, .02f, -5);
        public NpcTerrainNavigation TerrainNavigation;
        public bool ManualSimulation, Running = true;
        public bool MenuPaused { get; set; }
        public bool Possessed { get; private set; }
        public Vector3 ManualDirection { get; set; }
        public int Tick { get; private set; }
        public string Phase { get; private set; } = "Observe";
        public string GoalId => goal != null ? goal.id : "";
        public NpcActionApi Actions { get; internal set; }
        public void SetActionsForTesting(NpcActionApi actions) => Actions = actions;
        public int FailureCount { get; private set; }
        public bool Ready { get; private set; }
        public string LastResult { get; set; } = "Waiting for perception";
        public string LastFailureDiagnostic { get; private set; } = "none";
        public List<string> ChosenGoals = new List<string>();
        public StarfallMemoryExport MemoryExport;
        public string MemoryExportFailure { get; private set; } = "";
        public CityLife.Items.PhysicalItemBootstrap PhysicalItems;
        public ForagingExpeditionCycle Foraging;
        private NpcObservation goal;
        private readonly Dictionary<string, int> retryAfter = new Dictionary<string, int>(StringComparer.Ordinal);
        private Queue<Vector3> route;
        private int gestureTicks, requestId, stalledTicks, lastSeenTick;
        private string perceptionSignature = "", previousWait = "";
        private Vector3 previousPosition;

        private void Start()
        {
            if (Registry != null)
            {
                foreach (var item in Registry)
                {
                    if (item != null) item.RememberInitial();
                }
            }
            Ready = ResetState();
        }
        public IEnumerable<NpcInteractable> AllInteractables =>
            PhysicalItems != null && PhysicalItems.AllInteractables != null
                ? (Registry != null ? Registry.Where(i => i != null).Concat(PhysicalItems.AllInteractables.Where(i => i != null)) : PhysicalItems.AllInteractables.Where(i => i != null))
                : (IEnumerable<NpcInteractable>)(Registry ?? Array.Empty<NpcInteractable>());

        public bool ResetState()
        {
            Transform rightHand = (Actor != null && Actor.Animator != null) ? Actor.Animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            Transform leftHand = (Actor != null && Actor.Animator != null) ? Actor.Animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            NpcActionApi candidateActions;
            try
            {
                candidateActions = new NpcActionApi(AgentId, InstanceWorldId, transform, rightHand, leftHand, AllInteractables);
            }
            catch
            {
                return false;
            }

            var hunterClub = GetComponentInChildren<HunterClubCarry>();
            if (hunterClub != null)
            {
                candidateActions.OnLeftHandOccupiedChanged += stowed => hunterClub.SetStowed(stowed);
            }

            if (PhysicalItems != null)
            {
                bool physicalSuccess = PhysicalItems.OnActionsCreated(candidateActions);
                if (!physicalSuccess)
                {
                    return false;
                }
            }

            // Physical transaction and candidate validation succeeded: proceed to nonphysical reset side effects
            if (OptionalPlanner != null) OptionalPlanner.ResetSession();
            if (Survival != null) Survival.Cancel("world-reset");
            if (Foraging != null) Foraging.ResetExpedition();
            if (Registry != null)
            {
                foreach (var item in Registry)
                {
                    if (item != null) item.RestoreInitial();
                }
            }
            Tick = requestId = gestureTicks = stalledTicks = FailureCount = 0;
            goal = null; route = null; retryAfter.Clear(); ChosenGoals.Clear();
            if (Log != null) Log.ResetLog();
            perceptionSignature = previousWait = ""; Phase = "Observe"; LastResult = "Waiting for perception"; LastFailureDiagnostic = "none";
            Running = true; MenuPaused = false; Possessed = false; ManualDirection = Vector3.zero;
            if (Actor != null)
            {
                Actor.Place(SpawnPosition);
                Actor.transform.rotation = Quaternion.identity;
            }

            Actions = candidateActions;
            if (PhysicalItems != null && PhysicalItems.Model != null && PhysicalItems.Model.HighestReceiptRequestId > requestId)
            {
                requestId = PhysicalItems.Model.HighestReceiptRequestId;
            }
            Physics.SyncTransforms();
            return true;
        }

        public int RequestId => requestId;

        public bool ResumeRequestSequence(int persistedBound)
        {
            if (persistedBound < 0) return false;
            if (persistedBound > requestId)
            {
                requestId = persistedBound;
            }
            return true;
        }

        public bool TryAllocateRequestId(out int allocatedId)
        {
            int floor = requestId;
            var model = Actions != null && Actions.PhysicalModel != null
                ? Actions.PhysicalModel
                : (PhysicalItems != null ? PhysicalItems.Model : null);
            if (model != null && model.HighestReceiptRequestId > floor)
            {
                floor = model.HighestReceiptRequestId;
            }

            if (floor >= int.MaxValue)
            {
                allocatedId = -1;
                return false;
            }

            requestId = floor + 1;
            allocatedId = requestId;
            return true;
        }

        public NpcActionResult ExecutePlayerAction(NpcActionKind kind, string targetId)
        {
            if (Actions == null) return new NpcActionResult { success = false, code = "actions-uninitialized" };
            if (!TryAllocateRequestId(out int req))
                return new NpcActionResult { success = false, code = "request-id-overflow" };
            var result = Actions.Execute(req, kind, targetId);
            if (Log != null)
            {
                Log.Record(Tick, result.success ? "result" : "failure", DescribePerception(), targetId ?? "", kind.ToString(), result.code,
                    result.success ? "player-directed action completed" : "player-directed action rejected: " + result.code);
            }
            LastResult = result.code;
            return result;
        }

        public NpcActionResult ExecutePlayerAction(NpcActionKind kind, string itemId, string containerId)
        {
            if (Actions == null) return new NpcActionResult { success = false, code = "actions-uninitialized" };
            if (!TryAllocateRequestId(out int req))
                return new NpcActionResult { success = false, code = "request-id-overflow" };
            var result = Actions.Execute(req, kind, itemId, containerId);
            if (Log != null)
            {
                Log.Record(Tick, result.success ? "result" : "failure", DescribePerception(), (itemId ?? "") + "->" + (containerId ?? ""), kind.ToString(), result.code,
                    result.success ? "player-directed action completed" : "player-directed action rejected: " + result.code);
            }
            LastResult = result.code;
            return result;
        }
        private void FixedUpdate() { if (Ready && !ManualSimulation) StepTick(); }
        public void SetPossession(bool possessed)
        {
            if (Possessed == possessed) return;
            if (OptionalPlanner != null) OptionalPlanner.Cancel("possession-change");
            if (Survival != null) Survival.Cancel("possession-change");
            Possessed = possessed; ManualDirection = Vector3.zero; gestureTicks = 0; Actor.CancelGesture();
            if (!possessed) Running = true;
            if (!possessed && goal != null)
            {
                route = PlanRoute(goal.approach);
                previousPosition = transform.position; stalledTicks = 0; lastSeenTick = Tick;
                if (route == null) Fail("no-route-after-possession");
            }
            Log.Record(Tick, "control", DescribePerception(), GoalId, possessed ? "possess-same-inhabitant" : "release-to-autonomy",
                "identity, cargo, goal context and decision history retained");
        }
        public void Pause()
        {
            if (OptionalPlanner != null) OptionalPlanner.Cancel("autonomy-paused");
            if (Survival != null) Survival.Cancel("autonomy-paused");
            Running = false;
        }
        public void ToggleAutonomy()
        {
            if (Possessed) return;
            Running = !Running;
            if (!Running && OptionalPlanner != null) OptionalPlanner.Cancel("autonomy-paused");
            if (!Running && Survival != null) Survival.Cancel("autonomy-paused");
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
            if (Survival != null && Survival.Enabled && Survival.Food.Model.State.body.dead && Possessed)
            { Actor.Step(Vector3.zero, StepSeconds); return; }
            if (Possessed)
            {
                Actor.Step(ManualDirection, StepSeconds);
                if (Survival != null && Survival.Enabled)
                {
                    Survival.RememberCurrentWorld();
                }
                return;
            }
            if (!Running) { Actor.Step(Vector3.zero, StepSeconds); return; }
            bool hasAuthoredLegacyItems = Registry != null && Registry.Any(x => x != null && x.Kind == NpcObjectKind.Item && x.GetComponent<CityLife.Items.PhysicalItem>() == null);
            bool isHoldingFood = Actions != null && Actions.Held != null && (
                Actions.Held.StableId.Contains("fish") || Actions.Held.StableId.Contains("crab") ||
                Actions.Held.StableId.Contains("berry") || Actions.Held.StableId.Contains("food") ||
                Actions.Held.StableId.Contains("fruit") ||
                (Actions.Held.GetComponent<CityLife.Items.PhysicalItem>() != null &&
                 (Actions.Held.GetComponent<CityLife.Items.PhysicalItem>().itemTypeId.StartsWith("food") ||
                  Actions.Held.GetComponent<CityLife.Items.PhysicalItem>().itemTypeId == "fruit")));
            bool isHoldingTool = Actions != null && Actions.Held != null && (
                Actions.Held.StableId.Contains("club") || Actions.Held.StableId.Contains("rod") ||
                Actions.Held.StableId.Contains("tool") ||
                (Actions.Held.GetComponent<CityLife.Items.PhysicalItem>() != null &&
                 Actions.Held.GetComponent<CityLife.Items.PhysicalItem>().itemTypeId.StartsWith("tool")));
            bool allowSurvival = Actions == null || Actions.Held == null || isHoldingFood || isHoldingTool;
            bool survivalPriority = Survival != null && Survival.Enabled && (Survival.Food.Model.State.body.dead ||
                isHoldingFood ||
                (Survival.Food.Model.State.satiety < 7000 && allowSurvival) ||
                (Survival.Food.Model.State.hydration < 7000 && allowSurvival) ||
                // A scoped continuation earned in a prior real delivery cycle
                // resumes survival without inventing delivery state in this
                // reconstructed world. Never override a currently held item unless it's food.
                (Survival.VerifiedScopedContinuation && allowSurvival) ||
                (!hasAuthoredLegacyItems && allowSurvival) ||
                (goal == null && allowSurvival && (!hasAuthoredLegacyItems || (Actions.Deliveries >= 3 &&
                    Registry.Where(x => x.Kind == NpcObjectKind.Item && x.Permission && x.GetComponent<CityLife.Items.PhysicalItem>() == null).All(x => x.DeliveredTo.Length > 0)))));
            if (survivalPriority)
            { Phase = "Survive / grounded model"; if (Survival.StepTick()) return; }
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
                    if (!TryAllocateRequestId(out int req))
                    {
                        Fail("request-id-overflow", true);
                        Actor.Step(Vector3.zero, StepSeconds);
                        return;
                    }
                    var result = Actions.Execute(req, kind, GoalId);
                    if (MemoryExport != null && result.success && !result.duplicate)
                    {
                        try { MemoryExport.CompletedAction(AgentId, Tick, req, kind, GoalId, carriedItem, result); }
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
                    bool hasAuthoredItems = Registry != null && Registry.Any(x => x != null && x.Kind == NpcObjectKind.Item);
                    bool deliveriesFinished = !hasAuthoredItems || (Actions.Deliveries >= 3 || (Actions.Deliveries > 0 && Registry.Where(x => x != null && x.Kind == NpcObjectKind.Item && x.Permission).All(x => !string.IsNullOrEmpty(x.DeliveredTo))));
                    if (deliveriesFinished && Actions.Held == null)
                    {
                        if (Foraging == null) Foraging = GetComponent<ForagingExpeditionCycle>();
                        if (Foraging != null && Foraging.StepAutonomousLiving(this, Actor, StepSeconds))
                        {
                            Phase = "Living / " + Foraging.Phase;
                            LastResult = "Active living: " + Foraging.Phase;
                            return;
                        }
                    }

                    Phase = "Wait";
                    string reason = Actions.Held == null ? "no eligible perceived item" : "no eligible perceived destination; retain cargo";
                    if (reason != previousWait)
                    { Log.Record(Tick, "fallback", DescribePerception(), "", "wait-and-resense", reason, "sense again every 10 ticks"); previousWait = reason; }
                    Actor.Step(Vector3.zero, StepSeconds); return;
                }
                previousWait = ""; lastSeenTick = goal.seenAtTick; ChosenGoals.Add(goal.id);
                Log.Record(Tick, "goal", DescribePerception(), goal.id,
                    Actions.Held == null ? "collect" : "deliver", proposed ? "validated optional proposal; deterministic navigation and action checks" : "nearest eligible perception; distance=" + goal.distanceMillimetres + "mm; stable-id tie break");
                route = PlanRoute(goal.approach);
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
        private Queue<Vector3> PlanRoute(Vector3 target) => TerrainNavigation == null ? NpcGridNavigation.Plan(transform.position, target) : TerrainNavigation.Plan(transform.position, target);
        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        private void Fail(string code, bool record = true)
        {
            FailureCount++; LastResult = code;
            Vector3 next = route != null && route.Count > 0 ? route.Peek() : transform.position;
            string contacts = string.Join(",", Physics.OverlapSphere(transform.position + Vector3.up * .65f, .55f,
                ~0, QueryTriggerInteraction.Ignore)
                .Where(collider => !collider.transform.IsChildOf(transform))
                .Select(collider => collider.name + "[layer=" + collider.gameObject.layer + "]")
                .Distinct().OrderBy(name => name));
            LastFailureDiagnostic = "code=" + code + "; actor=" + transform.position + "; next=" + next +
                "; goal=" + GoalId + "; contacts=" + (contacts.Length == 0 ? "none" : contacts);
            if (record) Log.Record(Tick, "failure", DescribePerception(), GoalId, "stop-current-action", code,
                "keep cargo; exclude goal for 250 ticks; choose again; " + LastFailureDiagnostic);
            if (goal != null) retryAfter[goal.id] = Tick + 250;
            goal = null; route = null; gestureTicks = 0; stalledTicks = 0; Phase = "Fallback";
        }
        public string DescribePerception() => string.Join(", ", Perception.Current.Select(x =>
            x.id + (x.permission ? "" : " [denied]") + (x.available ? "" : " [unavailable]")));
    }
}
