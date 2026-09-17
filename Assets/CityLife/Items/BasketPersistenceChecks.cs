using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CityLife.World;

namespace CityLife.Items
{
    /// <summary>
    /// Deterministic in-memory and durable persistence checks for the stored item graph.
    /// Validates nested container roundtrip, exact memberships and masses, repeated restores,
    /// malformed graph refusals (duplicate IDs, dangling/self/cyclic parents, non-containers,
    /// immediate and ancestor capacity, actor carry limits with descendants), legacy v1
    /// compatibility without container fields, and replay receipts across restore.
    /// Pure C# check suite with zero side effects requiring Unity launch.
    /// </summary>
    public static class BasketPersistenceChecks
    {
        public static List<string> Run()
        {
            var passed = new List<string>();

            void Check(bool condition, string name)
            {
                if (!condition)
                    throw new InvalidOperationException("BASKET PERSISTENCE CHECK FAILED: " + name);
                passed.Add(name);
            }

            const string worldId = "test-basket-world";
            const string genId = "gen-01";
            const string actorId = "actor-starfall-01";

            var defChest = new ItemDefinition
            {
                itemTypeId = "container-chest",
                dimensions = new PhysicalDimensions(0.5f, 0.4f, 0.4f),
                massKg = 3.0f,
                isContainer = true,
                maxContainedSlots = 6,
                maxContainedVolumeM3 = 0.5f,
                maxContainedMassKg = 30.0f
            };

            var defBasket = new ItemDefinition
            {
                itemTypeId = "container-basket",
                dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                massKg = 1.0f,
                isContainer = true,
                maxContainedSlots = 4,
                maxContainedVolumeM3 = 0.1f,
                maxContainedMassKg = 10.0f
            };

            var defPouch = new ItemDefinition
            {
                itemTypeId = "container-pouch",
                dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                massKg = 0.2f,
                isContainer = true,
                maxContainedSlots = 2,
                maxContainedVolumeM3 = 0.01f,
                maxContainedMassKg = 2.0f
            };

            var defChisel = new ItemDefinition
            {
                itemTypeId = "tool-chisel",
                dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                massKg = 0.5f,
                isContainer = false
            };

            var defGem = new ItemDefinition
            {
                itemTypeId = "gem-ruby",
                dimensions = new PhysicalDimensions(0.02f, 0.02f, 0.02f),
                massKg = 0.1f,
                isContainer = false
            };

            void RegisterAllDefinitions(ItemModel m)
            {
                m.RegisterDefinition(defChest);
                m.RegisterDefinition(defBasket);
                m.RegisterDefinition(defPouch);
                m.RegisterDefinition(defChisel);
                m.RegisterDefinition(defGem);
            }

            var authority = new BasicItemActionAuthority();
            string tempDir = Path.Combine(Path.GetTempPath(), "basket-checks-" + Guid.NewGuid().ToString("N"));

            PhysicalSavePayload CopyPayload(PhysicalSavePayload src)
            {
                string json = JsonUtility.ToJson(src);
                return JsonUtility.FromJson<PhysicalSavePayload>(json);
            }

            try
            {
                Directory.CreateDirectory(tempDir);

                // -------------------------------------------------------------
                // 1. Nested stored roundtrip with exact IDs, membership, and mass
                // -------------------------------------------------------------
                PhysicalSavePayload savedPayload;
                string savePath = Path.Combine(tempDir, "nested-basket-save.json");
                {
                    var liveModel = new ItemModel(worldId, genId);
                    RegisterAllDefinitions(liveModel);

                    // Register items initially Free in the world
                    Check(liveModel.RegisterItem("chest-01", "container-chest", ItemLocationKind.Free, new Vector3(1f, 0f, 1f), Quaternion.identity), "register-chest");
                    Check(liveModel.RegisterItem("basket-01", "container-basket", ItemLocationKind.Free, new Vector3(2f, 0f, 1f), Quaternion.identity), "register-basket");
                    Check(liveModel.RegisterItem("pouch-01", "container-pouch", ItemLocationKind.Free, new Vector3(3f, 0f, 1f), Quaternion.identity), "register-pouch");
                    Check(liveModel.RegisterItem("chisel-01", "tool-chisel", ItemLocationKind.Free, new Vector3(4f, 0f, 1f), Quaternion.identity), "register-chisel");
                    Check(liveModel.RegisterItem("gem-01", "gem-ruby", ItemLocationKind.Free, new Vector3(5f, 0f, 1f), Quaternion.identity), "register-gem");

                    // Build nested hierarchy via authoritative actions:
                    // gem-01 into pouch-01
                    var r1 = liveModel.Execute(worldId, genId, new ItemActionRequest { requestId = 1, action = ItemActionKind.Pickup, actorId = actorId, itemId = "gem-01" }, authority);
                    Check(r1.success, "pickup-gem");
                    var r2 = liveModel.Execute(worldId, genId, new ItemActionRequest { requestId = 2, action = ItemActionKind.Store, actorId = actorId, itemId = "gem-01", targetId = "pouch-01" }, authority);
                    Check(r2.success, "store-gem-in-pouch");

                    // pouch-01 into basket-01
                    var r3 = liveModel.Execute(worldId, genId, new ItemActionRequest { requestId = 3, action = ItemActionKind.Pickup, actorId = actorId, itemId = "pouch-01" }, authority);
                    Check(r3.success, "pickup-pouch");
                    var r4 = liveModel.Execute(worldId, genId, new ItemActionRequest { requestId = 4, action = ItemActionKind.Store, actorId = actorId, itemId = "pouch-01", targetId = "basket-01" }, authority);
                    Check(r4.success, "store-pouch-in-basket");

                    // chisel-01 into basket-01
                    var r5 = liveModel.Execute(worldId, genId, new ItemActionRequest { requestId = 5, action = ItemActionKind.Pickup, actorId = actorId, itemId = "chisel-01" }, authority);
                    Check(r5.success, "pickup-chisel");
                    var r6 = liveModel.Execute(worldId, genId, new ItemActionRequest { requestId = 6, action = ItemActionKind.Store, actorId = actorId, itemId = "chisel-01", targetId = "basket-01" }, authority);
                    Check(r6.success, "store-chisel-in-basket");

                    // basket-01 into chest-01
                    var r7 = liveModel.Execute(worldId, genId, new ItemActionRequest { requestId = 7, action = ItemActionKind.Pickup, actorId = actorId, itemId = "basket-01" }, authority);
                    Check(r7.success, "pickup-basket");
                    var r8 = liveModel.Execute(worldId, genId, new ItemActionRequest { requestId = 8, action = ItemActionKind.Store, actorId = actorId, itemId = "basket-01", targetId = "chest-01" }, authority);
                    Check(r8.success, "store-basket-in-chest");

                    // Verify pre-save live containment and masses (tight tolerance <= 0.0001f kg)
                    // gem = 0.1
                    // pouch = 0.2 + 0.1 = 0.3
                    // chisel = 0.5
                    // basket = 1.0 + 0.3 + 0.5 = 1.8
                    // chest = 3.0 + 1.8 = 4.8
                    Check(Mathf.Abs(liveModel.GetItemTotalMassKg("gem-01") - 0.1f) <= 0.0001f, "live-mass-gem");
                    Check(Mathf.Abs(liveModel.GetItemTotalMassKg("pouch-01") - 0.3f) <= 0.0001f, "live-mass-pouch");
                    Check(Mathf.Abs(liveModel.GetItemTotalMassKg("chisel-01") - 0.5f) <= 0.0001f, "live-mass-chisel");
                    Check(Mathf.Abs(liveModel.GetItemTotalMassKg("basket-01") - 1.8f) <= 0.0001f, "live-mass-basket");
                    Check(Mathf.Abs(liveModel.GetItemTotalMassKg("chest-01") - 4.8f) <= 0.0001f, "live-mass-chest");
                    Check(Mathf.Abs(liveModel.GetContainerContainedMassKg("chest-01") - 1.8f) <= 0.0001f, "live-chest-contained-mass");
                    Check(Mathf.Abs(liveModel.GetContainerContainedMassKg("basket-01") - 0.8f) <= 0.0001f, "live-basket-contained-mass");
                    Check(Mathf.Abs(liveModel.GetContainerContainedMassKg("pouch-01") - 0.1f) <= 0.0001f, "live-pouch-contained-mass");

                    // Create snapshot: must serialize all Free, Carried, and Stored items with exact containerItemId
                    savedPayload = ItemPersistence.CreateSnapshot(liveModel, actorId);
                    Check(savedPayload != null, "snapshot-not-null");
                    Check(savedPayload.items.Count == 5, "snapshot-contains-all-5-items");
                    Check(savedPayload.receipts.Count == 8, "snapshot-contains-all-8-receipts");

                    // Save atomic to file
                    Check(ItemPersistence.SaveAtomic(savePath, savedPayload), "save-atomic-nested-succeeds");
                    Check(File.Exists(savePath), "save-file-exists-on-disk");

                    // TryLoad with null liveModel must fail closed when any record is Stored
                    Check(!ItemPersistence.TryLoad(savePath, worldId, genId, actorId, null, out var nullLoadedPayload) && nullLoadedPayload == null, "stored-nested-payload-with-null-model-fails-closed");

                    // Overweight carried payload with stored descendants: genuinely exceeds actor carry limit (25kg)
                    // Dedicated definitions for carried root container and stored descendants
                    var defHeavyPack = new ItemDefinition
                    {
                        itemTypeId = "container-heavy-pack",
                        dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                        massKg = 4.0f,
                        isContainer = true,
                        maxContainedSlots = 4,
                        maxContainedVolumeM3 = 0.5f,
                        maxContainedMassKg = 50.0f
                    };
                    var defSubPack = new ItemDefinition
                    {
                        itemTypeId = "container-sub-pack",
                        dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                        massKg = 1.0f,
                        isContainer = true,
                        maxContainedSlots = 4,
                        maxContainedVolumeM3 = 0.1f,
                        maxContainedMassKg = 30.0f
                    };
                    var defControlIngot = new ItemDefinition
                    {
                        itemTypeId = "ingot-control-19kg5",
                        dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                        massKg = 19.5f,
                        isContainer = false
                    };
                    var defOverweightIngot = new ItemDefinition
                    {
                        itemTypeId = "ingot-overweight-21kg",
                        dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                        massKg = 21.0f,
                        isContainer = false
                    };

                    var carryTestModel = new ItemModel(worldId, genId);
                    RegisterAllDefinitions(carryTestModel);
                    carryTestModel.RegisterDefinition(defHeavyPack);
                    carryTestModel.RegisterDefinition(defSubPack);
                    carryTestModel.RegisterDefinition(defControlIngot);
                    carryTestModel.RegisterDefinition(defOverweightIngot);

                    // Establish pre-existing live baseline state to verify failure preservation
                    Check(carryTestModel.RegisterItem("carry-baseline-item", "tool-chisel", ItemLocationKind.Free, new Vector3(8f, 8f, 8f), Quaternion.identity), "setup-carry-baseline-item");
                    int baselineCarryItemCount = carryTestModel.ItemCount;
                    long baselineCarryTick = carryTestModel.Tick;
                    int baselineCarryReceipt = carryTestModel.HighestReceiptRequestId;

                    // Control payload: at-limit (4.0kg pack + 1.0kg subpack + 19.5kg ingot + 0.5kg chisel = 25.0kg == actor limit)
                    // All container capacities, volumes, and slots pass; actor carry limit is exactly met
                    var controlCarriedPayload = new PhysicalSavePayload
                    {
                        worldId = worldId,
                        generationId = genId,
                        actorId = actorId,
                        tick = 10,
                        items = new List<SavedItemRecord>
                        {
                            new SavedItemRecord
                            {
                                itemId = "control-pack-01",
                                itemTypeId = "container-heavy-pack",
                                location = ItemLocationKind.Carried,
                                holderActorId = actorId,
                                massKg = 4.0f,
                                dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity
                            },
                            new SavedItemRecord
                            {
                                itemId = "control-subpack-01",
                                itemTypeId = "container-sub-pack",
                                location = ItemLocationKind.Stored,
                                containerItemId = "control-pack-01",
                                massKg = 1.0f,
                                dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity
                            },
                            new SavedItemRecord
                            {
                                itemId = "control-ingot-01",
                                itemTypeId = "ingot-control-19kg5",
                                location = ItemLocationKind.Stored,
                                containerItemId = "control-subpack-01",
                                massKg = 19.5f,
                                dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity
                            },
                            new SavedItemRecord
                            {
                                itemId = "control-chisel-01",
                                itemTypeId = "tool-chisel",
                                location = ItemLocationKind.Stored,
                                containerItemId = "control-pack-01",
                                massKg = 0.5f,
                                dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity
                            }
                        }
                    };
                    string controlSavePath = Path.Combine(tempDir, "control-carried-save.json");
                    Check(ItemPersistence.SaveAtomic(controlSavePath, controlCarriedPayload), "save-atomic-control-carried-succeeds");
                    Check(!ItemPersistence.TryLoad(controlSavePath, worldId, genId, actorId, null, out var nullControlPayload) && nullControlPayload == null, "control-stored-payload-with-null-model-fails-closed");
                    Check(ItemPersistence.TryLoad(controlSavePath, worldId, genId, actorId, carryTestModel, out var loadedControlPayload), "try-load-control-carried-with-live-model-succeeds");
                    Check(carryTestModel.CanRestoreSnapshot(loadedControlPayload), "control-carried-payload-can-restore-true");

                    // Overweight payload: 4.0kg pack + 1.0kg subpack + 21.0kg ingot + 0.5kg chisel = 26.5kg > 25.0kg actor limit
                    // All container capacities, volumes, and slots pass; failure isolates actor carry capacity
                    var overweightPayload = new PhysicalSavePayload
                    {
                        worldId = worldId,
                        generationId = genId,
                        actorId = actorId,
                        tick = 10,
                        items = new List<SavedItemRecord>
                        {
                            new SavedItemRecord
                            {
                                itemId = "overweight-pack-01",
                                itemTypeId = "container-heavy-pack",
                                location = ItemLocationKind.Carried,
                                holderActorId = actorId,
                                massKg = 4.0f,
                                dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity
                            },
                            new SavedItemRecord
                            {
                                itemId = "overweight-subpack-01",
                                itemTypeId = "container-sub-pack",
                                location = ItemLocationKind.Stored,
                                containerItemId = "overweight-pack-01",
                                massKg = 1.0f,
                                dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity
                            },
                            new SavedItemRecord
                            {
                                itemId = "overweight-ingot-01",
                                itemTypeId = "ingot-overweight-21kg",
                                location = ItemLocationKind.Stored,
                                containerItemId = "overweight-subpack-01",
                                massKg = 21.0f,
                                dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity
                            },
                            new SavedItemRecord
                            {
                                itemId = "overweight-chisel-01",
                                itemTypeId = "tool-chisel",
                                location = ItemLocationKind.Stored,
                                containerItemId = "overweight-pack-01",
                                massKg = 0.5f,
                                dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                                position = Vector3.zero,
                                rotation = Quaternion.identity
                            }
                        }
                    };
                    string overweightSavePath = Path.Combine(tempDir, "overweight-stored-save.json");
                    Check(ItemPersistence.SaveAtomic(overweightSavePath, overweightPayload), "save-atomic-overweight-stored-succeeds");

                    // 1) Null-model TryLoad fails closed on any Stored record
                    Check(!ItemPersistence.TryLoad(overweightSavePath, worldId, genId, actorId, null, out var nullOverweightPayload) && nullOverweightPayload == null, "stored-overweight-payload-with-null-model-fails-closed");

                    // 2) Dedicated live-model TryLoad / CanRestore / Restore rejects definition-backed overcapacity
                    Check(!ItemPersistence.TryLoad(overweightSavePath, worldId, genId, actorId, carryTestModel, out var modelOverweightPayload) && modelOverweightPayload == null, "stored-overweight-payload-with-live-model-fails-carry-capacity");
                    Check(!carryTestModel.CanRestoreSnapshot(overweightPayload), "overweight-carried-can-restore-false");
                    Check(!carryTestModel.RestoreSnapshot(overweightPayload), "overweight-carried-restore-false");

                    // Verify pre-existing live baseline item identity/state, count, tick, and receipts/sequence preserved
                    Check(carryTestModel.ItemCount == baselineCarryItemCount, "overweight-carried-preserves-item-count");
                    Check(carryTestModel.Tick == baselineCarryTick, "overweight-carried-preserves-tick");
                    Check(carryTestModel.HighestReceiptRequestId == baselineCarryReceipt, "overweight-carried-preserves-receipt-seq");
                    Check(carryTestModel.TryGetItem("carry-baseline-item", out var bCarrySnap) && bCarrySnap.location == ItemLocationKind.Free, "overweight-carried-preserves-baseline-item");

                    // Restore into a fresh model: same valid stored payload succeeds with correct live definitions
                    var restoredModel = new ItemModel(worldId, genId);
                    RegisterAllDefinitions(restoredModel);

                    Check(ItemPersistence.TryLoad(savePath, worldId, genId, actorId, restoredModel, out var loadedPayload), "try-load-nested-succeeds");
                    Check(restoredModel.CanRestoreSnapshot(loadedPayload), "can-restore-nested-true");
                    Check(restoredModel.RestoreSnapshot(loadedPayload), "restore-nested-succeeds");

                    // Verify exact memberships and locations
                    Check(restoredModel.ItemCount == 5, "restored-item-count-5");
                    Check(restoredModel.TryGetItem("chest-01", out var rChest) && rChest.location == ItemLocationKind.Free && rChest.containerItemId == null, "restored-chest-free");
                    Check(restoredModel.TryGetItem("basket-01", out var rBasket) && rBasket.location == ItemLocationKind.Stored && rBasket.containerItemId == "chest-01", "restored-basket-stored-in-chest");
                    Check(restoredModel.TryGetItem("pouch-01", out var rPouch) && rPouch.location == ItemLocationKind.Stored && rPouch.containerItemId == "basket-01", "restored-pouch-stored-in-basket");
                    Check(restoredModel.TryGetItem("chisel-01", out var rChisel) && rChisel.location == ItemLocationKind.Stored && rChisel.containerItemId == "basket-01", "restored-chisel-stored-in-basket");
                    Check(restoredModel.TryGetItem("gem-01", out var rGem) && rGem.location == ItemLocationKind.Stored && rGem.containerItemId == "pouch-01", "restored-gem-stored-in-pouch");

                    // Verify contents query
                    var chestContents = restoredModel.GetContainerContents("chest-01");
                    Check(chestContents.Count == 1 && chestContents[0] == "basket-01", "restored-chest-contents");
                    var basketContents = restoredModel.GetContainerContents("basket-01");
                    Check(basketContents.Count == 2 && basketContents.Contains("pouch-01") && basketContents.Contains("chisel-01"), "restored-basket-contents");
                    var pouchContents = restoredModel.GetContainerContents("pouch-01");
                    Check(pouchContents.Count == 1 && pouchContents[0] == "gem-01", "restored-pouch-contents");
                    var gemContents = restoredModel.GetContainerContents("gem-01");
                    Check(gemContents.Count == 0, "restored-gem-contents-empty");

                    // Verify ancestor chains
                    var gemAncestors = restoredModel.GetContainerAncestors("gem-01");
                    Check(gemAncestors.Count == 3 && gemAncestors[0] == "pouch-01" && gemAncestors[1] == "basket-01" && gemAncestors[2] == "chest-01", "restored-gem-ancestors");

                    // Verify exact recursive and contained masses after restore (tight tolerance <= 0.0001f kg)
                    Check(Mathf.Abs(restoredModel.GetItemTotalMassKg("gem-01") - 0.1f) <= 0.0001f, "restored-mass-gem");
                    Check(Mathf.Abs(restoredModel.GetItemTotalMassKg("pouch-01") - 0.3f) <= 0.0001f, "restored-mass-pouch");
                    Check(Mathf.Abs(restoredModel.GetItemTotalMassKg("chisel-01") - 0.5f) <= 0.0001f, "restored-mass-chisel");
                    Check(Mathf.Abs(restoredModel.GetItemTotalMassKg("basket-01") - 1.8f) <= 0.0001f, "restored-mass-basket");
                    Check(Mathf.Abs(restoredModel.GetItemTotalMassKg("chest-01") - 4.8f) <= 0.0001f, "restored-mass-chest");
                    Check(Mathf.Abs(restoredModel.GetContainerContainedMassKg("chest-01") - 1.8f) <= 0.0001f, "restored-chest-contained-mass");
                    Check(Mathf.Abs(restoredModel.GetContainerContainedMassKg("basket-01") - 0.8f) <= 0.0001f, "restored-basket-contained-mass");
                    Check(Mathf.Abs(restoredModel.GetContainerContainedMassKg("pouch-01") - 0.1f) <= 0.0001f, "restored-pouch-contained-mass");
                }

                // -------------------------------------------------------------
                // 2. Repeated five restores (idempotency and drift resistance)
                // -------------------------------------------------------------
                {
                    for (int iter = 1; iter <= 5; iter++)
                    {
                        var iterModel = new ItemModel(worldId, genId);
                        RegisterAllDefinitions(iterModel);

                        Check(iterModel.CanRestoreSnapshot(savedPayload), $"repeated-restore-can-restore-iter-{iter}");
                        Check(iterModel.RestoreSnapshot(savedPayload), $"repeated-restore-succeeds-iter-{iter}");
                        Check(iterModel.ItemCount == 5, $"repeated-restore-count-iter-{iter}");
                        Check(Mathf.Abs(iterModel.GetItemTotalMassKg("chest-01") - 4.8f) <= 0.0001f, $"repeated-restore-mass-iter-{iter}");
                        Check(iterModel.GetContainerContents("basket-01").Count == 2, $"repeated-restore-basket-children-iter-{iter}");
                        Check(iterModel.HighestReceiptRequestId == 8, $"repeated-restore-highest-receipt-iter-{iter}");
                    }
                }

                // -------------------------------------------------------------
                // 3. Graph refusal invariants with pre-state preserved
                // -------------------------------------------------------------
                {
                    // Establish an authoritative live pre-state
                    var testModel = new ItemModel(worldId, genId);
                    RegisterAllDefinitions(testModel);
                    Check(testModel.RegisterItem("baseline-item", "tool-chisel", ItemLocationKind.Free, new Vector3(9f, 9f, 9f), Quaternion.identity), "setup-baseline-item");
                    Check(testModel.ItemCount == 1, "baseline-model-has-1-item");

                    void AssertPreStatePreserved(string checkContext)
                    {
                        Check(testModel.ItemCount == 1, $"{checkContext}-preserves-item-count");
                        Check(testModel.TryGetItem("baseline-item", out var bSnap) && bSnap.location == ItemLocationKind.Free, $"{checkContext}-preserves-baseline-item");
                    }

                    // 3a. Duplicate item IDs
                    {
                        var bad = CopyPayload(savedPayload);
                        bad.items.Add(new SavedItemRecord
                        {
                            itemId = "gem-01", // Duplicate ID
                            itemTypeId = "gem-ruby",
                            location = ItemLocationKind.Free,
                            massKg = 0.1f,
                            dimensions = new PhysicalDimensions(0.02f, 0.02f, 0.02f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-duplicate-id-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-duplicate-id-restore");
                        AssertPreStatePreserved("refuse-duplicate-id");
                    }

                    // 3b. Dangling/missing parent container
                    {
                        var bad = CopyPayload(savedPayload);
                        var gemRec = bad.items.Find(x => x.itemId == "gem-01");
                        gemRec.containerItemId = "nonexistent-container-99";
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-dangling-parent-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-dangling-parent-restore");
                        AssertPreStatePreserved("refuse-dangling-parent");
                    }

                    // 3c. Self-container containment
                    {
                        var bad = CopyPayload(savedPayload);
                        var basketRec = bad.items.Find(x => x.itemId == "basket-01");
                        basketRec.containerItemId = "basket-01"; // Self containment
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-self-containment-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-self-containment-restore");
                        AssertPreStatePreserved("refuse-self-containment");
                    }

                    // 3d. Direct 2-cycle containment (A in B, B in A)
                    {
                        var bad = CopyPayload(savedPayload);
                        var chestRec = bad.items.Find(x => x.itemId == "chest-01");
                        var basketRec = bad.items.Find(x => x.itemId == "basket-01");
                        chestRec.location = ItemLocationKind.Stored;
                        chestRec.containerItemId = "basket-01";
                        basketRec.containerItemId = "chest-01";
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-direct-cycle-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-direct-cycle-restore");
                        AssertPreStatePreserved("refuse-direct-cycle");
                    }

                    // 3e. Indirect 3-cycle containment (A in B, B in C, C in A)
                    {
                        var bad = CopyPayload(savedPayload);
                        var chestRec = bad.items.Find(x => x.itemId == "chest-01");
                        var basketRec = bad.items.Find(x => x.itemId == "basket-01");
                        var pouchRec = bad.items.Find(x => x.itemId == "pouch-01");
                        chestRec.location = ItemLocationKind.Stored;
                        chestRec.containerItemId = "pouch-01"; // chest in pouch
                        pouchRec.containerItemId = "basket-01"; // pouch in basket
                        basketRec.containerItemId = "chest-01"; // basket in chest
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-indirect-cycle-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-indirect-cycle-restore");
                        AssertPreStatePreserved("refuse-indirect-cycle");
                    }

                    // 3f. Non-container parent
                    {
                        var bad = CopyPayload(savedPayload);
                        var gemRec = bad.items.Find(x => x.itemId == "gem-01");
                        gemRec.containerItemId = "chisel-01"; // chisel is not a container
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-non-container-parent-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-non-container-parent-restore");
                        AssertPreStatePreserved("refuse-non-container-parent");
                    }

                    // 3g. Immediate slot capacity exceeded
                    {
                        var bad = CopyPayload(savedPayload);
                        // pouch has maxContainedSlots = 2. Currently has gem-01. Add 2 more items into pouch (total 3 > 2).
                        bad.items.Add(new SavedItemRecord
                        {
                            itemId = "extra-gem-01",
                            itemTypeId = "gem-ruby",
                            location = ItemLocationKind.Stored,
                            containerItemId = "pouch-01",
                            massKg = 0.1f,
                            dimensions = new PhysicalDimensions(0.02f, 0.02f, 0.02f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        bad.items.Add(new SavedItemRecord
                        {
                            itemId = "extra-gem-02",
                            itemTypeId = "gem-ruby",
                            location = ItemLocationKind.Stored,
                            containerItemId = "pouch-01",
                            massKg = 0.1f,
                            dimensions = new PhysicalDimensions(0.02f, 0.02f, 0.02f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-slot-capacity-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-slot-capacity-restore");
                        AssertPreStatePreserved("refuse-slot-capacity");
                    }

                    // 3h. Immediate volume capacity exceeded
                    {
                        var tinyContainerDef = new ItemDefinition
                        {
                            itemTypeId = "tiny-thimble",
                            dimensions = new PhysicalDimensions(0.05f, 0.05f, 0.05f),
                            massKg = 0.05f,
                            isContainer = true,
                            maxContainedSlots = 5,
                            maxContainedVolumeM3 = 0.000001f, // Extremely small volume limit (1e-6 m3)
                            maxContainedMassKg = 10f
                        };
                        var tinyGemDef = new ItemDefinition
                        {
                            itemTypeId = "tiny-gem-05",
                            dimensions = new PhysicalDimensions(0.05f, 0.05f, 0.02f), // 0.05 * 0.05 * 0.02 = 0.00005 m3
                            massKg = 0.01f,
                            isContainer = false
                        };
                        testModel.RegisterDefinition(tinyContainerDef);
                        testModel.RegisterDefinition(tinyGemDef);

                        var bad = CopyPayload(savedPayload);
                        bad.items.Add(new SavedItemRecord
                        {
                            itemId = "thimble-01",
                            itemTypeId = "tiny-thimble",
                            location = ItemLocationKind.Free,
                            massKg = 0.05f,
                            dimensions = new PhysicalDimensions(0.05f, 0.05f, 0.05f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        bad.items.Add(new SavedItemRecord
                        {
                            itemId = "chisel-in-thimble",
                            itemTypeId = "tool-chisel",
                            location = ItemLocationKind.Stored,
                            containerItemId = "thimble-01",
                            massKg = 0.5f,
                            dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f), // Volume 0.00025 > 0.000001
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-volume-capacity-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-volume-capacity-restore");
                        AssertPreStatePreserved("refuse-volume-capacity");

                        // Tiny container (0.000001m3) with item 0.00005m3 that previously incorrectly passed with +0.0001f epsilon
                        var badTiny = CopyPayload(savedPayload);
                        badTiny.items.Add(new SavedItemRecord
                        {
                            itemId = "thimble-02",
                            itemTypeId = "tiny-thimble",
                            location = ItemLocationKind.Free,
                            massKg = 0.05f,
                            dimensions = new PhysicalDimensions(0.05f, 0.05f, 0.05f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        badTiny.items.Add(new SavedItemRecord
                        {
                            itemId = "gem-in-thimble-02",
                            itemTypeId = "tiny-gem-05",
                            location = ItemLocationKind.Stored,
                            containerItemId = "thimble-02",
                            massKg = 0.01f,
                            dimensions = new PhysicalDimensions(0.05f, 0.05f, 0.02f), // Volume 0.00005m3 > 0.000001m3 limit
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        Check(!testModel.CanRestoreSnapshot(badTiny), "refuse-tiny-volume-exceeded-can-restore");
                        Check(!testModel.RestoreSnapshot(badTiny), "refuse-tiny-volume-exceeded-restore");
                        AssertPreStatePreserved("refuse-tiny-volume-exceeded");

                        // Exact-limit volume: construct exact limit volume using same multiplication as dimensions
                        var exactDims = new PhysicalDimensions(0.1f, 0.05f, 0.05f);
                        float exactVolumeM3 = exactDims.width * exactDims.height * exactDims.depth;
                        var exactBoxDef = new ItemDefinition
                        {
                            itemTypeId = "exact-vol-box",
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            massKg = 0.2f,
                            isContainer = true,
                            maxContainedSlots = 5,
                            maxContainedVolumeM3 = exactVolumeM3,
                            maxContainedMassKg = 10f
                        };
                        testModel.RegisterDefinition(exactBoxDef);

                        var exactPayload = CopyPayload(savedPayload);
                        exactPayload.items.Add(new SavedItemRecord
                        {
                            itemId = "exact-box-01",
                            itemTypeId = "exact-vol-box",
                            location = ItemLocationKind.Free,
                            massKg = 0.2f,
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        exactPayload.items.Add(new SavedItemRecord
                        {
                            itemId = "chisel-in-exact-box",
                            itemTypeId = "tool-chisel",
                            location = ItemLocationKind.Stored,
                            containerItemId = "exact-box-01",
                            massKg = 0.5f,
                            dimensions = exactDims,
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        Check(testModel.CanRestoreSnapshot(exactPayload), "allow-exact-volume-capacity-can-restore");

                        // Slightly-over-limit volume: representable positive dimensions exceeding configured limit by < 0.0001m3
                        var slightOverDims = new PhysicalDimensions(0.12f, 0.05f, 0.05f);
                        float slightOverVolumeM3 = slightOverDims.width * slightOverDims.height * slightOverDims.depth;
                        float volumeDelta = slightOverVolumeM3 - exactVolumeM3;
                        Check(slightOverVolumeM3 > exactVolumeM3, "slight-over-volume-strictly-greater");
                        Check(volumeDelta > 0f && volumeDelta < 0.0001f, "slight-over-volume-delta-within-bounds");

                        var slightOverDef = new ItemDefinition
                        {
                            itemTypeId = "slight-over-vol-item",
                            dimensions = slightOverDims,
                            massKg = 0.5f,
                            isContainer = false
                        };
                        testModel.RegisterDefinition(slightOverDef);

                        var badSlightOverVolume = CopyPayload(savedPayload);
                        badSlightOverVolume.items.Add(new SavedItemRecord
                        {
                            itemId = "slight-over-box-01",
                            itemTypeId = "exact-vol-box",
                            location = ItemLocationKind.Free,
                            massKg = 0.2f,
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        badSlightOverVolume.items.Add(new SavedItemRecord
                        {
                            itemId = "item-in-slight-over-box",
                            itemTypeId = "slight-over-vol-item",
                            location = ItemLocationKind.Stored,
                            containerItemId = "slight-over-box-01",
                            massKg = 0.5f,
                            dimensions = slightOverDims,
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        Check(!testModel.CanRestoreSnapshot(badSlightOverVolume), "refuse-slight-over-volume-can-restore");
                        Check(!testModel.RestoreSnapshot(badSlightOverVolume), "refuse-slight-over-volume-restore");
                        AssertPreStatePreserved("refuse-slight-over-volume");
                    }

                    // 3i. Immediate mass capacity exceeded
                    {
                        var fragilePouchDef = new ItemDefinition
                        {
                            itemTypeId = "fragile-pouch",
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            massKg = 0.1f,
                            isContainer = true,
                            maxContainedSlots = 5,
                            maxContainedVolumeM3 = 0.5f,
                            maxContainedMassKg = 0.4f // Mass limit 0.4 kg
                        };
                        testModel.RegisterDefinition(fragilePouchDef);

                        var bad = CopyPayload(savedPayload);
                        bad.items.Add(new SavedItemRecord
                        {
                            itemId = "fragile-01",
                            itemTypeId = "fragile-pouch",
                            location = ItemLocationKind.Free,
                            massKg = 0.1f,
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        bad.items.Add(new SavedItemRecord
                        {
                            itemId = "heavy-chisel-in-fragile",
                            itemTypeId = "tool-chisel", // 0.5 kg > 0.4 kg limit
                            location = ItemLocationKind.Stored,
                            containerItemId = "fragile-01",
                            massKg = 0.5f,
                            dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-immediate-mass-capacity-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-immediate-mass-capacity-restore");
                        AssertPreStatePreserved("refuse-immediate-mass-capacity");

                        // Exact-limit contained mass
                        var exactMassPouchDef = new ItemDefinition
                        {
                            itemTypeId = "exact-mass-pouch",
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            massKg = 0.1f,
                            isContainer = true,
                            maxContainedSlots = 5,
                            maxContainedVolumeM3 = 0.5f,
                            maxContainedMassKg = 0.5f
                        };
                        testModel.RegisterDefinition(exactMassPouchDef);

                        var exactMassPayload = CopyPayload(savedPayload);
                        exactMassPayload.items.Add(new SavedItemRecord
                        {
                            itemId = "exact-mass-pouch-01",
                            itemTypeId = "exact-mass-pouch",
                            location = ItemLocationKind.Free,
                            massKg = 0.1f,
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        exactMassPayload.items.Add(new SavedItemRecord
                        {
                            itemId = "chisel-in-exact-mass-pouch",
                            itemTypeId = "tool-chisel", // 0.5 kg == 0.5 kg limit
                            location = ItemLocationKind.Stored,
                            containerItemId = "exact-mass-pouch-01",
                            massKg = 0.5f,
                            dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        Check(testModel.CanRestoreSnapshot(exactMassPayload), "allow-exact-contained-mass-can-restore");

                        // Slightly over: +0.001kg (1 gram) over 0.5kg limit => 0.501kg
                        var over1gDef = new ItemDefinition
                        {
                            itemTypeId = "chisel-over-1g",
                            dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                            massKg = 0.501f,
                            isContainer = false
                        };
                        testModel.RegisterDefinition(over1gDef);

                        var badOver1g = CopyPayload(savedPayload);
                        badOver1g.items.Add(new SavedItemRecord
                        {
                            itemId = "exact-mass-pouch-02",
                            itemTypeId = "exact-mass-pouch",
                            location = ItemLocationKind.Free,
                            massKg = 0.1f,
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        badOver1g.items.Add(new SavedItemRecord
                        {
                            itemId = "item-over-1g",
                            itemTypeId = "chisel-over-1g", // 0.501 kg > 0.5 kg limit
                            location = ItemLocationKind.Stored,
                            containerItemId = "exact-mass-pouch-02",
                            massKg = 0.501f,
                            dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        Check(!testModel.CanRestoreSnapshot(badOver1g), "refuse-contained-mass-over-1g-can-restore");
                        Check(!testModel.RestoreSnapshot(badOver1g), "refuse-contained-mass-over-1g-restore");
                        AssertPreStatePreserved("refuse-contained-mass-over-1g");

                        // Smaller representable over-limit value: +0.00005kg (50mg) over 0.5kg limit => 0.50005kg
                        var overTinyDef = new ItemDefinition
                        {
                            itemTypeId = "chisel-over-tiny",
                            dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                            massKg = 0.50005f,
                            isContainer = false
                        };
                        testModel.RegisterDefinition(overTinyDef);

                        var badOverTiny = CopyPayload(savedPayload);
                        badOverTiny.items.Add(new SavedItemRecord
                        {
                            itemId = "exact-mass-pouch-03",
                            itemTypeId = "exact-mass-pouch",
                            location = ItemLocationKind.Free,
                            massKg = 0.1f,
                            dimensions = new PhysicalDimensions(0.2f, 0.2f, 0.2f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        badOverTiny.items.Add(new SavedItemRecord
                        {
                            itemId = "item-over-tiny",
                            itemTypeId = "chisel-over-tiny", // 0.50005 kg > 0.5 kg limit
                            location = ItemLocationKind.Stored,
                            containerItemId = "exact-mass-pouch-03",
                            massKg = 0.50005f,
                            dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                            position = Vector3.zero,
                            rotation = Quaternion.identity
                        });
                        Check(!testModel.CanRestoreSnapshot(badOverTiny), "refuse-contained-mass-over-tiny-can-restore");
                        Check(!testModel.RestoreSnapshot(badOverTiny), "refuse-contained-mass-over-tiny-restore");
                        AssertPreStatePreserved("refuse-contained-mass-over-tiny");
                    }

                    // 3j. Ancestor mass capacity exceeded (nested contents exceed outer limit while passing inner)
                    {
                        // Outer container maxContainedMassKg = 2.0 kg
                        // Inner container maxContainedMassKg = 5.0 kg, dry mass 1.0 kg
                        // Stored item in inner container = 1.5 kg
                        // Inner container contained mass = 1.5 kg <= 5.0 kg (passes inner)
                        // Outer container contained mass = 1.0 kg (inner dry) + 1.5 kg (contents) = 2.5 kg > 2.0 kg!
                        var outerTightDef = new ItemDefinition
                        {
                            itemTypeId = "outer-tight-chest",
                            dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                            massKg = 2.0f,
                            isContainer = true,
                            maxContainedSlots = 5,
                            maxContainedVolumeM3 = 1.0f,
                            maxContainedMassKg = 2.0f // Only 2.0 kg capacity!
                        };
                        var heavyOreDef = new ItemDefinition
                        {
                            itemTypeId = "heavy-ore",
                            dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                            massKg = 1.5f,
                            isContainer = false
                        };
                        testModel.RegisterDefinition(outerTightDef);
                        testModel.RegisterDefinition(heavyOreDef);

                        var bad = new PhysicalSavePayload
                        {
                            worldId = worldId,
                            generationId = genId,
                            actorId = actorId,
                            tick = 10,
                            items = new List<SavedItemRecord>
                            {
                                new SavedItemRecord
                                {
                                    itemId = "outer-tight-01",
                                    itemTypeId = "outer-tight-chest",
                                    location = ItemLocationKind.Free,
                                    massKg = 2.0f,
                                    dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "inner-basket-01",
                                    itemTypeId = "container-basket", // dry mass 1.0 kg, maxContained 10 kg
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "outer-tight-01",
                                    massKg = 1.0f,
                                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "ore-01",
                                    itemTypeId = "heavy-ore", // 1.5 kg
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "inner-basket-01",
                                    massKg = 1.5f,
                                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                }
                            }
                        };
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-ancestor-mass-capacity-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-ancestor-mass-capacity-restore");
                        AssertPreStatePreserved("refuse-ancestor-mass-capacity");

                        // Exact-limit ancestor mass: inner dry 1.0kg + contained 1.0kg = 2.0kg == 2.0kg limit
                        var exactOreDef = new ItemDefinition
                        {
                            itemTypeId = "ore-exact-1kg",
                            dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                            massKg = 1.0f,
                            isContainer = false
                        };
                        testModel.RegisterDefinition(exactOreDef);

                        var exactAncestorPayload = new PhysicalSavePayload
                        {
                            worldId = worldId,
                            generationId = genId,
                            actorId = actorId,
                            tick = 10,
                            items = new List<SavedItemRecord>
                            {
                                new SavedItemRecord
                                {
                                    itemId = "outer-tight-exact",
                                    itemTypeId = "outer-tight-chest",
                                    location = ItemLocationKind.Free,
                                    massKg = 2.0f,
                                    dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "inner-basket-exact",
                                    itemTypeId = "container-basket",
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "outer-tight-exact",
                                    massKg = 1.0f,
                                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "ore-exact-item",
                                    itemTypeId = "ore-exact-1kg", // 1.0 kg -> total in outer = 2.0 kg == limit
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "inner-basket-exact",
                                    massKg = 1.0f,
                                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                }
                            }
                        };
                        Check(testModel.CanRestoreSnapshot(exactAncestorPayload), "allow-exact-ancestor-mass-can-restore");

                        // Ancestor slightly over: +0.001kg (1 gram) over outer limit
                        var ancestorOver1gDef = new ItemDefinition
                        {
                            itemTypeId = "ore-over-1g",
                            dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                            massKg = 1.001f,
                            isContainer = false
                        };
                        testModel.RegisterDefinition(ancestorOver1gDef);

                        var badAncestor1g = new PhysicalSavePayload
                        {
                            worldId = worldId,
                            generationId = genId,
                            actorId = actorId,
                            tick = 10,
                            items = new List<SavedItemRecord>
                            {
                                new SavedItemRecord
                                {
                                    itemId = "outer-tight-over1g",
                                    itemTypeId = "outer-tight-chest",
                                    location = ItemLocationKind.Free,
                                    massKg = 2.0f,
                                    dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "inner-basket-over1g",
                                    itemTypeId = "container-basket",
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "outer-tight-over1g",
                                    massKg = 1.0f,
                                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "ore-over-1g-item",
                                    itemTypeId = "ore-over-1g", // 1.001 kg -> total in outer = 2.001 kg > 2.0 kg
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "inner-basket-over1g",
                                    massKg = 1.001f,
                                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                }
                            }
                        };
                        Check(!testModel.CanRestoreSnapshot(badAncestor1g), "refuse-ancestor-mass-over-1g-can-restore");
                        Check(!testModel.RestoreSnapshot(badAncestor1g), "refuse-ancestor-mass-over-1g-restore");
                        AssertPreStatePreserved("refuse-ancestor-mass-over-1g");

                        // Ancestor slightly over: +0.00005kg (50mg) over outer limit
                        var ancestorOverTinyDef = new ItemDefinition
                        {
                            itemTypeId = "ore-over-tiny",
                            dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                            massKg = 1.00005f,
                            isContainer = false
                        };
                        testModel.RegisterDefinition(ancestorOverTinyDef);

                        var badAncestorTiny = new PhysicalSavePayload
                        {
                            worldId = worldId,
                            generationId = genId,
                            actorId = actorId,
                            tick = 10,
                            items = new List<SavedItemRecord>
                            {
                                new SavedItemRecord
                                {
                                    itemId = "outer-tight-overtiny",
                                    itemTypeId = "outer-tight-chest",
                                    location = ItemLocationKind.Free,
                                    massKg = 2.0f,
                                    dimensions = new PhysicalDimensions(0.5f, 0.5f, 0.5f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "inner-basket-overtiny",
                                    itemTypeId = "container-basket",
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "outer-tight-overtiny",
                                    massKg = 1.0f,
                                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "ore-over-tiny-item",
                                    itemTypeId = "ore-over-tiny", // 1.00005 kg -> total in outer = 2.00005 kg > 2.0 kg
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "inner-basket-overtiny",
                                    massKg = 1.00005f,
                                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                }
                            }
                        };
                        Check(!testModel.CanRestoreSnapshot(badAncestorTiny), "refuse-ancestor-mass-over-tiny-can-restore");
                        Check(!testModel.RestoreSnapshot(badAncestorTiny), "refuse-ancestor-mass-over-tiny-restore");
                        AssertPreStatePreserved("refuse-ancestor-mass-over-tiny");
                    }

                    // 3k. Actor carry limit mass exceeded including nested descendants
                    {
                        // Actor default limit: maxCarryMassKg = 25f, maxCarriedItems = 1
                        testModel.SetActorCarryLimits(actorId, new ActorCarryLimits(maxCarryMassKg: 2.0f, maxCarriedItems: 1));

                        // Basket dry mass = 1.0 kg, chisel = 0.5 kg, pouch = 0.2 kg, gem = 0.1 kg -> total = 1.8 kg
                        // Add an extra 0.5 kg chisel inside basket -> total = 2.3 kg > 2.0 kg actor carry limit
                        var bad = new PhysicalSavePayload
                        {
                            worldId = worldId,
                            generationId = genId,
                            actorId = actorId,
                            tick = 10,
                            items = new List<SavedItemRecord>
                            {
                                new SavedItemRecord
                                {
                                    itemId = "carried-basket",
                                    itemTypeId = "container-basket",
                                    location = ItemLocationKind.Carried,
                                    holderActorId = actorId,
                                    massKg = 1.0f,
                                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "item-in-carried-1",
                                    itemTypeId = "tool-chisel",
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "carried-basket",
                                    massKg = 0.5f,
                                    dimensions = new PhysicalDimensions(0.1f, 0.05f, 0.05f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "item-in-carried-2",
                                    itemTypeId = "heavy-ore", // 1.5 kg
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "carried-basket",
                                    massKg = 1.5f,
                                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                }
                            }
                        };
                        // Total carried mass: 1.0 + 0.5 + 1.5 = 3.0 kg > 2.0 kg limit
                        Check(!testModel.CanRestoreSnapshot(bad), "refuse-actor-carry-mass-with-descendants-can-restore");
                        Check(!testModel.RestoreSnapshot(bad), "refuse-actor-carry-mass-with-descendants-restore");
                        AssertPreStatePreserved("refuse-actor-carry-mass-with-descendants");

                        // Exact-limit actor carried mass with stored descendants: basket (dry 1.0kg) + ore-exact-1kg (1.0kg) = 2.0kg == limit
                        var exactCarriedPayload = new PhysicalSavePayload
                        {
                            worldId = worldId,
                            generationId = genId,
                            actorId = actorId,
                            tick = 10,
                            items = new List<SavedItemRecord>
                            {
                                new SavedItemRecord
                                {
                                    itemId = "carried-basket-exact",
                                    itemTypeId = "container-basket",
                                    location = ItemLocationKind.Carried,
                                    holderActorId = actorId,
                                    massKg = 1.0f,
                                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "ore-in-carried-exact",
                                    itemTypeId = "ore-exact-1kg", // 1.0 kg -> total carried = 2.0 kg == limit
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "carried-basket-exact",
                                    massKg = 1.0f,
                                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                }
                            }
                        };
                        Check(testModel.CanRestoreSnapshot(exactCarriedPayload), "allow-exact-actor-carry-mass-with-descendants");

                        // Slightly over: +0.001kg (1 gram) over carry limit
                        var badCarried1g = new PhysicalSavePayload
                        {
                            worldId = worldId,
                            generationId = genId,
                            actorId = actorId,
                            tick = 10,
                            items = new List<SavedItemRecord>
                            {
                                new SavedItemRecord
                                {
                                    itemId = "carried-basket-over1g",
                                    itemTypeId = "container-basket",
                                    location = ItemLocationKind.Carried,
                                    holderActorId = actorId,
                                    massKg = 1.0f,
                                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "ore-in-carried-over1g",
                                    itemTypeId = "ore-over-1g", // 1.001 kg -> total carried = 2.001 kg > 2.0 kg
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "carried-basket-over1g",
                                    massKg = 1.001f,
                                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                }
                            }
                        };
                        Check(!testModel.CanRestoreSnapshot(badCarried1g), "refuse-actor-carry-mass-over-1g-can-restore");
                        Check(!testModel.RestoreSnapshot(badCarried1g), "refuse-actor-carry-mass-over-1g-restore");
                        AssertPreStatePreserved("refuse-actor-carry-mass-over-1g");

                        // Slightly over: +0.00005kg (50mg) over carry limit
                        var badCarriedTiny = new PhysicalSavePayload
                        {
                            worldId = worldId,
                            generationId = genId,
                            actorId = actorId,
                            tick = 10,
                            items = new List<SavedItemRecord>
                            {
                                new SavedItemRecord
                                {
                                    itemId = "carried-basket-overtiny",
                                    itemTypeId = "container-basket",
                                    location = ItemLocationKind.Carried,
                                    holderActorId = actorId,
                                    massKg = 1.0f,
                                    dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                },
                                new SavedItemRecord
                                {
                                    itemId = "ore-in-carried-overtiny",
                                    itemTypeId = "ore-over-tiny", // 1.00005 kg -> total carried = 2.00005 kg > 2.0 kg
                                    location = ItemLocationKind.Stored,
                                    containerItemId = "carried-basket-overtiny",
                                    massKg = 1.00005f,
                                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f),
                                    position = Vector3.zero,
                                    rotation = Quaternion.identity
                                }
                            }
                        };
                        Check(!testModel.CanRestoreSnapshot(badCarriedTiny), "refuse-actor-carry-mass-over-tiny-can-restore");
                        Check(!testModel.RestoreSnapshot(badCarriedTiny), "refuse-actor-carry-mass-over-tiny-restore");
                        AssertPreStatePreserved("refuse-actor-carry-mass-over-tiny");

                        // Reset actor limits back
                        testModel.SetActorCarryLimits(actorId, new ActorCarryLimits(25f, 1));
                    }

                    // 3l. Exclusive owner field violation
                    {
                        // Stored item with holderActorId set
                        var badStoredWithHolder = CopyPayload(savedPayload);
                        badStoredWithHolder.items.Find(x => x.itemId == "gem-01").holderActorId = actorId;
                        Check(!testModel.CanRestoreSnapshot(badStoredWithHolder), "refuse-stored-with-holder");

                        // Carried item with containerItemId set
                        var badCarriedWithContainer = CopyPayload(savedPayload);
                        var chestRec = badCarriedWithContainer.items.Find(x => x.itemId == "chest-01");
                        chestRec.location = ItemLocationKind.Carried;
                        chestRec.holderActorId = actorId;
                        chestRec.containerItemId = "basket-01";
                        Check(!testModel.CanRestoreSnapshot(badCarriedWithContainer), "refuse-carried-with-container");

                        // Free item with containerItemId set
                        var badFreeWithContainer = CopyPayload(savedPayload);
                        badFreeWithContainer.items.Find(x => x.itemId == "chest-01").containerItemId = "basket-01";
                        Check(!testModel.CanRestoreSnapshot(badFreeWithContainer), "refuse-free-with-container");

                        AssertPreStatePreserved("refuse-ownership-violations");
                    }
                }

                // -------------------------------------------------------------
                // 4. Legacy Free/Carried v1 payload compatibility (absent containerItemId)
                // -------------------------------------------------------------
                {
                    // Construct standard v1 JSON without containerItemId property
                    string legacyJson =
                        "{\n" +
                        "  \"schema\": \"starfall.physical-save.v1\",\n" +
                        "  \"payload\": \"{\\\"worldId\\\":\\\"test-basket-world\\\",\\\"generationId\\\":\\\"gen-01\\\",\\\"actorId\\\":\\\"actor-starfall-01\\\",\\\"tick\\\":5,\\\"items\\\":[{\\\"itemId\\\":\\\"legacy-tool\\\",\\\"itemTypeId\\\":\\\"tool-chisel\\\",\\\"location\\\":0,\\\"holderActorId\\\":\\\"\\\",\\\"massKg\\\":0.5,\\\"dimensions\\\":{\\\"width\\\":0.1,\\\"height\\\":0.05,\\\"depth\\\":0.05},\\\"position\\\":{\\\"x\\\":0.0,\\\"y\\\":0.0,\\\"z\\\":0.0},\\\"rotation\\\":{\\\"x\\\":0.0,\\\"y\\\":0.0,\\\"z\\\":0.0,\\\"w\\\":1.0},\\\"lastUpdatedTick\\\":2},{\\\"itemId\\\":\\\"legacy-gem\\\",\\\"itemTypeId\\\":\\\"gem-ruby\\\",\\\"location\\\":1,\\\"holderActorId\\\":\\\"actor-starfall-01\\\",\\\"massKg\\\":0.1,\\\"dimensions\\\":{\\\"width\\\":0.02,\\\"height\\\":0.02,\\\"depth\\\":0.02},\\\"position\\\":{\\\"x\\\":1.0,\\\"y\\\":1.0,\\\"z\\\":1.0},\\\"rotation\\\":{\\\"x\\\":0.0,\\\"y\\\":0.0,\\\"z\\\":0.0,\\\"w\\\":1.0},\\\"lastUpdatedTick\\\":3}],\\\"receipts\\\":[]}\",\n" +
                        "  \"sha256\": \"\"" +
                        "}";

                    // Compute matching sha256 for envelope
                    var envTemp = JsonUtility.FromJson<PhysicalSaveEnvelope>(legacyJson);
                    envTemp.sha256 = ItemPersistence.ComputeSha256(envTemp.payload);
                    string validLegacyEnvelopeJson = JsonUtility.ToJson(envTemp, true);

                    string legacyFilePath = Path.Combine(tempDir, "legacy-v1-save.json");
                    File.WriteAllText(legacyFilePath, validLegacyEnvelopeJson);

                    var legacyModel = new ItemModel(worldId, genId);
                    RegisterAllDefinitions(legacyModel);

                    bool loadLegacyOk = ItemPersistence.TryLoad(legacyFilePath, worldId, genId, actorId, legacyModel, out var legacyPayload);
                    Check(loadLegacyOk && legacyPayload != null, "legacy-try-load-succeeds");
                    Check(legacyPayload.items.Count == 2, "legacy-payload-has-2-items");
                    Check(string.IsNullOrEmpty(legacyPayload.items[0].containerItemId), "legacy-tool-container-id-is-null-or-empty");
                    Check(string.IsNullOrEmpty(legacyPayload.items[1].containerItemId), "legacy-gem-container-id-is-null-or-empty");

                    // Verify legacy Free/Carried null-model load still works
                    bool loadLegacyNullOk = ItemPersistence.TryLoad(legacyFilePath, worldId, genId, actorId, null, out var legacyNullPayload);
                    Check(loadLegacyNullOk && legacyNullPayload != null, "legacy-try-load-null-model-succeeds");
                    Check(legacyNullPayload.items.Count == 2, "legacy-null-model-payload-has-2-items");

                    Check(legacyModel.CanRestoreSnapshot(legacyPayload), "legacy-can-restore-succeeds");
                    Check(legacyModel.RestoreSnapshot(legacyPayload), "legacy-restore-succeeds");
                    Check(legacyModel.ItemCount == 2, "legacy-restored-item-count-2");

                    Check(legacyModel.TryGetItem("legacy-tool", out var snapTool) && snapTool.location == ItemLocationKind.Free && snapTool.containerItemId == null, "legacy-tool-restored-as-free");
                    Check(legacyModel.TryGetItem("legacy-gem", out var snapGem) && snapGem.location == ItemLocationKind.Carried && snapGem.holderActorId == actorId && snapGem.containerItemId == null, "legacy-gem-restored-as-carried");

                    // Verify legacy items can execute normal Free/Carried state transitions after restore
                    var dropReceipt = legacyModel.Execute(worldId, genId, new ItemActionRequest
                    {
                        requestId = 201,
                        action = ItemActionKind.Drop,
                        actorId = actorId,
                        itemId = "legacy-gem",
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    }, authority);
                    Check(dropReceipt.success, "legacy-restored-carried-item-can-drop");

                    var pickupReceipt = legacyModel.Execute(worldId, genId, new ItemActionRequest
                    {
                        requestId = 202,
                        action = ItemActionKind.Pickup,
                        actorId = actorId,
                        itemId = "legacy-tool"
                    }, authority);
                    Check(pickupReceipt.success, "legacy-restored-free-item-can-pickup");
                }

                // -------------------------------------------------------------
                // 5. Replay receipts after restore
                // -------------------------------------------------------------
                {
                    var replayModel = new ItemModel(worldId, genId);
                    RegisterAllDefinitions(replayModel);
                    Check(replayModel.RestoreSnapshot(savedPayload), "restore-for-receipt-testing");
                    Check(replayModel.HighestReceiptRequestId == 8, "highest-receipt-request-id-is-8");

                    // Replay identical request with requestId = 8 (Store basket in chest): must return duplicate receipt
                    var replayReceipt8 = replayModel.Execute(worldId, genId, new ItemActionRequest
                    {
                        requestId = 8,
                        action = ItemActionKind.Store,
                        actorId = actorId,
                        itemId = "basket-01",
                        targetId = "chest-01"
                    }, authority);
                    Check(replayReceipt8.success && replayReceipt8.duplicate, "replay-receipt-8-returns-duplicate-success");

                    // Conflicting request with requestId = 8 (different action): must return request-id-conflict
                    var conflictReceipt8 = replayModel.Execute(worldId, genId, new ItemActionRequest
                    {
                        requestId = 8,
                        action = ItemActionKind.Drop,
                        actorId = actorId,
                        itemId = "basket-01",
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    }, authority);
                    Check(!conflictReceipt8.success && conflictReceipt8.code == "request-id-conflict", "conflicting-receipt-8-returns-conflict");

                    // Allocate next request sequence: must allocate above 8 -> 9
                    Check(replayModel.TryAllocateNextRequestId(out int nextReqId) && nextReqId == 9, "allocate-next-request-id-above-restored-receipts");

                    // Execute request 9 (Retrieve basket-01 from chest-01)
                    var retrieveReceipt9 = replayModel.Execute(worldId, genId, new ItemActionRequest
                    {
                        requestId = nextReqId,
                        action = ItemActionKind.Retrieve,
                        actorId = actorId,
                        itemId = "basket-01",
                        targetId = "chest-01"
                    }, authority);
                    Check(retrieveReceipt9.success && !retrieveReceipt9.duplicate, "next-action-9-retrieve-succeeds");
                    Check(replayModel.HighestReceiptRequestId == 9, "highest-receipt-advanced-to-9");

                    // Basket is now carried by actor
                    Check(replayModel.TryGetItem("basket-01", out var postRetSnap) && postRetSnap.location == ItemLocationKind.Carried, "retrieved-basket-is-carried");
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch { }
            }

            return passed;
        }
    }
}
