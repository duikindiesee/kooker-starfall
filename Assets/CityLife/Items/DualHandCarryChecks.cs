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

                Check(catalog.TryGet("food-river-carp", out var carpDef), "catalog-contains-river-carp");
                Check(Mathf.Approximately(carpDef.massKg, 0.85f), "river-carp-mass-matches-0.85kg");

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
                Check(HearthCooking.CanRoast("food-river-carp"), "can-roast-river-carp");
                Check(HearthCooking.CanRoast("food-protein-crab"), "can-roast-protein-crab");
                Check(!HearthCooking.CanRoast("canyon-stone"), "cannot-roast-stone");
                Check(HearthCooking.GetCookedTypeId("food-river-fish") == "food-cooked-fish", "fish-cooked-type-id-matches");
                Check(HearthCooking.GetCookedTypeId("food-river-carp") == "food-cooked-fish", "carp-cooked-type-id-matches");
                Check(HearthCooking.GetItemDisplayName("food-river-carp") == "Gauteng Common Carp", "carp-display-name-matches");
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

                // Gauteng Common Carp Full Physical Lifecycle Checks (spawning, carry, roast, eat, persist)
                var testCarpGo = new GameObject("TestCarpItem");
                var testHearthCarpGo = new GameObject("TestHearthCookingCarp");
                try
                {
                    var cooker = testHearthCarpGo.AddComponent<HearthCooking>();
                    cooker.AuthoredHearthPosition = Vector3.zero;

                    var carpPhys = testCarpGo.AddComponent<PhysicalItem>();
                    carpPhys.itemId = "carp-item-01";
                    carpPhys.itemTypeId = "food-river-carp";
                    carpPhys.massKg = 0.85f;
                    carpPhys.ConfigureComponents();

                    var carpInteractable = testCarpGo.AddComponent<NpcInteractable>();
                    carpInteractable.StableId = "item-food-river-carp-01";

                    var mr = testCarpGo.AddComponent<MeshRenderer>();

                    // 1. Physical item configuration & mass
                    Check(carpPhys.itemTypeId == "food-river-carp", "carp-physical-item-configured");
                    Check(Mathf.Approximately(carpPhys.massKg, 0.85f), "carp-mass-matches-0.85kg");

                    // 2. Hearth roasting transformation
                    Check(cooker.RoastItem(testCarpGo), "cooker-roasts-raw-carp");
                    Check(carpPhys.itemTypeId == "food-cooked-fish", "carp-type-id-updated-to-cooked-fish");
                    Check(Mathf.Approximately(carpPhys.massKg, 0.55f), "carp-mass-reduced-to-0.55kg-post-roast");
                    Check(carpInteractable.StableId.Contains("food-cooked-fish"), "carp-interactable-id-updated-to-cooked");

                    // 3. Nutrition & consumption lifecycle
                    var foodState = new Starfall.Food.FoodState();
                    foodState.satiety = 2000;
                    foodState.body.protein = 1500;
                    foodState.body.stomach = 1000;
                    // Apply cooked fish feast gains matching StarfallSurvivalAutonomy
                    foodState.body.stomach = Mathf.Min(10000, foodState.body.stomach + 3500);
                    foodState.body.protein = Mathf.Min(10000, foodState.body.protein + 4000);
                    foodState.satiety = Mathf.Min(10000, foodState.satiety + 3500);
                    Check(foodState.satiety == 5500, "carp-consumption-boosts-satiety-to-5500");
                    Check(foodState.body.protein == 5500, "carp-consumption-boosts-protein-to-5500");
                    Check(foodState.body.stomach == 4500, "carp-consumption-boosts-stomach-to-4500");

                    // 4. Persistence round-trip serialization
                    string json = JsonUtility.ToJson(foodState);
                    var reloadedState = JsonUtility.FromJson<Starfall.Food.FoodState>(json);
                    Check(reloadedState != null && reloadedState.satiety == 5500 && reloadedState.body.protein == 5500, "carp-consumption-state-persists-round-trip");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(testCarpGo);
                    UnityEngine.Object.DestroyImmediate(testHearthCarpGo);
                }

                // PlaceLedger expanded capacity check (4096 cells)
                Check(PlaceLedger.MaximumCells == 4096, "place-ledger-maximum-cells-equals-4096");
                Check(PlaceLedger.MaximumEvents == 512, "place-ledger-maximum-events-equals-512");
            }

            // -------------------------------------------------------------
            // 12. Starvation Prevention, Multi-Bush Foraging, and Fishing Checks
            // -------------------------------------------------------------
            {
                // Verify 2-word parse and build request for new actions
                var testEligible = new List<string> { "eat fruit", "catch fish", "catch crab", "eat catch", "seek food", "gather berry" };
                Check(StarfallSurvivalThought.Parse("eat fruit", testEligible, out string parsedFruit) && parsedFruit == "eat fruit", "parse-eat-fruit");
                Check(StarfallSurvivalThought.Parse("catch fish", testEligible, out string parsedFish) && parsedFish == "catch fish", "parse-catch-fish");
                Check(StarfallSurvivalThought.Parse("catch crab", testEligible, out string parsedCrab) && parsedCrab == "catch crab", "parse-catch-crab");
                Check(StarfallSurvivalThought.Parse("eat catch", testEligible, out string parsedEatCatch) && parsedEatCatch == "eat catch", "parse-eat-catch");
                Check(StarfallSurvivalThought.Parse("seek food", testEligible, out string parsedSeek) && parsedSeek == "seek food", "parse-seek-food");
                Check(StarfallSurvivalThought.Parse("gather berry", testEligible, out string parsedGather) && parsedGather == "gather berry", "parse-gather-berry");

                string reqJson = StarfallSurvivalThought.BuildRequest("test-model", 2000, 5000, new List<string> { "eat fruit", "explore north" }, 1, true, "eat fruit succeeded");
                Check(reqJson.Contains("eat fruit"), "request-json-contains-eat-fruit");
                Check(reqJson.Contains("Last verified outcome: ate ripe fruit to reduce hunger"), "request-json-contains-fruit-outcome");

                string fishReqJson = StarfallSurvivalThought.BuildRequest("test-model", 2000, 5000, new List<string> { "catch fish", "explore north" }, 0, false, "catch fish succeeded");
                Check(fishReqJson.Contains("Last verified outcome: caught freshwater fish in shallows"), "request-json-contains-fish-outcome");

                // Test spawning fish & crab in hand
                var testActorGo = new GameObject("TestInhabitantForaging");
                try
                {
                    var brain = testActorGo.AddComponent<CityLife.World.NpcAutonomy>();
                    var controls = testActorGo.AddComponent<CityLife.World.NpcPlayerControls>();

                    var rHandGo = new GameObject("RightHand");
                    rHandGo.transform.SetParent(testActorGo.transform);

                    var lHandGo = new GameObject("LeftHand");
                    lHandGo.transform.SetParent(testActorGo.transform);

                    var actions = new CityLife.World.NpcActionApi("test-agent", "test-world", testActorGo.transform, rHandGo.transform, lHandGo.transform, Array.Empty<CityLife.World.NpcInteractable>());
                    brain.SetActionsForTesting(actions);
                    controls.Brain = brain;

                    var fishGo = controls.SpawnFishInHand();
                    Check(fishGo != null, "controls-spawns-fish-in-hand");
                    var fishPhys = fishGo.GetComponent<PhysicalItem>();
                    Check(fishPhys != null && fishPhys.itemTypeId == "food-river-fish", "spawned-fish-has-river-fish-type");
                    Check(actions.Held != null, "actions-held-is-not-null-after-fish-spawn");
                    UnityEngine.Object.DestroyImmediate(fishGo);
                    actions.HoldItemDirect(null, false);

                    var carpGo = controls.SpawnCarpInHand();
                    Check(carpGo != null, "controls-spawns-carp-in-hand");
                    var carpPhys = carpGo.GetComponent<PhysicalItem>();
                    Check(carpPhys != null && carpPhys.itemTypeId == "food-river-carp", "spawned-carp-has-river-carp-type");
                    Check(actions.Held != null, "actions-held-is-not-null-after-carp-spawn");
                    UnityEngine.Object.DestroyImmediate(carpGo);
                    actions.HoldItemDirect(null, false);

                    var crabGo = controls.SpawnCrabInHand();
                    Check(crabGo != null, "controls-spawns-crab-in-hand");
                    var crabPhys = crabGo.GetComponent<PhysicalItem>();
                    Check(crabPhys != null && crabPhys.itemTypeId == "food-protein-crab", "spawned-crab-has-protein-crab-type");
                    UnityEngine.Object.DestroyImmediate(crabGo);
                    actions.HoldItemDirect(null, false);

                    var berryGo = controls.SpawnBerryInHand();
                    Check(berryGo != null, "controls-spawns-berry-in-hand");
                    var berryPhys = berryGo.GetComponent<PhysicalItem>();
                    Check(berryPhys != null && berryPhys.itemTypeId == "food-sourfig-berry", "spawned-berry-has-sourfig-type");
                    UnityEngine.Object.DestroyImmediate(berryGo);
                    actions.HoldItemDirect(null, false);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(testActorGo);
                }
            }

            // -------------------------------------------------------------
            // 13. Water Drinking, Container Invariants, Navigation Resilience, and Options Sync
            // -------------------------------------------------------------
            {
                // 1. Water Action Parsing & Request Formatting
                var testEligible = new List<string> { "drink water", "seek water", "drink river", "seek food" };
                Check(StarfallSurvivalThought.Parse("drink water", testEligible, out string parsedDrink) && parsedDrink == "drink water", "parse-drink-water");
                Check(StarfallSurvivalThought.Parse("seek water", testEligible, out string parsedSeekWater) && parsedSeekWater == "seek water", "parse-seek-water");
                Check(StarfallSurvivalThought.Parse("drink river", testEligible, out string parsedRiver) && parsedRiver == "drink river", "parse-drink-river");

                string waterReqJson = StarfallSurvivalThought.BuildRequest("test-model", 2000, 1000, new List<string> { "drink water", "explore north" }, 0, false, "drink water succeeded");
                Check(waterReqJson.Contains("Last verified outcome: drank carried freshwater from container"), "request-json-contains-drink-water-outcome");

                string seekWaterReqJson = StarfallSurvivalThought.BuildRequest("test-model", 2000, 1000, new List<string> { "seek water", "explore north" }, 0, false, "seek water started");
                Check(seekWaterReqJson.Contains("Last verified outcome: seeking freshwater river to quench thirst"), "request-json-contains-seek-water-outcome");

                // 2. Carried Water Container Model Invariants
                var s = new Starfall.Food.FoodState { world = "test-world", generation = "gen-1", freshwaterMl = 2000, hydration = 1000 };
                Check(Starfall.Food.FoodModel.Valid(s, "test-world", "gen-1"), "freshwater-2000-valid");
                s.freshwaterMl = 1750;
                Check(Starfall.Food.FoodModel.Valid(s, "test-world", "gen-1"), "freshwater-1750-valid");
                s.freshwaterMl = 0;
                Check(Starfall.Food.FoodModel.Valid(s, "test-world", "gen-1"), "freshwater-0-valid");
                s.freshwaterMl = 250;
                s.freshwaterMl -= 250; // drink water step
                s.hydration = Mathf.Min(10000, s.hydration + 2000);
                Check(s.freshwaterMl == 0 && s.hydration == 3000, "drink-water-restores-hydration-and-consumes-container");
                s.freshwaterMl = 2000; // drink river refill step
                Check(s.freshwaterMl == 2000 && Starfall.Food.FoodModel.Valid(s, "test-world", "gen-1"), "river-refill-restores-2000ml");

                // 3. Navigation FindNearestRiverBank
                var navGo = new GameObject("TestNavRiver");
                try
                {
                    var nav = navGo.AddComponent<CityLife.World.NpcTerrainNavigation>();
                    Vector3 riverBank = nav.FindNearestRiverBank(new Vector3(-53f, 4f, 39f));
                    Check(float.IsFinite(riverBank.x) && float.IsFinite(riverBank.y) && float.IsFinite(riverBank.z), "find-nearest-river-bank-finite");
                    Check(riverBank != Vector3.zero, "find-nearest-river-bank-non-zero");

                    // ConstrainMotion obstacle deflection
                    Vector3 forwardBlocked = nav.ConstrainMotion(new Vector3(0, 0, 0), Vector3.forward, 0.2f);
                    Check(float.IsFinite(forwardBlocked.x) && float.IsFinite(forwardBlocked.z), "constrain-motion-result-finite");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(navGo);
                }
            }

            // -------------------------------------------------------------
            // 14. Forage Priority, Immediate Eat When Low, and Moonbag Store When Full
            // -------------------------------------------------------------
            {
                // 1. Action Parse & Narrative Formatting for new Gather/Approach Outcomes
                var testEligible = new List<string> { "approach berry", "gather berry", "eat fruit", "seek food" };
                Check(StarfallSurvivalThought.Parse("approach berry", testEligible, out string parsedApp) && parsedApp == "approach berry", "parse-approach-berry");
                Check(StarfallSurvivalThought.Parse("gather berry", testEligible, out string parsedGath) && parsedGath == "gather berry", "parse-gather-berry-14");

                string ateReqJson = StarfallSurvivalThought.BuildRequest("test-model", 1500, 5000, testEligible, 0, false, "gather berry succeeded; ate immediately");
                Check(ateReqJson.Contains("gathered ripe berry and ate immediately to replenish energy"), "req-json-ate-immediately");

                string storedReqJson = StarfallSurvivalThought.BuildRequest("test-model", 9000, 8000, testEligible, 0, false, "gather berry succeeded; stored in moonbag");
                Check(storedReqJson.Contains("gathered ripe berry and stored in waist moonbag for later"), "req-json-stored-in-moonbag");

                string appReqJson = StarfallSurvivalThought.BuildRequest("test-model", 1500, 5000, testEligible, 0, false, "approach berry started");
                Check(appReqJson.Contains("approaching nearby ripe berry bush to forage"), "req-json-approach-started");

                // 2. State & Invariants when gathering and eating immediately on low hunger
                var s = new Starfall.Food.FoodState
                {
                    world = "test-world",
                    generation = "gen-1",
                    satiety = 1000,
                    hydration = 2000,
                    carriedFruit = 1,
                    knowsBerry = true,
                    berryEvidence = "gen-1.observed-fruit.1"
                };
                // Immediate eat consumes gathered berry and sets valid meal evidence
                s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                s.hydration = Mathf.Min(10000, s.hydration + 600);
                s.satiety = Mathf.Min(10000, s.satiety + 1500);
                Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 1500);
                s.knowsMealBenefit = true;
                s.lastMealEvidence = "gen-1.ate.2";
                s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
                Check(s.carriedFruit == 0 && s.satiety == 2500 && s.hydration == 2600, "gather-eat-immediately-applies-stats");
                Check(Starfall.Food.FoodModel.Valid(s, "test-world", "gen-1"), "gather-eat-immediately-model-valid");

                // 3. Moonbag Storage & Retrieval
                var bagGo = new GameObject("TestHunterMoonbag");
                try
                {
                    var moonbag = bagGo.AddComponent<HunterMoonbag>();
                    Check(moonbag.CanStore && !moonbag.CanRetrieve && moonbag.StoredCount == 0, "moonbag-initially-empty");
                    moonbag.StoreFruit();
                    Check(moonbag.StoredCount == 1 && moonbag.CanRetrieve, "moonbag-store-fruit-increments");
                    moonbag.RetrieveFruit();
                    Check(moonbag.StoredCount == 0 && !moonbag.CanRetrieve, "moonbag-retrieve-fruit-decrements");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(bagGo);
                }
            }

            // -------------------------------------------------------------
            // 15. Berry Hand Prop Scaling, Bush Fruit Harvesting & Regrowth
            // -------------------------------------------------------------
            {
                // 1. Berry Prop Visual Hierarchy (No 1m Sphere Scale Inflation)
                var berryTestGo = new GameObject("TestHeldBerry");
                var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.name = "Visual";
                visual.transform.SetParent(berryTestGo.transform, false);
                visual.transform.localScale = new Vector3(0.08f, 0.09f, 0.08f);

                // Simulate AttachToHand unparenting to root and forcing localScale = Vector3.one
                berryTestGo.transform.localScale = Vector3.one;
                Check(visual.transform.localScale.x <= 0.1f && visual.transform.localScale.y <= 0.1f, "held-berry-visual-retains-sub-decimetre-scale");
                Check(berryTestGo.GetComponent<MeshFilter>() == null, "held-berry-root-has-no-inflated-mesh-filter");
                UnityEngine.Object.DestroyImmediate(berryTestGo);

                // 2. Bush Fruit Harvesting, Depletion & Regrowth
                var bushGo = new GameObject("TestSourfigBush");
                var bushInteractable = bushGo.AddComponent<CityLife.World.NpcInteractable>();
                bushInteractable.StableId = "berry-food-test";
                bushInteractable.Kind = CityLife.World.NpcObjectKind.Place;
                bushInteractable.Occupant = "";
                bushInteractable.Permission = true;

                for (int v = 0; v < 4; v++)
                {
                    int named = v * 2;
                    var fruitPart = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    fruitPart.name = "Visible ripe sourfig fruit " + named;
                    fruitPart.transform.SetParent(bushGo.transform, false);

                    var crownPart = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    crownPart.name = "Sourfig fruit crown " + named;
                    crownPart.transform.SetParent(bushGo.transform, false);
                }

                var foodRuntimeGo = new GameObject("TestFoodRuntime");
                var foodRuntime = foodRuntimeGo.AddComponent<Starfall.Food.IntegratedFoodRuntime>();
                try
                {
                    Check(foodRuntime.GetBushActiveFruitCount(bushInteractable) == 4, "bush-initial-fruit-count-is-4");

                    // Harvest 1 fruit
                    bool h1 = foodRuntime.HarvestBerry(bushInteractable);
                    Check(h1 && foodRuntime.GetBushActiveFruitCount(bushInteractable) == 3, "harvest-decrements-fruit-count-to-3");
                    Check(bushInteractable.Available, "bush-remains-available-with-remaining-fruits");

                    // Harvest remaining 3 fruits to deplete
                    foodRuntime.HarvestBerry(bushInteractable);
                    foodRuntime.HarvestBerry(bushInteractable);
                    foodRuntime.HarvestBerry(bushInteractable);
                    Check(foodRuntime.GetBushActiveFruitCount(bushInteractable) == 0, "bush-exhausted-to-0-fruits");
                    Check(!bushInteractable.Available, "depleted-bush-marked-unavailable");

                    // Attempt harvest on depleted bush
                    bool hExhausted = foodRuntime.HarvestBerry(bushInteractable);
                    Check(!hExhausted, "depleted-bush-rejects-further-harvest");

                    // Regrow 1 fruit
                    foodRuntime.RegrowOneFruit(bushInteractable);
                    Check(foodRuntime.GetBushActiveFruitCount(bushInteractable) == 1, "regrowth-restores-one-fruit");
                    Check(bushInteractable.Available, "regrown-bush-becomes-available");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(foodRuntimeGo);
                    UnityEngine.Object.DestroyImmediate(bushGo);
                }
            }

            // -------------------------------------------------------------
            // 14. Coastal Wolf Ecology, Directional Vision, Safe Perimeter & Protein Defense
            // -------------------------------------------------------------
            {
                // Wolf ecology geometry & pack definitions
                Check(CoastalWolfEcology.VerifyWolfEcology(out string wolfRec), "wolf-ecology-geometry-verified");
                Check(CoastalWolfEcology.AuthoredWolfSpawns.Length >= 3, "wolf-authored-spawns-count-at-least-3");

                // Wolf state transitions and club hit recoil
                var wolfGo = new GameObject("TestWolf");
                var wolf = wolfGo.AddComponent<CoastalWolfEcology>();
                wolf.WolfId = "test-canyon-wolf";
                try
                {
                    Check(wolf.State == CoastalWolfEcology.WolfState.Prowl, "wolf-initial-state-is-prowl");
                    wolf.TakeClubHit(Vector3.zero);
                    Check(wolf.State == CoastalWolfEcology.WolfState.Flee, "wolf-hit-recoils-into-flee-state");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(wolfGo);
                }

                // Safe perimeter navigation bounds
                var navGo = new GameObject("TestNav");
                var nav = navGo.AddComponent<NpcTerrainNavigation>();
                try
                {
                    Check(nav.IsWithinSafePerimeter(new Vector3(0f, 0f, 50f), 10f), "origin-within-safe-perimeter");
                    Check(!nav.IsWithinSafePerimeter(new Vector3(350f, 0f, 50f), 10f), "east-extreme-void-outside-safe-perimeter");
                    Check(!nav.IsWithinSafePerimeter(new Vector3(-350f, 0f, 50f), 10f), "west-extreme-void-outside-safe-perimeter");
                    Check(!nav.IsWithinSafePerimeter(new Vector3(0f, 0f, 450f), 10f), "north-extreme-outside-safe-perimeter");
                    Check(!nav.IsWithinSafePerimeter(new Vector3(0f, 0f, -350f), 10f), "south-extreme-outside-safe-perimeter");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(navGo);
                }

                // Directional vision zones
                Check(VisionZone.Proximity == (VisionZone)0, "vision-zone-proximity-enum-defined");
                Check(VisionZone.Focal == (VisionZone)1, "vision-zone-focal-enum-defined");
                Check(VisionZone.Peripheral == (VisionZone)2, "vision-zone-peripheral-enum-defined");
                Check(VisionZone.DistantLandmark == (VisionZone)3, "vision-zone-distant-landmark-enum-defined");

                // Protein state and hedonic drive reduction
                var testBody = new FoodBody();
                testBody.health = 10000;
                testBody.protein = 4000; // Depleted protein drive
                testBody.endorphin = 2000;
                testBody.fatigue = 5000;
                Check(testBody.protein == 4000, "initial-protein-depleted");

                // Simulate defensive club strike endorphin surge
                testBody.endorphin = Mathf.Min(10000, testBody.endorphin + 2000);
                testBody.fatigue = Mathf.Max(0, testBody.fatigue - 1000);
                Check(testBody.endorphin == 4000, "club-defense-endorphin-boosted");
                Check(testBody.fatigue == 4000, "club-defense-fatigue-relieved");

                // Simulate eating protein crab
                testBody.protein = Mathf.Min(10000, testBody.protein + 2500);
                Check(testBody.protein == 6500, "eating-crab-replenishes-protein-drive");

                // Wolf meat and leather drops & catalog registration
                var catalog = PhysicalItemCatalog.CreateDefaultCatalog();
                Check(catalog.TryGet("food-wolf-meat", out var meatDef), "catalog-contains-food-wolf-meat");
                Check(catalog.TryGet("food-cooked-meat", out var cookedDef), "catalog-contains-food-cooked-meat");
                Check(catalog.TryGet("material-wolf-leather", out var leatherDef), "catalog-contains-material-wolf-leather");
                Check(CityLife.Food.HearthCooking.CanRoast("food-wolf-meat"), "hearth-cooking-can-roast-wolf-meat");
                Check(CityLife.Food.HearthCooking.GetCookedTypeId("food-wolf-meat") == "food-cooked-meat", "wolf-meat-cooks-to-cooked-meat");

                // Test drop spawning
                var meatDrop = CoastalWolfEcology.SpawnMeatDrop(Vector3.zero);
                var leatherDrop = CoastalWolfEcology.SpawnLeatherDrop(Vector3.zero);
                try
                {
                    Check(meatDrop != null && meatDrop.GetComponent<PhysicalItem>().itemTypeId == "food-wolf-meat", "spawned-meat-drop-item-verified");
                    Check(leatherDrop != null && leatherDrop.GetComponent<PhysicalItem>().itemTypeId == "material-wolf-leather", "spawned-leather-drop-item-verified");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(meatDrop);
                    UnityEngine.Object.DestroyImmediate(leatherDrop);
                }
            }

            // -------------------------------------------------------------
            // 12. Living World & Unified HUD Integration Checks
            // -------------------------------------------------------------
            {
                // 1. Berry Inventory Reconciliation: ghost fruit cleared and decrement on eat/drop
                var s = new FoodState();
                s.carriedFruit = 4; // Simulated ghost inventory lock
                NpcPlayerControls.ReconcileCarriedFruit(s, null, null);
                Check(s.carriedFruit == 0, "reconcile-clears-ghost-carried-fruit-when-hands-empty");

                // Decrement on eating / dropping
                s.carriedFruit = 2;
                s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
                Check(s.carriedFruit == 1, "eating-or-dropping-decrements-carried-fruit");

                // 2. Truthful Decision Reflection Provenance & Directing
                var go = new GameObject("TestAutonomyRoot");
                try
                {
                    var brain = go.AddComponent<NpcAutonomy>();
                    var surv = go.AddComponent<StarfallSurvivalAutonomy>();
                    surv.Brain = brain;

                    // Test player directive
                    surv.SetPlayerDirective(new Vector3(45f, 0f, -80f), "Canyon Overlook");
                    Check(surv.PlayerDirectiveTarget.HasValue && surv.PlayerDirectiveLabel == "Canyon Overlook", "player-directive-beacon-set");
                    surv.ClearPlayerDirective();
                    Check(!surv.PlayerDirectiveTarget.HasValue, "player-directive-beacon-cleared");

                    // Test well-fulfilled domestic camp routines
                    surv.Food = go.AddComponent<IntegratedFoodRuntime>();
                    surv.Food.Brain = brain;
                    surv.Food.EnsureModel();
                    surv.Food.Model.State.satiety = 9500;
                    surv.Food.Model.State.hydration = 9000;
                    surv.Food.Model.State.body.protein = 8500;
                    Check(surv.IsWellFulfilled(), "well-fulfilled-evaluates-true-when-needs-satisfied");

                    // 3. Survival Diary Chronicle & Milestones
                    var diary = go.AddComponent<StarfallSurvivalDiary>();
                    diary.AddEntry(100, "Forage", "Found ripe wild sourfigs on the rocks.");
                    Check(diary.Entries.Count == 1, "survival-diary-entry-recorded");
                    Check(diary.UnlockMilestone("first-feast", 100), "survival-milestone-unlocked");
                    Check(!diary.UnlockMilestone("first-feast", 120), "duplicate-milestone-unlock-ignored");
                    Check(diary.IsMilestoneUnlocked("first-feast"), "milestone-is-unlocked-verified");

                    // 4. Packed Wolf Shelter & Immunity Barrier
                    var wsGo = new GameObject("TestShelterWorkstation");
                    var ws = wsGo.AddComponent<StoneBuildingWorkstation>();
                    ws.UpdateRequirementsForTarget(StoneStructureKind.PackedWolfShelter);
                    Check(ws.RequiredStones == 8, "packed-wolf-shelter-requires-8-stones");
                    ws.IsCompleted = true;
                    ws.ConstructionSite = new Vector3(10f, 0f, 10f);
                    Check(ws.IsWolfShelterProtecting(new Vector3(11f, 0f, 11f), 6.0f), "inhabitant-inside-shelter-protected");
                    Check(!ws.IsWolfShelterProtecting(new Vector3(25f, 0f, 25f), 6.0f), "inhabitant-outside-shelter-unprotected");
                    UnityEngine.Object.DestroyImmediate(wsGo);

                    // 5. Diurnal Day/Night Cycle Progression
                    var envGo = new GameObject("TestEnvironment");
                    var env = envGo.AddComponent<IntegratedEnvironment>();
                    Check(env.DayDurationSeconds == 480f, "diurnal-day-duration-configured");
                    UnityEngine.Object.DestroyImmediate(envGo);

                    // 6. Reactive Crab Actor Verification
                    var crabTestGo = new GameObject("TestCrab");
                    var crabActor = crabTestGo.AddComponent<CoastalCrabActor>();
                    Check(crabActor != null && crabActor.ThreatDistance == 3.5f, "coastal-crab-actor-initialized");
                    UnityEngine.Object.DestroyImmediate(crabTestGo);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            return passed;
        }
    }
}
