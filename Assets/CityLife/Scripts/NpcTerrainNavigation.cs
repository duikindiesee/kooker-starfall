using System;
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
        public float WaterLevel(Vector3 p) => IslandField != null ? 0f : CoastalWater.CurrentLevel;
        public float WaterDepth(Vector3 p) => TryGround(p, out float h, out _) ? Mathf.Max(0, WaterLevel(p) - h) : 0;
        public const float SafeMinX = -260f, SafeMaxX = 260f, SafeMinZ = -260f, SafeMaxZ = 380f;

        public bool IsWithinSafePerimeter(Vector3 p, float margin = 10f)
        {
            if (IslandField != null) return PhysicalBounds.Contains(p);
            if (p.x < SafeMinX + margin || p.x > SafeMaxX - margin ||
                p.z < SafeMinZ + margin || p.z > SafeMaxZ - margin)
                return false;
            if (p.x < CoastalTerrain.MinX + 25f || p.x > CoastalTerrain.MaxX - 25f ||
                p.z < CoastalTerrain.MinZ + 25f || p.z > CoastalTerrain.MaxZ - 25f)
                return false;
            return true;
        }

        public Vector3 Current(Vector3 p) => Vector3.zero;
        public bool Walkable(Vector3 p, out Vector3 floor)
        {
            floor = p;
            if (!TryGround(p, out float h, out Vector3 n) || n.y < Mathf.Cos(45 * Mathf.Deg2Rad) || h < WaterLevel(p) - .85f) return false;
            floor.y = h;
            return !Physics.CheckCapsule(floor + Vector3.up * .45f, floor + Vector3.up * 1.5f,
                .35f, 1 << 8, QueryTriggerInteraction.Ignore);
        }
        private bool IsTraversable(Vector3 p, Vector3 curPos, out Vector3 floor)
        {
            floor = p;
            float waterLvl = WaterLevel(p);
            if (!TryGround(p, out float h, out Vector3 n)) return false;
            floor.y = h;
            if (Physics.CheckCapsule(floor + Vector3.up * .45f, floor + Vector3.up * 1.5f, .35f, 1 << 8, QueryTriggerInteraction.Ignore))
                return false;

            bool inWater = curPos.y <= waterLvl + 0.15f || h < waterLvl - 0.5f;
            if (inWater)
            {
                // In water: allow swimming/wading and bank climbing without getting stuck
                return n.y >= 0.25f && (floor.y - curPos.y <= 1.2f || curPos.y <= waterLvl + 0.1f);
            }

            if (n.y < Mathf.Cos(45 * Mathf.Deg2Rad) || h < waterLvl - .85f) return false;
            return floor.y - curPos.y <= .45f;
        }

        public Vector3 ConstrainMotion(Vector3 position, Vector3 direction, float distance)
        {
            var next = position + Vector3.ClampMagnitude(direction, 1) * (distance + .2f);
            if (IsTraversable(next, position, out Vector3 floor) && IsWithinSafePerimeter(floor, 5f))
                return direction;

            // Try slight left/right deflections (30 degrees) to step around small obstacles/rocks
            Vector3 leftDeflect = Quaternion.Euler(0, -30f, 0) * direction;
            var nextLeft = position + Vector3.ClampMagnitude(leftDeflect, 1) * (distance + .2f);
            if (IsTraversable(nextLeft, position, out Vector3 floorLeft) && IsWithinSafePerimeter(floorLeft, 5f))
                return leftDeflect;

            Vector3 rightDeflect = Quaternion.Euler(0, 30f, 0) * direction;
            var nextRight = position + Vector3.ClampMagnitude(rightDeflect, 1) * (distance + .2f);
            if (IsTraversable(nextRight, position, out Vector3 floorRight) && IsWithinSafePerimeter(floorRight, 5f))
                return rightDeflect;

            return Vector3.zero;
        }

        public Vector3 FindNearestRiverBank(Vector3 origin)
        {
            float bestDist = float.MaxValue;
            Vector3 bestBank = new Vector3(0f, CoastalTerrain.Height(0f, -15f), -15f); // Shallow river ford default
            // Search along the freshwater river corridor specifically for true waterline / shallow ford contact:
            // y between waterLevel - 0.25m (ankle-deep ford) and waterLevel + 0.15m (immediate sandy waterline edge)
            for (float z = -250f; z <= 380f; z += 10f)
            {
                for (float x = -60f; x <= 80f; x += 5f)
                {
                    float y = CoastalTerrain.Height(x, z);
                    if (y >= CoastalWater.Level - 0.25f && y <= CoastalWater.Level + 0.15f &&
                        CoastalTerrain.IsFreshwaterRiver(x, z, y, CoastalWater.CurrentLevel))
                    {
                        if (Walkable(new Vector3(x, y, z), out Vector3 floor))
                        {
                            float d = Vector3.Distance(origin, floor);
                            if (d < bestDist)
                            {
                                bestDist = d;
                                bestBank = floor;
                            }
                        }
                    }
                }
            }
            // Fallback to shallow water margin if no immediate waterline cell matched
            if (bestDist == float.MaxValue)
            {
                for (float z = -250f; z <= 380f; z += 15f)
                {
                    for (float x = -60f; x <= 80f; x += 8f)
                    {
                        float y = CoastalTerrain.Height(x, z);
                        if (y >= CoastalWater.Level - 0.35f && y <= CoastalWater.Level + 0.45f &&
                            CoastalTerrain.IsFreshwaterRiver(x, z, y, CoastalWater.CurrentLevel))
                        {
                            if (Walkable(new Vector3(x, y, z), out Vector3 floor))
                            {
                                float d = Vector3.Distance(origin, floor);
                                if (d < bestDist)
                                {
                                    bestDist = d;
                                    bestBank = floor;
                                }
                            }
                        }
                    }
                }
            }
            return bestBank;
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
            var open = new AStarMinHeap();
            open.Push(start, Vector2.Distance(start, end));

            Vector2Int closest = start;
            float closestDist = Vector2.Distance(start, end);
            var steps = new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

            while (open.Count > 0 && parent.Count < 16384)
            {
                if (!open.TryPop(out var p, out float currentF)) break;

                if (p == end) { closest = end; break; }

                float currentDist = Vector2.Distance(p, end);
                if (currentDist < closestDist)
                {
                    closestDist = currentDist;
                    closest = p;
                }

                float currentG = gScore[p];
                // Lazy deletion check: skip stale entries whose G-score has already been improved
                if (currentF > currentG + currentDist + 0.001f) continue;

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
                        open.Push(q, f);
                    }
                }
            }

            Vector2Int targetReached = parent.ContainsKey(end) ? end : (closest != start ? closest : start);
            if (targetReached == start && start != end) return null;

            var path = new List<Vector3>();
            var curr = targetReached;
            while (curr != start) { path.Add(positions[curr]); curr = parent[curr]; }
            path.Reverse();
            return new Queue<Vector3>(path);
        }

        private sealed class AStarMinHeap
        {
            private struct HeapNode
            {
                public Vector2Int pos;
                public float fScore;
            }

            private HeapNode[] heap = new HeapNode[256];
            public int Count { get; private set; }

            public void Push(Vector2Int pos, float fScore)
            {
                if (Count == heap.Length)
                {
                    Array.Resize(ref heap, heap.Length * 2);
                }
                int i = Count++;
                heap[i] = new HeapNode { pos = pos, fScore = fScore };
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (heap[i].fScore >= heap[parent].fScore) break;
                    HeapNode tmp = heap[i]; heap[i] = heap[parent]; heap[parent] = tmp;
                    i = parent;
                }
            }

            public bool TryPop(out Vector2Int pos, out float fScore)
            {
                if (Count == 0)
                {
                    pos = default;
                    fScore = 0f;
                    return false;
                }
                pos = heap[0].pos;
                fScore = heap[0].fScore;
                Count--;
                if (Count > 0)
                {
                    heap[0] = heap[Count];
                    int i = 0;
                    while (true)
                    {
                        int left = (i << 1) + 1;
                        if (left >= Count) break;
                        int right = left + 1;
                        int best = (right < Count && heap[right].fScore < heap[left].fScore) ? right : left;
                        if (heap[best].fScore >= heap[i].fScore) break;
                        HeapNode tmp = heap[i]; heap[i] = heap[best]; heap[best] = tmp;
                        i = best;
                    }
                }
                return true;
            }
        }
    }
}
