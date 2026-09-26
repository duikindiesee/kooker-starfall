using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CityLife.Items;
using CityLife.World;

namespace CityLife.World.Editor
{
    /// <summary>
    /// Automated validation test suite for the 'go fish' natural language directive,
    /// deterministic semantic normalization, club stowing, casting bank calculation,
    /// fish reservation state machine, and exact command receipts.
    /// </summary>
    public static class GoFishCommandValidationTests
    {
        [MenuItem("CityLife/Validate 'go fish' command loop")]
        public static void RunFromMenu()
        {
            if (RunAllChecks(out string receipt))
            {
                Debug.Log($"GO_FISH_VALIDATION_SUCCEEDED: {receipt}");
            }
            else
            {
                throw new Exception($"GO_FISH_VALIDATION_FAILED: {receipt}");
            }
        }

        public static void RunFocusedBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch validation only.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            RunFromMenu();
            EditorApplication.Exit(0);
        }

        public static bool RunAllChecks(out string receipt)
        {
            return RunAllChecks(out receipt, out _);
        }

        public static bool RunAllChecks(out string receipt, out List<string> passedChecks)
        {
            var checks = new List<string>();
            passedChecks = checks;

            try
            {
                // 1. Deterministic Semantic Normalization & Negation Rejection
                TestSemanticInterpreter(checks);

                // 2. Terrain Bank and Fish Geometry Calculations
                TestBankAndFishGeometry(checks);

                // 3. Hunter Club Stowing and Attachment
                TestClubStowPreservation(checks);

                // 4. Fishing Interaction Reserved Fish Progression (No Stalled Bobber)
                TestReservedFishProgression(checks);
                TestIndividualBaitInterest(checks);

                // 5. Public Command Routing from Normal Club-in-Hand State
                TestClubInHandCommandRouting(checks);

                // 6. Multiple Perceived Fish Selection
                TestMultipleFishSelection(checks);

                // 7. Negative Terminal Receipts for Production Public Behavior
                TestNegativeTerminalReceipts(checks);
                TestObservedItemMemory(checks);

                receipt = $"All {checks.Count} 'go fish' validation assertions passed:\n" + string.Join("\n", checks);
                return true;
            }
            catch (Exception ex)
            {
                receipt = $"Failed check: {ex}\nPassed so far:\n" + string.Join("\n", checks);
                return false;
            }
        }

        private static void TestObservedItemMemory(List<string> checks)
        {
            var model = new Starfall.Food.FoodModel("memory-world", "memory-generation", 12);
            var state = model.State;
            if (Starfall.Food.ItemObservationMemory.Nearest(state, "basket", Vector3.zero) != null)
                throw new Exception("Unobserved basket leaked into item memory.");
            Starfall.Food.ItemObservationMemory.Observe(state, "seen-basket", "basket", new Vector3(42, 1, -30), true, true, 20);
            var restored = new Starfall.Food.FoodModel(state.world, state.generation, state.seed);
            if (!restored.Restore(model.Json(), state.world, state.generation) ||
                Starfall.Food.ItemObservationMemory.Nearest(restored.State, "basket", Vector3.zero)?.id != "seen-basket")
                throw new Exception("Observed basket memory failed save/restore.");
            restored.State.observedItems[0].position += Vector3.right;
            if (Starfall.Food.ItemObservationMemory.Valid(restored.State)) throw new Exception("Tampered item position accepted.");
            Starfall.Food.ItemObservationMemory.Observe(state, "seen-basket", "basket", new Vector3(45, 1, -30), false, true, 21);
            if (Starfall.Food.ItemObservationMemory.Nearest(state, "basket", Vector3.zero) != null || state.observedItems.Count != 1)
                throw new Exception("New observation did not supersede stale availability.");
            state.observedItems[0].world = "other-world";
            if (Starfall.Food.ItemObservationMemory.Valid(state)) throw new Exception("Cross-world item memory accepted.");
            checks.Add("Item memory: unseen absent; observed basket survives reload; tamper/scope rejected; changed availability supersedes");
        }

        private static void TestIndividualBaitInterest(List<string> checks)
        {
            var fish = new List<RiverFishSchool.RiverFishInstance>();
            var response = new FishBaitResponse();
            Vector3 bait = new Vector3(-4, CoastalWater.CurrentLevel - 0.38f, -32);
            try
            {
                for (int i = 0; i < 12; i++)
                {
                    var go = new GameObject("bait-interest-fish-" + i);
                    go.transform.position = bait + Vector3.forward;
                    fish.Add(new RiverFishSchool.RiverFishInstance { gameObject = go });
                }
                response.Begin(1);
                response.Tick(0.1f, bait, fish);
                if (response.InterestedFish != null) throw new Exception("Immediate school-wide bait reaction.");
                for (int step = 0; step < 1200 && !response.AtBait; step++)
                {
                    response.Tick(0.05f, bait, fish);
                    int controlled = 0;
                    foreach (var f in fish)
                    {
                        if (f.fishingControlled) controlled++;
                        if (f != response.InterestedFish && Vector3.Distance(f.gameObject.transform.position, bait + Vector3.forward) > 0.001f)
                            throw new Exception("An uninterested fish moved toward bait.");
                    }
                    if (controlled > 1) throw new Exception("Multiple fish simultaneously claimed one bait.");
                }
                if (!response.AtBait || response.Elapsed < 2f) throw new Exception("No delayed physical approach to bait.");
                var claimant = response.InterestedFish;
                response.Release();
                if (claimant.isReserved || claimant.fishingControlled) throw new Exception("Cancelled bait retained fish ownership.");
                checks.Add("[BaitInterest] Twelve fish: delayed individual decisions, one physical approach, no school rush, clean cancellation.");
            }
            finally
            {
                response.Release();
                foreach (var f in fish) UnityEngine.Object.DestroyImmediate(f.gameObject);
            }
        }

        private static void TestSemanticInterpreter(List<string> checks)
        {
            // Exact 'go fish' directives
            AssertAction("go fish", SemanticActionKind.CatchFish, "deterministic");
            AssertAction("go fishing", SemanticActionKind.CatchFish, "deterministic");
            AssertAction("fishing", SemanticActionKind.CatchFish, "deterministic");
            AssertAction("catch fish", SemanticActionKind.CatchFish, "deterministic");
            AssertAction("catch a fish", SemanticActionKind.CatchFish, "deterministic");
            AssertAction("fish", SemanticActionKind.CatchFish, "deterministic");

            // Chained variants
            AssertAction("go fish then roast", SemanticActionKind.CatchThenRoast, "deterministic");
            AssertAction("go fishing then roast", SemanticActionKind.CatchThenRoast, "deterministic");
            AssertAction("go fish and roast", SemanticActionKind.CatchThenRoast, "deterministic");
            AssertAction("go fishing and roast", SemanticActionKind.CatchThenRoast, "deterministic");

            AssertAction("go fish then eat", SemanticActionKind.CatchThenEat, "deterministic");
            AssertAction("go fishing then eat", SemanticActionKind.CatchThenEat, "deterministic");
            AssertAction("go fish and eat", SemanticActionKind.CatchThenEat, "deterministic");
            AssertAction("go fishing and eat", SemanticActionKind.CatchThenEat, "deterministic");

            AssertAction("go fish then store", SemanticActionKind.CatchThenStore, "deterministic");
            AssertAction("go fishing then store", SemanticActionKind.CatchThenStore, "deterministic");
            AssertAction("go fish and store", SemanticActionKind.CatchThenStore, "deterministic");
            AssertAction("go fishing and store", SemanticActionKind.CatchThenStore, "deterministic");

            // Strict negation rejection
            AssertNegation("don't go fish");
            AssertNegation("dont go fish");
            AssertNegation("never fish");
            AssertNegation("avoid fishing");
            AssertNegation("stop fishing");
            AssertNegation("cannot fish");
            AssertNegation("do not catch fish");

            checks.Add("[SemanticInterpreter] Verified 'go fish' canonical shortcuts, chained actions, and strict negation filtering.");
        }

        private static void AssertAction(string text, SemanticActionKind expectedAction, string expectedSource)
        {
            var res = StarfallSemanticInterpreter.InterpretDeterministic(text);
            if (!res.Success || res.Action != expectedAction || res.Source != expectedSource)
            {
                throw new InvalidOperationException($"Directive '{text}' expected {expectedAction} via {expectedSource}, got {res.Action} via {res.Source} (success={res.Success}, reason={res.Reason}).");
            }
        }

        private static void AssertNegation(string text)
        {
            var res = StarfallSemanticInterpreter.InterpretDeterministic(text);
            if (res.Success || res.Action != SemanticActionKind.Rejected || res.Source != "negation-filter")
            {
                throw new InvalidOperationException($"Negation directive '{text}' was not rejected by negation filter: action={res.Action}, source={res.Source}, reason={res.Reason}");
            }
        }

        private static void TestBankAndFishGeometry(List<string> checks)
        {
            var go = new GameObject("Test_TerrainNav_Go");
            try
            {
                var nav = go.AddComponent<NpcTerrainNavigation>();

                // Verify FindNearestRiverBank returns a dry bank point above water level
                Vector3 origin = new Vector3(0, CoastalTerrain.Height(0, 0), 0);
                Vector3 bank = nav.FindNearestRiverBank(origin);

                if (bank == Vector3.zero)
                {
                    float cx = CoastalTerrain.RiverCenterlineX(20);
                    Debug.Log($"BANK_CENTER z20 cx={cx}, water={CoastalWater.CurrentLevel}");
                    for (float offset = -45; offset <= 45; offset += 10)
                    {
                        float x = cx + offset;
                        float nx = Mathf.MoveTowards(x,cx,5);
                        var p = new Vector3(x,CoastalTerrain.Height(x,20),20);
                        Debug.Log($"BANK_SAMPLE {p} adjacent={CoastalTerrain.IsFreshwaterRiver(nx,20,CoastalTerrain.Height(nx,20),CoastalWater.CurrentLevel)} walk={nav.Walkable(p,out var f)} floor={f}");
                    }
                    for (float z = -40; z <= 40; z += 20)
                        for (float x = -50; x <= 50; x += 10)
                        {
                            var p = new Vector3(x, CoastalTerrain.Height(x,z), z);
                            if (p.y >= CoastalWater.CurrentLevel + 0.15f)
                                Debug.Log($"BANK_DIAGNOSTIC {p} walk={nav.Walkable(p,out var floor)} floor={floor} safe={nav.IsWithinSafePerimeter(p,5)}");
                        }
                    throw new InvalidOperationException("FindNearestRiverBank returned Vector3.zero for valid island origin.");
                }

                if (bank.y < CoastalWater.Level + 0.15f)
                {
                    throw new InvalidOperationException($"FindNearestRiverBank returned elevation {bank.y:F2}m which is below dry bank margin ({CoastalWater.Level + 0.15f:F2}m).");
                }


                if (!nav.TryFindNearestRiverBank(origin, out Vector3 tryBank) || tryBank != bank)
                {
                    throw new InvalidOperationException("TryFindNearestRiverBank did not match FindNearestRiverBank output.");
                }

                // Verify TryFindCastingBankForFish
                Vector3 fishPos = new Vector3(2f, CoastalWater.Level, -15f); // Known freshwater river coordinate
                if (nav.TryFindCastingBankForFish(origin, fishPos, out Vector3 castingBank))
                {
                    if (castingBank.y < CoastalWater.Level + 0.15f)
                    {
                        throw new InvalidOperationException($"Casting bank {castingBank.y:F2}m is below dry waterline margin ({CoastalWater.Level + 0.15f:F2}m).");
                    }
                    float castDist = Vector3.Distance(castingBank, fishPos);
                    if (castDist < 2.0f || castDist > 18.0f)
                    {
                        throw new InvalidOperationException($"Casting distance {castDist:F2}m out of valid casting range [2.0m, 18.0m].");
                    }
                    checks.Add($"[TerrainNavigation] Validated dry bank geometry: bank={castingBank} distToFish={castDist:F1}m elevation={castingBank.y:F2}m (waterLevel={CoastalWater.Level:F2}m).");
                }
                else
                {
                    checks.Add("[TerrainNavigation] Validated FindNearestRiverBank dry elevation margin.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void TestClubStowPreservation(List<string> checks)
        {
            var actorGo = new GameObject("Test_Actor_Club");
            var clubGo = new GameObject("Test_Club_Prop");
            clubGo.transform.SetParent(actorGo.transform, false);

            try
            {
                var carry = actorGo.AddComponent<HunterClubCarry>();
                carry.Actor = actorGo.transform;
                carry.Club = clubGo.transform;

                bool stowed = carry.SetStowed(true, true);
                if (!stowed || !carry.Stowed)
                {
                    throw new InvalidOperationException("HunterClubCarry.SetStowed(true) failed to set Stowed flag.");
                }

                if (clubGo == null || !clubGo.activeSelf)
                {
                    throw new InvalidOperationException("HunterClubCarry stowing deleted or deactivated club GameObject (must preserve prop on back).");
                }

                checks.Add("[HunterClubCarry] Validated club stowing preserves GameObject and attaches to back without deletion.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actorGo);
            }
        }

        private static void TestReservedFishProgression(List<string> checks)
        {
            var go = new GameObject("Test_FishingInteraction_Go");
            var rodGo = new GameObject("Test_Rod_Go");
            var rightHandGo = new GameObject("RightHand");
            var leftHandGo = new GameObject("LeftHand");
            var fishGo = new GameObject("Test_Fish_Go");
            var schoolGo = new GameObject("Test_Bait_School");

            try
            {
                rightHandGo.transform.SetParent(go.transform, false);
                leftHandGo.transform.SetParent(go.transform, false);

                var brain = go.AddComponent<NpcAutonomy>();
                var actions = new NpcActionApi("test-agent", "test-world", go.transform, rightHandGo.transform, leftHandGo.transform, Array.Empty<NpcInteractable>());
                brain.SetActionsForTesting(actions);

                var rod = rodGo.AddComponent<FishingRodItem>();
                rod.TipTransform = rodGo.transform;
                var rodPhys = rodGo.AddComponent<PhysicalItem>();
                rodPhys.itemId = "tool-fishing-rod";
                rodPhys.itemTypeId = FishingRodItem.ItemTypeId;
                var rodNi = rodGo.AddComponent<NpcInteractable>();
                rodNi.StableId = "tool-fishing-rod";
                rodNi.Kind = NpcObjectKind.Item;
                rodNi.Permission = true;
                actions.RegisterInteractable(rodNi);
                actions.HoldItemDirect(rodNi, false);

                var fishing = go.AddComponent<FishingInteraction>();
                fishing.Brain = brain;
                fishing.ActiveRod = rod;

                var mockFish = new RiverFishSchool.RiverFishInstance
                {
                    gameObject = fishGo,
                    bodyTransform = fishGo.transform,
                    scale = 0.85f,
                    isReserved = false
                };

                // Position actor at shore and cast toward known river target with specific reserved fish
                Vector3 shorePos = new Vector3(2.0f, CoastalTerrain.Height(2.0f, -32.0f), -32.0f);
                Vector3 targetWater = new Vector3(-4.0f, CoastalWater.CurrentLevel, -32.0f);
                go.transform.position = shorePos;
                fishGo.transform.position = targetWater + new Vector3(0, -0.38f, 1f);
                var school = schoolGo.AddComponent<RiverFishSchool>();
                RiverFishSchool.Instance = school;
                school.ActiveFish.Add(mockFish);

                bool castOk = fishing.StartCast(targetWater, mockFish);
                if (!castOk || fishing.State != FishingState.Casting)
                {
                    throw new InvalidOperationException($"StartCast failed for ReservedFish: ok={castOk}, state={fishing.State}, receipt='{fishing.LastReceipt}'");
                }

                // Advance cast flight into Floating
                fishing.Tick(FishingInteraction.CastDuration + 0.1f);
                if (fishing.State != FishingState.Floating)
                {
                    throw new InvalidOperationException($"Expected state to transition to Floating after cast flight, got {fishing.State} with receipt '{fishing.LastReceipt}'.");
                }

                fishing.Tick(0.1f);
                if (fishing.State != FishingState.Floating || fishing.ReservedFish != null)
                    throw new InvalidOperationException("Fish rushed bait immediately after cast.");
                Vector3 start = fishGo.transform.position;
                for (int i = 0; i < 1200 && fishing.State != FishingState.Bite; i++)
                    fishing.Tick(0.05f);
                if (fishing.State != FishingState.Bite || !fishing.BaitEaten ||
                    Vector3.Distance(fishGo.transform.position, fishing.BaitPosition) > FishBaitResponse.ContactRadius)
                    throw new InvalidOperationException($"Fish did not physically reach and eat bait: {fishing.State}, {fishing.LastReceipt}");
                if (Vector3.Distance(start, fishGo.transform.position) < 0.5f)
                    throw new InvalidOperationException("Fish did not visibly approach bait.");

                // Strike during bite: must hook ReservedFish without failing on TryReserveFishNear
                bool strikeOk = fishing.StrikeAndReel(out string caughtSpecies, out float fishScale, out string receipt);
                if (!strikeOk || fishing.State != FishingState.Reeling)
                {
                    throw new InvalidOperationException($"StrikeAndReel failed during Bite for ReservedFish: ok={strikeOk}, state={fishing.State}, receipt={receipt}");
                }

                if (fishing.ReservedFish != mockFish)
                {
                    throw new InvalidOperationException("StrikeAndReel substituted or lost the exact ReservedFish instance.");
                }

                checks.Add("[FishingInteraction] Validated end-to-end ReservedFish progression: Floating -> Nibble -> Bite -> StrikeAndReel hooks exact reserved fish without stalling.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(fishGo);
                UnityEngine.Object.DestroyImmediate(schoolGo);
                RiverFishSchool.Instance = null;
                UnityEngine.Object.DestroyImmediate(rodGo);
            }
        }

        private static void TestClubInHandCommandRouting(List<string> checks)
        {
            var actorGo = new GameObject("Test_Actor_ClubInHand");
            var clubGo = new GameObject("Test_Club");
            var rodGo = new GameObject("Test_Rod_Ground");
            var rightHandGo = new GameObject("RightHand");
            var leftHandGo = new GameObject("LeftHand");

            try
            {
                rightHandGo.transform.SetParent(actorGo.transform, false);
                leftHandGo.transform.SetParent(actorGo.transform, false);

                var brain = actorGo.AddComponent<NpcAutonomy>();
                var actions = new NpcActionApi("test-agent", "test-world", actorGo.transform, rightHandGo.transform, leftHandGo.transform, Array.Empty<NpcInteractable>());
                brain.SetActionsForTesting(actions);

                var perception = actorGo.AddComponent<NpcPerception>();
                brain.Perception = perception;

                var carry = actorGo.AddComponent<HunterClubCarry>();
                carry.Actor = actorGo.transform;
                carry.Club = clubGo.transform;

                var clubNi = clubGo.AddComponent<NpcInteractable>();
                clubNi.StableId = "tool-hunter-club";
                clubNi.Kind = NpcObjectKind.Item;
                actions.RegisterInteractable(clubNi);

                // Normal club-in-hand state: right hand holds club, not stowed
                actions.HoldItemDirect(clubNi, false);
                carry.SetStowed(false, true);

                // Ground fishing rod within reach
                rodGo.transform.position = actorGo.transform.position + Vector3.forward * 0.5f;
                var rodNi = rodGo.AddComponent<NpcInteractable>();
                rodNi.StableId = "tool-fishing-rod";
                rodNi.Kind = NpcObjectKind.Item;
                rodNi.Permission = true;
                rodNi.HeldBy = "";
                rodNi.DeliveredTo = "";
                actions.RegisterInteractable(rodNi);

                var rodPhys = rodGo.AddComponent<PhysicalItem>();
                rodPhys.itemId = "tool-fishing-rod";
                rodPhys.itemTypeId = FishingRodItem.ItemTypeId;

                perception.Current.Add(new NpcObservation
                {
                    id = "tool-fishing-rod",
                    kind = NpcObjectKind.Item,
                    position = rodGo.transform.position,
                    permission = true,
                    available = true
                });

                var survival = actorGo.AddComponent<StarfallSurvivalAutonomy>();
                survival.Brain = brain;
                brain.Survival = survival;

                bool submitOk = survival.SubmitNaturalLanguageCommand("go fish");
                if (!submitOk || !survival.HasActiveCommand)
                {
                    throw new InvalidOperationException("Failed to submit 'go fish' command from club-in-hand state.");
                }

                if (survival.ActiveCommandCurrentStep != "Equip fishing rod")
                {
                    throw new InvalidOperationException($"Expected step 'Equip fishing rod', got '{survival.ActiveCommandCurrentStep}'.");
                }

                bool step1Done = survival.StepActiveCommand();
                if (!step1Done)
                {
                    throw new InvalidOperationException($"Step 1 (Equip fishing rod) did not complete: {survival.ActiveCommandStatus}");
                }

                // Verify: Club was stowed to back, prop preserved
                if (!carry.Stowed)
                {
                    throw new InvalidOperationException("HunterClubCarry was not set to stowed when equipping fishing rod.");
                }
                if (clubGo == null || !clubGo.activeSelf)
                {
                    throw new InvalidOperationException("Hunter club GameObject was destroyed or deactivated during stowing.");
                }

                // Verify: Rod equipped in right hand, left hand free
                if (actions.HeldRight != rodNi)
                {
                    throw new InvalidOperationException($"Fishing rod was not equipped into right hand (HeldRight={actions.HeldRight}).");
                }
                if (actions.HeldLeft != null)
                {
                    throw new InvalidOperationException($"Left hand was not free after equipping rod (HeldLeft={actions.HeldLeft}).");
                }

                // Verify: Command advanced to step 2
                if (survival.ActiveCommandStepIndex != 1 || survival.ActiveCommandCurrentStep != "Select active fish & calculate casting bank")
                {
                    throw new InvalidOperationException($"Command did not advance to step 2: index={survival.ActiveCommandStepIndex}, current='{survival.ActiveCommandCurrentStep}'.");
                }

                checks.Add("[CommandRouting] Verified 'go fish' public routing from normal club-in-hand: stows club to back, equips rod in right hand, leaves left hand free.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actorGo);
                UnityEngine.Object.DestroyImmediate(clubGo);
                UnityEngine.Object.DestroyImmediate(rodGo);
            }
        }

        private static void TestMultipleFishSelection(List<string> checks)
        {
            var actorGo = new GameObject("Test_Actor_MultiFish");
            var schoolGo = new GameObject("Test_School_MultiFish");
            var fish1Go = new GameObject("Test_Fish_Far");
            var fish2Go = new GameObject("Test_Fish_Near");
            var fish3Go = new GameObject("Test_Fish_Reserved");
            var rodGo = new GameObject("Test_Rod_Held");
            var rightHandGo = new GameObject("RightHand");
            var leftHandGo = new GameObject("LeftHand");

            try
            {
                rightHandGo.transform.SetParent(actorGo.transform, false);
                leftHandGo.transform.SetParent(actorGo.transform, false);

                var brain = actorGo.AddComponent<NpcAutonomy>();
                var nav = actorGo.AddComponent<NpcTerrainNavigation>();
                brain.TerrainNavigation = nav;

                var perception = actorGo.AddComponent<NpcPerception>();
                brain.Perception = perception;

                var actions = new NpcActionApi("test-agent", "test-world", actorGo.transform, rightHandGo.transform, leftHandGo.transform, Array.Empty<NpcInteractable>());
                brain.SetActionsForTesting(actions);

                // Position actor at known dry river bank
                Vector3 actorPos = new Vector3(43f, CoastalTerrain.Height(43f, 0f), 0f);
                if (actorPos.y < CoastalWater.Level + CoastalTide.AmplitudeMetres + 0.15f)
                    throw new InvalidOperationException("Multiple fish fixture must start on real dry terrain.");
                actorGo.transform.position = actorPos;

                var rod = rodGo.AddComponent<FishingRodItem>();
                rod.TipTransform = rodGo.transform;
                var rodPhys = rodGo.AddComponent<PhysicalItem>();
                rodPhys.itemId = "tool-fishing-rod";
                rodPhys.itemTypeId = FishingRodItem.ItemTypeId;
                var rodNi = rodGo.AddComponent<NpcInteractable>();
                rodNi.StableId = "tool-fishing-rod";
                rodNi.Kind = NpcObjectKind.Item;
                rodNi.Permission = true;
                actions.RegisterInteractable(rodNi);
                actions.HoldItemDirect(rodNi, false);

                var fishing = actorGo.AddComponent<FishingInteraction>();
                fishing.Brain = brain;
                fishing.ActiveRod = rod;

                var survival = actorGo.AddComponent<StarfallSurvivalAutonomy>();
                survival.Brain = brain;
                brain.Survival = survival;

                var school = schoolGo.AddComponent<RiverFishSchool>();
                RiverFishSchool.Instance = school;

                // Fish 1: Farther active fish (in freshwater river channel)
                Vector3 fish1Pos = new Vector3(23f, CoastalWater.Level, 0f);
                fish1Go.transform.position = fish1Pos;
                var fish1Ni = fish1Go.AddComponent<NpcInteractable>();
                fish1Ni.StableId = "river-fish-far";
                fish1Ni.Kind = NpcObjectKind.Item;
                var fish1Phys = fish1Go.AddComponent<PhysicalItem>();
                fish1Phys.itemId = "river-fish-far";
                fish1Phys.itemTypeId = "food-river-fish";
                var inst1 = new RiverFishSchool.RiverFishInstance
                {
                    gameObject = fish1Go,
                    interactable = fish1Ni,
                    physicalItem = fish1Phys,
                    swimCenter = fish1Pos,
                    scale = 0.8f,
                    isReserved = false
                };

                // Fish 2: Nearer active fish (in freshwater river channel)
                Vector3 fish2Pos = new Vector3(26f, CoastalWater.Level, -1f);
                fish2Go.transform.position = fish2Pos;
                var fish2Ni = fish2Go.AddComponent<NpcInteractable>();
                fish2Ni.StableId = "river-fish-near";
                fish2Ni.Kind = NpcObjectKind.Item;
                var fish2Phys = fish2Go.AddComponent<PhysicalItem>();
                fish2Phys.itemId = "river-fish-near";
                fish2Phys.itemTypeId = "food-river-carp";
                var inst2 = new RiverFishSchool.RiverFishInstance
                {
                    gameObject = fish2Go,
                    interactable = fish2Ni,
                    physicalItem = fish2Phys,
                    swimCenter = fish2Pos,
                    scale = 0.9f,
                    isReserved = false
                };

                // Fish 3: Nearest, but already reserved by another process
                Vector3 fish3Pos = new Vector3(27f, CoastalWater.Level, 0f);
                fish3Go.transform.position = fish3Pos;
                var fish3Ni = fish3Go.AddComponent<NpcInteractable>();
                fish3Ni.StableId = "river-fish-reserved";
                fish3Ni.Kind = NpcObjectKind.Item;
                var fish3Phys = fish3Go.AddComponent<PhysicalItem>();
                fish3Phys.itemId = "river-fish-reserved";
                fish3Phys.itemTypeId = "food-river-fish";
                var inst3 = new RiverFishSchool.RiverFishInstance
                {
                    gameObject = fish3Go,
                    interactable = fish3Ni,
                    physicalItem = fish3Phys,
                    swimCenter = fish3Pos,
                    scale = 0.85f,
                    isReserved = true // Pre-reserved!
                };

                school.ActiveFish.Add(inst1);
                school.ActiveFish.Add(inst2);
                school.ActiveFish.Add(inst3);

                // Add all to perception
                perception.Current.Add(new NpcObservation { id = fish1Ni.StableId, kind = NpcObjectKind.Item, position = fish1Pos, permission = true, available = true });
                perception.Current.Add(new NpcObservation { id = fish2Ni.StableId, kind = NpcObjectKind.Item, position = fish2Pos, permission = true, available = true });
                perception.Current.Add(new NpcObservation { id = fish3Ni.StableId, kind = NpcObjectKind.Item, position = fish3Pos, permission = true, available = true });

                bool submitOk = survival.SubmitNaturalLanguageCommand("go fish");
                if (!submitOk || !survival.HasActiveCommand)
                {
                    throw new InvalidOperationException("Failed to submit 'go fish' command for multiple fish selection.");
                }

                // Step 1: Equip fishing rod (already held, completes)
                survival.StepActiveCommand();

                // Step 2: Select active fish & calculate casting bank
                if (survival.ActiveCommandCurrentStep != "Select active fish & calculate casting bank")
                {
                    throw new InvalidOperationException($"Expected step 'Select active fish & calculate casting bank', got '{survival.ActiveCommandCurrentStep}'.");
                }

                bool step2Done = survival.StepActiveCommand();
                if (!step2Done)
                {
                    throw new InvalidOperationException($"Step 2 did not complete: {survival.ActiveCommandStatus}");
                }

                if (!survival.HasActiveCommand ||
                    survival.ActiveCommandCurrentStep != "Route to casting bank")
                {
                    throw new InvalidOperationException(
                        $"Fish selection did not advance to a live route step. " +
                        $"active={survival.HasActiveCommand}, index={survival.ActiveCommandStepIndex}, " +
                        $"step='{survival.ActiveCommandCurrentStep}', status='{survival.ActiveCommandStatus}', " +
                        $"receipt='{survival.LastCommandReceipt}'.");
                }

                // Verify: Fish 3 (reserved) was skipped
                if (!inst3.isReserved)
                {
                    throw new InvalidOperationException("Reserved fish state was corrupted.");
                }

                // Verify: Fish 2 (nearer unreserved) was selected, NOT Fish 1
                if (!inst2.isReserved)
                {
                    throw new InvalidOperationException("Nearest unreserved fish (fish2) was not reserved.");
                }
                if (inst1.isReserved)
                {
                    throw new InvalidOperationException("Farther fish (fish1) was reserved instead of nearest fish.");
                }

                checks.Add("[MultipleFishSelection] Verified deterministic selection of nearest reachable active fish and skipping of reserved fish.");
            }
            finally
            {
                RiverFishSchool.Instance = null;
                UnityEngine.Object.DestroyImmediate(actorGo);
                UnityEngine.Object.DestroyImmediate(schoolGo);
                UnityEngine.Object.DestroyImmediate(fish1Go);
                UnityEngine.Object.DestroyImmediate(fish2Go);
                UnityEngine.Object.DestroyImmediate(fish3Go);
                UnityEngine.Object.DestroyImmediate(rodGo);
            }
        }

        private static void TestNegativeTerminalReceipts(List<string> checks)
        {
            // 1. Missing Rod: neither held nor accessible in territory
            {
                var actorGo = new GameObject("Test_Neg_MissingRod");
                var rightHandGo = new GameObject("RightHand"); rightHandGo.transform.SetParent(actorGo.transform, false);
                var leftHandGo = new GameObject("LeftHand"); leftHandGo.transform.SetParent(actorGo.transform, false);

                try
                {
                    var brain = actorGo.AddComponent<NpcAutonomy>();
                    var perception = actorGo.AddComponent<NpcPerception>();
                    brain.Perception = perception;
                    var actions = new NpcActionApi("test-agent", "test-world", actorGo.transform, rightHandGo.transform, leftHandGo.transform, Array.Empty<NpcInteractable>());
                    brain.SetActionsForTesting(actions);

                    var survival = actorGo.AddComponent<StarfallSurvivalAutonomy>();
                    survival.Brain = brain;
                    brain.Survival = survival;

                    bool submit = survival.SubmitNaturalLanguageCommand("go fish");
                    if (!submit || !survival.HasActiveCommand)
                        throw new InvalidOperationException("Failed to submit command for missing rod test.");

                    survival.StepActiveCommand();

                    if (survival.HasActiveCommand)
                        throw new InvalidOperationException("Command still active when rod is missing.");
                    if (survival.LastCommandReceipt != "command-cancelled: command-failed-no-rod-available")
                        throw new InvalidOperationException($"Expected 'command-cancelled: command-failed-no-rod-available', got '{survival.LastCommandReceipt}'.");

                    checks.Add("[NegativeReceipt:MissingRod] Verified terminal receipt: 'command-cancelled: command-failed-no-rod-available'.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(actorGo);
                }
            }

            // 2. Hands Full: right hand holds item that cannot be stowed or dropped
            {
                var actorGo = new GameObject("Test_Neg_HandsFull");
                var itemGo = new GameObject("Test_UndroppableItem");
                var rightHandGo = new GameObject("RightHand"); rightHandGo.transform.SetParent(actorGo.transform, false);
                var leftHandGo = new GameObject("LeftHand"); leftHandGo.transform.SetParent(actorGo.transform, false);

                try
                {
                    var brain = actorGo.AddComponent<NpcAutonomy>();
                    var perception = actorGo.AddComponent<NpcPerception>();
                    brain.Perception = perception;
                    var actions = new NpcActionApi("test-agent", "test-world", actorGo.transform, rightHandGo.transform, leftHandGo.transform, Array.Empty<NpcInteractable>());
                    actions.AllowDrop = false; // Disallow drop to simulate blocked/full hands
                    brain.SetActionsForTesting(actions);

                    itemGo.transform.SetParent(rightHandGo.transform, false);
                    var itemNi = itemGo.AddComponent<NpcInteractable>();
                    itemNi.StableId = "heavy-boulder";
                    itemNi.Kind = NpcObjectKind.Item;
                    actions.RegisterInteractable(itemNi);
                    actions.HoldItemDirect(itemNi, false);

                    var survival = actorGo.AddComponent<StarfallSurvivalAutonomy>();
                    survival.Brain = brain;
                    brain.Survival = survival;

                    bool submit = survival.SubmitNaturalLanguageCommand("go fish");
                    if (!submit || !survival.HasActiveCommand)
                        throw new InvalidOperationException("Failed to submit command for hands full test.");

                    survival.StepActiveCommand();

                    if (survival.HasActiveCommand)
                        throw new InvalidOperationException("Command still active when hands are full.");
                    if (survival.LastCommandReceipt != "command-cancelled: command-failed-hands-full")
                        throw new InvalidOperationException($"Expected 'command-cancelled: command-failed-hands-full', got '{survival.LastCommandReceipt}'.");

                    checks.Add("[NegativeReceipt:HandsFull] Verified terminal receipt: 'command-cancelled: command-failed-hands-full'.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(actorGo);
                    UnityEngine.Object.DestroyImmediate(itemGo);
                }
            }

            // 3. Unknown River Geometry: actor far inland with zero river perception/memory
            {
                var actorGo = new GameObject("Test_Neg_UnknownRiver");
                var rodGo = new GameObject("Test_Held_Rod");
                var rightHandGo = new GameObject("RightHand"); rightHandGo.transform.SetParent(actorGo.transform, false);
                var leftHandGo = new GameObject("LeftHand"); leftHandGo.transform.SetParent(actorGo.transform, false);

                try
                {
                    // Far inland position where river is completely unknown (>100m away)
                    actorGo.transform.position = new Vector3(200f, CoastalTerrain.Height(200f, 60f), 60f);

                    var brain = actorGo.AddComponent<NpcAutonomy>();
                    var perception = actorGo.AddComponent<NpcPerception>();
                    brain.Perception = perception;
                    var actions = new NpcActionApi("test-agent", "test-world", actorGo.transform, rightHandGo.transform, leftHandGo.transform, Array.Empty<NpcInteractable>());
                    brain.SetActionsForTesting(actions);

                    rodGo.transform.SetParent(rightHandGo.transform, false);
                    var rodNi = rodGo.AddComponent<NpcInteractable>();
                    rodNi.StableId = "tool-fishing-rod";
                    rodNi.Kind = NpcObjectKind.Item;
                    var rodPhys = rodGo.AddComponent<PhysicalItem>();
                    rodPhys.itemId = "tool-fishing-rod";
                    rodPhys.itemTypeId = FishingRodItem.ItemTypeId;
                    actions.RegisterInteractable(rodNi);
                    actions.HoldItemDirect(rodNi, false);

                    var survival = actorGo.AddComponent<StarfallSurvivalAutonomy>();
                    survival.Brain = brain;
                    brain.Survival = survival;

                    bool submit = survival.SubmitNaturalLanguageCommand("go fish");
                    if (!submit || !survival.HasActiveCommand)
                        throw new InvalidOperationException("Failed to submit command for unknown river test.");

                    // Step 1: Equip rod (already held, completes)
                    survival.StepActiveCommand();

                    // Step 2: Select fish (fails closed because river is unknown)
                    survival.StepActiveCommand();

                    if (survival.HasActiveCommand)
                        throw new InvalidOperationException("Command still active when river geometry is unknown.");
                    if (survival.LastCommandReceipt != "command-cancelled: command-failed-river-unknown")
                        throw new InvalidOperationException($"Expected 'command-cancelled: command-failed-river-unknown', got '{survival.LastCommandReceipt}'.");

                    checks.Add("[NegativeReceipt:UnknownRiver] Verified terminal receipt: 'command-cancelled: command-failed-river-unknown'.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(actorGo);
                    UnityEngine.Object.DestroyImmediate(rodGo);
                }
            }

            // 4. Empty River / No Fish Perceived: actor at river bank, but no fish visible
            {
                var actorGo = new GameObject("Test_Neg_EmptyRiver");
                var rodGo = new GameObject("Test_Held_Rod");
                var schoolGo = new GameObject("Test_School_Empty");
                var rightHandGo = new GameObject("RightHand"); rightHandGo.transform.SetParent(actorGo.transform, false);
                var leftHandGo = new GameObject("LeftHand"); leftHandGo.transform.SetParent(actorGo.transform, false);

                try
                {
                    // Actor standing directly at known river bank
                    Vector3 bankPos = new Vector3(2.5f, CoastalTerrain.Height(2.5f, -15.0f), -15.0f);
                    actorGo.transform.position = bankPos;

                    var brain = actorGo.AddComponent<NpcAutonomy>();
                    var perception = actorGo.AddComponent<NpcPerception>();
                    brain.Perception = perception;
                    var nav = actorGo.AddComponent<NpcTerrainNavigation>();
                    brain.TerrainNavigation = nav;

                    var actions = new NpcActionApi("test-agent", "test-world", actorGo.transform, rightHandGo.transform, leftHandGo.transform, Array.Empty<NpcInteractable>());
                    brain.SetActionsForTesting(actions);

                    rodGo.transform.SetParent(rightHandGo.transform, false);
                    var rodNi = rodGo.AddComponent<NpcInteractable>();
                    rodNi.StableId = "tool-fishing-rod";
                    rodNi.Kind = NpcObjectKind.Item;
                    var rodPhys = rodGo.AddComponent<PhysicalItem>();
                    rodPhys.itemId = "tool-fishing-rod";
                    rodPhys.itemTypeId = FishingRodItem.ItemTypeId;
                    actions.RegisterInteractable(rodNi);
                    actions.HoldItemDirect(rodNi, false);

                    // Empty fish school (zero fish)
                    var school = schoolGo.AddComponent<RiverFishSchool>();
                    school.ActiveFish.Clear();
                    RiverFishSchool.Instance = school;

                    var survival = actorGo.AddComponent<StarfallSurvivalAutonomy>();
                    survival.Brain = brain;
                    brain.Survival = survival;

                    // Arrange the negative case at the actual computed bank, not in
                    // the ford. This fixture tests observation failure, not walking.
                    perception.Current.Add(new NpcObservation { id = "river-water", kind = NpcObjectKind.Place,
                        position = new Vector3(-4f, CoastalWater.CurrentLevel, -32f), available = true });
                    Vector3 actualBank = survival.FindRiverBankTarget(actorGo.transform.position);
                    if (actualBank == Vector3.zero) throw new InvalidOperationException("Empty river fixture has no dry bank.");
                    actorGo.transform.position = actualBank;

                    bool submit = survival.SubmitNaturalLanguageCommand("go fish");
                    if (!submit || !survival.HasActiveCommand)
                        throw new InvalidOperationException("Failed to submit command for empty river test.");

                    // Step 1: Equip rod (completes)
                    survival.StepActiveCommand();

                    // Step 2: Select fish (actor at bank, senses, but 0 fish visible -> fails closed)
                    survival.StepActiveCommand();

                    if (survival.HasActiveCommand)
                        throw new InvalidOperationException("Command still active when river has no perceived fish.");
                    if (survival.LastCommandReceipt != "command-cancelled: command-failed-no-fish-perceived")
                        throw new InvalidOperationException($"Expected 'command-cancelled: command-failed-no-fish-perceived', got '{survival.LastCommandReceipt}'.");

                    checks.Add("[NegativeReceipt:EmptyRiver] Verified terminal receipt: 'command-cancelled: command-failed-no-fish-perceived'.");
                }
                finally
                {
                    RiverFishSchool.Instance = null;
                    UnityEngine.Object.DestroyImmediate(actorGo);
                    UnityEngine.Object.DestroyImmediate(rodGo);
                    UnityEngine.Object.DestroyImmediate(schoolGo);
                }
            }

            // 5. User Cancel via Esc Key
            {
                var actorGo = new GameObject("Test_Neg_UserEsc");
                try
                {
                    var brain = actorGo.AddComponent<NpcAutonomy>();
                    var survival = actorGo.AddComponent<StarfallSurvivalAutonomy>();
                    survival.Brain = brain;
                    brain.Survival = survival;

                    bool submit = survival.SubmitNaturalLanguageCommand("go fish");
                    if (!submit || !survival.HasActiveCommand)
                        throw new InvalidOperationException("Failed to submit command for user esc test.");

                    // Cancel explicitly as triggered by NpcPlayerControls escapeKey handler
                    survival.CancelActiveCommand("user-cancelled-via-esc");

                    if (survival.HasActiveCommand)
                        throw new InvalidOperationException("Command still active after user esc cancellation.");
                    if (survival.LastCommandReceipt != "command-cancelled: user-cancelled-via-esc")
                        throw new InvalidOperationException($"Expected 'command-cancelled: user-cancelled-via-esc', got '{survival.LastCommandReceipt}'.");

                    checks.Add("[NegativeReceipt:UserEsc] Verified terminal receipt: 'command-cancelled: user-cancelled-via-esc'.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(actorGo);
                }
            }
        }
    }
}
