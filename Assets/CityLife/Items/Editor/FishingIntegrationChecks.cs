using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.Items;
using CityLife.Food;
using CityLife.World;

namespace CityLife.Items.Editor
{
    /// <summary>
    /// Fast in-memory verification suite for handcrafted river fishing rod mechanics,
    /// atomic fish reservation, species preservation, and hand/ground/basket transfer lifecycles.
    /// </summary>
    public static class FishingIntegrationChecks
    {
        public static List<string> Run()
        {
            var passed = new List<string>();

            void Check(bool condition, string checkName)
            {
                if (!condition) throw new InvalidOperationException($"FISHING_CHECK_FAILED: {checkName}");
                passed.Add(checkName);
            }

            // 1. Catalog Registration Checks
            var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
            Check(catalog.Contains("tool-fishing-rod"), "catalog-contains-fishing-rod");
            Check(catalog.TryGet("tool-fishing-rod", out var rodDef), "catalog-get-rod-def");
            Check(Mathf.Approximately(rodDef.massKg, 0.85f), "rod-mass-0.85kg");
            Check(Mathf.Approximately(rodDef.dimensions.depth, 2.10f), "rod-length-2.10m");
            Check(!rodDef.isContainer && !rodDef.isAnchored, "rod-is-unanchored-tool");
            Check(catalog.Contains("food-river-fish"), "catalog-contains-river-fish");
            Check(catalog.Contains("food-river-carp"), "catalog-contains-river-carp");
            Check(catalog.Contains("food-cooked-fish"), "catalog-contains-cooked-fish");

            // 2. Procedural 3D Fishing Rod Visual Generation
            var testRodGo = new GameObject("TestRodGo");
            try
            {
                var rodVisual = FishingRodItem.CreateVisual(testRodGo.transform);
                Check(rodVisual != null, "rod-visual-created");
                var tip = testRodGo.transform.Find("Visual/RodTip");
                Check(tip != null, "rod-tip-transform-exists");
                Check(rodVisual.transform.Find("VisibleCorkGrip") != null, "rod-has-contrasting-cork-grip");
                Check(rodVisual.transform.Find("VisibleReelSpool") != null &&
                      rodVisual.transform.Find("ReelCrank") != null, "rod-has-visible-reel-and-crank");
                Check(rodVisual.transform.Find("LineGuide1") != null &&
                      rodVisual.transform.Find("LineGuide4") != null, "rod-has-visible-line-guides");
                Check(FishingRodItem.GetOrCreateRodMesh().bounds.size.x >= 0.06f,
                    "rod-blank-visible-player-camera-silhouette-width");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testRodGo);
            }

            // 3. Spatial Validation: River Boundaries & Distance
            Vector3 validRiverTarget = new Vector3(-4.0f, CoastalWater.CurrentLevel, -32.0f);
            Vector3 shorePos = new Vector3(2.0f, CoastalTerrain.Height(2.0f, -32.0f), -32.0f);
            Check(RiverFishSchool.CanFishInRiver(shorePos, validRiverTarget, out string acceptReason), "valid-river-cast-accepted: " + acceptReason);

            Vector3 tooCloseTarget = shorePos + Vector3.forward * 0.5f;
            Check(!RiverFishSchool.CanFishInRiver(shorePos, tooCloseTarget, out string closeReason), "too-close-cast-rejected");
            Check(closeReason != null && closeReason.Contains("range"), "too-close-reason-range");

            Vector3 tooFarTarget = shorePos + Vector3.forward * 30.0f;
            Check(!RiverFishSchool.CanFishInRiver(shorePos, tooFarTarget, out string farReason), "too-far-cast-rejected");
            Check(farReason != null && farReason.Contains("range"), "too-far-reason-range");

            Vector3 hillTarget = new Vector3(120f, CoastalTerrain.Height(120f, 80f), 80f);
            Check(!RiverFishSchool.CanFishInRiver(shorePos, hillTarget, out string hillReason), "hill-cast-rejected");

            // 4. Fishing State Machine & StartCast Rejections
            var actorGo = new GameObject("TestActor");
            var rodInHandGo = new GameObject("TestRodInHand");
            var schoolGo = new GameObject("TestSchool");
            try
            {
                var rightHandGo = new GameObject("RightHand");
                rightHandGo.transform.SetParent(actorGo.transform, false);
                var leftHandGo = new GameObject("LeftHand");
                leftHandGo.transform.SetParent(actorGo.transform, false);
                var actions = new NpcActionApi("test-agent", "test-world", actorGo.transform, rightHandGo.transform, leftHandGo.transform, Array.Empty<NpcInteractable>());
                var brain = actorGo.AddComponent<NpcAutonomy>();
                brain.SetActionsForTesting(actions);

                var fishing = actorGo.AddComponent<FishingInteraction>();
                fishing.Brain = brain;

                // 4a. StartCast without rod rejected
                Check(!fishing.StartCast(validRiverTarget), "start-cast-no-rod-rejected");
                Check(fishing.LastReceipt == "cast-failed-no-rod", "start-cast-receipt-no-rod");

                // Equip rod in hand
                var rodPhys = rodInHandGo.AddComponent<PhysicalItem>();
                rodPhys.itemId = "test-rod-01";
                rodPhys.itemTypeId = "tool-fishing-rod";
                var rodNi = rodInHandGo.AddComponent<NpcInteractable>();
                rodNi.StableId = "test-rod-01";
                rodNi.Kind = NpcObjectKind.Item;
                actions.HoldItemDirect(rodNi, false);

                // 4b. StartCast with invalid river target rejected
                Check(!fishing.StartCast(hillTarget), "start-cast-invalid-target-rejected");
                Check(fishing.LastReceipt.StartsWith("cast-rejected-"), "start-cast-receipt-rejected-reason");

                // 4c. StartCast with valid target succeeds
                actorGo.transform.position = shorePos;
                Check(fishing.StartCast(validRiverTarget), "start-cast-valid-succeeded");
                Check(fishing.State == FishingState.Casting, "state-is-casting");
                Check(fishing.LastReceipt == "cast-started", "receipt-cast-started");

                // 4d. Re-calling StartCast while active rejected
                Check(!fishing.StartCast(validRiverTarget), "start-cast-while-active-rejected");

                // Advance cast flight
                fishing.Tick(FishingInteraction.CastDuration + 0.1f);
                Check(fishing.State == FishingState.Floating, "state-transition-to-floating");

                // 4e. Premature strike in Floating state
                Check(!fishing.StrikeAndReel(out _, out _, out string prematureReceipt), "premature-strike-rejected");
                Check(prematureReceipt == "premature-strike-empty", "premature-strike-receipt");
                Check(fishing.State == FishingState.Reeling, "state-is-reeling-empty");

                // Let empty reel finish
                fishing.Tick(FishingInteraction.ReelDuration + 0.1f);
                Check(fishing.State == FishingState.Idle, "state-reset-to-idle-after-empty-reel");

                // 5. Strike Missed: No Fish Near Bobber (Zero Phantom Catch Fallback!)
                fishing.StartCast(validRiverTarget);
                fishing.State = FishingState.Bite; // Forced into bite
                fishing.BobberPosition = new Vector3(999f, CoastalWater.CurrentLevel, 999f); // Way out in nowhere
                Check(!fishing.StrikeAndReel(out string missedSpecies, out _, out string missedReceipt), "strike-missed-no-fish-returns-false");
                Check(missedSpecies == null, "missed-species-is-null");
                Check(missedReceipt == "strike-missed-no-fish", "missed-receipt-strike-missed-no-fish");
                Check(fishing.ReservedFish == null, "reserved-fish-is-null-on-miss");
                fishing.Tick(FishingInteraction.ReelDuration + 0.1f);
                Check(fishing.State == FishingState.Idle, "missed-strike-retrieval-finished");

                // 6. Atomic Swimmer Reservation & Rollback
                var school = schoolGo.AddComponent<RiverFishSchool>();
                var swimmerGo = new GameObject("river-fish-test");
                var swimmerNi = swimmerGo.AddComponent<NpcInteractable>();
                swimmerNi.StableId = "river-fish-test";
                var swimmerPhys = swimmerGo.AddComponent<PhysicalItem>();
                swimmerPhys.itemId = "river-fish-test";
                swimmerPhys.itemTypeId = "food-river-fish";
                swimmerGo.transform.position = validRiverTarget + Vector3.down * 0.38f;

                var swimmerInstance = new RiverFishSchool.RiverFishInstance
                {
                    gameObject = swimmerGo,
                    interactable = swimmerNi,
                    physicalItem = swimmerPhys,
                    swimCenter = validRiverTarget,
                    scale = 0.85f,
                    isReserved = false
                };
                school.ActiveFish.Add(swimmerInstance);

                // Reserve fish
                Check(school.TryReserveFishNear(validRiverTarget, 5.0f, out var reserved), "try-reserve-fish-near-succeeded");
                Check(reserved == swimmerInstance, "reserved-matching-instance");
                Check(swimmerInstance.isReserved, "swimmer-marked-reserved");

                // Rollback reservation
                school.ReleaseReservation(reserved);
                Check(!swimmerInstance.isReserved, "swimmer-unreserved-after-rollback");
                Check(school.ActiveFish.Contains(swimmerInstance), "swimmer-still-in-active-fish-after-rollback");

                // 7. Successful Bite Strike, Reeling Hook Motion, and Authoritative Landing Transfer
                var model = new ItemModel("test-world", "test-gen");
                if (catalog.TryGet("food-river-fish", out var fishDef)) model.RegisterDefinition(fishDef);
                if (catalog.TryGet("tool-fishing-rod", out var rDef)) model.RegisterDefinition(rDef);
                actions.PhysicalModel = model;
                brain.InstanceWorldId = "test-world";

                // Pre-register swimmer in model as Free item
                model.RegisterItem(swimmerNi.StableId, "food-river-fish", ItemLocationKind.Free, validRiverTarget, Quaternion.identity);
                Check(model.TryGetItem(swimmerNi.StableId, out var preCatchState) && preCatchState.location == ItemLocationKind.Free, "swimmer-pre-registered-free-in-model");

                fishing.BobberPosition = validRiverTarget;
                Check(fishing.StartCast(validRiverTarget), "baited-cast-starts-from-idle");
                for (int i = 0; i < 1200 && fishing.State != FishingState.Bite; i++) fishing.Tick(0.05f);
                Check(fishing.State == FishingState.Bite && fishing.BaitEaten, "fish-reached-and-ate-bait:" + fishing.State + ":" + fishing.LastReceipt);

                Check(fishing.StrikeAndReel(out string caughtSpecies, out float scale, out string hookReceipt), "strike-with-fish-succeeded");
                Check(hookReceipt == "fish-hooked-reeling", "hook-receipt-fish-hooked-reeling");
                Check(fishing.ReservedFish == swimmerInstance, "reserved-fish-assigned-to-interaction");
                Check(caughtSpecies == "food-river-fish", "species-preserved-as-food-river-fish");

                // During Reeling: swimmer follows bobber
                fishing.Tick(0.35f);
                Check(swimmerGo.transform.position == fishing.BobberPosition, "hooked-fish-follows-bobber-position");

                // Reeling finishes -> LandCatch executes authoritative model pickup transaction & transfers to left hand!
                fishing.Tick(FishingInteraction.ReelDuration);
                Check(fishing.State == FishingState.Landed, "state-is-landed");
                Check(actions.HeldLeft == swimmerNi, "fish-transferred-to-left-hand");
                Check(!school.ActiveFish.Contains(swimmerInstance), "fish-atomically-removed-from-school");

                // Verify authoritative model state transition from Free -> Carried
                Check(model.TryGetItem(swimmerNi.StableId, out var postCatchState), "catch-found-in-physical-model");
                Check(postCatchState.location == ItemLocationKind.Carried, "catch-model-location-is-carried");
                Check(postCatchState.holderActorId == "test-agent", "catch-model-holder-is-actor");

                // 8. Dual-Hand Inspection Checks (Rod in HeldRight + Fish in HeldLeft)
                Check(actions.HeldRight == rodNi, "rod-still-in-right-hand");
                Check(actions.HeldLeft == swimmerNi, "fish-still-in-left-hand");
                Check(HearthCooking.CanRoastHeldItem(brain), "can-roast-held-item-detects-off-hand-fish");

                // Verify HearthCooking roasted the fish in the off hand (not the rod!)
                var cooker = actorGo.AddComponent<HearthCooking>();
                cooker.AuthoredHearthPosition = actorGo.transform.position;
                Check(HearthCooking.TryRoastHeldItem(brain), "try-roast-held-item-succeeds-from-off-hand");
                Check(swimmerPhys.itemTypeId == "food-cooked-fish", "off-hand-fish-roasted-to-food-cooked-fish");
                Check(rodPhys.itemTypeId == "tool-fishing-rod", "right-hand-rod-unmodified-by-cooking");

                // 9. Autonomy Dual-Hand Food Detection
                var survival = actorGo.AddComponent<StarfallSurvivalAutonomy>();
                survival.Brain = brain;
                brain.Survival = survival;
                Check(survival.TryGetHeldFoodOrCatch(out var foundFoodNi, out var foundFoodPhys, out bool isFoundLeft), "try-get-held-food-finds-off-hand-food");
                Check(foundFoodNi == swimmerNi && isFoundLeft, "off-hand-food-correctly-identified-as-left");
                Check(foundFoodPhys.itemTypeId == "food-cooked-fish", "found-food-phys-type-is-cooked-fish");

                // 10. Diary and Outcome Timing Verification
                int initialOutcomes = survival.FoodOutcomes;
                // StrikeAndReel must NOT increment FoodOutcomes
                fishing.StartCast(validRiverTarget);
                // Add new swimmer for second cast
                var swimmer2Go = new GameObject("river-fish-test-2");
                var swimmer2Ni = swimmer2Go.AddComponent<NpcInteractable>();
                swimmer2Ni.StableId = "river-fish-test-2";
                var swimmer2Phys = swimmer2Go.AddComponent<PhysicalItem>();
                swimmer2Phys.itemId = "river-fish-test-2";
                swimmer2Phys.itemTypeId = "food-river-fish";
                swimmer2Go.transform.position = validRiverTarget + Vector3.down * 0.38f;
                var swimmer2Instance = new RiverFishSchool.RiverFishInstance
                {
                    gameObject = swimmer2Go,
                    interactable = swimmer2Ni,
                    physicalItem = swimmer2Phys,
                    swimCenter = validRiverTarget,
                    scale = 0.85f,
                    isReserved = false
                };
                school.ActiveFish.Add(swimmer2Instance);
                actions.HoldItemDirect(null, true); // Free left hand for second catch

                for (int i = 0; i < 1200 && fishing.State != FishingState.Bite; i++) fishing.Tick(0.05f);
                Check(fishing.StrikeAndReel(out _, out _, out _), "second-fish-ate-bait-and-hooked");
                Check(survival.FoodOutcomes == initialOutcomes, "strike-and-reel-does-not-increment-food-outcomes");

                // LandCatch DOES record outcome and update diary
                fishing.Tick(FishingInteraction.ReelDuration);
                Check(survival.FoodOutcomes == initialOutcomes + 1, "land-catch-increments-food-outcomes");
                Check(survival.recentVerifiedOutcome == "land catch succeeded", "recent-outcome-is-land-catch-succeeded");

                // 11. Critical Hydration Urgency Cancels Active Fishing Safely
                fishing.StartCast(validRiverTarget);
                Check(fishing.IsFishingActive, "fishing-active-before-hydration-interruption");
                fishing.CancelFishing("urgent-hydration-quench-thirst");
                Check(!fishing.IsFishingActive, "fishing-safely-cancelled-by-urgent-hydration");
                Check(fishing.State == FishingState.Idle, "fishing-state-reset-to-idle");
                Check(fishing.LastReceipt == "urgent-hydration-quench-thirst", "cancellation-receipt-recorded");

                // 12. Empty Water Check: Floating State Does Not Transition to Nibble/Bite Without Nearby Fish
                school.ActiveFish.Clear();
                fishing.StartCast(validRiverTarget);
                fishing.Tick(FishingInteraction.CastDuration + 0.1f);
                Check(fishing.State == FishingState.Floating, "empty-water-reaches-floating");
                fishing.Tick(fishing.FloatTargetDuration + 1.0f);
                Check(fishing.State == FishingState.Floating, "empty-water-stays-floating-no-false-bite");
                Check(fishing.LastReceipt == "waiting-for-fish-interest", "empty-water-reports-waiting-for-interest");
                fishing.CancelFishing("empty-water-test-cleanup");

                // 13. Fail Closed on Request ID Allocation Failure (Line 642 Verification)
                var failClosedModel = new ItemModel("test-world", "test-gen");
                failClosedModel.RegisterDefinition(fishDef);
                failClosedModel.RegisterItem("fail-closed-fish", "food-river-fish", ItemLocationKind.Free, validRiverTarget, Quaternion.identity);
                failClosedModel.ResumeRequestSequence(int.MaxValue); // Sature request ID allocation
                actions.PhysicalModel = failClosedModel;

                var failSwimmerGo = new GameObject("fail-closed-fish");
                var failSwimmerNi = failSwimmerGo.AddComponent<NpcInteractable>();
                failSwimmerNi.StableId = "fail-closed-fish";
                var failSwimmerPhys = failSwimmerGo.AddComponent<PhysicalItem>();
                failSwimmerPhys.itemId = "fail-closed-fish";
                failSwimmerPhys.itemTypeId = "food-river-fish";
                failSwimmerGo.transform.position = validRiverTarget;

                var failSwimmerInstance = new RiverFishSchool.RiverFishInstance
                {
                    gameObject = failSwimmerGo,
                    interactable = failSwimmerNi,
                    physicalItem = failSwimmerPhys,
                    swimCenter = validRiverTarget,
                    scale = 0.85f,
                    isReserved = false
                };

                actions.HoldItemDirect(null, true); // Left hand open
                bool transferResult = fishing.TryTransferCatch(failSwimmerInstance, out string allocFailCode);
                Check(!transferResult, "allocation-failure-fails-closed");
                Check(allocFailCode == "model-request-id-allocation-failed", "allocation-failure-reports-proper-code: " + allocFailCode);
                Check(failClosedModel.TryGetItem("fail-closed-fish", out var stateUnchanged) && stateUnchanged.location == ItemLocationKind.Free,
                    "fish-location-remained-free-after-allocation-failure");
                UnityEngine.Object.DestroyImmediate(failSwimmerGo);

                // 14. Atomic Rollback on Postcondition Failure
                var rollbackModel = new ItemModel("test-world", "test-gen");
                rollbackModel.RegisterDefinition(fishDef);
                rollbackModel.RegisterItem("rollback-fish", "food-river-fish", ItemLocationKind.Free, validRiverTarget, Quaternion.identity);
                actions.PhysicalModel = rollbackModel;

                var rollbackSwimmerGo = new GameObject("rollback-fish");
                var rollbackSwimmerNi = rollbackSwimmerGo.AddComponent<NpcInteractable>();
                rollbackSwimmerNi.StableId = ""; // Empty stableId forces HoldItemDirect to return false!
                var rollbackSwimmerPhys = rollbackSwimmerGo.AddComponent<PhysicalItem>();
                rollbackSwimmerPhys.itemId = "rollback-fish";
                rollbackSwimmerPhys.itemTypeId = "food-river-fish";
                rollbackSwimmerGo.transform.position = validRiverTarget;

                var rollbackSwimmerInstance = new RiverFishSchool.RiverFishInstance
                {
                    gameObject = rollbackSwimmerGo,
                    interactable = rollbackSwimmerNi,
                    physicalItem = rollbackSwimmerPhys,
                    swimCenter = validRiverTarget,
                    scale = 0.85f,
                    isReserved = false
                };

                bool rollbackTransferRes = fishing.TryTransferCatch(rollbackSwimmerInstance, out string rollbackFailCode);
                Check(!rollbackTransferRes, "hold-failure-returns-false");
                Check(rollbackFailCode == "hold-item-direct-failed", "rollback-reports-hold-failure: " + rollbackFailCode);
                Check(rollbackModel.TryGetItem("rollback-fish", out var stateRolledBack) && stateRolledBack.location == ItemLocationKind.Free,
                    "atomic-rollback-restores-model-location-to-free");
                UnityEngine.Object.DestroyImmediate(rollbackSwimmerGo);

                // 15. Handedness Detection on Rig Bones and Signed Grip Mirroring
                var rigLeftHandGo = new GameObject("hand_l");
                var rigRightHandGo = new GameObject("hand_r");
                Check(PhysicalItem.DetectLeftHand(rigLeftHandGo.transform), "hand_l-detected-as-left-hand");
                Check(!PhysicalItem.DetectLeftHand(rigRightHandGo.transform), "hand_r-detected-as-right-hand");

                var gripTestGo = new GameObject("grip-test-berry");
                var gripPhys = gripTestGo.AddComponent<PhysicalItem>();
                gripPhys.GripLocalOffset = new Vector3(0.018f, 0.065f, 0.012f);

                gripPhys.AttachToHand(rigLeftHandGo.transform);
                Check(gripPhys.IsLeftHand, "grip-item-recorded-is-left-hand");
                Vector3 expectedLeftPos = rigLeftHandGo.transform.TransformPoint(new Vector3(-0.018f, 0.065f, 0.012f));
                Check(Vector3.Distance(gripTestGo.transform.position, expectedLeftPos) < 0.0001f, "left-hand-offset-mirrored-x-accurately");

                gripPhys.AttachToHand(rigRightHandGo.transform);
                Check(!gripPhys.IsLeftHand, "grip-item-recorded-is-right-hand");
                Vector3 expectedRightPos = rigRightHandGo.transform.TransformPoint(new Vector3(0.018f, 0.065f, 0.012f));
                Check(Vector3.Distance(gripTestGo.transform.position, expectedRightPos) < 0.0001f, "right-hand-offset-preserved-accurately");

                UnityEngine.Object.DestroyImmediate(rigLeftHandGo);
                UnityEngine.Object.DestroyImmediate(rigRightHandGo);
                UnityEngine.Object.DestroyImmediate(gripTestGo);

                // 16. Container Panel Hand Target Cycling
                var panelGo = new GameObject("panel-test");
                var panel = panelGo.AddComponent<PhysicalContainerPanel>();
                panel.EnsureInitialized();
                Check(panel.HandTarget == PhysicalContainerPanel.StoreHandTarget.Auto, "initial-hand-target-auto");
                panel.CycleStoreHand();
                Check(panel.HandTarget == PhysicalContainerPanel.StoreHandTarget.Left, "cycle-hand-target-left");
                panel.CycleStoreHand();
                Check(panel.HandTarget == PhysicalContainerPanel.StoreHandTarget.Right, "cycle-hand-target-right");
                panel.CycleStoreHand();
                Check(panel.HandTarget == PhysicalContainerPanel.StoreHandTarget.Auto, "cycle-hand-target-returns-auto");
                UnityEngine.Object.DestroyImmediate(panelGo);

                // 17. Grounded Natural Language Commands
                survival.CancelActiveCommand("setup-clean-state");

                bool cmdRiver = survival.SubmitNaturalLanguageCommand("go to river");
                Check(cmdRiver && survival.HasActiveCommand, "cmd-river-accepted");
                Check(survival.ActiveCommandTitle == "Go to river" && survival.ActiveCommandTotalSteps == 1, "cmd-river-steps-match");

                bool cmdFish = survival.SubmitNaturalLanguageCommand("catch a fish");
                Check(cmdFish && survival.HasActiveCommand, "cmd-fish-accepted");
                Check(survival.ActiveCommandTitle == "Catch a fish" && survival.ActiveCommandTotalSteps == 6, "cmd-fish-steps-match");

                var titles = survival.ActiveCommandStepTitles;
                bool titlesMatch = titles != null && titles.Count == 6 &&
                    titles[0] == "Equip fishing rod" &&
                    titles[1] == "Select active fish & calculate casting bank" &&
                    titles[2] == "Route to casting bank" &&
                    titles[3] == "Face fish & cast fishing line" &&
                    titles[4] == "Wait for fish bite & strike" &&
                    titles[5] == "Land catch into hand";
                Check(titlesMatch, "cmd-fish-sequence-contract-match");

                bool cmdGoFish = survival.SubmitNaturalLanguageCommand("go fish");
                Check(cmdGoFish && survival.HasActiveCommand, "cmd-gofish-accepted");
                Check(survival.ActiveCommandTitle == "Catch a fish" && survival.ActiveCommandTotalSteps == 6, "cmd-gofish-steps-match");

                bool cmdRoast = survival.SubmitNaturalLanguageCommand("roast catch");
                Check(cmdRoast && survival.HasActiveCommand, "cmd-roast-accepted");
                Check(survival.ActiveCommandTitle == "Roast catch" && survival.ActiveCommandTotalSteps == 3, "cmd-roast-steps-match");

                bool cmdStore = survival.SubmitNaturalLanguageCommand("store fish in basket");
                Check(cmdStore && survival.HasActiveCommand, "cmd-store-accepted");
                Check(survival.ActiveCommandTitle == "Store fish in basket" && survival.ActiveCommandTotalSteps == 3, "cmd-store-steps-match");

                bool cmdEat = survival.SubmitNaturalLanguageCommand("eat catch");
                Check(cmdEat && survival.HasActiveCommand, "cmd-eat-accepted");
                Check(survival.ActiveCommandTitle == "Eat catch" && survival.ActiveCommandTotalSteps == 2, "cmd-eat-steps-match");

                bool cmdCatchEat = survival.SubmitNaturalLanguageCommand("catch then eat");
                Check(cmdCatchEat && survival.HasActiveCommand, "cmd-catch-eat-accepted");
                Check(survival.ActiveCommandTitle == "Catch then eat" && survival.ActiveCommandTotalSteps == 11, "cmd-catch-eat-steps-match");

                bool cmdCatchStore = survival.SubmitNaturalLanguageCommand("catch then store");
                Check(cmdCatchStore && survival.HasActiveCommand, "cmd-catch-store-accepted");
                Check(survival.ActiveCommandTitle == "Catch then store" && survival.ActiveCommandTotalSteps == 9, "cmd-catch-store-steps-match");

                bool cmdCatchRoast = survival.SubmitNaturalLanguageCommand("catch then roast");
                Check(cmdCatchRoast && survival.HasActiveCommand, "cmd-catch-roast-accepted");
                Check(survival.ActiveCommandTitle == "Catch then roast" && survival.ActiveCommandTotalSteps == 9, "cmd-catch-roast-steps-match");

                // 18. Command Cancellation & Rejection Reporting
                survival.CancelActiveCommand("unit-test-cancel");
                Check(!survival.HasActiveCommand, "command-cancelled-successfully");
                Check(survival.LastCommandReceipt == "command-cancelled: unit-test-cancel", "cancellation-receipt-recorded: " + survival.LastCommandReceipt);

                bool unknownCmd = survival.SubmitNaturalLanguageCommand("fly to the moon");
                Check(!unknownCmd, "unrecognized-command-rejected");
                Check(survival.LastCommandReceipt.StartsWith("unrecognized-command"), "rejection-receipt-reported: " + survival.LastCommandReceipt);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actorGo);
                UnityEngine.Object.DestroyImmediate(rodInHandGo);
                UnityEngine.Object.DestroyImmediate(schoolGo);
            }

            return passed;
        }
    }
}
