using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    public enum VisionZone
    {
        Proximity,        // Immediate 360-degree touch/hearing proximity <= 3.5m
        Focal,            // Focused forward vision <= 65 deg, items up to 25m
        Peripheral,       // Peripheral side vision 65 - 110 deg, items up to 8.5m
        DistantLandmark   // Salient landmarks (refuge, hearth, groves, wolves) up to 65m in focal vision
    }

    [Serializable] public sealed class NpcObservation
    {
        public string id;
        public string observedType;
        public NpcObjectKind kind;
        public bool permission, available;
        public int distanceMillimetres;
        public Vector3 position, approach;
        public int seenAtTick;
        public float viewingAngle;
        public VisionZone visionZone;
    }
    public sealed class NpcPerception : MonoBehaviour
    {
        public float Radius = 25f;
        public float DistantLandmarkRadius = 65f;
        public string WorldId = "starfall.npc-courtyard.v1";
        public bool DirectionalVision = true;
        public List<NpcObservation> Current { get; private set; } = new List<NpcObservation>();
        public int OccludedCount { get; private set; }
        public List<NpcObservation> Sense(int tick)
        {
            Physics.SyncTransforms();
            var seen = new SortedDictionary<string, NpcObservation>(StringComparer.Ordinal);
            OccludedCount = 0;
            float maxSenseRadius = DirectionalVision ? DistantLandmarkRadius : Radius;
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 forward = transform.forward; forward.y = 0;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            else forward.Normalize();

            foreach (var collider in Physics.OverlapSphere(transform.position, maxSenseRadius, (1 << 11) | (1 << 9), QueryTriggerInteraction.Collide))
            {
                var item = collider.GetComponentInParent<NpcInteractable>();
                if (item == null || !item.isActiveAndEnabled || item.WorldId != WorldId || string.IsNullOrEmpty(item.StableId)) continue;
                if (item.transform == transform || item.transform.IsChildOf(transform)) continue;

                Vector3 ray = item.SightPoint - eye;
                float dist = ray.magnitude;
                if (dist > maxSenseRadius) continue;

                float angle = 0f;
                VisionZone zone = VisionZone.Proximity;

                if (DirectionalVision)
                {
                    Vector3 flatRay = ray; flatRay.y = 0;
                    if (flatRay.sqrMagnitude > 0.001f)
                    {
                        flatRay.Normalize();
                        angle = Vector3.Angle(forward, flatRay);
                    }

                    bool isSalient = item.Kind == NpcObjectKind.Place ||
                                     item.StableId.StartsWith("refuge", StringComparison.Ordinal) ||
                                     item.StableId.StartsWith("hearth", StringComparison.Ordinal) ||
                                     item.StableId.StartsWith("berry-food", StringComparison.Ordinal) ||
                                     item.StableId.Contains("wolf");

                    if (dist <= 3.5f)
                    {
                        zone = VisionZone.Proximity;
                    }
                    else if (angle <= 65f)
                    {
                        if (dist <= Radius)
                            zone = VisionZone.Focal;
                        else if (dist <= DistantLandmarkRadius && isSalient)
                            zone = VisionZone.DistantLandmark;
                        else
                            continue;
                    }
                    else if (angle <= 110f)
                    {
                        if (dist <= 8.5f)
                            zone = VisionZone.Peripheral;
                        else
                            continue;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    if (dist > Radius) continue;
                }

                if (Physics.Raycast(eye, ray.normalized, dist, (1 << 8) | (1 << 10), QueryTriggerInteraction.Ignore))
                {
                    OccludedCount++;
                    continue;
                }

                if (seen.ContainsKey(item.StableId)) continue;

                seen.Add(item.StableId, new NpcObservation {
                    id = item.StableId,
                    kind = item.Kind,
                    observedType = item.ObservedType,
                    permission = item.Permission,
                    available = item.Available,
                    position = item.SightPoint,
                    approach = item.Approach != null ? item.Approach.position : item.transform.position,
                    distanceMillimetres = Mathf.RoundToInt(dist * 1000),
                    seenAtTick = tick,
                    viewingAngle = angle,
                    visionZone = zone
                });
            }
            Current = new List<NpcObservation>(seen.Values);
            return Current;
        }
    }
}
