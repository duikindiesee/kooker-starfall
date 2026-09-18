using System;
using System.IO;
using System.Text;
using UnityEngine;
using CityLife.Items;

namespace Starfall.Food
{
    /// <summary>
    /// Durable single-writer checkpoint repository with OS-exclusive locking covering
    /// pointer read, expected-sequence comparison, immutable candidate validation,
    /// and atomic pointer publication.
    ///
    /// Recovery Contract:
    /// 1. Immutable generation-specific checkpoint files published without overwriting different bytes.
    /// 2. Atomic pointer publication is the single commit point.
    /// 3. Failures before pointer publication are NotCommitted.
    /// 4. Failures after pointer publication are CommittedPostCommitFailure or Indeterminate, never plain NotCommitted.
    /// 5. Corruption of the newest acknowledged checkpoint or pointer fails closed; never falls back to older checkpoints.
    /// 6. Non-empty repository without authoritative pointer fails closed as AmbiguousArtifactsWithoutPointer;
    ///    never silently initializes an empty world.
    /// </summary>
    public sealed class FoodOwnershipCheckpointRepository
    {
        public const string PointerFileName = "authoritative.pointer";
        public const string BackupPointerFileName = "authoritative.pointer.bak";
        public const string InitFileName = "repository.init";
        public const string LockFileName = ".commit.lock";
        public const string StagingSubdir = ".staging";

        private readonly string _repositoryDirectory;
        private readonly string _expectedWorld;
        private readonly string _expectedActor;
        private readonly string _expectedFoodGen;
        private readonly string _expectedPhysGen;

        public string RepositoryDirectory => _repositoryDirectory;
        public string ExpectedWorld => _expectedWorld;
        public string ExpectedActor => _expectedActor;
        public string ExpectedFoodGeneration => _expectedFoodGen;
        public string ExpectedPhysicalGeneration => _expectedPhysGen;

        /// <summary>
        /// Test-only fault injection hook. Used by verification checks to simulate crashes
        /// across the commit pipeline.
        /// </summary>
        public CheckpointFaultInjectionPoint FaultInjection { get; set; } = CheckpointFaultInjectionPoint.None;

        /// <summary>
        /// Test-only hook invoked immediately prior to readback verification during pointer publication reconciliation.
        /// Enables tests to inject real filesystem state (such as exclusive locks or unreadable file permissions).
        /// </summary>
        public Action TestHookBeforePointerReadback { get; set; }

        public FoodOwnershipCheckpointRepository(
            string repositoryDirectory,
            string expectedWorld,
            string expectedActor,
            string expectedFoodGen,
            string expectedPhysGen)
        {
            if (string.IsNullOrEmpty(repositoryDirectory))
                throw new ArgumentNullException(nameof(repositoryDirectory));
            if (!FoodModel.Id(expectedWorld))
                throw new ArgumentException("Invalid expectedWorld", nameof(expectedWorld));
            if (!FoodModel.Id(expectedActor))
                throw new ArgumentException("Invalid expectedActor", nameof(expectedActor));
            if (!FoodModel.Id(expectedFoodGen))
                throw new ArgumentException("Invalid expectedFoodGen", nameof(expectedFoodGen));
            if (!FoodModel.Id(expectedPhysGen))
                throw new ArgumentException("Invalid expectedPhysGen", nameof(expectedPhysGen));

            _repositoryDirectory = Path.GetFullPath(repositoryDirectory);
            _expectedWorld = expectedWorld;
            _expectedActor = expectedActor;
            _expectedFoodGen = expectedFoodGen;
            _expectedPhysGen = expectedPhysGen;
        }

        public string FormatCheckpointFilename(FoodOwnershipCheckpointEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            return $"chk-{envelope.foodGeneration}-{envelope.physicalGeneration}-seq{envelope.sequence:D8}-{envelope.checkpointHash}.json";
        }

        /// <summary>
        /// Explicit initialization for a first-ever empty repository under OS-exclusive lock (Finding 6).
        /// Fails closed if any checkpoint files, pointers, backups, staging, or unknown artifacts already exist.
        /// Persists explicit initialization scope marker bound to expected world, actor, and generation identifiers.
        /// </summary>
        public bool InitializeEmpty(out string error)
        {
            error = null;
            FileStream lockStream = null;
            try
            {
                Directory.CreateDirectory(_repositoryDirectory);
                try
                {
                    lockStream = AcquireExclusiveLock();
                }
                catch (IOException ex)
                {
                    error = "Exclusive OS lock acquisition failed: concurrent writer or initialization active: " + ex.Message;
                    return false;
                }

                string initPath = Path.Combine(_repositoryDirectory, InitFileName);
                string pointerPath = Path.Combine(_repositoryDirectory, PointerFileName);

                if (File.Exists(pointerPath))
                {
                    error = "Repository already contains an authoritative pointer.";
                    return false;
                }

                if (File.Exists(initPath))
                {
                    string initContent = File.ReadAllText(initPath);
                    if (FoodOwnershipCheckpointCodec.TryDecodeInit(initContent, out var existingInit, out _))
                    {
                        if (existingInit.worldId != _expectedWorld ||
                            existingInit.actorId != _expectedActor ||
                            existingInit.foodGeneration != _expectedFoodGen ||
                            existingInit.physicalGeneration != _expectedPhysGen)
                        {
                            error = $"Cross-scope initialization conflict: repository already initialized for {existingInit.worldId}/{existingInit.actorId}/{existingInit.foodGeneration}/{existingInit.physicalGeneration} but requested {_expectedWorld}/{_expectedActor}/{_expectedFoodGen}/{_expectedPhysGen}.";
                            return false;
                        }
                        error = "Repository is already initialized for this scope.";
                        return false;
                    }
                    else
                    {
                        error = "Cannot initialize repository: corrupt initialization marker exists.";
                        return false;
                    }
                }

                // Check all filesystem entries in _repositoryDirectory.
                // Allowed entries before writing repository.init: ONLY LockFileName (".commit.lock").
                var entries = Directory.GetFileSystemEntries(_repositoryDirectory);
                for (int i = 0; i < entries.Length; i++)
                {
                    string name = Path.GetFileName(entries[i]);
                    if (string.Equals(name, LockFileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.Equals(name, BackupPointerFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Cannot initialize empty repository: backup pointer artifact exists.";
                        return false;
                    }
                    if (string.Equals(name, StagingSubdir, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Cannot initialize empty repository: staging artifacts exist.";
                        return false;
                    }
                    if (name.StartsWith("chk-", StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Cannot initialize empty repository: checkpoint artifacts exist.";
                        return false;
                    }

                    error = $"Cannot initialize empty repository: non-empty directory contains artifact '{name}'.";
                    return false;
                }

                // Write repository.init under lock
                var initObj = new FoodOwnershipRepositoryInit
                {
                    schema = FoodOwnershipRepositoryInit.CurrentSchema,
                    worldId = _expectedWorld,
                    actorId = _expectedActor,
                    foodGeneration = _expectedFoodGen,
                    physicalGeneration = _expectedPhysGen
                };
                string initJson = FoodOwnershipCheckpointCodec.EncodeInit(initObj, true);
                string tmpInitPath = initPath + ".tmp-" + Guid.NewGuid().ToString("N");
                using (var fs = new FileStream(tmpInitPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(initJson);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
                File.Move(tmpInitPath, initPath);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (lockStream != null)
                {
                    lockStream.Dispose();
                }
            }
        }

        /// <summary>
        /// Acquires an OS-exclusive file lock on the repository lock file.
        /// Throws IOException if another process or thread holds the lock.
        /// </summary>
        private FileStream AcquireExclusiveLock()
        {
            Directory.CreateDirectory(_repositoryDirectory);
            string lockPath = Path.Combine(_repositoryDirectory, LockFileName);
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        /// <summary>
        /// Reads and decodes the current authoritative pointer on disk.
        /// Metadata diagnostic only; not restore admission.
        /// </summary>
        public CheckpointLoadResult GetAuthoritativePointer()
        {
            return ReadAuthoritativePointerInternal();
        }

        private CheckpointLoadResult ReadAuthoritativePointerInternal()
        {
            if (!Directory.Exists(_repositoryDirectory))
            {
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.Uninitialized,
                    Message = "Repository directory does not exist; uninitialized."
                };
            }

            string initPath = Path.Combine(_repositoryDirectory, InitFileName);
            if (!File.Exists(initPath))
            {
                var allEntries = Directory.GetFileSystemEntries(_repositoryDirectory);
                int nonLockCount = 0;
                for (int i = 0; i < allEntries.Length; i++)
                {
                    if (!string.Equals(Path.GetFileName(allEntries[i]), LockFileName, StringComparison.OrdinalIgnoreCase))
                        nonLockCount++;
                }

                if (nonLockCount > 0)
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.AmbiguousArtifactsWithoutPointer,
                        Message = "Non-empty repository without initialization marker or pointer. Explicit recovery required."
                    };
                }

                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.Uninitialized,
                    Message = "Repository directory is uninitialized. Explicit initialization required."
                };
            }

            try
            {
                string initJson = File.ReadAllText(initPath);
                if (!FoodOwnershipCheckpointCodec.TryDecodeInit(initJson, out var initObj, out var initErr))
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.CorruptPointer,
                        Message = "Corrupt repository initialization marker: " + initErr
                    };
                }

                if (initObj.worldId != _expectedWorld ||
                    initObj.actorId != _expectedActor ||
                    initObj.foodGeneration != _expectedFoodGen ||
                    initObj.physicalGeneration != _expectedPhysGen)
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.ScopeMismatch,
                        Message = $"Repository initialization scope mismatch: expected {_expectedWorld}/{_expectedActor}/{_expectedFoodGen}/{_expectedPhysGen} but got {initObj.worldId}/{initObj.actorId}/{initObj.foodGeneration}/{initObj.physicalGeneration}."
                    };
                }
            }
            catch (Exception ex)
            {
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.CorruptPointer,
                    Message = "Failed reading repository initialization marker: " + ex.Message,
                    Error = ex
                };
            }

            string pointerPath = Path.Combine(_repositoryDirectory, PointerFileName);
            if (!File.Exists(pointerPath))
            {
                // Inspect all entries in directory; allowed in empty initialized repo: ONLY LockFileName and InitFileName
                var entries = Directory.GetFileSystemEntries(_repositoryDirectory);
                int artifactCount = 0;
                for (int i = 0; i < entries.Length; i++)
                {
                    string name = Path.GetFileName(entries[i]);
                    if (string.Equals(name, LockFileName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, InitFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    artifactCount++;
                }

                if (artifactCount > 0)
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.AmbiguousArtifactsWithoutPointer,
                        Message = "Ambiguous repository: artifacts exist without authoritative pointer. Explicit recovery required."
                    };
                }

                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.EmptyRepository,
                    Message = "Repository is initialized and empty; no authoritative pointer exists."
                };
            }

            try
            {
                var fi = new FileInfo(pointerPath);
                if (fi.Length > FoodOwnershipPointer.MaxPointerSizeBytes)
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.CorruptPointer,
                        Message = $"Authoritative pointer file exceeds maximum allowed size ({fi.Length} bytes)."
                    };
                }

                string json = File.ReadAllText(pointerPath);
                if (!FoodOwnershipCheckpointCodec.TryDecodePointer(json, out var pointer, out var decodeErr))
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.CorruptPointer,
                        Message = "Authoritative pointer decode failed: " + decodeErr
                    };
                }

                if (pointer.worldId != _expectedWorld ||
                    pointer.actorId != _expectedActor ||
                    pointer.foodGeneration != _expectedFoodGen ||
                    pointer.physicalGeneration != _expectedPhysGen)
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.ScopeMismatch,
                        Message = $"Pointer scope mismatch: expected {_expectedWorld}/{_expectedActor}/{_expectedFoodGen}/{_expectedPhysGen} but got {pointer.worldId}/{pointer.actorId}/{pointer.foodGeneration}/{pointer.physicalGeneration}."
                    };
                }

                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.Success,
                    Pointer = pointer
                };
            }
            catch (Exception ex)
            {
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.CorruptPointer,
                    Message = "Failed reading authoritative pointer: " + ex.Message,
                    Error = ex
                };
            }
        }

        /// <summary>
        /// Atomically commits a new checkpoint candidate to the repository.
        ///
        /// Holds an OS-exclusive lock (FileStream FileShare.None) covering pointer read,
        /// expected-sequence comparison, immutable candidate validation, candidate flush,
        /// and atomic pointer publication.
        /// </summary>
        public CheckpointCommitResult Commit(
            FoodOwnershipCheckpointEnvelope candidate,
            FoodModel authoritativeFoodModel,
            ItemModel authoritativeItemModel)
        {
            if (candidate == null)
            {
                return new CheckpointCommitResult
                {
                    Status = CheckpointCommitStatus.ValidationFailed,
                    Message = "Candidate envelope cannot be null."
                };
            }

            // Finding 4: Require BOTH authoritative models
            if (authoritativeFoodModel == null)
            {
                return new CheckpointCommitResult
                {
                    Status = CheckpointCommitStatus.ValidationFailed,
                    Message = "authoritativeFoodModel is required; cannot commit with missing food authority."
                };
            }

            if (authoritativeItemModel == null)
            {
                return new CheckpointCommitResult
                {
                    Status = CheckpointCommitStatus.ValidationFailed,
                    Message = "authoritativeItemModel is required; cannot commit with missing item authority."
                };
            }

            FileStream lockStream = null;
            try
            {
                try
                {
                    lockStream = AcquireExclusiveLock();
                }
                catch (IOException ex)
                {
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.ConcurrencyConflict,
                        Message = "Exclusive OS lock acquisition failed: concurrent writer active.",
                        Error = ex
                    };
                }

                if (FaultInjection == CheckpointFaultInjectionPoint.FailBeforeWrite)
                {
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.NotCommitted,
                        Message = "Fault injection: FailBeforeWrite triggered."
                    };
                }

                // 1. Read pointer under lock
                var pointerResult = ReadAuthoritativePointerInternal();
                FoodOwnershipPointer currentPointer = null;
                FoodOwnershipCheckpointEnvelope currentEnvelope = null;
                long currentSequence = 0;
                string currentHash = "";

                if (pointerResult.Status == CheckpointLoadStatus.EmptyRepository)
                {
                    // This is the only valid sequence 0 case
                    currentSequence = 0;
                    currentHash = "";
                    currentPointer = null;
                }
                else if (pointerResult.Status == CheckpointLoadStatus.Success)
                {
                    currentPointer = pointerResult.Pointer;
                    currentSequence = currentPointer.sequence;
                    currentHash = currentPointer.checkpointHash;
                }
                else
                {
                    // Finding 2: Reject ScopeMismatch and every unexpected pointer load result before deriving sequence 0
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.CorruptState,
                        Message = $"Cannot commit: repository pointer load returned {pointerResult.Status}: {pointerResult.Message}",
                        Error = pointerResult.Error
                    };
                }

                string stagingDir = Path.Combine(_repositoryDirectory, StagingSubdir);
                Directory.CreateDirectory(stagingDir);

                // Finding 3: Validate CURRENT complete authoritative pair under SAME OS-exclusive lock before successor admission
                if (currentSequence > 0)
                {
                    var curLoadResult = LoadAuthoritativeCheckpointUnderLock(authoritativeFoodModel, authoritativeItemModel, out currentEnvelope, currentPointer);
                    if (curLoadResult.Status != CheckpointLoadStatus.Success)
                    {
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.CorruptState,
                            Message = $"Current authoritative checkpoint validation failed ({curLoadResult.Status}): {curLoadResult.Message}. Fail closed.",
                            Error = curLoadResult.Error
                        };
                    }
                }

                // 2. Expected-sequence comparison & stale sequence check
                if (candidate.sequence != currentSequence + 1)
                {
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.ConcurrencyConflict,
                        Message = $"Sequence conflict: expected candidate sequence {currentSequence + 1} but got {candidate.sequence} (current sequence is {currentSequence})."
                    };
                }

                if (currentSequence == 0)
                {
                    if (!string.IsNullOrEmpty(candidate.previousCheckpointHash))
                    {
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.ValidationFailed,
                            Message = "First checkpoint (sequence 1) must have empty previousCheckpointHash."
                        };
                    }
                }
                else
                {
                    if (!string.Equals(candidate.previousCheckpointHash, currentHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.ConcurrencyConflict,
                            Message = $"Previous checkpoint hash mismatch: expected {currentHash} but got {candidate.previousCheckpointHash}."
                        };
                    }
                }

                // Finding 5: Monotonic watermark & successor ledger validation
                if (!FoodOwnershipCheckpointCodec.ValidateSuccessorLedger(candidate, currentEnvelope, out string successorErr))
                {
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.ValidationFailed,
                        Message = "Candidate successor ledger validation failed: " + successorErr
                    };
                }

                // 3. Immutable candidate validation under lock
                if (!FoodOwnershipCheckpointCodec.ValidateEnvelopeStructural(
                    candidate,
                    _expectedWorld,
                    _expectedActor,
                    _expectedFoodGen,
                    _expectedPhysGen,
                    out string structErr))
                {
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.ValidationFailed,
                        Message = "Candidate structural validation failed: " + structErr
                    };
                }

                if (!FoodOwnershipCheckpointCodec.ValidateEnvelopeSemantic(
                    candidate,
                    authoritativeFoodModel,
                    authoritativeItemModel,
                    stagingDir,
                    out string semErr))
                {
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.ValidationFailed,
                        Message = "Candidate semantic validation failed: " + semErr
                    };
                }

                // 4. Checkpoint file publication
                string chkFileName = FormatCheckpointFilename(candidate);
                string chkFilePath = Path.Combine(_repositoryDirectory, chkFileName);
                string candidateJson = FoodOwnershipCheckpointCodec.EncodeEnvelope(candidate, true);

                if (File.Exists(chkFilePath))
                {
                    string existingContent = File.ReadAllText(chkFilePath);
                    if (!string.Equals(existingContent, candidateJson, StringComparison.Ordinal))
                    {
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.ConcurrencyConflict,
                            Message = $"Immutable checkpoint collision: file {chkFileName} already exists with divergent content."
                        };
                    }
                }
                else
                {
                    string tmpChkPath = chkFilePath + ".tmp-" + Guid.NewGuid().ToString("N");

                    if (FaultInjection == CheckpointFaultInjectionPoint.FailDuringPartialCheckpointWrite)
                    {
                        using (var fs = new FileStream(tmpChkPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            byte[] partialBytes = Encoding.UTF8.GetBytes("{\"schema\":\"truncated");
                            fs.Write(partialBytes, 0, partialBytes.Length);
                            fs.Flush(true);
                        }
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.NotCommitted,
                            Message = "Fault injection: FailDuringPartialCheckpointWrite triggered."
                        };
                    }

                    using (var fs = new FileStream(tmpChkPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(candidateJson);
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Flush(true); // Durably fsync
                    }

                    // Read back and validate bytes
                    string readBack = File.ReadAllText(tmpChkPath);
                    if (!string.Equals(readBack, candidateJson, StringComparison.Ordinal))
                    {
                        try { File.Delete(tmpChkPath); } catch { }
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.NotCommitted,
                            Message = "Read-back validation failed for written checkpoint file."
                        };
                    }

                    File.Move(tmpChkPath, chkFilePath);
                }

                if (FaultInjection == CheckpointFaultInjectionPoint.FailAfterCheckpointFlush)
                {
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.NotCommitted,
                        Message = "Fault injection: FailAfterCheckpointFlush triggered. Candidate written but pointer uncommitted."
                    };
                }

                // 5. Prepare temporary pointer
                var newPointer = new FoodOwnershipPointer
                {
                    schema = FoodOwnershipPointer.CurrentSchema,
                    worldId = candidate.worldId,
                    actorId = candidate.actorId,
                    foodGeneration = candidate.foodGeneration,
                    physicalGeneration = candidate.physicalGeneration,
                    sequence = candidate.sequence,
                    checkpointFilename = chkFileName,
                    checkpointHash = candidate.checkpointHash,
                    previousCheckpointHash = candidate.previousCheckpointHash
                };
                newPointer.pointerHash = FoodOwnershipCheckpointCodec.ComputeCanonicalPointerHash(newPointer);
                string pointerJson = FoodOwnershipCheckpointCodec.EncodePointer(newPointer, true);

                string pointerPath = Path.Combine(_repositoryDirectory, PointerFileName);
                string tmpPointerPath = pointerPath + ".tmp-" + Guid.NewGuid().ToString("N");

                using (var fs = new FileStream(tmpPointerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(pointerJson);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true); // Durably fsync
                }

                if (FaultInjection == CheckpointFaultInjectionPoint.FailBeforePointerPublication)
                {
                    try { File.Delete(tmpPointerPath); } catch { }
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.NotCommitted,
                        Message = "Fault injection: FailBeforePointerPublication triggered."
                    };
                }

                // 6. ATOMIC POINTER COMMIT POINT
                // Before this execution: failure is NotCommitted.
                // At this execution: pointer is replaced atomically on disk.
                bool pointerExisted = File.Exists(pointerPath);
                string previousPointerText = pointerExisted ? File.ReadAllText(pointerPath) : null;
                bool commitSucceeded = false;
                Exception replacementException = null;

                try
                {
                    if (FaultInjection == CheckpointFaultInjectionPoint.FailDuringPointerPublicationReplacement)
                    {
                        throw new IOException("Fault injection: FailDuringPointerPublicationReplacement triggered.");
                    }

                    if (pointerExisted)
                    {
                        string backupPath = Path.Combine(_repositoryDirectory, BackupPointerFileName);
                        File.Replace(tmpPointerPath, pointerPath, backupPath);
                    }
                    else
                    {
                        File.Move(tmpPointerPath, pointerPath);
                    }
                    commitSucceeded = true;
                }
                catch (Exception ex)
                {
                    replacementException = ex;
                }

                if (replacementException != null)
                {
                    string readbackText = null;
                    bool readbackReadSuccessfully = false;

                    try
                    {
                        TestHookBeforePointerReadback?.Invoke();

                        if (FaultInjection == CheckpointFaultInjectionPoint.FailDuringPointerPublicationReadback)
                        {
                            throw new IOException("Fault injection: FailDuringPointerPublicationReadback triggered.");
                        }

                        if (File.Exists(pointerPath))
                        {
                            readbackText = File.ReadAllText(pointerPath);
                            readbackReadSuccessfully = true;
                        }
                    }
                    catch (Exception rbEx)
                    {
                        // Finding 1: Do not swallow unknown outcome into failure; return Indeterminate/recovery-required
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.Indeterminate,
                            Message = "Pointer replacement encountered exception and readback failed (unreadable / IO fault); outcome is indeterminate (recovery required): " + rbEx.Message,
                            Error = replacementException
                        };
                    }

                    if (!readbackReadSuccessfully)
                    {
                        // Pass 3: After publication exception, missing readback pointer MUST ALWAYS mean Indeterminate/recovery-required,
                        // including first publication when pointerExisted=false. File.Exists absence is not proof of unchanged authority and can hide IO failure.
                        // NotCommitted is strictly reserved for verified identical OLD authoritative pointer bytes.
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.Indeterminate,
                            Message = "Authoritative pointer missing after publication exception; outcome is indeterminate (recovery required): " + replacementException.Message,
                            Error = replacementException
                        };
                    }

                    // Pointer file exists and was read successfully under lock
                    if (string.Equals(readbackText, pointerJson, StringComparison.Ordinal))
                    {
                        // Readback matching candidate => committed-postcommit-failure
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.CommittedPostCommitFailure,
                            Message = "Pointer committed on disk, but exception encountered during replacement: " + replacementException.Message,
                            CommittedSequence = candidate.sequence,
                            CheckpointHash = candidate.checkpointHash,
                            Error = replacementException
                        };
                    }
                    else if (pointerExisted && string.Equals(readbackText, previousPointerText, StringComparison.Ordinal))
                    {
                        // NotCommitted ONLY when authoritative previous pointer bytes/state demonstrably unchanged under lock
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.NotCommitted,
                            Message = "Atomic pointer replacement failed; previous authoritative pointer demonstrably unchanged: " + replacementException.Message,
                            Error = replacementException
                        };
                    }
                    else
                    {
                        // Unclassifiable or corrupt pointer state
                        return new CheckpointCommitResult
                        {
                            Status = CheckpointCommitStatus.Indeterminate,
                            Message = "Pointer state after replacement exception is unclassifiable; outcome is indeterminate (recovery required).",
                            Error = replacementException
                        };
                    }
                }

                if (FaultInjection == CheckpointFaultInjectionPoint.FailDuringPointerPublicationReadback)
                {
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.Indeterminate,
                        Message = "Fault injection: FailDuringPointerPublicationReadback triggered; outcome is indeterminate (recovery required).",
                        CommittedSequence = candidate.sequence,
                        CheckpointHash = candidate.checkpointHash
                    };
                }

                // Any failure after this point MUST NOT be reported plain NotCommitted
                if (FaultInjection == CheckpointFaultInjectionPoint.FailAfterPointerPublication)
                {
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.CommittedPostCommitFailure,
                        Message = "Fault injection: FailAfterPointerPublication triggered. Checkpoint is committed on disk.",
                        CommittedSequence = candidate.sequence,
                        CheckpointHash = candidate.checkpointHash
                    };
                }

                // 7. Acknowledgement & Staging Cleanup
                try
                {
                    if (FaultInjection == CheckpointFaultInjectionPoint.FailDuringAcknowledgementCleanup)
                    {
                        throw new IOException("Fault injection: FailDuringAcknowledgementCleanup triggered.");
                    }

                    if (Directory.Exists(stagingDir))
                    {
                        string[] tmpFiles = Directory.GetFiles(stagingDir, "*.tmp");
                        for (int i = 0; i < tmpFiles.Length; i++)
                        {
                            try { File.Delete(tmpFiles[i]); } catch { }
                        }
                        if (Directory.GetFileSystemEntries(stagingDir).Length == 0)
                        {
                            try { Directory.Delete(stagingDir, false); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    return new CheckpointCommitResult
                    {
                        Status = CheckpointCommitStatus.CommittedPostCommitFailure,
                        Message = "Checkpoint committed, but post-commit acknowledgement/cleanup failed: " + ex.Message,
                        CommittedSequence = candidate.sequence,
                        CheckpointHash = candidate.checkpointHash,
                        Error = ex
                    };
                }

                return new CheckpointCommitResult
                {
                    Status = CheckpointCommitStatus.Committed,
                    Message = "Checkpoint committed successfully.",
                    CommittedSequence = candidate.sequence,
                    CheckpointHash = candidate.checkpointHash
                };
            }
            finally
            {
                if (lockStream != null)
                {
                    lockStream.Dispose();
                }
            }
        }

        /// <summary>
        /// Reads and restores the newest authoritative checkpoint under OS-exclusive lock,
        /// validating structural integrity and semantic consistency against BOTH authoritative models.
        ///
        /// FAILS CLOSED:
        /// - Both authoritativeFoodModel and authoritativeItemModel are strictly required.
        /// - OS-exclusive lock is held across reading init, pointer, checkpoint file, and performing structural AND semantic validation.
        /// - If authoritative pointer is corrupt or missing: returns CorruptPointer or AmbiguousArtifactsWithoutPointer.
        /// - If newest checkpoint file is corrupt, missing, or fails structural or semantic validation: returns CorruptCheckpoint or MissingCheckpoint.
        /// - NEVER falls back to an older checkpoint, which could resurrect consumed food or duplicated items.
        /// - On EVERY failure, envelope and result.Checkpoint MUST be null.
        /// - Models are never mutated.
        /// </summary>
        public CheckpointLoadResult LoadAuthoritativeCheckpoint(
            FoodModel authoritativeFoodModel,
            ItemModel authoritativeItemModel,
            out FoodOwnershipCheckpointEnvelope envelope)
        {
            envelope = null;

            if (authoritativeFoodModel == null)
            {
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.MissingAuthority,
                    Message = "authoritativeFoodModel is required; cannot load without food authority."
                };
            }

            if (authoritativeItemModel == null)
            {
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.MissingAuthority,
                    Message = "authoritativeItemModel is required; cannot load without item authority."
                };
            }

            FileStream lockStream = null;
            try
            {
                try
                {
                    lockStream = AcquireExclusiveLock();
                }
                catch (IOException ex)
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.CorruptCheckpoint,
                        Message = "Exclusive OS lock acquisition failed: concurrent writer or load active: " + ex.Message,
                        Error = ex
                    };
                }

                return LoadAuthoritativeCheckpointUnderLock(authoritativeFoodModel, authoritativeItemModel, out envelope);
            }
            finally
            {
                if (lockStream != null)
                {
                    lockStream.Dispose();
                }
            }
        }

        /// <summary>
        /// Deprecated / fail-closed legacy overload without authoritative models.
        /// Fails closed with diagnostic; never returns Success or envelope payload to prevent unvalidated admission.
        /// Callers must use the dual-authority overload LoadAuthoritativeCheckpoint(FoodModel, ItemModel, out envelope).
        /// </summary>
        public CheckpointLoadResult LoadAuthoritativeCheckpoint(out FoodOwnershipCheckpointEnvelope envelope)
        {
            envelope = null;
            return new CheckpointLoadResult
            {
                Status = CheckpointLoadStatus.MissingAuthority,
                Message = "Authoritative FoodModel and ItemModel are required for checkpoint load; no-authority overload is disabled fail-closed to prevent unvalidated admission."
            };
        }

        /// <summary>
        /// Internal restore and validation helper executed under OS-exclusive lock.
        /// Reused by both LoadAuthoritativeCheckpoint and Commit to prevent duplicate lock acquisition or deadlock.
        /// </summary>
        private CheckpointLoadResult LoadAuthoritativeCheckpointUnderLock(
            FoodModel authoritativeFoodModel,
            ItemModel authoritativeItemModel,
            out FoodOwnershipCheckpointEnvelope envelope,
            FoodOwnershipPointer knownPointer = null)
        {
            envelope = null;

            if (authoritativeFoodModel == null)
            {
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.MissingAuthority,
                    Message = "authoritativeFoodModel is required; cannot load without food authority."
                };
            }

            if (authoritativeItemModel == null)
            {
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.MissingAuthority,
                    Message = "authoritativeItemModel is required; cannot load without item authority."
                };
            }

            if (authoritativeFoodModel.State == null)
            {
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.MissingAuthority,
                    Message = "authoritativeFoodModel.State is null."
                };
            }

            FoodOwnershipPointer pointer = knownPointer;
            if (pointer == null)
            {
                var ptrResult = ReadAuthoritativePointerInternal();
                if (ptrResult.Status != CheckpointLoadStatus.Success)
                {
                    return ptrResult;
                }
                pointer = ptrResult.Pointer;
            }

            if (!string.Equals(authoritativeFoodModel.State.world, _expectedWorld, StringComparison.Ordinal) ||
                !string.Equals(authoritativeFoodModel.State.generation, _expectedFoodGen, StringComparison.Ordinal) ||
                !string.Equals(authoritativeFoodModel.State.actorId, _expectedActor, StringComparison.Ordinal) ||
                !string.Equals(authoritativeItemModel.WorldId, _expectedWorld, StringComparison.Ordinal) ||
                !string.Equals(authoritativeItemModel.GenerationId, _expectedPhysGen, StringComparison.Ordinal))
            {
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.ScopeMismatch,
                    Message = $"Authoritative model scope mismatch: expected {_expectedWorld}/{_expectedActor}/{_expectedFoodGen}/{_expectedPhysGen} but food model is {authoritativeFoodModel.State.world}/{authoritativeFoodModel.State.actorId}/{authoritativeFoodModel.State.generation}, item model is {authoritativeItemModel.WorldId}/{authoritativeItemModel.GenerationId}.",
                    Pointer = pointer
                };
            }

            string chkPath = Path.Combine(_repositoryDirectory, pointer.checkpointFilename);

            if (!File.Exists(chkPath))
            {
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.MissingCheckpoint,
                    Message = $"Authoritative checkpoint file referenced by pointer does not exist: {pointer.checkpointFilename}.",
                    Pointer = pointer
                };
            }

            try
            {
                var fi = new FileInfo(chkPath);
                if (fi.Length > FoodOwnershipCheckpointEnvelope.MaxEnvelopeSizeBytes)
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.CorruptCheckpoint,
                        Message = $"Checkpoint file size {fi.Length} exceeds maximum allowed budget ({FoodOwnershipCheckpointEnvelope.MaxEnvelopeSizeBytes} bytes).",
                        Pointer = pointer
                    };
                }

                string chkJson = File.ReadAllText(chkPath);
                if (!FoodOwnershipCheckpointCodec.TryDecodeEnvelope(chkJson, out var decodedEnv, out string decodeErr))
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.CorruptCheckpoint,
                        Message = "Newest acknowledged checkpoint is corrupt: " + decodeErr,
                        Pointer = pointer
                    };
                }

                if (decodedEnv.sequence != pointer.sequence)
                {
                    long seq = decodedEnv.sequence;
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.CorruptCheckpoint,
                        Message = $"Checkpoint sequence {seq} mismatch with pointer sequence {pointer.sequence}.",
                        Pointer = pointer
                    };
                }

                if (!string.Equals(decodedEnv.checkpointHash, pointer.checkpointHash, StringComparison.OrdinalIgnoreCase))
                {
                    string hash = decodedEnv.checkpointHash;
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.CorruptCheckpoint,
                        Message = $"Checkpoint integrity hash mismatch with pointer ({hash} vs {pointer.checkpointHash}).",
                        Pointer = pointer
                    };
                }

                if (decodedEnv.worldId != _expectedWorld ||
                    decodedEnv.actorId != _expectedActor ||
                    decodedEnv.foodGeneration != _expectedFoodGen ||
                    decodedEnv.physicalGeneration != _expectedPhysGen)
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.ScopeMismatch,
                        Message = "Checkpoint scope does not match expected repository configuration.",
                        Pointer = pointer
                    };
                }

                if (!FoodOwnershipCheckpointCodec.ValidateEnvelopeStructural(
                    decodedEnv,
                    _expectedWorld,
                    _expectedActor,
                    _expectedFoodGen,
                    _expectedPhysGen,
                    out string structErr))
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.CorruptCheckpoint,
                        Message = "Authoritative checkpoint structural validation failed: " + structErr,
                        Pointer = pointer
                    };
                }

                string stagingDir = Path.Combine(_repositoryDirectory, StagingSubdir);
                if (!FoodOwnershipCheckpointCodec.ValidateEnvelopeSemantic(
                    decodedEnv,
                    authoritativeFoodModel,
                    authoritativeItemModel,
                    stagingDir,
                    out string semErr))
                {
                    return new CheckpointLoadResult
                    {
                        Status = CheckpointLoadStatus.CorruptCheckpoint,
                        Message = "Authoritative checkpoint semantic validation failed: " + semErr,
                        Pointer = pointer
                    };
                }

                envelope = decodedEnv;
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.Success,
                    Message = "Authoritative checkpoint loaded successfully.",
                    Checkpoint = envelope,
                    Pointer = pointer
                };
            }
            catch (Exception ex)
            {
                envelope = null;
                return new CheckpointLoadResult
                {
                    Status = CheckpointLoadStatus.CorruptCheckpoint,
                    Message = "Exception reading authoritative checkpoint: " + ex.Message,
                    Pointer = pointer,
                    Error = ex
                };
            }
        }
    }
}
