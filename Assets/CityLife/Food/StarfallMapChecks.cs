using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Starfall.Food;
using CityLife.World;

namespace Starfall.Food
{
    public static class StarfallMapChecks
    {
        public static List<string> Run(string folder)
        {
            Directory.CreateDirectory(folder);
            var passed = new List<string>();
            void Check(bool yes, string name)
            {
                if (!yes) throw new Exception("STARFALL MAP CHECK FAILED: " + name);
                passed.Add(name);
            }

            const string world = "map-test-world";
            const string gen = "map-test-gen";
            const string actor = "inhabitant-1";

            // 1. Unknown entries absent: fresh model has no revealed cells, places or events
            var freshModel = new FoodModel(world, gen, 4242);
            var vm = new StarfallMapViewModel(world, gen, actor);
            bool updated = vm.Update(freshModel.State, new Vector3(0, 0, 0));

            Check(updated && vm.IsValid, "fresh model view model updates successfully");
            Check(vm.ExploredCellCount == 0, "unknown cells absent: explored cell count begins at 0");
            Check(vm.ObservedPlaceCount == 0, "unknown places absent: observed place count begins at 0");
            Check(vm.EventCount == 0, "unknown events absent: event count begins at 0");
            Check(!vm.IsCellExplored(0, 0) && !vm.IsCellExplored(5, 5) && !vm.IsCellExplored(-10, 20),
                "unvisited cells report not explored");
            Check(vm.GetRememberedPlace("spring-food") == null && vm.GetRememberedPlace("berry-food") == null,
                "unseen places return null without disclosing registry coordinates");
            string freshAscii = vm.GenerateAsciiGrid(2);
            Check(!freshAscii.Contains("B") && !freshAscii.Contains("S") && !freshAscii.Contains("R") && !freshAscii.Contains("·"),
                "ascii grid for undiscovered world contains no revealed resource or explored marks");
            Check(freshAscii.Contains("░"), "ascii grid for undiscovered world renders fog of war on unexplored cells");

            // 2. Only matching scope admitted (fails closed on mismatch and after new-world/reload rebinding)
            freshModel.State.tick = 1;
            PlaceLedger.Occupy(freshModel.State, 1, 2, 1);
            PlaceLedger.Observe(freshModel.State, "berry-food", "Place", "fruiting-succulent",
                new Vector3(3, 1, 6), true, true, 1, 10, false);

            var foreignWorldVm = new StarfallMapViewModel("foreign-world", gen, actor);
            Check(!foreignWorldVm.Update(freshModel.State, new Vector3(3, 1, 6)) && !foreignWorldVm.IsValid && foreignWorldVm.ExploredCellCount == 0,
                "mismatched world scope fails closed with zero visible knowledge");

            var foreignGenVm = new StarfallMapViewModel(world, "foreign-gen", actor);
            Check(!foreignGenVm.Update(freshModel.State, new Vector3(3, 1, 6)) && !foreignGenVm.IsValid && foreignGenVm.ExploredCellCount == 0,
                "mismatched generation scope fails closed with zero visible knowledge");

            var foreignActorVm = new StarfallMapViewModel(world, gen, "foreign-actor");
            Check(!foreignActorVm.Update(freshModel.State, new Vector3(3, 1, 6)) && !foreignActorVm.IsValid && foreignActorVm.ExploredCellCount == 0,
                "mismatched actor scope fails closed with zero visible knowledge");

            var nullVm = new StarfallMapViewModel(world, gen, actor);
            Check(!nullVm.Update(null, new Vector3(3, 1, 6)) && !nullVm.IsValid,
                "null state fails closed");

            // Rebinding restores matching scope
            foreignWorldVm.Rebind(world, gen, actor);
            Check(foreignWorldVm.Update(freshModel.State, new Vector3(3, 1, 6)) && foreignWorldVm.IsValid && foreignWorldVm.ExploredCellCount == 1,
                "explicit rebind to matching scope restores synchronization");

            // New-world reload isolation
            var newWorldModel = new FoodModel("new-isolated-world", "new-isolated-gen", 4242);
            Check(!foreignWorldVm.Update(newWorldModel.State, Vector3.zero) && !foreignWorldVm.IsValid,
                "new world state fails closed against existing bound view model before rebind");
            foreignWorldVm.Rebind("new-isolated-world", "new-isolated-gen", actor);
            Check(foreignWorldVm.Update(newWorldModel.State, Vector3.zero) && foreignWorldVm.ExploredCellCount == 0 && foreignWorldVm.ObservedPlaceCount == 0,
                "rebound new world starts completely clean without inheriting old world places");

            // 3. Distinct event revisions preserved with tick/provenance
            // Event 1: first-seen (available=true)
            // Event 2: depletion change (available=false) -> changed
            // Event 3: recovery change (available=true) -> changed
            // Event 4: unchanged availability revisit with fresh tick -> revisit
            var historyModel = new FoodModel(world, gen, 4242);
            var hs = historyModel.State;
            Vector3 berryPos = new Vector3(9, 2, 12);
            hs.tick = 10;
            PlaceLedger.Observe(hs, "berry-food", "Place", "fruiting-succulent", berryPos, true, true, 10, 100, false);
            hs.tick = 20;
            PlaceLedger.Observe(hs, "berry-food", "Place", "fruiting-succulent", berryPos, false, true, 20, 200, true);
            hs.tick = 30;
            PlaceLedger.Observe(hs, "berry-food", "Place", "fruiting-succulent", berryPos, true, true, 30, 300, true);
            hs.tick = 40;
            PlaceLedger.Observe(hs, "berry-food", "Place", "fruiting-succulent", berryPos, true, true, 40, 400, true);

            var historyVm = new StarfallMapViewModel(world, gen, actor);
            Check(historyVm.Update(hs, berryPos), "history model updates view model");
            Check(historyVm.EventCount == 4, "all 4 distinct events preserved in view model");

            var events = historyVm.GetObservationEvents();
            Check(events.Length == 4, "retrieved events length matches event count");
            Check(events[0].Sequence == 1 && events[0].Kind == "first-seen" && events[0].FoodTick == 10 && events[0].BrainTick == 100 && events[0].Available,
                "event #1 preserves first-seen status and tick provenance");
            Check(events[1].Sequence == 2 && events[1].Kind == "changed" && events[1].FoodTick == 20 && events[1].BrainTick == 200 && !events[1].Available,
                "event #2 preserves depletion change status and tick provenance");
            Check(events[2].Sequence == 3 && events[2].Kind == "changed" && events[2].FoodTick == 30 && events[2].BrainTick == 300 && events[2].Available,
                "event #3 preserves recovery change status and tick provenance");
            Check(events[3].Sequence == 4 && events[3].Kind == "revisit" && events[3].FoodTick == 40 && events[3].BrainTick == 400 && events[3].Available,
                "event #4 preserves unchanged revisit status and tick provenance");

            var berryBelief = historyVm.GetRememberedPlace("berry-food");
            Check(berryBelief.HasValue, "remembered belief exists for observed berry");
            Check(berryBelief.Value.LastSeenKind == "revisit" && berryBelief.Value.LastSeenFoodTick == 40 && berryBelief.Value.WasAvailableWhenSeen,
                "latest belief reflects last observation from tick 40");
            Check(berryBelief.Value.StatusText.Contains("last seen available") && berryBelief.Value.StatusText.Contains("t=40"),
                "belief text explicitly distinguishes remembered belief from live availability");

            // 4. Round-trip existing FoodModel save/restore visible history unchanged
            PlaceLedger.Occupy(hs, 3, 4, 10);
            PlaceLedger.Occupy(hs, 3, 5, 20);
            // Refresh expected view model with current authoritative state after Occupy before save comparison
            historyVm.Update(hs, berryPos, forceRevalidate: true);
            string savePath = Path.Combine(folder, "map-roundtrip-save.json");
            historyModel.Save(savePath);

            var restoredModel = new FoodModel(world, gen, 4242);
            Check(restoredModel.Load(savePath, world, gen), "saved place memory reloads successfully");

            var restoredVm = new StarfallMapViewModel(world, gen, actor);
            Check(restoredVm.Update(restoredModel.State, berryPos), "restored model updates view model");
            Check(restoredVm.ExploredCellCount == historyVm.ExploredCellCount && restoredVm.ExploredCellCount == 2,
                "reloaded explored cell count matches post-occupy history");
            Check(restoredVm.ObservedPlaceCount == historyVm.ObservedPlaceCount && restoredVm.ObservedPlaceCount == 1,
                "reloaded observed place count unchanged");
            Check(restoredVm.EventCount == historyVm.EventCount && restoredVm.EventCount == 4,
                "reloaded observation event count matches all 4 events");

            var restoredEvents = restoredVm.GetObservationEvents();
            for (int i = 0; i < events.Length; i++)
            {
                Check(restoredEvents[i].Sequence == events[i].Sequence &&
                      restoredEvents[i].Kind == events[i].Kind &&
                      restoredEvents[i].FoodTick == events[i].FoodTick &&
                      restoredEvents[i].Hash == events[i].Hash,
                      $"reloaded event #{i+1} matches original bit-for-bit");
            }
            var restoredBelief = restoredVm.GetRememberedPlace("berry-food");
            Check(restoredBelief.HasValue && restoredBelief.Value.ProvenanceHash == berryBelief.Value.ProvenanceHash,
                "reloaded place belief matches original belief and provenance hash");

            // 5. Mutated returned view cannot alter authoritative state
            var cellsCopy = historyVm.GetExploredCells();
            int originalVisits = hs.exploredCells[0].visits;
            string originalHash = hs.observedPlaces[0].hash;
            // Attempt local modifications on array
            if (cellsCopy.Length > 0)
            {
                cellsCopy[0] = new ExploredCellEntry(999, 999, 0, 0, 9999);
            }
            Check(hs.exploredCells[0].visits == originalVisits && hs.exploredCells[0].x != 999,
                "mutating returned cell array cannot alter authoritative FoodState.exploredCells");

            var eventsCopy = historyVm.GetObservationEvents();
            if (eventsCopy.Length > 0)
            {
                eventsCopy[0] = new ObservationEventEntry(99, "fake", "fake", "fake", Vector3.zero, 0, 0, false, false, "fake");
            }
            Check(hs.observedPlaces[0].hash == originalHash && hs.observedPlaces[0].id == "berry-food",
                "mutating returned event array cannot alter authoritative FoodState.observedPlaces");

            // 6. Invalid or nonfinite coordinates handled safely
            Check(historyVm.Update(hs, new Vector3(float.NaN, 0, 0)) && !historyVm.HasValidActorPosition,
                "NaN actor coordinate handled safely without exception; marks position invalid");
            Check(historyVm.Update(hs, new Vector3(0, float.PositiveInfinity, 0)) && !historyVm.HasValidActorPosition,
                "Infinity actor coordinate handled safely without exception");
            Check(historyVm.Update(hs, new Vector3(25000f, 0, 0)) && !historyVm.HasValidActorPosition,
                "out-of-range actor coordinate handled safely without exception");

            string safeGrid = historyVm.GenerateAsciiGrid(3);
            Check(!string.IsNullOrEmpty(safeGrid) && !safeGrid.Contains("@"),
                "grid generation with invalid actor position falls back safely without crash");

            Check(!historyVm.IsCellExplored(int.MinValue, int.MaxValue),
                "extreme out-of-bounds cell lookup returns false without overflow");

            // 7. Map access doesn't reveal registry objects or mutate ledger
            int beforeCellCount = hs.exploredCells.Count;
            int beforeEventCount = hs.observedPlaces.Count;
            for (int i = 0; i < 50; i++)
            {
                historyVm.Update(hs, berryPos);
                historyVm.GenerateAsciiGrid(4);
                historyVm.GetExploredCells();
                historyVm.GetObservationEvents();
                historyVm.GetRememberedBeliefs();
                historyVm.IsCellExplored(i, i);
            }
            Check(hs.exploredCells.Count == beforeCellCount && hs.observedPlaces.Count == beforeEventCount,
                "repeated map view queries never mutate ledger counts");
            Check(PlaceLedger.Valid(hs), "ledger integrity and hash chain remains 100% valid after queries");
            Check(historyVm.GetRememberedPlace("unregistered-secret-object") == null,
                "unregistered or unobserved objects are never revealed");

            // 8. Revision tracking and display caching cadence
            int revBefore = historyVm.Revision;
            historyVm.Update(hs, berryPos);
            Check(historyVm.Revision == revBefore && !historyVm.DisplayChanged && !historyVm.MemoryChanged,
                "identical state and actor cell updates reuse cached display without revision bump");

            // Position shift within same 3m cell (e.g. 0.2m)
            historyVm.Update(hs, berryPos + new Vector3(0.2f, 0f, 0.2f));
            Check(historyVm.Revision == revBefore && !historyVm.DisplayChanged && !historyVm.MemoryChanged,
                "movement within same 3m cell preserves display cache without redrawing");

            // Movement into new 3m cell (e.g. +3.5m in X)
            historyVm.Update(hs, berryPos + new Vector3(3.5f, 0f, 0f));
            Check(historyVm.DisplayChanged && !historyVm.MemoryChanged && historyVm.Revision == revBefore,
                "stepping into new cell refreshes display without re-ingesting unchanged memory");

            // Mutating place memory advances revision and refreshes display
            hs.tick = 50;
            PlaceLedger.Occupy(hs, 10, 10, 50);
            historyVm.Update(hs, berryPos);
            Check(historyVm.MemoryChanged && historyVm.DisplayChanged && historyVm.Revision > revBefore,
                "authoritative place ledger revision advances memory revision and triggers validation");

            // 9. Dependency set validation fails closed on mismatched actor/world
            Check(!StarfallMapHud.ValidateDependencySet(null, null, null, out string errNull),
                "null dependencies fail set validation");
            Check(errNull.Contains("missing") || errNull.Contains("Missing"),
                "null dependencies report clear missing reason");

            var go = new GameObject("dep-test");
            try
            {
                var testBrain = go.AddComponent<NpcAutonomy>();
                testBrain.InstanceWorldId = "world-a";
                var testFood = go.AddComponent<IntegratedFoodRuntime>();
                Check(!StarfallMapHud.ValidateDependencySet(testBrain, testFood, null, out string errModelNull),
                    "unattached food runtime fails dependency set validation");
                Check(errModelNull.Contains("Food runtime or state missing"),
                    "unattached food runtime reports clear missing model error");

                testFood.Brain = testBrain;
                var ensureMethod = typeof(IntegratedFoodRuntime).GetMethod("EnsureModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                ensureMethod.Invoke(testFood, null);
                testFood.Model.State.world = "world-a";
                testFood.Model.State.generation = IntegratedFoodRuntime.Generation;
                testFood.Model.State.actorId = NpcAutonomy.AgentId;

                Check(StarfallMapHud.ValidateDependencySet(testBrain, testFood, null, out _),
                    "matching brain and food dependency set passes validation");

                // Foreign actor ID rejection
                testFood.Model.State.actorId = "foreign-actor";
                Check(!StarfallMapHud.ValidateDependencySet(testBrain, testFood, null, out string errActor),
                    "foreign actor fails dependency set validation");
                Check(errActor.Contains("Actor mismatch"), "reports clear actor mismatch reason");
                testFood.Model.State.actorId = NpcAutonomy.AgentId;

                // Brain reference mismatch
                testFood.Brain = null;
                Check(!StarfallMapHud.ValidateDependencySet(testBrain, testFood, null, out string errBrainRef),
                    "mismatched food.Brain fails dependency set validation");
                Check(errBrainRef.Contains("Brain reference"), "reports clear brain reference mismatch reason");
                testFood.Brain = testBrain;

                // World mismatch
                testBrain.InstanceWorldId = "world-mismatch";
                Check(!StarfallMapHud.ValidateDependencySet(testBrain, testFood, null, out string errWorld),
                    "mismatched world fails dependency set validation");
                Check(errWorld.Contains("World mismatch"), "reports clear world mismatch reason");
                testBrain.InstanceWorldId = "world-a";

                // Generation mismatch
                testFood.Model.State.generation = "foreign-gen";
                Check(!StarfallMapHud.ValidateDependencySet(testBrain, testFood, null, out string errGen),
                    "mismatched generation fails dependency set validation");
                Check(errGen.Contains("Generation mismatch"), "reports clear generation mismatch reason");
                testFood.Model.State.generation = IntegratedFoodRuntime.Generation;

                // Survival autonomy binding validation
                var testSurvival = go.AddComponent<StarfallSurvivalAutonomy>();
                testSurvival.Brain = null;
                testSurvival.Food = testFood;
                Check(!StarfallMapHud.ValidateDependencySet(testBrain, testFood, testSurvival, out string errSurvBrain),
                    "unmatched survival.Brain fails dependency set validation");
                testSurvival.Brain = testBrain;
                testSurvival.Food = null;
                Check(!StarfallMapHud.ValidateDependencySet(testBrain, testFood, testSurvival, out string errSurvFood),
                    "unmatched survival.Food fails dependency set validation");
                testSurvival.Food = testFood;
                Check(StarfallMapHud.ValidateDependencySet(testBrain, testFood, testSurvival, out _),
                    "matching survival autonomy passes dependency set validation");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            // 10. Populated view model invalidates projection and triggers display clear on null, foreign scope, or corrupted state
            {
                var popModel = new FoodModel(world, gen, 4242);
                var ps = popModel.State;
                ps.tick = 15;
                PlaceLedger.Occupy(ps, 5, 8, 10);
                PlaceLedger.Occupy(ps, 6, 8, 15);
                PlaceLedger.Observe(ps, "berry-food", "Place", "fruiting-succulent", new Vector3(15, 2, 24), true, true, 15, 150, false);
                PlaceLedger.Observe(ps, "spring-food", "Place", "freshwater-seep", new Vector3(30, 2, 45), true, true, 15, 150, false);

                var popVm = new StarfallMapViewModel(world, gen, actor);
                bool initialOk = popVm.Update(ps, new Vector3(15, 2, 24));
                Check(initialOk && popVm.IsValid, "populated view model updates initially valid");
                Check(popVm.ExploredCellCount == 2 && popVm.ObservedPlaceCount == 2 && popVm.EventCount == 2,
                    "populated view model contains cells, places, and events");
                Check(!string.IsNullOrEmpty(popVm.CachedAsciiGrid) && !string.IsNullOrEmpty(popVm.CachedBeliefsText),
                    "populated view model has generated cached display strings");

                // Case A: Feeding null state to previously populated view model
                bool nullResult = popVm.Update(null, new Vector3(15, 2, 24));
                Check(!nullResult, "updating populated view model with null state returns false");
                Check(!popVm.IsValid, "view model is marked invalid after null state");
                Check(popVm.ExploredCellCount == 0, "explored cells projection cleared to 0 after null state");
                Check(popVm.ObservedPlaceCount == 0, "observed places projection cleared to 0 after null state");
                Check(popVm.EventCount == 0, "observation events cleared to 0 after null state");
                Check(popVm.GetRememberedPlace("berry-food") == null, "remembered place lookup returns null after invalidation");
                Check(popVm.DisplayChanged, "display invalidation signaled (DisplayChanged==true) on transition to invalid");
                Check(popVm.MemoryChanged, "memory invalidation signaled (MemoryChanged==true) on transition to invalid");
                Check(string.IsNullOrEmpty(popVm.CachedAsciiGrid), "cached ascii grid string cleared on invalidation");
                Check(string.IsNullOrEmpty(popVm.CachedBeliefsText), "cached beliefs text cleared on invalidation");

                // Second call with null: display already cleared, should not spuriously set DisplayChanged again
                popVm.Update(null, new Vector3(15, 2, 24));
                Check(!popVm.DisplayChanged && !popVm.MemoryChanged,
                    "repeated invalid updates do not repeatedly signal DisplayChanged");

                // Re-populate view model
                popVm.Update(ps, new Vector3(15, 2, 24));
                Check(popVm.IsValid && popVm.ExploredCellCount == 2, "view model successfully re-populated");

                // Case B: Foreign actor ID
                var foreignActorState = new FoodModel(world, gen, 4242).State;
                foreignActorState.tick = 15;
                PlaceLedger.Occupy(foreignActorState, 5, 8, 10);
                foreignActorState.actorId = "foreign-actor-id";
                bool foreignActorResult = popVm.Update(foreignActorState, new Vector3(15, 2, 24));
                Check(!foreignActorResult && !popVm.IsValid, "foreign actor state returns false and marks invalid");
                Check(popVm.ExploredCellCount == 0 && popVm.ObservedPlaceCount == 0 && popVm.EventCount == 0,
                    "foreign actor clears all projected knowledge");
                Check(popVm.DisplayChanged && popVm.MemoryChanged,
                    "display clear signaled on foreign actor rejection");

                // Re-populate view model
                popVm.Update(ps, new Vector3(15, 2, 24));
                Check(popVm.IsValid && popVm.ExploredCellCount == 2, "view model successfully re-populated before world mismatch test");

                // Case C: Foreign world
                var foreignWorldState = new FoodModel("foreign-world-id", gen, 4242).State;
                foreignWorldState.tick = 15;
                PlaceLedger.Occupy(foreignWorldState, 5, 8, 10);
                foreignWorldState.actorId = actor;
                bool foreignWorldResult = popVm.Update(foreignWorldState, new Vector3(15, 2, 24));
                Check(!foreignWorldResult && !popVm.IsValid, "foreign world state returns false and marks invalid");
                Check(popVm.ExploredCellCount == 0 && popVm.ObservedPlaceCount == 0 && popVm.EventCount == 0,
                    "foreign world clears all projected knowledge");
                Check(popVm.DisplayChanged, "display clear signaled on foreign world rejection");

                // Re-populate view model
                popVm.Update(ps, new Vector3(15, 2, 24));
                Check(popVm.IsValid && popVm.ExploredCellCount == 2, "view model successfully re-populated before generation mismatch test");

                // Case D: Foreign generation
                var foreignGenState = new FoodModel(world, "foreign-generation-id", 4242).State;
                foreignGenState.tick = 15;
                PlaceLedger.Occupy(foreignGenState, 5, 8, 10);
                foreignGenState.actorId = actor;
                bool foreignGenResult = popVm.Update(foreignGenState, new Vector3(15, 2, 24));
                Check(!foreignGenResult && !popVm.IsValid, "foreign generation state returns false and marks invalid");
                Check(popVm.ExploredCellCount == 0 && popVm.ObservedPlaceCount == 0 && popVm.EventCount == 0,
                    "foreign generation clears all projected knowledge");
                Check(popVm.DisplayChanged, "display clear signaled on foreign generation rejection");

                // Re-populate view model
                popVm.Update(ps, new Vector3(15, 2, 24));
                Check(popVm.IsValid && popVm.ExploredCellCount == 2, "view model successfully re-populated before corrupted state test");

                // Case E: Corrupted/invalid state (broken hash chain)
                var corruptState = new FoodModel(world, gen, 4242).State;
                corruptState.tick = 15;
                corruptState.actorId = actor;
                PlaceLedger.Observe(corruptState, "berry-food", "Place", "fruiting-succulent", new Vector3(15, 2, 24), true, true, 15, 150, false);
                corruptState.observedPlaces[0].hash = "broken-hash";
                bool corruptResult = popVm.Update(corruptState, new Vector3(15, 2, 24));
                Check(!corruptResult && !popVm.IsValid, "corrupted state fails validation, returns false, and marks invalid");
                Check(popVm.ExploredCellCount == 0 && popVm.ObservedPlaceCount == 0 && popVm.EventCount == 0,
                    "corrupted state clears all projected knowledge");
                Check(popVm.DisplayChanged, "display clear signaled on corrupted state rejection");
            }

            // 11. HUD renders refusal and clears stale grid/beliefs on update failure and on dependency mismatch (component UI state check)
            {
                var hudGo = new GameObject("hud-invalidation-test");
                try
                {
                    var hud = hudGo.AddComponent<StarfallMapHud>();
                    var hudModel = new FoodModel(world, gen, 4242);
                    hudModel.State.tick = 10;
                    PlaceLedger.Occupy(hudModel.State, 3, 3, 10);
                    PlaceLedger.Observe(hudModel.State, "berry-food", "Place", "fruiting-succulent", new Vector3(9, 1, 9), true, true, 10, 100, false);

                    hud.Initialize(hudModel, world, gen, actor, () => new Vector3(9, 1, 9));
                    hud.ViewModel.Update(hudModel.State, new Vector3(9, 1, 9));
                    hud.RefreshUi();

                    Check(hud.ViewModel.IsValid, "HUD view model valid after component initialization");
                    Check(!string.IsNullOrEmpty(hud.GridLabel.text), "HUD displays non-empty ASCII grid when valid (component UI state check)");
                    Check(hud.BeliefsLabel.text.Contains("berry-food"), "HUD displays remembered berry belief when valid");
                    Check(hud.ProvenanceLabel.text == "Place Memory: Synchronized", "HUD provenance displays synchronized when valid");

                    // Invalidate view model (e.g. simulate scope failure or null state)
                    bool updateFailed = hud.ViewModel.Update(null, new Vector3(9, 1, 9));
                    Check(!updateFailed, "ViewModel update with null returns false");
                    Check(hud.ViewModel.DisplayChanged, "ViewModel signaled DisplayChanged on transition to invalid");

                    // Refresh UI following invalidation
                    hud.RefreshUi();

                    Check(string.IsNullOrEmpty(hud.GridLabel.text), "HUD grid label cleared to empty on invalidation");
                    Check(string.IsNullOrEmpty(hud.EventsLabel.text), "HUD events label cleared to empty on invalidation");
                    Check(hud.BeliefsLabel.text.Contains("failed validation or scope mismatch"),
                        "HUD beliefs label displays refusal notice without stale beliefs");
                    Check(!hud.BeliefsLabel.text.Contains("berry-food"),
                        "HUD beliefs label no longer contains stale berry belief");
                    Check(hud.StatusLabel.text.Contains("Map unavailable"),
                        "HUD status label displays map unavailable notice");
                    Check(hud.ProvenanceLabel.text == "Place Memory: Unavailable",
                        "HUD provenance displays unavailable notice");

                    // Test dependency mismatch handling directly
                    Check(!StarfallMapHud.ValidateDependencySet(null, null, null, out string depErr),
                        "ValidateDependencySet fails on null dependencies");
                    hud.RenderDependencyMismatch(depErr);

                    Check(string.IsNullOrEmpty(hud.GridLabel.text), "HUD grid label cleared on dependency mismatch");
                    Check(string.IsNullOrEmpty(hud.EventsLabel.text), "HUD events label cleared on dependency mismatch");
                    Check(hud.BeliefsLabel.text.Contains("Map fails closed"),
                        "HUD beliefs label renders fail-closed notice on dependency mismatch");
                    Check(hud.StatusLabel.text.Contains("Dependency set mismatch"),
                        "HUD status label renders dependency mismatch notice");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(hudGo);
                }
            }

            // 12. Responsive HUD layout, safe areas, minimum 14px font size, and tabbed history (component UI state check)
            {
                var hudGo = new GameObject("hud-responsive-test");
                try
                {
                    var hud = hudGo.AddComponent<StarfallMapHud>();
                    var hudModel = new FoodModel(world, gen, 4242);
                    hud.Initialize(hudModel, world, gen, actor, () => Vector3.zero);
                    hud.RefreshUi();

                    // Minimum 14px body font size guarantees legibility without halving to 6px
                    Check(hud.GridLabel.fontSize >= 14, "HUD grid font size is at least 14px (component UI state check)");
                    Check(hud.BeliefsLabel.fontSize >= 14, "HUD beliefs font size is at least 14px (component UI state check)");
                    Check(hud.EventsLabel.fontSize >= 14, "HUD events font size is at least 14px (component UI state check)");
                    Check(hud.StatusLabel.fontSize >= 14, "HUD status font size is at least 14px (component UI state check)");
                    Check(hud.ProvenanceLabel.fontSize >= 14, "HUD provenance font size is at least 14px (component UI state check)");

                    // Safe bounds at 800x450: top safe offset clears ~280x80 top-right controls, bottom clears weather, left clears decision panel
                    StarfallMapHud.CalculateSafeBounds(800, 450, false, out var pos800C, out var size800C, out float top800, out float avail800);
                    Check(top800 >= 90f, "800x450 top safe offset clears existing top-right controls (~280x80)");
                    Check(top800 + size800C.y <= 450f - 140f, "800x450 compact summary bottom edge clears bottom-right weather");
                    Check(800f + pos800C.x - size800C.x >= 260f, "800x450 compact summary left edge clears top-left decision panel");

                    StarfallMapHud.CalculateSafeBounds(800, 450, true, out var pos800E, out var size800E, out _, out _);
                    Check(top800 + size800E.y <= 450f - 140f, "800x450 expanded map bottom edge clears bottom-right weather");
                    Check(800f + pos800E.x - size800E.x >= 260f, "800x450 expanded map left edge clears top-left decision panel");

                    // Safe bounds at 1280x720 and 1600x900
                    StarfallMapHud.CalculateSafeBounds(1280, 720, true, out var pos1280, out var size1280, out float top1280, out _);
                    Check(top1280 >= 170f, "1280x720 top safe offset clears top-right controls");
                    Check(top1280 + size1280.y <= 720f - 140f, "1280x720 expanded map clears bottom-right weather");

                    StarfallMapHud.CalculateSafeBounds(1600, 900, true, out var pos1600, out var size1600, out float top1600, out _);
                    Check(top1600 >= 170f, "1600x900 top safe offset clears top-right controls");
                    Check(top1600 + size1600.y <= 900f - 140f, "1600x900 expanded map clears bottom-right weather");

                    // Tab switching
                    hud.SetActiveTab(1);
                    Check(hud.ActiveTab == 1, "SetActiveTab switches to tab 1 (Beliefs)");
                    hud.SetActiveTab(2);
                    Check(hud.ActiveTab == 2, "SetActiveTab switches to tab 2 (History)");
                    hud.SetActiveTab(0);
                    Check(hud.ActiveTab == 0, "SetActiveTab switches to tab 0 (Grid)");

                    // Compact/expanded toggle
                    hud.Expanded = true;
                    Check(hud.Expanded, "HUD expanded mode toggled on");
                    hud.Expanded = false;
                    Check(!hud.Expanded, "HUD expanded mode toggled off to compact summary");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(hudGo);
                }
            }

            // 13. AAA Tactical Visual Map Generator, Dynamic Fog-of-War, GPS Regions, and Terrain Navigation
            {
                // World to Map UV bounds and coordinate mapping
                Vector2 minUv = StarfallVisualMapGenerator.WorldToMapUV(new Vector3(-250f, 0, -250f));
                Check(Mathf.Approximately(minUv.x, 0f) && Mathf.Approximately(minUv.y, 0f), "WorldToMapUV maps southwest world bounds to (0, 0)");

                Vector2 maxUv = StarfallVisualMapGenerator.WorldToMapUV(new Vector3(250f, 0, 250f));
                Check(Mathf.Approximately(maxUv.x, 1f) && Mathf.Approximately(maxUv.y, 1f), "WorldToMapUV maps northeast world bounds to (1, 1)");

                Vector2 centerUv = StarfallVisualMapGenerator.WorldToMapUV(Vector3.zero);
                Check(Mathf.Approximately(centerUv.x, 0.5f) && Mathf.Approximately(centerUv.y, 0.5f), "WorldToMapUV maps origin to map center (0.5, 0.5)");

                Vector3 roundTripOrigin = StarfallVisualMapGenerator.MapUVToWorld(new Vector2(0.5f, 0.5f));
                Check(Mathf.Approximately(roundTripOrigin.x, 0f) && Mathf.Approximately(roundTripOrigin.z, 0f), "MapUVToWorld recovers world (0, 0) from center UV");

                // Topographical map texture generation
                var topoTex = StarfallVisualMapGenerator.GenerateTopographicalTexture();
                try
                {
                    Check(topoTex != null, "GenerateTopographicalTexture produces non-null relief texture");
                    Check(topoTex.width == StarfallVisualMapGenerator.TextureWidth && topoTex.height == StarfallVisualMapGenerator.TextureHeight,
                        "relief texture dimensions match 384x384 high-detail map specification");
                    Color32 samplePx = topoTex.GetPixel(StarfallVisualMapGenerator.TextureWidth / 2, StarfallVisualMapGenerator.TextureHeight / 2);
                    Check(samplePx.a > 0, "relief texture contains opaque rendered pixels");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(topoTex);
                }

                // Dynamic Fog of War mask creation and cell reveal
                var fogTex = StarfallVisualMapGenerator.CreateFogOfWarTexture();
                try
                {
                    Check(fogTex != null, "CreateFogOfWarTexture produces non-null fog mask");
                    Check(fogTex.width == StarfallVisualMapGenerator.TextureWidth && fogTex.height == StarfallVisualMapGenerator.TextureHeight,
                        "fog mask matches 384x384 map resolution");
                    var pixels = fogTex.GetPixels32();
                    Check(pixels[0].a == 248, "unexplored fog mask initialized to 248 dark atmospheric mist");

                    // Reveal world cell (0, 0)
                    StarfallVisualMapGenerator.RevealCell(pixels, 0, 0, 22f);
                    int centerIdx = (StarfallVisualMapGenerator.TextureHeight / 2) * StarfallVisualMapGenerator.TextureWidth + (StarfallVisualMapGenerator.TextureWidth / 2);
                    Check(pixels[centerIdx].a == 0, "RevealCell burns away fog of war at cell center to alpha 0");
                    Check(pixels[0].a == 248, "RevealCell leaves distant unexplored corners shrouded in dark mist");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(fogTex);
                }

                // GPS Regional naming
                Check(StarfallMapHud.GetRegionName(new Vector3(-150f, 10f, 100f)) == "Refuge Cavern & West Ridge",
                    "GPS region identifies Refuge Cavern & West Ridge");
                Check(StarfallMapHud.GetRegionName(new Vector3(120f, 2f, -60f)) == "Freshwater Spring Oasis",
                    "GPS region identifies Freshwater Spring Oasis");
                Check(StarfallMapHud.GetRegionName(new Vector3(0f, -2f, 0f)) == "Whispering River Shallows",
                    "GPS region identifies Whispering River Shallows");
                Check(StarfallMapHud.GetRegionName(new Vector3(0f, 20f, 150f)) == "North River Meander & Cliffs",
                    "GPS region identifies North River Meander & Cliffs");
                Check(StarfallMapHud.GetRegionName(new Vector3(0f, 10f, -150f)) == "South Canyon Basin",
                    "GPS region identifies South Canyon Basin");
                Check(StarfallMapHud.GetRegionName(new Vector3(60f, 10f, 100f)) == "Sunlit Canyon Terrace",
                    "GPS region identifies Sunlit Canyon Terrace fallback");

                // Navigation surface properties
                var navGo = new GameObject("nav-unit-test");
                try
                {
                    var nav = navGo.AddComponent<NpcTerrainNavigation>();
                    Check(nav.PhysicalBounds.size.x > 0f, "NpcTerrainNavigation reports valid physical bounds");
                    Check(nav.WaterLevel(Vector3.zero) == CoastalWater.Level, "NpcTerrainNavigation returns CoastalWater level");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(navGo);
                }
            }

            return passed;
        }
    }
}
