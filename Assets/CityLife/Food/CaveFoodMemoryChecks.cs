using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Starfall.Food
{
    /// <summary>
    /// Authored test suite for CaveFoodMemory projection.
    /// Follows FoodChecks style (List<string> Run(string folder) with local Check assertions).
    /// Tests are authored for coordinator execution; not executed in source pass.
    /// </summary>
    public static class CaveFoodMemoryChecks
    {
        public static List<string> Run(string folder)
        {
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            var passed = new List<string>();
            void Check(bool yes, string name)
            {
                if (!yes) throw new Exception("CAVE FOOD MEMORY CHECK FAILED: " + name);
                passed.Add(name);
            }

            const string world = "cave-test-world";
            const string gen = "cave-test-gen";
            const string actor = "inhabitant-1";

            // -------------------------------------------------------------
            // Section 1: No history / legacy behavior
            // -------------------------------------------------------------
            // 1A: Fresh state (empty history)
            var fresh = new FoodModel(world, gen, 4242);
            Check(CaveFoodMemory.TryProject(fresh.State, world, gen, actor, out var freshBeliefs, out var freshReason) &&
                freshBeliefs.Count == 0 && freshReason == null,
                "fresh state with zero observations yields empty beliefs without error");
            Check(CaveFoodMemory.Project(fresh.State, world, gen, actor).Count == 0,
                "fresh state Project returns empty list");
            Check(!CaveFoodMemory.TryGetLatestBelief(fresh.State, world, gen, actor, "berry-food", out _, out _),
                "unobserved food yields unknown, not discovered food");
            Check(PlaceLedger.Valid(fresh.State),
                "fresh state with empty history validates under PlaceLedger");

            // 1B: Genuine omitted-field legacy JSON fixture (pre-place-ledger schema)
            // Strips both newly added fields to verify genuine schema omission.
            string freshJson = fresh.Json();
            string legacyJson = Regex.Replace(freshJson, @"\""exploredCells\"":\[\],?", "");
            legacyJson = Regex.Replace(legacyJson, @"\""observedPlaces\"":\[\],?", "");
            legacyJson = Regex.Replace(legacyJson, @",\s*\""exploredCells\"":\[\]", "");
            legacyJson = Regex.Replace(legacyJson, @",\s*\""observedPlaces\"":\[\]", "");
            legacyJson = Regex.Replace(legacyJson, @",\s*}", "}");
            legacyJson = Regex.Replace(legacyJson, @",\s*]", "]");

            Check(!legacyJson.Contains("\"exploredCells\"") && !legacyJson.Contains("\"observedPlaces\""),
                "legacy JSON genuinely omits both exploredCells and observedPlaces fields");
            Check(!string.IsNullOrEmpty(legacyJson) && legacyJson.Contains("\"schema\""),
                "legacy JSON payload is non-empty and contains valid schema");

            // Inspectable parser representation diagnostic:
            // JsonUtility may deserialize omitted fields as null or retain default empty collections.
            // Rather than assuming parser representation without evidence, inspect and report exact field state.
            var legacyState = JsonUtility.FromJson<FoodState>(legacyJson);
            Check(legacyState != null, "legacy JSON successfully parsed by JsonUtility into FoodState");

            string exploredStatus = legacyState.exploredCells == null ? "null" : $"non-null(count={legacyState.exploredCells.Count})";
            string observedStatus = legacyState.observedPlaces == null ? "null" : $"non-null(count={legacyState.observedPlaces.Count})";
            bool exploredAcceptable = legacyState.exploredCells == null || legacyState.exploredCells.Count == 0;
            bool observedAcceptable = legacyState.observedPlaces == null || legacyState.observedPlaces.Count == 0;

            Check(exploredAcceptable && observedAcceptable,
                $"omitted legacy fields deserialize as null or empty lists without invented data (exploredCells={exploredStatus}, observedPlaces={observedStatus})");

            bool legacyLedgerValid = PlaceLedger.Valid(legacyState);
            Check(legacyLedgerValid,
                $"legacy state deserialized from omitted-field JSON validates under PlaceLedger (exploredCells={exploredStatus}, observedPlaces={observedStatus})");

            Check(CaveFoodMemory.TryProject(legacyState, world, gen, actor, out var legacyBeliefs, out var legacyReason) &&
                legacyBeliefs.Count == 0 && legacyReason == null,
                $"omitted-field legacy history yields empty beliefs without error (exploredCells={exploredStatus}, observedPlaces={observedStatus})");
            Check(CaveFoodMemory.Project(legacyState, world, gen, actor).Count == 0,
                "omitted-field legacy Project returns empty list without error");
            Check(!CaveFoodMemory.TryGetLatestBelief(legacyState, world, gen, actor, "berry-food", out _, out _),
                "unobserved food yields unknown under omitted-field legacy state");

            // 1C: Explicit-null history case (preserves acceptance of explicit null/null legacy memory)
            var explicitNullState = JsonUtility.FromJson<FoodState>(freshJson);
            explicitNullState.exploredCells = null;
            explicitNullState.observedPlaces = null;
            Check(explicitNullState.exploredCells == null && explicitNullState.observedPlaces == null,
                "explicit-null state has null exploredCells and null observedPlaces lists");
            Check(PlaceLedger.Valid(explicitNullState),
                "explicit-null legacy state validates under PlaceLedger");
            Check(CaveFoodMemory.TryProject(explicitNullState, world, gen, actor, out var explicitNullBeliefs, out var explicitNullReason) &&
                explicitNullBeliefs.Count == 0 && explicitNullReason == null,
                "explicit-null legacy history yields unknown, not discovered food without inventing coordinates");
            Check(CaveFoodMemory.Project(explicitNullState, world, gen, actor).Count == 0,
                "explicit-null legacy Project returns empty list without error");
            Check(!CaveFoodMemory.TryGetLatestBelief(explicitNullState, world, gen, actor, "berry-food", out _, out _),
                "unobserved food yields unknown under explicit-null legacy state");

            // 1D: Validation of genuine omitted-field JSON through existing FoodModel.Restore
            var restoreModel = new FoodModel(world, gen, 4242);
            bool restored = restoreModel.Restore(legacyJson, world, gen);
            Check(restored, "FoodModel.Restore accepts genuine omitted-field legacy JSON");
            Check(restoreModel.State.world == world && restoreModel.State.generation == gen && restoreModel.State.actorId == actor,
                "restored legacy model preserves world, generation, and actor scope");
            Check(restoreModel.State.exploredCells != null && restoreModel.State.exploredCells.Count == 0 &&
                restoreModel.State.observedPlaces != null && restoreModel.State.observedPlaces.Count == 0,
                "restored legacy model initializes clean empty exploredCells and observedPlaces collections");
            Check(CaveFoodMemory.Project(restoreModel.State, world, gen, actor).Count == 0,
                "restored legacy model projects empty beliefs without invented knowledge");
            Check(!CaveFoodMemory.TryGetLatestBelief(restoreModel.State, world, gen, actor, "berry-food", out var restoredBelief, out _),
                "restored legacy model yields unknown food for unobserved berry-food");
            Check(!restoredBelief.ReadyToGather,
                "restored legacy food belief is never ready to gather after reload");
            Check(restoredBelief.RequiresRuntimeRevalidation,
                "restored legacy food belief requires runtime revalidation");
            Check(!restoredBelief.IsCandidateForRoute,
                "restored legacy food belief is not candidate for route");

            // Restore fails closed on scope mismatch with omitted-field JSON
            var scopeMismatchModel = new FoodModel(world, gen, 4242);
            Check(!scopeMismatchModel.Restore(legacyJson, "wrong-world", gen),
                "FoodModel.Restore rejects omitted-field legacy JSON on mismatched world scope");
            Check(!scopeMismatchModel.Restore(legacyJson, world, "wrong-gen"),
                "FoodModel.Restore rejects omitted-field legacy JSON on mismatched generation scope");

            // -------------------------------------------------------------
            // Section 2: World / Generation / Actor mismatch fails closed
            // -------------------------------------------------------------
            var scopeModel = new FoodModel(world, gen, 4242);
            scopeModel.State.tick = 1;
            PlaceLedger.Observe(scopeModel.State, "berry-food", "Place", "fruiting-succulent",
                new Vector3(5, 1, 10), true, true, 1, 10, false);

            // World mismatch
            Check(!CaveFoodMemory.TryProject(scopeModel.State, "wrong-world", gen, actor, out _, out var worldErr) &&
                worldErr.Contains("Scope mismatch"),
                "mismatched world fails closed via TryProject");
            bool worldThrown = false;
            try { CaveFoodMemory.Project(scopeModel.State, "wrong-world", gen, actor); }
            catch (ArgumentException) { worldThrown = true; }
            Check(worldThrown, "mismatched world throws ArgumentException via Project");

            // Generation mismatch
            Check(!CaveFoodMemory.TryProject(scopeModel.State, world, "wrong-gen", actor, out _, out var genErr) &&
                genErr.Contains("Scope mismatch"),
                "mismatched generation fails closed via TryProject");
            bool genThrown = false;
            try { CaveFoodMemory.Project(scopeModel.State, world, "wrong-gen", actor); }
            catch (ArgumentException) { genThrown = true; }
            Check(genThrown, "mismatched generation throws ArgumentException via Project");

            // Actor mismatch
            Check(!CaveFoodMemory.TryProject(scopeModel.State, world, gen, "wrong-actor", out _, out var actorErr) &&
                actorErr.Contains("Scope mismatch"),
                "mismatched actor fails closed via TryProject");
            bool actorThrown = false;
            try { CaveFoodMemory.Project(scopeModel.State, world, gen, "wrong-actor"); }
            catch (ArgumentException) { actorThrown = true; }
            Check(actorThrown, "mismatched actor throws ArgumentException via Project");

            // Invalid identifier format
            Check(!CaveFoodMemory.TryProject(scopeModel.State, "invalid world!", gen, actor, out _, out var idErr) &&
                idErr.Contains("Invalid scope"),
                "invalid world identifier syntax rejected");
            bool idThrown = false;
            try { CaveFoodMemory.Project(scopeModel.State, world, "invalid gen!", actor); }
            catch (ArgumentException) { idThrown = true; }
            Check(idThrown, "invalid generation syntax throws ArgumentException");

            // Null state
            Check(!CaveFoodMemory.TryProject(null, world, gen, actor, out _, out var nullErr) &&
                nullErr.Contains("null"),
                "null FoodState fails closed");
            bool nullThrown = false;
            try { CaveFoodMemory.Project(null, world, gen, actor); }
            catch (ArgumentException) { nullThrown = true; }
            Check(nullThrown, "null FoodState throws ArgumentException via Project");

            // -------------------------------------------------------------
            // Section 3: Tampered ledger fails closed
            // -------------------------------------------------------------
            var tamperModel = new FoodModel(world, gen, 4242);
            tamperModel.State.tick = 1;
            PlaceLedger.Observe(tamperModel.State, "berry-food", "Place", "fruiting-succulent",
                new Vector3(5, 1, 10), true, true, 1, 10, false);
            tamperModel.State.tick = 2;
            PlaceLedger.Observe(tamperModel.State, "berry-food", "Place", "fruiting-succulent",
                new Vector3(5, 1, 10), false, true, 2, 20, true);
            Check(PlaceLedger.Valid(tamperModel.State), "baseline ledger is valid before tampering");

            // Tamper 1: Invalid hash
            var hashTampered = JsonUtility.FromJson<FoodState>(tamperModel.Json());
            hashTampered.observedPlaces[0].hash = "tampered-hash-value";
            Check(!PlaceLedger.Valid(hashTampered), "tampered event hash invalidates ledger");
            Check(!CaveFoodMemory.TryProject(hashTampered, world, gen, actor, out _, out var hashErr) &&
                hashErr.Contains("validation failed"),
                "tampered hash fails closed in TryProject");
            bool hashThrown = false;
            try { CaveFoodMemory.Project(hashTampered, world, gen, actor); }
            catch (ArgumentException) { hashThrown = true; }
            Check(hashThrown, "tampered hash throws ArgumentException in Project");

            // Tamper 2: Tampered payload without updating hash
            var contentTampered = JsonUtility.FromJson<FoodState>(tamperModel.Json());
            contentTampered.observedPlaces[0].available = !contentTampered.observedPlaces[0].available;
            Check(!PlaceLedger.Valid(contentTampered), "tampered availability without hash update invalidates ledger");
            Check(!CaveFoodMemory.TryProject(contentTampered, world, gen, actor, out _, out _),
                "tampered availability fails closed in TryProject");

            // Tamper 3: Sequence number tampering
            var seqTampered = JsonUtility.FromJson<FoodState>(tamperModel.Json());
            seqTampered.observedPlaces[1].sequence = 999;
            Check(!PlaceLedger.Valid(seqTampered), "tampered sequence invalidates ledger");
            Check(!CaveFoodMemory.TryProject(seqTampered, world, gen, actor, out _, out _),
                "tampered sequence fails closed in TryProject");

            // Tamper 4: Hash link break (tampered previousHash)
            var linkTampered = JsonUtility.FromJson<FoodState>(tamperModel.Json());
            linkTampered.observedPlaces[1].previousHash = "broken-chain";
            Check(!PlaceLedger.Valid(linkTampered), "tampered previousHash breaks chain");
            Check(!CaveFoodMemory.TryProject(linkTampered, world, gen, actor, out _, out _),
                "tampered previousHash fails closed in TryProject");

            // Tamper 5: Non-finite position coordinates
            var posTampered = JsonUtility.FromJson<FoodState>(tamperModel.Json());
            posTampered.observedPlaces[0].position = new Vector3(float.NaN, 0, 0);
            Check(!PlaceLedger.Valid(posTampered), "non-finite position coordinate invalidates ledger");
            Check(!CaveFoodMemory.TryProject(posTampered, world, gen, actor, out _, out _),
                "non-finite position coordinate fails closed in TryProject");

            // -------------------------------------------------------------
            // Section 4: Newest unavailable/permitted status superseding old available
            // -------------------------------------------------------------
            var obsModel = new FoodModel(world, gen, 4242);
            var os = obsModel.State;
            Vector3 berryPos = new Vector3(12, 3, 18);
            os.tick = 10;
            PlaceLedger.Observe(os, "berry-food", "Place", "fruiting-succulent", berryPos, true, true, 10, 100, false);
            os.tick = 15;
            PlaceLedger.Observe(os, "spring-food", "Place", "freshwater-seep", new Vector3(30, 1, 40), true, true, 15, 150, false);

            var initialBeliefs = CaveFoodMemory.Project(os, world, gen, actor);
            Check(initialBeliefs.Count == 1,
                "only fruiting-succulent is included; freshwater-seep is filtered out of cave food memory");
            var b0 = initialBeliefs[0];
            Check(b0.Id == "berry-food" && b0.WasAvailableWhenSeen && b0.WasPermittedWhenSeen &&
                b0.LastSeenFoodTick == 10 && b0.LastSeenBrainTick == 100 && b0.LastSeenKind == "first-seen" &&
                b0.IsCandidateForRoute,
                "initial observation records available and permitted fruiting succulent as route candidate");
            Check(!b0.ReadyToGather && b0.RequiresRuntimeRevalidation,
                "initial available observation is NEVER ready to gather from history alone and requires runtime revalidation");

            // Depletion event superseding availability
            os.tick = 25;
            PlaceLedger.Observe(os, "berry-food", "Place", "fruiting-succulent", berryPos, false, true, 25, 250, true);
            var depletedBeliefs = CaveFoodMemory.Project(os, world, gen, actor);
            Check(depletedBeliefs.Count == 1, "still exactly one belief returned for berry-food");
            var b1 = depletedBeliefs[0];
            Check(!b1.WasAvailableWhenSeen && b1.WasPermittedWhenSeen && b1.LastSeenFoodTick == 25 &&
                b1.LastSeenKind == "changed",
                "newest depleted observation supersedes older available status");
            Check(!b1.IsCandidateForRoute && !b1.ReadyToGather && b1.Reason.Contains("depleted/unavailable"),
                "depleted belief is not candidate for route, never ready to gather, and provides deprioritized reason");

            // Unpermitted event superseding permission
            os.tick = 35;
            PlaceLedger.Observe(os, "berry-food", "Place", "fruiting-succulent", berryPos, true, false, 35, 350, true);
            var unpermBeliefs = CaveFoodMemory.Project(os, world, gen, actor);
            var b2 = unpermBeliefs[0];
            Check(b2.WasAvailableWhenSeen && !b2.WasPermittedWhenSeen && b2.LastSeenFoodTick == 35 &&
                b2.LastSeenKind == "changed",
                "newest unpermitted observation supersedes previous permitted status");
            Check(!b2.IsCandidateForRoute && !b2.ReadyToGather && b2.Reason.Contains("not permitted"),
                "unpermitted belief is disqualified for route choice");

            // Multiple distinct fruiting succulents with deterministic ordering
            var multiModel = new FoodModel(world, gen, 4242);
            multiModel.State.tick = 5;
            PlaceLedger.Observe(multiModel.State, "berry-b", "Place", "fruiting-succulent",
                new Vector3(10, 0, 0), true, true, 5, 50, false);
            PlaceLedger.Observe(multiModel.State, "berry-a", "Place", "fruiting-succulent",
                new Vector3(20, 0, 0), false, true, 5, 51, false);
            var multiBeliefs = CaveFoodMemory.Project(multiModel.State, world, gen, actor);
            Check(multiBeliefs.Count == 2 && multiBeliefs[0].Id == "berry-a" && multiBeliefs[1].Id == "berry-b",
                "multiple fruiting succulents returned in deterministic alphabetical order by ID");
            Check(!multiBeliefs[0].WasAvailableWhenSeen && multiBeliefs[1].WasAvailableWhenSeen,
                "individual availability preserved across multiple distinct food places");

            // -------------------------------------------------------------
            // Section 5: Repeated projection and restart deserialization
            // -------------------------------------------------------------
            // Revisit restores available & permitted
            os.tick = 50;
            PlaceLedger.Observe(os, "berry-food", "Place", "fruiting-succulent", berryPos, true, true, 50, 500, true);
            var activeBeliefs = CaveFoodMemory.Project(os, world, gen, actor);
            Check(activeBeliefs[0].WasAvailableWhenSeen && activeBeliefs[0].WasPermittedWhenSeen &&
                activeBeliefs[0].LastSeenFoodTick == 50,
                "recovery revisit records latest available and permitted status");
            Check(!activeBeliefs[0].ReadyToGather && activeBeliefs[0].RequiresRuntimeRevalidation,
                "re-available belief is NEVER ready to gather from history alone");

            // Repeated projection is idempotent
            for (int i = 0; i < 5; i++)
            {
                var rep = CaveFoodMemory.Project(os, world, gen, actor);
                Check(rep.Count == 1 && rep[0].Id == "berry-food" && rep[0].LastSeenFoodTick == 50 &&
                    !rep[0].ReadyToGather,
                    "repeated projection is idempotent and preserves dated belief without readiness");
            }

            // Save and reload snapshot into a fresh model
            string savePath = Path.Combine(folder, "cave-food-memory-save.json");
            obsModel.Save(savePath);
            var restartModel = new FoodModel(world, gen, 4242);
            Check(restartModel.Load(savePath, world, gen), "model loads cleanly from persisted snapshot");
            var reloadedBeliefs = CaveFoodMemory.Project(restartModel.State, world, gen, actor);
            Check(reloadedBeliefs.Count == 1, "reloaded model projects exactly 1 fruiting succulent belief");
            var rb = reloadedBeliefs[0];
            Check(rb.Id == "berry-food" && rb.LastSeenFoodTick == 50 && rb.Position == berryPos &&
                rb.WasAvailableWhenSeen && rb.WasPermittedWhenSeen,
                "reloaded belief preserves exact dated tick, position, and availability");
            Check(!rb.ReadyToGather && rb.RequiresRuntimeRevalidation,
                "CRITICAL INVARIANT: reloaded available belief is NEVER ready to gather from history alone and requires runtime revalidation");
            Check(rb.Reason.Contains("Never ready to gather from history alone") &&
                rb.Reason.Contains("mandatory on arrival"),
                "route reason explicitly states revalidation requirement on arrival");

            // -------------------------------------------------------------
            // Section 6: No source mutation
            // -------------------------------------------------------------
            string jsonBefore = restartModel.Json();
            int eventsBefore = restartModel.State.observedPlaces.Count;
            int cellsBefore = restartModel.State.exploredCells.Count;
            int tickBefore = restartModel.State.tick;

            CaveFoodMemory.TryProject(restartModel.State, world, gen, actor, out _, out _);
            CaveFoodMemory.Project(restartModel.State, world, gen, actor);
            CaveFoodMemory.TryGetLatestBelief(restartModel.State, world, gen, actor, "berry-food", out _, out _);

            string jsonAfter = restartModel.Json();
            Check(jsonBefore == jsonAfter,
                "projection causes zero source mutation: FoodState JSON is bit-for-bit identical before and after projection");
            Check(restartModel.State.observedPlaces.Count == eventsBefore &&
                restartModel.State.exploredCells.Count == cellsBefore &&
                restartModel.State.tick == tickBefore,
                "event count, cell count, and tick remain strictly unmodified");

            return passed;
        }
    }
}
