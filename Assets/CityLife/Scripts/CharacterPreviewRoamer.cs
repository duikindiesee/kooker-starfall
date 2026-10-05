using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    // Authored goals + bounded grid search. No learning, memory model, or external services.
    [DefaultExecutionOrder(-20)]
    public sealed class CharacterPreviewRoamer : MonoBehaviour
    {
        public CharacterPreviewActor Actor;
        public Transform Crystal, SourceStand, DestinationStand, DestinationSocket;
        public bool Running = true;
        public bool Complete { get; private set; }
        public bool Carrying { get; private set; }
        public Vector3 Direction { get; private set; }
        public int Plans { get; private set; }
        public int BlockedCells { get; private set; }
        public List<Observation> Journal = new List<Observation>();
        public string Activity { get; private set; } = "Preparing a short expedition";
        private Queue<Vector3> route;
        private int phase;
        private float delay = 1.2f, stalled;
        private Vector3 previous;
        private static readonly Vector2Int[] Neighbours =
            { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        [Serializable] public sealed class Observation
        { public int sequence; public float seconds; public Vector3 position; public string observation, action, outcome; }
        private void Record(string observation, string action, string outcome)
        {
            Journal.Add(new Observation { sequence = Journal.Count + 1, seconds = Time.time,
                position = transform.position, observation = observation, action = action, outcome = outcome });
        }
        public void Pause() { Running = false; Direction = Vector3.zero; }
        public void Toggle()
        {
            if (Complete) return;
            Running = !Running; Direction = Vector3.zero; route = null;
        }
        public void Begin()
        {
            Running = true; Complete = false; Carrying = false;
            phase = 0; delay = .2f; stalled = 0; route = null;
            Journal.Clear(); Plans = 0; BlockedCells = 0;
        }
        private void Update()
        {
            Direction = Vector3.zero;
            if (!Running || Complete || Actor.TestControl) return;
            delay -= Time.deltaTime;
            if (delay > 0) return;
            if (phase == 1)
            {
                if (Vector3.Distance(transform.position, SourceStand.position) > .6f)
                { Fail("Collect stand moved outside reach."); return; }
                var hand = Actor.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                Crystal.SetParent(hand, false); Crystal.localPosition = new Vector3(.06f, .04f, 0);
                Crystal.localRotation = Quaternion.identity; Carrying = true;
                Record("Crystal still present at the source stand.", "Attach the crystal to the right hand.", "Carrying one crystal.");
                phase = 2; route = null;
            }
            if (phase == 3)
            {
                if (!Carrying || Vector3.Distance(transform.position, DestinationStand.position) > .6f)
                { Fail("Deposit preconditions not satisfied."); return; }
                Crystal.SetParent(DestinationSocket, false); Crystal.localPosition = Vector3.zero;
                Crystal.localRotation = Quaternion.identity; Carrying = false;
                Activity = "Crystal delivered; continuing continuous exploration";
                Record("Carried crystal and destination are within reach.", "Place the crystal on the destination.", "Source empty; destination occupied. Expedition complete; roaming the terrace.");
                phase = 4; delay = 1.8f; route = null;
                return;
            }
            if (phase >= 4)
            {
                Activity = "Exploring the living world";
                if (route == null || route.Count == 0)
                {
                    Vector3 roamTarget = new Vector3(
                        UnityEngine.Random.Range(-7f, 7f),
                        0,
                        UnityEngine.Random.Range(-7f, 7f)
                    );
                    route = Plan(roamTarget);
                    if (route == null)
                    {
                        route = new Queue<Vector3>();
                        route.Enqueue(transform.position + transform.forward * 1.5f);
                    }
                }
                while (route.Count > 0 && FlatDistance(transform.position, route.Peek()) < .2f) route.Dequeue();
                if (route.Count == 0)
                {
                    Actor.Gesture(); delay = 2.0f; stalled = 0; route = null;
                    Record("Completed exploration stroll.", "Look across the canyon.", "Resting briefly before next movement.");
                    return;
                }
                Vector3 diff = route.Peek() - transform.position; diff.y = 0;
                Direction = diff.normalized * Mathf.Min(1, diff.magnitude / Mathf.Max(.001f, Actor.WalkSpeed * Mathf.Min(Time.deltaTime, .05f)));
                if (FlatDistance(previous, transform.position) < .001f) stalled += Time.deltaTime; else stalled = 0;
                previous = transform.position;
                if (stalled > 2) { route = null; stalled = 0; }
                return;
            }
            Vector3 goal = phase == 0 ? SourceStand.position : DestinationStand.position;
            Activity = phase == 0 ? "Finding a route to the crystal" : "Carrying the crystal to its destination";
            if (route == null)
            {
                route = Plan(goal);
                if (route == null) { Fail("No traversable route inside the test stage."); return; }
            }
            while (route.Count > 0 && FlatDistance(transform.position, route.Peek()) < .15f) route.Dequeue();
            if (route.Count == 0)
            {
                Actor.Gesture(); phase++; delay = 2.05f; stalled = 0;
                Record("Reached the authored interaction stand; range checked.", "Play the free Interact clip.", "Interaction in progress.");
                return;
            }
            Vector3 difference = route.Peek() - transform.position; difference.y = 0;
            Direction = difference.normalized * Mathf.Min(1, difference.magnitude / Mathf.Max(.001f, Actor.WalkSpeed * Mathf.Min(Time.deltaTime, .05f)));
            if (FlatDistance(previous, transform.position) < .001f) stalled += Time.deltaTime; else stalled = 0;
            previous = transform.position;
            if (stalled > 2) { Fail("Movement stalled for two seconds; stopped safely."); }
        }
        private void Fail(string why)
        {
            Pause(); Activity = "Expedition paused";
            Record(why, "Stop movement.", "Needs inspection; no successful outcome claimed.");
        }
        private static float FlatDistance(Vector3 a, Vector3 b)
        { a.y = b.y = 0; return Vector3.Distance(a, b); }
        private Queue<Vector3> Plan(Vector3 goal)
        {
            Physics.SyncTransforms();
            var start = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.z));
            var end = new Vector2Int(Mathf.RoundToInt(goal.x), Mathf.RoundToInt(goal.z));
            var open = new Queue<Vector2Int>(); open.Enqueue(start);
            var parents = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var blocked = new HashSet<Vector2Int>();
            while (open.Count > 0 && parents.Count <= 441)
            {
                var p = open.Dequeue();
                if (p == end) break;
                foreach (var offset in Neighbours)
                {
                    var q = p + offset;
                    if (Mathf.Abs(q.x) > 10 || Mathf.Abs(q.y) > 10 || parents.ContainsKey(q) || blocked.Contains(q)) continue;
                    Vector3 v = new Vector3(q.x, 0, q.y);
                    if (Physics.CheckCapsule(v + Vector3.up * .46f, v + Vector3.up * 1.5f, .42f, 1 << 8, QueryTriggerInteraction.Ignore))
                    { blocked.Add(q); continue; }
                    Vector3 from = new Vector3(p.x, .95f, p.y);
                    Vector3 delta = new Vector3(offset.x, 0, offset.y);
                    if (Physics.CapsuleCast(from - Vector3.up * .49f, from + Vector3.up * .55f, .42f,
                        delta, delta.magnitude, 1 << 8, QueryTriggerInteraction.Ignore)) continue;
                    parents.Add(q, p); open.Enqueue(q);
                }
            }
            Plans++; BlockedCells += blocked.Count;
            if (!parents.ContainsKey(end)) return null;
            var reverse = new List<Vector3>(); var current = end;
            while (current != start) { reverse.Add(new Vector3(current.x, 0, current.y)); current = parents[current]; }
            reverse.Reverse();
            Record("Authored destination; collision queries found " + blocked.Count + " blocked grid cells.",
                "Search a bounded one-metre grid.", "Route contains " + reverse.Count + " clear steps; no learning involved.");
            return new Queue<Vector3>(reverse);
        }
        private GUIStyle title, text;
        private void OnGUI()
        {
            if (CharacterPreviewSmoke.Requested) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            }
            GUI.Box(new Rect(18, 18, 490, 190), "");
            GUI.Label(new Rect(34, 30, 460, 32), "STARFALL  /  First inhabitant", title);
            GUI.Label(new Rect(34, 66, 450, 70), Activity + "\n" +
                (Running ? "Scripted expedition" : Complete ? "Delivery finished" : "Expedition paused") +
                "  ·  " + (Carrying ? "Carrying a crystal" : "Hands free"), text);
            GUI.Label(new Rect(34, 136, 450, 62), "WASD walk  ·  Hold right mouse to look\nR resume/pause expedition  ·  Esc stop and release pointer", text);
            if (Journal.Count > 0)
            {
                var last = Journal[Journal.Count - 1];
                GUI.Box(new Rect(18, Screen.height - 135, 650, 116), "");
                GUI.Label(new Rect(34, Screen.height - 123, 615, 98),
                    "Observation: " + last.observation + "\nAction: " + last.action + "\nOutcome: " + last.outcome, text);
            }
        }
    }
}
