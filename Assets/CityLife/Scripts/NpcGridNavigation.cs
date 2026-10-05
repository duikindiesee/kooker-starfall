using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    public static class NpcGridNavigation
    {
        private static readonly Vector2Int[] Neighbours = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        public static Queue<Vector3> Plan(Vector3 origin, Vector3 goal)
        {
            Physics.SyncTransforms();
            var start = new Vector2Int(Mathf.RoundToInt(origin.x), Mathf.RoundToInt(origin.z));
            var end = new Vector2Int(Mathf.RoundToInt(goal.x), Mathf.RoundToInt(goal.z));
            var open = new Queue<Vector2Int>(); open.Enqueue(start);
            var parents = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var blocked = new HashSet<Vector2Int>();
            while (open.Count > 0 && parents.Count <= 441)
            {
                var p = open.Dequeue(); if (p == end) break;
                foreach (var step in Neighbours)
                {
                    var q = p + step;
                    if (Mathf.Abs(q.x) > 10 || Mathf.Abs(q.y) > 10 || parents.ContainsKey(q) || blocked.Contains(q)) continue;
                    Vector3 v = new Vector3(q.x, 0, q.y), from = new Vector3(p.x, 0, p.y);
                    if (Physics.CheckCapsule(v + Vector3.up * .46f, v + Vector3.up * 1.5f, .42f, 1 << 8, QueryTriggerInteraction.Ignore))
                    { blocked.Add(q); continue; }
                    Vector3 delta = v - from;
                    if (Physics.CapsuleCast(from + Vector3.up * .46f, from + Vector3.up * 1.5f, .42f,
                        delta, delta.magnitude, 1 << 8, QueryTriggerInteraction.Ignore)) continue;
                    parents.Add(q, p); open.Enqueue(q);
                }
            }
            if (!parents.ContainsKey(end)) return null;
            var reverse = new List<Vector3>(); var current = end;
            while (current != start) { reverse.Add(new Vector3(current.x, 0, current.y)); current = parents[current]; }
            reverse.Reverse(); return new Queue<Vector3>(reverse);
        }
    }
}
