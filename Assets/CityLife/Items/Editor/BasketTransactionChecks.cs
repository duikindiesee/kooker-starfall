using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using CityLife.World;

namespace CityLife.Items.Editor
{
    /// <summary>
    /// Production transactional store/retrieve and persistent container slot test suite.
    /// Verifies:
    /// 1. Monotonic request ID sequence allocation and bounded ledger capacity.
    /// 2. Deterministic first-vacant slot allocation, stable container slots, and stable holes on retrieve without reshuffling.
    /// 3. Immediate duplicate replay resolution from semantic PhysicalModel receipts before spatial/reach/LOS checks.
    /// 4. Conflicting request denial preserving original receipt and code.
    /// 5. Single accessible root navigation for nested interactables across reach, line-of-sight, world scope, and permission gates.
    /// 6. Strict capacity limits: container slot capacity, direct volume capacity, direct mass capacity, and recursive ancestor mass capacity.
    /// 7. Containment cycle detection and immediate refusal.
    /// 8. Two-phase prepared transition commit, revision bump invalidation, and cancellation.
    /// 9. Exact atomic rollback of runtime physical properties on execution or commit failure without disturbing unrelated bodies.
    /// 10. Backward-compatible in-memory migration of v1 legacy saves by ordinal item ID with SHA-256 checksum verification and untouched disk files.
    /// 11. Strict validation and refusal of malformed slot configurations (out-of-bounds, duplicate occupancy, inconsistent assignment, or non-Stored slot values).
    /// 12. 50 Store/Retrieve cycles without state drift or mass leakage.
    /// 13. 5 sequential save/restarts with durable denial replay and monotonic sequence allocation.
    /// 14. Authoritative bounded-ledger-full outcome mapping and refusal through real physical adapter without mutation or local authority.
    /// </summary>
    internal static class BasketTransactionChecks
    {
        public static void Run(
            Scene scene,
            PhysicsScene physics,
            Func<string, GameObject> createGo,
            Action<bool, string, string> checkRaw,
            List<string> passed,
            string worldId,
            string genId,
            string agentId,
            GameObject actorGo,
            GameObject handGo)
        {
            void check(bool condition, string name, string diagnostic = null)
            {
                checkRaw(condition, name, diagnostic);
            }

            PhysicalSavePayload ClonePayload(PhysicalSavePayload src)
            {
                if (src == null) return null;
                string json = JsonUtility.ToJson(src);
                return JsonUtility.FromJson<PhysicalSavePayload>(json);
            }

            var ownedObjects = new List<GameObject>();

            GameObject CreateTrackedGo(string name)
            {
                var go = createGo(name);
                ownedObjects.Add(go);
                return go;
            }

            try
            {
                // =============================================================
                // Section 1: Model Setup & Definitions
                // =============================================================
                var model = new ItemModel(worldId, genId);
                var authority = new BasicItemActionAuthority { allowContainerAccess = true, allowPlacement = true };

                var basketDef = new ItemDefinition
                {
                    itemTypeId = "test-basket",
                    dimensions = new PhysicalDimensions(0.3f, 0.25f, 0.3f),
                    massKg = 2.0f,
                    isContainer = true,
                    maxContainedSlots = 4,
                    maxContainedVolumeM3 = 0.08f,
                    maxContainedMassKg = 15.0f
                };
                check(model.RegisterDefinition(basketDef), "basket-tx-register-basket-def");

                var chestDef = new ItemDefinition
                {
                    itemTypeId = "test-chest",
                    dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                    massKg = 5.0f,
                    isContainer = true,
                    maxContainedSlots = 6,
                    maxContainedVolumeM3 = 0.5f,
                    maxContainedMassKg = 40.0f
                };
                check(model.RegisterDefinition(chestDef), "basket-tx-register-chest-def");

                var appleDef = new ItemDefinition
                {
                    itemTypeId = "test-apple",
                    dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                    massKg = 0.2f,
                    isContainer = false
                };
                check(model.RegisterDefinition(appleDef), "basket-tx-register-apple-def");

                var pearDef = new ItemDefinition
                {
                    itemTypeId = "test-pear",
                    dimensions = new PhysicalDimensions(0.09f, 0.09f, 0.09f),
                    massKg = 0.25f,
                    isContainer = false
                };
                check(model.RegisterDefinition(pearDef), "basket-tx-register-pear-def");

                var heavyDef = new ItemDefinition
                {
                    itemTypeId = "test-heavy-rock",
                    dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                    massKg = 20.0f,
                    isContainer = false
                };
                check(model.RegisterDefinition(heavyDef), "basket-tx-register-heavy-def");

                var bulkyDef = new ItemDefinition
                {
                    itemTypeId = "test-bulky-block",
                    dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                    massKg = 1.0f,
                    isContainer = false
                };
                check(model.RegisterDefinition(bulkyDef), "basket-tx-register-bulky-def");

                model.SetActorCarryLimits(agentId, new ActorCarryLimits(25.0f, 2));

                // =============================================================
                // Section 2: Monotonic Request Sequence Allocation
                // =============================================================
                check(model.TryAllocateNextRequestId(out int reqId1), "basket-tx-allocate-req-1");
                check(reqId1 == 1, "basket-tx-first-req-is-1", $"actual={reqId1}");
                check(model.ResumeRequestSequence(100), "basket-tx-resume-req-seq");
                check(model.TryAllocateNextRequestId(out int reqId2), "basket-tx-allocate-req-after-resume");
                check(reqId2 == 101, "basket-tx-resumed-monotonic-id", $"actual={reqId2}");

                // =============================================================
                // Section 3: Prepared Transitions, Stable Slots, & Stable Holes
                // =============================================================
                check(model.RegisterItem("basket-01", "test-basket", ItemLocationKind.Free, new Vector3(0, 0, 0), Quaternion.identity), "basket-tx-reg-basket");
                check(model.RegisterItem("item-apple-1", "test-apple", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-apple-1");
                check(model.RegisterItem("item-apple-2", "test-apple", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-apple-2");
                check(model.RegisterItem("item-apple-3", "test-apple", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-apple-3");
                check(model.RegisterItem("item-pear-1", "test-pear", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-pear-1");

                // Actor picks up item-apple-1
                check(model.TryAllocateNextRequestId(out int pickReq1), "basket-tx-alloc-pick-1");
                var pickRes1 = model.Execute(worldId, genId, new ItemActionRequest { requestId = pickReq1, action = ItemActionKind.Pickup, actorId = agentId, itemId = "item-apple-1" }, authority);
                check(pickRes1.success, "basket-tx-pick-apple-1");

                // Authoritative first vacant slot allocation for slot 0
                check(model.TryAllocateNextRequestId(out int storeReq1), "basket-tx-alloc-store-1");
                var storeReqObj1 = new ItemActionRequest { requestId = storeReq1, action = ItemActionKind.Store, actorId = agentId, itemId = "item-apple-1", targetId = "basket-01" };
                check(model.TryPrepareTransition(worldId, genId, storeReqObj1, authority, out var token1, out var reject1), "basket-tx-prep-store-1", reject1);
                check(token1.AssignedSlot == 0, "basket-tx-token-assigned-slot-0", $"slot={token1.AssignedSlot}");
                check(model.TryCommitTransition(token1, out var receipt1), "basket-tx-commit-store-1");
                check(receipt1.success && receipt1.code == "stored-in-container", "basket-tx-receipt-store-1");
                check(model.TryGetItemContainerSlot("item-apple-1", out int slot1) && slot1 == 0, "basket-tx-item-slot-is-0");

                // Actor picks up item-apple-2
                check(model.TryAllocateNextRequestId(out int pickReq2), "basket-tx-alloc-pick-2");
                var pickRes2 = model.Execute(worldId, genId, new ItemActionRequest { requestId = pickReq2, action = ItemActionKind.Pickup, actorId = agentId, itemId = "item-apple-2" }, authority);
                check(pickRes2.success, "basket-tx-pick-apple-2");

                // Store second item -> allocates slot 1
                check(model.TryAllocateNextRequestId(out int storeReq2), "basket-tx-alloc-store-2");
                var storeReqObj2 = new ItemActionRequest { requestId = storeReq2, action = ItemActionKind.Store, actorId = agentId, itemId = "item-apple-2", targetId = "basket-01" };
                check(model.TryPrepareTransition(worldId, genId, storeReqObj2, authority, out var token2, out var reject2), "basket-tx-prep-store-2", reject2);
                check(token2.AssignedSlot == 1, "basket-tx-token-assigned-slot-1", $"slot={token2.AssignedSlot}");
                check(model.TryCommitTransition(token2, out var receipt2), "basket-tx-commit-store-2");
                check(model.TryGetItemContainerSlot("item-apple-2", out int slot2) && slot2 == 1, "basket-tx-item-slot-is-1");

                // Actor picks up item-apple-3
                check(model.TryAllocateNextRequestId(out int pickReq3), "basket-tx-alloc-pick-3");
                var pickRes3 = model.Execute(worldId, genId, new ItemActionRequest { requestId = pickReq3, action = ItemActionKind.Pickup, actorId = agentId, itemId = "item-apple-3" }, authority);
                check(pickRes3.success, "basket-tx-pick-apple-3");

                // Store third item -> allocates slot 2
                check(model.TryAllocateNextRequestId(out int storeReq3), "basket-tx-alloc-store-3");
                var storeReqObj3 = new ItemActionRequest { requestId = storeReq3, action = ItemActionKind.Store, actorId = agentId, itemId = "item-apple-3", targetId = "basket-01" };
                check(model.TryPrepareTransition(worldId, genId, storeReqObj3, authority, out var token3, out var reject3), "basket-tx-prep-store-3", reject3);
                check(token3.AssignedSlot == 2, "basket-tx-token-assigned-slot-2", $"slot={token3.AssignedSlot}");
                check(model.TryCommitTransition(token3, out _), "basket-tx-commit-store-3");

                // Retrieve middle item ("item-apple-2" at slot 1)
                check(model.TryAllocateNextRequestId(out int retReq1), "basket-tx-alloc-ret-1");
                var retReqObj1 = new ItemActionRequest { requestId = retReq1, action = ItemActionKind.Retrieve, actorId = agentId, itemId = "item-apple-2", targetId = "basket-01" };
                check(model.TryPrepareTransition(worldId, genId, retReqObj1, authority, out var retToken1, out var retDeny1), "basket-tx-prep-ret-1", retDeny1);
                check(retToken1.AssignedSlot == 1, "basket-tx-ret-token-recorded-slot-1");
                check(model.TryCommitTransition(retToken1, out var retReceipt1), "basket-tx-commit-ret-1");
                check(retReceipt1.success && retReceipt1.code == "retrieved-from-container", "basket-tx-receipt-ret-1");

                // Verify non-stored item clears slot to -1
                check(model.TryGetItem("item-apple-2", out var retAppleSnap), "basket-tx-get-ret-apple");
                check(retAppleSnap.containerSlot == -1, "basket-tx-retrieved-slot-is-neg1", $"actual={retAppleSnap.containerSlot}");
                check(retAppleSnap.location == ItemLocationKind.Carried, "basket-tx-retrieved-location-is-carried");

                // STABLE HOLES CHECK:
                // Occupants must be slot 0 -> "item-apple-1", slot 2 -> "item-apple-3", slot 1 is vacant!
                var occupants = model.GetContainerSlotOccupants("basket-01");
                check(occupants.ContainsKey(0) && occupants[0] == "item-apple-1", "basket-tx-slot-0-remains-apple-1");
                check(!occupants.ContainsKey(1), "basket-tx-slot-1-is-vacant-hole");
                check(occupants.ContainsKey(2) && occupants[2] == "item-apple-3", "basket-tx-slot-2-remains-apple-3");

                // Drop item-apple-2 so hand is free to pick up pear
                check(model.TryAllocateNextRequestId(out int dropReq1), "basket-tx-alloc-drop-1");
                var dropRes1 = model.Execute(worldId, genId, new ItemActionRequest { requestId = dropReq1, action = ItemActionKind.Drop, actorId = agentId, itemId = "item-apple-2", position = new Vector3(10, 0, 10), rotation = Quaternion.identity }, authority);
                check(dropRes1.success, "basket-tx-drop-apple-2");

                // Pick up item-pear-1
                check(model.TryAllocateNextRequestId(out int pickReqPear), "basket-tx-alloc-pick-pear");
                var pickResPear = model.Execute(worldId, genId, new ItemActionRequest { requestId = pickReqPear, action = ItemActionKind.Pickup, actorId = agentId, itemId = "item-pear-1" }, authority);
                check(pickResPear.success, "basket-tx-pick-pear-1");

                // Store new item -> must fill hole at slot 1 without reshuffling existing slots
                check(model.TryAllocateNextRequestId(out int storeReq4), "basket-tx-alloc-store-4");
                var storeReqObj4 = new ItemActionRequest { requestId = storeReq4, action = ItemActionKind.Store, actorId = agentId, itemId = "item-pear-1", targetId = "basket-01" };
                check(model.TryPrepareTransition(worldId, genId, storeReqObj4, authority, out var token4, out var reject4), "basket-tx-prep-store-4", reject4);
                check(token4.AssignedSlot == 1, "basket-tx-fills-vacant-hole-at-slot-1", $"slot={token4.AssignedSlot}");
                check(model.TryCommitTransition(token4, out _), "basket-tx-commit-store-4");
                check(model.TryGetItemContainerSlot("item-pear-1", out int pearSlot) && pearSlot == 1, "basket-tx-pear-stored-in-slot-1");
                check(model.TryGetItemContainerSlot("item-apple-1", out int apple1Slot) && apple1Slot == 0, "basket-tx-apple-1-unmoved");
                check(model.TryGetItemContainerSlot("item-apple-3", out int apple3Slot) && apple3Slot == 2, "basket-tx-apple-3-unmoved");

                // =============================================================
                // Section 4: Capacity & Cycle Refusals
                // =============================================================
                // 1. Slot capacity refusal: basket has 4 slots (occupied: 0, 1, 2)
                check(model.RegisterItem("item-fill-4", "test-apple", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-fill-4");
                check(model.TryAllocateNextRequestId(out int pickFill4), "basket-tx-alloc-pick-fill-4");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = pickFill4, action = ItemActionKind.Pickup, actorId = agentId, itemId = "item-fill-4" }, authority).success, "basket-tx-pick-fill-4");

                check(model.TryAllocateNextRequestId(out int fillReq4), "basket-tx-alloc-fill-4");
                check(model.TryPrepareTransition(worldId, genId, new ItemActionRequest { requestId = fillReq4, action = ItemActionKind.Store, actorId = agentId, itemId = "item-fill-4", targetId = "basket-01" }, authority, out var fillTok4, out _), "basket-tx-fill-slot-3");
                check(fillTok4.AssignedSlot == 3, "basket-tx-assigned-last-slot-3");
                check(model.TryCommitTransition(fillTok4, out _), "basket-tx-commit-slot-3");

                // Basket now has all 4 slots full
                check(model.RegisterItem("item-overflow", "test-apple", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-overflow");
                check(model.TryAllocateNextRequestId(out int pickOverReq), "basket-tx-alloc-pick-over");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = pickOverReq, action = ItemActionKind.Pickup, actorId = agentId, itemId = "item-overflow" }, authority).success, "basket-tx-pick-overflow");

                check(model.TryAllocateNextRequestId(out int overSlotReq), "basket-tx-alloc-overslot");
                bool overSlotPrepared = model.TryPrepareTransition(worldId, genId, new ItemActionRequest { requestId = overSlotReq, action = ItemActionKind.Store, actorId = agentId, itemId = "item-overflow", targetId = "basket-01" }, authority, out _, out string overSlotDeny);
                check(!overSlotPrepared && overSlotDeny == "container-slot-capacity-exceeded", "basket-tx-reject-slot-overflow", overSlotDeny);

                // Drop item-overflow
                check(model.TryAllocateNextRequestId(out int dropOverReq), "basket-tx-alloc-drop-over");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = dropOverReq, action = ItemActionKind.Drop, actorId = agentId, itemId = "item-overflow", position = new Vector3(20, 0, 20), rotation = Quaternion.identity }, authority).success, "basket-tx-drop-overflow");

                // 2. Mass capacity refusal
                check(model.RegisterItem("item-heavy", "test-heavy-rock", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-heavy");
                check(model.TryAllocateNextRequestId(out int pickHeavyReq), "basket-tx-alloc-pick-heavy");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = pickHeavyReq, action = ItemActionKind.Pickup, actorId = agentId, itemId = "item-heavy" }, authority).success, "basket-tx-pick-heavy");

                check(model.RegisterItem("basket-fresh", "test-basket", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-fresh-basket");
                check(model.TryAllocateNextRequestId(out int heavyReq2), "basket-tx-alloc-heavy-2");
                bool heavyPrepared = model.TryPrepareTransition(worldId, genId, new ItemActionRequest { requestId = heavyReq2, action = ItemActionKind.Store, actorId = agentId, itemId = "item-heavy", targetId = "basket-fresh" }, authority, out _, out string heavyDeny);
                check(!heavyPrepared && heavyDeny == "container-mass-capacity-exceeded", "basket-tx-reject-mass-capacity", heavyDeny);

                // Drop item-heavy
                check(model.TryAllocateNextRequestId(out int dropHeavyReq), "basket-tx-alloc-drop-heavy");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = dropHeavyReq, action = ItemActionKind.Drop, actorId = agentId, itemId = "item-heavy", position = new Vector3(30, 0, 30), rotation = Quaternion.identity }, authority).success, "basket-tx-drop-heavy");

                // 3. Volume capacity refusal
                check(model.RegisterItem("item-bulky", "test-bulky-block", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-bulky");
                check(model.TryAllocateNextRequestId(out int pickBulkyReq), "basket-tx-alloc-pick-bulky");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = pickBulkyReq, action = ItemActionKind.Pickup, actorId = agentId, itemId = "item-bulky" }, authority).success, "basket-tx-pick-bulky");

                check(model.TryAllocateNextRequestId(out int bulkyReq), "basket-tx-alloc-bulky");
                bool bulkyPrepared = model.TryPrepareTransition(worldId, genId, new ItemActionRequest { requestId = bulkyReq, action = ItemActionKind.Store, actorId = agentId, itemId = "item-bulky", targetId = "basket-fresh" }, authority, out _, out string bulkyDeny);
                check(!bulkyPrepared && bulkyDeny == "container-volume-capacity-exceeded", "basket-tx-reject-volume-capacity", bulkyDeny);

                // Drop item-bulky
                check(model.TryAllocateNextRequestId(out int dropBulkyReq), "basket-tx-alloc-drop-bulky");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = dropBulkyReq, action = ItemActionKind.Drop, actorId = agentId, itemId = "item-bulky", position = new Vector3(40, 0, 40), rotation = Quaternion.identity }, authority).success, "basket-tx-drop-bulky");

                // 4. Containment cycle refusal
                check(model.RegisterItem("chest-01", "test-chest", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-chest");
                check(model.RegisterItem("basket-in-chest", "test-basket", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-basket-in-chest");

                // Pick up basket-in-chest and store in chest-01
                check(model.TryAllocateNextRequestId(out int pickNestReq), "basket-tx-alloc-pick-nest");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = pickNestReq, action = ItemActionKind.Pickup, actorId = agentId, itemId = "basket-in-chest" }, authority).success, "basket-tx-pick-nest");

                check(model.TryAllocateNextRequestId(out int nestReq), "basket-tx-alloc-nest");
                check(model.TryPrepareTransition(worldId, genId, new ItemActionRequest { requestId = nestReq, action = ItemActionKind.Store, actorId = agentId, itemId = "basket-in-chest", targetId = "chest-01" }, authority, out var nestTok, out _), "basket-tx-prep-nest");
                check(model.TryCommitTransition(nestTok, out _), "basket-tx-commit-nest");

                // Register chest-carried
                check(model.RegisterItem("chest-carried", "test-chest", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-chest-carried");

                // Pick up basket-fresh and store in chest-carried
                check(model.TryAllocateNextRequestId(out int pickBasketFreshReq), "basket-tx-alloc-pick-basket-fresh");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = pickBasketFreshReq, action = ItemActionKind.Pickup, actorId = agentId, itemId = "basket-fresh" }, authority).success, "basket-tx-pick-basket-fresh");

                check(model.TryAllocateNextRequestId(out int nestReq2), "basket-tx-alloc-nest-2");
                check(model.TryPrepareTransition(worldId, genId, new ItemActionRequest { requestId = nestReq2, action = ItemActionKind.Store, actorId = agentId, itemId = "basket-fresh", targetId = "chest-carried" }, authority, out var nestTok2, out _), "basket-tx-prep-nest-2");
                check(model.TryCommitTransition(nestTok2, out _), "basket-tx-commit-nest-2");

                // Pick up chest-carried (now containing basket-fresh)
                check(model.TryAllocateNextRequestId(out int pickChestCarried), "basket-tx-alloc-pick-chest-carried");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = pickChestCarried, action = ItemActionKind.Pickup, actorId = agentId, itemId = "chest-carried" }, authority).success, "basket-tx-pick-chest-carried");

                // Try to store chest-carried in basket-fresh -> Cycle!
                check(model.TryAllocateNextRequestId(out int cycleReq), "basket-tx-alloc-cycle");
                bool cyclePrepared = model.TryPrepareTransition(worldId, genId, new ItemActionRequest { requestId = cycleReq, action = ItemActionKind.Store, actorId = agentId, itemId = "chest-carried", targetId = "basket-fresh" }, authority, out _, out string cycleDeny);
                check(!cyclePrepared && cycleDeny == "containment-cycle-detected", "basket-tx-reject-cycle", cycleDeny);

                // Drop chest-carried
                check(model.TryAllocateNextRequestId(out int dropChestCarriedReq), "basket-tx-alloc-drop-chest-carried");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = dropChestCarriedReq, action = ItemActionKind.Drop, actorId = agentId, itemId = "chest-carried", position = new Vector3(50, 0, 50), rotation = Quaternion.identity }, authority).success, "basket-tx-drop-chest-carried");

                // =============================================================
                // Section 5: Revision Invalidation & Commit Refusal
                // =============================================================
                check(model.RegisterItem("item-gem", "test-apple", ItemLocationKind.Free, Vector3.zero, Quaternion.identity), "basket-tx-reg-gem");
                check(model.TryAllocateNextRequestId(out int pickGemReq), "basket-tx-alloc-pick-gem");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = pickGemReq, action = ItemActionKind.Pickup, actorId = agentId, itemId = "item-gem" }, authority).success, "basket-tx-pick-gem");

                check(model.TryAllocateNextRequestId(out int revReq), "basket-tx-alloc-rev");
                check(model.TryPrepareTransition(worldId, genId, new ItemActionRequest { requestId = revReq, action = ItemActionKind.Store, actorId = agentId, itemId = "item-gem", targetId = "basket-fresh" }, authority, out var revToken, out _), "basket-tx-prep-rev-tok");

                // Mutate model revision via AdvanceTick
                long preTickRev = model.Revision;
                model.AdvanceTick();
                check(model.Revision > preTickRev, "basket-tx-advance-tick-bumps-revision");

                // Commit must fail due to revision mismatch
                bool revCommit = model.TryCommitTransition(revToken, out var revFailReceipt);
                check(!revCommit && revFailReceipt.requestId == 0 && !revFailReceipt.success, "basket-tx-commit-fails-on-revision-mismatch");
                check(model.TryGetItem("item-gem", out var gemSnap) && gemSnap.location == ItemLocationKind.Carried, "basket-tx-gem-remained-carried");

                // Drop item-gem
                check(model.TryAllocateNextRequestId(out int dropGemReq), "basket-tx-alloc-drop-gem");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = dropGemReq, action = ItemActionKind.Drop, actorId = agentId, itemId = "item-gem", position = new Vector3(60, 0, 60), rotation = Quaternion.identity }, authority).success, "basket-tx-drop-gem");

                // =============================================================
                // Section 6: Runtime NpcActionApi Integration & Rollback
                // =============================================================
                var rtBasketGo = CreateTrackedGo("rt-basket");
                rtBasketGo.transform.position = actorGo.transform.position + new Vector3(0.4f, 0.2f, 0.4f);
                var rtBasketCol = rtBasketGo.AddComponent<BoxCollider>();
                rtBasketCol.size = new Vector3(0.3f, 0.25f, 0.3f);
                var rtBasketRb = rtBasketGo.AddComponent<Rigidbody>();
                rtBasketRb.mass = 2.0f;
                var rtBasketPhys = rtBasketGo.AddComponent<PhysicalItem>();
                rtBasketPhys.itemId = "live-basket";
                rtBasketPhys.itemTypeId = "test-basket";
                rtBasketPhys.massKg = 2.0f;
                rtBasketPhys.dimensions = new PhysicalDimensions(0.3f, 0.25f, 0.3f);
                var rtBasketInteractable = rtBasketGo.AddComponent<NpcInteractable>();
                rtBasketInteractable.StableId = "live-basket";
                rtBasketInteractable.Kind = NpcObjectKind.Item;
                rtBasketInteractable.Permission = true;
                rtBasketInteractable.WorldId = worldId;
                var rtBasketApproachGo = CreateTrackedGo("rt-basket-approach");
                rtBasketApproachGo.transform.position = rtBasketGo.transform.position;
                rtBasketApproachGo.transform.SetParent(rtBasketGo.transform, true);
                rtBasketInteractable.Approach = rtBasketApproachGo.transform;
                rtBasketPhys.Bind(model, worldId, genId);

                var rtAppleGo = CreateTrackedGo("rt-apple");
                rtAppleGo.transform.position = actorGo.transform.position + new Vector3(0.1f, 0.2f, 0.1f);
                var rtAppleCol = rtAppleGo.AddComponent<BoxCollider>();
                rtAppleCol.size = new Vector3(0.08f, 0.08f, 0.08f);
                var rtAppleRb = rtAppleGo.AddComponent<Rigidbody>();
                rtAppleRb.mass = 0.2f;
                var rtApplePhys = rtAppleGo.AddComponent<PhysicalItem>();
                rtApplePhys.itemId = "live-apple";
                rtApplePhys.itemTypeId = "test-apple";
                rtApplePhys.massKg = 0.2f;
                rtApplePhys.dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f);
                var rtAppleInteractable = rtAppleGo.AddComponent<NpcInteractable>();
                rtAppleInteractable.StableId = "live-apple";
                rtAppleInteractable.Kind = NpcObjectKind.Item;
                rtAppleInteractable.Permission = true;
                rtAppleInteractable.WorldId = worldId;
                rtApplePhys.Bind(model, worldId, genId);

                // Unrelated body for isolation testing
                var rtOtherGo = CreateTrackedGo("rt-unrelated-body");
                rtOtherGo.transform.position = actorGo.transform.position + new Vector3(2.5f, 0.5f, 2.5f);
                var rtOtherCol = rtOtherGo.AddComponent<BoxCollider>();
                var rtOtherRb = rtOtherGo.AddComponent<Rigidbody>();
                rtOtherRb.mass = 1.0f;
                Vector3 otherInitialLinVel = new Vector3(1.2f, -0.5f, 0.8f);
                Vector3 otherInitialAngVel = new Vector3(0.3f, 1.1f, -0.4f);
                rtOtherRb.linearVelocity = otherInitialLinVel;
                rtOtherRb.angularVelocity = otherInitialAngVel;
                var rtOtherPhys = rtOtherGo.AddComponent<PhysicalItem>();
                rtOtherPhys.itemId = "live-other";
                rtOtherPhys.itemTypeId = "test-apple";
                rtOtherPhys.massKg = 1.0f;
                rtOtherPhys.dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f);
                var rtOtherInteractable = rtOtherGo.AddComponent<NpcInteractable>();
                rtOtherInteractable.StableId = "live-other";
                rtOtherInteractable.Kind = NpcObjectKind.Item;
                rtOtherInteractable.Permission = true;
                rtOtherInteractable.WorldId = worldId;
                rtOtherPhys.Bind(model, worldId, genId);

                Vector3 otherInitialPos = rtOtherGo.transform.position;
                Quaternion otherInitialRot = rtOtherGo.transform.rotation;

                // Register live items into authoritative model as Free
                check(model.RegisterItem("live-basket", "test-basket", ItemLocationKind.Free, rtBasketGo.transform.position, rtBasketGo.transform.rotation), "basket-tx-reg-live-basket");
                check(model.RegisterItem("live-apple", "test-apple", ItemLocationKind.Free, rtAppleGo.transform.position, rtAppleGo.transform.rotation), "basket-tx-reg-live-apple");
                check(model.RegisterItem("live-other", "test-apple", ItemLocationKind.Free, rtOtherGo.transform.position, rtOtherGo.transform.rotation), "basket-tx-reg-live-other");

                // Put live-apple in actor hand in model
                check(model.TryAllocateNextRequestId(out int pickLiveAppleReq), "basket-tx-alloc-pick-live-apple");
                check(model.Execute(worldId, genId, new ItemActionRequest { requestId = pickLiveAppleReq, action = ItemActionKind.Pickup, actorId = agentId, itemId = "live-apple" }, authority).success, "basket-tx-pick-live-apple");

                var api = new NpcActionApi(agentId, worldId, actorGo.transform, handGo.transform, new[] { rtBasketInteractable, rtAppleInteractable, rtOtherInteractable });
                api.PhysicalModel = model;
                api.PhysicalAuthority = authority;

                // Put apple in hand in Unity scene
                rtApplePhys.AttachToHand(handGo.transform);
                rtAppleInteractable.HeldBy = agentId;
                check(api.RestoreHeld(rtAppleInteractable), "basket-tx-restore-held-apple");

                // 1. Missing-hand retrieval refusal check:
                var apiNoHand = new NpcActionApi(agentId, worldId, actorGo.transform, null, new[] { rtBasketInteractable, rtAppleInteractable, rtOtherInteractable });
                apiNoHand.PhysicalModel = model;
                apiNoHand.PhysicalAuthority = authority;
                check(model.TryAllocateNextRequestId(out int noHandReq), "basket-tx-alloc-nohand-req");
                var noHandRes = apiNoHand.Execute(noHandReq, NpcActionKind.Retrieve, "live-apple", "live-basket");
                check(!noHandRes.success && noHandRes.code == "item-unavailable", "basket-tx-reject-missing-hand", noHandRes.code);
                var noHandDup = apiNoHand.Execute(noHandReq, NpcActionKind.Retrieve, "live-apple", "live-basket");
                check(noHandDup.duplicate && !noHandDup.success && noHandDup.code == "item-unavailable", "basket-tx-missing-hand-refusal-replay");

                // 2. Item-permission refusal check:
                rtAppleInteractable.Permission = false;
                check(model.TryAllocateNextRequestId(out int noPermReq), "basket-tx-alloc-noperm-req");
                var noPermRes = api.Execute(noPermReq, NpcActionKind.Store, "live-apple", "live-basket");
                check(!noPermRes.success && noPermRes.code == "permission-denied", "basket-tx-reject-item-permission", noPermRes.code);
                var noPermDup = api.Execute(noPermReq, NpcActionKind.Store, "live-apple", "live-basket");
                check(noPermDup.duplicate && !noPermDup.success && noPermDup.code == "permission-denied", "basket-tx-item-permission-refusal-replay");
                rtAppleInteractable.Permission = true;

                // 3. Test failpoint after runtime apply rollback:
                Transform applePreActionParent = rtAppleGo.transform.parent;
                Vector3 applePreActionPos = rtAppleGo.transform.position;
                Quaternion applePreActionRot = rtAppleGo.transform.rotation;
                Vector3 applePreActionScale = rtAppleGo.transform.lossyScale;
                Transform applePreActionHand = rtApplePhys.CarriedHand;
                bool applePreActionCarried = rtApplePhys.IsCarried;
                bool applePreActionStored = rtApplePhys.IsStored;
                string applePreActionContainer = rtApplePhys.BoundContainerItemId;
                bool applePreActionColEnabled = rtAppleCol.enabled;
                bool applePreActionColTrigger = rtAppleCol.isTrigger;
                bool applePreActionRbKinematic = rtAppleRb.isKinematic;
                bool applePreActionRbGravity = rtAppleRb.useGravity;
                NpcInteractable preActionHeld = api.Held;
                string preActionHeldBy = rtAppleInteractable.HeldBy;

                try
                {
                    api.SetFailAfterPhysicalApplyForTesting(true);
                    check(model.TryAllocateNextRequestId(out int failpointReq), "basket-tx-alloc-failpoint-req");
                    var failpointRes = api.Execute(failpointReq, NpcActionKind.Store, "live-apple", "live-basket");
                    check(!failpointRes.success && failpointRes.code == "physical-application-failed", "basket-tx-failpoint-rejected", failpointRes.code);

                    // Verify model state remained unchanged and carried
                    check(model.TryGetItem("live-apple", out var postFailModelSnap) &&
                          postFailModelSnap.location == ItemLocationKind.Carried &&
                          postFailModelSnap.holderActorId == agentId &&
                          postFailModelSnap.containerSlot == -1 &&
                          string.IsNullOrEmpty(postFailModelSnap.containerItemId), "basket-tx-failpoint-model-unchanged-carried");

                    // Verify exact rollback of physical item, interactable, and API held state
                    check(api.Held == preActionHeld, "basket-tx-failpoint-apple-still-held");
                    check(rtAppleInteractable.HeldBy == preActionHeldBy, "basket-tx-failpoint-apple-heldby-restored");
                    check(rtApplePhys.IsCarried == applePreActionCarried && rtApplePhys.IsCarried, "basket-tx-failpoint-apple-is-carried");
                    check(rtApplePhys.CarriedHand == applePreActionHand, "basket-tx-failpoint-apple-carried-hand-restored");
                    check(rtApplePhys.IsStored == applePreActionStored && !rtApplePhys.IsStored, "basket-tx-failpoint-apple-not-stored");
                    check(rtApplePhys.BoundContainerItemId == applePreActionContainer, "basket-tx-failpoint-apple-bound-container-restored");
                    check(rtAppleCol.enabled == applePreActionColEnabled && rtAppleCol.isTrigger == applePreActionColTrigger, "basket-tx-failpoint-apple-collider-restored");
                    check(rtAppleGo.transform.parent == applePreActionParent, "basket-tx-failpoint-apple-parent-restored");
                    check(Vector3.Distance(rtAppleGo.transform.position, applePreActionPos) < 0.0001f, "basket-tx-failpoint-apple-pos-restored");
                    check(Quaternion.Angle(rtAppleGo.transform.rotation, applePreActionRot) < 0.01f, "basket-tx-failpoint-apple-rot-restored");
                    check(Vector3.Distance(rtAppleGo.transform.lossyScale, applePreActionScale) < 0.001f, "basket-tx-failpoint-apple-scale-restored");
                    check(rtAppleRb.isKinematic == applePreActionRbKinematic && rtAppleRb.useGravity == applePreActionRbGravity, "basket-tx-failpoint-apple-rigidbody-restored");

                    // Verify unrelated body is completely unaffected in pose and velocities
                    check(Vector3.Distance(rtOtherGo.transform.position, otherInitialPos) < 0.0001f, "basket-tx-failpoint-unrelated-unmoved");
                    check(Quaternion.Angle(rtOtherGo.transform.rotation, otherInitialRot) < 0.01f, "basket-tx-failpoint-unrelated-unrotated");
                    check(Vector3.Distance(rtOtherRb.linearVelocity, otherInitialLinVel) < 0.0001f, "basket-tx-failpoint-unrelated-linvel-preserved");
                    check(Vector3.Distance(rtOtherRb.angularVelocity, otherInitialAngVel) < 0.0001f, "basket-tx-failpoint-unrelated-angvel-preserved");
                }
                finally
                {
                    api.SetFailAfterPhysicalApplyForTesting(false);
                }

                // 4. Execute Store through NpcActionApi
                check(model.TryAllocateNextRequestId(out int apiStoreReq), "basket-tx-alloc-api-store");
                var storeResult = api.Execute(apiStoreReq, NpcActionKind.Store, "live-apple", "live-basket");
                check(storeResult.success && storeResult.code == "stored-in-container", "basket-tx-api-store-success", storeResult.code);
                check(api.Held == null, "basket-tx-held-cleared-after-store");
                check(rtApplePhys.IsStored, "basket-tx-apple-phys-is-stored");
                check(!rtAppleCol.enabled, "basket-tx-apple-col-disabled-when-stored");
                check(rtAppleGo.transform.parent == rtBasketGo.transform, "basket-tx-apple-parented-to-basket");

                // Duplicate Replay Check (idempotent, immediate from receipt)
                var dupResult = api.Execute(apiStoreReq, NpcActionKind.Store, "live-apple", "live-basket");
                check(dupResult.duplicate && dupResult.success && dupResult.code == "stored-in-container", "basket-tx-duplicate-replay-success");

                // Conflicting Request Refusal Check
                var conflictResult = api.Execute(apiStoreReq, NpcActionKind.Store, "live-apple", "other-target");
                check(!conflictResult.success && conflictResult.code == "request-id-conflict", "basket-tx-conflict-refused");

                // 5. Execute Retrieve through NpcActionApi
                check(model.TryAllocateNextRequestId(out int apiRetReq), "basket-tx-alloc-api-ret");
                var retResult = api.Execute(apiRetReq, NpcActionKind.Retrieve, "live-apple", "live-basket");
                check(retResult.success && retResult.code == "retrieved-from-container", "basket-tx-api-ret-success", retResult.code);
                check(api.Held == rtAppleInteractable, "basket-tx-apple-restored-to-held");
                check(rtApplePhys.IsCarried, "basket-tx-apple-phys-is-carried");
                check(rtAppleCol.enabled && rtAppleCol.isTrigger, "basket-tx-apple-col-enabled-as-trigger");

                // 6. Atomic Rollback Verification on Failure:
                // Try to store an item not in hand -> cargo-ownership-mismatch
                check(model.TryAllocateNextRequestId(out int failReq), "basket-tx-alloc-fail-req");
                var failResult = api.Execute(failReq, NpcActionKind.Store, "live-other", "live-basket");
                check(!failResult.success && failResult.code == "cargo-ownership-mismatch", "basket-tx-cargo-mismatch-fail", failResult.code);

                // Verify apple remains intact in hand, and unrelated body is completely untouched
                check(api.Held == rtAppleInteractable, "basket-tx-held-intact-after-failure");
                check(Vector3.Distance(rtOtherGo.transform.position, otherInitialPos) < 0.0001f, "basket-tx-unrelated-body-unmoved");
                check(Quaternion.Angle(rtOtherGo.transform.rotation, otherInitialRot) < 0.01f, "basket-tx-unrelated-body-unrotated");
                check(Vector3.Distance(rtOtherRb.linearVelocity, otherInitialLinVel) < 0.0001f, "basket-tx-unrelated-body-linvel-preserved");
                check(Vector3.Distance(rtOtherRb.angularVelocity, otherInitialAngVel) < 0.0001f, "basket-tx-unrelated-body-angvel-preserved");

                // 7. Self-Store Semantic Denial, Replay, and Conflict Ordering:
                // Fresh self-store valid scoped denial:
                check(model.TryAllocateNextRequestId(out int selfStoreReq), "basket-tx-alloc-self-store-req");
                var selfRes1 = api.Execute(selfStoreReq, NpcActionKind.Store, "live-apple", "live-apple");
                check(!selfRes1.success && !selfRes1.duplicate && selfRes1.code == "cannot-store-in-itself", "basket-tx-self-store-denied", selfRes1.code);

                // Immediate duplicate replay of self-store:
                var selfResDup = api.Execute(selfStoreReq, NpcActionKind.Store, "live-apple", "live-apple");
                check(!selfResDup.success && selfResDup.duplicate && selfResDup.code == "cannot-store-in-itself", "basket-tx-self-store-duplicate-replay");

                // Conflicting reused request ID on self-store:
                var selfResConflict = api.Execute(selfStoreReq, NpcActionKind.Store, "live-apple", "live-basket");
                check(!selfResConflict.success && selfResConflict.code == "request-id-conflict", "basket-tx-self-store-conflict");

                // Request ID recorded for different target changed to self target must return conflict:
                check(model.TryAllocateNextRequestId(out int changeToSelfReq), "basket-tx-alloc-change-to-self-req");
                var origTargetRes = api.Execute(changeToSelfReq, NpcActionKind.Store, "live-other", "live-basket");
                check(!origTargetRes.success && origTargetRes.code == "cargo-ownership-mismatch", "basket-tx-orig-target-denial-recorded");
                var changeToSelfRes = api.Execute(changeToSelfReq, NpcActionKind.Store, "live-apple", "live-apple");
                check(!changeToSelfRes.success && changeToSelfRes.code == "request-id-conflict", "basket-tx-change-to-self-returns-conflict");

                // =============================================================
                // Section 7: Save Migration & Malformed Slot Refusals
                // =============================================================
                // 1. V1 Save Migration: Stored items with containerSlot = -1 get deterministically assigned
                var v1Payload = new PhysicalSavePayload
                {
                    worldId = worldId,
                    generationId = genId,
                    actorId = agentId,
                    tick = 10,
                    items = new List<SavedItemRecord>
                    {
                        new SavedItemRecord
                        {
                            itemId = "mig-chest",
                            itemTypeId = "test-chest",
                            location = ItemLocationKind.Free,
                            holderActorId = null,
                            containerItemId = null,
                            containerSlot = -1,
                            massKg = 5.0f,
                            dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        },
                        new SavedItemRecord
                        {
                            itemId = "mig-pear-z",
                            itemTypeId = "test-pear",
                            location = ItemLocationKind.Stored,
                            holderActorId = null,
                            containerItemId = "mig-chest",
                            containerSlot = -1,
                            massKg = 0.25f,
                            dimensions = new PhysicalDimensions(0.09f, 0.09f, 0.09f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        },
                        new SavedItemRecord
                        {
                            itemId = "mig-apple-a",
                            itemTypeId = "test-apple",
                            location = ItemLocationKind.Stored,
                            holderActorId = null,
                            containerItemId = "mig-chest",
                            containerSlot = -1,
                            massKg = 0.2f,
                            dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        }
                    },
                    receipts = new List<SavedReceiptRecord>()
                };

                string payloadJson = JsonUtility.ToJson(v1Payload);
                var v1Envelope = new PhysicalSaveEnvelope
                {
                    schema = ItemPersistence.LegacySchemaVersion,
                    payload = payloadJson,
                    sha256 = ItemPersistence.ComputeSha256(payloadJson)
                };
                string legacySaveJson = JsonUtility.ToJson(v1Envelope, true);

                string tempFile = Path.Combine(Path.GetTempPath(), "legacy-v1-save-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(tempFile, legacySaveJson);

                PhysicalSavePayload loadedPayload = null;
                try
                {
                    var restoreModel = new ItemModel(worldId, genId);
                    restoreModel.RegisterDefinition(chestDef);
                    restoreModel.RegisterDefinition(appleDef);
                    restoreModel.RegisterDefinition(pearDef);

                    bool loadSuccess = ItemPersistence.TryLoad(tempFile, worldId, genId, agentId, restoreModel, out loadedPayload);
                    check(loadSuccess, "basket-tx-load-legacy-v1");
                    check(loadedPayload != null, "basket-tx-v1-payload-not-null");

                    // Verify on-disk file remains untouched
                    string diskTextAfterLoad = File.ReadAllText(tempFile);
                    check(diskTextAfterLoad == legacySaveJson, "basket-tx-v1-disk-file-untouched");

                    // Ordinal sorting migration: "mig-apple-a" < "mig-pear-z", so apple gets slot 0, pear gets slot 1
                    var loadedApple = loadedPayload?.items.Find(i => i.itemId == "mig-apple-a");
                    var loadedPear = loadedPayload?.items.Find(i => i.itemId == "mig-pear-z");
                    check(loadedApple != null && loadedApple.containerSlot == 0, "basket-tx-migrated-apple-slot-0", $"slot={loadedApple?.containerSlot}");
                    check(loadedPear != null && loadedPear.containerSlot == 1, "basket-tx-migrated-pear-slot-1", $"slot={loadedPear?.containerSlot}");

                    // Restore into fresh model
                    check(restoreModel.RestoreSnapshot(loadedPayload), "basket-tx-restore-migrated-snapshot");
                    check(restoreModel.TryGetItemContainerSlot("mig-apple-a", out int rSlotA) && rSlotA == 0, "basket-tx-restored-model-apple-slot-0");
                    check(restoreModel.TryGetItemContainerSlot("mig-pear-z", out int rSlotZ) && rSlotZ == 1, "basket-tx-restored-model-pear-slot-1");
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }

                // 2. Strict v2 save on disk: stored item with containerSlot = -1 MUST be rejected without mutation
                var v2BadPayload = new PhysicalSavePayload
                {
                    worldId = worldId,
                    generationId = genId,
                    actorId = agentId,
                    tick = 10,
                    items = new List<SavedItemRecord>
                    {
                        new SavedItemRecord
                        {
                            itemId = "v2-chest",
                            itemTypeId = "test-chest",
                            location = ItemLocationKind.Free,
                            containerSlot = -1,
                            massKg = 5.0f,
                            dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        },
                        new SavedItemRecord
                        {
                            itemId = "v2-apple",
                            itemTypeId = "test-apple",
                            location = ItemLocationKind.Stored,
                            containerItemId = "v2-chest",
                            containerSlot = -1, // Malformed in v2!
                            massKg = 0.2f,
                            dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity,
                            lastUpdatedTick = 10
                        }
                    },
                    receipts = new List<SavedReceiptRecord>()
                };
                string v2BadPayloadJson = JsonUtility.ToJson(v2BadPayload);
                var v2BadEnvelope = new PhysicalSaveEnvelope
                {
                    schema = ItemPersistence.SchemaVersion,
                    payload = v2BadPayloadJson,
                    sha256 = ItemPersistence.ComputeSha256(v2BadPayloadJson)
                };
                string v2BadFilePath = Path.Combine(Path.GetTempPath(), "v2-bad-save-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(v2BadFilePath, JsonUtility.ToJson(v2BadEnvelope, true));
                try
                {
                    var v2TestModel = new ItemModel(worldId, genId);
                    v2TestModel.RegisterDefinition(chestDef);
                    v2TestModel.RegisterDefinition(appleDef);
                    bool v2LoadResult = ItemPersistence.TryLoad(v2BadFilePath, worldId, genId, agentId, v2TestModel, out var v2LoadedPayload);
                    check(!v2LoadResult && v2LoadedPayload == null, "basket-tx-v2-reject-unassigned-slot-on-disk");
                }
                finally
                {
                    if (File.Exists(v2BadFilePath)) File.Delete(v2BadFilePath);
                }

                // 3. Malformed Slot Configuration Refusals (in-memory):
                if (loadedPayload != null)
                {
                    // Out-of-bounds slot
                    var badSlotPayload = ClonePayload(loadedPayload);
                    badSlotPayload.items.Find(i => i.itemId == "mig-apple-a").containerSlot = 999;
                    check(!model.CanRestoreSnapshot(badSlotPayload), "basket-tx-reject-out-of-bounds-slot");

                    // Duplicate slot occupancy
                    var dupSlotPayload = ClonePayload(loadedPayload);
                    dupSlotPayload.items.Find(i => i.itemId == "mig-pear-z").containerSlot = 0; // Both in slot 0!
                    check(!model.CanRestoreSnapshot(dupSlotPayload), "basket-tx-reject-duplicate-slot-occupancy");

                    // Non-stored item with slot != -1
                    var nonStoredSlotPayload = ClonePayload(loadedPayload);
                    nonStoredSlotPayload.items.Find(i => i.itemId == "mig-chest").containerSlot = 0; // Chest is Free!
                    check(!model.CanRestoreSnapshot(nonStoredSlotPayload), "basket-tx-reject-free-item-with-slot");

                    // Inconsistent slot assignment (mix of -1 and >= 0 in same container)
                    var mixSlotPayload = ClonePayload(loadedPayload);
                    mixSlotPayload.items.Find(i => i.itemId == "mig-apple-a").containerSlot = 0;
                    mixSlotPayload.items.Find(i => i.itemId == "mig-pear-z").containerSlot = -1;
                    check(!model.CanRestoreSnapshot(mixSlotPayload), "basket-tx-reject-inconsistent-slots");
                }

                // =============================================================
                // Section 8: 50 Store/Retrieve Cycles & Physical Stability
                // =============================================================
                int completedCycles = 0;
                for (int c = 0; c < 50; c++)
                {
                    check(model.TryAllocateNextRequestId(out int cStoreReq), $"basket-tx-cycle-{c}-alloc-store");
                    var cStoreRes = api.Execute(cStoreReq, NpcActionKind.Store, "live-apple", "live-basket");
                    if (!cStoreRes.success || api.Held != null || !rtApplePhys.IsStored) break;

                    check(model.TryAllocateNextRequestId(out int cRetReq), $"basket-tx-cycle-{c}-alloc-ret");
                    var cRetRes = api.Execute(cRetReq, NpcActionKind.Retrieve, "live-apple", "live-basket");
                    if (!cRetRes.success || api.Held != rtAppleInteractable || !rtApplePhys.IsCarried) break;

                    completedCycles++;
                }
                check(completedCycles == 50, "basket-tx-50-cycles-completed", $"completed={completedCycles}");
                check(Mathf.Abs(rtApplePhys.massKg - 0.2f) <= 0.0001f, "basket-tx-50-cycles-mass-exact");
                check(Mathf.Abs(rtAppleGo.transform.lossyScale.x - 1f) <= 0.001f, "basket-tx-50-cycles-scale-unit");

                // =============================================================
                // Section 9: 5 Restarts with Durable Scoped Denial Replay
                // =============================================================
                string restartSavePath = Path.Combine(Path.GetTempPath(), "basket-tx-restarts-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    // Record a valid scoped denial in the model:
                    check(model.TryAllocateNextRequestId(out int denialReqId), "basket-tx-alloc-denial-req");
                    var denialResult = api.Execute(denialReqId, NpcActionKind.Store, "live-other", "live-basket");
                    check(!denialResult.success && denialResult.code == "cargo-ownership-mismatch", "basket-tx-initial-denial-recorded");

                    // Record a valid self-store denial in the model to test replay across restarts:
                    check(model.TryAllocateNextRequestId(out int restartSelfDenialReqId), "basket-tx-alloc-restart-self-denial-req");
                    var initialSelfDenialRes = api.Execute(restartSelfDenialReqId, NpcActionKind.Store, "live-apple", "live-apple");
                    check(!initialSelfDenialRes.success && !initialSelfDenialRes.duplicate && initialSelfDenialRes.code == "cannot-store-in-itself", "basket-tx-initial-self-denial-recorded");

                    // Store the apple
                    check(model.TryAllocateNextRequestId(out int storeAppleReq), "basket-tx-alloc-restart-store");
                    var storeAppleRes = api.Execute(storeAppleReq, NpcActionKind.Store, "live-apple", "live-basket");
                    check(storeAppleRes.success, "basket-tx-initial-store-recorded");

                    // Snapshot and save
                    var restartPayload = ItemPersistence.CreateSnapshot(model, agentId);
                    check(ItemPersistence.SaveAtomic(restartSavePath, restartPayload), "basket-tx-initial-save-atomic");

                    for (int r = 1; r <= 5; r++)
                    {
                        var restartModel = new ItemModel(worldId, genId);
                        restartModel.RegisterDefinition(basketDef);
                        restartModel.RegisterDefinition(chestDef);
                        restartModel.RegisterDefinition(appleDef);
                        restartModel.RegisterDefinition(pearDef);
                        restartModel.RegisterDefinition(heavyDef);
                        restartModel.RegisterDefinition(bulkyDef);

                        check(ItemPersistence.TryLoad(restartSavePath, worldId, genId, agentId, restartModel, out var reloadedPayload), $"basket-tx-restart-{r}-load-success");
                        check(restartModel.RestoreSnapshot(reloadedPayload), $"basket-tx-restart-{r}-restore-success");

                        var restartApi = new NpcActionApi(agentId, worldId, actorGo.transform, handGo.transform, new[] { rtBasketInteractable, rtAppleInteractable, rtOtherInteractable });
                        restartApi.PhysicalModel = restartModel;
                        restartApi.PhysicalAuthority = authority;

                        // Replay the denial request: MUST return duplicate refusal identical to pre-restart!
                        var replayDenialRes = restartApi.Execute(denialReqId, NpcActionKind.Store, "live-other", "live-basket");
                        check(replayDenialRes.duplicate && !replayDenialRes.success && replayDenialRes.code == "cargo-ownership-mismatch", $"basket-tx-restart-denial-replay-{r}");

                        // Replay the self-store denial request: MUST return duplicate refusal identical to pre-restart!
                        var replaySelfStoreRes = restartApi.Execute(restartSelfDenialReqId, NpcActionKind.Store, "live-apple", "live-apple");
                        check(replaySelfStoreRes.duplicate && !replaySelfStoreRes.success && replaySelfStoreRes.code == "cannot-store-in-itself", $"basket-tx-restart-self-store-replay-{r}");

                        // Replay the store request: MUST return duplicate success identical to pre-restart!
                        var replayStoreRes = restartApi.Execute(storeAppleReq, NpcActionKind.Store, "live-apple", "live-basket");
                        check(replayStoreRes.duplicate && replayStoreRes.success && replayStoreRes.code == "stored-in-container", $"basket-tx-restart-store-replay-{r}");

                        // Monotonic sequence allocation advances strictly above restored highest receipt
                        check(restartModel.TryAllocateNextRequestId(out int nextReqAfterRestart), $"basket-tx-restart-{r}-alloc-next");
                        check(nextReqAfterRestart > restartModel.HighestReceiptRequestId, $"basket-tx-restart-{r}-monotonic-above-restored");

                        // Re-save for next restart cycle
                        var nextCyclePayload = ItemPersistence.CreateSnapshot(restartModel, agentId);
                        check(ItemPersistence.SaveAtomic(restartSavePath, nextCyclePayload), $"basket-tx-restart-{r}-resave-success");
                    }
                }
                finally
                {
                    if (File.Exists(restartSavePath)) File.Delete(restartSavePath);
                    string bak = restartSavePath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                }

                // =============================================================
                // Section 10: Authoritative Bounded Ledger Full (512) Outcome Mapping
                // =============================================================
                {
                    var fullModel = new ItemModel(worldId, genId);
                    fullModel.RegisterDefinition(basketDef);
                    fullModel.RegisterDefinition(appleDef);
                    fullModel.SetActorCarryLimits(agentId, new ActorCarryLimits(25.0f, 2));

                    var fullBasketGo = CreateTrackedGo("rt-full-basket");
                    fullBasketGo.transform.position = actorGo.transform.position + new Vector3(0.4f, 0.2f, 0.4f);
                    var fullBasketCol = fullBasketGo.AddComponent<BoxCollider>();
                    fullBasketCol.size = new Vector3(0.3f, 0.25f, 0.3f);
                    var fullBasketRb = fullBasketGo.AddComponent<Rigidbody>();
                    fullBasketRb.mass = 2.0f;
                    var fullBasketPhys = fullBasketGo.AddComponent<PhysicalItem>();
                    fullBasketPhys.itemId = "full-basket";
                    fullBasketPhys.itemTypeId = "test-basket";
                    fullBasketPhys.massKg = 2.0f;
                    fullBasketPhys.dimensions = new PhysicalDimensions(0.3f, 0.25f, 0.3f);
                    var fullBasketInteractable = fullBasketGo.AddComponent<NpcInteractable>();
                    fullBasketInteractable.StableId = "full-basket";
                    fullBasketInteractable.Kind = NpcObjectKind.Item;
                    fullBasketInteractable.Permission = true;
                    fullBasketInteractable.WorldId = worldId;
                    var fullBasketAppGo = CreateTrackedGo("rt-full-basket-approach");
                    fullBasketAppGo.transform.position = fullBasketGo.transform.position;
                    fullBasketAppGo.transform.SetParent(fullBasketGo.transform, true);
                    fullBasketInteractable.Approach = fullBasketAppGo.transform;
                    fullBasketPhys.Bind(fullModel, worldId, genId);

                    var fullAppleGo = CreateTrackedGo("rt-full-apple");
                    fullAppleGo.transform.position = actorGo.transform.position + new Vector3(0.1f, 0.2f, 0.1f);
                    var fullAppleCol = fullAppleGo.AddComponent<BoxCollider>();
                    fullAppleCol.size = new Vector3(0.08f, 0.08f, 0.08f);
                    var fullAppleRb = fullAppleGo.AddComponent<Rigidbody>();
                    fullAppleRb.mass = 0.2f;
                    var fullApplePhys = fullAppleGo.AddComponent<PhysicalItem>();
                    fullApplePhys.itemId = "full-apple";
                    fullApplePhys.itemTypeId = "test-apple";
                    fullApplePhys.massKg = 0.2f;
                    fullApplePhys.dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f);
                    var fullAppleInteractable = fullAppleGo.AddComponent<NpcInteractable>();
                    fullAppleInteractable.StableId = "full-apple";
                    fullAppleInteractable.Kind = NpcObjectKind.Item;
                    fullAppleInteractable.Permission = true;
                    fullAppleInteractable.WorldId = worldId;
                    fullApplePhys.Bind(fullModel, worldId, genId);

                    check(fullModel.RegisterItem("full-basket", "test-basket", ItemLocationKind.Free, fullBasketGo.transform.position, fullBasketGo.transform.rotation), "basket-tx-full-reg-basket");
                    check(fullModel.RegisterItem("full-apple", "test-apple", ItemLocationKind.Free, fullAppleGo.transform.position, fullAppleGo.transform.rotation), "basket-tx-full-reg-apple");

                    // Pick up apple in fullModel
                    check(fullModel.TryAllocateNextRequestId(out int fullPickReq), "basket-tx-full-alloc-pick");
                    check(fullModel.Execute(worldId, genId, new ItemActionRequest { requestId = fullPickReq, action = ItemActionKind.Pickup, actorId = agentId, itemId = "full-apple" }, authority).success, "basket-tx-full-pick-apple");

                    var fullApi = new NpcActionApi(agentId, worldId, actorGo.transform, handGo.transform, new[] { fullBasketInteractable, fullAppleInteractable });
                    fullApi.PhysicalModel = fullModel;
                    fullApi.PhysicalAuthority = authority;

                    fullApplePhys.AttachToHand(handGo.transform);
                    fullAppleInteractable.HeldBy = agentId;
                    check(fullApi.RestoreHeld(fullAppleInteractable), "basket-tx-full-restore-held");

                    // Fill fullModel receipt ledger up to MaxReceiptLedgerSize (512)
                    // Note: fullPickReq took receipt ID 1. Fill remaining 511 receipts monotonically:
                    bool allFillRecorded = true;
                    for (int fillId = 2; fillId <= ItemModel.MaxReceiptLedgerSize; fillId++)
                    {
                        if (!fullModel.TryAllocateNextRequestId(out int allocatedFillId) || allocatedFillId != fillId)
                        {
                            allFillRecorded = false;
                            break;
                        }
                        var fillReq = new ItemActionRequest
                        {
                            requestId = allocatedFillId,
                            action = ItemActionKind.Pickup,
                            actorId = agentId,
                            itemId = "full-apple"
                        };
                        var fillRec = fullModel.RecordRejectionReceipt(worldId, genId, fillReq, "ledger-fill-rejection");
                        if (fillRec.success || fillRec.code != "ledger-fill-rejection")
                        {
                            allFillRecorded = false;
                            break;
                        }
                    }
                    check(allFillRecorded, "basket-tx-full-ledger-filled-to-capacity");

                    // Now the ledger is completely full (512 receipts).
                    // Allocate next monotonic request ID (513)
                    check(fullModel.TryAllocateNextRequestId(out int overflowReqId), "basket-tx-full-alloc-overflow-req");
                    check(overflowReqId == 513, "basket-tx-full-overflow-req-is-513", $"actual={overflowReqId}");

                    // Execute scoped refusal (self-store) through real NpcActionApi adapter:
                    var ledgerFullResult = fullApi.Execute(overflowReqId, NpcActionKind.Store, "full-apple", "full-apple");
                    check(!ledgerFullResult.success && !ledgerFullResult.duplicate && ledgerFullResult.code == "bounded-ledger-full", "basket-tx-adapter-returns-bounded-ledger-full", ledgerFullResult.code);

                    // Confirm no mutation occurred: apple remains carried in model and in scene
                    check(fullModel.TryGetItem("full-apple", out var fullAppleSnap) && fullAppleSnap.location == ItemLocationKind.Carried, "basket-tx-ledger-full-no-model-mutation");
                    check(fullApi.Held == fullAppleInteractable, "basket-tx-ledger-full-held-intact");
                    check(fullApplePhys.IsCarried, "basket-tx-ledger-full-phys-carried");

                    // Confirm no receipt was added to ledger for the overflow request
                    check(!fullModel.TryGetReceipt(overflowReqId, out _, out _), "basket-tx-ledger-full-no-overflow-receipt-stored");
                }

                // =============================================================
                // Section 11: SaveAtomic Detached Canonicalization Boundary Tests
                // =============================================================
                // 1. Legacy in-memory source saved -> inspect canonical v2 slots + strict live-model load succeeds;
                //    original caller byte serialization, object references, list order, and receipt signatures unchanged.
                string canonicalSavePath = Path.Combine(Path.GetTempPath(), "basket-tx-canonical-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var legacySourcePayload = new PhysicalSavePayload
                    {
                        worldId = worldId,
                        generationId = genId,
                        actorId = agentId,
                        tick = 10,
                        items = new List<SavedItemRecord>
                        {
                            new SavedItemRecord
                            {
                                itemId = "legacy-chest",
                                itemTypeId = "test-chest",
                                location = ItemLocationKind.Free,
                                containerSlot = -1,
                                massKg = 5.0f,
                                dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            },
                            new SavedItemRecord
                            {
                                itemId = "legacy-stored-b",
                                itemTypeId = "test-pear",
                                location = ItemLocationKind.Stored,
                                containerItemId = "legacy-chest",
                                containerSlot = -1,
                                massKg = 0.25f,
                                dimensions = new PhysicalDimensions(0.09f, 0.09f, 0.09f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            },
                            new SavedItemRecord
                            {
                                itemId = "legacy-stored-a",
                                itemTypeId = "test-apple",
                                location = ItemLocationKind.Stored,
                                containerItemId = "legacy-chest",
                                containerSlot = -1,
                                massKg = 0.2f,
                                dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            }
                        },
                        receipts = new List<SavedReceiptRecord>
                        {
                            new SavedReceiptRecord
                            {
                                requestId = 10,
                                signature = "sig-store-legacy-a",
                                receipt = new ItemReceipt
                                {
                                    requestId = 10,
                                    worldId = worldId,
                                    generationId = genId,
                                    actorId = agentId,
                                    action = ItemActionKind.Store,
                                    itemId = "legacy-stored-a",
                                    targetId = "legacy-chest",
                                    success = true,
                                    duplicate = false,
                                    code = "stored-in-container",
                                    totalCarriedMassKg = 0f
                                }
                            }
                        }
                    };

                    // Record pre-save caller state
                    string preSaveCallerJson = JsonUtility.ToJson(legacySourcePayload, false);
                    var itemChestRef = legacySourcePayload.items[0];
                    var itemBRef = legacySourcePayload.items[1];
                    var itemARef = legacySourcePayload.items[2];
                    var receiptRef = legacySourcePayload.receipts[0];

                    check(ItemPersistence.SaveAtomic(canonicalSavePath, legacySourcePayload), "basket-tx-canonical-save-atomic-succeeds");
                    check(File.Exists(canonicalSavePath), "basket-tx-canonical-file-exists");

                    // Verify caller original payload, objects, byte serialization, list order, and receipts remain completely untouched
                    string postSaveCallerJson = JsonUtility.ToJson(legacySourcePayload, false);
                    check(string.Equals(postSaveCallerJson, preSaveCallerJson, StringComparison.Ordinal), "basket-tx-caller-bytes-unchanged", $"post={postSaveCallerJson}");
                    check(ReferenceEquals(legacySourcePayload.items[0], itemChestRef) &&
                          ReferenceEquals(legacySourcePayload.items[1], itemBRef) &&
                          ReferenceEquals(legacySourcePayload.items[2], itemARef), "basket-tx-caller-item-references-preserved");
                    check(legacySourcePayload.items[0].containerSlot == -1 &&
                          legacySourcePayload.items[1].containerSlot == -1 &&
                          legacySourcePayload.items[2].containerSlot == -1, "basket-tx-caller-slots-remain-unassigned");
                    check(string.Equals(legacySourcePayload.items[0].itemId, "legacy-chest", StringComparison.Ordinal) &&
                          string.Equals(legacySourcePayload.items[1].itemId, "legacy-stored-b", StringComparison.Ordinal) &&
                          string.Equals(legacySourcePayload.items[2].itemId, "legacy-stored-a", StringComparison.Ordinal), "basket-tx-caller-list-order-preserved");
                    check(ReferenceEquals(legacySourcePayload.receipts[0], receiptRef) &&
                          string.Equals(legacySourcePayload.receipts[0].signature, "sig-store-legacy-a", StringComparison.Ordinal) &&
                          legacySourcePayload.receipts[0].requestId == 10, "basket-tx-caller-receipt-preserved");

                    // Inspect on-disk file envelope and serialized canonical slots
                    string fileText = File.ReadAllText(canonicalSavePath);
                    var envelope = JsonUtility.FromJson<PhysicalSaveEnvelope>(fileText);
                    check(envelope != null, "basket-tx-canonical-envelope-not-null");
                    check(string.Equals(envelope.schema, ItemPersistence.SchemaVersion, StringComparison.Ordinal), "basket-tx-canonical-schema-v2", envelope.schema);

                    string computedSha = ItemPersistence.ComputeSha256(envelope.payload);
                    check(string.Equals(computedSha, envelope.sha256, StringComparison.OrdinalIgnoreCase), "basket-tx-canonical-sha-matches");

                    var diskPayload = JsonUtility.FromJson<PhysicalSavePayload>(envelope.payload);
                    check(diskPayload != null && diskPayload.items != null && diskPayload.items.Count == 3, "basket-tx-canonical-disk-payload-count-3");

                    // On disk: list order preserved, but slots canonicalized by ordinal item ID ("legacy-stored-a" < "legacy-stored-b")
                    check(string.Equals(diskPayload.items[0].itemId, "legacy-chest", StringComparison.Ordinal) &&
                          string.Equals(diskPayload.items[1].itemId, "legacy-stored-b", StringComparison.Ordinal) &&
                          string.Equals(diskPayload.items[2].itemId, "legacy-stored-a", StringComparison.Ordinal), "basket-tx-disk-list-order-preserved");

                    var diskChest = diskPayload.items.Find(i => i.itemId == "legacy-chest");
                    var diskA = diskPayload.items.Find(i => i.itemId == "legacy-stored-a");
                    var diskB = diskPayload.items.Find(i => i.itemId == "legacy-stored-b");
                    check(diskChest != null && diskChest.containerSlot == -1, "basket-tx-disk-chest-slot-minus-1", $"slot={diskChest?.containerSlot}");
                    check(diskA != null && diskA.containerSlot == 0, "basket-tx-disk-apple-slot-0", $"slot={diskA?.containerSlot}");
                    check(diskB != null && diskB.containerSlot == 1, "basket-tx-disk-pear-slot-1", $"slot={diskB?.containerSlot}");

                    // On disk: receipt struct and signature preserved
                    check(diskPayload.receipts != null && diskPayload.receipts.Count == 1 &&
                          diskPayload.receipts[0].requestId == 10 &&
                          string.Equals(diskPayload.receipts[0].signature, "sig-store-legacy-a", StringComparison.Ordinal) &&
                          string.Equals(diskPayload.receipts[0].receipt.code, "stored-in-container", StringComparison.Ordinal), "basket-tx-disk-receipt-intact");

                    // Strict live-model load succeeds against canonical v2 file
                    var canonLiveModel = new ItemModel(worldId, genId);
                    canonLiveModel.RegisterDefinition(chestDef);
                    canonLiveModel.RegisterDefinition(appleDef);
                    canonLiveModel.RegisterDefinition(pearDef);

                    bool liveLoadSuccess = ItemPersistence.TryLoad(canonicalSavePath, worldId, genId, agentId, canonLiveModel, out var loadedCanonPayload);
                    check(liveLoadSuccess && loadedCanonPayload != null, "basket-tx-canonical-strict-live-model-load-succeeds");
                    check(canonLiveModel.CanRestoreSnapshot(loadedCanonPayload), "basket-tx-canonical-can-restore-true");
                    check(canonLiveModel.RestoreSnapshot(loadedCanonPayload), "basket-tx-canonical-restore-succeeds");
                    check(canonLiveModel.TryGetItemContainerSlot("legacy-stored-a", out int slotA) && slotA == 0, "basket-tx-canonical-restored-slot-a-0");
                    check(canonLiveModel.TryGetItemContainerSlot("legacy-stored-b", out int slotB) && slotB == 1, "basket-tx-canonical-restored-slot-b-1");
                }
                finally
                {
                    if (File.Exists(canonicalSavePath)) File.Delete(canonicalSavePath);
                    string bak = canonicalSavePath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                }

                // 2. Nested independent ordinal slots
                string nestedSavePath = Path.Combine(Path.GetTempPath(), "basket-tx-nested-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var nestedPayload = new PhysicalSavePayload
                    {
                        worldId = worldId,
                        generationId = genId,
                        actorId = agentId,
                        tick = 10,
                        items = new List<SavedItemRecord>
                        {
                            new SavedItemRecord
                            {
                                itemId = "nest-chest",
                                itemTypeId = "test-chest",
                                location = ItemLocationKind.Free,
                                containerSlot = -1,
                                massKg = 5.0f,
                                dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            },
                            new SavedItemRecord
                            {
                                itemId = "nest-basket",
                                itemTypeId = "test-basket",
                                location = ItemLocationKind.Stored,
                                containerItemId = "nest-chest",
                                containerSlot = -1,
                                massKg = 2.0f,
                                dimensions = new PhysicalDimensions(0.3f, 0.25f, 0.3f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            },
                            new SavedItemRecord
                            {
                                itemId = "nest-apple",
                                itemTypeId = "test-apple",
                                location = ItemLocationKind.Stored,
                                containerItemId = "nest-chest",
                                containerSlot = -1,
                                massKg = 0.2f,
                                dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            },
                            new SavedItemRecord
                            {
                                itemId = "nest-pear",
                                itemTypeId = "test-pear",
                                location = ItemLocationKind.Stored,
                                containerItemId = "nest-basket",
                                containerSlot = -1,
                                massKg = 0.25f,
                                dimensions = new PhysicalDimensions(0.09f, 0.09f, 0.09f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            }
                        },
                        receipts = new List<SavedReceiptRecord>()
                    };

                    check(ItemPersistence.SaveAtomic(nestedSavePath, nestedPayload), "basket-tx-nested-save-atomic-succeeds");

                    string nestedFileText = File.ReadAllText(nestedSavePath);
                    var nestedEnv = JsonUtility.FromJson<PhysicalSaveEnvelope>(nestedFileText);
                    var nestedDisk = JsonUtility.FromJson<PhysicalSavePayload>(nestedEnv.payload);

                    // In nest-chest: "nest-apple" < "nest-basket" -> nest-apple gets 0, nest-basket gets 1
                    // In nest-basket: "nest-pear" is sole child -> nest-pear gets 0
                    var diskNestChest = nestedDisk.items.Find(i => i.itemId == "nest-chest");
                    var diskNestApple = nestedDisk.items.Find(i => i.itemId == "nest-apple");
                    var diskNestBasket = nestedDisk.items.Find(i => i.itemId == "nest-basket");
                    var diskNestPear = nestedDisk.items.Find(i => i.itemId == "nest-pear");

                    check(diskNestChest != null && diskNestChest.containerSlot == -1, "basket-tx-nested-root-slot-minus-1");
                    check(diskNestApple != null && diskNestApple.containerSlot == 0, "basket-tx-nested-apple-slot-0", $"slot={diskNestApple?.containerSlot}");
                    check(diskNestBasket != null && diskNestBasket.containerSlot == 1, "basket-tx-nested-basket-slot-1", $"slot={diskNestBasket?.containerSlot}");
                    check(diskNestPear != null && diskNestPear.containerSlot == 0, "basket-tx-nested-pear-slot-0", $"slot={diskNestPear?.containerSlot}");

                    var nestedLiveModel = new ItemModel(worldId, genId);
                    nestedLiveModel.RegisterDefinition(chestDef);
                    nestedLiveModel.RegisterDefinition(basketDef);
                    nestedLiveModel.RegisterDefinition(appleDef);
                    nestedLiveModel.RegisterDefinition(pearDef);

                    bool nestedLoadSuccess = ItemPersistence.TryLoad(nestedSavePath, worldId, genId, agentId, nestedLiveModel, out var loadedNestedPayload);
                    check(nestedLoadSuccess && loadedNestedPayload != null, "basket-tx-nested-strict-live-model-load-succeeds");
                    check(nestedLiveModel.CanRestoreSnapshot(loadedNestedPayload), "basket-tx-nested-can-restore-true");
                    check(nestedLiveModel.RestoreSnapshot(loadedNestedPayload), "basket-tx-nested-restore-succeeds");
                    check(nestedLiveModel.TryGetItemContainerSlot("nest-apple", out int nSlotA) && nSlotA == 0, "basket-tx-nested-restored-apple-slot-0");
                    check(nestedLiveModel.TryGetItemContainerSlot("nest-basket", out int nSlotB) && nSlotB == 1, "basket-tx-nested-restored-basket-slot-1");
                    check(nestedLiveModel.TryGetItemContainerSlot("nest-pear", out int nSlotP) && nSlotP == 0, "basket-tx-nested-restored-pear-slot-0");
                }
                finally
                {
                    if (File.Exists(nestedSavePath)) File.Delete(nestedSavePath);
                    string bak = nestedSavePath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                }

                // 3. Valid assigned holes preserved: already-assigned slots (e.g. slots 0 and 2) must remain unchanged
                string holeSavePath = Path.Combine(Path.GetTempPath(), "basket-tx-hole-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var holePayload = new PhysicalSavePayload
                    {
                        worldId = worldId,
                        generationId = genId,
                        actorId = agentId,
                        tick = 10,
                        items = new List<SavedItemRecord>
                        {
                            new SavedItemRecord
                            {
                                itemId = "hole-chest",
                                itemTypeId = "test-chest",
                                location = ItemLocationKind.Free,
                                containerSlot = -1,
                                massKg = 5.0f,
                                dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            },
                            new SavedItemRecord
                            {
                                itemId = "hole-apple",
                                itemTypeId = "test-apple",
                                location = ItemLocationKind.Stored,
                                containerItemId = "hole-chest",
                                containerSlot = 0, // Slot 0
                                massKg = 0.2f,
                                dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            },
                            new SavedItemRecord
                            {
                                itemId = "hole-pear",
                                itemTypeId = "test-pear",
                                location = ItemLocationKind.Stored,
                                containerItemId = "hole-chest",
                                containerSlot = 2, // Slot 2 (hole at slot 1!)
                                massKg = 0.25f,
                                dimensions = new PhysicalDimensions(0.09f, 0.09f, 0.09f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            }
                        },
                        receipts = new List<SavedReceiptRecord>()
                    };

                    check(ItemPersistence.SaveAtomic(holeSavePath, holePayload), "basket-tx-hole-save-atomic-succeeds");

                    string holeFileText = File.ReadAllText(holeSavePath);
                    var holeEnv = JsonUtility.FromJson<PhysicalSaveEnvelope>(holeFileText);
                    var holeDisk = JsonUtility.FromJson<PhysicalSavePayload>(holeEnv.payload);

                    var diskHoleApple = holeDisk.items.Find(i => i.itemId == "hole-apple");
                    var diskHolePear = holeDisk.items.Find(i => i.itemId == "hole-pear");
                    check(diskHoleApple != null && diskHoleApple.containerSlot == 0, "basket-tx-hole-apple-retains-slot-0", $"slot={diskHoleApple?.containerSlot}");
                    check(diskHolePear != null && diskHolePear.containerSlot == 2, "basket-tx-hole-pear-retains-slot-2", $"slot={diskHolePear?.containerSlot}");

                    var holeLiveModel = new ItemModel(worldId, genId);
                    holeLiveModel.RegisterDefinition(chestDef);
                    holeLiveModel.RegisterDefinition(appleDef);
                    holeLiveModel.RegisterDefinition(pearDef);

                    bool holeLoadSuccess = ItemPersistence.TryLoad(holeSavePath, worldId, genId, agentId, holeLiveModel, out var loadedHolePayload);
                    check(holeLoadSuccess && loadedHolePayload != null, "basket-tx-hole-strict-live-model-load-succeeds");
                    check(holeLiveModel.CanRestoreSnapshot(loadedHolePayload), "basket-tx-hole-can-restore-true");
                    check(holeLiveModel.RestoreSnapshot(loadedHolePayload), "basket-tx-hole-restore-succeeds");
                    check(holeLiveModel.TryGetItemContainerSlot("hole-apple", out int hSlotA) && hSlotA == 0, "basket-tx-hole-restored-apple-slot-0");
                    check(holeLiveModel.TryGetItemContainerSlot("hole-pear", out int hSlotP) && hSlotP == 2, "basket-tx-hole-restored-pear-slot-2");
                }
                finally
                {
                    if (File.Exists(holeSavePath)) File.Delete(holeSavePath);
                    string bak = holeSavePath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                }

                // 4. Malformed values NOT normalized: mixed assigned/unassigned, <-1, and invalid nonstored values
                // 4a. Mixed assigned/unassigned within same container: not normalized, retains -1 on disk, fails strict v2 TryLoad
                string mixedSavePath = Path.Combine(Path.GetTempPath(), "basket-tx-mixed-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var mixedPayload = new PhysicalSavePayload
                    {
                        worldId = worldId,
                        generationId = genId,
                        actorId = agentId,
                        tick = 10,
                        items = new List<SavedItemRecord>
                        {
                            new SavedItemRecord
                            {
                                itemId = "mixed-chest",
                                itemTypeId = "test-chest",
                                location = ItemLocationKind.Free,
                                containerSlot = -1,
                                massKg = 5.0f,
                                dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            },
                            new SavedItemRecord
                            {
                                itemId = "mixed-apple",
                                itemTypeId = "test-apple",
                                location = ItemLocationKind.Stored,
                                containerItemId = "mixed-chest",
                                containerSlot = 0, // Assigned
                                massKg = 0.2f,
                                dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            },
                            new SavedItemRecord
                            {
                                itemId = "mixed-pear",
                                itemTypeId = "test-pear",
                                location = ItemLocationKind.Stored,
                                containerItemId = "mixed-chest",
                                containerSlot = -1, // Unassigned (mixed!)
                                massKg = 0.25f,
                                dimensions = new PhysicalDimensions(0.09f, 0.09f, 0.09f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            }
                        },
                        receipts = new List<SavedReceiptRecord>()
                    };

                    check(ItemPersistence.SaveAtomic(mixedSavePath, mixedPayload), "basket-tx-mixed-save-atomic-succeeds");

                    string mixedFileText = File.ReadAllText(mixedSavePath);
                    var mixedEnv = JsonUtility.FromJson<PhysicalSaveEnvelope>(mixedFileText);
                    var mixedDisk = JsonUtility.FromJson<PhysicalSavePayload>(mixedEnv.payload);
                    var diskMixedPear = mixedDisk.items.Find(i => i.itemId == "mixed-pear");
                    check(diskMixedPear != null && diskMixedPear.containerSlot == -1, "basket-tx-mixed-pear-slot-minus-1-preserved");

                    var mixedLiveModel = new ItemModel(worldId, genId);
                    mixedLiveModel.RegisterDefinition(chestDef);
                    mixedLiveModel.RegisterDefinition(appleDef);
                    mixedLiveModel.RegisterDefinition(pearDef);

                    bool mixedLoadResult = ItemPersistence.TryLoad(mixedSavePath, worldId, genId, agentId, mixedLiveModel, out var mixedLoadedPayload);
                    check(!mixedLoadResult && mixedLoadedPayload == null, "basket-tx-mixed-unassigned-fails-strict-v2-load");
                }
                finally
                {
                    if (File.Exists(mixedSavePath)) File.Delete(mixedSavePath);
                    string bak = mixedSavePath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                }

                // 4b. Stored slot < -1 (e.g. -2): not normalized, retains -2 on disk, fails strict v2 TryLoad
                string negSlotSavePath = Path.Combine(Path.GetTempPath(), "basket-tx-neg-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var negPayload = new PhysicalSavePayload
                    {
                        worldId = worldId,
                        generationId = genId,
                        actorId = agentId,
                        tick = 10,
                        items = new List<SavedItemRecord>
                        {
                            new SavedItemRecord
                            {
                                itemId = "neg-chest",
                                itemTypeId = "test-chest",
                                location = ItemLocationKind.Free,
                                containerSlot = -1,
                                massKg = 5.0f,
                                dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            },
                            new SavedItemRecord
                            {
                                itemId = "neg-apple",
                                itemTypeId = "test-apple",
                                location = ItemLocationKind.Stored,
                                containerItemId = "neg-chest",
                                containerSlot = -2, // Invalid negative slot < -1
                                massKg = 0.2f,
                                dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            }
                        },
                        receipts = new List<SavedReceiptRecord>()
                    };

                    check(ItemPersistence.SaveAtomic(negSlotSavePath, negPayload), "basket-tx-neg-save-atomic-succeeds");

                    string negFileText = File.ReadAllText(negSlotSavePath);
                    var negEnv = JsonUtility.FromJson<PhysicalSaveEnvelope>(negFileText);
                    var negDisk = JsonUtility.FromJson<PhysicalSavePayload>(negEnv.payload);
                    var diskNegApple = negDisk.items.Find(i => i.itemId == "neg-apple");
                    check(diskNegApple != null && diskNegApple.containerSlot == -2, "basket-tx-neg-slot-minus-2-preserved");

                    var negLiveModel = new ItemModel(worldId, genId);
                    negLiveModel.RegisterDefinition(chestDef);
                    negLiveModel.RegisterDefinition(appleDef);

                    bool negLoadResult = ItemPersistence.TryLoad(negSlotSavePath, worldId, genId, agentId, negLiveModel, out var negLoadedPayload);
                    check(!negLoadResult && negLoadedPayload == null, "basket-tx-neg-slot-fails-strict-v2-load");
                }
                finally
                {
                    if (File.Exists(negSlotSavePath)) File.Delete(negSlotSavePath);
                    string bak = negSlotSavePath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                }

                // 4c. Non-stored item with invalid slot: Free item with containerSlot != -1 not normalized to -1, fails strict TryLoad
                string badNonStoredSavePath = Path.Combine(Path.GetTempPath(), "basket-tx-badnonstored-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var badNonStoredPayload = new PhysicalSavePayload
                    {
                        worldId = worldId,
                        generationId = genId,
                        actorId = agentId,
                        tick = 10,
                        items = new List<SavedItemRecord>
                        {
                            new SavedItemRecord
                            {
                                itemId = "badnonstored-chest",
                                itemTypeId = "test-chest",
                                location = ItemLocationKind.Free,
                                containerSlot = 0, // Invalid: Free item must have containerSlot = -1
                                massKg = 5.0f,
                                dimensions = new PhysicalDimensions(0.6f, 0.4f, 0.5f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity,
                                lastUpdatedTick = 10
                            }
                        },
                        receipts = new List<SavedReceiptRecord>()
                    };

                    check(ItemPersistence.SaveAtomic(badNonStoredSavePath, badNonStoredPayload), "basket-tx-badnonstored-save-atomic-succeeds");

                    string bnsFileText = File.ReadAllText(badNonStoredSavePath);
                    var bnsEnv = JsonUtility.FromJson<PhysicalSaveEnvelope>(bnsFileText);
                    var bnsDisk = JsonUtility.FromJson<PhysicalSavePayload>(bnsEnv.payload);
                    var diskBnsChest = bnsDisk.items.Find(i => i.itemId == "badnonstored-chest");
                    check(diskBnsChest != null && diskBnsChest.containerSlot == 0, "basket-tx-badnonstored-slot-0-not-cleared");

                    var bnsLiveModel = new ItemModel(worldId, genId);
                    bnsLiveModel.RegisterDefinition(chestDef);

                    bool bnsLoadResult = ItemPersistence.TryLoad(badNonStoredSavePath, worldId, genId, agentId, bnsLiveModel, out var bnsLoadedPayload);
                    check(!bnsLoadResult && bnsLoadedPayload == null, "basket-tx-badnonstored-fails-strict-load");
                }
                finally
                {
                    if (File.Exists(badNonStoredSavePath)) File.Delete(badNonStoredSavePath);
                    string bak = badNonStoredSavePath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                }
            }
            finally
            {
                foreach (var go in ownedObjects)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }
    }
}
