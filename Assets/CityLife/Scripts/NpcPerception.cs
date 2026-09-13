using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    [Serializable] public sealed class NpcObservation
    {
        public string id;
        public NpcObjectKind kind;
        public bool permission, available;
        public int distanceMillimetres;
        public Vector3 position, approach;
        public int seenAtTick;
    }
    public sealed class NpcPerception : MonoBehaviour
    {
        public float Radius = 12;
        public string WorldId = "starfall.npc-courtyard.v1";
        public List<NpcObservation> Current { get; private set; } = new List<NpcObservation>();
        public int OccludedCount { get; private set; }
        public List<NpcObservation> Sense(int tick)
        {
            Physics.SyncTransforms();
            var seen = new SortedDictionary<string, NpcObservation>(StringComparer.Ordinal);
            OccludedCount = 0;
            foreach (var collider in Physics.OverlapSphere(transform.position, Radius, 1 << 11, QueryTriggerInteraction.Collide))
            {
                var item = collider.GetComponentInParent<NpcInteractable>();
                if (item == null || !item.isActiveAndEnabled || item.WorldId != WorldId || string.IsNullOrEmpty(item.StableId)) continue;
                if (Vector3.Distance(transform.position, item.SightPoint) > Radius) continue;
                Vector3 ray = item.SightPoint - (transform.position + Vector3.up * 1.6f);
                if (Physics.Raycast(transform.position + Vector3.up * 1.6f, ray.normalized, ray.magnitude, (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore))
                { OccludedCount++; continue; }
                if (seen.ContainsKey(item.StableId)) throw new InvalidOperationException("Duplicate visible object id: " + item.StableId);
                seen.Add(item.StableId, new NpcObservation { id = item.StableId, kind = item.Kind,
                    permission = item.Permission, available = item.Available, position = item.SightPoint,
                    approach = item.Approach != null ? item.Approach.position : item.transform.position,
                    distanceMillimetres = Mathf.RoundToInt(Vector3.Distance(transform.position, item.SightPoint) * 1000), seenAtTick = tick });
            }
            Current = new List<NpcObservation>(seen.Values);
            return Current;
        }
    }
}
