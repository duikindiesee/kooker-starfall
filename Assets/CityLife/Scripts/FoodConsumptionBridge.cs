using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CityLife.Items;
using Starfall.Food;

namespace CityLife.World
{
    /// <summary>
    /// Authoritative, conserved food and physical item consumption bridge.
    /// Ensures that any consumption (manual player H key, autonomous survival goal,
    /// or natural language command directive) strictly adheres to the narrow dual-authority contract:
    /// 1. Validates exact held edible item, bound FoodModel, bound ItemModel, worldId, actorId, and generation identifiers.
    /// 2. Fails closed on missing authority, unregistered/unallowed items, request ID exhaustion, or authority refusal.
    /// 3. Stages both nutrition changes and item consume/tombstone without live state mutation.
    /// 4. Atomically commits one combined authoritative checkpoint envelope via FoodOwnershipCheckpointRepository.Commit.
    /// 5. Only upon successful committed outcome: publishes live state to FoodModel.State and ItemModel,
    ///    persists disk copies, and clears the correct hand and visual GameObject.
    /// 6. Rolls back cleanly without mutating live state on any refusal or commit failure.
    ///
    /// Prevents item resurrection, phantom duplication, or repeat nutrition on reload.
    /// </summary>
    public static class FoodConsumptionBridge
    {
        public static readonly HashSet<string> EdibleItemTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "food-sourfig-berry",
            "food-river-fish",
            "food-river-carp",
            "food-cooked-fish",
            "food-cooked-meat",
            "food-wolf-meat",
            "food-cooked-crab",
            "food-protein-crab"
        };

        public static bool IsIndeterminateFrozen { get; private set; }
        public static string IndeterminateFreezeReason { get; private set; }

        public static void ResetFreezeForTesting()
        {
            IsIndeterminateFrozen = false;
            IndeterminateFreezeReason = null;
        }

        public static bool IsEdibleType(string typeId)
        {
            return !string.IsNullOrEmpty(typeId) && EdibleItemTypes.Contains(typeId);
        }

        public static string GetRepositoryDirectory(NpcAutonomy brain)
        {
            if (brain != null && brain.Survival != null &&
                (!string.IsNullOrEmpty(brain.Survival.SavePath) || Application.isPlaying))
            {
                // Bootstrap runs before Survival.Start assigns SavePath. Resolve
                // the same configured path now, rather than choosing the physical
                // save's different folder and missing an acknowledged checkpoint.
                string survivalPath = brain.Survival.SavePath;
                if (string.IsNullOrEmpty(survivalPath))
                {
                    string[] args = Environment.GetCommandLineArgs();
                    int flag = Array.IndexOf(args, "-npcSurvivalSave");
                    if (flag >= 0 && flag + 1 < args.Length) survivalPath = args[flag + 1];
                    else
                    {
                        int mapFlag = Array.IndexOf(args, "-starfallMapAcceptance");
                        if (mapFlag >= 0 && mapFlag + 1 < args.Length)
                            survivalPath = Path.Combine(args[mapFlag + 1], "starfall-map-save.json");
                        else if (Array.IndexOf(args, "-npcSurvivalRuntime") < 0)
                            survivalPath = Path.Combine(Application.persistentDataPath, "starfall-survival-save.json");
                    }
                }
                string dir = Path.GetDirectoryName(survivalPath);
                if (!string.IsNullOrEmpty(dir)) return Path.Combine(dir, "food-ownership-repository");
            }
            if (brain != null && brain.PhysicalItems != null && !string.IsNullOrEmpty(brain.PhysicalItems.PhysicalSavePath))
            {
                string dir = Path.GetDirectoryName(brain.PhysicalItems.PhysicalSavePath);
                if (!string.IsNullOrEmpty(dir)) return Path.Combine(dir, "food-ownership-repository");
            }
            string fallbackRoot = !string.IsNullOrEmpty(Application.persistentDataPath)
                ? Application.persistentDataPath
                : Path.GetTempPath();
            return Path.Combine(fallbackRoot, "food-ownership-repository");
        }

        /// <summary>
        /// Authoritative atomic food consumption transaction.
        /// </summary>
        public static bool TryConsumeHeldFood(
            NpcAutonomy brain,
            NpcInteractable foodInteractable,
            PhysicalItem foodPhys,
            bool isLeftHand,
            string provenance,
            out string receiptCode)
        {
            receiptCode = "uninitialized";

            // 1. Mandatory Authority & World Scoping (Fail Closed)
            if (brain == null)
            {
                receiptCode = "consume-refused-brain-null";
                return false;
            }

            if (IsIndeterminateFrozen)
            {
                receiptCode = "consume-refused-indeterminate-frozen: " + IndeterminateFreezeReason;
                return false;
            }

            if (brain.Actions == null)
            {
                receiptCode = "consume-refused-actions-null";
                return false;
            }

            // Scope must use bound Food on Survival component; never search unrelated worlds
            if (brain.Survival == null || brain.Survival.Food == null || brain.Survival.Food.Model == null)
            {
                receiptCode = "consume-refused-food-authority-missing";
                return false;
            }

            if (brain.PhysicalItems == null || brain.PhysicalItems.Model == null)
            {
                receiptCode = "consume-refused-physical-authority-missing";
                return false;
            }

            var foodModel = brain.Survival.Food.Model;
            var itemModel = brain.PhysicalItems.Model;

            string worldId = brain.InstanceWorldId;
            string actorId = NpcAutonomy.AgentId;
            string foodGen = foodModel.State != null ? foodModel.State.generation : IntegratedFoodRuntime.Generation;
            string physGen = itemModel.GenerationId;

            if (foodModel.State == null ||
                !string.Equals(foodModel.State.world, worldId, StringComparison.Ordinal) ||
                !string.Equals(foodModel.State.actorId, actorId, StringComparison.Ordinal))
            {
                receiptCode = "consume-refused-food-scope-mismatch";
                return false;
            }

            if (!string.Equals(itemModel.WorldId, worldId, StringComparison.Ordinal))
            {
                receiptCode = "consume-refused-item-scope-mismatch";
                return false;
            }

            // 2. Exact Selected Hand Verification
            var heldInteractable = isLeftHand ? brain.Actions.HeldLeft : brain.Actions.HeldRight;
            if (heldInteractable == null)
            {
                receiptCode = "consume-refused-no-held-item";
                return false;
            }

            if (foodInteractable != null && heldInteractable != foodInteractable &&
                !string.Equals(heldInteractable.StableId, foodInteractable.StableId, StringComparison.Ordinal))
            {
                receiptCode = "consume-refused-wrong-hand-item";
                return false;
            }

            if (foodPhys != null && !string.Equals(heldInteractable.StableId, foodPhys.itemId, StringComparison.Ordinal))
            {
                receiptCode = "consume-refused-hand-physical-item-mismatch";
                return false;
            }

            string itemId = foodPhys != null ? foodPhys.itemId : heldInteractable.StableId;

            if (string.IsNullOrEmpty(itemId))
            {
                receiptCode = "consume-refused-empty-item-id";
                return false;
            }

            // 3. Strict Authoritative Registration & Edible Allowlist Enforcement (No arbitrary items fall through)
            if (itemModel.IsRetired(itemId))
            {
                receiptCode = "consume-refused-item-already-retired";
                return false;
            }

            if (!itemModel.TryGetItem(itemId, out var existingRec))
            {
                receiptCode = "consume-refused-item-not-registered";
                return false;
            }

            string itemTypeId = existingRec.itemTypeId;
            if (foodPhys != null && !string.Equals(foodPhys.itemTypeId, itemTypeId, StringComparison.Ordinal))
            {
                receiptCode = "consume-refused-type-mismatch";
                return false;
            }

            if (!IsEdibleType(itemTypeId))
            {
                receiptCode = "consume-refused-not-edible: " + itemTypeId;
                return false;
            }

            // 4. Authoritative Carried Check
            if (existingRec.location != ItemLocationKind.Carried ||
                !string.Equals(existingRec.holderActorId, actorId, StringComparison.Ordinal))
            {
                receiptCode = "consume-refused-item-not-authoritatively-carried";
                return false;
            }

            // 5. Request ID Allocation for Physical Item (Fail Closed; strictly NO synthetic clock fallback)
            if (!brain.TryAllocateRequestId(out int itemReqId))
            {
                receiptCode = "consume-refused-item-request-id-exhausted";
                return false;
            }

            // 6. Stage ItemModel Material Batch on Detached Fork (Live itemModel remains untouched)
            var batchReq = new MaterialBatchRequest
            {
                requestId = itemReqId,
                worldId = worldId,
                generationId = physGen,
                actorId = actorId,
                commandName = "Eat",
                effects = new List<MaterialBatchItemEffect>
                {
                    new MaterialBatchItemEffect
                    {
                        kind = MaterialEffectKind.Consume,
                        itemId = itemId,
                        itemTypeId = itemTypeId,
                        provenance = provenance ?? "consumed-meal"
                    }
                }
            };

            var forkedModel = itemModel.CreateDetachedFork(true);
            var batchAuth = new BasicMaterialBatchAuthority { requiredActorId = actorId };
            if (!forkedModel.TryPrepareMaterialBatch(batchReq, batchAuth, out var forkPrepToken, out string forkPrepDeny))
            {
                receiptCode = "consume-refused-prep-denied: " + forkPrepDeny;
                return false;
            }

            if (!forkedModel.TryCommitMaterialBatch(forkPrepToken, out var forkBatchReceipt))
            {
                receiptCode = "consume-refused-item-commit-failed";
                return false;
            }

            var stagedPhysSnapshot = ItemPersistence.CreateSnapshot(forkedModel, actorId);
            string stagedPhysJson = JsonUtility.ToJson(stagedPhysSnapshot, false);

            // 7. Stage Nutrition Deltas on Cloned FoodState (Live foodModel remains untouched)
            string currentFoodJson = foodModel.Json();
            var stagedState = JsonUtility.FromJson<FoodState>(currentFoodJson);
            if (stagedState == null)
            {
                receiptCode = "consume-refused-food-serialization-failed";
                return false;
            }

            // Independent monotonic Food request namespace
            int foodReqId = stagedState.lastRequest + 1;
            if (foodReqId <= stagedState.lastRequest)
            {
                receiptCode = "consume-refused-food-request-overflow";
                return false;
            }

            ApplyNutritionDeltas(stagedState, itemTypeId);
            stagedState.knowsMealBenefit = true;
            stagedState.lastRequest = foodReqId;
            stagedState.lastMealEvidence = stagedState.generation + ".ate." + foodReqId;
            if (!FoodModel.Valid(stagedState, worldId, foodGen))
            {
                receiptCode = "consume-refused-staged-food-invalid";
                return false;
            }
            string stagedFoodJson = JsonUtility.ToJson(stagedState, false);

            // 8. Combined Checkpoint Envelope Commit via FoodOwnershipCheckpointRepository
            string repoDir = GetRepositoryDirectory(brain);
            var repo = new FoodOwnershipCheckpointRepository(repoDir, worldId, actorId, foodGen, physGen);

            if (!Directory.Exists(repoDir) || Directory.GetFileSystemEntries(repoDir).Length == 0)
            {
                if (!repo.InitializeEmpty(out string initErr))
                {
                    receiptCode = "consume-checkpoint-init-failed: " + initErr;
                    return false;
                }
            }

            long currentSeq = 0;
            string prevHash = "";
            long highWatermark = 0;
            var receipts = new List<FoodOwnershipReceiptRecord>();

            string ptrPath = Path.Combine(repoDir, FoodOwnershipCheckpointRepository.PointerFileName);
            if (File.Exists(ptrPath))
            {
                string ptrJson = File.ReadAllText(ptrPath);
                if (FoodOwnershipCheckpointCodec.TryDecodePointer(ptrJson, out var currentPtr, out _))
                {
                    currentSeq = currentPtr.sequence;
                    prevHash = currentPtr.checkpointHash ?? "";
                    string envPath = Path.Combine(repoDir, currentPtr.checkpointFilename);
                    if (File.Exists(envPath))
                    {
                        string envJson = File.ReadAllText(envPath);
                        if (FoodOwnershipCheckpointCodec.TryDecodeEnvelope(envJson, out var curEnv, out _))
                        {
                            receipts = new List<FoodOwnershipReceiptRecord>(curEnv.receipts);
                            highWatermark = curEnv.requestHighWatermark;
                        }
                    }
                }
            }

            long nextTxId = highWatermark + 1;
            if (nextTxId <= highWatermark)
            {
                receiptCode = "consume-refused-transaction-watermark-overflow";
                return false;
            }

            long nextSeq = currentSeq + 1;
            receipts.Add(new FoodOwnershipReceiptRecord
            {
                transactionId = nextTxId,
                requestSignature = "Eat:" + itemId,
                foodRequestId = foodReqId,
                itemRequestId = itemReqId,
                status = FoodOwnershipReceiptStatus.Committed,
                statusCode = "consumed-ok",
                checkpointSequence = nextSeq,
                payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("consumed-" + itemId)
            });

            var candidateEnvelope = new FoodOwnershipCheckpointEnvelope
            {
                schema = FoodOwnershipCheckpointEnvelope.CurrentSchema,
                worldId = worldId,
                actorId = actorId,
                foodGeneration = foodGen,
                physicalGeneration = physGen,
                sequence = nextSeq,
                previousCheckpointHash = prevHash,
                requestHighWatermark = nextTxId,
                foodPayload = stagedFoodJson,
                physicalPayload = stagedPhysJson,
                receipts = receipts
            };
            candidateEnvelope.foodPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(candidateEnvelope.foodPayload);
            candidateEnvelope.physicalPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(candidateEnvelope.physicalPayload);
            candidateEnvelope.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(candidateEnvelope);

            var commitResult = repo.Commit(candidateEnvelope, foodModel, itemModel);
            if (commitResult.Status == CheckpointCommitStatus.Indeterminate)
            {
                if (repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var recoveredEnv).Status == CheckpointLoadStatus.Success && recoveredEnv != null)
                {
                    if (recoveredEnv.sequence == candidateEnvelope.sequence &&
                        string.Equals(recoveredEnv.checkpointHash, candidateEnvelope.checkpointHash, StringComparison.Ordinal))
                    {
                        commitResult = new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.Committed,
                            CommittedSequence = recoveredEnv.sequence,
                            CheckpointHash = recoveredEnv.checkpointHash
                        };
                    }
                }
            }

            if (commitResult.Status == CheckpointCommitStatus.Indeterminate)
            {
                IsIndeterminateFrozen = true;
                IndeterminateFreezeReason = commitResult.Message;
                if (brain != null)
                {
                    brain.Pause();
                    if (brain.Survival != null) brain.Survival.Cancel("indeterminate-checkpoint-frozen");
                }
                receiptCode = "consume-checkpoint-indeterminate-frozen: " + commitResult.Message;
                return false;
            }

            if (commitResult.Status != CheckpointCommitStatus.Committed &&
                commitResult.Status != CheckpointCommitStatus.CommittedPostCommitFailure)
            {
                receiptCode = "consume-checkpoint-commit-refused: " + commitResult.Message;
                return false;
            }

            // 9. Publish Live State (Only After Durable Checkpoint Succeeded)
            foodModel.Restore(stagedFoodJson, worldId, foodGen);
            itemModel.RestoreSnapshot(stagedPhysSnapshot);

            if (brain.PhysicalItems != null)
            {
                brain.PhysicalItems.SaveCurrentState();
            }

            if (brain.Survival != null)
            {
                brain.Survival.Persist();
                brain.Survival.FoodOutcomes++;
                brain.Survival.recentVerifiedOutcome = "eat succeeded: " + itemTypeId;
            }

            // Clear hand & destroy consumed visual GameObject
            brain.Actions.HoldItemDirect(null, isLeftHand);

            if (foodPhys != null && foodPhys.gameObject != null)
            {
                UnityEngine.Object.Destroy(foodPhys.gameObject);
            }
            else if (heldInteractable != null && heldInteractable.gameObject != null)
            {
                UnityEngine.Object.Destroy(heldInteractable.gameObject);
            }

            if (brain.Actor != null)
            {
                brain.Actor.Gesture();
            }

            receiptCode = "consumed-success: " + itemId;
            return true;
        }

        private static void ApplyNutritionDeltas(FoodState s, string itemTypeId)
        {
            if (itemTypeId == "food-cooked-fish")
            {
                s.satiety = Mathf.Min(10000, s.satiety + 4500);
                if (s.body != null)
                {
                    s.body.stomach = Mathf.Min(10000, s.body.stomach + 3500);
                    s.body.protein = Mathf.Min(10000, s.body.protein + 4000);
                    s.body.health = Mathf.Min(10000, s.body.health + 2000);
                    FoodPhysiology.ApplyDriveReduction(s.body, 3500);
                }
            }
            else if (itemTypeId == "food-river-fish" || itemTypeId == "food-river-carp")
            {
                s.satiety = Mathf.Min(10000, s.satiety + 2500);
                if (s.body != null)
                {
                    s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                    s.body.protein = Mathf.Min(10000, s.body.protein + 2500);
                    s.body.health = Mathf.Min(10000, s.body.health + 1000);
                    FoodPhysiology.ApplyDriveReduction(s.body, 2200);
                }
            }
            else if (itemTypeId == "food-cooked-meat")
            {
                s.satiety = Mathf.Min(10000, s.satiety + 4500);
                if (s.body != null)
                {
                    s.body.stomach = Mathf.Min(10000, s.body.stomach + 4500);
                    s.body.protein = Mathf.Min(10000, s.body.protein + 5000);
                    s.body.health = Mathf.Min(10000, s.body.health + 2500);
                    FoodPhysiology.ApplyDriveReduction(s.body, 4000);
                }
            }
            else if (itemTypeId == "food-wolf-meat")
            {
                s.satiety = Mathf.Min(10000, s.satiety + 2200);
                if (s.body != null)
                {
                    s.body.stomach = Mathf.Min(10000, s.body.stomach + 2500);
                    s.body.protein = Mathf.Min(10000, s.body.protein + 3200);
                    s.body.health = Mathf.Min(10000, s.body.health + 800);
                    FoodPhysiology.ApplyDriveReduction(s.body, 2500);
                }
            }
            else if (itemTypeId == "food-cooked-crab")
            {
                s.satiety = Mathf.Min(10000, s.satiety + 3000);
                if (s.body != null)
                {
                    s.body.stomach = Mathf.Min(10000, s.body.stomach + 3000);
                    s.body.protein = Mathf.Min(10000, s.body.protein + 3800);
                    s.body.health = Mathf.Min(10000, s.body.health + 1500);
                    FoodPhysiology.ApplyDriveReduction(s.body, 3200);
                }
            }
            else if (itemTypeId == "food-protein-crab")
            {
                s.satiety = Mathf.Min(10000, s.satiety + 2000);
                if (s.body != null)
                {
                    s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                    s.body.protein = Mathf.Min(10000, s.body.protein + 2500);
                    s.body.health = Mathf.Min(10000, s.body.health + 800);
                    FoodPhysiology.ApplyDriveReduction(s.body, 2500);
                }
            }
            else if (itemTypeId == "food-sourfig-berry")
            {
                s.satiety = Mathf.Min(10000, s.satiety + 1500);
                s.hydration = Mathf.Min(10000, s.hydration + 600);
                if (s.body != null)
                {
                    s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                    FoodPhysiology.ApplyDriveReduction(s.body, 1500);
                }
                s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
            }
        }

        /// <summary>
        /// Restores and reconciles BOTH FoodModel and ItemModel from the authoritative checkpoint
        /// repository prior to world fish and visual object reconstruction on startup.
        /// </summary>
        public static bool TryRestoreFromAuthoritativeCheckpoint(NpcAutonomy brain, out string statusMessage)
        {
            statusMessage = "no-checkpoint-loaded";
            if (brain == null || brain.Survival == null || brain.Survival.Food == null || brain.PhysicalItems == null)
            {
                statusMessage = "authorities-missing";
                return false;
            }

            var foodModel = brain.Survival.Food.Model;
            var itemModel = brain.PhysicalItems.Model;
            if (foodModel == null || itemModel == null)
            {
                statusMessage = "models-null";
                return false;
            }

            string repoDir = GetRepositoryDirectory(brain);
            if (!Directory.Exists(repoDir) || !File.Exists(Path.Combine(repoDir, FoodOwnershipCheckpointRepository.PointerFileName)))
            {
                statusMessage = "no-authoritative-pointer";
                return false;
            }

            string worldId = brain.InstanceWorldId;
            string actorId = NpcAutonomy.AgentId;
            string foodGen = foodModel.State != null ? foodModel.State.generation : IntegratedFoodRuntime.Generation;
            string physGen = itemModel.GenerationId;

            var repo = new FoodOwnershipCheckpointRepository(repoDir, worldId, actorId, foodGen, physGen);
            var loadRes = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var env);
            if (loadRes.Status != CheckpointLoadStatus.Success || env == null)
            {
                statusMessage = "checkpoint-load-failed: " + loadRes.Message;
                return false;
            }

            // Preflight both payloads before mutating either model
            if (string.IsNullOrWhiteSpace(env.foodPayload) || string.IsNullOrWhiteSpace(env.physicalPayload))
            {
                statusMessage = "checkpoint-payload-empty";
                return false;
            }

            var detachedFood = new FoodModel(worldId, foodGen, foodModel.State != null ? foodModel.State.seed : 0);
            if (foodModel.State != null) detachedFood.State.actorId = actorId;
            if (!detachedFood.Restore(env.foodPayload, worldId, foodGen))
            {
                statusMessage = "food-preflight-invalid";
                return false;
            }

            PhysicalSavePayload preflightPhys;
            try
            {
                preflightPhys = JsonUtility.FromJson<PhysicalSavePayload>(env.physicalPayload);
            }
            catch (Exception ex)
            {
                statusMessage = "physical-preflight-exception: " + ex.Message;
                return false;
            }

            if (preflightPhys == null || !itemModel.CanRestoreSnapshot(preflightPhys))
            {
                statusMessage = "physical-preflight-invalid";
                return false;
            }

            // Both preflights passed: restore both models in unison
            bool foodRestored = foodModel.Restore(env.foodPayload, worldId, foodGen);
            bool physRestored = itemModel.RestoreSnapshot(preflightPhys);

            if (foodRestored && physRestored)
            {
                if (brain.Survival != null)
                {
                    brain.Survival.SyncFoodRequestHighWatermark(foodModel.State != null ? foodModel.State.lastRequest : 0);
                }
                if (brain.PhysicalItems != null && brain.Actions != null)
                {
                    if (!brain.PhysicalItems.RebindActions(brain.Actions))
                    {
                        statusMessage = "checkpoint-runtime-bindings-refused";
                        return false;
                    }
                }
                var school = RiverFishSchool.Instance;
                if (school != null)
                {
                    school.ReconcileWithAuthoritativeModel(itemModel);
                }
                statusMessage = $"restored-checkpoint-seq{env.sequence}";
                return true;
            }

            statusMessage = $"partial-restore-food-{foodRestored}-phys-{physRestored}";
            return false;
        }

        /// <summary>
        /// Commits the current live state of FoodModel and ItemModel into the authoritative checkpoint repository.
        /// Ensures subsequent storage, retrieval, or save actions advance the checkpoint sequence without rewinding.
        /// </summary>
        public static bool TryCommitCoordinatedCheckpoint(NpcAutonomy brain, string actionSignature, out string receiptCode)
        {
            receiptCode = "uninitialized";
            if (IsIndeterminateFrozen)
            {
                receiptCode = "sync-refused-indeterminate-frozen: " + IndeterminateFreezeReason;
                return false;
            }
            if (brain == null || brain.Survival == null || brain.Survival.Food == null || brain.PhysicalItems == null)
            {
                receiptCode = "sync-authorities-missing";
                return false;
            }

            var foodModel = brain.Survival.Food.Model;
            var itemModel = brain.PhysicalItems.Model;
            if (foodModel == null || itemModel == null)
            {
                receiptCode = "sync-models-null";
                return false;
            }

            string worldId = brain.InstanceWorldId;
            string actorId = NpcAutonomy.AgentId;
            string foodGen = foodModel.State != null ? foodModel.State.generation : IntegratedFoodRuntime.Generation;
            string physGen = itemModel.GenerationId;

            if (foodModel.State == null || !FoodModel.Valid(foodModel.State, worldId, foodGen))
            {
                receiptCode = "sync-refused-food-not-valid";
                return false;
            }

            string repoDir = GetRepositoryDirectory(brain);
            var repo = new FoodOwnershipCheckpointRepository(repoDir, worldId, actorId, foodGen, physGen);

            if (!Directory.Exists(repoDir) || Directory.GetFileSystemEntries(repoDir).Length == 0)
            {
                if (!repo.InitializeEmpty(out string initErr))
                {
                    receiptCode = "sync-init-failed: " + initErr;
                    return false;
                }
            }

            long currentSeq = 0;
            string prevHash = "";
            long highWatermark = 0;
            var receipts = new List<FoodOwnershipReceiptRecord>();

            string ptrPath = Path.Combine(repoDir, FoodOwnershipCheckpointRepository.PointerFileName);
            if (File.Exists(ptrPath))
            {
                string ptrJson = File.ReadAllText(ptrPath);
                if (FoodOwnershipCheckpointCodec.TryDecodePointer(ptrJson, out var currentPtr, out _))
                {
                    currentSeq = currentPtr.sequence;
                    prevHash = currentPtr.checkpointHash ?? "";
                    string envPath = Path.Combine(repoDir, currentPtr.checkpointFilename);
                    if (File.Exists(envPath))
                    {
                        string envJson = File.ReadAllText(envPath);
                        if (FoodOwnershipCheckpointCodec.TryDecodeEnvelope(envJson, out var curEnv, out _))
                        {
                            receipts = new List<FoodOwnershipReceiptRecord>(curEnv.receipts);
                            highWatermark = curEnv.requestHighWatermark;
                        }
                    }
                }
            }

            long nextTxId = highWatermark + 1;
            long nextSeq = currentSeq + 1;
            int foodReqId = foodModel.State != null ? foodModel.State.lastRequest : 0;
            brain.TryAllocateRequestId(out int itemReqId);

            receipts.Add(new FoodOwnershipReceiptRecord
            {
                transactionId = nextTxId,
                requestSignature = string.IsNullOrEmpty(actionSignature) ? "Sync" : actionSignature,
                foodRequestId = foodReqId,
                itemRequestId = itemReqId,
                status = FoodOwnershipReceiptStatus.Committed,
                statusCode = "sync-ok",
                checkpointSequence = nextSeq,
                payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("sync-" + nextSeq)
            });

            var physSnapshot = ItemPersistence.CreateSnapshot(itemModel, actorId);
            string physJson = JsonUtility.ToJson(physSnapshot, false);
            string foodJson = foodModel.Json();

            var candidateEnvelope = new FoodOwnershipCheckpointEnvelope
            {
                schema = FoodOwnershipCheckpointEnvelope.CurrentSchema,
                worldId = worldId,
                actorId = actorId,
                foodGeneration = foodGen,
                physicalGeneration = physGen,
                sequence = nextSeq,
                previousCheckpointHash = prevHash,
                requestHighWatermark = nextTxId,
                foodPayload = foodJson,
                physicalPayload = physJson,
                receipts = receipts
            };
            candidateEnvelope.foodPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(candidateEnvelope.foodPayload);
            candidateEnvelope.physicalPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(candidateEnvelope.physicalPayload);
            candidateEnvelope.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(candidateEnvelope);

            var commitResult = repo.Commit(candidateEnvelope, foodModel, itemModel);
            if (commitResult.Status == CheckpointCommitStatus.Indeterminate)
            {
                if (repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var recoveredEnv).Status == CheckpointLoadStatus.Success && recoveredEnv != null)
                {
                    if (recoveredEnv.sequence == candidateEnvelope.sequence &&
                        string.Equals(recoveredEnv.checkpointHash, candidateEnvelope.checkpointHash, StringComparison.Ordinal))
                    {
                        commitResult = new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.Committed,
                            CommittedSequence = recoveredEnv.sequence,
                            CheckpointHash = recoveredEnv.checkpointHash
                        };
                    }
                }
            }

            if (commitResult.Status == CheckpointCommitStatus.Indeterminate)
            {
                IsIndeterminateFrozen = true;
                IndeterminateFreezeReason = commitResult.Message;
                if (brain != null)
                {
                    brain.Pause();
                    if (brain.Survival != null) brain.Survival.Cancel("indeterminate-checkpoint-frozen");
                }
                receiptCode = "sync-commit-indeterminate-frozen: " + commitResult.Message;
                return false;
            }

            if (commitResult.Status != CheckpointCommitStatus.Committed &&
                commitResult.Status != CheckpointCommitStatus.CommittedPostCommitFailure)
            {
                receiptCode = "sync-commit-failed: " + commitResult.Message;
                return false;
            }

            receiptCode = $"sync-committed-seq{nextSeq}";
            return true;
        }
    }
}
