using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starfall.Food
{
    /// <summary>
    /// Distinguishable committed vs rejected receipt status for food ownership transactions.
    /// Request records describe already supplied committed or rejected outcomes.
    /// </summary>
    public enum FoodOwnershipReceiptStatus
    {
        None = 0,
        Committed = 1,
        Rejected = 2
    }

    /// <summary>
    /// Authoritative record of an evaluated ownership request outcome.
    /// References bridge-local transaction ID and independent subsystem namespaces (FoodModel and ItemModel).
    /// Prevents repeat issuance across restart and eviction cycles.
    /// </summary>
    [Serializable]
    public sealed class FoodOwnershipReceiptRecord
    {
        public long transactionId;
        public string requestSignature;
        public int foodRequestId;
        public int itemRequestId;
        public FoodOwnershipReceiptStatus status;
        public string statusCode;
        public long checkpointSequence;
        public string payloadHash;

        public FoodOwnershipReceiptRecord Clone()
        {
            return new FoodOwnershipReceiptRecord
            {
                transactionId = this.transactionId,
                requestSignature = this.requestSignature,
                foodRequestId = this.foodRequestId,
                itemRequestId = this.itemRequestId,
                status = this.status,
                statusCode = this.statusCode,
                checkpointSequence = this.checkpointSequence,
                payloadHash = this.payloadHash
            };
        }
    }

    /// <summary>
    /// Versioned checkpoint envelope containing complete food payload, physical payload,
    /// unchanged food-generation and physical-generation identifiers, shared world/actor identity,
    /// monotonic checkpoint sequence, previous checkpoint hash, request high-watermark,
    /// and committed receipt records.
    ///
    /// This is storage infrastructure, not inventory authority or executable transaction behavior.
    /// Preserves exact verbatim payload text to avoid parse-reserialize drift.
    /// </summary>
    [Serializable]
    public sealed class FoodOwnershipCheckpointEnvelope
    {
        public const string CurrentSchema = "starfall.food-ownership-checkpoint.v1";
        public const int MaxEnvelopeSizeBytes = 2 * 1024 * 1024; // 2 MB budget
        public const int MaxReceiptLedgerSize = 256;

        public string schema = CurrentSchema;
        public string worldId;
        public string actorId;
        public string foodGeneration;      // e.g. "combined-v1"
        public string physicalGeneration;  // e.g. "gen-01"
        public long sequence;              // Monotonic sequence starting at 1
        public string previousCheckpointHash = ""; // Empty string for sequence 1, 64-char lowercase hex for sequence > 1
        public long requestHighWatermark;  // Highest bridge transactionId evaluated
        public string foodPayload;         // Verbatim UTF-8 JSON text of FoodState
        public string foodPayloadHash;     // SHA-256 hex of foodPayload
        public string physicalPayload;     // Verbatim UTF-8 JSON text of PhysicalSavePayload
        public string physicalPayloadHash; // SHA-256 hex of physicalPayload
        public List<FoodOwnershipReceiptRecord> receipts = new List<FoodOwnershipReceiptRecord>();
        public string checkpointHash;      // Canonical SHA-256 integrity hash of envelope
    }

    /// <summary>
    /// Authoritative commit pointer record on disk.
    /// Binds expected world, actor, and generation identifiers to the exact immutable checkpoint filename and hash.
    /// </summary>
    [Serializable]
    public sealed class FoodOwnershipPointer
    {
        public const string CurrentSchema = "starfall.food-ownership-pointer.v1";
        public const int MaxPointerSizeBytes = 64 * 1024; // 64 KB budget

        public string schema = CurrentSchema;
        public string worldId;
        public string actorId;
        public string foodGeneration;
        public string physicalGeneration;
        public long sequence;
        public string checkpointFilename;
        public string checkpointHash;
        public string previousCheckpointHash = "";
        public string pointerHash;
    }

    /// <summary>
    /// Commit outcome states. Any failure occurring after pointer commit is reported
    /// as CommittedPostCommitFailure or Indeterminate, never plain NotCommitted.
    /// </summary>
    public enum CheckpointCommitStatus
    {
        Committed,
        NotCommitted,
        CommittedPostCommitFailure,
        Indeterminate,
        ConcurrencyConflict,
        ValidationFailed,
        CorruptState
    }

    /// <summary>
    /// Result of a checkpoint commit operation.
    /// </summary>
    public sealed class CheckpointCommitResult
    {
        public CheckpointCommitStatus Status;
        public string Message;
        public long CommittedSequence;
        public string CheckpointHash;
        public Exception Error;

        public override string ToString() => $"[CheckpointCommitResult Status={Status} Seq={CommittedSequence} Hash={CheckpointHash} Message={Message}]";
    }

    /// <summary>
    /// Result status of reading the authoritative checkpoint.
    /// Corruption of acknowledged newest checkpoint fails closed; never falls back to an older checkpoint.
    /// </summary>
    public enum CheckpointLoadStatus
    {
        Success,
        EmptyRepository,
        AmbiguousArtifactsWithoutPointer,
        CorruptPointer,
        MissingCheckpoint,
        CorruptCheckpoint,
        ScopeMismatch,
        Uninitialized,
        MissingAuthority
    }

    /// <summary>
    /// Result of loading the authoritative checkpoint.
    /// </summary>
    public sealed class CheckpointLoadResult
    {
        public CheckpointLoadStatus Status;
        public string Message;
        public FoodOwnershipCheckpointEnvelope Checkpoint;
        public FoodOwnershipPointer Pointer;
        public Exception Error;

        public override string ToString() => $"[CheckpointLoadResult Status={Status} Message={Message}]";
    }

    /// <summary>
    /// Authoritative repository initialization marker persisted on disk.
    /// Binds expected world, actor, and generation identifiers to the repository.
    /// </summary>
    [Serializable]
    public sealed class FoodOwnershipRepositoryInit
    {
        public const string CurrentSchema = "starfall.food-ownership-repository-init.v1";
        public const int MaxInitSizeBytes = 16 * 1024; // 16 KB budget

        public string schema = CurrentSchema;
        public string worldId;
        public string actorId;
        public string foodGeneration;
        public string physicalGeneration;
        public string initHash;
    }

    /// <summary>
    /// Fault injection test hook points for durability, crash recovery, and restart verification.
    /// </summary>
    public enum CheckpointFaultInjectionPoint
    {
        None = 0,
        FailBeforeWrite,
        FailDuringPartialCheckpointWrite,
        FailAfterCheckpointFlush,
        FailBeforePointerPublication,
        FailDuringPointerPublicationReplacement,
        FailDuringPointerPublicationReadback,
        FailAfterPointerPublication,
        FailDuringAcknowledgementCleanup
    }
}
