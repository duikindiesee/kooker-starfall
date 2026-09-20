using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;
using CityLife.World;

namespace CityLife.World
{
    public enum FishingState
    {
        Idle,
        Casting,
        Floating,
        Nibble,
        Bite,
        Reeling,
        Landed,
        Cancelled
    }

    /// <summary>
    /// Interactive river fishing state machine.
    /// Drives realistic casting, buoyant float physics, nibble/bite progressions,
    /// strike timing windows, atomic fish extraction from RiverFishSchool, and catch landing.
    /// Integrates seamlessly with NpcPlayerControls and StarfallSurvivalAutonomy.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FishingInteraction : MonoBehaviour
    {
        public const float DefaultCastDistance = 8.5f;
        public const float CastDuration = 0.65f;
        public const float FloatMinDuration = 2.0f;
        public const float FloatMaxDuration = 4.2f;
        public const float NibbleDuration = 1.6f;
        public const float BiteWindowDuration = 2.5f;
        public const float ReelDuration = 0.75f;

        [Header("State")]
        public FishingState State = FishingState.Idle;
        public float StateTimer;
        public float FloatTargetDuration = 3.0f;

        [Header("Cast & Catch Data")]
        public Vector3 CastOrigin;
        public Vector3 CastTarget;
        public Vector3 BobberPosition;
        public string TargetSpeciesTypeId;
        public float TargetFishScale = 0.85f;
        public string LastReceipt = "idle";

        [Header("References")]
        public NpcAutonomy Brain;
        public FishingRodItem ActiveRod;
        public GameObject BobberInstance;
        public LineRenderer DynamicLine;
        public RiverFishSchool.RiverFishInstance ReservedFish;

        private AudioSource audioSource;
        private static AudioClip splashClip;
        private static AudioClip reelClip;

        private void Awake()
        {
            if (Brain == null) Brain = GetComponent<NpcAutonomy>() ?? GetComponentInParent<NpcAutonomy>();
            EnsureAudio();
        }

        private void EnsureAudio()
        {
            if (audioSource == null)
            {
                audioSource = gameObject.GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                    audioSource.playOnAwake = false;
                    audioSource.spatialBlend = 0.6f;
                    audioSource.volume = 0.75f;
                }
            }
        }

        private void Start()
        {
            EnsureBobberAndLine();
        }

        public void EnsureBobberAndLine()
        {
            if (BobberInstance == null)
            {
                BobberInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                BobberInstance.name = "FishingBobber_Float";
                var col = BobberInstance.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }

                BobberInstance.transform.localScale = new Vector3(0.09f, 0.12f, 0.09f);
                var rend = BobberInstance.GetComponent<MeshRenderer>();
                if (rend != null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    var mat = new Material(shader) { name = "BobberMaterial" };
                    mat.SetColor("_BaseColor", new Color(0.95f, 0.22f, 0.12f, 1f)); // High-visibility red/orange float
                    mat.SetFloat("_Smoothness", 0.7f);
                    rend.sharedMaterial = mat;
                }
                BobberInstance.SetActive(false);
            }

            if (DynamicLine == null)
            {
                var lineGo = new GameObject("FishingLine_Renderer");
                lineGo.transform.SetParent(transform, false);
                DynamicLine = lineGo.AddComponent<LineRenderer>();
                DynamicLine.positionCount = 3;
                DynamicLine.startWidth = 0.005f;
                DynamicLine.endWidth = 0.004f;
                DynamicLine.useWorldSpace = true;

                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader) { name = "FishingLine_Material" };
                mat.SetColor("_BaseColor", new Color(0.90f, 0.88f, 0.82f, 0.85f)); // Translucent monofilament / silk twine
                DynamicLine.material = mat;
                DynamicLine.enabled = false;
            }
        }

        public bool IsFishingActive => State != FishingState.Idle && State != FishingState.Landed && State != FishingState.Cancelled;

        public bool IsHoldingFishingRod(out FishingRodItem rod)
        {
            rod = null;
            if (Brain == null || Brain.Actions == null) return false;

            var r = Brain.Actions.HeldRight;
            if (r != null)
            {
                var rodComp = r.GetComponent<FishingRodItem>() ?? r.GetComponentInChildren<FishingRodItem>();
                var phys = r.GetComponent<PhysicalItem>();
                if (rodComp != null || (phys != null && phys.itemTypeId == FishingRodItem.ItemTypeId))
                {
                    rod = rodComp ?? r.gameObject.AddComponent<FishingRodItem>();
                    ActiveRod = rod;
                    return true;
                }
            }

            var l = Brain.Actions.HeldLeft;
            if (l != null)
            {
                var rodComp = l.GetComponent<FishingRodItem>() ?? l.GetComponentInChildren<FishingRodItem>();
                var phys = l.GetComponent<PhysicalItem>();
                if (rodComp != null || (phys != null && phys.itemTypeId == FishingRodItem.ItemTypeId))
                {
                    rod = rodComp ?? l.gameObject.AddComponent<FishingRodItem>();
                    ActiveRod = rod;
                    return true;
                }
            }

            return false;
        }

        public bool CanStartCast(Vector3 casterPos, out Vector3 targetWaterPos, out string reason)
        {
            targetWaterPos = Vector3.zero;
            reason = null;

            if (!IsHoldingFishingRod(out _))
            {
                reason = "Not holding fishing rod in hand";
                return false;
            }

            if (IsFishingActive)
            {
                reason = $"Already fishing in state {State}";
                return false;
            }

            // Project forward cast in actor's forward direction
            Vector3 forward = transform.forward;
            forward.y = 0;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();

            // Cast 6m - 10m forward
            Vector3 prospectiveTarget = casterPos + forward * DefaultCastDistance;
            float channelX = CoastalTerrain.RiverCenterlineX(prospectiveTarget.z);
            // Bias target toward deep river centerline
            prospectiveTarget.x = Mathf.Lerp(prospectiveTarget.x, channelX, 0.65f);
            prospectiveTarget.y = CoastalWater.CurrentLevel;

            if (!RiverFishSchool.CanFishInRiver(casterPos, prospectiveTarget, out reason))
            {
                // Try slightly nearer or further cast if initial point missed river
                for (float testDist = 5.0f; testDist <= 12.0f; testDist += 2.0f)
                {
                    Vector3 altTarget = casterPos + forward * testDist;
                    altTarget.x = CoastalTerrain.RiverCenterlineX(altTarget.z);
                    altTarget.y = CoastalWater.CurrentLevel;
                    if (RiverFishSchool.CanFishInRiver(casterPos, altTarget, out _))
                    {
                        targetWaterPos = altTarget;
                        return true;
                    }
                }
                return false;
            }

            targetWaterPos = prospectiveTarget;
            return true;
        }

        public bool StartCast(Vector3 targetWaterPos)
        {
            EnsureBobberAndLine();
            if (!IsHoldingFishingRod(out var rod))
            {
                LastReceipt = "cast-failed-no-rod";
                return false;
            }

            if (IsFishingActive)
            {
                LastReceipt = $"cast-rejected-already-fishing-state-{State}";
                return false;
            }

            Vector3 casterPos = transform.position;
            if (!RiverFishSchool.CanFishInRiver(casterPos, targetWaterPos, out string reason))
            {
                LastReceipt = $"cast-rejected-{reason}";
                return false;
            }

            ActiveRod = rod;
            Vector3 tipPos = rod.TipTransform != null ? rod.TipTransform.position : transform.position + transform.forward * 1.5f + Vector3.up * 1.2f;

            CastOrigin = tipPos;
            CastTarget = targetWaterPos;
            BobberPosition = tipPos;

            State = FishingState.Casting;
            StateTimer = 0f;
            FloatTargetDuration = UnityEngine.Random.Range(FloatMinDuration, FloatMaxDuration);
            TargetSpeciesTypeId = null;

            if (BobberInstance != null)
            {
                BobberInstance.transform.position = tipPos;
                BobberInstance.SetActive(true);
            }
            if (DynamicLine != null)
            {
                DynamicLine.enabled = true;
                UpdateLinePositions(tipPos, tipPos);
            }

            PlayWhooshCue();
            LastReceipt = "cast-started";
            return true;
        }

        public bool StrikeAndReel(out string caughtSpecies, out float fishScale, out string receipt)
        {
            caughtSpecies = null;
            fishScale = 0.85f;
            receipt = null;

            if (State == FishingState.Idle)
            {
                receipt = "strike-rejected-not-fishing";
                LastReceipt = receipt;
                return false;
            }

            if (State == FishingState.Bite)
            {
                // Successful strike during active bite window!
                State = FishingState.Reeling;
                StateTimer = 0f;

                // Atomic fish reservation from RiverFishSchool
                var school = RiverFishSchool.Instance ?? FindFirstObjectByType<RiverFishSchool>();
                bool reserved = false;
                if (school != null)
                {
                    reserved = school.TryReserveFishNear(BobberPosition, 7.5f, out var fish);
                    if (reserved)
                    {
                        ReservedFish = fish;
                        bool isCarp = (fish.interactable != null && fish.interactable.StableId != null && fish.interactable.StableId.Contains("carp")) ||
                                      (fish.gameObject != null && fish.gameObject.name.Contains("carp"));
                        TargetSpeciesTypeId = isCarp ? "food-river-carp" : "food-river-fish";
                        TargetFishScale = fish.scale > 0.05f ? fish.scale : (isCarp ? 0.85f : 0.75f);
                    }
                }

                if (!reserved)
                {
                    // STRICT: No fish near bobber => no catch! No phantom fallback!
                    ReservedFish = null;
                    TargetSpeciesTypeId = null;
                    receipt = "strike-missed-no-fish";
                    LastReceipt = receipt;
                    PlaySplashCue();
                    PlayReelCue();
                    return false;
                }

                caughtSpecies = TargetSpeciesTypeId;
                fishScale = TargetFishScale;
                receipt = "fish-hooked-reeling";
                LastReceipt = receipt;

                PlaySplashCue();
                PlayReelCue();
                return true;
            }
            else if (State == FishingState.Floating || State == FishingState.Nibble)
            {
                // Premature strike before bite: line pulled early, fish spooked away
                State = FishingState.Reeling;
                StateTimer = 0f;
                if (ReservedFish != null)
                {
                    RiverFishSchool.Instance?.ReleaseReservation(ReservedFish);
                    ReservedFish = null;
                }
                TargetSpeciesTypeId = null;
                receipt = "premature-strike-empty";
                LastReceipt = receipt;
                PlaySplashCue();
                return false;
            }
            else
            {
                CancelFishing("strike-in-invalid-state");
                receipt = "strike-cancelled";
                return false;
            }
        }

        public void CancelFishing(string reason = "cancelled")
        {
            if (ReservedFish != null)
            {
                if (RiverFishSchool.Instance != null)
                {
                    RiverFishSchool.Instance.ReleaseReservation(ReservedFish);
                }
                else
                {
                    ReservedFish.isReserved = false;
                }
                ReservedFish = null;
            }

            State = FishingState.Cancelled;
            StateTimer = 0f;
            LastReceipt = reason;

            if (BobberInstance != null) BobberInstance.SetActive(false);
            if (DynamicLine != null) DynamicLine.enabled = false;

            State = FishingState.Idle;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (State == FishingState.Idle) return;

            // Strict safety check: if rod is dropped or lost while fishing, cancel immediately
            if (!IsHoldingFishingRod(out _))
            {
                CancelFishing("rod-unheld");
                return;
            }

            StateTimer += dt;
            Vector3 tipPos = (ActiveRod != null && ActiveRod.TipTransform != null)
                ? ActiveRod.TipTransform.position
                : transform.position + transform.forward * 1.5f + Vector3.up * 1.2f;

            switch (State)
            {
                case FishingState.Casting:
                {
                    float t = Mathf.Clamp01(StateTimer / CastDuration);
                    // Parabolic arc flight
                    float arcY = Mathf.Sin(t * Mathf.PI) * 2.2f;
                    BobberPosition = Vector3.Lerp(CastOrigin, CastTarget, t) + Vector3.up * arcY;

                    if (BobberInstance != null) BobberInstance.transform.position = BobberPosition;
                    UpdateLinePositions(tipPos, BobberPosition);

                    if (t >= 1.0f)
                    {
                        State = FishingState.Floating;
                        StateTimer = 0f;
                        BobberPosition = CastTarget;
                        BobberPosition.y = CoastalWater.CurrentLevel;
                        PlaySplashCue();
                    }
                    break;
                }

                case FishingState.Floating:
                {
                    // Gentle wave undulation
                    float waterY = CoastalWater.CurrentLevel;
                    float wave = Mathf.Sin(Time.time * 2.5f) * 0.025f;
                    BobberPosition = new Vector3(CastTarget.x, waterY + wave, CastTarget.z);

                    if (BobberInstance != null) BobberInstance.transform.position = BobberPosition;
                    UpdateLinePositions(tipPos, BobberPosition);

                    if (StateTimer >= FloatTargetDuration)
                    {
                        var school = RiverFishSchool.Instance ?? FindFirstObjectByType<RiverFishSchool>();
                        bool hasFish = school != null && school.HasFishNear(BobberPosition, 7.5f);
                        if (hasFish)
                        {
                            State = FishingState.Nibble;
                            StateTimer = 0f;
                        }
                        else
                        {
                            // Empty water: no false bite! Bobber stays floating calmly
                            StateTimer = FloatTargetDuration * 0.75f;
                            LastReceipt = "calm-waters-no-fish-nearby";
                        }
                    }
                    break;
                }

                case FishingState.Nibble:
                {
                    // Twitching bobber dips
                    float waterY = CoastalWater.CurrentLevel;
                    float twitch = Mathf.Abs(Mathf.Sin(Time.time * 8f)) * -0.045f;
                    BobberPosition = new Vector3(CastTarget.x, waterY + twitch, CastTarget.z);

                    if (BobberInstance != null) BobberInstance.transform.position = BobberPosition;
                    UpdateLinePositions(tipPos, BobberPosition);

                    if (StateTimer >= NibbleDuration)
                    {
                        State = FishingState.Bite;
                        StateTimer = 0f;
                        PlaySplashCue();
                    }
                    break;
                }

                case FishingState.Bite:
                {
                    // Plunged deep underwater with vigorous tugs
                    float waterY = CoastalWater.CurrentLevel;
                    float tug = -0.16f + Mathf.Sin(Time.time * 12f) * 0.04f;
                    BobberPosition = new Vector3(CastTarget.x, waterY + tug, CastTarget.z);

                    if (BobberInstance != null) BobberInstance.transform.position = BobberPosition;
                    UpdateLinePositions(tipPos, BobberPosition);

                    if (StateTimer >= BiteWindowDuration)
                    {
                        // Fish got away!
                        CancelFishing("fish-escaped-bite-window-expired");
                    }
                    break;
                }

                case FishingState.Reeling:
                {
                    float t = Mathf.Clamp01(StateTimer / ReelDuration);
                    BobberPosition = Vector3.Lerp(CastTarget, tipPos, t);

                    if (BobberInstance != null) BobberInstance.transform.position = BobberPosition;
                    UpdateLinePositions(tipPos, BobberPosition);

                    if (ReservedFish != null && ReservedFish.gameObject != null)
                    {
                        ReservedFish.gameObject.transform.position = BobberPosition;
                    }

                    if (t >= 1.0f)
                    {
                        if (ReservedFish != null && ReservedFish.gameObject != null)
                        {
                            LandCatch();
                        }
                        else
                        {
                            CancelFishing("empty-line-retrieved");
                        }
                    }
                    break;
                }

                case FishingState.Landed:
                {
                    if (StateTimer >= 0.5f)
                    {
                        State = FishingState.Idle;
                    }
                    break;
                }
            }
        }

        private void LandCatch()
        {
            if (ReservedFish == null || ReservedFish.gameObject == null)
            {
                CancelFishing("land-catch-failed-no-reserved-fish");
                return;
            }

            // Validate hand/capacity/model before ownership transfer
            bool transferSuccess = TryTransferCatch(ReservedFish, out string transferCode);
            if (!transferSuccess)
            {
                // Rollback reservation on failure: fish returns safely to river
                if (RiverFishSchool.Instance != null)
                {
                    RiverFishSchool.Instance.ReleaseReservation(ReservedFish);
                }
                else
                {
                    ReservedFish.isReserved = false;
                }
                ReservedFish = null;

                CancelFishing($"land-transfer-failed-{transferCode}");
                return;
            }

            string speciesLanded = TargetSpeciesTypeId;
            float scaleLanded = TargetFishScale;

            // Transfer succeeded! Atomically complete catch in school
            if (RiverFishSchool.Instance != null)
            {
                RiverFishSchool.Instance.CompleteCatch(ReservedFish);
            }
            ReservedFish = null;

            State = FishingState.Landed;
            StateTimer = 0f;

            if (BobberInstance != null) BobberInstance.SetActive(false);
            if (DynamicLine != null) DynamicLine.enabled = false;

            PlaySplashCue();
            LastReceipt = $"landed-{speciesLanded}-{transferCode}";

            // Review Item 4: Record diary and outcome success AFTER LandCatch transfer
            if (Brain != null && Brain.Survival != null)
            {
                Brain.Survival.RecordCatchLanded(speciesLanded, scaleLanded, transferCode);
            }
        }

        public bool TryTransferCatch(RiverFishSchool.RiverFishInstance fish, out string transferCode)
        {
            transferCode = null;
            if (fish == null || fish.gameObject == null || fish.interactable == null)
            {
                transferCode = "fish-null";
                return false;
            }

            if (Brain == null || Brain.Actions == null)
            {
                transferCode = "actions-unavailable";
                return false;
            }

            var actions = Brain.Actions;
            var model = actions.PhysicalModel ?? (Brain.PhysicalItems != null ? Brain.PhysicalItems.Model : null);
            string agentId = actions.AgentId;
            string worldId = Brain.InstanceWorldId ?? actions.WorldId ?? "starfall.coastal-canyon.v1";
            string fishItemId = !string.IsNullOrEmpty(fish.interactable.StableId)
                ? fish.interactable.StableId
                : (fish.physicalItem != null && !string.IsNullOrEmpty(fish.physicalItem.itemId) ? fish.physicalItem.itemId : fish.interactable.StableId);
            string itemTypeId = (fish.physicalItem != null && !string.IsNullOrEmpty(fish.physicalItem.itemTypeId))
                ? fish.physicalItem.itemTypeId
                : (TargetSpeciesTypeId ?? "food-river-fish");

            // Ensure definition exists in model if model is present
            if (model != null && !model.TryGetDefinition(itemTypeId, out _))
            {
                var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
                if (catalog.TryGet(itemTypeId, out var def))
                {
                    model.RegisterDefinition(def);
                }
            }

            // 1. Hand capacity evaluation:
            bool rightFree = actions.HeldRight == null;
            bool leftFree = actions.HeldLeft == null;

            // 2. PhysicalModel carry capacity validation if model is active
            if (model != null)
            {
                var limits = model.GetActorCarryLimits(agentId);
                var carried = model.GetActorCarriedItemIds(agentId);

                if (carried.Count >= limits.maxCarriedItems)
                {
                    rightFree = false;
                    leftFree = false;
                }

                float curMass = model.GetActorCarriedMassKg(agentId);
                float itemMass = fish.physicalItem != null ? fish.physicalItem.massKg : 0.75f;
                if (curMass + itemMass > limits.maxCarryMassKg)
                {
                    rightFree = false;
                    leftFree = false;
                }
            }

            if (rightFree || leftFree)
            {
                // Transfer to open hand!
                bool isLeft = !rightFree; // If right occupied by rod, use left hand
                Transform targetHand = isLeft ? actions.LeftHandTransform : actions.RightHandTransform;
                if (targetHand == null)
                {
                    transferCode = "target-hand-transform-null";
                    return false;
                }

                Vector3 fallbackPos = fish.swimCenter != Vector3.zero ? fish.swimCenter : (targetHand != null ? targetHand.position : transform.position);
                PreparedItemTransitionToken preparedPickupToken = null;

                // If model is present, prepare authoritative transition
                if (model != null)
                {
                    if (model.TryGetItem(fishItemId, out var existingState))
                    {
                        if (existingState.location == ItemLocationKind.Carried && !string.Equals(existingState.holderActorId, agentId, StringComparison.Ordinal))
                        {
                            transferCode = $"invalid-item-location-{existingState.location}";
                            return false;
                        }
                    }
                    else
                    {
                        // Register item as Free at fallback position
                        if (!model.RegisterItem(fishItemId, itemTypeId, ItemLocationKind.Free, fallbackPos, Quaternion.identity))
                        {
                            transferCode = "model-register-free-failed";
                            return false;
                        }
                    }

                    // Authoritative pickup transaction: Fail closed if request id cannot be allocated
                    if (!model.TryAllocateNextRequestId(out int pickupReqId))
                    {
                        transferCode = "model-request-id-allocation-failed";
                        return false;
                    }

                    var pickupReq = new ItemActionRequest
                    {
                        requestId = pickupReqId,
                        action = ItemActionKind.Pickup,
                        actorId = agentId,
                        itemId = fishItemId
                    };
                    var auth = actions.PhysicalAuthority ?? new BasicItemActionAuthority();
                    if (!model.TryPrepareTransition(worldId, model.GenerationId, pickupReq, auth, out preparedPickupToken, out string rejectionCode))
                    {
                        transferCode = $"model-pickup-failed-{rejectionCode}";
                        return false;
                    }
                }

                void Rollback()
                {
                    if (model != null && preparedPickupToken != null)
                    {
                        model.CancelPreparedTransition(preparedPickupToken);
                        preparedPickupToken = null;
                    }

                    if (actions != null)
                    {
                        if (isLeft && actions.HeldLeft == fish.interactable) actions.HoldItemDirect(null, true);
                        else if (!isLeft && actions.HeldRight == fish.interactable) actions.HoldItemDirect(null, false);
                    }

                    if (fish.gameObject != null)
                    {
                        fish.gameObject.transform.SetParent(null, true);
                        if (fish.physicalItem != null)
                        {
                            fish.physicalItem.ReleaseToPhysics(fallbackPos, Quaternion.identity);
                        }
                    }
                }

                // Parent to hand and ensure visual alignment
                fish.gameObject.transform.SetParent(targetHand, false);
                fish.gameObject.transform.localPosition = new Vector3(0, 0, 0.04f);
                fish.gameObject.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                fish.gameObject.transform.localScale = Vector3.one * (fish.scale > 0.05f ? fish.scale : 0.85f);
                fish.gameObject.SetActive(true);

                if (fish.physicalItem != null)
                {
                    fish.physicalItem.itemTypeId = itemTypeId;
                    fish.physicalItem.ConfigureComponents();
                    if (model != null)
                    {
                        fish.physicalItem.Bind(model, worldId, model.GenerationId);
                    }
                }

                bool holdOk = actions.HoldItemDirect(fish.interactable, isLeft);
                if (!holdOk)
                {
                    Rollback();
                    transferCode = "hold-item-direct-failed";
                    return false;
                }

                var assignedHeld = isLeft ? actions.HeldLeft : actions.HeldRight;
                if (assignedHeld != fish.interactable)
                {
                    Rollback();
                    transferCode = "actions-held-hand-mismatch";
                    return false;
                }

                // Commit the model transaction authoritatively now that all physical / scene hold operations succeeded
                if (model != null && preparedPickupToken != null)
                {
                    if (!model.TryCommitTransition(preparedPickupToken, out var commitReceipt) || !commitReceipt.success)
                    {
                        Rollback();
                        transferCode = "model-commit-pickup-failed";
                        return false;
                    }

                    if (!model.TryGetItem(fishItemId, out var verifiedSnap) ||
                        verifiedSnap.location != ItemLocationKind.Carried ||
                        !string.Equals(verifiedSnap.holderActorId, agentId, StringComparison.Ordinal))
                    {
                        Rollback();
                        transferCode = "model-holder-location-verification-failed";
                        return false;
                    }
                }

                if (Brain.Registry != null && !System.Array.Exists(Brain.Registry, x => x == fish.interactable))
                {
                    var list = new List<NpcInteractable>(Brain.Registry) { fish.interactable };
                    Brain.Registry = list.ToArray();
                }

                transferCode = isLeft ? "hand-left" : "hand-right";
                return true;
            }

            // 3. Hands full: evaluate safe dry shore ground drop
            Vector3 actorPos = transform.position;
            Vector3 shorePos = actorPos + transform.forward * 0.75f;
            float groundY = CoastalTerrain.Height(shorePos.x, shorePos.z);
            float waterLevel = CoastalWater.CurrentLevel;

            if (groundY < waterLevel - 0.20f)
            {
                transferCode = "hands-full-and-shore-underwater";
                return false;
            }

            shorePos.y = groundY + 0.08f;
            fish.gameObject.transform.SetParent(null, true);
            fish.gameObject.transform.position = shorePos;
            fish.gameObject.transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 90f);
            fish.gameObject.transform.localScale = Vector3.one * (fish.scale > 0.05f ? fish.scale : 0.85f);
            fish.gameObject.SetActive(true);

            if (fish.physicalItem != null)
            {
                fish.physicalItem.itemTypeId = itemTypeId;
                fish.physicalItem.ConfigureComponents();
                if (fish.physicalItem.Body != null)
                {
                    fish.physicalItem.Body.linearVelocity = Vector3.zero;
                    fish.physicalItem.Body.angularVelocity = Vector3.zero;
                    fish.physicalItem.Body.isKinematic = false;
                    fish.physicalItem.Body.useGravity = true;
                }
                if (model != null)
                {
                    if (!model.TryGetItem(fishItemId, out _))
                    {
                        model.RegisterItem(fishItemId, itemTypeId, ItemLocationKind.Free, shorePos, Quaternion.identity);
                    }
                    else
                    {
                        model.SyncFreeTransform(worldId, model.GenerationId, fishItemId, shorePos, Quaternion.identity);
                    }
                    fish.physicalItem.Bind(model, worldId, model.GenerationId);

                    if (!model.TryGetItem(fishItemId, out var shoreSnap) || shoreSnap.location != ItemLocationKind.Free)
                    {
                        transferCode = "model-shore-verification-failed";
                        return false;
                    }
                }
            }

            fish.interactable.HeldBy = "";
            if (Brain.Registry != null && !System.Array.Exists(Brain.Registry, x => x == fish.interactable))
            {
                var list = new List<NpcInteractable>(Brain.Registry) { fish.interactable };
                Brain.Registry = list.ToArray();
            }

            transferCode = "ground-safe-shore";
            return true;
        }

        private void UpdateLinePositions(Vector3 tip, Vector3 bobber)
        {
            if (DynamicLine == null || !DynamicLine.enabled) return;

            // Slight line sag
            Vector3 mid = (tip + bobber) * 0.5f;
            float sag = Mathf.Clamp(Vector3.Distance(tip, bobber) * 0.06f, 0.05f, 0.40f);
            mid.y -= sag;

            DynamicLine.SetPosition(0, tip);
            DynamicLine.SetPosition(1, mid);
            DynamicLine.SetPosition(2, bobber);
        }

        private void PlaySplashCue()
        {
            EnsureAudio();
            if (audioSource != null)
            {
                if (splashClip == null) splashClip = CreateSyntheticSplashClip();
                audioSource.PlayOneShot(splashClip, 0.6f);
            }
        }

        private void PlayReelCue()
        {
            EnsureAudio();
            if (audioSource != null)
            {
                if (reelClip == null) reelClip = CreateSyntheticReelClip();
                audioSource.PlayOneShot(reelClip, 0.5f);
            }
        }

        private void PlayWhooshCue()
        {
            EnsureAudio();
            if (audioSource != null)
            {
                if (reelClip == null) reelClip = CreateSyntheticReelClip();
                audioSource.PlayOneShot(reelClip, 0.35f);
            }
        }

        private static AudioClip CreateSyntheticSplashClip()
        {
            int sampleRate = 22050;
            int length = sampleRate / 3; // 0.33s
            float[] samples = new float[length];
            var rng = new System.Random(1337);
            for (int i = 0; i < length; i++)
            {
                float env = 1f - (i / (float)length);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float tone = Mathf.Sin(i * 0.08f);
                samples[i] = (noise * 0.7f + tone * 0.3f) * env * 0.4f;
            }
            var clip = AudioClip.Create("Fishing_Splash", length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateSyntheticReelClip()
        {
            int sampleRate = 22050;
            int length = sampleRate / 4; // 0.25s
            float[] samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float click = ((i % 200) < 30) ? 0.35f : 0.0f;
                float env = 1f - (i / (float)length);
                samples[i] = click * env;
            }
            var clip = AudioClip.Create("Fishing_Reel", length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
