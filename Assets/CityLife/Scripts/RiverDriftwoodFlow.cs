using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    /// <summary>
    /// Autonomous river driftwood flotsam simulation.
    /// Manages buoyant driftwood logs and fallen timber branches floating down
    /// the freshwater canyon river from the southern waterfall gorge toward the sea.
    /// Provides continuous natural replenishment as logs drift downstream,
    /// beach along gravel sandbars, or get harvested by the inhabitant for hearth fuel and shelter.
    /// </summary>
    public sealed class RiverDriftwoodFlow : MonoBehaviour
    {
        public const int TargetFloatingLogCount = 6;
        public const float FlowSpeed = 0.72f; // ~0.72 m/s downstream river current

        [Serializable]
        public class FloatingLogInstance
        {
            public GameObject gameObject;
            public PhysicalItem physicalItem;
            public NpcInteractable interactable;
            public float speed;
            public float bobPhase;
            public float eddyYawSpeed;
            public bool isBeached;
            public float currentT; // Parameter along river path
        }

        public readonly List<FloatingLogInstance> ActiveLogs = new List<FloatingLogInstance>();

        private static readonly Vector2[] RiverPath = new[]
        {
            new Vector2(18f, -210f),  // Base of waterfall plunge pool run
            new Vector2(10f, -165f),  // Upper south canyon run
            new Vector2(-15f, -110f), // South meander bend
            new Vector2(-25f, -60f),  // Approaching river ford
            new Vector2(0f, -15f),    // Shallow river ford crossing
            new Vector2(18f, 35f),    // North canyon entrance
            new Vector2(5f, 95f),     // Mid-canyon reach
            new Vector2(-12f, 155f),  // Lower meander pool
            new Vector2(15f, 220f),   // Outer estuary reach
            new Vector2(0f, 320f)     // North delta opening
        };

        private float nextSpawnCheckTime;
        private int logSequenceCounter;

        public static RiverDriftwoodFlow Create(Transform parent, string worldId = "world-starfall-0")
        {
            var flowGo = new GameObject("River_Driftwood_Flow_System");
            flowGo.transform.SetParent(parent, false);
            var comp = flowGo.AddComponent<RiverDriftwoodFlow>();
            comp.InitializeLogs(worldId);
            return comp;
        }

        private void InitializeLogs(string worldId)
        {
            // Seed initial logs along the river path at varied stages of their downstream journey
            float[] initialTs = { 0.05f, 0.22f, 0.38f, 0.55f, 0.72f, 0.88f };
            for (int i = 0; i < initialTs.Length; i++)
            {
                SpawnLogAtProgress(initialTs[i], worldId);
            }
        }

        public FloatingLogInstance SpawnLogAtProgress(float progressT, string worldId)
        {
            Vector3 spawnPos = SampleRiverPosition(progressT);
            float groundY = CoastalTerrain.Height(spawnPos.x, spawnPos.z);
            float waterY = CoastalWater.CurrentLevel;
            spawnPos.y = Mathf.Max(groundY + 0.05f, waterY + 0.04f);

            int idNum = ++logSequenceCounter;
            string stableId = $"river-driftwood-flotsam-{idNum:D3}";

            var logGo = new GameObject(stableId, typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
            logGo.transform.SetParent(transform, false);
            logGo.transform.position = spawnPos;

            // Random initial driftwood yaw and slight roll/pitch
            float initYaw = UnityEngine.Random.Range(-180f, 180f);
            float initPitch = UnityEngine.Random.Range(-5f, 5f);
            logGo.transform.rotation = Quaternion.Euler(initPitch, initYaw, 0f);

            float scale = UnityEngine.Random.Range(0.9f, 1.25f);
            logGo.transform.localScale = Vector3.one * scale;

            logGo.GetComponent<MeshFilter>().sharedMesh = DriftwoodTideDeposit.GetOrCreateDriftwoodMesh();
            logGo.GetComponent<MeshRenderer>().sharedMaterial = DriftwoodTideDeposit.GetOrCreateDriftwoodMaterial();

            var col = logGo.GetComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size = new Vector3(0.24f, 0.24f, 0.95f);
            logGo.layer = 9; // Interactive item layer

            var approach = new GameObject(stableId + " approach");
            approach.transform.SetParent(logGo.transform, false);
            approach.transform.localPosition = Vector3.zero;

            var ni = logGo.AddComponent<NpcInteractable>();
            ni.StableId = stableId;
            ni.WorldId = worldId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;

            var phys = logGo.AddComponent<PhysicalItem>();
            phys.itemId = stableId;
            phys.itemTypeId = "wood-driftwood-log";
            phys.massKg = 4.2f * scale;
            phys.dimensions = new PhysicalDimensions(0.95f * scale, 0.22f * scale, 0.22f * scale);
            phys.ConfigureComponents();
            if (phys.Body != null)
            {
                phys.Body.isKinematic = true;
                phys.Body.useGravity = false;
                phys.Body.linearVelocity = Vector3.zero;
                phys.Body.angularVelocity = Vector3.zero;
            }

            var instance = new FloatingLogInstance
            {
                gameObject = logGo,
                physicalItem = phys,
                interactable = ni,
                speed = FlowSpeed * UnityEngine.Random.Range(0.85f, 1.15f),
                bobPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                eddyYawSpeed = UnityEngine.Random.Range(-3f, 3f),
                isBeached = false,
                currentT = progressT
            };

            ActiveLogs.Add(instance);
            return instance;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float waterLevel = CoastalWater.CurrentLevel;
            float time = Time.time;

            for (int i = ActiveLogs.Count - 1; i >= 0; i--)
            {
                var log = ActiveLogs[i];
                if (log == null || log.gameObject == null)
                {
                    ActiveLogs.RemoveAt(i);
                    continue;
                }

                // If carried in hand, stowed, or placed by camp, don't simulate river current
                if (log.physicalItem != null && (log.physicalItem.IsCarried || log.physicalItem.IsStored))
                {
                    log.isBeached = true;
                    continue;
                }

                Vector3 currentPos = log.gameObject.transform.position;
                float groundY = CoastalTerrain.Height(currentPos.x, currentPos.z);
                float depth = waterLevel - groundY;

                // If high and dry on the upper bank (e.g. pushed onto dry shore by high water), it remains beached
                if (depth < -0.06f)
                {
                    if (!log.isBeached && log.physicalItem != null && log.physicalItem.Body != null)
                    {
                        log.physicalItem.Body.isKinematic = false;
                        log.physicalItem.Body.useGravity = true;
                    }
                    log.isBeached = true;
                    continue;
                }

                log.isBeached = false;
                if (log.physicalItem != null && log.physicalItem.Body != null && !log.physicalItem.Body.isKinematic)
                {
                    log.physicalItem.Body.isKinematic = true;
                    log.physicalItem.Body.useGravity = false;
                    log.physicalItem.Body.linearVelocity = Vector3.zero;
                    log.physicalItem.Body.angularVelocity = Vector3.zero;
                }

                // Advance along downstream river spline
                log.currentT += (log.speed / 500f) * dt;

                // If log has reached the northern delta / open sea, recycle it to upstream waterfall run
                if (log.currentT >= 1.0f)
                {
                    log.currentT = UnityEngine.Random.Range(0.01f, 0.05f);
                    log.speed = FlowSpeed * UnityEngine.Random.Range(0.85f, 1.15f);
                }

                Vector3 targetPathPos = SampleRiverPosition(log.currentT);
                float pathGroundY = CoastalTerrain.Height(targetPathPos.x, targetPathPos.z);
                float floatY = waterLevel + 0.04f + Mathf.Sin(time * 2.2f + log.bobPhase) * 0.025f;

                // Gentle pitch and roll bobbing on surface waves
                float pitchBob = Mathf.Sin(time * 1.8f + log.bobPhase) * 3.5f;
                float rollBob = Mathf.Cos(time * 2.5f + log.bobPhase) * 4.0f;

                // River flow alignment: orient log longitudinally along the downstream channel
                Vector3 flowDir = targetPathPos - currentPos;
                flowDir.y = 0;
                float baseYaw = flowDir.sqrMagnitude > 0.001f
                    ? Mathf.Atan2(flowDir.x, flowDir.z) * Mathf.Rad2Deg
                    : log.gameObject.transform.eulerAngles.y;
                float yawWobble = Mathf.Sin(time * 0.9f + log.bobPhase) * 14f;
                float targetYaw = baseYaw + yawWobble;

                Quaternion targetRot = Quaternion.Euler(pitchBob, targetYaw, rollBob);
                log.gameObject.transform.rotation = Quaternion.Slerp(log.gameObject.transform.rotation, targetRot, 3.0f * dt);

                // Smoothly steer toward river channel current
                Vector3 nextPos = Vector3.Lerp(currentPos, new Vector3(targetPathPos.x, floatY, targetPathPos.z), 4f * dt);
                nextPos.y = Mathf.Max(pathGroundY + 0.05f, floatY);
                log.gameObject.transform.position = nextPos;
            }

            // Periodic replenishment check: ensure river constantly has active drifting wood
            if (time >= nextSpawnCheckTime)
            {
                nextSpawnCheckTime = time + 6.0f;
                int floatingCount = 0;
                for (int i = 0; i < ActiveLogs.Count; i++)
                {
                    if (ActiveLogs[i] != null && ActiveLogs[i].gameObject != null && !ActiveLogs[i].isBeached)
                        floatingCount++;
                }

                if (floatingCount < TargetFloatingLogCount)
                {
                    // Spawn fresh wood dislodged from the southern headwall waterfall
                    SpawnLogAtProgress(UnityEngine.Random.Range(0.01f, 0.06f), "world-starfall-0");
                }
            }
        }

        public static Vector3 SampleRiverPosition(float t)
        {
            t = Mathf.Clamp01(t);
            float scaledT = t * (RiverPath.Length - 1);
            int idx = Mathf.FloorToInt(scaledT);
            int nextIdx = Mathf.Min(idx + 1, RiverPath.Length - 1);
            float frac = scaledT - idx;

            Vector2 p1 = RiverPath[idx];
            Vector2 p2 = RiverPath[nextIdx];
            Vector2 interp = Vector2.Lerp(p1, p2, frac);

            float y = CoastalTerrain.Height(interp.x, interp.y);
            return new Vector3(interp.x, y, interp.y);
        }
    }
}
