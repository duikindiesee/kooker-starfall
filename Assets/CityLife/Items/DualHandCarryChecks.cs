using System;
using System.Collections.Generic;
using UnityEngine;
using CityLife.World;
using CityLife.Food;
using Starfall.Food;

namespace CityLife.Items
{
    /// <summary>
    /// Deterministic in-memory unit tests for dual-hand carry (primary right, off-hand left),
    /// third pickup refusal ('hands-full'), targeted and LIFO dropping, leather bag storage,
    /// and left-hand occupancy notification for cosmetic club stowing.
    /// Follows strict TDD verification with zero external dependencies.
    /// </summary>
    public static class DualHandCarryChecks
    {
        public static List<string> Run()
        {
            var passed = new List<string>();

            void Check(bool condition, string name)
            {
                if (!condition)
                    throw new InvalidOperationException("DUAL HAND CARRY CHECK FAILED: " + name);
                passed.Add(name);
            }

            const string worldId = "test-coastal-world";
            const string genId = "gen-01";
            const string agentId = "actor-hunter-01";

            // -------------------------------------------------------------
            // 1. Catalog Definitions Verification
            // -------------------------------------------------------------
            {
                var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
                Check(catalog.TryGet("stone-river-pebble", out var pebbleDef), "catalog-contains-river-pebble");
                Check(Mathf.Approximately(pebbleDef.massKg, 0.65f), "pebble-mass-matches-0.65kg");
                Check(!pebbleDef.isContainer, "pebble-is-not-container");

                Check(catalog.TryGet("container-leather-bag", out var bagDef), "catalog-contains-leather-bag");
                Check(Mathf.Approximately(bagDef.massKg, 0.45f), "leather-bag-mass-matches-0.45kg");
                Check(bagDef.isContainer, "leather-bag-is-container");
                Check(bagDef.maxContainedSlots == 6, "leather-bag-slots-equals-6");
                Check(Mathf.Approximately(bagDef.maxContainedMassKg, 15.0f), "leather-bag-max-mass-equals-15kg");
                Check(true, "catalog-pebble-and-leather-bag-definitions-valid");
            }

            // -------------------------------------------------------------
            // 2. ItemModel Multi-Carry Limits Enforcement
            // -------------------------------------------------------------
            {
                var model = new ItemModel(worldId, genId);
                var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
                catalog.PopulateModel(model);
                var authority = new BasicItemActionAuthority();

                model.SetActorCarryLimits(agentId, new ActorCarryLimits(maxCarryMassKg: 25.0f, maxCarriedItems: 2));
                var limits = model.GetActorCarryLimits(agentId);
                Check(limits.maxCarriedItems == 2, "model-actor-carry-limits-configured-for-two-hands");

                model.RegisterItem("m-pebble-1", "stone-river-pebble", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("m-pebble-2", "stone-river-pebble", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
                model.RegisterItem("m-pebble-3", "stone-river-pebble", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

                var r1 = model.Execute(worldId, genId, new ItemActionRequest { requestId = 1, action = ItemActionKind.Pickup, actorId = agentId, itemId = "m-pebble-1" }, authority);
                Check(r1.success, "model-first-pickup-succeeds");
                Check(model.GetActorCarriedItemIds(agentId).Count == 1, "model-carried-count-is-1");

                var r2 = model.Execute(worldId, genId, new ItemActionRequest { requestId = 2, action = ItemActionKind.Pickup, actorId = agentId, itemId = "m-pebble-2" }, authority);
                Check(r2.success, "model-second-pickup-succeeds");
                Check(model.GetActorCarriedItemIds(agentId).Count == 2, "model-carried-count-is-2");

                var r3 = model.Execute(worldId, genId, new ItemActionRequest { requestId = 3, action = ItemActionKind.Pickup, actorId = agentId, itemId = "m-pebble-3" }, authority);
                Check(!r3.success && r3.code == "actor-hands-full", "model-third-pickup-rejected-actor-hands-full");
                Check(model.GetActorCarriedItemIds(agentId).Count == 2, "model-carried-count-remains-2");
                Check(true, "item-model-permits-two-carried-items-and-refuses-third");
            }

            // -------------------------------------------------------------
            // 3. Dual-Hand Action API: Pickup Succession, Hands-Full Refusal,
            //    Targeted & LIFO Drop, and Left-Hand Occupancy Events
            // -------------------------------------------------------------
            var tempObjects = new List<GameObject>();
            try
            {
                var actorGo = new GameObject("TestActor");
                tempObjects.Add(actorGo);
                actorGo.transform.position = Vector3.zero;

                var rightHandGo = new GameObject("RightHand");
                rightHandGo.transform.SetParent(actorGo.transform, false);
                rightHandGo.transform.localPosition = new Vector3(0.3f, 1.0f, 0.3f);

                var leftHandGo = new GameObject("LeftHand");
                leftHandGo.transform.SetParent(actorGo.transform, false);
                leftHandGo.transform.localPosition = new Vector3(-0.3f, 1.0f, 0.3f);

                var model = new ItemModel(worldId, genId);
                var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
                catalog.PopulateModel(model);
                var authority = new BasicItemActionAuthority();
                model.SetActorCarryLimits(agentId, new ActorCarryLimits(25.0f, 2));

                GameObject CreateTestPhysicalItem(string id, string typeId, Vector3 pos)
                {
                    var go = new GameObject(id);
                    tempObjects.Add(go);
                    go.transform.position = pos;
                    var box = go.AddComponent<BoxCollider>();
                    box.size = new Vector3(0.12f, 0.08f, 0.10f);
                    var rb = go.AddComponent<Rigidbody>();
                    rb.mass = 0.65f;

                    var ni = go.AddComponent<NpcInteractable>();
                    ni.StableId = id;
                    ni.WorldId = worldId;
                    ni.Kind = NpcObjectKind.Item;
                    ni.Permission = true;
                    ni.Approach = go.transform;

                    var phys = go.AddComponent<PhysicalItem>();
                    phys.itemId = id;
                    phys.itemTypeId = typeId;
                    phys.massKg = 0.65f;
                    phys.dimensions = new PhysicalDimensions(0.12f, 0.08f, 0.10f);
                    phys.ConfigureComponents();
                    phys.Bind(model, worldId, genId);

                    model.RegisterItem(id, typeId, ItemLocationKind.Free, pos, Quaternion.identity);
                    return go;
                }

                // Place items in front of actor within reach
                var p1Go = CreateTestPhysicalItem("pebble-01", "stone-river-pebble", new Vector3(0.1f, 0.0f, 0.4f));
                var p2Go = CreateTestPhysicalItem("pebble-02", "stone-river-pebble", new Vector3(-0.1f, 0.0f, 0.4f));
                var p3Go = CreateTestPhysicalItem("pebble-03", "stone-river-pebble", new Vector3(0.0f, 0.0f, 0.5f));

                var registry = new List<NpcInteractable>
                {
                    p1Go.GetComponent<NpcInteractable>(),
                    p2Go.GetComponent<NpcInteractable>(),
                    p3Go.GetComponent<NpcInteractable>()
                };

                var api = new NpcActionApi(agentId, worldId, actorGo.transform, rightHandGo.transform, leftHandGo.transform, registry);
                api.PhysicalModel = model;
                api.PhysicalAuthority = authority;

                // Track left-hand occupancy events
                bool leftOccupiedEventSeen = false;
                bool lastLeftOccupiedState = false;
                api.OnLeftHandOccupiedChanged += occupied =>
                {
                    leftOccupiedEventSeen = true;
                    lastLeftOccupiedState = occupied;
                };

                // Initial carry state check
                Check(api.HeldRight == null, "initial-held-right-null");
                Check(api.HeldLeft == null, "initial-held-left-null");
                Check(api.Held == null, "initial-held-backcompat-null");
                Check(api.HeldCount == 0, "initial-held-count-zero");

                // Pickup 1: Pebble 01 -> Right Hand
                var res1 = api.Execute(101, NpcActionKind.Pickup, "pebble-01");
                Check(res1.success && res1.code == "picked-up", "api-pickup-pebble-01-succeeds");
                Check(api.HeldRight != null && api.HeldRight.StableId == "pebble-01", "held-right-is-pebble-01");
                Check(api.HeldLeft == null, "held-left-still-null-after-first-pickup");
                Check(api.Held != null && api.Held.StableId == "pebble-01", "held-backcompat-returns-right-hand-item");
                Check(api.HeldCount == 1, "held-count-is-1-after-first-pickup");
                Check(!leftOccupiedEventSeen, "left-hand-not-notified-when-right-hand-picks-up");

                // Pickup 2: Pebble 02 -> Left Hand (Dual Carry)
                var res2 = api.Execute(102, NpcActionKind.Pickup, "pebble-02");
                Check(res2.success && res2.code == "picked-up", "api-pickup-pebble-02-succeeds");
                Check(api.HeldRight != null && api.HeldRight.StableId == "pebble-01", "held-right-still-pebble-01");
                Check(api.HeldLeft != null && api.HeldLeft.StableId == "pebble-02", "held-left-is-pebble-02");
                Check(api.HeldCount == 2, "held-count-is-2-dual-carry-active");
                Check(leftOccupiedEventSeen && lastLeftOccupiedState, "left-hand-occupancy-event-fired-true");
                Check(true, "dual-hand-pickup-both-hands-occupied");

                // Pickup 3: Pebble 03 -> Rejected (Hands Full)
                var res3 = api.Execute(103, NpcActionKind.Pickup, "pebble-03");
                Check(!res3.success && res3.code == "hands-full", "api-pickup-third-pebble-rejected-hands-full");
                Check(api.HeldCount == 2, "held-count-remains-2-after-denial");
                Check(true, "dual-hand-third-pickup-rejected-hands-full");

                // Drop targeted item by StableId: Drop Pebble 01 (Right Hand)
                var dropR = api.Execute(104, NpcActionKind.Drop, "pebble-01");
                Check(dropR.success && dropR.code == "dropped", "drop-pebble-01-succeeds");
                Check(api.HeldRight == null, "held-right-cleared-after-drop");
                Check(api.HeldLeft != null && api.HeldLeft.StableId == "pebble-02", "held-left-remains-pebble-02");
                Check(api.Held != null && api.Held.StableId == "pebble-02", "held-backcompat-returns-remaining-left-item");
                Check(api.HeldCount == 1, "held-count-is-1-after-dropping-right");
                Check(true, "dual-hand-drop-targeted-item-frees-correct-hand");

                // Now pick up Pebble 03: should attach into the now-empty Right Hand!
                var resPickAgain = api.Execute(105, NpcActionKind.Pickup, "pebble-03");
                Check(resPickAgain.success && resPickAgain.code == "picked-up", "pickup-into-freed-right-hand-succeeds");
                Check(api.HeldRight != null && api.HeldRight.StableId == "pebble-03", "held-right-is-now-pebble-03");
                Check(api.HeldLeft != null && api.HeldLeft.StableId == "pebble-02", "held-left-still-pebble-02");
                Check(api.HeldCount == 2, "held-count-is-2-again");

                // Drop LIFO Default (targetId = null): Drops Left Hand first!
                leftOccupiedEventSeen = false;
                var dropLifo1 = api.Execute(106, NpcActionKind.Drop, null);
                Check(dropLifo1.success && dropLifo1.code == "dropped", "drop-lifo-1-succeeds");
                Check(api.HeldLeft == null, "lifo-drop-cleared-left-hand-first");
                Check(api.HeldRight != null && api.HeldRight.StableId == "pebble-03", "held-right-remains-pebble-03");
                Check(api.HeldCount == 1, "held-count-is-1-after-lifo-drop-1");
                Check(leftOccupiedEventSeen && !lastLeftOccupiedState, "left-hand-occupancy-event-fired-false");

                // Drop LIFO Default again: Drops Right Hand!
                // Clear previously dropped pebble-01 from the right-hand release zone
                p1Go.transform.position = new Vector3(10f, 0f, 0f);
                Physics.SyncTransforms();

                var dropLifo2 = api.Execute(107, NpcActionKind.Drop, null);
                Check(dropLifo2.success && dropLifo2.code == "dropped", "drop-lifo-2-succeeds");
                Check(api.HeldRight == null, "held-right-cleared-after-lifo-drop-2");
                Check(api.HeldLeft == null, "both-hands-now-empty");
                Check(api.HeldCount == 0, "held-count-is-zero");
                Check(true, "dual-hand-lifo-drop-clears-left-hand-then-right");
            }
            finally
            {
                for (int i = tempObjects.Count - 1; i >= 0; i--)
                {
                    if (tempObjects[i] != null)
                        UnityEngine.Object.DestroyImmediate(tempObjects[i]);
                }
            }

            // -------------------------------------------------------------
            // 4. Leather Bag Storage: Store Carried Pebble into Bag Frees Hand
            // -------------------------------------------------------------
            var bagObjects = new List<GameObject>();
            try
            {
                var actorGo = new GameObject("BagActor");
                bagObjects.Add(actorGo);
                actorGo.transform.position = Vector3.zero;

                var rightHandGo = new GameObject("RHand");
                rightHandGo.transform.SetParent(actorGo.transform, false);
                rightHandGo.transform.localPosition = new Vector3(0.3f, 1.0f, 0.3f);

                var leftHandGo = new GameObject("LHand");
                leftHandGo.transform.SetParent(actorGo.transform, false);
                leftHandGo.transform.localPosition = new Vector3(-0.3f, 1.0f, 0.3f);

                var model = new ItemModel(worldId, genId);
                var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
                catalog.PopulateModel(model);
                var authority = new BasicItemActionAuthority();
                model.SetActorCarryLimits(agentId, new ActorCarryLimits(25.0f, 2));

                // Create leather bag container placed near actor
                var bagGo = new GameObject("leather-bag-01");
                bagObjects.Add(bagGo);
                bagGo.transform.position = new Vector3(0.2f, 0.0f, 0.3f);
                var bagCol = bagGo.AddComponent<BoxCollider>();
                bagCol.size = new Vector3(0.25f, 0.20f, 0.20f);
                var bagRb = bagGo.AddComponent<Rigidbody>();
                bagRb.mass = 0.45f;
                var bagNi = bagGo.AddComponent<NpcInteractable>();
                bagNi.StableId = "leather-bag-01";
                bagNi.WorldId = worldId;
                bagNi.Kind = NpcObjectKind.Item;
                bagNi.Permission = true;
                bagNi.Approach = bagGo.transform;
                var bagPhys = bagGo.AddComponent<PhysicalItem>();
                bagPhys.itemId = "leather-bag-01";
                bagPhys.itemTypeId = "container-leather-bag";
                bagPhys.massKg = 0.45f;
                bagPhys.dimensions = new PhysicalDimensions(0.25f, 0.20f, 0.20f);
                bagPhys.ConfigureComponents();
                bagPhys.Bind(model, worldId, genId);
                model.RegisterItem("leather-bag-01", "container-leather-bag", ItemLocationKind.Free, bagGo.transform.position, Quaternion.identity);

                // Create pebbles
                GameObject CreatePebble(string id, Vector3 pos)
                {
                    var go = new GameObject(id);
                    bagObjects.Add(go);
                    go.transform.position = pos;
                    var col = go.AddComponent<BoxCollider>();
                    col.size = new Vector3(0.12f, 0.08f, 0.10f);
                    var rb = go.AddComponent<Rigidbody>();
                    rb.mass = 0.65f;
                    var ni = go.AddComponent<NpcInteractable>();
                    ni.StableId = id;
                    ni.WorldId = worldId;
                    ni.Kind = NpcObjectKind.Item;
                    ni.Permission = true;
                    ni.Approach = go.transform;
                    var phys = go.AddComponent<PhysicalItem>();
                    phys.itemId = id;
                    phys.itemTypeId = "stone-river-pebble";
                    phys.massKg = 0.65f;
                    phys.dimensions = new PhysicalDimensions(0.12f, 0.08f, 0.10f);
                    phys.ConfigureComponents();
                    phys.Bind(model, worldId, genId);
                    model.RegisterItem(id, "stone-river-pebble", ItemLocationKind.Free, pos, Quaternion.identity);
                    return go;
                }

                var pebbleAGo = CreatePebble("pebble-A", new Vector3(0.1f, 0.0f, 0.4f));
                var pebbleBGo = CreatePebble("pebble-B", new Vector3(-0.1f, 0.0f, 0.4f));
                var pebbleCGo = CreatePebble("pebble-C", new Vector3(0.0f, 0.0f, 0.45f));

                var reg = new List<NpcInteractable>
                {
                    bagNi,
                    pebbleAGo.GetComponent<NpcInteractable>(),
                    pebbleBGo.GetComponent<NpcInteractable>(),
                    pebbleCGo.GetComponent<NpcInteractable>()
                };

                var api = new NpcActionApi(agentId, worldId, actorGo.transform, rightHandGo.transform, leftHandGo.transform, reg);
                api.PhysicalModel = model;
                api.PhysicalAuthority = authority;

                // Pick up pebble-A (Right) and pebble-B (Left)
                Check(api.Execute(201, NpcActionKind.Pickup, "pebble-A").success, "bagtest-pickup-A-right");
                Check(api.Execute(202, NpcActionKind.Pickup, "pebble-B").success, "bagtest-pickup-B-left");
                Check(api.HeldCount == 2, "bagtest-both-hands-full");

                // Store pebble-B from Left Hand into leather-bag-01
                var storeRes = api.Execute(203, NpcActionKind.Store, "pebble-B", "leather-bag-01");
                Check(storeRes.success && storeRes.code == "stored-in-container", "store-pebble-B-into-bag-succeeds");
                Check(api.HeldLeft == null, "held-left-is-cleared-after-store");
                Check(api.HeldRight != null && api.HeldRight.StableId == "pebble-A", "held-right-still-holds-pebble-A");
                Check(api.HeldCount == 1, "held-count-is-1-hand-freed");

                // Now pick up pebble-C: should succeed into the freed Left Hand!
                var pickCRes = api.Execute(204, NpcActionKind.Pickup, "pebble-C");
                Check(pickCRes.success && pickCRes.code == "picked-up", "pickup-pebble-C-into-freed-left-hand-succeeds");
                Check(api.HeldLeft != null && api.HeldLeft.StableId == "pebble-C", "held-left-holds-pebble-C");
                Check(api.HeldCount == 2, "held-count-back-to-2");
                Check(true, "dual-hand-store-into-leather-bag-frees-hand-slot");
            }
            finally
            {
                for (int i = bagObjects.Count - 1; i >= 0; i--)
                {
                    if (bagObjects[i] != null)
                        UnityEngine.Object.DestroyImmediate(bagObjects[i]);
                }
            }

            // -------------------------------------------------------------
            // 5. Hunter Club Back-Holster Behavior
            // -------------------------------------------------------------
            var clubTestObjects = new List<GameObject>();
            try
            {
                var actorGo = new GameObject("TestHunterActor");
                clubTestObjects.Add(actorGo);
                var chestGo = new GameObject("Chest");
                clubTestObjects.Add(chestGo);
                chestGo.transform.SetParent(actorGo.transform, false);

                var clubGo = new GameObject("TestClub");
                clubTestObjects.Add(clubGo);
                var mr = clubGo.AddComponent<MeshRenderer>();

                var carry = actorGo.AddComponent<CityLife.World.HunterClubCarry>();
                carry.Actor = actorGo.transform;
                carry.Club = clubGo.transform;

                // Initially not stowed
                Check(!carry.Stowed, "hunter-club-initially-not-stowed");
                Check(mr.enabled, "hunter-club-renderer-enabled");

                // Set stowed -> true: renderer remains enabled and stowed is active
                carry.SetStowed(true);
                Check(carry.Stowed, "hunter-club-stowed-true");
                Check(mr.enabled, "hunter-club-renderer-remains-enabled-when-stowed");

                // Set stowed -> false
                carry.SetStowed(false);
                Check(!carry.Stowed, "hunter-club-stowed-false-restored");
                Check(true, "hunter-club-back-holster-verified");
            }
            finally
            {
                for (int i = clubTestObjects.Count - 1; i >= 0; i--)
                {
                    if (clubTestObjects[i] != null)
                        UnityEngine.Object.DestroyImmediate(clubTestObjects[i]);
                }
            }

            // -------------------------------------------------------------
            // 6. Marine Protein Crab & Tidal Driftwood Verification
            // -------------------------------------------------------------
            {
                var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
                Check(catalog.TryGet("food-protein-crab", out var crabDef), "catalog-contains-protein-crab");
                Check(Mathf.Approximately(crabDef.massKg, 0.45f), "protein-crab-mass-matches-0.45kg");
                Check(!crabDef.isContainer, "protein-crab-is-not-container");

                Check(catalog.TryGet("wood-driftwood-log", out var driftDef), "catalog-contains-driftwood-log");
                Check(Mathf.Approximately(driftDef.massKg, 4.2f), "driftwood-log-mass-matches-4.2kg");
                Check(!driftDef.isContainer, "driftwood-log-is-not-container");

                // Verify procedural meshes and ecology definitions
                Check(CoastalCrabDistribution.VerifyCrabEcology(out _), "crab-ecology-geometry-verified");
                Check(DriftwoodTideDeposit.VerifyDriftwoodEcology(out _), "driftwood-ecology-geometry-verified");
            }

            // -------------------------------------------------------------
            // 7. Coastal Tide & Canyon Micro-Weather Verification
            // -------------------------------------------------------------
            {
                // Tide mathematical properties
                float t0 = CoastalTide.EvaluateTide(0f);
                Check(Mathf.Abs(t0) < 0.001f, "tide-evaluates-to-zero-at-time-zero");

                float tHigh = CoastalTide.EvaluateTide(60f); // 60s is 1/4 of 240s cycle
                Check(tHigh >= CoastalTide.AmplitudeMetres - 0.01f, "tide-evaluates-to-peak-high-at-quarter-cycle");
                Check(CoastalTide.IsHighTide(60f), "quarter-cycle-correctly-identified-as-high-tide");

                float tLow = CoastalTide.EvaluateTide(180f); // 180s is 3/4 of 240s cycle
                Check(tLow <= -CoastalTide.AmplitudeMetres + 0.01f, "tide-evaluates-to-slack-low-at-three-quarter-cycle");
                Check(CoastalTide.IsLowTide(180f), "three-quarter-cycle-correctly-identified-as-low-tide");

                // Micro-Weather verification
                Check(CanyonMicroWeather.VerifyMicroWeather(out _), "canyon-micro-weathers-verified");

                var cavernRep = CanyonMicroWeather.Sample(new Vector3(-140f, 6f, 90f));
                Check(cavernRep.exposure == ExposureRating.Sheltered && cavernRep.rainMultiplier == 0f, "refuge-cavern-provides-complete-rain-shelter");

                var mesaRep = CanyonMicroWeather.Sample(new Vector3(50f, 30f, -20f));
                Check(mesaRep.exposure == ExposureRating.Harsh && mesaRep.windMultiplier > 1.5f, "high-mesa-amplifies-wind-and-solar-load");

                var coastRep = CanyonMicroWeather.Sample(new Vector3(0f, -2f, 100f));
                Check(coastRep.exposure == ExposureRating.Maritime && coastRep.localTemperatureC < 22f, "coastal-delta-samples-cool-maritime-breeze");
            }

            // -------------------------------------------------------------
            // 8. Freshwater River & Creatures Drive Reduction Affinities
            // -------------------------------------------------------------
            {
                // River freshwater drinking boundary verification
                Check(CoastalTerrain.IsFreshwaterRiver(0f, 0f, -2f, -2f), "river-channel-center-is-freshwater-drinking");
                Check(CoastalTerrain.IsFreshwaterRiver(25f, -245f, 10f, -2f), "waterfall-approach-is-freshwater-drinking");
                Check(!CoastalTerrain.IsFreshwaterRiver(0f, 500f, -2f, -2f), "north-sea-saline-water-refuses-freshwater-drinking");
                Check(!CoastalTerrain.IsFreshwaterRiver(50f, -20f, 30f, -2f), "high-mesa-cliff-refuses-river-drinking");

                // Drive reduction endorphin & affinity verification
                var body = new Starfall.Food.FoodBody { endorphin = 2000 };
                Starfall.Food.FoodPhysiology.ApplyDriveReduction(body, 1800);
                Check(body.endorphin == 3800, "drive-reduction-boosts-endorphins-by-1800-on-drink");

                var satObj = new GameObject("test-survival-autonomy");
                try
                {
                    var satAutonomy = satObj.AddComponent<StarfallSurvivalAutonomy>();
                    satAutonomy.BoostPlaceAffinity("freshwater-river", 15);
                    Check(satAutonomy.CherishedPlaceAffinities.TryGetValue("freshwater-river", out int aff) && aff == 15, "river-drinking-boosts-freshwater-river-cherished-affinity");
                    satAutonomy.BoostPlaceAffinity("freshwater-river", 20);
                    Check(satAutonomy.CherishedPlaceAffinities["freshwater-river"] == 35, "repeated-visitation-accumulates-cherished-place-affinity");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(satObj);
                }
            }

            // -------------------------------------------------------------
            // 9. Shallow River Crossing Ford & Dry Berry Bush Verification
            // -------------------------------------------------------------
            {
                // Shallow ford crossing geometry & navigability
                Check(CoastalTerrain.IsRiverFord(0f, -15f), "shallow-ford-detected-at-river-crossing-center");
                Check(CoastalTerrain.IsRiverFord(15f, -15f), "shallow-ford-detected-at-east-bank-approach");
                Check(CoastalTerrain.IsRiverFord(-15f, -15f), "shallow-ford-detected-at-west-bank-approach");
                Check(!CoastalTerrain.IsRiverFord(0f, 150f), "far-north-outlet-not-identified-as-ford");
                Check(!CoastalTerrain.IsRiverFord(0f, -250f), "far-south-waterfall-not-identified-as-ford");

                float fordY = CoastalTerrain.Height(0f, -15f);
                Check(fordY >= -2.25f && fordY <= -2.10f, "ford-riverbed-height-is-shallow-18cm-water-depth");
                float depth = CoastalWater.Level - fordY;
                Check(depth >= 0.10f && depth <= 0.25f, "ford-water-depth-is-ankle-deep-not-swimming");

                // Both east and west approaches must be walkable above water line
                float eastApproachY = CoastalTerrain.Height(28f, -15f);
                float westApproachY = CoastalTerrain.Height(-28f, -15f);
                Check(eastApproachY > CoastalWater.Level, "ford-east-bank-approach-is-dry-land");
                Check(westApproachY > CoastalWater.Level, "ford-west-bank-approach-is-dry-land");

                // Dry berry bush placements: no bushes underwater!
                Check(CoastalTerrain.Height(142f, -65f) >= CoastalWater.Level + 0.8f, "east-terrace-berry-is-dry");
                Check(CoastalTerrain.Height(122f, -54f) >= CoastalWater.Level + 0.8f, "spring-oasis-berry-is-dry");
                Check(CoastalTerrain.Height(135f, -95f) >= CoastalWater.Level + 0.8f, "south-terrace-berry-is-dry");
                Check(CoastalTerrain.Height(-152f, 110f) >= CoastalWater.Level + 0.8f, "refuge-shelf-berry-is-dry");
                Check(CoastalTerrain.Height(-170f, 95f) >= CoastalWater.Level + 0.8f, "west-cave-berry-is-dry");
                Check(CoastalTerrain.Height(65f, -35f) >= CoastalWater.Level + 0.8f, "ford-east-bank-berry-is-dry");
                Check(CoastalTerrain.Height(-65f, -35f) >= CoastalWater.Level + 0.8f, "ford-west-bank-berry-is-dry");
            }

            // -------------------------------------------------------------
            // 10. Hunter Waist Moonbag & River Fish Ecology Verification
            // -------------------------------------------------------------
            {
                var mbGo = new GameObject("test-moonbag");
                try
                {
                    var mb = mbGo.AddComponent<HunterMoonbag>();
                    Check(mb.StoredCount == 0, "moonbag-initially-empty");
                    Check(mb.CanStore, "moonbag-can-store-initially");
                    Check(!mb.CanRetrieve, "empty-moonbag-cannot-retrieve");

                    Check(mb.StoreFruit(), "moonbag-stores-first-fruit");
                    Check(mb.StoredCount == 1, "moonbag-count-is-1");
                    Check(mb.CanStore, "moonbag-can-still-store-second-fruit");

                    Check(mb.StoreFruit(), "moonbag-stores-second-fruit");
                    Check(mb.StoredCount == 2, "moonbag-count-is-2");
                    Check(!mb.CanStore, "full-moonbag-refuses-third-fruit");
                    Check(!mb.StoreFruit(), "moonbag-store-third-returns-false");

                    Check(mb.RetrieveFruit(), "moonbag-retrieves-fruit");
                    Check(mb.StoredCount == 1, "moonbag-count-is-1-after-retrieve");
                    Check(mb.RetrieveFruit(), "moonbag-retrieves-second-fruit");
                    Check(mb.StoredCount == 0, "moonbag-count-is-0-after-all-retrieved");
                    Check(!mb.CanRetrieve, "empty-moonbag-refuses-further-retrieve");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(mbGo);
                }

                // River Fish School ecology & catalog verification
                Check(RiverFishSchool.VerifyFishEcology(out _), "river-fish-ecology-verified");

                var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
                Check(catalog.TryGet("food-river-fish", out var fishDef), "catalog-contains-river-fish");
                Check(Mathf.Approximately(fishDef.massKg, 0.65f), "river-fish-mass-matches-0.65kg");

                Check(catalog.TryGet("food-sourfig-berry", out var berryDef), "catalog-contains-sourfig-berry");
                Check(Mathf.Approximately(berryDef.massKg, 0.08f), "sourfig-berry-mass-matches-0.08kg");

                Check(catalog.TryGet("container-waist-bag", out var waistDef), "catalog-contains-waist-moonbag");
                Check(waistDef.maxContainedSlots == 2, "waist-moonbag-slots-equals-2");

                // Cooked items catalog checks
                Check(catalog.TryGet("food-cooked-fish", out var cookedFishDef), "catalog-contains-cooked-fish");
                Check(Mathf.Approximately(cookedFishDef.massKg, 0.55f), "cooked-fish-mass-matches-0.55kg");
                Check(cookedFishDef.dimensions.height > 0f, "cooked-fish-has-valid-dimensions");

                Check(catalog.TryGet("food-cooked-crab", out var cookedCrabDef), "catalog-contains-cooked-crab");
                Check(Mathf.Approximately(cookedCrabDef.massKg, 0.40f), "cooked-crab-mass-matches-0.40kg");
                Check(cookedCrabDef.dimensions.height > 0f, "cooked-crab-has-valid-dimensions");

                // HearthCooking transformation logic checks
                Check(HearthCooking.CanRoast("food-river-fish"), "can-roast-river-fish");
                Check(HearthCooking.CanRoast("food-protein-crab"), "can-roast-protein-crab");
                Check(!HearthCooking.CanRoast("canyon-stone"), "cannot-roast-stone");
                Check(HearthCooking.GetCookedTypeId("food-river-fish") == "food-cooked-fish", "fish-cooked-type-id-matches");
                Check(HearthCooking.GetCookedTypeId("food-protein-crab") == "food-cooked-crab", "crab-cooked-type-id-matches");

                var testHearthGo = new GameObject("TestHearthCooking");
                var testFishGo = new GameObject("TestFishItem");
                try
                {
                    var cooker = testHearthGo.AddComponent<HearthCooking>();
                    cooker.AuthoredHearthPosition = Vector3.zero;

                    var fishPhys = testFishGo.AddComponent<PhysicalItem>();
                    fishPhys.itemId = "fish-01";
                    fishPhys.itemTypeId = "food-river-fish";
                    fishPhys.massKg = 0.65f;
                    fishPhys.ConfigureComponents();

                    var fishInteractable = testFishGo.AddComponent<NpcInteractable>();
                    fishInteractable.StableId = "item-food-river-fish-01";

                    var mr = testFishGo.AddComponent<MeshRenderer>();

                    Check(cooker.RoastItem(testFishGo), "cooker-roasts-raw-fish");
                    Check(fishPhys.itemTypeId == "food-cooked-fish", "fish-type-id-updated-to-cooked");
                    Check(Mathf.Approximately(fishPhys.massKg, 0.55f), "fish-mass-updated-to-0.55kg");
                    Check(fishInteractable.StableId.Contains("food-cooked-fish"), "fish-interactable-id-updated");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(testFishGo);
                    UnityEngine.Object.DestroyImmediate(testHearthGo);
                }

                // PlaceLedger expanded capacity check (4096 cells)
                Check(PlaceLedger.MaximumCells == 4096, "place-ledger-maximum-cells-equals-4096");
                Check(PlaceLedger.MaximumEvents == 512, "place-ledger-maximum-events-equals-512");
            }

            return passed;
        }
    }
}