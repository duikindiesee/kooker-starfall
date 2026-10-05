using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using CityLife.Items;

namespace Starfall.Food
{
    /// <summary>
    /// Focused verification checks for the food ownership checkpoint codec and repository.
    /// Authored for coordinator execution in Unity; not executed during authoring pass.
    ///
    /// 1. Valid checkpoint roundtrip with actual FoodModel and ItemModel payload validation; no mutation of supplied models.
    /// 2. Invalid/partial/mixed-world/generation/actor/definition-backed payloads rejected without pointer change.
    /// 3. Request ledger duplicate/divergent/reordered/overflow/high-watermark and replay-reference rejection.
    /// 4. Deterministic hashing and detection of byte tampering; strict schema/size bounds.
    /// 5. Inject failure across commit lifecycle; restart resolves old or new complete pair according to actual commit, never mixed payloads.
    /// 6. Corrupt newest acknowledged checkpoint/pointer while an older valid checkpoint exists: explicit fail-closed result, NO fallback or empty initialization.
    /// 7. Orphan precommit candidate ignored when valid old pointer exists; ambiguous no-pointer nonempty repository rejected.
    /// 8. Stale sequence/competing writer rejection and immutable-name collision preserves prior data.
    /// 9. Authoritative load validation: fail-closed missing/null/wrong authority, semantic tamper detection under load, and sequence/hash diagnostic safety.
    /// </summary>
    public static class FoodOwnershipCheckpointChecks
    {
        private static string Diag(CheckpointCommitResult r) => r == null ? "result=null" : $"actual Status={r.Status}, Message='{r.Message}'" + (r.Error != null ? $", Error={r.Error.GetType().Name}: {r.Error.Message}" : "");
        private static string Diag(CheckpointLoadResult r) => r == null ? "result=null" : $"actual Status={r.Status}, Message='{r.Message}'" + (r.Error != null ? $", Error={r.Error.GetType().Name}: {r.Error.Message}" : "");

        public static List<string> Run(string testRootFolder)
        {
            var passed = new List<string>();

            void Check(bool condition, string checkName, string failureDetails = null)
            {
                if (!condition)
                {
                    string details = string.IsNullOrEmpty(failureDetails) ? "" : " - " + failureDetails;
                    throw new Exception("FOOD OWNERSHIP CHECKPOINT CHECK FAILED: " + checkName + details);
                }
                passed.Add(checkName);
            }

            const string worldId = "world-test-bridge";
            const string actorId = "inhabitant-1";
            const string foodGen = "combined-v1";
            const string physGen = "gen-01";

            Directory.CreateDirectory(testRootFolder);

            // Setup authoritative FoodModel
            var foodModel = new FoodModel(worldId, foodGen, 4242);
            foodModel.State.actorId = actorId;
            foodModel.State.satiety = 6500;
            foodModel.State.hydration = 5500;
            foodModel.State.carriedFruit = 2;
            foodModel.State.seeds = 1;
            foodModel.State.freshwaterMl = 1500;
            foodModel.State.actorPosition = new Vector3(12f, 1f, 14f);

            // Setup authoritative ItemModel with default catalog
            var itemModel = new ItemModel(worldId, physGen);
            PhysicalItemCatalog.CreateDefaultCatalog().PopulateModel(itemModel);
            itemModel.RegisterItem("demo-stone", "canyon-stone", ItemLocationKind.Free, new Vector3(1f, 0f, 1f), Quaternion.identity);
            itemModel.RegisterItem("demo-chest", "container-chest", ItemLocationKind.Free, new Vector3(2f, 0f, 2f), Quaternion.identity);

            // Helper to generate valid candidate envelope
            FoodOwnershipCheckpointEnvelope BuildValidEnvelope(long sequence, string prevHash, long highWatermark, List<FoodOwnershipReceiptRecord> receipts)
            {
                var physPayload = ItemPersistence.CreateSnapshot(itemModel, actorId);
                string physJson = JsonUtility.ToJson(physPayload, false);
                string foodJson = foodModel.Json();

                var env = new FoodOwnershipCheckpointEnvelope
                {
                    schema = FoodOwnershipCheckpointEnvelope.CurrentSchema,
                    worldId = worldId,
                    actorId = actorId,
                    foodGeneration = foodGen,
                    physicalGeneration = physGen,
                    sequence = sequence,
                    previousCheckpointHash = prevHash ?? "",
                    requestHighWatermark = highWatermark,
                    foodPayload = foodJson,
                    physicalPayload = physJson,
                    receipts = receipts ?? new List<FoodOwnershipReceiptRecord>()
                };

                env.foodPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(env.foodPayload);
                env.physicalPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(env.physicalPayload);
                env.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(env);
                return env;
            }

            // -------------------------------------------------------------
            // CHECK 1: Valid roundtrip with payload validation; no model mutation
            // -------------------------------------------------------------
            {
                string repoDir = Path.Combine(testRootFolder, "chk-1-roundtrip");
                var repo = new FoodOwnershipCheckpointRepository(repoDir, worldId, actorId, foodGen, physGen);
                Check(repo.InitializeEmpty(out _), "chk1-init-empty");

                int initialFoodSatiety = foodModel.State.satiety;
                int initialFoodFruit = foodModel.State.carriedFruit;
                int initialItemCount = itemModel.ItemCount;

                var initialReceipts = new List<FoodOwnershipReceiptRecord>
                {
                    new FoodOwnershipReceiptRecord
                    {
                        transactionId = 1,
                        requestSignature = "System:Initialize",
                        foodRequestId = 0,
                        itemRequestId = 0,
                        status = FoodOwnershipReceiptStatus.Committed,
                        statusCode = "ok",
                        checkpointSequence = 1,
                        payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("init-receipt-payload")
                    }
                };

                var cand1 = BuildValidEnvelope(1, "", 1, initialReceipts);
                var commitResult = repo.Commit(cand1, foodModel, itemModel);

                Check(commitResult.Status == CheckpointCommitStatus.Committed, "chk1-commit-succeeded", Diag(commitResult));
                Check(commitResult.CommittedSequence == 1, "chk1-committed-seq-1", Diag(commitResult));

                // Verification of NO model mutation
                Check(foodModel.State.satiety == initialFoodSatiety, "chk1-food-satiety-unmutated");
                Check(foodModel.State.carriedFruit == initialFoodFruit, "chk1-food-fruit-unmutated");
                Check(itemModel.ItemCount == initialItemCount, "chk1-item-count-unmutated");

                var loadResult = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loadedEnv);
                Check(loadResult.Status == CheckpointLoadStatus.Success, "chk1-load-succeeded", Diag(loadResult));
                Check(loadedEnv != null, "chk1-loaded-not-null", Diag(loadResult));
                Check(loadResult.Checkpoint != null && loadResult.Checkpoint == loadedEnv, "chk1-loaded-result-checkpoint-matches", Diag(loadResult));
                Check(loadedEnv.sequence == 1, "chk1-loaded-seq-1", Diag(loadResult));
                Check(loadedEnv.checkpointHash == cand1.checkpointHash, "chk1-loaded-hash-matches", Diag(loadResult));
                Check(loadedEnv.foodPayload == cand1.foodPayload, "chk1-loaded-food-verbatim", Diag(loadResult));
                Check(loadedEnv.physicalPayload == cand1.physicalPayload, "chk1-loaded-phys-verbatim", Diag(loadResult));
                Check(loadedEnv.receipts.Count == 1 && loadedEnv.receipts[0].transactionId == 1, "chk1-loaded-receipts-match", Diag(loadResult));

                // Non-mutating semantic validation of loaded checkpoint
                string stagingDir = Path.Combine(repoDir, ".staging");
                Check(FoodOwnershipCheckpointCodec.ValidateEnvelopeSemantic(loadedEnv, foodModel, itemModel, stagingDir, out string semErr),
                    "chk1-loaded-semantic-validation-passes", $"semErr={semErr}");
            }

            // -------------------------------------------------------------
            // CHECK 2: Invalid/partial/mixed-world/generation/actor/definition-backed payloads rejected without pointer change
            // -------------------------------------------------------------
            {
                string repoDir = Path.Combine(testRootFolder, "chk-2-rejections");
                var repo = new FoodOwnershipCheckpointRepository(repoDir, worldId, actorId, foodGen, physGen);
                Check(repo.InitializeEmpty(out _), "chk2-init-empty");

                var validSeq1 = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1 = repo.Commit(validSeq1, foodModel, itemModel);
                Check(resSeq1.Status == CheckpointCommitStatus.Committed, "chk2-seq1-committed", Diag(resSeq1));

                // Test A: Bad schema version
                var badSchema = BuildValidEnvelope(2, validSeq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                badSchema.schema = "starfall.food-ownership-checkpoint.v999";
                badSchema.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(badSchema);
                var resBadSchema = repo.Commit(badSchema, foodModel, itemModel);
                Check(resBadSchema.Status == CheckpointCommitStatus.ValidationFailed, "chk2-reject-bad-schema", Diag(resBadSchema));

                // Test B: Partial / corrupt food payload
                var badFoodJson = BuildValidEnvelope(2, validSeq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                badFoodJson.foodPayload = "{\"schema\":\"starfall.food.v1\",\"corrupt\":";
                badFoodJson.foodPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(badFoodJson.foodPayload);
                badFoodJson.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(badFoodJson);
                var resBadFoodJson = repo.Commit(badFoodJson, foodModel, itemModel);
                Check(resBadFoodJson.Status == CheckpointCommitStatus.ValidationFailed, "chk2-reject-corrupt-food-json", Diag(resBadFoodJson));

                // Test C: Mixed world in food payload
                var badWorldFood = JsonUtility.FromJson<FoodState>(foodModel.Json());
                badWorldFood.world = "foreign-world";
                var badWorldCand = BuildValidEnvelope(2, validSeq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                badWorldCand.foodPayload = JsonUtility.ToJson(badWorldFood);
                badWorldCand.foodPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(badWorldCand.foodPayload);
                badWorldCand.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(badWorldCand);
                var resBadWorld = repo.Commit(badWorldCand, foodModel, itemModel);
                Check(resBadWorld.Status == CheckpointCommitStatus.ValidationFailed, "chk2-reject-mixed-food-world", Diag(resBadWorld));

                // Test D: Mixed generation in envelope
                var badGenCand = BuildValidEnvelope(2, validSeq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                badGenCand.foodGeneration = "wrong-gen-v2";
                badGenCand.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(badGenCand);
                var resBadGen = repo.Commit(badGenCand, foodModel, itemModel);
                Check(resBadGen.Status == CheckpointCommitStatus.ValidationFailed, "chk2-reject-mixed-generation", Diag(resBadGen));

                // Test E: Mixed actor in physical payload
                var badActorPhys = ItemPersistence.CreateSnapshot(itemModel, "different-inhabitant");
                var badActorCand = BuildValidEnvelope(2, validSeq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                badActorCand.physicalPayload = JsonUtility.ToJson(badActorPhys, false);
                badActorCand.physicalPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(badActorCand.physicalPayload);
                badActorCand.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(badActorCand);
                var resBadActor = repo.Commit(badActorCand, foodModel, itemModel);
                Check(resBadActor.Status == CheckpointCommitStatus.ValidationFailed, "chk2-reject-mixed-actor", Diag(resBadActor));

                // Test F: Definition-backed admission rejection (unknown itemTypeId)
                var unbackedPhys = ItemPersistence.CreateSnapshot(itemModel, actorId);
                unbackedPhys.items.Add(new SavedItemRecord
                {
                    itemId = "unregistered-plasma-chisel",
                    itemTypeId = "plasma-chisel", // NOT in catalog!
                    location = ItemLocationKind.Free,
                    position = Vector3.zero,
                    rotation = Quaternion.identity
                });
                var unbackedCand = BuildValidEnvelope(2, validSeq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                unbackedCand.physicalPayload = JsonUtility.ToJson(unbackedPhys, false);
                unbackedCand.physicalPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(unbackedCand.physicalPayload);
                unbackedCand.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(unbackedCand);
                var resUnbacked = repo.Commit(unbackedCand, foodModel, itemModel);
                Check(resUnbacked.Status == CheckpointCommitStatus.ValidationFailed, "chk2-reject-unknown-catalog-def", Diag(resUnbacked));

                // Test G: Finding 4 - Missing food authority rejected; no silent fallback
                var validSeq2 = BuildValidEnvelope(2, validSeq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var resNullFood = repo.Commit(validSeq2, null, itemModel);
                Check(resNullFood.Status == CheckpointCommitStatus.ValidationFailed, "chk2-reject-null-food-authority", Diag(resNullFood));

                // Test H: Finding 4 - Missing item authority rejected; no silent fallback
                var resNullItem = repo.Commit(validSeq2, foodModel, null);
                Check(resNullItem.Status == CheckpointCommitStatus.ValidationFailed, "chk2-reject-null-item-authority", Diag(resNullItem));

                // Test I: Finding 4 - Codec-level semantic validation null checks
                string stagingDir = Path.Combine(repoDir, ".staging");
                Check(!FoodOwnershipCheckpointCodec.ValidateEnvelopeSemantic(validSeq2, null, itemModel, stagingDir, out string nullFoodErr), "chk2-codec-reject-null-food");
                Check(nullFoodErr.Contains("Authoritative FoodModel is required"), "chk2-codec-null-food-error-identified");

                Check(!FoodOwnershipCheckpointCodec.ValidateEnvelopeSemantic(validSeq2, foodModel, null, stagingDir, out string nullItemErr), "chk2-codec-reject-null-item");
                Check(nullItemErr.Contains("Authoritative ItemModel is required"), "chk2-codec-null-item-error-identified");

                // Test J1: Finding 4 - Bad candidate food seed rejected with ValidationFailed when authoritative models are correct
                var wrongSeedFoodState = new FoodModel(worldId, foodGen, 99999);
                wrongSeedFoodState.State.actorId = actorId;
                var badSeedCand = BuildValidEnvelope(2, validSeq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                badSeedCand.foodPayload = wrongSeedFoodState.Json();
                badSeedCand.foodPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(badSeedCand.foodPayload);
                badSeedCand.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(badSeedCand);
                var badSeedResult = repo.Commit(badSeedCand, foodModel, itemModel);
                Check(badSeedResult.Status == CheckpointCommitStatus.ValidationFailed,
                    "chk2-reject-mismatched-food-seed", Diag(badSeedResult));

                // Test J2: Finding 4 - Wrong food authority model rejected with CorruptState because current checkpoint seq 1 validation fails against wrong authority
                var wrongSeedModel = new FoodModel(worldId, foodGen, 99999);
                wrongSeedModel.State.actorId = actorId;
                string ptrBeforeJ2 = File.ReadAllText(Path.Combine(repoDir, FoodOwnershipCheckpointRepository.PointerFileName));
                int foodSatietyBeforeJ2 = foodModel.State.satiety;
                var wrongFoodAuthResult = repo.Commit(validSeq2, wrongSeedModel, itemModel);
                Check(wrongFoodAuthResult.Status == CheckpointCommitStatus.CorruptState,
                    "chk2-reject-wrong-food-authority-corrupt-state", Diag(wrongFoodAuthResult));
                Check(wrongFoodAuthResult.Message.Contains("Current authoritative checkpoint"),
                    "chk2-wrong-food-authority-message-identified", Diag(wrongFoodAuthResult));
                string ptrAfterJ2 = File.ReadAllText(Path.Combine(repoDir, FoodOwnershipCheckpointRepository.PointerFileName));
                Check(ptrBeforeJ2 == ptrAfterJ2, "chk2-wrong-food-authority-pointer-unchanged");
                Check(foodModel.State.satiety == foodSatietyBeforeJ2, "chk2-wrong-food-authority-supplied-food-unchanged");
                Check(wrongSeedModel.State.seed == 99999, "chk2-wrong-food-authority-supplied-wrong-seed-unchanged");

                // Test K1: Finding 4 - Bad candidate item scope rejected with ValidationFailed when authoritative models are correct
                var foreignItemModel = new ItemModel("foreign-world", physGen);
                PhysicalItemCatalog.CreateDefaultCatalog().PopulateModel(foreignItemModel);
                var badItemScopeCand = BuildValidEnvelope(2, validSeq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                badItemScopeCand.physicalPayload = JsonUtility.ToJson(ItemPersistence.CreateSnapshot(foreignItemModel, actorId), false);
                badItemScopeCand.physicalPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(badItemScopeCand.physicalPayload);
                badItemScopeCand.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(badItemScopeCand);
                var badItemScopeResult = repo.Commit(badItemScopeCand, foodModel, itemModel);
                Check(badItemScopeResult.Status == CheckpointCommitStatus.ValidationFailed,
                    "chk2-reject-mismatched-item-scope", Diag(badItemScopeResult));

                // Test K2: Finding 4 - Wrong item authority model rejected with CorruptState because current checkpoint seq 1 validation fails against wrong authority
                var wrongScopeItemModel = new ItemModel("foreign-world", physGen);
                string ptrBeforeK2 = File.ReadAllText(Path.Combine(repoDir, FoodOwnershipCheckpointRepository.PointerFileName));
                int itemCountBeforeK2 = itemModel.ItemCount;
                var wrongItemAuthResult = repo.Commit(validSeq2, foodModel, wrongScopeItemModel);
                Check(wrongItemAuthResult.Status == CheckpointCommitStatus.CorruptState,
                    "chk2-reject-wrong-item-authority-corrupt-state", Diag(wrongItemAuthResult));
                Check(wrongItemAuthResult.Message.Contains("Current authoritative checkpoint"),
                    "chk2-wrong-item-authority-message-identified", Diag(wrongItemAuthResult));
                string ptrAfterK2 = File.ReadAllText(Path.Combine(repoDir, FoodOwnershipCheckpointRepository.PointerFileName));
                Check(ptrBeforeK2 == ptrAfterK2, "chk2-wrong-item-authority-pointer-unchanged");
                Check(itemModel.ItemCount == itemCountBeforeK2, "chk2-wrong-item-authority-supplied-item-unchanged");
                Check(wrongScopeItemModel.WorldId == "foreign-world", "chk2-wrong-item-authority-supplied-wrong-scope-unchanged");

                // Verify pointer remained unchanged at sequence 1 throughout all rejections
                var loadResultCurrent = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var current);
                Check(loadResultCurrent.Status == CheckpointLoadStatus.Success && current != null && current.sequence == 1,
                    "chk2-pointer-unchanged-after-all-rejections", Diag(loadResultCurrent));
            }

            // -------------------------------------------------------------
            // CHECK 3: Request ledger duplicate/divergent/reordered/overflow/high-watermark and replay-reference rejection
            // -------------------------------------------------------------
            {
                string repoDir = Path.Combine(testRootFolder, "chk-3-ledger");
                var repo = new FoodOwnershipCheckpointRepository(repoDir, worldId, actorId, foodGen, physGen);
                Check(repo.InitializeEmpty(out _), "chk3-init-empty");

                var validSeq1 = BuildValidEnvelope(1, "", 0, new List<FoodOwnershipReceiptRecord>());
                var resValidSeq1 = repo.Commit(validSeq1, foodModel, itemModel);
                Check(resValidSeq1.Status == CheckpointCommitStatus.Committed, "chk3-seq1-committed", Diag(resValidSeq1));

                // Duplicate transaction ID
                var dupList = new List<FoodOwnershipReceiptRecord>
                {
                    new FoodOwnershipReceiptRecord { transactionId = 1, requestSignature = "SigA", status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("h1") },
                    new FoodOwnershipReceiptRecord { transactionId = 1, requestSignature = "SigB", status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("h2") }
                };
                var candDup = BuildValidEnvelope(2, validSeq1.checkpointHash, 5, dupList);
                var resDup = repo.Commit(candDup, foodModel, itemModel);
                Check(resDup.Status == CheckpointCommitStatus.ValidationFailed, "chk3-reject-duplicate-txid", Diag(resDup));

                // Unordered transaction IDs (transactionId 5 followed by 3)
                var unordList = new List<FoodOwnershipReceiptRecord>
                {
                    new FoodOwnershipReceiptRecord { transactionId = 5, requestSignature = "SigA", status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("h1") },
                    new FoodOwnershipReceiptRecord { transactionId = 3, requestSignature = "SigB", status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("h2") }
                };
                var candUnord = BuildValidEnvelope(2, validSeq1.checkpointHash, 5, unordList);
                var resUnord = repo.Commit(candUnord, foodModel, itemModel);
                Check(resUnord.Status == CheckpointCommitStatus.ValidationFailed, "chk3-reject-unordered-txid", Diag(resUnord));

                // Overflow: exceeds MaxReceiptLedgerSize (256)
                var overflowList = new List<FoodOwnershipReceiptRecord>();
                for (int i = 1; i <= 257; i++)
                {
                    overflowList.Add(new FoodOwnershipReceiptRecord
                    {
                        transactionId = i,
                        requestSignature = "Sig" + i,
                        status = FoodOwnershipReceiptStatus.Committed,
                        statusCode = "ok",
                        checkpointSequence = 2,
                        payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("h" + i)
                    });
                }
                var candOverflow = BuildValidEnvelope(2, validSeq1.checkpointHash, 300, overflowList);
                var resOverflow = repo.Commit(candOverflow, foodModel, itemModel);
                Check(resOverflow.Status == CheckpointCommitStatus.ValidationFailed, "chk3-reject-ledger-overflow", Diag(resOverflow));

                // Transaction ID exceeds declared high-watermark
                var hwExceedList = new List<FoodOwnershipReceiptRecord>
                {
                    new FoodOwnershipReceiptRecord { transactionId = 10, requestSignature = "Sig", status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("h") }
                };
                var candHwExceed = BuildValidEnvelope(2, validSeq1.checkpointHash, 5, hwExceedList); // watermark 5 < txid 10
                var resHwExceed = repo.Commit(candHwExceed, foodModel, itemModel);
                Check(resHwExceed.Status == CheckpointCommitStatus.ValidationFailed, "chk3-reject-txid-exceeds-watermark", Diag(resHwExceed));

                // Success receipt referring to another/future checkpoint
                var futureReceiptList = new List<FoodOwnershipReceiptRecord>
                {
                    new FoodOwnershipReceiptRecord { transactionId = 2, requestSignature = "Sig", status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 99, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("h") }
                };
                var candFutureRef = BuildValidEnvelope(2, validSeq1.checkpointHash, 2, futureReceiptList);
                var resFutureRef = repo.Commit(candFutureRef, foodModel, itemModel);
                Check(resFutureRef.Status == CheckpointCommitStatus.ValidationFailed, "chk3-reject-receipt-ref-future-checkpoint", Diag(resFutureRef));

                // Finding 5: Successor with regressed high-watermark rejected
                // First commit a valid sequence 2 with receipts 1 and 2, watermark 5
                var seq2Receipts = new List<FoodOwnershipReceiptRecord>
                {
                    new FoodOwnershipReceiptRecord { transactionId = 1, requestSignature = "Tx1", foodRequestId = 1, itemRequestId = 1, status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("p1") },
                    new FoodOwnershipReceiptRecord { transactionId = 2, requestSignature = "Tx2", foodRequestId = 2, itemRequestId = 2, status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("p2") }
                };
                var candSeq2 = BuildValidEnvelope(2, validSeq1.checkpointHash, 5, seq2Receipts);
                var resSeq2 = repo.Commit(candSeq2, foodModel, itemModel);
                Check(resSeq2.Status == CheckpointCommitStatus.Committed, "chk3-seq2-committed", Diag(resSeq2));

                // Adversarial successor 3: valid structure, but requestHighWatermark regresses from 5 to 4
                var candHwRegress = BuildValidEnvelope(3, candSeq2.checkpointHash, 4, seq2Receipts);
                var resHwRegress = repo.Commit(candHwRegress, foodModel, itemModel);
                Check(resHwRegress.Status == CheckpointCommitStatus.ValidationFailed, "chk3-reject-watermark-regression", Diag(resHwRegress));

                // Adversarial successor 3: alters payloadHash of retained receipt 1
                var alteredReceipts = new List<FoodOwnershipReceiptRecord>
                {
                    new FoodOwnershipReceiptRecord { transactionId = 1, requestSignature = "Tx1", foodRequestId = 1, itemRequestId = 1, status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("divergent-p1") },
                    new FoodOwnershipReceiptRecord { transactionId = 2, requestSignature = "Tx2", foodRequestId = 2, itemRequestId = 2, status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("p2") }
                };
                var candAlteredHash = BuildValidEnvelope(3, candSeq2.checkpointHash, 5, alteredReceipts);
                var resAlteredHash = repo.Commit(candAlteredHash, foodModel, itemModel);
                Check(resAlteredHash.Status == CheckpointCommitStatus.ValidationFailed, "chk3-reject-altered-retained-receipt-hash", Diag(resAlteredHash));

                // Adversarial successor 3: alters status of retained receipt 1 from Committed to Rejected
                var alteredStatus = new List<FoodOwnershipReceiptRecord>
                {
                    new FoodOwnershipReceiptRecord { transactionId = 1, requestSignature = "Tx1", foodRequestId = 1, itemRequestId = 1, status = FoodOwnershipReceiptStatus.Rejected, statusCode = "failed", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("p1") },
                    new FoodOwnershipReceiptRecord { transactionId = 2, requestSignature = "Tx2", foodRequestId = 2, itemRequestId = 2, status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("p2") }
                };
                var candAlteredStatus = BuildValidEnvelope(3, candSeq2.checkpointHash, 5, alteredStatus);
                var resAlteredStatus = repo.Commit(candAlteredStatus, foodModel, itemModel);
                Check(resAlteredStatus.Status == CheckpointCommitStatus.ValidationFailed, "chk3-reject-altered-retained-receipt-status", Diag(resAlteredStatus));

                // Commit valid sequence 3 with bounded eviction (retains only txid 2, drops txid 1) and adds txid 6
                var seq3Receipts = new List<FoodOwnershipReceiptRecord>
                {
                    new FoodOwnershipReceiptRecord { transactionId = 2, requestSignature = "Tx2", foodRequestId = 2, itemRequestId = 2, status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("p2") },
                    new FoodOwnershipReceiptRecord { transactionId = 6, requestSignature = "Tx6", foodRequestId = 3, itemRequestId = 3, status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 3, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("p6") }
                };
                var candSeq3 = BuildValidEnvelope(3, candSeq2.checkpointHash, 6, seq3Receipts);
                var resSeq3 = repo.Commit(candSeq3, foodModel, itemModel);
                Check(resSeq3.Status == CheckpointCommitStatus.Committed, "chk3-seq3-committed-with-eviction", Diag(resSeq3));

                // Adversarial successor 4: attempts to reissue evicted txid 1
                var reissueEvictedList = new List<FoodOwnershipReceiptRecord>
                {
                    new FoodOwnershipReceiptRecord { transactionId = 1, requestSignature = "Tx1-Reissued", foodRequestId = 4, itemRequestId = 4, status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 4, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("reissued") },
                    new FoodOwnershipReceiptRecord { transactionId = 2, requestSignature = "Tx2", foodRequestId = 2, itemRequestId = 2, status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 2, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("p2") },
                    new FoodOwnershipReceiptRecord { transactionId = 6, requestSignature = "Tx6", foodRequestId = 3, itemRequestId = 3, status = FoodOwnershipReceiptStatus.Committed, statusCode = "ok", checkpointSequence = 3, payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("p6") }
                };
                var candReissue = BuildValidEnvelope(4, candSeq3.checkpointHash, 6, reissueEvictedList);
                var resReissue = repo.Commit(candReissue, foodModel, itemModel);
                Check(resReissue.Status == CheckpointCommitStatus.ValidationFailed, "chk3-reject-reissued-evicted-txid", Diag(resReissue));
            }

            // -------------------------------------------------------------
            // CHECK 4: Deterministic hashing and detection of byte tampering; strict schema/size bounds
            // -------------------------------------------------------------
            {
                var env = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                string hash1 = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(env);
                string hash2 = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(env);
                Check(string.Equals(hash1, hash2, StringComparison.Ordinal) && !string.IsNullOrEmpty(hash1), "chk4-deterministic-hash-repeatable");

                string validJson = FoodOwnershipCheckpointCodec.EncodeEnvelope(env, true);
                Check(FoodOwnershipCheckpointCodec.TryDecodeEnvelope(validJson, out var decodedEnv, out string decodeErr), "chk4-valid-json-decodes", $"decodeErr={decodeErr}");

                // Tamper food payload by 1 byte
                // Mutate DECODED foodPayload exactly once (replace actual compact satiety6500->6501 in its foodPayload)
                string originalFoodPayload = decodedEnv.foodPayload;
                string tamperedFoodPayload = originalFoodPayload.Replace("\"satiety\":6500", "\"satiety\":6501");
                if (string.Equals(tamperedFoodPayload, originalFoodPayload, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Explicit assertion failed: food payload bytes did not change after tampering.");
                }

                var tamperedFoodEnv = new FoodOwnershipCheckpointEnvelope
                {
                    schema = decodedEnv.schema,
                    worldId = decodedEnv.worldId,
                    actorId = decodedEnv.actorId,
                    foodGeneration = decodedEnv.foodGeneration,
                    physicalGeneration = decodedEnv.physicalGeneration,
                    sequence = decodedEnv.sequence,
                    previousCheckpointHash = decodedEnv.previousCheckpointHash,
                    requestHighWatermark = decodedEnv.requestHighWatermark,
                    foodPayload = tamperedFoodPayload,
                    foodPayloadHash = decodedEnv.foodPayloadHash, // Retain original declared hash without recomputing
                    physicalPayload = decodedEnv.physicalPayload,
                    physicalPayloadHash = decodedEnv.physicalPayloadHash,
                    receipts = decodedEnv.receipts,
                    checkpointHash = decodedEnv.checkpointHash // Retain original declared hash without recomputing
                };

                // Serialize without recomputing ANY payload/checkpoint hash (do not use EncodeEnvelope as it recomputes hashes)
                string tamperedFood = JsonUtility.ToJson(tamperedFoodEnv, true);
                Check(!FoodOwnershipCheckpointCodec.TryDecodeEnvelope(tamperedFood, out _, out string tamperFoodErr), "chk4-tampered-food-rejected", $"tamperFoodErr={tamperFoodErr}");
                Check(tamperFoodErr.Contains("Food payload hash mismatch"), "chk4-tampered-food-identified", $"tamperFoodErr={tamperFoodErr}");

                // Tamper physical payload by 1 byte
                string tamperedPhys = validJson.Replace("demo-stone", "demo-stonx");
                Check(!FoodOwnershipCheckpointCodec.TryDecodeEnvelope(tamperedPhys, out _, out string tamperPhysErr), "chk4-tampered-phys-rejected", $"tamperPhysErr={tamperPhysErr}");
                Check(tamperPhysErr.Contains("Physical payload hash mismatch"), "chk4-tampered-phys-identified", $"tamperPhysErr={tamperPhysErr}");

                // Tamper sequence number without updating checkpointHash
                string tamperedSeq = validJson.Replace("\"sequence\": 1", "\"sequence\": 2");
                Check(!FoodOwnershipCheckpointCodec.TryDecodeEnvelope(tamperedSeq, out _, out string tamperSeqErr), "chk4-tampered-seq-rejected", $"tamperSeqErr={tamperSeqErr}");
                Check(tamperSeqErr.Contains("integrity hash mismatch"), "chk4-tampered-seq-identified", $"tamperSeqErr={tamperSeqErr}");

                // Exceeding 2MB envelope budget
                string hugeJson = validJson + new string(' ', FoodOwnershipCheckpointEnvelope.MaxEnvelopeSizeBytes + 10);
                Check(!FoodOwnershipCheckpointCodec.TryDecodeEnvelope(hugeJson, out _, out string hugeErr), "chk4-oversized-envelope-rejected", $"hugeErr={hugeErr}");
                Check(hugeErr.Contains("exceeds maximum envelope size"), "chk4-oversized-envelope-identified", $"hugeErr={hugeErr}");
            }

            // -------------------------------------------------------------
            // CHECK 5: Fault injection across commit lifecycle; restart resolves old or new complete pair
            // -------------------------------------------------------------
            {
                string repoDir = Path.Combine(testRootFolder, "chk-5-fault-injection");
                var repo = new FoodOwnershipCheckpointRepository(repoDir, worldId, actorId, foodGen, physGen);
                Check(repo.InitializeEmpty(out _), "chk5-init-empty");

                var seq1 = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1 = repo.Commit(seq1, foodModel, itemModel);
                Check(resSeq1.Status == CheckpointCommitStatus.Committed, "chk5-seq1-committed", Diag(resSeq1));

                // 5a: FailBeforeWrite -> NotCommitted, restart resolves seq 1
                repo.FaultInjection = CheckpointFaultInjectionPoint.FailBeforeWrite;
                var cand2a = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var res5a = repo.Commit(cand2a, foodModel, itemModel);
                Check(res5a.Status == CheckpointCommitStatus.NotCommitted, "chk5a-fail-before-write-not-committed", Diag(res5a));
                var load5a = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5a);
                Check(load5a.Status == CheckpointLoadStatus.Success && restart5a != null && restart5a.sequence == 1,
                    "chk5a-restart-resolves-old-pair", Diag(load5a));

                // 5b: FailDuringPartialCheckpointWrite -> NotCommitted, restart resolves seq 1
                repo.FaultInjection = CheckpointFaultInjectionPoint.FailDuringPartialCheckpointWrite;
                var cand2b = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var res5b = repo.Commit(cand2b, foodModel, itemModel);
                Check(res5b.Status == CheckpointCommitStatus.NotCommitted, "chk5b-fail-partial-write-not-committed", Diag(res5b));
                var load5b = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5b);
                Check(load5b.Status == CheckpointLoadStatus.Success && restart5b != null && restart5b.sequence == 1,
                    "chk5b-restart-resolves-old-pair", Diag(load5b));

                // 5c: FailAfterCheckpointFlush -> NotCommitted, restart resolves seq 1
                repo.FaultInjection = CheckpointFaultInjectionPoint.FailAfterCheckpointFlush;
                var cand2c = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var res5c = repo.Commit(cand2c, foodModel, itemModel);
                Check(res5c.Status == CheckpointCommitStatus.NotCommitted, "chk5c-fail-after-flush-not-committed", Diag(res5c));
                var load5c = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5c);
                Check(load5c.Status == CheckpointLoadStatus.Success && restart5c != null && restart5c.sequence == 1,
                    "chk5c-restart-resolves-old-pair", Diag(load5c));

                // 5d: FailBeforePointerPublication -> NotCommitted, restart resolves seq 1
                repo.FaultInjection = CheckpointFaultInjectionPoint.FailBeforePointerPublication;
                var cand2d = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var res5d = repo.Commit(cand2d, foodModel, itemModel);
                Check(res5d.Status == CheckpointCommitStatus.NotCommitted, "chk5d-fail-before-pub-not-committed", Diag(res5d));
                var load5d = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5d);
                Check(load5d.Status == CheckpointLoadStatus.Success && restart5d != null && restart5d.sequence == 1,
                    "chk5d-restart-resolves-old-pair", Diag(load5d));

                // 5e: Finding 1 - FailDuringPointerPublicationReplacement with demonstrably unchanged pointer -> NotCommitted
                bool hookRan5e = false;
                repo.FaultInjection = CheckpointFaultInjectionPoint.FailDuringPointerPublicationReplacement;
                repo.TestHookBeforePointerReadback = () => { hookRan5e = true; };
                var cand2e = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var res5e = repo.Commit(cand2e, foodModel, itemModel);
                repo.TestHookBeforePointerReadback = null;
                repo.FaultInjection = CheckpointFaultInjectionPoint.None;
                Check(hookRan5e, "chk5e-hook-actually-ran");
                Check(res5e.Status == CheckpointCommitStatus.NotCommitted, "chk5e-fail-replacement-demonstrably-unchanged-not-committed", Diag(res5e));
                var load5e = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5e);
                Check(load5e.Status == CheckpointLoadStatus.Success && restart5e != null && restart5e.sequence == 1,
                    "chk5e-restart-resolves-old-pair", Diag(load5e));

                // Deliberately advance to sequence 2 cleanly before post-publication failure checks 5j and 5k
                var seq2Clean = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var resSeq2Clean = repo.Commit(seq2Clean, foodModel, itemModel);
                Check(resSeq2Clean.Status == CheckpointCommitStatus.Committed, "chk5-seq2-committed", Diag(resSeq2Clean));

                // 5j: FailAfterPointerPublication -> CommittedPostCommitFailure, restart resolves seq 3
                repo.FaultInjection = CheckpointFaultInjectionPoint.FailAfterPointerPublication;
                var cand3j = BuildValidEnvelope(3, seq2Clean.checkpointHash, 3, new List<FoodOwnershipReceiptRecord>());
                var res5j = repo.Commit(cand3j, foodModel, itemModel);
                Check(res5j.Status == CheckpointCommitStatus.CommittedPostCommitFailure, "chk5j-fail-after-pub-committed-post-failure", Diag(res5j));
                Check(res5j.CommittedSequence == 3, "chk5j-seq-3-acknowledged", Diag(res5j));
                var load5j = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5j);
                Check(load5j.Status == CheckpointLoadStatus.Success && restart5j != null && restart5j.sequence == 3,
                    "chk5j-restart-resolves-new-pair", Diag(load5j));
                Check(restart5j != null && restart5j.receipts.Count == cand3j.receipts.Count, "chk5j-restart-receipts-match-new", Diag(load5j));

                // 5k: FailDuringAcknowledgementCleanup -> CommittedPostCommitFailure, restart resolves seq 4
                repo.FaultInjection = CheckpointFaultInjectionPoint.FailDuringAcknowledgementCleanup;
                var cand4 = BuildValidEnvelope(4, restart5j.checkpointHash, 4, new List<FoodOwnershipReceiptRecord>());
                var res5k = repo.Commit(cand4, foodModel, itemModel);
                Check(res5k.Status == CheckpointCommitStatus.CommittedPostCommitFailure, "chk5k-fail-cleanup-committed-post-failure", Diag(res5k));
                var load5k = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5k);
                Check(load5k.Status == CheckpointLoadStatus.Success && restart5k != null && restart5k.sequence == 4,
                    "chk5k-restart-resolves-new-pair", Diag(load5k));
                Check(restart5k != null && restart5k.receipts.Count == cand4.receipts.Count, "chk5k-restart-receipts-match-new", Diag(load5k));

                repo.FaultInjection = CheckpointFaultInjectionPoint.None;
            }

            // -------------------------------------------------------------
            // CHECK 5f: FailDuringPointerPublicationReadback -> Indeterminate (outcome uncertain, new pointer committed)
            // -------------------------------------------------------------
            {
                string repoDir5f = Path.Combine(testRootFolder, "chk-5f-readback-fault");
                var repo5f = new FoodOwnershipCheckpointRepository(repoDir5f, worldId, actorId, foodGen, physGen);
                Check(repo5f.InitializeEmpty(out _), "chk5f-init-empty");

                var seq1_5f = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1_5f = repo5f.Commit(seq1_5f, foodModel, itemModel);
                Check(resSeq1_5f.Status == CheckpointCommitStatus.Committed, "chk5f-seq1-committed", Diag(resSeq1_5f));

                repo5f.FaultInjection = CheckpointFaultInjectionPoint.FailDuringPointerPublicationReadback;
                var cand2f = BuildValidEnvelope(2, seq1_5f.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var res5f = repo5f.Commit(cand2f, foodModel, itemModel);
                repo5f.FaultInjection = CheckpointFaultInjectionPoint.None;

                Check(res5f.Status == CheckpointCommitStatus.Indeterminate, "chk5f-fail-readback-indeterminate", Diag(res5f));

                // Independently reopen/reload to verify new pointer committed on disk prior to readback fault
                var reopen5f = new FoodOwnershipCheckpointRepository(repoDir5f, worldId, actorId, foodGen, physGen);
                var loadResult5f = reopen5f.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5f);
                Check(loadResult5f.Status == CheckpointLoadStatus.Success && restart5f.sequence == 2, "chk5f-restart-resolves-new-pair", Diag(loadResult5f));
                Check(string.Equals(restart5f.checkpointHash, cand2f.checkpointHash, StringComparison.Ordinal), "chk5f-restart-hash-matches-new", Diag(loadResult5f));
                Check(restart5f.receipts.Count == cand2f.receipts.Count, "chk5f-restart-receipts-match-new", Diag(loadResult5f));
            }

            // -------------------------------------------------------------
            // CHECK 5g: Real filesystem IO fault on readback via test-only hook -> Indeterminate (old pointer unchanged proven-failure)
            // -------------------------------------------------------------
            {
                string repoDir5g = Path.Combine(testRootFolder, "chk-5g-io-readback");
                var repo5g = new FoodOwnershipCheckpointRepository(repoDir5g, worldId, actorId, foodGen, physGen);
                Check(repo5g.InitializeEmpty(out _), "chk5g-init-empty");

                var seq1_5g = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1_5g = repo5g.Commit(seq1_5g, foodModel, itemModel);
                Check(resSeq1_5g.Status == CheckpointCommitStatus.Committed, "chk5g-seq1-committed", Diag(resSeq1_5g));

                repo5g.FaultInjection = CheckpointFaultInjectionPoint.FailDuringPointerPublicationReplacement;
                bool hookRan5g = false;
                FileStream lockHold5g = null;
                repo5g.TestHookBeforePointerReadback = () =>
                {
                    hookRan5g = true;
                    string ptrPath = Path.Combine(repoDir5g, FoodOwnershipCheckpointRepository.PointerFileName);
                    lockHold5g = new FileStream(ptrPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                };

                var cand2g = BuildValidEnvelope(2, seq1_5g.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var res5g = repo5g.Commit(cand2g, foodModel, itemModel);
                if (lockHold5g != null) { lockHold5g.Dispose(); lockHold5g = null; }
                repo5g.TestHookBeforePointerReadback = null;
                repo5g.FaultInjection = CheckpointFaultInjectionPoint.None;

                Check(hookRan5g, "chk5g-hook-actually-ran");
                Check(res5g.Status == CheckpointCommitStatus.Indeterminate, "chk5g-real-io-fault-readback-indeterminate", Diag(res5g));

                // Independently reopen/reload: old pointer unchanged proven-failure
                var reopen5g = new FoodOwnershipCheckpointRepository(repoDir5g, worldId, actorId, foodGen, physGen);
                var loadResult5g = reopen5g.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5g);
                Check(loadResult5g.Status == CheckpointLoadStatus.Success && restart5g.sequence == 1, "chk5g-reopen-resolves-old-pointer-unchanged", Diag(loadResult5g));
                Check(string.Equals(restart5g.checkpointHash, seq1_5g.checkpointHash, StringComparison.Ordinal), "chk5g-reopen-hash-matches-old", Diag(loadResult5g));
                Check(restart5g.receipts.Count == seq1_5g.receipts.Count, "chk5g-reopen-receipts-match-old", Diag(loadResult5g));
            }

            // -------------------------------------------------------------
            // CHECK 5h: Replacement error where pointer on disk matches candidate -> CommittedPostCommitFailure
            // -------------------------------------------------------------
            {
                string repoDir5h = Path.Combine(testRootFolder, "chk-5h-matching-readback");
                var repo5h = new FoodOwnershipCheckpointRepository(repoDir5h, worldId, actorId, foodGen, physGen);
                Check(repo5h.InitializeEmpty(out _), "chk5h-init-empty");

                var seq1_5h = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1_5h = repo5h.Commit(seq1_5h, foodModel, itemModel);
                Check(resSeq1_5h.Status == CheckpointCommitStatus.Committed, "chk5h-seq1-committed", Diag(resSeq1_5h));

                repo5h.FaultInjection = CheckpointFaultInjectionPoint.FailDuringPointerPublicationReplacement;
                string ptrPath5h = Path.Combine(repoDir5h, FoodOwnershipCheckpointRepository.PointerFileName);
                var cand2h = BuildValidEnvelope(2, seq1_5h.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                bool hookRan5h = false;
                repo5h.TestHookBeforePointerReadback = () =>
                {
                    hookRan5h = true;
                    // Simulate atomic replacement taking effect on disk prior to exception
                    var p = new FoodOwnershipPointer
                    {
                        schema = FoodOwnershipPointer.CurrentSchema,
                        worldId = cand2h.worldId,
                        actorId = cand2h.actorId,
                        foodGeneration = cand2h.foodGeneration,
                        physicalGeneration = cand2h.physicalGeneration,
                        sequence = cand2h.sequence,
                        checkpointFilename = repo5h.FormatCheckpointFilename(cand2h),
                        checkpointHash = cand2h.checkpointHash,
                        previousCheckpointHash = cand2h.previousCheckpointHash
                    };
                    p.pointerHash = FoodOwnershipCheckpointCodec.ComputeCanonicalPointerHash(p);
                    File.WriteAllText(ptrPath5h, FoodOwnershipCheckpointCodec.EncodePointer(p, true));
                };

                var res5h = repo5h.Commit(cand2h, foodModel, itemModel);
                repo5h.TestHookBeforePointerReadback = null;
                repo5h.FaultInjection = CheckpointFaultInjectionPoint.None;

                Check(hookRan5h, "chk5h-hook-actually-ran");
                Check(res5h.Status == CheckpointCommitStatus.CommittedPostCommitFailure, "chk5h-replacement-exception-matching-readback-committed-post-failure", Diag(res5h));
                Check(res5h.CommittedSequence == 2, "chk5h-seq-2-committed", Diag(res5h));

                // Independently reopen/reload: new pointer committed
                var reopen5h = new FoodOwnershipCheckpointRepository(repoDir5h, worldId, actorId, foodGen, physGen);
                var loadResult5h = reopen5h.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5h);
                Check(loadResult5h.Status == CheckpointLoadStatus.Success && restart5h.sequence == 2, "chk5h-restart-resolves-new-pair", Diag(loadResult5h));
                Check(string.Equals(restart5h.checkpointHash, cand2h.checkpointHash, StringComparison.Ordinal), "chk5h-restart-hash-matches-new", Diag(loadResult5h));
                Check(restart5h.receipts.Count == cand2h.receipts.Count, "chk5h-restart-receipts-match-new", Diag(loadResult5h));
            }

            // -------------------------------------------------------------
            // CHECK 5i: Pointer missing after replacement exception -> Indeterminate (missing requiring recovery)
            // -------------------------------------------------------------
            {
                string repoDir5i = Path.Combine(testRootFolder, "chk-5i-pointer-missing");
                var repo5i = new FoodOwnershipCheckpointRepository(repoDir5i, worldId, actorId, foodGen, physGen);
                Check(repo5i.InitializeEmpty(out _), "chk5i-init-empty");

                var seq1_5i = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1_5i = repo5i.Commit(seq1_5i, foodModel, itemModel);
                Check(resSeq1_5i.Status == CheckpointCommitStatus.Committed, "chk5i-seq1-committed", Diag(resSeq1_5i));

                repo5i.FaultInjection = CheckpointFaultInjectionPoint.FailDuringPointerPublicationReplacement;
                string ptrPath5i = Path.Combine(repoDir5i, FoodOwnershipCheckpointRepository.PointerFileName);
                var cand2i = BuildValidEnvelope(2, seq1_5i.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                bool hookRan5i = false;
                repo5i.TestHookBeforePointerReadback = () =>
                {
                    hookRan5i = true;
                    if (File.Exists(ptrPath5i)) File.Delete(ptrPath5i);
                };

                var res5i = repo5i.Commit(cand2i, foodModel, itemModel);
                repo5i.TestHookBeforePointerReadback = null;
                repo5i.FaultInjection = CheckpointFaultInjectionPoint.None;

                Check(hookRan5i, "chk5i-hook-actually-ran");
                Check(res5i.Status == CheckpointCommitStatus.Indeterminate, "chk5i-pointer-missing-indeterminate", Diag(res5i));

                // Independently reopen/reload: missing pointer with existing artifacts fails closed, requiring recovery
                var reopen5i = new FoodOwnershipCheckpointRepository(repoDir5i, worldId, actorId, foodGen, physGen);
                var loadResult5i = reopen5i.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5i);
                Check(loadResult5i.Status == CheckpointLoadStatus.AmbiguousArtifactsWithoutPointer, "chk5i-reopen-missing-pointer-requires-recovery", Diag(loadResult5i));
                Check(restart5i == null, "chk5i-reopen-no-envelope-loaded", Diag(loadResult5i));
            }

            // -------------------------------------------------------------
            // CHECK 5i-corrupt: Corrupt / unclassifiable pointer on disk after replacement exception -> Indeterminate
            // -------------------------------------------------------------
            {
                string repoDir5iCorrupt = Path.Combine(testRootFolder, "chk-5i-pointer-corrupt");
                var repo5iCorrupt = new FoodOwnershipCheckpointRepository(repoDir5iCorrupt, worldId, actorId, foodGen, physGen);
                Check(repo5iCorrupt.InitializeEmpty(out _), "chk5i-corrupt-init-empty");

                var seq1_5iCorrupt = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1_5iCorrupt = repo5iCorrupt.Commit(seq1_5iCorrupt, foodModel, itemModel);
                Check(resSeq1_5iCorrupt.Status == CheckpointCommitStatus.Committed, "chk5i-corrupt-seq1-committed", Diag(resSeq1_5iCorrupt));

                repo5iCorrupt.FaultInjection = CheckpointFaultInjectionPoint.FailDuringPointerPublicationReplacement;
                string ptrPath5iCorrupt = Path.Combine(repoDir5iCorrupt, FoodOwnershipCheckpointRepository.PointerFileName);
                var cand2iCorrupt = BuildValidEnvelope(2, seq1_5iCorrupt.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                bool hookRan5iCorrupt = false;
                repo5iCorrupt.TestHookBeforePointerReadback = () =>
                {
                    hookRan5iCorrupt = true;
                    File.WriteAllText(ptrPath5iCorrupt, "corrupt-unclassifiable-pointer-bytes");
                };

                var res5iCorrupt = repo5iCorrupt.Commit(cand2iCorrupt, foodModel, itemModel);
                repo5iCorrupt.TestHookBeforePointerReadback = null;
                repo5iCorrupt.FaultInjection = CheckpointFaultInjectionPoint.None;

                Check(hookRan5iCorrupt, "chk5i-corrupt-hook-actually-ran");
                Check(res5iCorrupt.Status == CheckpointCommitStatus.Indeterminate, "chk5i-pointer-corrupt-indeterminate", Diag(res5iCorrupt));

                // Independently reopen/reload: corrupt pointer fails closed, requiring recovery
                var reopen5iCorrupt = new FoodOwnershipCheckpointRepository(repoDir5iCorrupt, worldId, actorId, foodGen, physGen);
                var loadResult5iCorrupt = reopen5iCorrupt.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restart5iCorrupt);
                Check(loadResult5iCorrupt.Status == CheckpointLoadStatus.CorruptPointer, "chk5i-reopen-corrupt-pointer-requires-recovery", Diag(loadResult5iCorrupt));
                Check(restart5iCorrupt == null, "chk5i-reopen-corrupt-no-envelope-loaded", Diag(loadResult5iCorrupt));
            }

            // -------------------------------------------------------------
            // CHECK 5-firstpub-missing: First publication with missing readback pointer -> Indeterminate (never NotCommitted)
            // -------------------------------------------------------------
            {
                string repoDirFirstPubMissing = Path.Combine(testRootFolder, "chk-5-firstpub-missing");
                var repoFirstPubMissing = new FoodOwnershipCheckpointRepository(repoDirFirstPubMissing, worldId, actorId, foodGen, physGen);
                Check(repoFirstPubMissing.InitializeEmpty(out _), "chk5-firstpub-missing-init-empty");

                repoFirstPubMissing.FaultInjection = CheckpointFaultInjectionPoint.FailDuringPointerPublicationReplacement;
                string ptrPathFirstPubMissing = Path.Combine(repoDirFirstPubMissing, FoodOwnershipCheckpointRepository.PointerFileName);
                bool hookRanFirstPubMissing = false;
                repoFirstPubMissing.TestHookBeforePointerReadback = () =>
                {
                    hookRanFirstPubMissing = true;
                    if (File.Exists(ptrPathFirstPubMissing)) File.Delete(ptrPathFirstPubMissing);
                };

                var cand1Missing = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resFirstPubMissing = repoFirstPubMissing.Commit(cand1Missing, foodModel, itemModel);
                repoFirstPubMissing.TestHookBeforePointerReadback = null;
                repoFirstPubMissing.FaultInjection = CheckpointFaultInjectionPoint.None;

                Check(hookRanFirstPubMissing, "chk5-firstpub-missing-hook-reached");
                Check(resFirstPubMissing.Status == CheckpointCommitStatus.Indeterminate, "chk5-firstpub-missing-indeterminate", Diag(resFirstPubMissing));

                // Independently reopen/reload: ambiguous artifacts (chk-1 file exists without pointer) requiring recovery
                var reopenFirstPubMissing = new FoodOwnershipCheckpointRepository(repoDirFirstPubMissing, worldId, actorId, foodGen, physGen);
                var loadResultFirstPubMissing = reopenFirstPubMissing.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restartFirstPubMissing);
                Check(loadResultFirstPubMissing.Status == CheckpointLoadStatus.AmbiguousArtifactsWithoutPointer, "chk5-firstpub-missing-reopen-requires-recovery", Diag(loadResultFirstPubMissing));
                Check(restartFirstPubMissing == null, "chk5-firstpub-missing-reopen-no-env", Diag(loadResultFirstPubMissing));
            }

            // -------------------------------------------------------------
            // CHECK 5-firstpub-unreadable: First publication with unreadable readback pointer -> Indeterminate (never NotCommitted)
            // -------------------------------------------------------------
            {
                string repoDirFirstPubUnreadable = Path.Combine(testRootFolder, "chk-5-firstpub-unreadable");
                var repoFirstPubUnreadable = new FoodOwnershipCheckpointRepository(repoDirFirstPubUnreadable, worldId, actorId, foodGen, physGen);
                Check(repoFirstPubUnreadable.InitializeEmpty(out _), "chk5-firstpub-unreadable-init-empty");

                repoFirstPubUnreadable.FaultInjection = CheckpointFaultInjectionPoint.FailDuringPointerPublicationReplacement;
                string ptrPathFirstPubUnreadable = Path.Combine(repoDirFirstPubUnreadable, FoodOwnershipCheckpointRepository.PointerFileName);
                bool hookRanFirstPubUnreadable = false;
                FileStream lockHoldFirstPub = null;
                repoFirstPubUnreadable.TestHookBeforePointerReadback = () =>
                {
                    hookRanFirstPubUnreadable = true;
                    // Create pointer file but hold exclusive sharing lock so readback is unreadable (IO fault)
                    lockHoldFirstPub = new FileStream(ptrPathFirstPubUnreadable, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                };

                var cand1Unreadable = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resFirstPubUnreadable = repoFirstPubUnreadable.Commit(cand1Unreadable, foodModel, itemModel);
                if (lockHoldFirstPub != null) { lockHoldFirstPub.Dispose(); lockHoldFirstPub = null; }
                repoFirstPubUnreadable.TestHookBeforePointerReadback = null;
                repoFirstPubUnreadable.FaultInjection = CheckpointFaultInjectionPoint.None;

                Check(hookRanFirstPubUnreadable, "chk5-firstpub-unreadable-hook-reached");
                Check(resFirstPubUnreadable.Status == CheckpointCommitStatus.Indeterminate, "chk5-firstpub-unreadable-indeterminate", Diag(resFirstPubUnreadable));

                // Independently reopen/reload: unreadable/corrupt (0-byte) pointer requiring recovery
                var reopenFirstPubUnreadable = new FoodOwnershipCheckpointRepository(repoDirFirstPubUnreadable, worldId, actorId, foodGen, physGen);
                var loadResultFirstPubUnreadable = reopenFirstPubUnreadable.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var restartFirstPubUnreadable);
                Check(loadResultFirstPubUnreadable.Status == CheckpointLoadStatus.CorruptPointer, "chk5-firstpub-unreadable-reopen-requires-recovery", Diag(loadResultFirstPubUnreadable));
                Check(restartFirstPubUnreadable == null, "chk5-firstpub-unreadable-reopen-no-env", Diag(loadResultFirstPubUnreadable));
            }

            // -------------------------------------------------------------
            // CHECK 6: Corrupt newest acknowledged checkpoint/pointer: explicit fail-closed, NO fallback or empty init
            // -------------------------------------------------------------
            {
                string repoDir = Path.Combine(testRootFolder, "chk-6-corruption");
                var repo = new FoodOwnershipCheckpointRepository(repoDir, worldId, actorId, foodGen, physGen);
                Check(repo.InitializeEmpty(out _), "chk6-init-empty");

                var seq1 = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1 = repo.Commit(seq1, foodModel, itemModel);
                Check(resSeq1.Status == CheckpointCommitStatus.Committed, "chk6-seq1-committed", Diag(resSeq1));

                var seq2 = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var resSeq2 = repo.Commit(seq2, foodModel, itemModel);
                Check(resSeq2.Status == CheckpointCommitStatus.Committed, "chk6-seq2-committed", Diag(resSeq2));

                // Case A: Corrupt newest checkpoint file on disk -> Load fails closed
                string chk2Path = Path.Combine(repoDir, repo.FormatCheckpointFilename(seq2));
                Check(File.Exists(chk2Path), "chk6-chk2-file-exists");
                File.WriteAllText(chk2Path, "{\"corrupt\": true, \"truncated\":");

                var loadResultA = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loadedA);
                Check(loadResultA.Status == CheckpointLoadStatus.CorruptCheckpoint, "chk6-corrupt-checkpoint-fails-closed", Diag(loadResultA));
                Check(loadedA == null, "chk6-no-fallback-to-seq1-on-corrupt-seq2", Diag(loadResultA));

                // Case B: Corrupt authoritative pointer file on disk -> Load fails closed
                string ptrPath = Path.Combine(repoDir, FoodOwnershipCheckpointRepository.PointerFileName);
                Check(File.Exists(ptrPath), "chk6-ptr-file-exists");
                File.WriteAllText(ptrPath, "corrupt-pointer-bytes");

                var loadResultB = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loadedB);
                Check(loadResultB.Status == CheckpointLoadStatus.CorruptPointer, "chk6-corrupt-pointer-fails-closed", Diag(loadResultB));
                Check(loadedB == null, "chk6-no-fallback-on-corrupt-pointer", Diag(loadResultB));

                // Case C: Finding 3 - Commit successor after current acknowledged checkpoint file corruption MUST fail closed
                string repoDirC = Path.Combine(testRootFolder, "chk-6-commit-corruption");
                var repoC = new FoodOwnershipCheckpointRepository(repoDirC, worldId, actorId, foodGen, physGen);
                Check(repoC.InitializeEmpty(out _), "chk6c-init-empty");

                var seq1C = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1C = repoC.Commit(seq1C, foodModel, itemModel);
                Check(resSeq1C.Status == CheckpointCommitStatus.Committed, "chk6c-seq1-committed", Diag(resSeq1C));

                // Corrupt sequence 1 checkpoint file on disk
                string seq1PathC = Path.Combine(repoDirC, repoC.FormatCheckpointFilename(seq1C));
                File.WriteAllText(seq1PathC, "{\"schema\":\"corrupt-data\"}");

                // Submit valid sequence 2 candidate
                var validSeq2C = BuildValidEnvelope(2, seq1C.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var commitCorruptResult = repoC.Commit(validSeq2C, foodModel, itemModel);
                Check(commitCorruptResult.Status == CheckpointCommitStatus.CorruptState, "chk6c-commit-fails-closed-on-corrupt-current-checkpoint", Diag(commitCorruptResult));
                Check(commitCorruptResult.Message.Contains("Current authoritative checkpoint"), "chk6c-error-identified-corrupt-current", Diag(commitCorruptResult));

                // Verify pointer on disk was NOT updated to sequence 2
                var ptrLoad = repoC.GetAuthoritativePointer();
                Check(ptrLoad.Status == CheckpointLoadStatus.Success && ptrLoad.Pointer.sequence == 1, "chk6c-pointer-sequence-remains-1", Diag(ptrLoad));

                // Case D: Commit successor after pointer corruption MUST fail closed
                File.WriteAllText(Path.Combine(repoDirC, FoodOwnershipCheckpointRepository.PointerFileName), "tampered-corrupt-pointer");
                var commitCorruptPtrResult = repoC.Commit(validSeq2C, foodModel, itemModel);
                Check(commitCorruptPtrResult.Status == CheckpointCommitStatus.CorruptState, "chk6d-commit-fails-closed-on-corrupt-pointer", Diag(commitCorruptPtrResult));
            }

            // -------------------------------------------------------------
            // CHECK 7: Orphan precommit candidate ignored; ambiguous no-pointer nonempty repository rejected
            // -------------------------------------------------------------
            {
                string repoDirA = Path.Combine(testRootFolder, "chk-7-orphans");
                var repoA = new FoodOwnershipCheckpointRepository(repoDirA, worldId, actorId, foodGen, physGen);
                Check(repoA.InitializeEmpty(out _), "chk7-init-empty");

                var seq1 = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1A = repoA.Commit(seq1, foodModel, itemModel);
                Check(resSeq1A.Status == CheckpointCommitStatus.Committed, "chk7-seq1-committed", Diag(resSeq1A));

                // Create unreferenced orphan temporary files
                File.WriteAllText(Path.Combine(repoDirA, "chk-orphan.tmp-12345"), "orphan staging bytes");
                File.WriteAllText(Path.Combine(repoDirA, "chk-uncommitted-seq00000099.json"), "uncommitted orphan checkpoint");

                var loadResultA = repoA.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loadedA);
                Check(loadResultA.Status == CheckpointLoadStatus.Success && loadedA.sequence == 1,
                    "chk7-orphan-precommit-candidates-ignored", Diag(loadResultA));

                // Finding 6: Ambiguous nonempty repository without pointer:
                // Test 7B1: Directory contains only chk-*.json without pointer -> AmbiguousArtifactsWithoutPointer
                string repoDirB1 = Path.Combine(testRootFolder, "chk-7-ambiguous-chk");
                Directory.CreateDirectory(repoDirB1);
                File.WriteAllText(Path.Combine(repoDirB1, "chk-combined-v1-gen-01-seq00000001-fake.json"), "checkpoint data without pointer");

                var repoB1 = new FoodOwnershipCheckpointRepository(repoDirB1, worldId, actorId, foodGen, physGen);
                var loadResultB1 = repoB1.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loadedB1);
                Check(loadResultB1.Status == CheckpointLoadStatus.AmbiguousArtifactsWithoutPointer,
                    "chk7-ambiguous-nonempty-no-pointer-rejected-fail-closed", Diag(loadResultB1));
                Check(loadedB1 == null, "chk7-ambiguous-repository-no-state-loaded", Diag(loadResultB1));
                Check(!repoB1.InitializeEmpty(out _), "chk7-reject-init-on-existing-chk-artifacts");

                // Test 7B2: Directory contains only backup pointer without pointer -> AmbiguousArtifactsWithoutPointer, NOT EmptyRepository
                string repoDirB2 = Path.Combine(testRootFolder, "chk-7-ambiguous-backup");
                Directory.CreateDirectory(repoDirB2);
                File.WriteAllText(Path.Combine(repoDirB2, FoodOwnershipCheckpointRepository.BackupPointerFileName), "backup-pointer-bytes");

                var repoB2 = new FoodOwnershipCheckpointRepository(repoDirB2, worldId, actorId, foodGen, physGen);
                var ptrB2 = repoB2.GetAuthoritativePointer();
                Check(ptrB2.Status == CheckpointLoadStatus.AmbiguousArtifactsWithoutPointer,
                    "chk7-backup-only-rejected-ambiguous-not-empty", Diag(ptrB2));
                Check(!repoB2.InitializeEmpty(out string bakInitErr), "chk7-reject-init-on-backup-artifact");
                Check(bakInitErr.Contains("backup pointer artifact exists"), "chk7-backup-init-error-identified");

                // Test 7B3: Directory contains only staging directory without pointer -> AmbiguousArtifactsWithoutPointer, NOT EmptyRepository
                string repoDirB3 = Path.Combine(testRootFolder, "chk-7-ambiguous-staging");
                Directory.CreateDirectory(repoDirB3);
                Directory.CreateDirectory(Path.Combine(repoDirB3, FoodOwnershipCheckpointRepository.StagingSubdir));

                var repoB3 = new FoodOwnershipCheckpointRepository(repoDirB3, worldId, actorId, foodGen, physGen);
                var ptrB3 = repoB3.GetAuthoritativePointer();
                Check(ptrB3.Status == CheckpointLoadStatus.AmbiguousArtifactsWithoutPointer,
                    "chk7-staging-only-rejected-ambiguous-not-empty", Diag(ptrB3));
                Check(!repoB3.InitializeEmpty(out string stgInitErr), "chk7-reject-init-on-staging-artifact");
                Check(stgInitErr.Contains("staging artifacts exist"), "chk7-staging-init-error-identified");

                // Test 7B4: Directory contains only orphan precommit candidate (.tmp) without pointer -> AmbiguousArtifactsWithoutPointer
                string repoDirB4 = Path.Combine(testRootFolder, "chk-7-ambiguous-first-candidate-orphan");
                Directory.CreateDirectory(repoDirB4);
                File.WriteAllText(Path.Combine(repoDirB4, "chk-combined-v1-gen-01-seq00000001.tmp-999"), "orphan-candidate-bytes");

                var repoB4 = new FoodOwnershipCheckpointRepository(repoDirB4, worldId, actorId, foodGen, physGen);
                var ptrB4 = repoB4.GetAuthoritativePointer();
                Check(ptrB4.Status == CheckpointLoadStatus.AmbiguousArtifactsWithoutPointer,
                    "chk7-first-candidate-orphan-rejected-ambiguous", Diag(ptrB4));
                Check(!repoB4.InitializeEmpty(out _), "chk7-reject-init-on-orphan-candidate");

                // Test 7B5: Directory contains only unknown artifact -> AmbiguousArtifactsWithoutPointer, NOT EmptyRepository
                string repoDirB5 = Path.Combine(testRootFolder, "chk-7-ambiguous-unknown");
                Directory.CreateDirectory(repoDirB5);
                File.WriteAllText(Path.Combine(repoDirB5, "foreign-artifact.dat"), "random-bytes");

                var repoB5 = new FoodOwnershipCheckpointRepository(repoDirB5, worldId, actorId, foodGen, physGen);
                var ptrB5 = repoB5.GetAuthoritativePointer();
                Check(ptrB5.Status == CheckpointLoadStatus.AmbiguousArtifactsWithoutPointer,
                    "chk7-unknown-artifact-rejected-ambiguous", Diag(ptrB5));
                Check(!repoB5.InitializeEmpty(out string unkInitErr), "chk7-reject-init-on-unknown-artifact");
                Check(unkInitErr.Contains("non-empty directory contains artifact"), "chk7-unknown-init-error-identified");

                // Test 7B6: Truly empty initialized repository (contains only LockFileName and InitFileName) returns EmptyRepository
                string repoDirB6 = Path.Combine(testRootFolder, "chk-7-initialized-empty");
                var repoB6 = new FoodOwnershipCheckpointRepository(repoDirB6, worldId, actorId, foodGen, physGen);
                Check(repoB6.InitializeEmpty(out _), "chk7-init-empty-b6");
                var ptrB6 = repoB6.GetAuthoritativePointer();
                Check(ptrB6.Status == CheckpointLoadStatus.EmptyRepository,
                    "chk7-empty-repo-returns-empty-status", Diag(ptrB6));
            }

            // -------------------------------------------------------------
            // CHECK 8: Stale sequence/competing writer rejection, cross-scope overwrite prevention, and init bypass rejection
            // -------------------------------------------------------------
            {
                string repoDir = Path.Combine(testRootFolder, "chk-8-concurrency");
                var repo = new FoodOwnershipCheckpointRepository(repoDir, worldId, actorId, foodGen, physGen);
                Check(repo.InitializeEmpty(out _), "chk8-init-empty");

                var seq1 = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1 = repo.Commit(seq1, foodModel, itemModel);
                Check(resSeq1.Status == CheckpointCommitStatus.Committed, "chk8-seq1-committed", Diag(resSeq1));

                var seq2 = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                var resSeq2 = repo.Commit(seq2, foodModel, itemModel);
                Check(resSeq2.Status == CheckpointCommitStatus.Committed, "chk8-seq2-committed", Diag(resSeq2));

                // Stale sequence: attempt committing sequence 1 or 2 again
                var staleCand = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resStale = repo.Commit(staleCand, foodModel, itemModel);
                Check(resStale.Status == CheckpointCommitStatus.ConcurrencyConflict,
                    "chk8-reject-stale-sequence", Diag(resStale));

                // Sequence gap: attempt committing sequence 4 when sequence 2 is head
                var gapCand = BuildValidEnvelope(4, seq2.checkpointHash, 4, new List<FoodOwnershipReceiptRecord>());
                var resGap = repo.Commit(gapCand, foodModel, itemModel);
                Check(resGap.Status == CheckpointCommitStatus.ConcurrencyConflict,
                    "chk8-reject-sequence-gap", Diag(resGap));

                // Previous hash mismatch: sequence 3 with bad previous hash
                var badPrevCand = BuildValidEnvelope(3, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", 3, new List<FoodOwnershipReceiptRecord>());
                var resBadPrev = repo.Commit(badPrevCand, foodModel, itemModel);
                Check(resBadPrev.Status == CheckpointCommitStatus.ConcurrencyConflict,
                    "chk8-reject-previous-hash-mismatch", Diag(resBadPrev));

                // Competing writer: hold OS-exclusive lock on lock file
                string lockFilePath = Path.Combine(repoDir, FoodOwnershipCheckpointRepository.LockFileName);
                using (var competingLock = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    var cand3 = BuildValidEnvelope(3, seq2.checkpointHash, 3, new List<FoodOwnershipReceiptRecord>());
                    var competingResult = repo.Commit(cand3, foodModel, itemModel);
                    Check(competingResult.Status == CheckpointCommitStatus.ConcurrencyConflict,
                        "chk8-reject-competing-writer-holding-lock", Diag(competingResult));
                }

                // Immutable-name collision: candidate matching existing sequence 2 file name but divergent bytes
                string seq2FileName = repo.FormatCheckpointFilename(seq2);
                string seq2Path = Path.Combine(repoDir, seq2FileName);
                string originalSeq2Content = File.ReadAllText(seq2Path);

                var collisionCand = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                collisionCand.receipts.Add(new FoodOwnershipReceiptRecord
                {
                    transactionId = 99,
                    requestSignature = "Collision",
                    status = FoodOwnershipReceiptStatus.Committed,
                    statusCode = "ok",
                    checkpointSequence = 2,
                    payloadHash = FoodOwnershipCheckpointCodec.ComputeSha256("collision-hash")
                });
                collisionCand.checkpointHash = seq2.checkpointHash; // Force matching filename

                var collisionResult = repo.Commit(collisionCand, foodModel, itemModel);
                Check(collisionResult.Status == CheckpointCommitStatus.ConcurrencyConflict,
                    "chk8-reject-immutable-collision", Diag(collisionResult));
                Check(File.ReadAllText(seq2Path) == originalSeq2Content,
                    "chk8-immutable-collision-preserves-prior-bytes");

                // Finding 2: Cross-scope overwrite rejection test
                string repoDirCross = Path.Combine(testRootFolder, "chk-8-cross-scope");
                var repoScopeA = new FoodOwnershipCheckpointRepository(repoDirCross, "world-A", "actor-A", foodGen, physGen);
                Check(repoScopeA.InitializeEmpty(out _), "chk8-init-scope-A");

                var foodModelA = new FoodModel("world-A", foodGen, 4242);
                foodModelA.State.actorId = "actor-A";
                var itemModelA = new ItemModel("world-A", physGen);
                PhysicalItemCatalog.CreateDefaultCatalog().PopulateModel(itemModelA);

                var candScopeA = new FoodOwnershipCheckpointEnvelope
                {
                    schema = FoodOwnershipCheckpointEnvelope.CurrentSchema,
                    worldId = "world-A",
                    actorId = "actor-A",
                    foodGeneration = foodGen,
                    physicalGeneration = physGen,
                    sequence = 1,
                    previousCheckpointHash = "",
                    requestHighWatermark = 1,
                    foodPayload = foodModelA.Json(),
                    physicalPayload = JsonUtility.ToJson(ItemPersistence.CreateSnapshot(itemModelA, "actor-A"), false),
                    receipts = new List<FoodOwnershipReceiptRecord>()
                };
                candScopeA.foodPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(candScopeA.foodPayload);
                candScopeA.physicalPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(candScopeA.physicalPayload);
                candScopeA.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(candScopeA);

                var resScopeA = repoScopeA.Commit(candScopeA, foodModelA, itemModelA);
                Check(resScopeA.Status == CheckpointCommitStatus.Committed, "chk8-commit-scope-A", Diag(resScopeA));

                string ptrPathA = Path.Combine(repoDirCross, FoodOwnershipCheckpointRepository.PointerFileName);
                string originalPointerA = File.ReadAllText(ptrPathA);
                string chkPathA = Path.Combine(repoDirCross, repoScopeA.FormatCheckpointFilename(candScopeA));
                string originalChkContentA = File.ReadAllText(chkPathA);

                // Scope B repo attempts commit to Scope A's directory
                var repoScopeB = new FoodOwnershipCheckpointRepository(repoDirCross, "world-B", "actor-B", foodGen, physGen);
                var foodModelB = new FoodModel("world-B", foodGen, 5555);
                foodModelB.State.actorId = "actor-B";
                var itemModelB = new ItemModel("world-B", physGen);
                PhysicalItemCatalog.CreateDefaultCatalog().PopulateModel(itemModelB);

                var candScopeB = new FoodOwnershipCheckpointEnvelope
                {
                    schema = FoodOwnershipCheckpointEnvelope.CurrentSchema,
                    worldId = "world-B",
                    actorId = "actor-B",
                    foodGeneration = foodGen,
                    physicalGeneration = physGen,
                    sequence = 1,
                    previousCheckpointHash = "",
                    requestHighWatermark = 1,
                    foodPayload = foodModelB.Json(),
                    physicalPayload = JsonUtility.ToJson(ItemPersistence.CreateSnapshot(itemModelB, "actor-B"), false),
                    receipts = new List<FoodOwnershipReceiptRecord>()
                };
                candScopeB.foodPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(candScopeB.foodPayload);
                candScopeB.physicalPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(candScopeB.physicalPayload);
                candScopeB.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(candScopeB);

                var crossCommitResult = repoScopeB.Commit(candScopeB, foodModelB, itemModelB);
                Check(crossCommitResult.Status == CheckpointCommitStatus.CorruptState, "chk8-reject-cross-scope-commit", Diag(crossCommitResult));
                Check(crossCommitResult.Message.Contains("ScopeMismatch"), "chk8-cross-scope-message-identified", Diag(crossCommitResult));

                // Assert scope A's bytes on disk remain completely unmodified
                Check(File.ReadAllText(ptrPathA) == originalPointerA, "chk8-scope-A-pointer-preserved");
                Check(File.ReadAllText(chkPathA) == originalChkContentA, "chk8-scope-A-checkpoint-file-preserved");

                // Finding 6: Bypass test - Commit on uninitialized directory fails closed
                string uninitDir = Path.Combine(testRootFolder, "chk-8-uninit-bypass");
                var uninitRepo = new FoodOwnershipCheckpointRepository(uninitDir, worldId, actorId, foodGen, physGen);
                var uninitResult = uninitRepo.Commit(seq1, foodModel, itemModel);
                Check(uninitResult.Status == CheckpointCommitStatus.CorruptState, "chk8-reject-uninitialized-commit-bypass", Diag(uninitResult));

                // Finding 6: Cross-scope InitializeEmpty conflict
                string crossInitDir = Path.Combine(testRootFolder, "chk-8-cross-init");
                var initScope1 = new FoodOwnershipCheckpointRepository(crossInitDir, "world-1", actorId, foodGen, physGen);
                Check(initScope1.InitializeEmpty(out _), "chk8-init-scope-1");

                var initScope2 = new FoodOwnershipCheckpointRepository(crossInitDir, "world-2", actorId, foodGen, physGen);
                Check(!initScope2.InitializeEmpty(out string crossInitErr), "chk8-reject-cross-scope-init");
                Check(crossInitErr.Contains("Cross-scope initialization conflict"), "chk8-cross-init-conflict-identified");

                // Finding 6: Competing lock on InitializeEmpty
                string lockInitDir = Path.Combine(testRootFolder, "chk-8-lock-init");
                Directory.CreateDirectory(lockInitDir);
                using (new FileStream(Path.Combine(lockInitDir, FoodOwnershipCheckpointRepository.LockFileName), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    var lockRepo = new FoodOwnershipCheckpointRepository(lockInitDir, worldId, actorId, foodGen, physGen);
                    Check(!lockRepo.InitializeEmpty(out string lockErr), "chk8-reject-init-competing-lock");
                    Check(lockErr.Contains("Exclusive OS lock acquisition failed"), "chk8-init-lock-error-identified");
                }
            }

            // -------------------------------------------------------------
            // CHECK 9: Authoritative load validation: fail-closed missing/null/wrong authority, semantic tamper detection under load, and sequence/hash diagnostic safety
            // -------------------------------------------------------------
            {
                string repoDir = Path.Combine(testRootFolder, "chk-9-load-authoritative");
                var repo = new FoodOwnershipCheckpointRepository(repoDir, worldId, actorId, foodGen, physGen);
                Check(repo.InitializeEmpty(out _), "chk9-init-empty");

                var seq1 = BuildValidEnvelope(1, "", 1, new List<FoodOwnershipReceiptRecord>());
                var resSeq1_9 = repo.Commit(seq1, foodModel, itemModel);
                Check(resSeq1_9.Status == CheckpointCommitStatus.Committed, "chk9-seq1-committed", Diag(resSeq1_9));

                string ptrPath = Path.Combine(repoDir, FoodOwnershipCheckpointRepository.PointerFileName);

                // 9A: Legacy no-authority overload fails closed with MissingAuthority
                var oldRes = repo.LoadAuthoritativeCheckpoint(out var oldEnv);
                Check(oldRes.Status == CheckpointLoadStatus.MissingAuthority, "chk9a-reject-no-authority-overload", Diag(oldRes));
                Check(oldRes.Checkpoint == null, "chk9a-no-authority-result-checkpoint-null", Diag(oldRes));
                Check(oldEnv == null, "chk9a-no-authority-envelope-null", Diag(oldRes));
                Check(oldRes.Message.Contains("Authoritative FoodModel and ItemModel are required"), "chk9a-no-authority-message-identified", Diag(oldRes));

                // 9B: Null food authority rejected
                var nullFoodRes = repo.LoadAuthoritativeCheckpoint(null, itemModel, out var nullFoodEnv);
                Check(nullFoodRes.Status == CheckpointLoadStatus.MissingAuthority, "chk9b-reject-null-food-authority", Diag(nullFoodRes));
                Check(nullFoodRes.Checkpoint == null, "chk9b-null-food-result-checkpoint-null", Diag(nullFoodRes));
                Check(nullFoodEnv == null, "chk9b-null-food-envelope-null", Diag(nullFoodRes));
                Check(nullFoodRes.Message.Contains("authoritativeFoodModel is required"), "chk9b-null-food-message-identified", Diag(nullFoodRes));

                // 9C: Null item authority rejected
                var nullItemRes = repo.LoadAuthoritativeCheckpoint(foodModel, null, out var nullItemEnv);
                Check(nullItemRes.Status == CheckpointLoadStatus.MissingAuthority, "chk9c-reject-null-item-authority", Diag(nullItemRes));
                Check(nullItemRes.Checkpoint == null, "chk9c-null-item-result-checkpoint-null", Diag(nullItemRes));
                Check(nullItemEnv == null, "chk9c-null-item-envelope-null", Diag(nullItemRes));
                Check(nullItemRes.Message.Contains("authoritativeItemModel is required"), "chk9c-null-item-message-identified", Diag(nullItemRes));

                // 9D: Wrong food authority model (seed 99999) rejected with CorruptCheckpoint; models and pointer unmutated
                string ptrBefore9d = File.ReadAllText(ptrPath);
                int foodSatietyBefore9d = foodModel.State.satiety;
                int itemCountBefore9d = itemModel.ItemCount;

                var wrongFoodAuthority = new FoodModel(worldId, foodGen, 99999);
                wrongFoodAuthority.State.actorId = actorId;
                var wrongFoodRes = repo.LoadAuthoritativeCheckpoint(wrongFoodAuthority, itemModel, out var wrongFoodEnv);
                Check(wrongFoodRes.Status == CheckpointLoadStatus.CorruptCheckpoint, "chk9d-reject-wrong-food-authority", Diag(wrongFoodRes));
                Check(wrongFoodRes.Checkpoint == null, "chk9d-wrong-food-result-checkpoint-null", Diag(wrongFoodRes));
                Check(wrongFoodEnv == null, "chk9d-wrong-food-envelope-null", Diag(wrongFoodRes));
                Check(File.ReadAllText(ptrPath) == ptrBefore9d, "chk9d-wrong-food-pointer-unchanged");
                Check(foodModel.State.satiety == foodSatietyBefore9d, "chk9d-wrong-food-original-model-unmutated");
                Check(itemModel.ItemCount == itemCountBefore9d, "chk9d-wrong-food-item-model-unmutated");
                Check(wrongFoodAuthority.State.seed == 99999, "chk9d-wrong-food-authority-model-unmutated");

                // 9E: Wrong item authority model ("foreign-world") rejected with ScopeMismatch; models and pointer unmutated
                var wrongItemAuthority = new ItemModel("foreign-world", physGen);
                PhysicalItemCatalog.CreateDefaultCatalog().PopulateModel(wrongItemAuthority);
                string ptrBefore9e = File.ReadAllText(ptrPath);

                var wrongItemRes = repo.LoadAuthoritativeCheckpoint(foodModel, wrongItemAuthority, out var wrongItemEnv);
                Check(wrongItemRes.Status == CheckpointLoadStatus.ScopeMismatch, "chk9e-reject-wrong-item-authority-scope", Diag(wrongItemRes));
                Check(wrongItemRes.Checkpoint == null, "chk9e-wrong-item-result-checkpoint-null", Diag(wrongItemRes));
                Check(wrongItemEnv == null, "chk9e-wrong-item-envelope-null", Diag(wrongItemRes));
                Check(File.ReadAllText(ptrPath) == ptrBefore9e, "chk9e-wrong-item-pointer-unchanged");
                Check(foodModel.State.satiety == foodSatietyBefore9d, "chk9e-wrong-item-food-model-unmutated");
                Check(wrongItemAuthority.WorldId == "foreign-world", "chk9e-wrong-item-authority-model-unmutated");

                // Helper to publish a tampered checkpoint to disk with fully recomputed canonical hashes
                void InstallTamperedAuthoritativeCheckpoint(FoodOwnershipCheckpointEnvelope tampered)
                {
                    tampered.foodPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(tampered.foodPayload);
                    tampered.physicalPayloadHash = FoodOwnershipCheckpointCodec.ComputeSha256(tampered.physicalPayload);
                    tampered.checkpointHash = FoodOwnershipCheckpointCodec.ComputeCanonicalEnvelopeHash(tampered);

                    string chkFilename = repo.FormatCheckpointFilename(tampered);
                    File.WriteAllText(Path.Combine(repoDir, chkFilename), FoodOwnershipCheckpointCodec.EncodeEnvelope(tampered, true));

                    var ptr = new FoodOwnershipPointer
                    {
                        schema = FoodOwnershipPointer.CurrentSchema,
                        worldId = tampered.worldId,
                        actorId = tampered.actorId,
                        foodGeneration = tampered.foodGeneration,
                        physicalGeneration = tampered.physicalGeneration,
                        sequence = tampered.sequence,
                        checkpointFilename = chkFilename,
                        checkpointHash = tampered.checkpointHash,
                        previousCheckpointHash = tampered.previousCheckpointHash
                    };
                    ptr.pointerHash = FoodOwnershipCheckpointCodec.ComputeCanonicalPointerHash(ptr);
                    File.WriteAllText(ptrPath, FoodOwnershipCheckpointCodec.EncodePointer(ptr, true));
                }

                // 9F: Tampered food seed with valid recomputed hashes -> CorruptCheckpoint, envelope null, no fallback
                var tamperedSeedFood = new FoodModel(worldId, foodGen, 88888);
                tamperedSeedFood.State.actorId = actorId;
                var env9f = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                env9f.foodPayload = tamperedSeedFood.Json();
                InstallTamperedAuthoritativeCheckpoint(env9f);
                string ptrBefore9f = File.ReadAllText(ptrPath);

                var res9f = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loaded9f);
                Check(res9f.Status == CheckpointLoadStatus.CorruptCheckpoint, "chk9f-tampered-seed-recomputed-hash-corrupt", Diag(res9f));
                Check(res9f.Checkpoint == null, "chk9f-tampered-seed-result-checkpoint-null", Diag(res9f));
                Check(loaded9f == null, "chk9f-tampered-seed-no-fallback", Diag(res9f));
                Check(File.ReadAllText(ptrPath) == ptrBefore9f, "chk9f-tampered-seed-pointer-unchanged");
                Check(foodModel.State.satiety == foodSatietyBefore9d, "chk9f-tampered-seed-food-unmutated");
                Check(itemModel.ItemCount == itemCountBefore9d, "chk9f-tampered-seed-item-unmutated");

                // 9G: Tampered body physiology (satiety 99999 > 10000) with valid recomputed hashes -> CorruptCheckpoint, no fallback
                var foodState9g = JsonUtility.FromJson<FoodState>(foodModel.Json());
                foodState9g.satiety = 99999;
                var env9g = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                env9g.foodPayload = JsonUtility.ToJson(foodState9g);
                InstallTamperedAuthoritativeCheckpoint(env9g);
                string ptrBefore9g = File.ReadAllText(ptrPath);

                var res9g = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loaded9g);
                Check(res9g.Status == CheckpointLoadStatus.CorruptCheckpoint, "chk9g-tampered-satiety-recomputed-hash-corrupt", Diag(res9g));
                Check(res9g.Checkpoint == null, "chk9g-tampered-satiety-result-checkpoint-null", Diag(res9g));
                Check(loaded9g == null, "chk9g-tampered-satiety-no-fallback", Diag(res9g));
                Check(File.ReadAllText(ptrPath) == ptrBefore9g, "chk9g-tampered-satiety-pointer-unchanged");
                Check(foodModel.State.satiety == foodSatietyBefore9d, "chk9g-tampered-satiety-food-unmutated");
                Check(itemModel.ItemCount == itemCountBefore9d, "chk9g-tampered-satiety-item-unmutated");

                // 9H: Tampered place ledger cell (x = 99999 > 3334) with valid recomputed hashes -> CorruptCheckpoint, no fallback
                var foodState9h = JsonUtility.FromJson<FoodState>(foodModel.Json());
                foodState9h.exploredCells = new List<PlaceCell>
                {
                    new PlaceCell
                    {
                        world = worldId,
                        generation = foodGen,
                        actor = actorId,
                        x = 99999,
                        z = 0,
                        firstFoodTick = 0,
                        lastFoodTick = 0,
                        visits = 1
                    }
                };
                var env9h = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                env9h.foodPayload = JsonUtility.ToJson(foodState9h);
                InstallTamperedAuthoritativeCheckpoint(env9h);
                string ptrBefore9h = File.ReadAllText(ptrPath);

                var res9h = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loaded9h);
                Check(res9h.Status == CheckpointLoadStatus.CorruptCheckpoint, "chk9h-tampered-place-ledger-recomputed-hash-corrupt", Diag(res9h));
                Check(res9h.Checkpoint == null, "chk9h-tampered-place-ledger-result-checkpoint-null", Diag(res9h));
                Check(loaded9h == null, "chk9h-tampered-place-ledger-no-fallback", Diag(res9h));
                Check(File.ReadAllText(ptrPath) == ptrBefore9h, "chk9h-tampered-place-ledger-pointer-unchanged");
                Check(foodModel.State.satiety == foodSatietyBefore9d, "chk9h-tampered-place-ledger-food-unmutated");
                Check(itemModel.ItemCount == itemCountBefore9d, "chk9h-tampered-place-ledger-item-unmutated");

                // 9I: Tampered physical unknown type ("plasma-chisel") with valid recomputed hashes -> CorruptCheckpoint, no fallback
                var physSnapshot9i = ItemPersistence.CreateSnapshot(itemModel, actorId);
                physSnapshot9i.items.Add(new SavedItemRecord
                {
                    itemId = "alien-laser-1",
                    itemTypeId = "plasma-chisel",
                    location = ItemLocationKind.Free,
                    position = Vector3.zero,
                    rotation = Quaternion.identity,
                    massKg = 1.0f,
                    dimensions = new PhysicalDimensions(0.1f, 0.1f, 0.1f)
                });
                var env9i = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                env9i.physicalPayload = JsonUtility.ToJson(physSnapshot9i, false);
                InstallTamperedAuthoritativeCheckpoint(env9i);
                string ptrBefore9i = File.ReadAllText(ptrPath);

                var res9i = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loaded9i);
                Check(res9i.Status == CheckpointLoadStatus.CorruptCheckpoint, "chk9i-tampered-unknown-type-recomputed-hash-corrupt", Diag(res9i));
                Check(res9i.Checkpoint == null, "chk9i-tampered-unknown-type-result-checkpoint-null", Diag(res9i));
                Check(loaded9i == null, "chk9i-tampered-unknown-type-no-fallback", Diag(res9i));
                Check(File.ReadAllText(ptrPath) == ptrBefore9i, "chk9i-tampered-unknown-type-pointer-unchanged");
                Check(foodModel.State.satiety == foodSatietyBefore9d, "chk9i-tampered-unknown-type-food-unmutated");
                Check(itemModel.ItemCount == itemCountBefore9d, "chk9i-tampered-unknown-type-item-unmutated");

                // 9J: Tampered physical dangling container ("nonexistent-phantom-chest") with valid recomputed hashes -> CorruptCheckpoint, no fallback
                var physSnapshot9j = ItemPersistence.CreateSnapshot(itemModel, actorId);
                physSnapshot9j.items.Add(new SavedItemRecord
                {
                    itemId = "orphan-rock-1",
                    itemTypeId = "canyon-stone",
                    location = ItemLocationKind.Stored,
                    containerItemId = "nonexistent-phantom-chest",
                    containerSlot = -1,
                    position = Vector3.zero,
                    rotation = Quaternion.identity,
                    massKg = 2.5f,
                    dimensions = new PhysicalDimensions(0.25f, 0.25f, 0.25f)
                });
                var env9j = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                env9j.physicalPayload = JsonUtility.ToJson(physSnapshot9j, false);
                InstallTamperedAuthoritativeCheckpoint(env9j);
                string ptrBefore9j = File.ReadAllText(ptrPath);

                var res9j = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loaded9j);
                Check(res9j.Status == CheckpointLoadStatus.CorruptCheckpoint, "chk9j-tampered-dangling-container-recomputed-hash-corrupt", Diag(res9j));
                Check(res9j.Checkpoint == null, "chk9j-tampered-dangling-container-result-checkpoint-null", Diag(res9j));
                Check(loaded9j == null, "chk9j-tampered-dangling-container-no-fallback", Diag(res9j));
                Check(File.ReadAllText(ptrPath) == ptrBefore9j, "chk9j-tampered-dangling-container-pointer-unchanged");
                Check(foodModel.State.satiety == foodSatietyBefore9d, "chk9j-tampered-dangling-container-food-unmutated");
                Check(itemModel.ItemCount == itemCountBefore9d, "chk9j-tampered-dangling-container-item-unmutated");

                // 9K: Tampered physical capacity (30 canyon-stones = 75kg > 25kg carry limit) with valid recomputed hashes -> CorruptCheckpoint, no fallback
                var physSnapshot9k = ItemPersistence.CreateSnapshot(itemModel, actorId);
                for (int i = 1; i <= 30; i++)
                {
                    physSnapshot9k.items.Add(new SavedItemRecord
                    {
                        itemId = "overweight-stone-" + i,
                        itemTypeId = "canyon-stone",
                        location = ItemLocationKind.Carried,
                        holderActorId = actorId,
                        containerSlot = -1,
                        position = Vector3.zero,
                        rotation = Quaternion.identity,
                        massKg = 2.5f,
                        dimensions = new PhysicalDimensions(0.25f, 0.25f, 0.25f)
                    });
                }
                var env9k = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                env9k.physicalPayload = JsonUtility.ToJson(physSnapshot9k, false);
                InstallTamperedAuthoritativeCheckpoint(env9k);
                string ptrBefore9k = File.ReadAllText(ptrPath);

                var res9k = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loaded9k);
                Check(res9k.Status == CheckpointLoadStatus.CorruptCheckpoint, "chk9k-tampered-capacity-recomputed-hash-corrupt", Diag(res9k));
                Check(res9k.Checkpoint == null, "chk9k-tampered-capacity-result-checkpoint-null", Diag(res9k));
                Check(loaded9k == null, "chk9k-tampered-capacity-no-fallback", Diag(res9k));
                Check(File.ReadAllText(ptrPath) == ptrBefore9k, "chk9k-tampered-capacity-pointer-unchanged");
                Check(foodModel.State.satiety == foodSatietyBefore9d, "chk9k-tampered-capacity-food-unmutated");
                Check(itemModel.ItemCount == itemCountBefore9d, "chk9k-tampered-capacity-item-unmutated");

                // 9L: Sequence mismatch (envelope seq 99 vs pointer seq 2) diagnostic interpolation safe from NRE
                var env9l = BuildValidEnvelope(99, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                string chkFilename9l = repo.FormatCheckpointFilename(env9l);
                File.WriteAllText(Path.Combine(repoDir, chkFilename9l), FoodOwnershipCheckpointCodec.EncodeEnvelope(env9l, true));

                var ptr9l = new FoodOwnershipPointer
                {
                    schema = FoodOwnershipPointer.CurrentSchema,
                    worldId = env9l.worldId,
                    actorId = env9l.actorId,
                    foodGeneration = env9l.foodGeneration,
                    physicalGeneration = env9l.physicalGeneration,
                    sequence = 2,
                    checkpointFilename = chkFilename9l,
                    checkpointHash = env9l.checkpointHash,
                    previousCheckpointHash = seq1.checkpointHash
                };
                ptr9l.pointerHash = FoodOwnershipCheckpointCodec.ComputeCanonicalPointerHash(ptr9l);
                File.WriteAllText(ptrPath, FoodOwnershipCheckpointCodec.EncodePointer(ptr9l, true));

                var res9l = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loaded9l);
                Check(res9l.Status == CheckpointLoadStatus.CorruptCheckpoint, "chk9l-sequence-mismatch-status-corrupt", Diag(res9l));
                Check(res9l.Checkpoint == null, "chk9l-sequence-mismatch-result-null", Diag(res9l));
                Check(loaded9l == null, "chk9l-sequence-mismatch-out-envelope-null", Diag(res9l));
                Check(res9l.Error == null, "chk9l-sequence-mismatch-no-nre-exception", Diag(res9l));
                Check(res9l.Message.Contains("99") && res9l.Message.Contains("2"), "chk9l-sequence-mismatch-diagnostic-interpolated", Diag(res9l));

                // 9M: Hash mismatch diagnostic interpolation safe from NRE
                var env9m = BuildValidEnvelope(2, seq1.checkpointHash, 2, new List<FoodOwnershipReceiptRecord>());
                string chkFilename9m = repo.FormatCheckpointFilename(env9m);
                File.WriteAllText(Path.Combine(repoDir, chkFilename9m), FoodOwnershipCheckpointCodec.EncodeEnvelope(env9m, true));

                const string fakePointerHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
                var ptr9m = new FoodOwnershipPointer
                {
                    schema = FoodOwnershipPointer.CurrentSchema,
                    worldId = env9m.worldId,
                    actorId = env9m.actorId,
                    foodGeneration = env9m.foodGeneration,
                    physicalGeneration = env9m.physicalGeneration,
                    sequence = 2,
                    checkpointFilename = chkFilename9m,
                    checkpointHash = fakePointerHash,
                    previousCheckpointHash = seq1.checkpointHash
                };
                ptr9m.pointerHash = FoodOwnershipCheckpointCodec.ComputeCanonicalPointerHash(ptr9m);
                File.WriteAllText(ptrPath, FoodOwnershipCheckpointCodec.EncodePointer(ptr9m, true));

                var res9m = repo.LoadAuthoritativeCheckpoint(foodModel, itemModel, out var loaded9m);
                Check(res9m.Status == CheckpointLoadStatus.CorruptCheckpoint, "chk9m-hash-mismatch-status-corrupt", Diag(res9m));
                Check(res9m.Checkpoint == null, "chk9m-hash-mismatch-result-null", Diag(res9m));
                Check(loaded9m == null, "chk9m-hash-mismatch-out-envelope-null", Diag(res9m));
                Check(res9m.Error == null, "chk9m-hash-mismatch-no-nre-exception", Diag(res9m));
                Check(res9m.Message.Contains(env9m.checkpointHash) && res9m.Message.Contains(fakePointerHash), "chk9m-hash-mismatch-diagnostic-interpolated", Diag(res9m));
            }

            return passed;
        }
    }
}
