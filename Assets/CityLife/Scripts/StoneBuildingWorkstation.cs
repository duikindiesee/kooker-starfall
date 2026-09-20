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
        StorageCairn = 2,   // 4-8 flat slabs; elevates food baskets above ground dampness and pests
        PackedWolfShelter = 3, // 8-12 packed basalt river stones; fortified predator redoubt protecting against night wolf attacks
        WolfPeltBivouac = 4    // 4 anchor stones + timber sticks and wolf leather; mobile wilderness camp tent
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
                case StoneStructureKind.PackedWolfShelter:
                    RequiredStones = 8;
                    break;
                case StoneStructureKind.WolfPeltBivouac:
                    RequiredStones = 4;
                    break;
            }
        }

        public bool IsWolfShelterProtecting(Vector3 position, float protectionRadius = 6.0f)
        {
            if (!IsCompleted || CurrentTarget != StoneStructureKind.PackedWolfShelter) return false;
            float d = Vector3.Distance(ConstructionSite, position);
            return d <= protectionRadius;
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
            var stoneObj = new GameObject($"{CurrentTarget}_Stone_{stoneIndex + 1}");
            stoneObj.transform.SetParent(transform, true);
            stoneObj.layer = 8; // Layer 8: Geometry

            // Compute layout position based on structure kind
            Vector3 localOffset = Vector3.zero;
            Vector3 scale = new Vector3(0.55f, 0.28f, 0.45f);

            switch (CurrentTarget)
            {
                case StoneStructureKind.HearthRing:
                    // Circular ring arrangement (radius 0.65m)
                    float angle = (stoneIndex / (float)RequiredStones) * Mathf.PI * 2f;
                    localOffset = new Vector3(Mathf.Cos(angle) * 0.65f, 0.1f, Mathf.Sin(angle) * 0.65f);
                    scale = new Vector3(0.35f, 0.22f, 0.35f);
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

                case StoneStructureKind.PackedWolfShelter:
                    // 2-tier curved perimeter wall arc (8 stones) enclosing shelter entrance with a defensive chokepoint
                    int wTier = stoneIndex / 4; // 2 tiers of 4 stones
                    int wCol = stoneIndex % 4;
                    float wAngle = -1.15f + (wCol / 3.0f) * 2.3f;
                    float wRadius = 2.4f;
                    localOffset = new Vector3(
                        Mathf.Sin(wAngle) * wRadius,
                        0.16f + wTier * 0.32f,
                        Mathf.Cos(wAngle) * wRadius
                    );
                    scale = new Vector3(0.65f, 0.32f, 0.50f);
                    break;

                case StoneStructureKind.WolfPeltBivouac:
                    // 4 corner anchor stones for wilderness camp tent
                    float bx = (stoneIndex % 2 == 0) ? -1.1f : 1.1f;
                    float bz = (stoneIndex < 2) ? -0.9f : 0.9f;
                    localOffset = new Vector3(bx, 0.12f, bz);
                    scale = new Vector3(0.38f, 0.25f, 0.38f);
                    break;
            }

            stoneObj.transform.position = ConstructionSite + localOffset;
            stoneObj.transform.localScale = scale;

            var mesh = CityLife.Stones.StoneMeshGenerator.GenerateMesh(
                CurrentTarget == StoneStructureKind.StorageCairn ? CityLife.Stones.StoneShapeKind.FlatSlab :
                CityLife.Stones.StoneShapeKind.Fieldstone,
                seed: 200 + stoneIndex, variantIndex: stoneIndex % 3, uniformScale: 1.0f, flatShaded: true);

            var mf = stoneObj.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = stoneObj.AddComponent<MeshRenderer>();
            if (Bootstrap != null && Bootstrap.StoneMaterial != null)
                mr.sharedMaterial = Bootstrap.StoneMaterial;

            var colBox = stoneObj.AddComponent<BoxCollider>();
            colBox.size = Vector3.one * 0.85f;

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
                slab.layer = 8;
                slab.transform.SetParent(transform, true);
                slab.transform.position = ConstructionSite + new Vector3(0, 0.35f, 0);
                slab.transform.localScale = new Vector3(0.9f, 0.08f, 0.9f);
                if (Bootstrap != null && Bootstrap.StoneMaterial != null)
                    slab.GetComponent<MeshRenderer>().sharedMaterial = Bootstrap.StoneMaterial;
                _spawnedVisualStones.Add(slab);
            }
            else if (CurrentTarget == StoneStructureKind.PackedWolfShelter)
            {
                // Left and right defensive gateposts
                var leftPost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leftPost.name = "WolfShelter_LeftGatepost";
                leftPost.layer = 8;
                leftPost.transform.SetParent(transform, true);
                leftPost.transform.position = ConstructionSite + new Vector3(-1.35f, 0.55f, 2.3f);
                leftPost.transform.localScale = new Vector3(0.45f, 1.1f, 0.45f);
                if (Bootstrap != null && Bootstrap.StoneMaterial != null)
                    leftPost.GetComponent<MeshRenderer>().sharedMaterial = Bootstrap.StoneMaterial;
                _spawnedVisualStones.Add(leftPost);

                var rightPost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rightPost.name = "WolfShelter_RightGatepost";
                rightPost.layer = 8;
                rightPost.transform.SetParent(transform, true);
                rightPost.transform.position = ConstructionSite + new Vector3(1.35f, 0.55f, 2.3f);
                rightPost.transform.localScale = new Vector3(0.45f, 1.1f, 0.45f);
                if (Bootstrap != null && Bootstrap.StoneMaterial != null)
                    rightPost.GetComponent<MeshRenderer>().sharedMaterial = Bootstrap.StoneMaterial;
                _spawnedVisualStones.Add(rightPost);
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

            // 5. Packed wolf shelter verification
            ws.UpdateRequirementsForTarget(StoneStructureKind.PackedWolfShelter);
            bool shelterReq8 = ws.RequiredStones == 8;
            bool shelterProtectsInside = ws.IsWolfShelterProtecting(ws.transform.position + new Vector3(1f, 0, 1f));
            bool shelterProtectsOutside = !ws.IsWolfShelterProtecting(ws.transform.position + new Vector3(12f, 0, 12f));
            bool shelterPass = shelterReq8 && shelterProtectsInside && shelterProtectsOutside;

            UnityEngine.Object.DestroyImmediate(go);

            bool allPassed = initNotComplete && targetIsHearth && reqIs6 && acceptsCobble && acceptsField &&
                             rejectsWood && dep1 && dep6 && finalComplete && rejectsOverflow && attenuationPass && shelterPass;

            verificationReceipt = $"init={initNotComplete}, targetHearth={targetIsHearth}, req6={reqIs6}, " +
                                  $"acceptsStones={acceptsCobble && acceptsField}, rejectsWood={rejectsWood}({woodReason}), " +
                                  $"comp6={finalComplete}, rejectsOverflow={rejectsOverflow}({overReason}), " +
                                  $"attenuationPass={attenuationPass}(lee={leeAttenuation:F2}, windward={windwardAttenuation:F2}), " +
                                  $"shelterPass={shelterPass}";

            return allPassed;
        }
    }
}
