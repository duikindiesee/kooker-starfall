using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using CityLife.Items;

namespace Starfall.Food
{
    /// <summary>
    /// Codec and validator for versioned food ownership checkpoints.
    /// Defines canonical byte representations, deterministic SHA-256 hashing,
    /// tamper detection, and semantic validation without mutating caller models.
    ///
    /// Preserves exact verbatim payload text to eliminate parse-reserialize drift.
    /// </summary>
    public static class FoodOwnershipCheckpointCodec
    {
        private static readonly Regex Hex64Pattern = new Regex(@"\A[0-9a-f]{64}\z", RegexOptions.Compiled);

        public static string ComputeSha256(string text)
        {
            if (text == null) return "";
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return ComputeSha256(bytes);
        }

        public static string ComputeSha256(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(64);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Computes the canonical deterministic SHA-256 integrity hash over the envelope fields.
        /// Preserves exact payload hashes and strict receipt ordering.
        /// </summary>
        public static string ComputeCanonicalEnvelopeHash(FoodOwnershipCheckpointEnvelope envelope)
        {
            if (envelope == null) return "";

            var sb = new StringBuilder(2048);
            sb.Append(envelope.schema ?? "").Append('\n');
            sb.Append(envelope.worldId ?? "").Append('\n');
            sb.Append(envelope.actorId ?? "").Append('\n');
            sb.Append(envelope.foodGeneration ?? "").Append('\n');
            sb.Append(envelope.physicalGeneration ?? "").Append('\n');
            sb.Append(envelope.sequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(envelope.previousCheckpointHash ?? "").Append('\n');
            sb.Append(envelope.requestHighWatermark.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(envelope.foodPayloadHash ?? "").Append('\n');
            sb.Append(envelope.physicalPayloadHash ?? "").Append('\n');

            if (envelope.receipts != null)
            {
                for (int i = 0; i < envelope.receipts.Count; i++)
                {
                    var r = envelope.receipts[i];
                    if (r == null) continue;
                    sb.Append(r.transactionId.ToString(CultureInfo.InvariantCulture)).Append(':')
                      .Append(r.requestSignature ?? "").Append(':')
                      .Append(r.foodRequestId.ToString(CultureInfo.InvariantCulture)).Append(':')
                      .Append(r.itemRequestId.ToString(CultureInfo.InvariantCulture)).Append(':')
                      .Append(((int)r.status).ToString(CultureInfo.InvariantCulture)).Append(':')
                      .Append(r.statusCode ?? "").Append(':')
                      .Append(r.checkpointSequence.ToString(CultureInfo.InvariantCulture)).Append(':')
                      .Append(r.payloadHash ?? "").Append(';');
                }
            }
            sb.Append('\n');

            return ComputeSha256(sb.ToString());
        }

        /// <summary>
        /// Computes the canonical deterministic SHA-256 integrity hash for an authoritative pointer.
        /// </summary>
        public static string ComputeCanonicalPointerHash(FoodOwnershipPointer pointer)
        {
            if (pointer == null) return "";

            var sb = new StringBuilder(512);
            sb.Append(pointer.schema ?? "").Append('\n');
            sb.Append(pointer.worldId ?? "").Append('\n');
            sb.Append(pointer.actorId ?? "").Append('\n');
            sb.Append(pointer.foodGeneration ?? "").Append('\n');
            sb.Append(pointer.physicalGeneration ?? "").Append('\n');
            sb.Append(pointer.sequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(pointer.checkpointFilename ?? "").Append('\n');
            sb.Append(pointer.checkpointHash ?? "").Append('\n');
            sb.Append(pointer.previousCheckpointHash ?? "").Append('\n');

            return ComputeSha256(sb.ToString());
        }

        /// <summary>
        /// Encodes a checkpoint envelope to formatted JSON. Automatically calculates
        /// foodPayloadHash, physicalPayloadHash, and envelope checkpointHash.
        /// </summary>
        public static string EncodeEnvelope(FoodOwnershipCheckpointEnvelope envelope, bool pretty = true)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            envelope.foodPayloadHash = ComputeSha256(envelope.foodPayload);
            envelope.physicalPayloadHash = ComputeSha256(envelope.physicalPayload);
            envelope.checkpointHash = ComputeCanonicalEnvelopeHash(envelope);

            return JsonUtility.ToJson(envelope, pretty);
        }

        /// <summary>
        /// Attempts to decode and cryptographically verify a checkpoint envelope JSON.
        /// Rejects corrupt schema, byte tampering, and payload/envelope checksum mismatches.
        /// </summary>
        public static bool TryDecodeEnvelope(string json, out FoodOwnershipCheckpointEnvelope envelope, out string errorMessage)
        {
            envelope = null;
            errorMessage = null;

            if (string.IsNullOrEmpty(json))
            {
                errorMessage = "Checkpoint JSON is empty or null.";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(json) > FoodOwnershipCheckpointEnvelope.MaxEnvelopeSizeBytes)
            {
                errorMessage = $"Checkpoint JSON exceeds maximum envelope size of {FoodOwnershipCheckpointEnvelope.MaxEnvelopeSizeBytes} bytes.";
                return false;
            }

            try
            {
                envelope = JsonUtility.FromJson<FoodOwnershipCheckpointEnvelope>(json);
            }
            catch (Exception ex)
            {
                errorMessage = "JSON parsing exception: " + ex.Message;
                envelope = null;
                return false;
            }

            if (envelope == null)
            {
                errorMessage = "JSON deserialized to null envelope.";
                return false;
            }

            if (envelope.schema != FoodOwnershipCheckpointEnvelope.CurrentSchema)
            {
                errorMessage = $"Unsupported envelope schema version: '{envelope.schema}' (expected '{FoodOwnershipCheckpointEnvelope.CurrentSchema}').";
                return false;
            }

            if (string.IsNullOrEmpty(envelope.foodPayload) || string.IsNullOrEmpty(envelope.foodPayloadHash))
            {
                errorMessage = "Missing food payload or food payload hash.";
                return false;
            }

            string computedFoodSha = ComputeSha256(envelope.foodPayload);
            if (!string.Equals(computedFoodSha, envelope.foodPayloadHash, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Food payload hash mismatch: computed {computedFoodSha} vs declared {envelope.foodPayloadHash}.";
                return false;
            }

            if (string.IsNullOrEmpty(envelope.physicalPayload) || string.IsNullOrEmpty(envelope.physicalPayloadHash))
            {
                errorMessage = "Missing physical payload or physical payload hash.";
                return false;
            }

            string computedPhysSha = ComputeSha256(envelope.physicalPayload);
            if (!string.Equals(computedPhysSha, envelope.physicalPayloadHash, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Physical payload hash mismatch: computed {computedPhysSha} vs declared {envelope.physicalPayloadHash}.";
                return false;
            }

            string computedEnvelopeSha = ComputeCanonicalEnvelopeHash(envelope);
            if (!string.Equals(computedEnvelopeSha, envelope.checkpointHash, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Checkpoint envelope integrity hash mismatch: computed {computedEnvelopeSha} vs declared {envelope.checkpointHash}.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Encodes a pointer record to JSON, automatically calculating pointerHash.
        /// </summary>
        public static string EncodePointer(FoodOwnershipPointer pointer, bool pretty = true)
        {
            if (pointer == null) throw new ArgumentNullException(nameof(pointer));
            pointer.pointerHash = ComputeCanonicalPointerHash(pointer);
            return JsonUtility.ToJson(pointer, pretty);
        }

        /// <summary>
        /// Attempts to decode and verify an authoritative pointer record JSON.
        /// </summary>
        public static bool TryDecodePointer(string json, out FoodOwnershipPointer pointer, out string errorMessage)
        {
            pointer = null;
            errorMessage = null;

            if (string.IsNullOrEmpty(json))
            {
                errorMessage = "Pointer JSON is empty or null.";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(json) > FoodOwnershipPointer.MaxPointerSizeBytes)
            {
                errorMessage = $"Pointer JSON exceeds maximum pointer size of {FoodOwnershipPointer.MaxPointerSizeBytes} bytes.";
                return false;
            }

            try
            {
                pointer = JsonUtility.FromJson<FoodOwnershipPointer>(json);
            }
            catch (Exception ex)
            {
                errorMessage = "Pointer JSON deserialization exception: " + ex.Message;
                pointer = null;
                return false;
            }

            if (pointer == null)
            {
                errorMessage = "Pointer JSON deserialized to null.";
                return false;
            }

            if (pointer.schema != FoodOwnershipPointer.CurrentSchema)
            {
                errorMessage = $"Unsupported pointer schema version: '{pointer.schema}' (expected '{FoodOwnershipPointer.CurrentSchema}').";
                return false;
            }

            if (!FoodModel.Id(pointer.worldId) || !FoodModel.Id(pointer.actorId) ||
                !FoodModel.Id(pointer.foodGeneration) || !FoodModel.Id(pointer.physicalGeneration))
            {
                errorMessage = "Pointer contains invalid identifier format.";
                return false;
            }

            if (pointer.sequence < 1)
            {
                errorMessage = $"Invalid pointer sequence: {pointer.sequence} (must be >= 1).";
                return false;
            }

            if (string.IsNullOrEmpty(pointer.checkpointFilename) || !Regex.IsMatch(pointer.checkpointFilename, @"\Achk-[a-zA-Z0-9._-]+-[a-zA-Z0-9._-]+-seq[0-9]{8}-[0-9a-f]{64}\.json\z"))
            {
                errorMessage = $"Invalid or unsafe checkpoint filename in pointer: '{pointer.checkpointFilename}'.";
                return false;
            }

            if (string.IsNullOrEmpty(pointer.checkpointHash) || !Hex64Pattern.IsMatch(pointer.checkpointHash))
            {
                errorMessage = $"Invalid checkpoint hash in pointer: '{pointer.checkpointHash}'.";
                return false;
            }

            string computedPointerHash = ComputeCanonicalPointerHash(pointer);
            if (!string.Equals(computedPointerHash, pointer.pointerHash, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Pointer hash mismatch: computed {computedPointerHash} vs declared {pointer.pointerHash}.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Computes canonical deterministic SHA-256 integrity hash for repository initialization marker.
        /// </summary>
        public static string ComputeCanonicalInitHash(FoodOwnershipRepositoryInit init)
        {
            if (init == null) return "";
            var sb = new StringBuilder(256);
            sb.Append(init.schema ?? "").Append('\n');
            sb.Append(init.worldId ?? "").Append('\n');
            sb.Append(init.actorId ?? "").Append('\n');
            sb.Append(init.foodGeneration ?? "").Append('\n');
            sb.Append(init.physicalGeneration ?? "").Append('\n');
            return ComputeSha256(sb.ToString());
        }

        /// <summary>
        /// Encodes a repository initialization marker to JSON, automatically calculating initHash.
        /// </summary>
        public static string EncodeInit(FoodOwnershipRepositoryInit init, bool pretty = true)
        {
            if (init == null) throw new ArgumentNullException(nameof(init));
            init.initHash = ComputeCanonicalInitHash(init);
            return JsonUtility.ToJson(init, pretty);
        }

        /// <summary>
        /// Attempts to decode and verify a repository initialization marker JSON.
        /// </summary>
        public static bool TryDecodeInit(string json, out FoodOwnershipRepositoryInit init, out string errorMessage)
        {
            init = null;
            errorMessage = null;

            if (string.IsNullOrEmpty(json))
            {
                errorMessage = "Init JSON is empty or null.";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(json) > FoodOwnershipRepositoryInit.MaxInitSizeBytes)
            {
                errorMessage = $"Init JSON exceeds maximum size of {FoodOwnershipRepositoryInit.MaxInitSizeBytes} bytes.";
                return false;
            }

            try
            {
                init = JsonUtility.FromJson<FoodOwnershipRepositoryInit>(json);
            }
            catch (Exception ex)
            {
                errorMessage = "Init JSON deserialization exception: " + ex.Message;
                init = null;
                return false;
            }

            if (init == null)
            {
                errorMessage = "Init JSON deserialized to null.";
                return false;
            }

            if (init.schema != FoodOwnershipRepositoryInit.CurrentSchema)
            {
                errorMessage = $"Unsupported init schema version: '{init.schema}' (expected '{FoodOwnershipRepositoryInit.CurrentSchema}').";
                return false;
            }

            if (!FoodModel.Id(init.worldId) || !FoodModel.Id(init.actorId) ||
                !FoodModel.Id(init.foodGeneration) || !FoodModel.Id(init.physicalGeneration))
            {
                errorMessage = "Init marker contains invalid identifier format.";
                return false;
            }

            string computedHash = ComputeCanonicalInitHash(init);
            if (!string.Equals(computedHash, init.initHash, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Init hash mismatch: computed {computedHash} vs declared {init.initHash}.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Performs structural, schema, range, and ledger validation of a candidate envelope.
        /// Rejects unknown schema, missing/partial payloads, invalid lengths/numbers/IDs,
        /// mixed actor/world, incorrect expected generations, sequence regression,
        /// duplicate/unordered request records, and invalid receipt hashes.
        /// </summary>
        public static bool ValidateEnvelopeStructural(
            FoodOwnershipCheckpointEnvelope envelope,
            string expectedWorld,
            string expectedActor,
            string expectedFoodGen,
            string expectedPhysicalGen,
            out string errorMessage)
        {
            errorMessage = null;

            if (envelope == null)
            {
                errorMessage = "Envelope cannot be null.";
                return false;
            }

            if (envelope.schema != FoodOwnershipCheckpointEnvelope.CurrentSchema)
            {
                errorMessage = $"Envelope schema '{envelope.schema}' is unsupported. Expected '{FoodOwnershipCheckpointEnvelope.CurrentSchema}'.";
                return false;
            }

            // Scope checks
            if (!string.Equals(envelope.worldId, expectedWorld, StringComparison.Ordinal))
            {
                errorMessage = $"World ID mismatch: envelope '{envelope.worldId}' vs expected '{expectedWorld}'.";
                return false;
            }

            if (!string.Equals(envelope.actorId, expectedActor, StringComparison.Ordinal))
            {
                errorMessage = $"Actor ID mismatch: envelope '{envelope.actorId}' vs expected '{expectedActor}'.";
                return false;
            }

            if (!string.Equals(envelope.foodGeneration, expectedFoodGen, StringComparison.Ordinal))
            {
                errorMessage = $"Food generation mismatch: envelope '{envelope.foodGeneration}' vs expected '{expectedFoodGen}'.";
                return false;
            }

            if (!string.Equals(envelope.physicalGeneration, expectedPhysicalGen, StringComparison.Ordinal))
            {
                errorMessage = $"Physical generation mismatch: envelope '{envelope.physicalGeneration}' vs expected '{expectedPhysicalGen}'.";
                return false;
            }

            if (!FoodModel.Id(envelope.worldId) || !FoodModel.Id(envelope.actorId) ||
                !FoodModel.Id(envelope.foodGeneration) || !FoodModel.Id(envelope.physicalGeneration))
            {
                errorMessage = "Envelope contains malformed identifier strings.";
                return false;
            }

            if (envelope.sequence < 1)
            {
                errorMessage = $"Envelope sequence must be >= 1, but was {envelope.sequence}.";
                return false;
            }

            if (envelope.sequence == 1)
            {
                if (!string.IsNullOrEmpty(envelope.previousCheckpointHash))
                {
                    errorMessage = "Initial checkpoint (sequence 1) must have empty previousCheckpointHash.";
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(envelope.previousCheckpointHash) || !Hex64Pattern.IsMatch(envelope.previousCheckpointHash))
                {
                    errorMessage = $"Checkpoint sequence {envelope.sequence} requires valid 64-character hex previousCheckpointHash.";
                    return false;
                }
            }

            if (envelope.requestHighWatermark < 0)
            {
                errorMessage = $"Request high-watermark cannot be negative ({envelope.requestHighWatermark}).";
                return false;
            }

            // Ledger checks
            if (envelope.receipts == null)
            {
                errorMessage = "Envelope receipts list cannot be null.";
                return false;
            }

            if (envelope.receipts.Count > FoodOwnershipCheckpointEnvelope.MaxReceiptLedgerSize)
            {
                errorMessage = $"Receipts ledger overflow: {envelope.receipts.Count} entries exceeds maximum {FoodOwnershipCheckpointEnvelope.MaxReceiptLedgerSize}.";
                return false;
            }

            long previousTransactionId = 0;
            var seenTransactionIds = new HashSet<long>();
            var seenTransactionHashes = new Dictionary<long, string>();

            for (int i = 0; i < envelope.receipts.Count; i++)
            {
                var r = envelope.receipts[i];
                if (r == null)
                {
                    errorMessage = $"Receipt entry at index {i} is null.";
                    return false;
                }

                if (r.transactionId <= 0)
                {
                    errorMessage = $"Receipt at index {i} has non-positive transactionId ({r.transactionId}).";
                    return false;
                }

                // Strict monotonically increasing ordering: rejects duplicate or unordered request records
                if (r.transactionId <= previousTransactionId)
                {
                    errorMessage = $"Receipt ledger is unordered or contains duplicates at index {i}: transactionId {r.transactionId} <= previous {previousTransactionId}.";
                    return false;
                }
                previousTransactionId = r.transactionId;

                if (!seenTransactionIds.Add(r.transactionId))
                {
                    errorMessage = $"Duplicate transaction ID {r.transactionId} in receipt ledger.";
                    return false;
                }

                if (r.transactionId > envelope.requestHighWatermark)
                {
                    errorMessage = $"Receipt transaction ID {r.transactionId} exceeds envelope high-watermark {envelope.requestHighWatermark}.";
                    return false;
                }

                if (r.status != FoodOwnershipReceiptStatus.Committed && r.status != FoodOwnershipReceiptStatus.Rejected)
                {
                    errorMessage = $"Receipt {r.transactionId} has invalid status {r.status}.";
                    return false;
                }

                if (string.IsNullOrEmpty(r.requestSignature) || r.requestSignature.Length > 256)
                {
                    errorMessage = $"Receipt {r.transactionId} has invalid or oversized requestSignature.";
                    return false;
                }

                if (string.IsNullOrEmpty(r.statusCode) || r.statusCode.Length > 256)
                {
                    errorMessage = $"Receipt {r.transactionId} has invalid or oversized statusCode.";
                    return false;
                }

                if (string.IsNullOrEmpty(r.payloadHash) || !Hex64Pattern.IsMatch(r.payloadHash))
                {
                    errorMessage = $"Receipt {r.transactionId} has missing or malformed payloadHash.";
                    return false;
                }

                // Divergent payload hash check for duplicate IDs
                if (seenTransactionHashes.TryGetValue(r.transactionId, out string existingHash))
                {
                    if (!string.Equals(existingHash, r.payloadHash, StringComparison.OrdinalIgnoreCase))
                    {
                        errorMessage = $"Duplicate receipt ID {r.transactionId} with divergent payload hash.";
                        return false;
                    }
                }
                else
                {
                    seenTransactionHashes.Add(r.transactionId, r.payloadHash);
                }

                // Success receipts referring to another checkpoint rejection
                if (r.status == FoodOwnershipReceiptStatus.Committed)
                {
                    if (r.checkpointSequence <= 0 || r.checkpointSequence > envelope.sequence)
                    {
                        errorMessage = $"Committed receipt {r.transactionId} references invalid or future checkpoint sequence {r.checkpointSequence} (envelope sequence is {envelope.sequence}).";
                        return false;
                    }
                }
            }

            // Cryptographic checksum verification
            if (string.IsNullOrEmpty(envelope.foodPayload) || string.IsNullOrEmpty(envelope.foodPayloadHash))
            {
                errorMessage = "Food payload and hash cannot be empty.";
                return false;
            }

            if (!string.Equals(ComputeSha256(envelope.foodPayload), envelope.foodPayloadHash, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Food payload SHA-256 hash mismatch.";
                return false;
            }

            if (string.IsNullOrEmpty(envelope.physicalPayload) || string.IsNullOrEmpty(envelope.physicalPayloadHash))
            {
                errorMessage = "Physical payload and hash cannot be empty.";
                return false;
            }

            if (!string.Equals(ComputeSha256(envelope.physicalPayload), envelope.physicalPayloadHash, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Physical payload SHA-256 hash mismatch.";
                return false;
            }

            if (string.IsNullOrEmpty(envelope.checkpointHash) ||
                !string.Equals(ComputeCanonicalEnvelopeHash(envelope), envelope.checkpointHash, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Canonical envelope SHA-256 integrity hash mismatch.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Performs full semantic validation of the embedded food and physical payloads
        /// against supplied authoritative models without mutating either model.
        ///
        /// Requires BOTH authoritativeFoodModel and authoritativeItemModel; null models
        /// are rejected to prevent unvalidated or weaker admissions into durable storage (Finding 4).
        ///
        /// Verifies matching world, actor, generations, and seed.
        /// Reuses FoodModel.Restore against a detached FoodModel to validate actor, seed,
        /// physiology, ecology, and PlaceLedger.
        ///
        /// Reuses ItemPersistence.TryLoad via a private temporary staging file and
        /// ItemModel.CanRestoreSnapshot to validate live catalog definitions, dimensions,
        /// masses, containment graph, and actor carry limits.
        /// </summary>
        public static bool ValidateEnvelopeSemantic(
            FoodOwnershipCheckpointEnvelope envelope,
            FoodModel authoritativeFoodModel,
            ItemModel authoritativeItemModel,
            string stagingDirectory,
            out string errorMessage)
        {
            errorMessage = null;

            if (envelope == null)
            {
                errorMessage = "Envelope cannot be null.";
                return false;
            }

            // Both models are strictly required (Finding 4)
            if (authoritativeFoodModel == null)
            {
                errorMessage = "Authoritative FoodModel is required for semantic validation; null authority is rejected.";
                return false;
            }

            if (authoritativeItemModel == null)
            {
                errorMessage = "Authoritative ItemModel is required for semantic validation; null authority is rejected.";
                return false;
            }

            // 1. FoodModel semantic validation & identity checks
            if (authoritativeFoodModel.State == null)
            {
                errorMessage = "authoritativeFoodModel.State is null.";
                return false;
            }

            if (!string.Equals(authoritativeFoodModel.State.world, envelope.worldId, StringComparison.Ordinal))
            {
                errorMessage = $"FoodModel world mismatch: model '{authoritativeFoodModel.State.world}' vs envelope '{envelope.worldId}'.";
                return false;
            }

            if (!string.Equals(authoritativeFoodModel.State.generation, envelope.foodGeneration, StringComparison.Ordinal))
            {
                errorMessage = $"FoodModel generation mismatch: model '{authoritativeFoodModel.State.generation}' vs envelope '{envelope.foodGeneration}'.";
                return false;
            }

            if (!string.Equals(authoritativeFoodModel.State.actorId, envelope.actorId, StringComparison.Ordinal))
            {
                errorMessage = $"FoodModel actor mismatch: model '{authoritativeFoodModel.State.actorId}' vs envelope '{envelope.actorId}'.";
                return false;
            }

            // Validate seed, actorId, physiology, and PlaceLedger against a detached model without mutating the caller's model
            var detached = new FoodModel(envelope.worldId, envelope.foodGeneration, authoritativeFoodModel.State.seed);
            detached.State.actorId = authoritativeFoodModel.State.actorId;

            bool restored = detached.Restore(envelope.foodPayload, envelope.worldId, envelope.foodGeneration);
            if (!restored)
            {
                errorMessage = "Food payload failed semantic restoration and validation against detached FoodModel (seed, actor, ecology, or PlaceLedger invalid).";
                return false;
            }

            // 2. Physical item payload semantic validation & identity checks
            if (!string.Equals(authoritativeItemModel.WorldId, envelope.worldId, StringComparison.Ordinal))
            {
                errorMessage = $"ItemModel world mismatch: model '{authoritativeItemModel.WorldId}' vs envelope '{envelope.worldId}'.";
                return false;
            }

            if (!string.Equals(authoritativeItemModel.GenerationId, envelope.physicalGeneration, StringComparison.Ordinal))
            {
                errorMessage = $"ItemModel generation mismatch: model '{authoritativeItemModel.GenerationId}' vs envelope '{envelope.physicalGeneration}'.";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(envelope.physicalPayload) > ItemPersistence.MaxFileSizeBytes)
            {
                errorMessage = $"Physical payload exceeds MaxFileSizeBytes ({ItemPersistence.MaxFileSizeBytes}).";
                return false;
            }

            if (string.IsNullOrEmpty(stagingDirectory))
            {
                errorMessage = "Staging directory required for private staging file validation with ItemPersistence.TryLoad.";
                return false;
            }

            Directory.CreateDirectory(stagingDirectory);
            string stagingFile = Path.Combine(stagingDirectory, "item-stage-" + Guid.NewGuid().ToString("N") + ".tmp");

            string physicalSchema = envelope.physicalPayload != null && envelope.physicalPayload.Contains("\"isManaged\":true")
                ? ItemPersistence.ManagedSchemaVersion
                : ItemPersistence.SchemaVersion;

            var saveEnvelope = new PhysicalSaveEnvelope
            {
                schema = physicalSchema,
                payload = envelope.physicalPayload,
                sha256 = envelope.physicalPayloadHash
            };

            try
            {
                string envelopeJson = JsonUtility.ToJson(saveEnvelope, false);
                using (var fs = new FileStream(stagingFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(envelopeJson);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }

                // Reuses exact public ItemPersistence.TryLoad loader with live authoritative ItemModel
                bool loaded = ItemPersistence.TryLoad(
                    stagingFile,
                    envelope.worldId,
                    envelope.physicalGeneration,
                    envelope.actorId,
                    authoritativeItemModel,
                    out var parsedPayload);

                if (!loaded || parsedPayload == null)
                {
                    errorMessage = "Physical payload failed ItemPersistence.TryLoad admission with authoritative ItemModel.";
                    return false;
                }

                // Reuses exact definition-backed CanRestoreSnapshot validator
                if (!authoritativeItemModel.CanRestoreSnapshot(parsedPayload))
                {
                    errorMessage = "Physical payload failed definition-backed semantic admission via ItemModel.CanRestoreSnapshot.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = "Physical payload staging validation exception: " + ex.Message;
                return false;
            }
            finally
            {
                if (File.Exists(stagingFile))
                {
                    try { File.Delete(stagingFile); } catch { }
                }
            }

            return true;
        }

        /// <summary>
        /// Validates successor candidate ledger against the currently committed authoritative envelope (Finding 5).
        /// Enforces:
        /// 1. Monotonic requestHighWatermark (candidate >= current).
        /// 2. Overlapping retained transaction IDs must preserve identical immutable signatures,
        ///    status, status codes, payload hashes, and checkpoint sequences.
        /// 3. Bounded eviction anti-reissue: an evicted transaction ID (id <= current.requestHighWatermark
        ///    that was not retained in current.receipts) cannot be reissued in candidate.
        /// 4. Newly issued receipts (id > current.requestHighWatermark) must reference candidate.sequence.
        /// </summary>
        public static bool ValidateSuccessorLedger(
            FoodOwnershipCheckpointEnvelope candidate,
            FoodOwnershipCheckpointEnvelope currentAuthoritative,
            out string errorMessage)
        {
            errorMessage = null;

            if (candidate == null)
            {
                errorMessage = "Candidate envelope cannot be null.";
                return false;
            }

            if (currentAuthoritative == null)
            {
                // First checkpoint ever: all committed receipts must reference candidate sequence (1)
                if (candidate.receipts != null)
                {
                    for (int i = 0; i < candidate.receipts.Count; i++)
                    {
                        var r = candidate.receipts[i];
                        if (r != null && r.status == FoodOwnershipReceiptStatus.Committed && r.checkpointSequence != candidate.sequence)
                        {
                            errorMessage = $"Initial checkpoint receipt {r.transactionId} must reference checkpointSequence {candidate.sequence} but was {r.checkpointSequence}.";
                            return false;
                        }
                    }
                }
                return true;
            }

            // Monotonic high-watermark
            if (candidate.requestHighWatermark < currentAuthoritative.requestHighWatermark)
            {
                errorMessage = $"Request high-watermark regressed: candidate ({candidate.requestHighWatermark}) < previous committed ({currentAuthoritative.requestHighWatermark}).";
                return false;
            }

            // Index current retained receipts by transactionId
            var currentReceipts = new Dictionary<long, FoodOwnershipReceiptRecord>();
            if (currentAuthoritative.receipts != null)
            {
                for (int i = 0; i < currentAuthoritative.receipts.Count; i++)
                {
                    var r = currentAuthoritative.receipts[i];
                    if (r != null)
                    {
                        currentReceipts[r.transactionId] = r;
                    }
                }
            }

            if (candidate.receipts != null)
            {
                for (int i = 0; i < candidate.receipts.Count; i++)
                {
                    var candR = candidate.receipts[i];
                    if (candR == null) continue;

                    if (candR.transactionId <= currentAuthoritative.requestHighWatermark)
                    {
                        // Retained receipt: MUST exist in currentAuthoritative.receipts
                        if (!currentReceipts.TryGetValue(candR.transactionId, out var curR))
                        {
                            errorMessage = $"Receipt transaction ID {candR.transactionId} <= previous high-watermark {currentAuthoritative.requestHighWatermark} was not retained in previous checkpoint ledger; reissuing evicted transaction IDs is forbidden.";
                            return false;
                        }

                        // Immutable fields comparison
                        if (!string.Equals(candR.requestSignature, curR.requestSignature, StringComparison.Ordinal))
                        {
                            errorMessage = $"Retained receipt {candR.transactionId} modified immutable requestSignature (expected '{curR.requestSignature}', got '{candR.requestSignature}').";
                            return false;
                        }

                        if (candR.foodRequestId != curR.foodRequestId)
                        {
                            errorMessage = $"Retained receipt {candR.transactionId} modified immutable foodRequestId (expected {curR.foodRequestId}, got {candR.foodRequestId}).";
                            return false;
                        }

                        if (candR.itemRequestId != curR.itemRequestId)
                        {
                            errorMessage = $"Retained receipt {candR.transactionId} modified immutable itemRequestId (expected {curR.itemRequestId}, got {candR.itemRequestId}).";
                            return false;
                        }

                        if (candR.status != curR.status)
                        {
                            errorMessage = $"Retained receipt {candR.transactionId} modified immutable status (expected {curR.status}, got {candR.status}).";
                            return false;
                        }

                        if (!string.Equals(candR.statusCode, curR.statusCode, StringComparison.Ordinal))
                        {
                            errorMessage = $"Retained receipt {candR.transactionId} modified immutable statusCode (expected '{curR.statusCode}', got '{candR.statusCode}').";
                            return false;
                        }

                        if (candR.checkpointSequence != curR.checkpointSequence)
                        {
                            errorMessage = $"Retained receipt {candR.transactionId} modified immutable checkpointSequence (expected {curR.checkpointSequence}, got {candR.checkpointSequence}).";
                            return false;
                        }

                        if (!string.Equals(candR.payloadHash, curR.payloadHash, StringComparison.OrdinalIgnoreCase))
                        {
                            errorMessage = $"Retained receipt {candR.transactionId} modified immutable payloadHash (expected '{curR.payloadHash}', got '{candR.payloadHash}').";
                            return false;
                        }
                    }
                    else
                    {
                        // Newly issued receipt: transactionId > previous requestHighWatermark
                        if (candR.status == FoodOwnershipReceiptStatus.Committed && candR.checkpointSequence != candidate.sequence)
                        {
                            errorMessage = $"New receipt {candR.transactionId} has checkpointSequence {candR.checkpointSequence}, but must match current candidate sequence {candidate.sequence}.";
                            return false;
                        }
                    }
                }
            }

            return true;
        }
    }
}
