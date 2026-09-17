using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;
using CityLife.Stones;

namespace CityLife.World
{
    /// <summary>
    /// Archetypes of dry-stone structures buildable during the stone-age survival phase.
    /// </summary>
    public enum StoneStructureKind
    {
        HearthRing = 0,     // 6-12 stones enclosing fire pit; improves burn efficiency and thermal retention
        WindbreakWall = 1,  // 8-16 stacked fieldstones; blocks 60-80% of incoming wind in its lee
        StorageCairn = 2    // 4-8 flat slabs; elevates food baskets above ground dampness and pests
    }

    /// <summary>
    /// Stone Building Workstation & Masonry Runtime:
    /// Coordinates the physical construction of dry-stone survival structures.
    /// Inhabitant carries gathered river cobbles, fieldstones, and flat slabs to the site
    /// and stacks them into permanent architectural features.
    /// </summary>
    public sealed class StoneBuildingWorkstation : MonoBehaviour
    {
        [Header("References")]
        public NpcAutonomy Brain;
        public PhysicalItemBootstrap Bootstrap;
        public NpcInteractable Interactable;

        [Header("Construction Configuration")]
        public StoneStructureKind CurrentTarget = StoneStructureKind.HearthRing;
        public Vector3 ConstructionSite = Vector3.zero;

        [Header("Building Progress")]
        public int RequiredStones = 6;
        public int DepositedStonesCount = 0;
        public bool IsCompleted = false;

        private readonly List<string> _depositedStoneIds = new List<string>();
        private readonly List<GameObject> _spawnedVisualStones = new List<GameObject>();

        public IReadOnlyList<string> DepositedStoneIds => _depositedStoneIds;

        private void Awake()
        {
            if (Interactable == null)
                Interactable = GetComponent<NpcInteractable>();
            if (Interactable == null)
            {
                Interactable = gameObject.AddComponent<NpcInteractable>();
                Interactable.StableId = "stone-building-workstation";
                Interactable.Kind = NpcObjectKind.Place;
                Interactable.Permission = true;
                Interactable.Approach = transform;
            }

            if (ConstructionSite == Vector3.zero)
                ConstructionSite = transform.position;

            UpdateRequirementsForTarget(CurrentTarget);
        }

        /// <summary>
        /// Updates the required stone count based on the selected structure kind.
        /// </summary>
        public void UpdateRequirementsForTarget(StoneStructureKind kind)
        {
            CurrentTarget = kind;
            switch (kind)
            {
                case StoneStructureKind.HearthRing:
                    RequiredStones = 6;
                    break;
                case StoneStructureKind.WindbreakWall:
                    RequiredStones = 8;
                    break;
                case StoneStructureKind.StorageCairn:
                    RequiredStones = 4;
                    break;
            }
        }

        /// <summary>
        /// Validates whether a given item type is an acceptable stone material for construction.
        /// </summary>
        public bool CanDepositStone(string itemTypeId, out string reason)
        {
            if (IsCompleted)
            {
                reason = "structure-already-complete";
                return false;
            }

            if (DepositedStonesCount >= RequiredStones)
            {
                reason = "all-stones-deposited";
                return false;
            }

            // Acceptable stone kinds: river cobble, fieldstone, flat slab, or generic stone
            if (string.IsNullOrEmpty(itemTypeId) || (!itemTypeId.Contains("stone") && !itemTypeId.Contains("cobble") && !itemTypeId.Contains("slab")))
            {
                reason = $"unsupported-material: {itemTypeId}";
                return false;
            }

            reason = "ready";
            return true;
        }

        /// <summary>
        /// Deposits a carried stone into the current structure.
        /// When enough stones are deposited, completes the structure and instantiates physical geometry.
        /// </summary>
        public bool DepositStone(string stoneItemId, string itemTypeId, out bool structureCompleted)
        {
            structureCompleted = false;

            if (!CanDepositStone(itemTypeId, out string reason))
                return false;

            _depositedStoneIds.Add(stoneItemId);
            DepositedStonesCount++;

            // Spawn a placed visual stone piece at the target site
            SpawnPlacedStoneVisual(DepositedStonesCount - 1, itemTypeId);

            if (DepositedStonesCount >= RequiredStones)
            {
                CompleteStructure();
                structureCompleted = true;
            }

            if (Brain != null && Brain.Log != null)
            {
                Brain.Log.Record(
                    Brain.Tick,
                    "masonry",
                    "stone-placement",
                    stoneItemId,
                    "deposit-stone",
                    $"deposited stone {DepositedStonesCount}/{RequiredStones} for {CurrentTarget}",
                    structureCompleted ? "structure complete" : "under construction"
                );
            }

            return true;
        }

        private void SpawnPlacedStoneVisual(int stoneIndex, string itemTypeId)
        {
            var stoneObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            stoneObj.name = $"{CurrentTarget}_Stone_{stoneIndex + 1}";
            stoneObj.transform.SetParent(transform, true);

            // Compute layout position based on structure kind
            Vector3 localOffset = Vector3.zero;
            Vector3 scale = new Vector3(0.35f, 0.22f, 0.35f);

            switch (CurrentTarget)
            {
                case StoneStructureKind.HearthRing:
                    // Circular ring arrangement (radius 0.65m)
                    float angle = (stoneIndex / (float)RequiredStones) * Mathf.PI * 2f;
                    localOffset = new Vector3(Mathf.Cos(angle) * 0.65f, 0.1f, Mathf.Sin(angle) * 0.65f);
                    break;

                case StoneStructureKind.WindbreakWall:
                    // 2-tier curved wall arc (facing northwest adverse winds)
                    int tier = stoneIndex / 4; // 2 tiers of 4
                    int col = stoneIndex % 4;
                    float arcAngle = -Mathf.PI * 0.35f + (col / 3.0f) * Mathf.PI * 0.7f;
                    localOffset = new Vector3(
                        Mathf.Sin(arcAngle) * 1.2f,
                        0.12f + tier * 0.24f,
                        Mathf.Cos(arcAngle) * 1.2f
                    );
                    scale = new Vector3(0.45f, 0.22f, 0.32f);
                    break;

                case StoneStructureKind.StorageCairn:
                    // Stacked 4-pillar/platform formation
                    float dx = (stoneIndex % 2 == 0) ? -0.3f : 0.3f;
                    float dz = (stoneIndex < 2) ? -0.3f : 0.3f;
                    localOffset = new Vector3(dx, 0.15f, dz);
                    scale = new Vector3(0.4f, 0.25f, 0.4f);
                    break;
            }

            stoneObj.transform.position = ConstructionSite + localOffset;
            stoneObj.transform.localScale = scale;

            if (Bootstrap != null && Bootstrap.StoneMaterial != null)
            {
                stoneObj.GetComponent<MeshRenderer>().sharedMaterial = Bootstrap.StoneMaterial;
            }

            _spawnedVisualStones.Add(stoneObj);
        }

        private void CompleteStructure()
        {
            IsCompleted = true;

            // If storage cairn, add a top capping slab
            if (CurrentTarget == StoneStructureKind.StorageCairn)
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "StorageCairn_PlatformSlab";
                slab.transform.SetParent(transform, true);
                slab.transform.position = ConstructionSite + new Vector3(0, 0.35f, 0);
                slab.transform.localScale = new Vector3(0.9f, 0.08f, 0.9f);
                if (Bootstrap != null && Bootstrap.StoneMaterial != null)
                    slab.GetComponent<MeshRenderer>().sharedMaterial = Bootstrap.StoneMaterial;
                _spawnedVisualStones.Add(slab);
            }

            Debug.Log($"STONE_STRUCTURE_COMPLETED: {CurrentTarget} built at {ConstructionSite} using {DepositedStonesCount} stones.");
        }

        /// <summary>
        /// Computes directional wind attenuation factor in the lee of a completed WindbreakWall.
        /// Returns 0.0 (no reduction) to 0.75 (75% wind attenuation).
        /// </summary>
        public float GetWindAttenuation(Vector3 queryPosition, Vector3 windDirection)
        {
            if (!IsCompleted || CurrentTarget != StoneStructureKind.WindbreakWall)
                return 0.0f;

            Vector3 toQuery = queryPosition - ConstructionSite;
            float dist = toQuery.magnitude;
            if (dist > 2.5f) return 0.0f; // Outside effective shelter shadow

            // Lee side check: query position is downstream of wind direction
            float dot = Vector3.Dot(toQuery.normalized, windDirection.normalized);
            if (dot > 0.3f)
            {
                // Protected within the downstream cone
                float distFalloff = Mathf.Clamp01(1.0f - (dist / 2.5f));
                return 0.75f * distFalloff;
            }

            return 0.0f;
        }

        /// <summary>
        /// Standalone unit verification of stone building state and construction logic.
        /// </summary>
        public static bool VerifyBuildingLogic(out string verificationReceipt)
        {
            var go = new GameObject("TestBuildingWorkstation");
            var ws = go.AddComponent<StoneBuildingWorkstation>();

            // 1. Initial State
            bool initNotComplete = !ws.IsCompleted;
            bool targetIsHearth = ws.CurrentTarget == StoneStructureKind.HearthRing;
            bool reqIs6 = ws.RequiredStones == 6;

            // 2. Acceptance of valid stone vs invalid material
            bool acceptsCobble = ws.CanDepositStone("stone-river-cobble", out _);
            bool acceptsField = ws.CanDepositStone("stone-fieldstone", out _);
            bool rejectsWood = !ws.CanDepositStone("wood-fallen-branch", out string woodReason);

            // 3. Sequential deposit simulation
            bool dep1 = ws.DepositStone("cobble-01", "stone-river-cobble", out bool comp1);
            bool dep2 = ws.DepositStone("cobble-02", "stone-river-cobble", out _);
            bool dep3 = ws.DepositStone("cobble-03", "stone-river-cobble", out _);
            bool dep4 = ws.DepositStone("cobble-04", "stone-river-cobble", out _);
            bool dep5 = ws.DepositStone("cobble-05", "stone-river-cobble", out _);
            bool dep6 = ws.DepositStone("cobble-06", "stone-river-cobble", out bool comp6);

            bool finalComplete = ws.IsCompleted && comp6 && !comp1;
            bool rejectsOverflow = !ws.CanDepositStone("stone-river-cobble", out string overReason);

            // 4. Windbreak wall attenuation verification
            ws.UpdateRequirementsForTarget(StoneStructureKind.WindbreakWall);
            ws.IsCompleted = true; // Simulate completion
            float leeAttenuation = ws.GetWindAttenuation(ws.transform.position + new Vector3(0, 0, 1.0f), new Vector3(0, 0, 1.0f));
            float windwardAttenuation = ws.GetWindAttenuation(ws.transform.position + new Vector3(0, 0, -1.0f), new Vector3(0, 0, 1.0f));
            bool attenuationPass = leeAttenuation > 0.4f && windwardAttenuation == 0.0f;

            UnityEngine.Object.DestroyImmediate(go);

            bool allPassed = initNotComplete && targetIsHearth && reqIs6 && acceptsCobble && acceptsField &&
                             rejectsWood && dep1 && dep6 && finalComplete && rejectsOverflow && attenuationPass;

            verificationReceipt = $"init={initNotComplete}, targetHearth={targetIsHearth}, req6={reqIs6}, " +
                                  $"acceptsStones={acceptsCobble && acceptsField}, rejectsWood={rejectsWood}({woodReason}), " +
                                  $"comp6={finalComplete}, rejectsOverflow={rejectsOverflow}({overReason}), " +
                                  $"attenuationPass={attenuationPass}(lee={leeAttenuation:F2}, windward={windwardAttenuation:F2})";

            return allPassed;
        }
    }
}
