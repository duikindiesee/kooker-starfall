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
            if (!TryGround(p, out float h, out Vector3 n) || n.y < Mathf.Cos(45 * Mathf.Deg2Rad) || h < WaterLevel(p) - .85f) return false;
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
        public Queue<Vector3> Plan(Vector3 origin, Vector3 goal) => PlanInternal(origin, goal, null);

        public Queue<Vector3> PlanExplored(Vector3 origin, Vector3 goal, HashSet<string> exploredCellKeys) => PlanInternal(origin, goal, exploredCellKeys);

        private Queue<Vector3> PlanInternal(Vector3 origin, Vector3 goal, HashSet<string> exploredCells)
        {
            Physics.SyncTransforms();
            var start = new Vector2Int(Mathf.RoundToInt(origin.x), Mathf.RoundToInt(origin.z));
            var end = new Vector2Int(Mathf.RoundToInt(goal.x), Mathf.RoundToInt(goal.z));

            // If destination cell is not walkable (e.g. inside boulder or water), search nearest walkable cell within 4m
            if (!Walkable(new Vector3(end.x, 0, end.y), out _))
            {
                float bestOffset = float.MaxValue;
                Vector2Int adjustedEnd = end;
                for (int dx = -4; dx <= 4; dx++)
                for (int dz = -4; dz <= 4; dz++)
                {
                    var cand = new Vector2Int(end.x + dx, end.y + dz);
                    if (Walkable(new Vector3(cand.x, 0, cand.y), out _))
                    {
                        float d = dx * dx + dz * dz;
                        if (d < bestOffset) { bestOffset = d; adjustedEnd = cand; }
                    }
                }
                end = adjustedEnd;
            }

            var gScore = new Dictionary<Vector2Int, float> { [start] = 0f };
            var parent = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var positions = new Dictionary<Vector2Int, Vector3> { [start] = origin };
            var blocked = new HashSet<Vector2Int>();
            var open = new List<(Vector2Int pos, float fScore)> { (start, Vector2.Distance(start, end)) };

            Vector2Int closest = start;
            float closestDist = Vector2.Distance(start, end);
            var steps = new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

            while (open.Count > 0 && parent.Count < 16384)
            {
                int bestIdx = 0;
                float bestF = open[0].fScore;
                for (int i = 1; i < open.Count; i++)
                {
                    if (open[i].fScore < bestF) { bestF = open[i].fScore; bestIdx = i; }
                }
                var p = open[bestIdx].pos;
                open.RemoveAt(bestIdx);

                if (p == end) { closest = end; break; }

                float currentDist = Vector2.Distance(p, end);
                if (currentDist < closestDist)
                {
                    closestDist = currentDist;
                    closest = p;
                }

                float currentG = gScore[p];
                foreach (var step in steps)
                {
                    var q = p + step;
                    if (blocked.Contains(q)) continue;
                    if (exploredCells != null)
                    {
                        int cx = Mathf.RoundToInt(q.x / 3f);
                        int cz = Mathf.RoundToInt(q.y / 3f);
                        if (!exploredCells.Contains(cx + ":" + cz)) continue;
                    }
                    if (!Walkable(new Vector3(q.x, 0, q.y), out Vector3 floor)) { blocked.Add(q); continue; }
                    var from = positions[p];
                    if (Mathf.Abs(floor.y - from.y) > .5f) continue;
                    var delta = floor - from;
                    if (Physics.CapsuleCast(from + Vector3.up * .45f, from + Vector3.up * 1.5f, .35f,
                        delta.normalized, delta.magnitude, 1 << 8, QueryTriggerInteraction.Ignore)) continue;

                    float tentativeG = currentG + 1f;
                    if (!gScore.TryGetValue(q, out float prevG) || tentativeG < prevG)
                    {
                        gScore[q] = tentativeG;
                        parent[q] = p;
                        positions[q] = floor;
                        float f = tentativeG + Vector2.Distance(q, end);
                        open.Add((q, f));
                    }
                }
            }

            Vector2Int targetReached = parent.ContainsKey(end) ? end : (closestDist <= 3.0f ? closest : start);
            if (targetReached == start && start != end) return null;

            var path = new List<Vector3>();
            var curr = targetReached;
            while (curr != start) { path.Add(positions[curr]); curr = parent[curr]; }
            path.Reverse();
            return new Queue<Vector3>(path);
        }
    }
}
