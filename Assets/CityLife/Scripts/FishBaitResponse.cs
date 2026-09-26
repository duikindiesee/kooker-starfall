using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    // One submerged bait competes for attention; only its claimant leaves the school.
    // Elapsed simulation time and stable fish identity determine independent decisions.
    public sealed class FishBaitResponse
    {
        public const float DetectionRadius = 7.5f;
        public const float ContactRadius = 0.22f;
        public RiverFishSchool.RiverFishInstance InterestedFish { get; private set; }
        public float Elapsed { get; private set; }
        public bool AtBait { get; private set; }
        public int IgnoredDecisions { get; private set; }
        private readonly Dictionary<RiverFishSchool.RiverFishInstance, float> nextDecision = new();
        private readonly Dictionary<RiverFishSchool.RiverFishInstance, int> attempts = new();
        private int castSequence;

        public void Begin(int sequence)
        {
            Release();
            castSequence = sequence;
            Elapsed = 0;
            IgnoredDecisions = 0;
            nextDecision.Clear();
            attempts.Clear();
        }

        public static float Personality(RiverFishSchool.RiverFishInstance fish, int salt)
        {
            string id = fish.interactable != null ? fish.interactable.StableId : fish.gameObject.name;
            uint hash = 2166136261;
            unchecked
            {
                foreach (char c in id ?? "fish") hash = (hash ^ c) * 16777619;
                hash = (hash ^ (uint)salt) * 16777619;
                hash ^= hash >> 16;
                hash *= 2246822519;
                hash ^= hash >> 13;
            }
            return (hash & 0xffffff) / 16777216f;
        }

        public void Tick(float dt, Vector3 bait, IReadOnlyList<RiverFishSchool.RiverFishInstance> fish)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            Elapsed += dt;
            if (InterestedFish != null && !Available(InterestedFish, true)) Release();
            if (InterestedFish == null && fish != null)
            {
                RiverFishSchool.RiverFishInstance winner = null;
                float earliest = float.MaxValue;
                foreach (var f in fish)
                {
                    if (!Available(f, false)) continue;
                    if ((f.gameObject.transform.position - bait).sqrMagnitude > DetectionRadius * DetectionRadius) continue;
                    if (!nextDecision.TryGetValue(f, out float due))
                    {
                        due = Elapsed + 2f + Personality(f, castSequence) * 6f;
                        nextDecision[f] = due;
                    }
                    if (Elapsed < due) continue;
                    attempts.TryGetValue(f, out int attempt);
                    attempts[f] = attempt + 1;
                    nextDecision[f] = Elapsed + 3f + Personality(f, attempt + 37) * 5f;
                    float interest = 0.30f + Personality(f, 71) * 0.40f;
                    if (Personality(f, castSequence * 101 + attempt + 17) > interest)
                    {
                        IgnoredDecisions++;
                        continue;
                    }
                    if (due < earliest && WaterPath(f.gameObject.transform.position, bait))
                    {
                        earliest = due;
                        winner = f;
                    }
                }
                if (winner != null)
                {
                    InterestedFish = winner;
                    winner.isReserved = true;
                    winner.fishingControlled = true;
                }
            }

            if (InterestedFish == null) return;
            var fishTransform = InterestedFish.gameObject.transform;
            if (!WaterPath(fishTransform.position, bait)) { Release(); return; }
            Vector3 delta = bait - fishTransform.position;
            float speed = 0.55f + Personality(InterestedFish, 83) * 0.55f;
            fishTransform.position = Vector3.MoveTowards(fishTransform.position, bait, speed * dt);
            if (delta.sqrMagnitude > 0.001f)
                fishTransform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            AtBait = Vector3.Distance(fishTransform.position, bait) <= ContactRadius;
        }

        private static bool Available(RiverFishSchool.RiverFishInstance f, bool owned)
        {
            return f != null && f.gameObject != null && f.gameObject.activeSelf &&
                (owned || !f.isReserved) && (f.physicalItem == null ||
                (!f.physicalItem.IsCarried && !f.physicalItem.IsStored));
        }

        private static bool WaterPath(Vector3 from, Vector3 to)
        {
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.4f));
            for (int i = 0; i <= samples; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, (float)i / samples);
                if (CoastalTerrain.Height(p.x, p.z) > p.y - 0.08f ||
                    p.y > CoastalWater.CurrentLevel - 0.08f) return false;
            }
            return true;
        }

        public void Release()
        {
            if (InterestedFish != null)
            {
                InterestedFish.isReserved = false;
                InterestedFish.fishingControlled = false;
            }
            InterestedFish = null;
            AtBait = false;
        }
    }
}
