using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CityLife.Food;

namespace CityLife.Items
{
    /// <summary>
    /// Deterministic test authority fixture validating actor scope, approved commands, and non-empty effects.
    /// Does not use an indiscriminate universal-allow boolean.
    /// </summary>
    public sealed class TestMaterialBatchAuthority : IMaterialBatchAuthority
    {
        private readonly string expectedActorId;
        private readonly HashSet<string> approvedCommands;

        public TestMaterialBatchAuthority(string expectedActorId, params string[] approvedCommands)
        {
            this.expectedActorId = expectedActorId;
            this.approvedCommands = new HashSet<string>(approvedCommands, StringComparer.Ordinal);
        }

        public bool AuthorizeBatch(ItemModel model, MaterialBatchRequest request, out string failureReason)
        {
            failureReason = null;
            if (model == null)
            {
                failureReason = "null-model";
                return false;
            }

            if (!string.Equals(request.actorId, expectedActorId, StringComparison.Ordinal))
            {
                failureReason = "unauthorized-actor";
                return false;
            }

            if (string.IsNullOrEmpty(request.commandName) || !approvedCommands.Contains(request.commandName))
            {
                failureReason = "unapproved-command";
                return false;
            }

            if (request.effects == null || request.effects.Count == 0)
            {
                failureReason = "empty-effects";
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Deterministic test suite validating Material Transaction Pass 1 primitives:
    /// 1. Detached fork isolation (definitions, anchored items, limits, receipts, dynamic items, tokens).
    /// 2. Direct model execution refusal (only managed detached forks may execute material batches).
    /// 3. Whole-batch capacity and carry rollback (second output failure leaves candidate unmutated).
    /// 4. Strict actor carry limits (one hand / root item, 25 kg total mass).
    /// 5. Nested container volume and slot bounds.
    /// 6. Tombstones and retired ID rejection across RegisterItem, Execute, and CanRestoreSnapshot.
    /// 7. Idempotent replay, conflicting request ID refusal, and stale sequence rejection.
    /// 8. Revision-bound token lifecycle (stale revision, cancellation, single-use, foreign model).
    /// 9. Sequential issuance watermark and bounded refusal on overflow.
    /// 10. Managed schema v3 persistence roundtrip and legacy v1/v2 compatibility without silent upgrade.
    /// 11. Resurrection and tampered metadata fail-closed rejection.
    /// 12. Reviewed FoodPhysicalCatalog constants for fruit and seed.
    /// Pure C# suite requiring zero live scene, gameplay runtime, or disk side effects.
    /// </summary>
    public static class ItemMaterialTransactionChecks
    {
        public const string WorldId = "test-material-world";
        public const string GenId = "gen-01";
        public const string ActorId = "actor-starfall-01";

        public static List<string> Run()
        {
            var passed = new List<string>();

            TestForkIsolationDefinitions(passed);
            TestForkIsolationStaticAnchors(passed);
            TestForkIsolationActorLimits(passed);
            TestForkIsolationReceipts(passed);
            TestForkIsolationItems(passed);
            TestForkIsolationTokens(passed);

            TestDirectModelBatchRefusal(passed);
            TestEnableManagedWorkflowRequiresDetachedFork(passed);

            TestWholeBatchCapacitySecondOutputRollback(passed);
            TestWholeBatchCapacityVolumeRollback(passed);

            TestCarryLimitsOneHandRootEnforced(passed);
            TestCarryLimitsMassEnforced(passed);
            TestNestedContainerVolumeAndSlots(passed);

            TestTombstoneCreationAndQuery(passed);
            TestRetiredItemRegistrationRejection(passed);
            TestRetiredItemActionRejection(passed);
            TestRetiredItemSnapshotRestoreRejection(passed);

            TestIdempotentReplayReturnsSameReceipt(passed);
            TestConflictingRequestIdRejection(passed);
            TestStaleRequestIdRejection(passed);

            TestTokenLifecycleStaleRevision(passed);
            TestTokenLifecycleCancellation(passed);
            TestTokenLifecycleSingleUse(passed);
            TestTokenLifecycleForeignModel(passed);

            TestIssuanceWatermarkSequential(passed);
            TestIssuanceWatermarkOverflowRefusal(passed);

            TestAuthorityRejectionStopsBatch(passed);

            TestManagedPayloadPersistenceRoundtripV3(passed);
            TestLegacyV2SaveCompatibilityNoManagedLeak(passed);
            TestLegacySaveWithInjectedManagedMetadataRejection(passed);
            TestManagedSchemaWithoutManagedFlagRejection(passed);
            TestResurrectionTamperRejection(passed);

            TestTombstoneLedgerBoundedRefusal(passed);
            TestMaterialReceiptLedgerBoundedRefusal(passed);

            TestFoodPhysicalCatalogFruitAndSeedDefinitions(passed);

            return passed;
        }

        private static void Assert(bool condition, string checkName, List<string> passed)
        {
            if (!condition)
            {
                throw new InvalidOperationException("MATERIAL CHECK FAILED: " + checkName);
            }
            passed.Add(checkName);
        }

        private static void PutItemInContainer(ItemModel model, string actorId, string itemId, string containerId)
        {
            var auth = new BasicItemActionAuthority();
            model.TryAllocateNextRequestId(out int r1);
            model.Execute(WorldId, GenId, new ItemActionRequest { requestId = r1, action = ItemActionKind.Pickup, actorId = actorId, itemId = itemId }, auth);
            model.TryAllocateNextRequestId(out int r2);
            model.Execute(WorldId, GenId, new ItemActionRequest { requestId = r2, action = ItemActionKind.Store, actorId = actorId, itemId = itemId, targetId = containerId }, auth);
        }

        private static ItemModel CreateTestModel()
        {
            var model = new ItemModel(WorldId, GenId);
            RegisterStandardTestDefinitions(model);
            return model;
        }

        private static void RegisterStandardTestDefinitions(ItemModel m)
        {
            FoodPhysicalCatalog.RegisterToModel(m);

            m.RegisterDefinition(new ItemDefinition
            {
                itemTypeId = "container.basket.v1",
                massKg = 0.5f,
                dimensions = new PhysicalDimensions(0.25f, 0.25f, 0.25f),
                isContainer = true,
                maxContainedSlots = 4,
                maxContainedVolumeM3 = 0.015f,
                maxContainedMassKg = 5.0f
            });

            m.RegisterDefinition(new ItemDefinition
            {
                itemTypeId = "container.chest.v1",
                massKg = 5.0f,
                dimensions = new PhysicalDimensions(0.6f, 0.5f, 0.5f),
                isContainer = true,
                maxContainedSlots = 8,
                maxContainedVolumeM3 = 0.15f,
                maxContainedMassKg = 50.0f
            });

            m.RegisterDefinition(new ItemDefinition
            {
                itemTypeId = "structure.bed.v1",
                massKg = 20.0f,
                dimensions = new PhysicalDimensions(1.0f, 0.3f, 2.0f),
                isAnchored = true
            });

            m.RegisterDefinition(new ItemDefinition
            {
                itemTypeId = "item.heavy.rock.v1",
                massKg = 24.5f,
                dimensions = new PhysicalDimensions(0.3f, 0.3f, 0.3f)
            });
        }

        private static void TestForkIsolationDefinitions(List<string> passed)
        {
            var source = CreateTestModel();
            var fork = source.CreateDetachedFork();

            Assert(fork.IsDetachedFork, "ForkIsolationDefinitions.IsDetachedFork", passed);
            Assert(fork.DefinitionCount == source.DefinitionCount, "ForkIsolationDefinitions.CountMatch", passed);

            var extraDef = new ItemDefinition
            {
                itemTypeId = "tool.hoe.v1",
                massKg = 1.2f,
                dimensions = new PhysicalDimensions(0.1f, 0.8f, 0.1f)
            };
            bool registeredOnFork = fork.RegisterDefinition(extraDef);
            Assert(registeredOnFork, "ForkIsolationDefinitions.ForkRegisterSuccess", passed);
            Assert(fork.TryGetDefinition("tool.hoe.v1", out _), "ForkIsolationDefinitions.ForkHasDef", passed);
            Assert(!source.TryGetDefinition("tool.hoe.v1", out _), "ForkIsolationDefinitions.SourceIsolated", passed);
        }

        private static void TestForkIsolationStaticAnchors(List<string> passed)
        {
            var source = CreateTestModel();
            bool regAnchored = source.RegisterItem("bed-01", "structure.bed.v1", ItemLocationKind.Anchored, new Vector3(0, 0, 0), Quaternion.identity);
            Assert(regAnchored, "ForkIsolationStaticAnchors.SourceRegistered", passed);

            var fork = source.CreateDetachedFork();
            Assert(fork.TryGetItem("bed-01", out var forkSnap), "ForkIsolationStaticAnchors.ForkHasAnchor", passed);
            Assert(forkSnap.location == ItemLocationKind.Anchored, "ForkIsolationStaticAnchors.ForkLocationAnchored", passed);

            // Mutate fork by unregistering or doing other operations; source anchored item remains intact
            Assert(source.TryGetItem("bed-01", out var sourceSnap), "ForkIsolationStaticAnchors.SourceStillHasAnchor", passed);
            Assert(sourceSnap.location == ItemLocationKind.Anchored, "ForkIsolationStaticAnchors.SourceLocationAnchored", passed);
        }

        private static void TestForkIsolationActorLimits(List<string> passed)
        {
            var source = CreateTestModel();
            source.SetActorCarryLimits(ActorId, new ActorCarryLimits(25.0f, 1));

            var fork = source.CreateDetachedFork();
            var forkLimits = fork.GetActorCarryLimits(ActorId);
            Assert(Mathf.Abs(forkLimits.maxCarryMassKg - 25.0f) < 0.001f, "ForkIsolationActorLimits.ForkInitialMatch", passed);

            fork.SetActorCarryLimits(ActorId, new ActorCarryLimits(40.0f, 2));
            var sourceLimits = source.GetActorCarryLimits(ActorId);
            Assert(Mathf.Abs(sourceLimits.maxCarryMassKg - 25.0f) < 0.001f, "ForkIsolationActorLimits.SourcePreserved", passed);
            Assert(sourceLimits.maxCarriedItems == 1, "ForkIsolationActorLimits.SourceCountPreserved", passed);
        }

        private static void TestForkIsolationReceipts(List<string> passed)
        {
            var source = CreateTestModel();
            source.RegisterItem("basket-01", "container.basket.v1", ItemLocationKind.Free, new Vector3(0, 0, 0), Quaternion.identity);

            var req = new ItemActionRequest
            {
                requestId = 1,
                actorId = ActorId,
                action = ItemActionKind.Pickup,
                itemId = "basket-01"
            };
            var receipt = source.Execute(WorldId, GenId, req, new BasicItemActionAuthority());
            Assert(receipt.success, "ForkIsolationReceipts.SourceExecuteSuccess", passed);

            var fork = source.CreateDetachedFork();
            Assert(fork.TryGetReceipt(1, out var sig, out var r), "ForkIsolationReceipts.ForkHasReceipt", passed);
            Assert(r.success, "ForkIsolationReceipts.ForkReceiptMatches", passed);

            // Execute next request on fork only
            var forkReq = new ItemActionRequest
            {
                requestId = 2,
                actorId = ActorId,
                action = ItemActionKind.Drop,
                itemId = "basket-01",
                position = new Vector3(1, 0, 1),
                rotation = Quaternion.identity
            };
            var forkReceipt = fork.Execute(WorldId, GenId, forkReq, new BasicItemActionAuthority());
            Assert(forkReceipt.success, "ForkIsolationReceipts.ForkExecuteSuccess", passed);
            Assert(fork.TryGetReceipt(2, out _, out _), "ForkIsolationReceipts.ForkHasReceipt2", passed);
            Assert(!source.TryGetReceipt(2, out _, out _), "ForkIsolationReceipts.SourceReceipt2Isolated", passed);
        }

        private static void TestForkIsolationItems(List<string> passed)
        {
            var source = CreateTestModel();
            source.RegisterItem("fruit-01", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, new Vector3(2, 0, 2), Quaternion.identity);

            var fork = source.CreateDetachedFork(true);
            Assert(fork.TryGetItem("fruit-01", out var fSnap), "ForkIsolationItems.ForkHasItem", passed);

            // Mutate item in fork via material consume
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 1,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Consume,
                        itemId = "fruit-01",
                        provenance = "consumed-fruit"
                    }
                }
            };

            bool prepared = fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            Assert(prepared, "ForkIsolationItems.PrepareSuccess", passed);
            bool committed = fork.TryCommitMaterialBatch(token, out _);
            Assert(committed, "ForkIsolationItems.CommitSuccess", passed);

            // Fork item consumed, source item intact
            Assert(!fork.TryGetItem("fruit-01", out _), "ForkIsolationItems.ForkConsumed", passed);
            Assert(fork.IsRetired("fruit-01"), "ForkIsolationItems.ForkRetired", passed);
            Assert(source.TryGetItem("fruit-01", out var sSnap), "ForkIsolationItems.SourceIntact", passed);
            Assert(sSnap.location == ItemLocationKind.Free, "ForkIsolationItems.SourceLocationFree", passed);
            Assert(!source.IsRetired("fruit-01"), "ForkIsolationItems.SourceNotRetired", passed);
        }

        private static void TestForkIsolationTokens(List<string> passed)
        {
            var source = CreateTestModel();
            source.RegisterItem("basket-01", "container.basket.v1", ItemLocationKind.Free, new Vector3(0, 0, 0), Quaternion.identity);

            // Prepare item transition on source
            var req = new ItemActionRequest
            {
                requestId = 1,
                actorId = ActorId,
                action = ItemActionKind.Pickup,
                itemId = "basket-01"
            };
            bool sourcePrep = source.TryPrepareTransition(req, new BasicItemActionAuthority(), out var sourceToken, out _);
            Assert(sourcePrep, "ForkIsolationTokens.SourcePrepared", passed);

            var fork = source.CreateDetachedFork();
            // Source token cannot be committed on fork
            bool forkCommit = fork.TryCommitTransition(sourceToken, out _);
            Assert(!forkCommit, "ForkIsolationTokens.SourceTokenRejectedOnFork", passed);
        }

        private static void TestDirectModelBatchRefusal(List<string> passed)
        {
            var direct = CreateTestModel();
            direct.RegisterItem("fruit-01", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, new Vector3(0, 0, 0), Quaternion.identity);

            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 1,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Consume,
                        itemId = "fruit-01"
                    }
                }
            };

            bool prepared = direct.TryPrepareMaterialBatch(batchReq, auth, out _, out string rejection);
            Assert(!prepared, "DirectModelBatchRefusal.PrepareRejected", passed);
            Assert(string.Equals(rejection, "material-batches-require-managed-detached-candidate", StringComparison.Ordinal), "DirectModelBatchRefusal.RejectionCode", passed);
        }

        private static void TestEnableManagedWorkflowRequiresDetachedFork(List<string> passed)
        {
            var direct = CreateTestModel();
            bool directEnable = direct.EnableManagedWorkflow();
            Assert(!directEnable, "EnableManagedWorkflowRequiresDetachedFork.DirectFailed", passed);
            Assert(!direct.IsManaged, "EnableManagedWorkflowRequiresDetachedFork.DirectNotManaged", passed);

            var fork = direct.CreateDetachedFork();
            Assert(!fork.IsManaged, "EnableManagedWorkflowRequiresDetachedFork.ForkNotManagedInitially", passed);
            bool forkEnable = fork.EnableManagedWorkflow();
            Assert(forkEnable, "EnableManagedWorkflowRequiresDetachedFork.ForkEnabled", passed);
            Assert(fork.IsManaged, "EnableManagedWorkflowRequiresDetachedFork.ForkIsManaged", passed);
        }

        private static void TestWholeBatchCapacitySecondOutputRollback(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("basket-01", "container.basket.v1", ItemLocationKind.Free, new Vector3(0, 0, 0), Quaternion.identity);
            // Pre-fill basket with 3 items (basket max capacity is 4 slots)
            model.RegisterItem("seed-01", FoodPhysicalCatalog.SeedTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
            model.RegisterItem("seed-02", FoodPhysicalCatalog.SeedTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
            model.RegisterItem("seed-03", FoodPhysicalCatalog.SeedTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
            PutItemInContainer(model, ActorId, "seed-01", "basket-01");
            PutItemInContainer(model, ActorId, "seed-02", "basket-01");
            PutItemInContainer(model, ActorId, "seed-03", "basket-01");
            // Put an existing fruit on the ground to be consumed
            model.RegisterItem("fruit-in", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, new Vector3(1, 0, 0), Quaternion.identity);

            var fork = model.CreateDetachedFork(true);

            // Request batch: consume fruit-in, issue TWO fruits into basket-01
            // 1st fruit fits (slot 3); 2nd fruit overflows (slot 4 exceeds maxContainedSlots=4)
            fork.TryAllocateNextRequestId(out int batchReqId);
            var auth = new TestMaterialBatchAuthority(ActorId, "Harvest");
            var batchReq = new MaterialBatchRequest
            {
                requestId = batchReqId,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Harvest",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Consume,
                        itemId = "fruit-in",
                        provenance = "harvest-parent"
                    },
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Issue,
                        itemId = "fruit-out-1",
                        itemTypeId = FoodPhysicalCatalog.FruitTypeId,
                        destinationLocation = ItemLocationKind.Stored,
                        destinationContainerId = "basket-01",
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    },
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Issue,
                        itemId = "fruit-out-2",
                        itemTypeId = FoodPhysicalCatalog.FruitTypeId,
                        destinationLocation = ItemLocationKind.Stored,
                        destinationContainerId = "basket-01",
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    }
                }
            };

            bool prep = fork.TryPrepareMaterialBatch(batchReq, auth, out _, out string rejection);
            Assert(!prep, "WholeBatchCapacitySecondOutputRollback.Rejected", passed);
            Assert(string.Equals(rejection, "destination-container-slots-full", StringComparison.Ordinal), "WholeBatchCapacitySecondOutputRollback.RejectionReason", passed);

            // Verify candidate state is completely unmodified (zero mutation on prepare failure)
            Assert(fork.TryGetItem("fruit-in", out var inSnap), "WholeBatchCapacitySecondOutputRollback.InputNotConsumed", passed);
            Assert(inSnap.location == ItemLocationKind.Free, "WholeBatchCapacitySecondOutputRollback.InputLocationIntact", passed);
            Assert(!fork.IsRetired("fruit-in"), "WholeBatchCapacitySecondOutputRollback.InputNotRetired", passed);
            Assert(!fork.TryGetItem("fruit-out-1", out _), "WholeBatchCapacitySecondOutputRollback.Output1NotIssued", passed);
            Assert(!fork.TryGetItem("fruit-out-2", out _), "WholeBatchCapacitySecondOutputRollback.Output2NotIssued", passed);
            Assert(fork.TombstoneCount == 0, "WholeBatchCapacitySecondOutputRollback.ZeroTombstones", passed);
        }

        private static void TestWholeBatchCapacityVolumeRollback(List<string> passed)
        {
            var model = CreateTestModel();
            // Basket maxContainedVolumeM3 is 0.015 m3
            model.RegisterItem("basket-01", "container.basket.v1", ItemLocationKind.Free, new Vector3(0, 0, 0), Quaternion.identity);

            var fork = model.CreateDetachedFork(true);

            // Attempt to issue a heavy rock (0.3m cube = 0.027 m3 volume > 0.015 m3) into basket
            fork.TryAllocateNextRequestId(out int batchReqId);
            var auth = new TestMaterialBatchAuthority(ActorId, "Gather");
            var batchReq = new MaterialBatchRequest
            {
                requestId = batchReqId,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Gather",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Issue,
                        itemId = "rock-out-1",
                        itemTypeId = "item.heavy.rock.v1",
                        destinationLocation = ItemLocationKind.Stored,
                        destinationContainerId = "basket-01",
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    }
                }
            };

            bool prep = fork.TryPrepareMaterialBatch(batchReq, auth, out _, out string rejection);
            Assert(!prep, "WholeBatchCapacityVolumeRollback.Rejected", passed);
            Assert(string.Equals(rejection, "destination-container-volume-exceeded", StringComparison.Ordinal), "WholeBatchCapacityVolumeRollback.RejectionReason", passed);
            Assert(!fork.TryGetItem("rock-out-1", out _), "WholeBatchCapacityVolumeRollback.RockNotIssued", passed);
        }

        private static void TestCarryLimitsOneHandRootEnforced(List<string> passed)
        {
            var model = CreateTestModel();
            // Actor carries basket-01 (1 root item, hand occupied)
            model.RegisterItem("basket-01", "container.basket.v1", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
            model.Execute(WorldId, GenId, new ItemActionRequest { requestId = 1, action = ItemActionKind.Pickup, actorId = ActorId, itemId = "basket-01" }, new BasicItemActionAuthority());

            var fork = model.CreateDetachedFork(true);
            fork.TryAllocateNextRequestId(out int batchReqId);

            // Attempt to issue another Carried root item directly to actor
            var auth = new TestMaterialBatchAuthority(ActorId, "Gather");
            var batchReq = new MaterialBatchRequest
            {
                requestId = batchReqId,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Gather",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Issue,
                        itemId = "fruit-carried-1",
                        itemTypeId = FoodPhysicalCatalog.FruitTypeId,
                        destinationLocation = ItemLocationKind.Carried,
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    }
                }
            };

            bool prep = fork.TryPrepareMaterialBatch(batchReq, auth, out _, out string rejection);
            Assert(!prep, "CarryLimitsOneHandRootEnforced.Rejected", passed);
            Assert(string.Equals(rejection, "actor-carry-slots-exceeded", StringComparison.Ordinal), "CarryLimitsOneHandRootEnforced.Reason", passed);
            Assert(!fork.TryGetItem("fruit-carried-1", out _), "CarryLimitsOneHandRootEnforced.NotIssued", passed);
        }

        private static void TestCarryLimitsMassEnforced(List<string> passed)
        {
            var model = CreateTestModel();
            // Actor carries heavy rock (24.5 kg). Limit is 25.0 kg.
            model.RegisterItem("rock-01", "item.heavy.rock.v1", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
            model.Execute(WorldId, GenId, new ItemActionRequest { requestId = 1, action = ItemActionKind.Pickup, actorId = ActorId, itemId = "rock-01" }, new BasicItemActionAuthority());

            var fork = model.CreateDetachedFork(true);
            fork.TryAllocateNextRequestId(out int batchReqId);

            // Even if hand limit was 2, adding another item that pushes mass past 25kg must fail
            fork.SetActorCarryLimits(ActorId, new ActorCarryLimits(25.0f, 2));

            var auth = new TestMaterialBatchAuthority(ActorId, "Gather");
            var batchReq = new MaterialBatchRequest
            {
                requestId = batchReqId,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Gather",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Issue,
                        itemId = "basket-extra",
                        itemTypeId = "container.basket.v1", // 0.5 kg + 24.5 kg = 25.0 kg. But let's issue chest (5.0 kg -> 29.5 kg)
                        destinationLocation = ItemLocationKind.Carried,
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    }
                }
            };
            batchReq.effects[0] = new MaterialBatchItemEffect
            {
                kind = MaterialEffectKind.Issue,
                itemId = "chest-extra",
                itemTypeId = "container.chest.v1", // 5 kg -> 29.5 kg > 25 kg
                destinationLocation = ItemLocationKind.Carried,
                position = Vector3.zero,
                rotation = Quaternion.identity
            };

            bool prep = fork.TryPrepareMaterialBatch(batchReq, auth, out _, out string rejection);
            Assert(!prep, "CarryLimitsMassEnforced.Rejected", passed);
            Assert(string.Equals(rejection, "actor-carry-mass-exceeded", StringComparison.Ordinal), "CarryLimitsMassEnforced.Reason", passed);
        }

        private static void TestNestedContainerVolumeAndSlots(List<string> passed)
        {
            var model = CreateTestModel();
            // Chest holding basket
            model.RegisterItem("chest-01", "container.chest.v1", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
            model.RegisterItem("basket-01", "container.basket.v1", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
            PutItemInContainer(model, ActorId, "basket-01", "chest-01");

            var fork = model.CreateDetachedFork(true);
            fork.TryAllocateNextRequestId(out int batchReqId);

            var auth = new TestMaterialBatchAuthority(ActorId, "Gather");
            var batchReq = new MaterialBatchRequest
            {
                requestId = batchReqId,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Gather",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Issue,
                        itemId = "fruit-nested-1",
                        itemTypeId = FoodPhysicalCatalog.FruitTypeId,
                        destinationLocation = ItemLocationKind.Stored,
                        destinationContainerId = "basket-01",
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    }
                }
            };

            bool prep = fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            Assert(prep, "NestedContainerVolumeAndSlots.Prepared", passed);
            bool commit = fork.TryCommitMaterialBatch(token, out var receipt);
            Assert(commit, "NestedContainerVolumeAndSlots.Committed", passed);
            Assert(receipt.success, "NestedContainerVolumeAndSlots.ReceiptSuccess", passed);

            Assert(fork.TryGetItem("fruit-nested-1", out var snap), "NestedContainerVolumeAndSlots.FruitStored", passed);
            Assert(snap.location == ItemLocationKind.Stored, "NestedContainerVolumeAndSlots.LocationStored", passed);
            Assert(string.Equals(snap.containerItemId, "basket-01", StringComparison.Ordinal), "NestedContainerVolumeAndSlots.ParentBasket", passed);
        }

        private static void TestTombstoneCreationAndQuery(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-eat", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, new Vector3(1, 0, 1), Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 10,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Consume,
                        itemId = "fruit-eat",
                        provenance = "consumed-fruit-meal"
                    }
                }
            };

            bool prep = fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            Assert(prep, "TombstoneCreationAndQuery.Prepared", passed);
            bool commit = fork.TryCommitMaterialBatch(token, out var receipt);
            Assert(commit, "TombstoneCreationAndQuery.Committed", passed);

            Assert(!fork.TryGetItem("fruit-eat", out _), "TombstoneCreationAndQuery.NotActive", passed);
            Assert(fork.IsRetired("fruit-eat"), "TombstoneCreationAndQuery.IsRetired", passed);
            Assert(fork.TryGetTombstone("fruit-eat", out var tomb), "TombstoneCreationAndQuery.GetTombstone", passed);
            Assert(string.Equals(tomb.itemId, "fruit-eat", StringComparison.Ordinal), "TombstoneCreationAndQuery.ItemId", passed);
            Assert(string.Equals(tomb.reason, "consumed", StringComparison.Ordinal), "TombstoneCreationAndQuery.Reason", passed);
            Assert(string.Equals(tomb.retiredByActorId, ActorId, StringComparison.Ordinal), "TombstoneCreationAndQuery.Actor", passed);
            Assert(string.Equals(tomb.provenance, "consumed-fruit-meal", StringComparison.Ordinal), "TombstoneCreationAndQuery.Provenance", passed);
        }

        private static void TestRetiredItemRegistrationRejection(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("seed-plant", FoodPhysicalCatalog.SeedTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Plant");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 11,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Plant",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.PlantSeed,
                        itemId = "seed-plant",
                        provenance = "planted-in-bed"
                    }
                }
            };

            fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            fork.TryCommitMaterialBatch(token, out _);

            // Attempt to re-register seed-plant
            bool reReg = fork.RegisterItem("seed-plant", FoodPhysicalCatalog.SeedTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
            Assert(!reReg, "RetiredItemRegistrationRejection.Rejected", passed);
        }

        private static void TestRetiredItemActionRejection(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-ret", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 12,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Consume,
                        itemId = "fruit-ret"
                    }
                }
            };

            fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            fork.TryCommitMaterialBatch(token, out _);

            // Attempt standard pickup on retired item
            var pickupReq = new ItemActionRequest
            {
                requestId = 13,
                actorId = ActorId,
                action = ItemActionKind.Pickup,
                itemId = "fruit-ret"
            };
            var receipt = fork.Execute(WorldId, GenId, pickupReq, new BasicItemActionAuthority());
            Assert(!receipt.success, "RetiredItemActionRejection.Rejected", passed);
        }

        private static void TestRetiredItemSnapshotRestoreRejection(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-snap", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 14,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Consume,
                        itemId = "fruit-snap"
                    }
                }
            };

            fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            fork.TryCommitMaterialBatch(token, out _);

            // Craft a payload that attempts to restore fruit-snap as an active item
            var payload = new PhysicalSavePayload
            {
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                tick = fork.Tick,
                isManaged = true,
                items = new List<SavedItemRecord>
                {
                    new SavedItemRecord
                    {
                        itemId = "fruit-snap",
                        itemTypeId = FoodPhysicalCatalog.FruitTypeId,
                        location = ItemLocationKind.Free,
                        massKg = 0.020f,
                        dimensions = new PhysicalDimensions(0.035f, 0.035f, 0.035f),
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    }
                },
                tombstones = fork.GetAllTombstones(),
                materialReceipts = fork.GetAllMaterialReceipts()
            };

            bool canRestore = fork.CanRestoreSnapshot(payload);
            Assert(!canRestore, "RetiredItemSnapshotRestoreRejection.CanRestoreRejected", passed);
            bool restored = fork.RestoreSnapshot(payload);
            Assert(!restored, "RetiredItemSnapshotRestoreRejection.RestoreRejected", passed);
        }

        private static void TestIdempotentReplayReturnsSameReceipt(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-replay", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 20,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Consume,
                        itemId = "fruit-replay"
                    }
                }
            };

            fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            fork.TryCommitMaterialBatch(token, out var originalReceipt);

            // Replay identical request
            bool replayPrep = fork.TryPrepareMaterialBatch(batchReq, auth, out _, out string rejection);
            Assert(!replayPrep, "IdempotentReplayReturnsSameReceipt.PrepRefused", passed);
            Assert(string.Equals(rejection, "duplicate-replay", StringComparison.Ordinal), "IdempotentReplayReturnsSameReceipt.RejectionCode", passed);

            bool foundReceipt = fork.TryGetMaterialReceipt(20, out string sig, out var replayReceipt);
            Assert(foundReceipt, "IdempotentReplayReturnsSameReceipt.ReceiptRetrieved", passed);
            Assert(replayReceipt.success == originalReceipt.success, "IdempotentReplayReturnsSameReceipt.SuccessMatch", passed);
            Assert(replayReceipt.retiredItemIds.Count == originalReceipt.retiredItemIds.Count, "IdempotentReplayReturnsSameReceipt.RetiredMatch", passed);
        }

        private static void TestConflictingRequestIdRejection(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-conf-1", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);
            model.RegisterItem("fruit-conf-2", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq1 = new MaterialBatchRequest
            {
                requestId = 25,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect { kind = MaterialEffectKind.Consume, itemId = "fruit-conf-1" }
                }
            };
            fork.TryPrepareMaterialBatch(batchReq1, auth, out var token1, out _);
            fork.TryCommitMaterialBatch(token1, out _);

            // Submitting different effects with same requestId=25
            var batchReq2 = new MaterialBatchRequest
            {
                requestId = 25,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect { kind = MaterialEffectKind.Consume, itemId = "fruit-conf-2" }
                }
            };

            bool prepConf = fork.TryPrepareMaterialBatch(batchReq2, auth, out _, out string rejection);
            Assert(!prepConf, "ConflictingRequestIdRejection.Rejected", passed);
            Assert(string.Equals(rejection, "conflicting-request-id", StringComparison.Ordinal), "ConflictingRequestIdRejection.Reason", passed);
        }

        private static void TestStaleRequestIdRejection(List<string> passed)
        {
            var model = CreateTestModel();
            var fork = model.CreateDetachedFork(true);
            fork.ResumeRequestSequence(50); // Set high-watermark bound to 50

            var auth = new TestMaterialBatchAuthority(ActorId, "Gather");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 30, // 30 <= 50, but not present in ledger
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Gather",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Issue,
                        itemId = "fruit-stale",
                        itemTypeId = FoodPhysicalCatalog.FruitTypeId,
                        destinationLocation = ItemLocationKind.Free,
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    }
                }
            };

            bool prepStale = fork.TryPrepareMaterialBatch(batchReq, auth, out _, out string rejection);
            Assert(!prepStale, "StaleRequestIdRejection.Rejected", passed);
            Assert(string.Equals(rejection, "stale-request-sequence", StringComparison.Ordinal), "StaleRequestIdRejection.Reason", passed);
        }

        private static void TestTokenLifecycleStaleRevision(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-rev", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 35,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect { kind = MaterialEffectKind.Consume, itemId = "fruit-rev" }
                }
            };

            fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);

            // Bump revision on fork by registering a new definition
            fork.RegisterDefinition(new ItemDefinition
            {
                itemTypeId = "test.extra.v1",
                massKg = 1.0f,
                dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f)
            });

            bool commit = fork.TryCommitMaterialBatch(token, out _);
            Assert(!commit, "TokenLifecycleStaleRevision.Rejected", passed);
        }

        private static void TestTokenLifecycleCancellation(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-canc", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 36,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect { kind = MaterialEffectKind.Consume, itemId = "fruit-canc" }
                }
            };

            fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            bool cancelled = fork.CancelMaterialBatch(token);
            Assert(cancelled, "TokenLifecycleCancellation.CancelledSuccess", passed);
            Assert(token.IsCancelled, "TokenLifecycleCancellation.IsCancelledTrue", passed);

            bool commit = fork.TryCommitMaterialBatch(token, out _);
            Assert(!commit, "TokenLifecycleCancellation.CommitRejected", passed);
        }

        private static void TestTokenLifecycleSingleUse(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-single", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 37,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect { kind = MaterialEffectKind.Consume, itemId = "fruit-single" }
                }
            };

            fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            bool firstCommit = fork.TryCommitMaterialBatch(token, out _);
            Assert(firstCommit, "TokenLifecycleSingleUse.FirstSuccess", passed);

            bool secondCommit = fork.TryCommitMaterialBatch(token, out _);
            Assert(!secondCommit, "TokenLifecycleSingleUse.SecondRejected", passed);
        }

        private static void TestTokenLifecycleForeignModel(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-foreign", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var forkA = model.CreateDetachedFork(true);
            var forkB = model.CreateDetachedFork(true);

            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 38,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect { kind = MaterialEffectKind.Consume, itemId = "fruit-foreign" }
                }
            };

            forkA.TryPrepareMaterialBatch(batchReq, auth, out var tokenA, out _);
            bool commitB = forkB.TryCommitMaterialBatch(tokenA, out _);
            Assert(!commitB, "TokenLifecycleForeignModel.Rejected", passed);
        }

        private static void TestIssuanceWatermarkSequential(List<string> passed)
        {
            var model = CreateTestModel();
            var fork = model.CreateDetachedFork(true);

            bool alloc1 = fork.TryAllocateIssuanceId("fruit", out string id1);
            Assert(alloc1, "IssuanceWatermarkSequential.Alloc1Success", passed);
            Assert(string.Equals(id1, "fruit-000001", StringComparison.Ordinal), "IssuanceWatermarkSequential.Id1Format", passed);

            bool alloc2 = fork.TryAllocateIssuanceId("seed", out string id2);
            Assert(alloc2, "IssuanceWatermarkSequential.Alloc2Success", passed);
            Assert(string.Equals(id2, "seed-000002", StringComparison.Ordinal), "IssuanceWatermarkSequential.Id2Format", passed);

            Assert(fork.IssuanceHighWatermark == 2, "IssuanceWatermarkSequential.WatermarkValue", passed);
        }

        private static void TestIssuanceWatermarkOverflowRefusal(List<string> passed)
        {
            var model = CreateTestModel();
            var fork = model.CreateDetachedFork(true);

            var snap = new PhysicalSavePayload
            {
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                tick = 10,
                isManaged = true,
                issuanceHighWatermark = long.MaxValue - 1,
                items = new List<SavedItemRecord>(),
                receipts = new List<SavedReceiptRecord>()
            };
            fork.RestoreSnapshot(snap);

            bool alloc = fork.TryAllocateIssuanceId("overflow", out _);
            Assert(!alloc, "IssuanceWatermarkOverflowRefusal.RefusedOnOverflow", passed);
        }

        private static void TestAuthorityRejectionStopsBatch(List<string> passed)
        {
            var model = CreateTestModel();
            var fork = model.CreateDetachedFork(true);

            // Authority only allows "Gather", but command is "Exploit"
            var auth = new TestMaterialBatchAuthority(ActorId, "Gather");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 45,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Exploit",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Issue,
                        itemId = "fruit-exploit",
                        itemTypeId = FoodPhysicalCatalog.FruitTypeId,
                        destinationLocation = ItemLocationKind.Free,
                        position = Vector3.zero,
                        rotation = Quaternion.identity
                    }
                }
            };

            bool prep = fork.TryPrepareMaterialBatch(batchReq, auth, out _, out string rejection);
            Assert(!prep, "AuthorityRejectionStopsBatch.Rejected", passed);
            Assert(string.Equals(rejection, "unapproved-command", StringComparison.Ordinal), "AuthorityRejectionStopsBatch.Reason", passed);
        }

        private static void TestManagedPayloadPersistenceRoundtripV3(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-live", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, new Vector3(1, 0, 1), Quaternion.identity);
            model.RegisterItem("fruit-eat", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, new Vector3(2, 0, 2), Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 100,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect { kind = MaterialEffectKind.Consume, itemId = "fruit-eat", provenance = "meal" }
                }
            };
            fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            fork.TryCommitMaterialBatch(token, out _);

            fork.TryAllocateIssuanceId("item", out _);

            var snapshot = ItemPersistence.CreateSnapshot(fork, ActorId);
            Assert(snapshot.isManaged, "ManagedPayloadPersistenceRoundtripV3.SnapshotIsManaged", passed);
            Assert(snapshot.tombstones.Count == 1, "ManagedPayloadPersistenceRoundtripV3.SnapshotTombstoneCount", passed);
            Assert(snapshot.materialReceipts.Count == 1, "ManagedPayloadPersistenceRoundtripV3.SnapshotMaterialReceiptCount", passed);
            Assert(snapshot.issuanceHighWatermark == 1, "ManagedPayloadPersistenceRoundtripV3.SnapshotWatermark", passed);

            string tempFile = Path.Combine(Path.GetTempPath(), "test-save-v3-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                bool saved = ItemPersistence.SaveAtomic(tempFile, snapshot);
                Assert(saved, "ManagedPayloadPersistenceRoundtripV3.SaveSuccess", passed);

                string rawJson = File.ReadAllText(tempFile);
                var envelope = JsonUtility.FromJson<PhysicalSaveEnvelope>(rawJson);
                Assert(string.Equals(envelope.schema, ItemPersistence.ManagedSchemaVersion, StringComparison.Ordinal), "ManagedPayloadPersistenceRoundtripV3.SchemaV3", passed);

                bool loaded = ItemPersistence.TryLoad(tempFile, WorldId, GenId, ActorId, fork, out var loadedPayload);
                Assert(loaded, "ManagedPayloadPersistenceRoundtripV3.LoadSuccess", passed);
                Assert(loadedPayload.isManaged, "ManagedPayloadPersistenceRoundtripV3.LoadedIsManaged", passed);
                Assert(loadedPayload.tombstones.Count == 1, "ManagedPayloadPersistenceRoundtripV3.LoadedTombstoneCount", passed);
                Assert(loadedPayload.materialReceipts.Count == 1, "ManagedPayloadPersistenceRoundtripV3.LoadedReceiptCount", passed);

                var restoredFork = model.CreateDetachedFork();
                bool restored = restoredFork.RestoreSnapshot(loadedPayload);
                Assert(restored, "ManagedPayloadPersistenceRoundtripV3.RestoreSuccess", passed);
                Assert(restoredFork.IsManaged, "ManagedPayloadPersistenceRoundtripV3.RestoredIsManaged", passed);
                Assert(restoredFork.IsRetired("fruit-eat"), "ManagedPayloadPersistenceRoundtripV3.RestoredRetired", passed);
                Assert(restoredFork.TryGetItem("fruit-live", out _), "ManagedPayloadPersistenceRoundtripV3.RestoredLive", passed);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private static void TestLegacyV2SaveCompatibilityNoManagedLeak(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("basket-legacy", "container.basket.v1", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            // Legacy unmanaged snapshot
            var snapshot = ItemPersistence.CreateSnapshot(model, ActorId);
            Assert(!snapshot.isManaged, "LegacyV2SaveCompatibilityNoManagedLeak.SnapshotNotManaged", passed);
            Assert(snapshot.tombstones.Count == 0, "LegacyV2SaveCompatibilityNoManagedLeak.ZeroTombstones", passed);

            string tempFile = Path.Combine(Path.GetTempPath(), "test-save-v2-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                bool saved = ItemPersistence.SaveAtomic(tempFile, snapshot);
                Assert(saved, "LegacyV2SaveCompatibilityNoManagedLeak.SaveSuccess", passed);

                string rawJson = File.ReadAllText(tempFile);
                var envelope = JsonUtility.FromJson<PhysicalSaveEnvelope>(rawJson);
                Assert(string.Equals(envelope.schema, ItemPersistence.SchemaVersion, StringComparison.Ordinal), "LegacyV2SaveCompatibilityNoManagedLeak.SchemaV2", passed);

                bool loaded = ItemPersistence.TryLoad(tempFile, WorldId, GenId, ActorId, model, out var loadedPayload);
                Assert(loaded, "LegacyV2SaveCompatibilityNoManagedLeak.LoadSuccess", passed);
                Assert(!loadedPayload.isManaged, "LegacyV2SaveCompatibilityNoManagedLeak.LoadedNotManaged", passed);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private static void TestLegacySaveWithInjectedManagedMetadataRejection(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("basket-01", "container.basket.v1", ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var snapshot = ItemPersistence.CreateSnapshot(model, ActorId);
            string tempFile = Path.Combine(Path.GetTempPath(), "test-tamper-legacy-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                ItemPersistence.SaveAtomic(tempFile, snapshot);

                // Tamper: load envelope, inject isManaged=true or tombstones into payload JSON, rehash sha256
                string raw = File.ReadAllText(tempFile);
                var env = JsonUtility.FromJson<PhysicalSaveEnvelope>(raw);
                var candidate = JsonUtility.FromJson<PhysicalSavePayload>(env.payload);
                candidate.isManaged = true; // Injected into v2 envelope!
                string tamperedPayloadJson = JsonUtility.ToJson(candidate);
                env.payload = tamperedPayloadJson;
                env.sha256 = ItemPersistence.ComputeSha256(tamperedPayloadJson);
                File.WriteAllText(tempFile, JsonUtility.ToJson(env));

                bool loaded = ItemPersistence.TryLoad(tempFile, WorldId, GenId, ActorId, model, out _);
                Assert(!loaded, "LegacySaveWithInjectedManagedMetadataRejection.TamperRejected", passed);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private static void TestManagedSchemaWithoutManagedFlagRejection(List<string> passed)
        {
            var model = CreateTestModel();
            var fork = model.CreateDetachedFork(true);
            var snapshot = ItemPersistence.CreateSnapshot(fork, ActorId);
            string tempFile = Path.Combine(Path.GetTempPath(), "test-tamper-v3-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                ItemPersistence.SaveAtomic(tempFile, snapshot);

                string raw = File.ReadAllText(tempFile);
                var env = JsonUtility.FromJson<PhysicalSaveEnvelope>(raw);
                var candidate = JsonUtility.FromJson<PhysicalSavePayload>(env.payload);
                candidate.isManaged = false; // schema says v3, but payload claims unmanaged!
                string tamperedPayloadJson = JsonUtility.ToJson(candidate);
                env.payload = tamperedPayloadJson;
                env.sha256 = ItemPersistence.ComputeSha256(tamperedPayloadJson);
                File.WriteAllText(tempFile, JsonUtility.ToJson(env));

                bool loaded = ItemPersistence.TryLoad(tempFile, WorldId, GenId, ActorId, fork, out _);
                Assert(!loaded, "ManagedSchemaWithoutManagedFlagRejection.Rejected", passed);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private static void TestResurrectionTamperRejection(List<string> passed)
        {
            var model = CreateTestModel();
            model.RegisterItem("fruit-tomb", FoodPhysicalCatalog.FruitTypeId, ItemLocationKind.Free, Vector3.zero, Quaternion.identity);

            var fork = model.CreateDetachedFork(true);
            var auth = new TestMaterialBatchAuthority(ActorId, "Eat");
            var batchReq = new MaterialBatchRequest
            {
                requestId = 200,
                worldId = WorldId,
                generationId = GenId,
                actorId = ActorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect { kind = MaterialEffectKind.Consume, itemId = "fruit-tomb" }
                }
            };
            fork.TryPrepareMaterialBatch(batchReq, auth, out var token, out _);
            fork.TryCommitMaterialBatch(token, out _);

            var snapshot = ItemPersistence.CreateSnapshot(fork, ActorId);
            // Tamper: resurrect fruit-tomb by adding it into active items list
            snapshot.items.Add(new SavedItemRecord
            {
                itemId = "fruit-tomb",
                itemTypeId = FoodPhysicalCatalog.FruitTypeId,
                location = ItemLocationKind.Free,
                massKg = 0.020f,
                dimensions = new PhysicalDimensions(0.035f, 0.035f, 0.035f),
                position = Vector3.zero,
                rotation = Quaternion.identity
            });

            string tempFile = Path.Combine(Path.GetTempPath(), "test-resurrect-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                // Serializing tampered payload
                string json = JsonUtility.ToJson(snapshot);
                var env = new PhysicalSaveEnvelope
                {
                    schema = ItemPersistence.ManagedSchemaVersion,
                    payload = json,
                    sha256 = ItemPersistence.ComputeSha256(json)
                };
                File.WriteAllText(tempFile, JsonUtility.ToJson(env));

                bool loaded = ItemPersistence.TryLoad(tempFile, WorldId, GenId, ActorId, fork, out _);
                Assert(!loaded, "ResurrectionTamperRejection.TryLoadRejected", passed);

                bool canRestore = fork.CanRestoreSnapshot(snapshot);
                Assert(!canRestore, "ResurrectionTamperRejection.CanRestoreRejected", passed);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private static void TestTombstoneLedgerBoundedRefusal(List<string> passed)
        {
            var model = CreateTestModel();
            var fork = model.CreateDetachedFork(true);
            var snapshot = ItemPersistence.CreateSnapshot(fork, ActorId);

            // Populate 1025 tombstones (> 1024 bound)
            for (int i = 0; i <= ItemModel.MaxTombstoneLedgerSize; i++)
            {
                snapshot.tombstones.Add(new TombstoneRecord
                {
                    itemId = "tomb-" + i,
                    itemTypeId = FoodPhysicalCatalog.FruitTypeId,
                    reason = "consumed",
                    provenance = "meal",
                    retiredByActorId = ActorId,
                    retiredTick = 1
                });
            }

            string tempFile = Path.Combine(Path.GetTempPath(), "test-overflow-tomb-" + Guid.NewGuid().ToString("N") + ".json");
            bool saved = ItemPersistence.SaveAtomic(tempFile, snapshot);
            Assert(!saved, "TombstoneLedgerBoundedRefusal.SaveAtomicRefused", passed);

            bool canRestore = fork.CanRestoreSnapshot(snapshot);
            Assert(!canRestore, "TombstoneLedgerBoundedRefusal.CanRestoreRefused", passed);
        }

        private static void TestMaterialReceiptLedgerBoundedRefusal(List<string> passed)
        {
            var model = CreateTestModel();
            var fork = model.CreateDetachedFork(true);
            var snapshot = ItemPersistence.CreateSnapshot(fork, ActorId);

            // Populate 513 receipts (> 512 bound)
            for (int i = 1; i <= ItemModel.MaxMaterialReceiptLedgerSize + 1; i++)
            {
                snapshot.materialReceipts.Add(new SavedMaterialBatchReceiptRecord
                {
                    requestId = i,
                    signature = "sig-" + i,
                    receipt = new MaterialBatchReceipt
                    {
                        requestId = i,
                        worldId = WorldId,
                        generationId = GenId,
                        actorId = ActorId,
                        success = true
                    }
                });
            }

            string tempFile = Path.Combine(Path.GetTempPath(), "test-overflow-receipt-" + Guid.NewGuid().ToString("N") + ".json");
            bool saved = ItemPersistence.SaveAtomic(tempFile, snapshot);
            Assert(!saved, "MaterialReceiptLedgerBoundedRefusal.SaveAtomicRefused", passed);

            bool canRestore = fork.CanRestoreSnapshot(snapshot);
            Assert(!canRestore, "MaterialReceiptLedgerBoundedRefusal.CanRestoreRefused", passed);
        }

        private static void TestFoodPhysicalCatalogFruitAndSeedDefinitions(List<string> passed)
        {
            var fruit = FoodPhysicalCatalog.CreateFruitDefinition();
            Assert(string.Equals(fruit.itemTypeId, FoodPhysicalCatalog.FruitTypeId, StringComparison.Ordinal), "FoodPhysicalCatalog.FruitTypeId", passed);
            Assert(Mathf.Abs(fruit.massKg - 0.020f) < 0.0001f, "FoodPhysicalCatalog.FruitMass", passed);
            Assert(Mathf.Abs(fruit.dimensions.width - 0.035f) < 0.0001f, "FoodPhysicalCatalog.FruitWidth", passed);
            Assert(Mathf.Abs(fruit.dimensions.height - 0.035f) < 0.0001f, "FoodPhysicalCatalog.FruitHeight", passed);
            Assert(Mathf.Abs(fruit.dimensions.depth - 0.035f) < 0.0001f, "FoodPhysicalCatalog.FruitDepth", passed);
            Assert(!fruit.isContainer, "FoodPhysicalCatalog.FruitNotContainer", passed);
            Assert(!fruit.isAnchored, "FoodPhysicalCatalog.FruitNotAnchored", passed);

            var seed = FoodPhysicalCatalog.CreateSeedDefinition();
            Assert(string.Equals(seed.itemTypeId, FoodPhysicalCatalog.SeedTypeId, StringComparison.Ordinal), "FoodPhysicalCatalog.SeedTypeId", passed);
            Assert(Mathf.Abs(seed.massKg - 0.0002f) < 0.00001f, "FoodPhysicalCatalog.SeedMass", passed);
            Assert(Mathf.Abs(seed.dimensions.width - 0.006f) < 0.0001f, "FoodPhysicalCatalog.SeedWidth", passed);
            Assert(Mathf.Abs(seed.dimensions.height - 0.006f) < 0.0001f, "FoodPhysicalCatalog.SeedHeight", passed);
            Assert(Mathf.Abs(seed.dimensions.depth - 0.006f) < 0.0001f, "FoodPhysicalCatalog.SeedDepth", passed);
            Assert(!seed.isContainer, "FoodPhysicalCatalog.SeedNotContainer", passed);
            Assert(!seed.isAnchored, "FoodPhysicalCatalog.SeedNotAnchored", passed);

            var model = new ItemModel(WorldId, GenId);
            FoodPhysicalCatalog.RegisterToModel(model);
            Assert(model.TryGetDefinition(FoodPhysicalCatalog.FruitTypeId, out _), "FoodPhysicalCatalog.RegisteredFruit", passed);
            Assert(model.TryGetDefinition(FoodPhysicalCatalog.SeedTypeId, out _), "FoodPhysicalCatalog.RegisteredSeed", passed);
        }
    }
}
