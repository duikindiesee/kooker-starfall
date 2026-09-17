using System.Collections.Generic;
using UnityEngine;
using Starfall.EnvironmentFoundation;

namespace CityLife.World
{
    // The regional adapter queries the same mesh collider that the player stands on.
    public sealed class NpcTerrainNavigation : MonoBehaviour, IEnvironmentSurface
    {
        public const string RegionId = "starfall.integrated-coastal.v1";
        public IslandField IslandField;
        public string WorldId => IslandField != null ? IslandField.Definition.worldId : RegionId;
        public string Revision => IslandField != null ? IslandField.Definition.Fingerprint() : CoastalTerrain.ContentRevision + ".integrated1";
        public Bounds PhysicalBounds => IslandField != null
            ? new Bounds(Vector3.zero, new Vector3(IslandField.Definition.Width, 800f, IslandField.Definition.Width))
            : new Bounds(
                new Vector3((CoastalTerrain.MinX + CoastalTerrain.MaxX) * .5f, 50,
                    (CoastalTerrain.MinZ + CoastalTerrain.MaxZ) * .5f),
                new Vector3(CoastalTerrain.MaxX - CoastalTerrain.MinX, 400,
                    CoastalTerrain.MaxZ - CoastalTerrain.MinZ));
        public bool Contains(Vector3 p) => EnvironmentClock.Finite(p.sqrMagnitude) && PhysicalBounds.Contains(p);
        public bool TryGround(Vector3 p, out float height, out Vector3 normal)
        {
            height = 0; normal = Vector3.up;
            if (!Contains(p)) return false;
            if (IslandField != null)
            {
                height = IslandField.Ground(p.x, p.z);
                double gx = p.x / IslandField.Definition.cellMetres + IslandField.Definition.cells / 2.0;
                double gz = p.z / IslandField.Definition.cellMetres + IslandField.Definition.cells / 2.0;
                normal = IslandField.Normal(Mathf.RoundToInt((float)gx), Mathf.RoundToInt((float)gz));
                return true;
            }
            if (!Physics.Raycast(new Vector3(p.x, 170, p.z), Vector3.down,
                out RaycastHit hit, 300, 1 << 10, QueryTriggerInteraction.Ignore)) return false;
            height = hit.point.y; normal = hit.normal; return true;
        }
        public float WaterLevel(Vector3 p) => IslandField != null ? 0f : CoastalWater.Level;
        public float WaterDepth(Vector3 p) => TryGround(p, out float h, out _) ? Mathf.Max(0, WaterLevel(p) - h) : 0;
        public Vector3 Current(Vector3 p) => Vector3.zero;
        public bool Walkable(Vector3 p, out Vector3 floor)
        {
            floor = p;
            if (!TryGround(p, out float h, out Vector3 n) || n.y < Mathf.Cos(45 * Mathf.Deg2Rad) || h < WaterLevel(p) - .2f) return false;
            floor.y = h;
            return !Physics.CheckCapsule(floor + Vector3.up * .45f, floor + Vector3.up * 1.5f,
                .35f, 1 << 8, QueryTriggerInteraction.Ignore);
        }
        public Vector3 ConstrainMotion(Vector3 position, Vector3 direction, float distance)
        {
            var next = position + Vector3.ClampMagnitude(direction, 1) * (distance + .3f);
            if (!Walkable(next, out Vector3 floor) || floor.y - position.y > .28f) return Vector3.zero;
            return direction;
        }
        public Queue<Vector3> Plan(Vector3 origin, Vector3 goal)
        {
            Physics.SyncTransforms();
            var start = new Vector2Int(Mathf.RoundToInt(origin.x), Mathf.RoundToInt(origin.z));
            var end = new Vector2Int(Mathf.RoundToInt(goal.x), Mathf.RoundToInt(goal.z));
            var open = new Queue<Vector2Int>(); open.Enqueue(start);
            var parent = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var positions = new Dictionary<Vector2Int, Vector3> { [start] = origin };
            var blocked = new HashSet<Vector2Int>();
            var steps = new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
            while (open.Count > 0 && parent.Count < 8192)
            {
                var p = open.Dequeue(); if (p == end) break;
                foreach (var step in steps)
                {
                    var q = p + step;
                    if (parent.ContainsKey(q) || blocked.Contains(q)) continue;
                    if (!Walkable(new Vector3(q.x, 0, q.y), out Vector3 floor)) { blocked.Add(q); continue; }
                    var from = positions[p];
                    if (Mathf.Abs(floor.y - from.y) > .5f) continue;
                    var delta = floor - from;
                    if (Physics.CapsuleCast(from + Vector3.up * .45f, from + Vector3.up * 1.5f, .35f,
                        delta.normalized, delta.magnitude, 1 << 8, QueryTriggerInteraction.Ignore)) continue;
                    parent[q] = p; positions[q] = floor; open.Enqueue(q);
                }
            }
            if (!parent.ContainsKey(end)) return null;
            var path = new List<Vector3>(); var current = end;
            while (current != start) { path.Add(positions[current]); current = parent[current]; }
            path.Reverse(); return new Queue<Vector3>(path);
        }
    }
}
