using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CityLife.World;

namespace CityLife.Items
{
    /// <summary>
    /// Bounded runtime bootstrap for physical item foundation integration in the live canyon world.
    /// Explicitly authors and owns authoritative item definitions via PhysicalItemCatalog, supports
    /// multi-instance physical bootstrap and cold restore using the validated ItemPersistence list binder,
    /// binds authoritative ItemModel and PhysicalAuthority to NpcActionApi, and synchronizes dynamic
    /// physics transforms in FixedUpdate.
    /// Supports opt-in restart persistence via explicit absolute -physicalSave path, and 4 separate-process
    /// diagnostic flows: save-carried, load-carried, drop-save-free, and load-free under normal frames.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class PhysicalItemBootstrap : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public ItemModel Model { get; private set; }
        public IItemActionAuthority Authority { get; private set; }
        public PhysicalItem DemonstrationItem { get; private set; }
        public NpcInteractable DemonstrationInteractable { get; private set; }
        public MeshRenderer DemonstrationRenderer { get; private set; }

        [Tooltip("Serialized URP material asset for demonstration visual cube. Assigned during scene build generation.")]
        public Material DemonstrationMaterial;

        [Tooltip("Serialized URP material asset for woven basket visual. Assigned during scene build generation.")]
        public Material BasketMaterial;

        [Tooltip("Serialized URP material asset for natural stones.")]
        public Material StoneMaterial;

        [Tooltip("Serialized URP material asset for tinder dry brush.")]
        public Material TinderMaterial;

        [Tooltip("Serialized URP material asset for fallen wood.")]
        public Material WoodMaterial;

        [Tooltip("Opt-in flag to populate starter canyon layout with basket and small physical objects on fresh start.")]
        public bool OptInStarterLayout = false;

        [Tooltip("Narrow serialized explicit rooted save override. Priority: explicit CLI -physicalSave > ExplicitSavePathOverride > integrated default (if opted in).")]
        [SerializeField]
        public string ExplicitSavePathOverride = string.Empty;

        public string DemonstrationItemId = "canyon-artifact-01";
        public string DemonstrationItemTypeId = "canyon-stone";
        public float DemonstrationItemMassKg = 2.5f;
        public Vector3 DemonstrationItemDimensions = new Vector3(0.25f, 0.25f, 0.25f);

        public bool DiagnosticRunning { get; private set; }
        public string DiagnosticStatus { get; private set; } = "idle";
        public string DiagnosticMode { get; private set; }
        public string EvidenceDirectory { get; private set; }
        public string PhysicalSavePath { get; private set; }
        public bool SaveRejected { get; private set; }
        public bool SourceSaveRejected { get; private set; }
        public string RejectedSourcePath { get; private set; }
        public bool LastSaveFailed { get; private set; }
        public string LastSaveError { get; private set; }
        public bool HasSavedPayload { get; private set; }
        public static bool IsFullyQualifiedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;

            try
            {
                if (!Path.IsPathFullyQualified(path)) return false;
            }
            catch
            {
                return false;
            }

            // Reject Windows drive-relative paths (e.g. C:save.json)
            if (path.Length >= 2 && path[1] == ':')
            {
                if (path.Length < 3 || (path[2] != '\\' && path[2] != '/') || !char.IsLetter(path[0]))
                {
                    return false;
                }
            }

            // Reject Windows root-relative paths (e.g. \save.json or /save.json)
            if ((path.StartsWith("\\") || path.StartsWith("/")) && !path.StartsWith("\\\\") && !path.StartsWith("//"))
            {
                return false;
            }

            // Reject malformed UNC paths (e.g. \\save.json without server and share)
            if (path.StartsWith("\\\\") || path.StartsWith("//"))
            {
                string unc = path.Substring(2).TrimStart('\\', '/');
                int sep = unc.IndexOfAny(new[] { '\\', '/' });
                if (sep <= 0 || sep >= unc.Length - 1)
                {
                    return false;
                }
            }

            try
            {
                string full = Path.GetFullPath(path);
                if (string.IsNullOrEmpty(full)) return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        public static bool ValidateCommandLineSaveArgs(string[] args, out string path, out string error)
        {
            path = null;
            error = null;
            if (args == null) return true;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-physicalSave", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "invalid-cli-save-path";
                        return false;
                    }

                    string candidate = args[i + 1];
                    if (!IsFullyQualifiedPath(candidate))
                    {
                        error = "invalid-cli-save-path";
                        return false;
                    }

                    try
                    {
                        path = Path.GetFullPath(candidate);
                        return true;
                    }
                    catch
                    {
                        error = "invalid-cli-save-path";
                        return false;
                    }
                }
            }

            return true;
        }

        public static string GetDefaultSavePath(string worldId = null)
        {
            string root = !string.IsNullOrEmpty(Application.persistentDataPath)
                ? Application.persistentDataPath
                : Directory.GetCurrentDirectory();
            string fileName = !string.IsNullOrEmpty(worldId)
                ? $"physical-{worldId.Replace(':', '-').Replace('/', '-').Replace('\\', '-')}-default.json"
                : "physical-world-default.json";
            return Path.GetFullPath(Path.Combine(root, "Saves", fileName));
        }

        private string activeRecoveryPath;

        public string GetRecoverySavePath()
        {
            if (!string.IsNullOrEmpty(activeRecoveryPath))
            {
                return activeRecoveryPath;
            }

            string dir = !string.IsNullOrEmpty(RejectedSourcePath)
                ? Path.GetDirectoryName(RejectedSourcePath)
                : Path.Combine(!string.IsNullOrEmpty(Application.persistentDataPath) ? Application.persistentDataPath : Directory.GetCurrentDirectory(), "Saves");

            string baseName;
            if (!string.IsNullOrEmpty(RejectedSourcePath))
            {
                baseName = Path.GetFileNameWithoutExtension(RejectedSourcePath) + "-recovery";
            }
            else
            {
                string worldId = Brain != null ? Brain.InstanceWorldId : null;
                baseName = !string.IsNullOrEmpty(worldId)
                    ? $"physical-{worldId.Replace(':', '-').Replace('/', '-').Replace('\\', '-')}-recovery"
                    : "physical-world-recovery";
            }

            string candidate = Path.GetFullPath(Path.Combine(dir, baseName + ".json"));
            int counter = 1;
            while (File.Exists(candidate))
            {
                candidate = Path.GetFullPath(Path.Combine(dir, $"{baseName}-{counter}.json"));
                counter++;
            }
            return candidate;
        }

        public bool SaveRecoveryState(out string recoveryPath)
        {
            recoveryPath = GetRecoverySavePath();
            return SaveCurrentState(recoveryPath, isRecoveryAction: true);
        }

        private PhysicalSavePayload savedPayload;
        private bool restoreAttempted;
        private bool itemCreated;

        public PhysicalItemCatalog Catalog { get; private set; }
        private readonly List<PhysicalItemRuntimeBinding> bindings = new List<PhysicalItemRuntimeBinding>();
        public IReadOnlyList<PhysicalItemRuntimeBinding> Bindings => bindings;

        public void RegisterBinding(PhysicalItemRuntimeBinding binding)
        {
            if (binding != null && !bindings.Contains(binding))
            {
                bindings.Add(binding);
            }
        }

        public IEnumerable<NpcInteractable> AllInteractables
        {
            get
            {
                if (bindings != null && bindings.Count > 0)
                {
                    for (int i = 0; i < bindings.Count; i++)
                    {
                        var b = bindings[i];
                        if (b != null && b.interactable != null)
                            yield return b.interactable;
                    }
                }
                else if (DemonstrationInteractable != null)
                {
                    yield return DemonstrationInteractable;
                }
            }
        }

        public bool RegisterAuthoritativeDefinition(ItemDefinition def)
        {
            if (def == null || !def.IsValid())
                return false;

            if (Catalog == null) Catalog = PhysicalItemCatalog.CreateDefaultCatalog();

            if (Catalog.Contains(def.itemTypeId))
                return false;

            if (Model != null)
            {
                if (Model.TryGetDefinition(def.itemTypeId, out _))
                    return false;

                bool modelRegistered = Model.RegisterDefinition(def);
                if (!modelRegistered)
                    return false;

                bool catalogRegistered = Catalog.Register(def);
                if (!catalogRegistered)
                    return false;

                return true;
            }

            return Catalog.Register(def);
        }


        private void Awake()
        {
            if (Brain == null) Brain = GetComponent<NpcAutonomy>();
            if (Brain != null && Brain.PhysicalItems == null) Brain.PhysicalItems = this;

            string cliSavePath = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-physicalSave", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        SaveRejected = true;
                        LastSaveFailed = true;
                        LastSaveError = "invalid-cli-save-path";
                        Debug.LogError("[PhysicalItemBootstrap] CLI argument -physicalSave was supplied without a path. Halting save IO without fallback.");
                        return;
                    }

                    string candidatePath = args[i + 1];
                    if (!IsFullyQualifiedPath(candidatePath))
                    {
                        SaveRejected = true;
                        LastSaveFailed = true;
                        LastSaveError = "invalid-cli-save-path";
                        Debug.LogError($"[PhysicalItemBootstrap] CLI argument -physicalSave path '{candidatePath}' is not fully qualified and absolute. Halting save IO without fallback.");
                        return;
                    }

                    try
                    {
                        cliSavePath = Path.GetFullPath(candidatePath);
                    }
                    catch (Exception ex)
                    {
                        SaveRejected = true;
                        LastSaveFailed = true;
                        LastSaveError = "invalid-cli-save-path";
                        Debug.LogError($"[PhysicalItemBootstrap] CLI argument -physicalSave path '{candidatePath}' cannot be normalized: {ex.Message}. Halting save IO without fallback.");
                        return;
                    }
                }
                else if ((string.Equals(args[i], "-physicalDiagnosticMode", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(args[i], "-physicalSaveMode", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                {
                    DiagnosticMode = args[i + 1].ToLowerInvariant().Trim();
                }
                else if (string.Equals(args[i], "-physicalInWorldDiagnostic", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(DiagnosticMode))
                    {
                        DiagnosticMode = "in-world";
                    }
                }
                else if ((string.Equals(args[i], "-physicalEvidence", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(args[i], "-physicalItemEvidence", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                {
                    EvidenceDirectory = Path.GetFullPath(args[i + 1]);
                }
            }

            string worldId = Brain != null ? Brain.InstanceWorldId : "starfall.coastal-canyon.v1";
            string actorId = Brain != null ? NpcAutonomy.AgentId : "inhabitant-01";

            // Strict Save Path Precedence:
            // 1. Explicit CLI -physicalSave highest
            // 2. Serialized ExplicitSavePathOverride second (validated rooted/absolute before any IO)
            // 3. Integrated default only if both absent and opted in
            if (!string.IsNullOrEmpty(cliSavePath))
            {
                PhysicalSavePath = cliSavePath;
            }
            else if (!string.IsNullOrEmpty(ExplicitSavePathOverride))
            {
                if (!IsFullyQualifiedPath(ExplicitSavePathOverride))
                {
                    SaveRejected = true;
                    LastSaveFailed = true;
                    LastSaveError = "invalid-configured-save-path";
                    Debug.LogError($"[PhysicalItemBootstrap] Explicit save path override '{ExplicitSavePathOverride}' is not fully qualified and absolute. Halting save IO without fallback to user default.");
                    return;
                }

                try
                {
                    PhysicalSavePath = Path.GetFullPath(ExplicitSavePathOverride);
                }
                catch (Exception ex)
                {
                    SaveRejected = true;
                    LastSaveFailed = true;
                    LastSaveError = "invalid-configured-save-path";
                    Debug.LogError($"[PhysicalItemBootstrap] Explicit save path override '{ExplicitSavePathOverride}' cannot be normalized: {ex.Message}. Halting save IO without fallback to user default.");
                    return;
                }
            }
            else if (OptInStarterLayout)
            {
                PhysicalSavePath = GetDefaultSavePath(worldId);
            }

            if (Catalog == null) Catalog = PhysicalItemCatalog.CreateDefaultCatalog();

            // Validate demonstration configuration before creating any objects or models
            if (!ItemModel.IsValidId(DemonstrationItemId) ||
                string.IsNullOrEmpty(DemonstrationItemTypeId) ||
                !ItemModel.IsValidId(DemonstrationItemTypeId) ||
                !ItemDefinition.Finite(DemonstrationItemMassKg) || DemonstrationItemMassKg <= 0f || DemonstrationItemMassKg > 10000f ||
                DemonstrationItemDimensions.x <= 0f || DemonstrationItemDimensions.x > 20f || !ItemDefinition.Finite(DemonstrationItemDimensions.x) ||
                DemonstrationItemDimensions.y <= 0f || DemonstrationItemDimensions.y > 20f || !ItemDefinition.Finite(DemonstrationItemDimensions.y) ||
                DemonstrationItemDimensions.z <= 0f || DemonstrationItemDimensions.z > 20f || !ItemDefinition.Finite(DemonstrationItemDimensions.z))
            {
                SaveRejected = true;
                Debug.LogError($"[PhysicalItemBootstrap] Invalid demonstration configuration: itemId='{DemonstrationItemId}', typeId='{DemonstrationItemTypeId}', mass={DemonstrationItemMassKg}, dimensions={DemonstrationItemDimensions}. Zero objects created.");
                return;
            }

            var demoDef = new ItemDefinition
            {
                itemTypeId = DemonstrationItemTypeId,
                massKg = DemonstrationItemMassKg,
                dimensions = new PhysicalDimensions(DemonstrationItemDimensions.x, DemonstrationItemDimensions.y, DemonstrationItemDimensions.z),
                isContainer = false,
                isAnchored = false,
                requiresSupportToPlace = false
            };

            if (!demoDef.IsValid())
            {
                SaveRejected = true;
                Debug.LogError("[PhysicalItemBootstrap] Demonstration definition is not valid. Zero objects created.");
                return;
            }

            // Explicit validated authoritative demo configuration updates catalog
            bool registeredInCatalog = Catalog.RegisterOrUpdate(demoDef);
            if (!registeredInCatalog)
            {
                SaveRejected = true;
                Debug.LogError("[PhysicalItemBootstrap] Failed to register demonstration definition in authoritative catalog.");
                return;
            }

            Model = new ItemModel(worldId, "gen-01");
            Catalog.PopulateModel(Model);
            Authority = new BasicItemActionAuthority();

            SetupDemonstrationItem();

            // The coordinated checkpoint is newer than an independent physical
            // save after actions such as storage. Hydrate that exact item graph
            // before NpcAutonomy constructs its action registry.
            if (Brain?.Survival?.Food != null)
            {
                var repositoryPath = FoodConsumptionBridge.GetRepositoryDirectory(Brain);
                if (File.Exists(Path.Combine(repositoryPath, Starfall.Food.FoodOwnershipCheckpointRepository.PointerFileName)))
                {
                    Brain.Survival.Food.EnsureModel();
                    var foodModel = Brain.Survival.Food.Model;
                    var repository = new Starfall.Food.FoodOwnershipCheckpointRepository(repositoryPath, worldId,
                        NpcAutonomy.AgentId, foodModel.State.generation, Model.GenerationId);
                    var loaded = repository.LoadAuthoritativeCheckpoint(foodModel, Model, out var envelope);
                    if (loaded.Status != Starfall.Food.CheckpointLoadStatus.Success || envelope == null ||
                        !LoadSavePayloadInternal(PhysicalSavePath, envelope.physicalPayload))
                    {
                        SaveRejected = true;
                        Debug.LogWarning("[PhysicalItemBootstrap] Authoritative checkpoint hydration refused; no stale physical fallback. " + loaded.Message);
                    }
                    else Debug.Log("[PhysicalItemBootstrap] Hydrated authoritative checkpoint item graph.");
                    return;
                }
            }

            if (!string.IsNullOrEmpty(PhysicalSavePath))
            {
                if (File.Exists(PhysicalSavePath))
                {
                    bool loaded = LoadSavePayload(PhysicalSavePath);
                    if (!loaded)
                    {
                        SaveRejected = true;
                        SourceSaveRejected = true;
                        RejectedSourcePath = PhysicalSavePath;
                        Debug.LogWarning($"[PhysicalItemBootstrap] Rejected invalid/foreign save file at '{PhysicalSavePath}'. Prior file preserved untouched; fresh start initialized.");
                    }
                    else
                    {
                        HasSavedPayload = true;
                        SaveRejected = false;
                        SourceSaveRejected = false;
                        RejectedSourcePath = null;
                        Debug.Log($"[PhysicalItemBootstrap] Valid physical save payload loaded from '{PhysicalSavePath}'.");
                    }
                }
                else
                {
                    Debug.Log($"[PhysicalItemBootstrap] Physical save path '{PhysicalSavePath}' not found on disk. Fresh start initialized.");
                }
            }
        }

        public bool LoadSavePayload(string path)
            => LoadSavePayloadInternal(path, null);

        private bool LoadSavePayloadInternal(string path, string authoritativePayload)
        {
            SourceSaveRejected = true;
            RejectedSourcePath = path;

            // 1. Explicit unsupported live reload refusal BEFORE ANY mutation
            if (Brain != null && Brain.Actions != null)
            {
                SaveRejected = true;
                return false;
            }

            if (string.IsNullOrEmpty(path) || !IsFullyQualifiedPath(path) || (authoritativePayload == null && !File.Exists(path)))
            {
                SaveRejected = true;
                return false;
            }

            string worldId = Brain != null ? Brain.InstanceWorldId : "starfall.coastal-canyon.v1";
            string actorId = Brain != null ? NpcAutonomy.AgentId : "inhabitant-01";
            UnityEngine.SceneManagement.Scene targetScene = Brain != null && Brain.gameObject.scene.IsValid()
                ? Brain.gameObject.scene
                : UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            if (Catalog == null) Catalog = PhysicalItemCatalog.CreateDefaultCatalog();

            var candidateModel = new ItemModel(worldId, "gen-01");
            Catalog.PopulateModel(candidateModel);

            var stagedObjects = new List<GameObject>();
            var stagedBindings = new List<PhysicalItemRuntimeBinding>();

            try
            {
                var fi = new FileInfo(path);
                if (authoritativePayload == null && fi.Length > ItemPersistence.MaxFileSizeBytes)
                {
                    SaveRejected = true;
                    return false;
                }

                string text = authoritativePayload == null ? File.ReadAllText(path) : JsonUtility.ToJson(new PhysicalSaveEnvelope
                { schema = ItemPersistence.SchemaVersion, payload = authoritativePayload, sha256 = ItemPersistence.ComputeSha256(authoritativePayload) });
                if (string.IsNullOrEmpty(text))
                {
                    SaveRejected = true;
                    return false;
                }

                var envelope = JsonUtility.FromJson<PhysicalSaveEnvelope>(text);
                if (envelope == null || envelope.schema != ItemPersistence.SchemaVersion ||
                    string.IsNullOrEmpty(envelope.payload) || string.IsNullOrEmpty(envelope.sha256))
                {
                    SaveRejected = true;
                    return false;
                }

                string computedSha = ItemPersistence.ComputeSha256(envelope.payload);
                if (!string.Equals(computedSha, envelope.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SaveRejected = true;
                    return false;
                }

                var candidate = JsonUtility.FromJson<PhysicalSavePayload>(envelope.payload);
                if (candidate == null)
                {
                    SaveRejected = true;
                    return false;
                }

                // Scope validation
                if (!string.Equals(candidate.worldId, worldId, StringComparison.Ordinal) ||
                    !string.Equals(candidate.generationId, "gen-01", StringComparison.Ordinal) ||
                    !string.Equals(candidate.actorId, actorId, StringComparison.Ordinal) ||
                    candidate.tick < 0 || candidate.items == null)
                {
                    SaveRejected = true;
                    return false;
                }

                // Validate original nonphysical registry ID collisions
                if (Brain != null && Brain.Registry != null)
                {
                    for (int i = 0; i < Brain.Registry.Length; i++)
                    {
                        var reg = Brain.Registry[i];
                        if (reg != null && !string.IsNullOrEmpty(reg.StableId))
                        {
                            for (int j = 0; j < candidate.items.Count; j++)
                            {
                                var it = candidate.items[j];
                                if (it != null && string.Equals(reg.StableId, it.itemId, StringComparison.Ordinal))
                                {
                                    var authoredPhysical = reg.GetComponent<PhysicalItem>();
                                    if (authoredPhysical != null && authoredPhysical.itemId == it.itemId &&
                                        authoredPhysical.itemTypeId == it.itemTypeId && reg.WorldId == worldId) continue;
                                    SaveRejected = true;
                                    return false;
                                }
                            }
                        }
                    }
                }

                // Strictly validate all items against authoritative catalog
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                SavedItemRecord carriedRecord = null;

                foreach (var it in candidate.items)
                {
                    if (it == null || !ItemModel.IsValidId(it.itemId))
                    {
                        SaveRejected = true;
                        return false;
                    }
                    if (!seenIds.Add(it.itemId))
                    {
                        SaveRejected = true;
                        return false; // Duplicate item ID
                    }

                    // Must exist in authoritative catalog (defensive copy returned)
                    if (!Catalog.TryGet(it.itemTypeId, out var def))
                    {
                        SaveRejected = true;
                        return false;
                    }
                    if (def.isAnchored)
                    {
                        SaveRejected = true;
                        return false;
                    }

                    if (Mathf.Abs(it.massKg - def.massKg) > 0.0001f)
                    {
                        SaveRejected = true;
                        return false;
                    }
                    if (Mathf.Abs(it.dimensions.width - def.dimensions.width) > 0.0001f ||
                        Mathf.Abs(it.dimensions.height - def.dimensions.height) > 0.0001f ||
                        Mathf.Abs(it.dimensions.depth - def.dimensions.depth) > 0.0001f)
                    {
                        SaveRejected = true;
                        return false;
                    }

                    if (!ItemDefinition.Finite(it.position.x) || !ItemDefinition.Finite(it.position.y) || !ItemDefinition.Finite(it.position.z))
                    {
                        SaveRejected = true;
                        return false;
                    }
                    if (!ItemDefinition.TryCanonicalizeRotation(it.rotation, out var canonRot))
                    {
                        SaveRejected = true;
                        return false;
                    }
                    it.rotation = canonRot;

                    if (it.lastUpdatedTick < 0 || it.lastUpdatedTick > candidate.tick)
                    {
                        SaveRejected = true;
                        return false;
                    }

                    if (it.location == ItemLocationKind.Carried)
                    {
                        if (carriedRecord != null)
                        {
                            SaveRejected = true;
                            return false; // Multiple carried roots
                        }
                        carriedRecord = it;
                    }
                }

                // Check actor/animator/hand requirements
                Transform hand = null;
                if (carriedRecord != null)
                {
                    if (Brain == null || Brain.Actor == null || Brain.Actor.Animator == null)
                    {
                        SaveRejected = true;
                        return false; // Required actor/animator absent
                    }
                    hand = Brain.Actor.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                    if (hand == null || hand.gameObject.scene != targetScene)
                    {
                        SaveRejected = true;
                        return false; // Required hand absent or wrong scene
                    }
                }

                // Model graph preflight
                if (!candidateModel.CanRestoreSnapshot(candidate))
                {
                    SaveRejected = true;
                    return false;
                }

                // Stage inactive GameObjects and candidate bindings
                foreach (var rec in candidate.items)
                {
                    Catalog.TryGet(rec.itemTypeId, out var def);

                    var go = new GameObject(rec.itemId);
                    go.SetActive(false); // STAGED INACTIVE
                    stagedObjects.Add(go);
                    go.layer = 11;
                    if (targetScene.IsValid())
                    {
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, targetScene);
                    }

                    go.transform.position = rec.position;
                    go.transform.rotation = rec.rotation;
                    go.transform.localScale = Vector3.one;

                    var box = go.AddComponent<BoxCollider>();
                    box.size = new Vector3(def.dimensions.width, def.dimensions.height, def.dimensions.depth);
                    box.center = Vector3.zero;

                    var rb = go.AddComponent<Rigidbody>();
                    rb.mass = def.massKg;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                    CreateVisualForItem(go.transform, rec.itemTypeId, def);

                    var approachObj = new GameObject(rec.itemId + " approach");
                    approachObj.transform.SetParent(go.transform, false);
                    approachObj.transform.localPosition = Vector3.zero;

                    var interactable = go.AddComponent<NpcInteractable>();
                    interactable.StableId = rec.itemId;
                    interactable.WorldId = worldId;
                    interactable.Kind = NpcObjectKind.Item;
                    interactable.Permission = true;
                    interactable.Approach = approachObj.transform;

                    var phys = go.AddComponent<PhysicalItem>();
                    phys.itemId = rec.itemId;
                    phys.itemTypeId = rec.itemTypeId;
                    phys.massKg = def.massKg;
                    phys.dimensions = def.dimensions;
                    phys.isAnchored = false;
                    phys.ConfigureComponents();
                    if (rec.itemTypeId == FishingRodItem.ItemTypeId) FishingRodItem.ConfigureRuntimeRod(phys, interactable);
                    phys.Bind(candidateModel, worldId, candidateModel.GenerationId);
                    phys.RecordInitialRendererStates();

                    if (!phys.IsValid())
                    {
                        SaveRejected = true;
                        DisposeStaged(stagedObjects);
                        return false;
                    }

                    stagedBindings.Add(new PhysicalItemRuntimeBinding(rec.itemId, phys, interactable));
                }

                if (stagedBindings.Count != candidate.items.Count)
                {
                    SaveRejected = true;
                    DisposeStaged(stagedObjects);
                    return false;
                }

                // Validate actual list binder before publishing or destroying baseline
                var candidateInteractables = new List<NpcInteractable>(stagedBindings.Count + (Brain != null && Brain.Registry != null ? Brain.Registry.Length : 0));
                if (Brain != null && Brain.Registry != null)
                {
                    for (int i = 0; i < Brain.Registry.Length; i++)
                    {
                        if (Brain.Registry[i] != null && !seenIds.Contains(Brain.Registry[i].StableId)) candidateInteractables.Add(Brain.Registry[i]);
                    }
                }
                for (int i = 0; i < stagedBindings.Count; i++)
                {
                    if (stagedBindings[i].interactable != null) candidateInteractables.Add(stagedBindings[i].interactable);
                }

                Transform actorTransform = (Brain != null && Brain.Actor != null) ? Brain.Actor.transform : (Brain != null ? Brain.transform : (targetScene.rootCount > 0 ? targetScene.GetRootGameObjects()[0].transform : transform));
                var candidateActions = new NpcActionApi(actorId, worldId, actorTransform, hand, candidateInteractables);
                candidateActions.PhysicalModel = candidateModel;
                candidateActions.PhysicalAuthority = Authority ?? new BasicItemActionAuthority();

                bool binderValidated = ItemPersistence.RestoreRuntime(candidate, candidateModel, candidateActions, stagedBindings);
                if (!binderValidated)
                {
                    SaveRejected = true;
                    DisposeStaged(stagedObjects);
                    return false;
                }

                // ALL FALLIBLE CHECKS & BINDER VALIDATION PASSED: IRREVERSIBLE COMMIT
                // 1. Destroy previous baseline physical objects
                ClearCommittedItems();
                if (Brain?.Registry != null)
                    foreach (var authored in Brain.Registry)
                        if (authored != null && seenIds.Contains(authored.StableId) && authored.GetComponent<PhysicalItem>() != null)
                            DestroyImmediate(authored.gameObject);

                // 2. Activate staged candidate objects
                foreach (var go in stagedObjects)
                {
                    if (go != null) go.SetActive(true);
                }

                // 3. Commit model, bindings, and save payload
                Model = candidateModel;
                bindings.Clear();
                bindings.AddRange(stagedBindings);
                savedPayload = candidate;
                HasSavedPayload = true;
                SaveRejected = false;
                SourceSaveRejected = false;
                RejectedSourcePath = null;
                restoreAttempted = true; // Staged objects already restored by validated binder

                var school = UnityEngine.Object.FindAnyObjectByType<CityLife.World.RiverFishSchool>();
                if (school != null)
                {
                    school.ReconcileWithAuthoritativeModel(Model);
                }

                // 4. Update demonstration references for backward compatibility
                PhysicalItem demoPhys = null;
                foreach (var b in bindings)
                {
                    if (string.Equals(b.itemId, DemonstrationItemId, StringComparison.Ordinal))
                    {
                        demoPhys = b.physicalItem;
                        break;
                    }
                }
                if (demoPhys == null && carriedRecord != null)
                {
                    foreach (var b in bindings)
                    {
                        if (string.Equals(b.itemId, carriedRecord.itemId, StringComparison.Ordinal))
                        {
                            demoPhys = b.physicalItem;
                            break;
                        }
                    }
                }
                if (demoPhys == null && bindings.Count > 0)
                {
                    demoPhys = bindings[0].physicalItem;
                }

                if (demoPhys != null)
                {
                    DemonstrationItem = demoPhys;
                    DemonstrationInteractable = demoPhys.GetComponent<NpcInteractable>();
                    DemonstrationRenderer = demoPhys.GetComponentInChildren<MeshRenderer>();
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhysicalItemBootstrap] Exception during save staging: {ex.Message}");
                DisposeStaged(stagedObjects);
                SaveRejected = true;
                return false;
            }
        }

        private void ClearCommittedItems()
        {
            if (bindings != null)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (b != null && b.physicalItem != null && b.physicalItem.gameObject != null)
                    {
                        b.physicalItem.transform.SetParent(null, false);
                    }
                }
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (b != null && b.physicalItem != null && b.physicalItem.gameObject != null)
                    {
                        DestroyImmediate(b.physicalItem.gameObject);
                    }
                }
                bindings.Clear();
            }
            if (DemonstrationItem != null && DemonstrationItem.gameObject != null)
            {
                DemonstrationItem.transform.SetParent(null, false);
                DestroyImmediate(DemonstrationItem.gameObject);
                DemonstrationItem = null;
                DemonstrationInteractable = null;
                DemonstrationRenderer = null;
            }
            itemCreated = false;
        }

        private void OnDestroy()
        {
            ClearCommittedItems();
        }

        private static void DisposeStaged(List<GameObject> stagedObjects)
        {
            if (stagedObjects == null) return;
            for (int i = 0; i < stagedObjects.Count; i++)
            {
                var go = stagedObjects[i];
                if (go != null)
                {
                    DestroyImmediate(go);
                }
            }
            stagedObjects.Clear();
        }

        private void Start()
        {
            if (Brain != null && Brain.Actions != null)
            {
                OnActionsCreated(Brain.Actions);
            }

            if (!string.IsNullOrEmpty(DiagnosticMode))
            {
                StartCoroutine(RunDiagnosticFlow(DiagnosticMode, EvidenceDirectory, PhysicalSavePath));
            }
        }

        public bool RebindActions(NpcActionApi actions)
        {
            if (actions == null || Model == null) return false;

            // Preflight candidate context before mutating actions or objects
            string worldId = Brain != null ? Brain.InstanceWorldId : Model.WorldId;
            string actorId = Brain != null ? NpcAutonomy.AgentId : actions.AgentId;

            if (!string.Equals(actions.WorldId, Model.WorldId, StringComparison.Ordinal) ||
                !string.Equals(actions.WorldId, worldId, StringComparison.Ordinal) ||
                !string.Equals(actions.AgentId, actorId, StringComparison.Ordinal))
            {
                return false;
            }

            if (actions.ActorTransform == null || !actions.ActorTransform.gameObject.scene.IsValid())
            {
                return false;
            }

            // Create current state snapshot to reuse the validated ItemPersistence binder contract
            var currentSnapshot = ItemPersistence.CreateSnapshot(Model, actions.AgentId);
            if (currentSnapshot == null) return false;

            var origModel = actions.PhysicalModel;
            var origAuth = actions.PhysicalAuthority;

            // Assign candidate context for binder preflight
            actions.PhysicalModel = Model;
            actions.PhysicalAuthority = Authority ?? new BasicItemActionAuthority();

            bool restored = ItemPersistence.RestoreRuntime(currentSnapshot, Model, actions, bindings);
            if (!restored)
            {
                // Restore original context on failure with zero object mutations
                actions.PhysicalModel = origModel;
                actions.PhysicalAuthority = origAuth;
                return false;
            }

            Authority = actions.PhysicalAuthority;
            return true;
        }

        public bool OnActionsCreated(NpcActionApi actions)
        {
            if (actions == null || Model == null) return false;

            if (!string.IsNullOrEmpty(actions.AgentId))
            {
                Model.SetActorCarryLimits(actions.AgentId, new ActorCarryLimits(25.0f, 2));
            }

            if (HasSavedPayload && savedPayload != null && !restoreAttempted)
            {
                var origModel = actions.PhysicalModel;
                var origAuth = actions.PhysicalAuthority;
                actions.PhysicalModel = Model;
                actions.PhysicalAuthority = Authority ?? new BasicItemActionAuthority();

                bool restored = ItemPersistence.RestoreRuntime(savedPayload, Model, actions, bindings);
                if (!restored)
                {
                    actions.PhysicalModel = origModel;
                    actions.PhysicalAuthority = origAuth;
                    Debug.LogError("[PhysicalItemBootstrap] Failed to restore runtime state from save payload.");
                    return false;
                }
                else
                {
                    restoreAttempted = true;
                    Authority = actions.PhysicalAuthority;
                    Debug.Log("[PhysicalItemBootstrap] Successfully restored runtime physical state from save payload.");
                    return true;
                }
            }

            return RebindActions(actions);
        }

        private void Update()
        {
            if (Brain != null && Brain.Actions != null && Brain.Actions.PhysicalModel == null)
            {
                OnActionsCreated(Brain.Actions);
            }
        }

        public void SyncFreeItems()
        {
            if (Brain == null || Brain.Actions == null || Model == null)
                return;

            if (bindings != null && bindings.Count > 0)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (b == null || b.physicalItem == null || b.interactable == null) continue;
                    if (b.physicalItem.gameObject != b.interactable.gameObject) continue;
                    if (!b.physicalItem.gameObject.scene.IsValid() || b.physicalItem.gameObject.scene != Brain.gameObject.scene) continue;
                    if (b.interactable.WorldId != Brain.InstanceWorldId) continue;
                    if (!b.physicalItem.IsBoundTo(Model, Brain.InstanceWorldId, Model.GenerationId)) continue;
                    if (!Model.TryGetItem(b.itemId, out var snap) || snap.location != ItemLocationKind.Free) continue;

                    Brain.Actions.SyncFreeTransform(b.itemId);
                }
            }
            else if (DemonstrationItem != null && DemonstrationInteractable != null)
            {
                if (DemonstrationItem.gameObject == DemonstrationInteractable.gameObject &&
                    DemonstrationItem.gameObject.scene.IsValid() && DemonstrationItem.gameObject.scene == Brain.gameObject.scene &&
                    DemonstrationInteractable.WorldId == Brain.InstanceWorldId &&
                    DemonstrationItem.IsBoundTo(Model, Brain.InstanceWorldId, Model.GenerationId))
                {
                    Brain.Actions.SyncFreeTransform(DemonstrationItemId);
                }
            }
        }

        private void FixedUpdate()
        {
            SyncFreeItems();
        }

        private void SetupDemonstrationItem()
        {
            if (itemCreated) return;

            Vector3 spawnPos = Brain != null ? Brain.SpawnPosition : Vector3.zero;
            // Place within natural reach: 0.42m in front of inhabitant spawn along forward axis
            Vector3 itemPos = spawnPos + new Vector3(0f, 0.15f, 0.42f);
            if (Physics.Raycast(itemPos + Vector3.up * 2f, Vector3.down, out var hit, 10f, (1 << 8) | (1 << 10)))
            {
                itemPos.y = hit.point.y + DemonstrationItemDimensions.y * 0.5f;
            }

            var go = new GameObject(DemonstrationItemId);
            go.layer = 0; // Layer 0 (Default): excluded from perception 11 and LOS 8|10, included in drop clearance
            if (Brain != null && Brain.gameObject.scene.IsValid())
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, Brain.gameObject.scene);
            }

            go.transform.position = itemPos;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var box = go.AddComponent<BoxCollider>();
            box.size = DemonstrationItemDimensions;
            box.center = Vector3.zero;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = DemonstrationItemMassKg;
            // Speculative continuous collision detection anticipates both linear and angular motion to mitigate contact tunneling on rotating items
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var demoDefForVisual = new ItemDefinition
            {
                itemTypeId = DemonstrationItemTypeId,
                massKg = DemonstrationItemMassKg,
                dimensions = new PhysicalDimensions(DemonstrationItemDimensions.x, DemonstrationItemDimensions.y, DemonstrationItemDimensions.z)
            };
            var visual = CreateVisualForItem(go.transform, DemonstrationItemTypeId, demoDefForVisual);
            DemonstrationRenderer = visual.GetComponentInChildren<MeshRenderer>();

            string matDiag = GetVisualMaterialDiagnostic();
            if (DemonstrationMaterial != null)
            {
                Debug.Log($"[PhysicalItemBootstrap] Demonstration visual material: {matDiag}");
            }
            else
            {
                Debug.LogWarning($"[PhysicalItemBootstrap] Demonstration visual material reference absent: {matDiag}");
            }

            var interactable = go.AddComponent<NpcInteractable>();
            interactable.StableId = DemonstrationItemId;
            interactable.WorldId = Brain != null ? Brain.InstanceWorldId : "starfall.coastal-canyon.v1";
            interactable.Kind = NpcObjectKind.Item;
            interactable.Permission = true;

            var approachObj = new GameObject(DemonstrationItemId + " approach");
            approachObj.transform.SetParent(go.transform, false);
            approachObj.transform.localPosition = Vector3.zero;
            interactable.Approach = approachObj.transform;
            DemonstrationInteractable = interactable;

            var phys = go.AddComponent<PhysicalItem>();
            phys.itemId = DemonstrationItemId;
            phys.itemTypeId = DemonstrationItemTypeId;
            phys.massKg = DemonstrationItemMassKg;
            phys.dimensions = new PhysicalDimensions(DemonstrationItemDimensions.x, DemonstrationItemDimensions.y, DemonstrationItemDimensions.z);
            phys.isAnchored = false;
            phys.ConfigureComponents();
            phys.Bind(Model, interactable.WorldId, Model.GenerationId);
            phys.RecordInitialRendererStates();
            DemonstrationItem = phys;

            bool registered = Model.RegisterItem(DemonstrationItemId, DemonstrationItemTypeId, ItemLocationKind.Free, go.transform.position, go.transform.rotation);
            if (!registered)
            {
                DestroyImmediate(go);
                DemonstrationItem = null;
                DemonstrationInteractable = null;
                DemonstrationRenderer = null;
                itemCreated = false;
                Debug.LogError($"[PhysicalItemBootstrap] Failed to register demonstration item '{DemonstrationItemId}' in Model.");
                return;
            }
            bindings.Clear();
            bindings.Add(new PhysicalItemRuntimeBinding(DemonstrationItemId, phys, interactable));
            itemCreated = true;

            if (OptInStarterLayout)
            {
                var targetScene = Brain != null && Brain.gameObject.scene.IsValid()
                    ? Brain.gameObject.scene
                    : UnityEngine.SceneManagement.SceneManager.GetActiveScene();

                CreateStarterPhysicalItem("canyon-cobble-01", "stone-river-cobble", new Vector3(1.2f, 0.10f, 0.8f), targetScene);
                CreateStarterPhysicalItem("canyon-fieldstone-01", "stone-fieldstone", new Vector3(-1.1f, 0.12f, 0.9f), targetScene);
                CreateStarterPhysicalItem("canyon-tinder-01", "fire-tinder-bundle", new Vector3(0.8f, 0.12f, 1.4f), targetScene);
                CreateStarterPhysicalItem("canyon-branch-01", "wood-fallen-branch", new Vector3(-1.4f, 0.15f, 1.6f), targetScene);
                CreateStarterPhysicalItem("canyon-ruby-01", "gem-ruby", new Vector3(-0.35f, 0.05f, 0.35f), targetScene);
                CreateStarterPhysicalItem("canyon-chisel-01", "tool-chisel", new Vector3(-0.35f, 0.05f, 0.48f), targetScene);
            }
        }

        public string GetVisualMaterialDiagnostic()
        {
            if (DemonstrationRenderer == null)
                return "renderer=none";
            var mat = DemonstrationRenderer.sharedMaterial;
            if (mat == null)
                return "material=none (unassigned)";
            string sName = mat.shader != null ? mat.shader.name : "missing";
            bool sup = mat.shader != null && mat.shader.isSupported;
            return $"material={mat.name}; shader={sName}; supported={sup}";
        }

        private GameObject CreateVisualForItem(Transform parent, string itemTypeId, ItemDefinition def)
        {
            if (itemTypeId == FishingRodItem.ItemTypeId) return FishingRodItem.CreateVisual(parent);
            if (string.Equals(itemTypeId, "container-basket", StringComparison.Ordinal))
            {
                var basketMat = BasketMaterial != null ? BasketMaterial : DemonstrationMaterial;
                return WovenBasketVisual.CreateVisual(parent, basketMat, new Vector3(def.dimensions.width, def.dimensions.height, def.dimensions.depth));
            }
            else if (string.Equals(itemTypeId, "stone-river-cobble", StringComparison.Ordinal) ||
                     string.Equals(itemTypeId, "stone-fieldstone", StringComparison.Ordinal))
            {
                var visual = new GameObject("Visual");
                visual.transform.SetParent(parent, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                var shape = string.Equals(itemTypeId, "stone-river-cobble", StringComparison.Ordinal)
                    ? CityLife.Stones.StoneShapeKind.RiverCobble
                    : CityLife.Stones.StoneShapeKind.Fieldstone;
                var mesh = CityLife.Stones.StoneMeshGenerator.GenerateMesh(shape, 101, 0, 1.0f, true);
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                var rend = visual.AddComponent<MeshRenderer>();
                rend.sharedMaterial = StoneMaterial != null ? StoneMaterial : DemonstrationMaterial;
                return visual;
            }
            else if (string.Equals(itemTypeId, "fire-tinder-bundle", StringComparison.Ordinal))
            {
                var visual = new GameObject("Visual");
                visual.transform.SetParent(parent, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                var mesh = CityLife.Fire.TinderGeometry.GenerateMesh(CityLife.Fire.TinderParameters.ForLod(0, 4217));
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                var rend = visual.AddComponent<MeshRenderer>();
                rend.sharedMaterial = TinderMaterial != null ? TinderMaterial : DemonstrationMaterial;
                return visual;
            }
            else if (string.Equals(itemTypeId, "wood-fallen-branch", StringComparison.Ordinal))
            {
                var visual = new GameObject("Visual");
                visual.transform.SetParent(parent, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                var mesh = CityLife.Wood.FallenWoodGenerator.GenerateMesh(CityLife.Wood.FallenWoodProfile.CreateBranchPreset(), 0x4A8C193Eu, out _);
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                var rend = visual.AddComponent<MeshRenderer>();
                rend.sharedMaterial = WoodMaterial != null ? WoodMaterial : DemonstrationMaterial;
                return visual;
            }
            else if (string.Equals(itemTypeId, "tool-stone-blade", StringComparison.Ordinal) ||
                     string.Equals(itemTypeId, "tool-fire-striker", StringComparison.Ordinal))
            {
                var visual = new GameObject("Visual");
                visual.transform.SetParent(parent, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                var shape = string.Equals(itemTypeId, "tool-stone-blade", StringComparison.Ordinal)
                    ? CityLife.Stones.StoneShapeKind.FlatSlab
                    : CityLife.Stones.StoneShapeKind.Handstone;
                var mesh = CityLife.Stones.StoneMeshGenerator.GenerateMesh(shape, 303, 0, 0.5f, true);
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                var rend = visual.AddComponent<MeshRenderer>();
                rend.sharedMaterial = StoneMaterial != null ? StoneMaterial : DemonstrationMaterial;
                return visual;
            }
            else
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Visual";
                var vCol = visual.GetComponent<Collider>();
                if (vCol != null) DestroyImmediate(vCol);
                visual.transform.SetParent(parent, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = new Vector3(def.dimensions.width, def.dimensions.height, def.dimensions.depth);

                var renderer = visual.GetComponent<MeshRenderer>();
                if (renderer != null && DemonstrationMaterial != null)
                {
                    renderer.sharedMaterial = DemonstrationMaterial;
                }
                return visual;
            }
        }

        private void CreateStarterPhysicalItem(string itemId, string itemTypeId, Vector3 localOffset, UnityEngine.SceneManagement.Scene targetScene)
        {
            if (Catalog == null || !Catalog.TryGet(itemTypeId, out var def))
            {
                Debug.LogWarning($"[PhysicalItemBootstrap] Starter item {itemId} type {itemTypeId} not found in authoritative catalog.");
                return;
            }

            Vector3 spawnPos = Brain != null ? Brain.SpawnPosition : Vector3.zero;
            Vector3 itemPos = spawnPos + localOffset;
            if (Physics.Raycast(itemPos + Vector3.up * 2f, Vector3.down, out var hit, 10f, (1 << 8) | (1 << 10)))
            {
                itemPos.y = hit.point.y + def.dimensions.height * 0.5f;
            }

            var go = new GameObject(itemId);
            go.layer = 0;
            if (targetScene.IsValid())
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, targetScene);
            }

            go.transform.position = itemPos;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(def.dimensions.width, def.dimensions.height, def.dimensions.depth);
            box.center = Vector3.zero;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = def.massKg;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            CreateVisualForItem(go.transform, itemTypeId, def);

            var approachObj = new GameObject(itemId + " approach");
            approachObj.transform.SetParent(go.transform, false);
            approachObj.transform.localPosition = Vector3.zero;

            var interactable = go.AddComponent<NpcInteractable>();
            interactable.StableId = itemId;
            interactable.WorldId = Brain != null ? Brain.InstanceWorldId : "starfall.coastal-canyon.v1";
            interactable.Kind = NpcObjectKind.Item;
            interactable.Permission = true;
            interactable.Approach = approachObj.transform;

            var phys = go.AddComponent<PhysicalItem>();
            phys.itemId = itemId;
            phys.itemTypeId = itemTypeId;
            phys.massKg = def.massKg;
            phys.dimensions = def.dimensions;
            phys.isAnchored = false;
            phys.ConfigureComponents();
            phys.Bind(Model, interactable.WorldId, Model.GenerationId);
            phys.RecordInitialRendererStates();

            bool registered = Model.RegisterItem(itemId, itemTypeId, ItemLocationKind.Free, go.transform.position, go.transform.rotation);
            if (registered)
            {
                bindings.Add(new PhysicalItemRuntimeBinding(itemId, phys, interactable));
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        public PhysicalItem CreatePhysicalItem(string itemId, string itemTypeId, Vector3 worldPosition, Quaternion rotation)
        {
            if (Catalog == null || Model == null) return null;
            if (!Catalog.TryGet(itemTypeId, out var def)) return null;

            var go = new GameObject(itemId);
            go.transform.position = worldPosition;
            go.transform.rotation = rotation;
            go.transform.localScale = Vector3.one;

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(def.dimensions.width, def.dimensions.height, def.dimensions.depth);
            box.center = Vector3.zero;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = def.massKg;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            CreateVisualForItem(go.transform, itemTypeId, def);

            var approachObj = new GameObject(itemId + " approach");
            approachObj.transform.SetParent(go.transform, false);
            approachObj.transform.localPosition = Vector3.zero;

            var interactable = go.AddComponent<NpcInteractable>();
            interactable.StableId = itemId;
            interactable.WorldId = Brain != null ? Brain.InstanceWorldId : "starfall.coastal-canyon.v1";
            interactable.Kind = NpcObjectKind.Item;
            interactable.Permission = true;
            interactable.Approach = approachObj.transform;

            var phys = go.AddComponent<PhysicalItem>();
            phys.itemId = itemId;
            phys.itemTypeId = itemTypeId;
            phys.massKg = def.massKg;
            phys.dimensions = def.dimensions;
            phys.isAnchored = false;
            phys.ConfigureComponents();
            phys.Bind(Model, interactable.WorldId, Model.GenerationId);
            phys.RecordInitialRendererStates();

            bool registered = Model.RegisterItem(itemId, itemTypeId, ItemLocationKind.Free, worldPosition, rotation);
            if (registered)
            {
                bindings.Add(new PhysicalItemRuntimeBinding(itemId, phys, interactable));
                return phys;
            }
            else
            {
                DestroyImmediate(go);
                return null;
            }
        }

        public bool SaveCurrentState(string savePath = null, bool isRecoveryAction = false)
        {
            string stagingPath = null;
            try
            {
                string path = !string.IsNullOrEmpty(savePath) ? savePath : PhysicalSavePath;
                if (string.IsNullOrEmpty(path))
                {
                    path = GetDefaultSavePath(Brain != null ? Brain.InstanceWorldId : null);
                }

                if (string.IsNullOrEmpty(path) || !IsFullyQualifiedPath(path))
                {
                    LastSaveFailed = true;
                    LastSaveError = "invalid-or-non-rooted-path";
                    Debug.LogWarning($"[PhysicalItemBootstrap] SaveCurrentState failed: invalid or non-fully-qualified path '{path}'.");
                    return false;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(path);
                }
                catch (Exception ex)
                {
                    LastSaveFailed = true;
                    LastSaveError = "invalid-or-non-rooted-path";
                    Debug.LogWarning($"[PhysicalItemBootstrap] SaveCurrentState path normalization failed for '{path}': {ex.Message}");
                    return false;
                }

                // Refuse overwriting rejected source file!
                if (SourceSaveRejected && !string.IsNullOrEmpty(RejectedSourcePath))
                {
                    string rejFull = null;
                    try { rejFull = Path.GetFullPath(RejectedSourcePath); } catch { }
                    if (string.Equals(fullPath, rejFull, StringComparison.OrdinalIgnoreCase))
                    {
                        LastSaveFailed = true;
                        LastSaveError = "refused-overwriting-rejected-source";
                        Debug.LogWarning($"[PhysicalItemBootstrap] SaveCurrentState refused: destination '{path}' is the rejected source save file. Rejected source file preserved byte-for-byte.");
                        return false;
                    }
                }

                // Recovery action collision check: NEVER overwrite an unrelated existing sibling!
                // Repeated overwrite allowed ONLY if destination matches the current deliberate activeRecoveryPath.
                if (isRecoveryAction)
                {
                    if (File.Exists(fullPath))
                    {
                        bool isCurrentDeliberateTarget = !string.IsNullOrEmpty(activeRecoveryPath) &&
                            string.Equals(fullPath, Path.GetFullPath(activeRecoveryPath), StringComparison.OrdinalIgnoreCase);
                        if (!isCurrentDeliberateTarget)
                        {
                            LastSaveFailed = true;
                            LastSaveError = "refused-overwriting-existing-sibling";
                            Debug.LogWarning($"[PhysicalItemBootstrap] SaveRecoveryState refused: destination '{path}' already exists as an unrelated sibling file. Preservation enforced.");
                            return false;
                        }
                    }
                }

                if (Model == null)
                {
                    LastSaveFailed = true;
                    LastSaveError = "model-null";
                    Debug.LogWarning("[PhysicalItemBootstrap] SaveCurrentState failed: Model is null.");
                    return false;
                }

                string actorId = Brain != null ? NpcAutonomy.AgentId : "inhabitant-01";
                var payload = ItemPersistence.CreateSnapshot(Model, actorId);
                if (payload == null)
                {
                    LastSaveFailed = true;
                    LastSaveError = "snapshot-failed";
                    Debug.LogWarning("[PhysicalItemBootstrap] SaveCurrentState failed: snapshot creation failed.");
                    return false;
                }

                // Perform atomic write using isolated staging path to ensure atomic no-clobber promotion
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                stagingPath = Path.Combine(dir ?? "", ".staging-" + Guid.NewGuid().ToString("N") + ".json");
                bool staged = ItemPersistence.SaveAtomic(stagingPath, payload);
                if (!staged)
                {
                    LastSaveFailed = true;
                    LastSaveError = "atomic-save-failed";
                    Debug.LogWarning($"[PhysicalItemBootstrap] SaveCurrentState failed: SaveAtomic failed on staging path '{stagingPath}'.");
                    return false;
                }

                if (isRecoveryAction)
                {
                    bool isCurrentDeliberateTarget = !string.IsNullOrEmpty(activeRecoveryPath) &&
                        string.Equals(fullPath, Path.GetFullPath(activeRecoveryPath), StringComparison.OrdinalIgnoreCase);
                    if (File.Exists(fullPath) && !isCurrentDeliberateTarget)
                    {
                        try { File.Delete(stagingPath); } catch { }
                        LastSaveFailed = true;
                        LastSaveError = "refused-overwriting-existing-sibling";
                        return false;
                    }
                }

                if (Brain != null)
                {
                    bool chkOk = FoodConsumptionBridge.TryCommitCoordinatedCheckpoint(Brain, "physical-save", out string chkCode);
                    if (!chkOk)
                    {
                        if (!string.IsNullOrEmpty(stagingPath))
                        {
                            try { if (File.Exists(stagingPath)) File.Delete(stagingPath); } catch { }
                        }
                        LastSaveFailed = true;
                        LastSaveError = "checkpoint-sync-refused: " + chkCode;
                        SaveRejected = true;
                        Debug.LogWarning($"[PhysicalItemBootstrap] SaveCurrentState coordinated checkpoint refused: {chkCode}");
                        return false;
                    }
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(stagingPath, fullPath, fullPath + ".bak");
                }
                else
                {
                    File.Move(stagingPath, fullPath);
                }
                if (isRecoveryAction)
                {
                    activeRecoveryPath = fullPath;
                }

                HasSavedPayload = true;
                PhysicalSavePath = fullPath;
                LastSaveFailed = false;
                LastSaveError = null;
                if (!SourceSaveRejected)
                {
                    SaveRejected = false;
                }
                Debug.Log($"[PhysicalItemBootstrap] Authoritative physical state successfully saved to '{fullPath}'.");
                return true;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(stagingPath))
                {
                    try { if (File.Exists(stagingPath)) File.Delete(stagingPath); } catch { }
                }
                LastSaveFailed = true;
                LastSaveError = "save-preparation-failed";
                Debug.LogWarning($"[PhysicalItemBootstrap] SaveCurrentState caught exception: {ex.Message}");
                return false;
            }
        }

        public IEnumerator RunDiagnosticFlow(string mode, string evidenceDir, string savePath)
        {
            DiagnosticRunning = true;
            DiagnosticStatus = mode + "-launch";

            void RecordStage(string stage)
            {
                DiagnosticStatus = stage;
                Debug.Log($"[PhysicalItemDiagnostic] {stage}");
                if (!string.IsNullOrEmpty(evidenceDir))
                {
                    try
                    {
                        Directory.CreateDirectory(evidenceDir);
                        File.AppendAllText(Path.Combine(evidenceDir, "stages.txt"), DateTime.UtcNow.ToString("o") + " " + stage + Environment.NewLine);
                    }
                    catch { }
                }
            }

            void Fail(string reason)
            {
                DiagnosticStatus = mode + "-failed: " + reason;
                Debug.LogError($"[PhysicalItemDiagnostic] {DiagnosticStatus}");
                if (!string.IsNullOrEmpty(evidenceDir))
                {
                    try
                    {
                        Directory.CreateDirectory(evidenceDir);
                        File.WriteAllText(Path.Combine(evidenceDir, "failed.txt"), DiagnosticStatus + Environment.NewLine);
                        var failReport = "{\n  \"status\": \"FAIL\",\n  \"diagnostic\": \"" + mode + "\",\n  \"error\": \"" + reason.Replace("\"", "\\\"") + "\"\n}\n";
                        File.WriteAllText(Path.Combine(evidenceDir, "summary.json"), failReport);
                    }
                    catch { }
                }
                if (!Application.isEditor)
                {
                    Application.Quit(3);
                }
            }

            void Pass(string diagName)
            {
                DiagnosticStatus = diagName + "-passed";
                Debug.Log($"[PhysicalItemDiagnostic] {DiagnosticStatus}");
                if (!string.IsNullOrEmpty(evidenceDir))
                {
                    try
                    {
                        Directory.CreateDirectory(evidenceDir);
                        File.WriteAllText(Path.Combine(evidenceDir, "passed.txt"), diagName + " PASS" + Environment.NewLine);
                        var passReport = "{\n  \"status\": \"PASS\",\n  \"diagnostic\": \"" + diagName + "\",\n  \"itemId\": \"" + DemonstrationItemId + "\"\n}\n";
                        File.WriteAllText(Path.Combine(evidenceDir, "summary.json"), passReport);
                    }
                    catch { }
                }
            }

            RecordStage(mode + "-launch");
            string matDiag = GetVisualMaterialDiagnostic();
            Debug.Log($"[PhysicalItemDiagnostic] Visual material diagnostic: {matDiag}");
            if (DemonstrationMaterial != null && DemonstrationMaterial.shader != null)
            {
                string sName = DemonstrationMaterial.shader.name.Replace('/', '-').Replace(' ', '_');
                RecordStage($"{mode}-visual-{DemonstrationMaterial.name}-shader-{sName}-supported-{DemonstrationMaterial.shader.isSupported}");
            }
            else
            {
                RecordStage($"{mode}-visual-material-absent");
            }

            // Bounded wait for inhabitant and action readiness (up to 100 fixed updates)
            int waitTicks = 0;
            while (Brain == null || !Brain.Ready || Brain.Actions == null || DemonstrationItem == null)
            {
                waitTicks++;
                if (waitTicks > 100)
                {
                    RecordStage(mode + "-readiness-timeout");
                    Fail("Readiness timeout after 100 ticks");
                    yield break;
                }
                yield return new WaitForFixedUpdate();
            }

            bool wasRunning = Brain.Running;
            Brain.Pause();

            try
            {
                if (string.Equals(mode, "save-carried", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(savePath) || SaveRejected)
                    {
                        RecordStage("save-carried-invalid-save-path");
                        Fail("Missing, non-absolute, or rejected save path");
                        yield break;
                    }

                    for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

                    float dist = Vector3.Distance(Brain.transform.position, DemonstrationItem.transform.position);
                    if (dist > 0.65f)
                    {
                        RecordStage("save-carried-out-of-reach");
                        Fail("Item out of reach: " + dist);
                        yield break;
                    }

                    RecordStage("save-carried-pickup-attempt");
                    var pickupRes = Brain.ExecutePlayerAction(NpcActionKind.Pickup, DemonstrationItemId);
                    if (!pickupRes.success)
                    {
                        RecordStage("save-carried-pickup-failed-" + pickupRes.code);
                        Fail("Pickup failed: " + pickupRes.code);
                        yield break;
                    }
                    RecordStage("save-carried-pickup-passed");

                    for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();

                    if (!DemonstrationItem.IsCarried || Brain.Actions.Held != DemonstrationInteractable ||
                        Vector3.Distance(DemonstrationItem.transform.lossyScale, Vector3.one) > 0.001f)
                    {
                        RecordStage("save-carried-invalid-carried-state");
                        Fail("Item not in valid carried state");
                        yield break;
                    }

                    RecordStage("save-carried-save-attempt");
                    var payload = ItemPersistence.CreateSnapshot(Model, NpcAutonomy.AgentId);
                    bool saved = ItemPersistence.SaveAtomic(savePath, payload);
                    if (!saved)
                    {
                        RecordStage("save-carried-save-failed");
                        Fail("SaveAtomic returned false");
                        yield break;
                    }

                    bool loadBack = ItemPersistence.TryLoad(savePath, Brain.InstanceWorldId, Model.GenerationId, NpcAutonomy.AgentId, Model, out var backPayload);
                    if (!loadBack || backPayload.items.Count != 1 || backPayload.items[0].location != ItemLocationKind.Carried ||
                        !string.Equals(backPayload.items[0].holderActorId, NpcAutonomy.AgentId, StringComparison.Ordinal))
                    {
                        RecordStage("save-carried-verify-disk-failed");
                        Fail("Saved file verification on disk failed");
                        yield break;
                    }

                    RecordStage("save-carried-passed");
                    Pass("save-carried");
                    yield return new WaitForSeconds(0.1f);
                    if (!Application.isEditor) Application.Quit(0);
                }
                else if (string.Equals(mode, "load-carried", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath) || SaveRejected || !HasSavedPayload)
                    {
                        RecordStage("load-carried-missing-or-rejected-save");
                        Fail("Missing, rejected, or un-restored save payload");
                        yield break;
                    }

                    if (!DemonstrationItem.IsCarried || DemonstrationItem.CarriedHand != Brain.Actions.HandTransform ||
                        Brain.Actions.Held != DemonstrationInteractable || DemonstrationInteractable.HeldBy != NpcAutonomy.AgentId ||
                        Vector3.Distance(DemonstrationItem.transform.lossyScale, Vector3.one) > 0.001f)
                    {
                        RecordStage("load-carried-restored-verification-failed");
                        Fail("Restored carried state verification failed");
                        yield break;
                    }
                    RecordStage("load-carried-restored-verified");

                    for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();

                    RecordStage("load-carried-drop-attempt");
                    var dropRes = Brain.ExecutePlayerAction(NpcActionKind.Drop, DemonstrationItemId);
                    if (!dropRes.success)
                    {
                        RecordStage("load-carried-drop-failed-" + dropRes.code);
                        Fail("Drop failed: " + dropRes.code);
                        yield break;
                    }
                    RecordStage("load-carried-drop-passed");

                    if (DemonstrationItem.IsCarried || DemonstrationItem.Body == null || DemonstrationItem.Body.isKinematic ||
                        DemonstrationItem.ItemCollider == null || DemonstrationItem.ItemCollider.isTrigger)
                    {
                        RecordStage("load-carried-dynamic-physics-failed");
                        Fail("Dynamic physics restoration failed after drop");
                        yield break;
                    }

                    bool settled = false;
                    int consecutiveSettled = 0;
                    for (int i = 0; i < 250; i++)
                    {
                        yield return new WaitForFixedUpdate();
                        if (DemonstrationItem.Body != null && !DemonstrationItem.IsCarried)
                        {
                            float speed = DemonstrationItem.Body.linearVelocity.magnitude;
                            float angSpeed = DemonstrationItem.Body.angularVelocity.magnitude;
                            if (speed <= 0.03f && angSpeed <= 0.05236f)
                            {
                                consecutiveSettled++;
                                if (consecutiveSettled >= 10)
                                {
                                    settled = true;
                                    break;
                                }
                            }
                            else
                            {
                                consecutiveSettled = 0;
                            }
                        }
                    }

                    if (!settled)
                    {
                        RecordStage("load-carried-settle-timeout");
                        Fail("Settlement timeout after 250 ticks");
                        yield break;
                    }
                    RecordStage("load-carried-settle-passed");

                    Vector3 settledPos = DemonstrationItem.transform.position;
                    bool driftPassed = true;
                    for (int i = 0; i < 100; i++)
                    {
                        yield return new WaitForFixedUpdate();
                        if (Vector3.Distance(DemonstrationItem.transform.position, settledPos) > 0.02f)
                        {
                            driftPassed = false;
                            break;
                        }
                    }

                    if (!driftPassed)
                    {
                        RecordStage("load-carried-drift-exceeded");
                        Fail("Rest drift exceeded 0.02m");
                        yield break;
                    }
                    RecordStage("load-carried-drift-passed");

                    if (!Model.TryGetItem(DemonstrationItemId, out var snap) || snap.location != ItemLocationKind.Free)
                    {
                        RecordStage("load-carried-model-mismatch");
                        Fail("Model location mismatch: expected Free");
                        yield break;
                    }

                    RecordStage("load-carried-passed");
                    Pass("load-carried");
                    yield return new WaitForSeconds(0.1f);
                    if (!Application.isEditor) Application.Quit(0);
                }
                else if (string.Equals(mode, "drop-save-free", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(savePath) || SaveRejected)
                    {
                        RecordStage("drop-save-free-invalid-save-path");
                        Fail("Missing, non-absolute, or rejected save path");
                        yield break;
                    }

                    for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

                    float dist = Vector3.Distance(Brain.transform.position, DemonstrationItem.transform.position);
                    if (dist > 0.65f)
                    {
                        RecordStage("drop-save-free-out-of-reach");
                        Fail("Item out of reach: " + dist);
                        yield break;
                    }

                    RecordStage("drop-save-free-pickup-attempt");
                    var pickupRes = Brain.ExecutePlayerAction(NpcActionKind.Pickup, DemonstrationItemId);
                    if (!pickupRes.success)
                    {
                        RecordStage("drop-save-free-pickup-failed-" + pickupRes.code);
                        Fail("Pickup failed: " + pickupRes.code);
                        yield break;
                    }
                    RecordStage("drop-save-free-pickup-passed");

                    for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();

                    RecordStage("drop-save-free-drop-attempt");
                    var dropRes = Brain.ExecutePlayerAction(NpcActionKind.Drop, DemonstrationItemId);
                    if (!dropRes.success)
                    {
                        RecordStage("drop-save-free-drop-failed-" + dropRes.code);
                        Fail("Drop failed: " + dropRes.code);
                        yield break;
                    }
                    RecordStage("drop-save-free-drop-passed");

                    bool settled = false;
                    int consecutiveSettled = 0;
                    for (int i = 0; i < 250; i++)
                    {
                        yield return new WaitForFixedUpdate();
                        if (DemonstrationItem.Body != null && !DemonstrationItem.IsCarried)
                        {
                            float speed = DemonstrationItem.Body.linearVelocity.magnitude;
                            float angSpeed = DemonstrationItem.Body.angularVelocity.magnitude;
                            if (speed <= 0.03f && angSpeed <= 0.05236f)
                            {
                                consecutiveSettled++;
                                if (consecutiveSettled >= 10)
                                {
                                    settled = true;
                                    break;
                                }
                            }
                            else
                            {
                                consecutiveSettled = 0;
                            }
                        }
                    }

                    if (!settled)
                    {
                        RecordStage("drop-save-free-settle-timeout");
                        Fail("Settlement timeout after 250 ticks");
                        yield break;
                    }
                    RecordStage("drop-save-free-settle-passed");

                    Vector3 settledPos = DemonstrationItem.transform.position;
                    bool driftPassed = true;
                    for (int i = 0; i < 100; i++)
                    {
                        yield return new WaitForFixedUpdate();
                        if (Vector3.Distance(DemonstrationItem.transform.position, settledPos) > 0.02f)
                        {
                            driftPassed = false;
                            break;
                        }
                    }

                    if (!driftPassed)
                    {
                        RecordStage("drop-save-free-drift-exceeded");
                        Fail("Rest drift exceeded 0.02m");
                        yield break;
                    }
                    RecordStage("drop-save-free-drift-passed");

                    RecordStage("drop-save-free-save-attempt");
                    var payload = ItemPersistence.CreateSnapshot(Model, NpcAutonomy.AgentId);
                    bool saved = ItemPersistence.SaveAtomic(savePath, payload);
                    if (!saved)
                    {
                        RecordStage("drop-save-free-save-failed");
                        Fail("SaveAtomic returned false");
                        yield break;
                    }

                    bool loadBack = ItemPersistence.TryLoad(savePath, Brain.InstanceWorldId, Model.GenerationId, NpcAutonomy.AgentId, Model, out var backPayload);
                    if (!loadBack || backPayload.items.Count != 1 || backPayload.items[0].location != ItemLocationKind.Free ||
                        !string.IsNullOrEmpty(backPayload.items[0].holderActorId))
                    {
                        RecordStage("drop-save-free-verify-disk-failed");
                        Fail("Saved file verification on disk failed");
                        yield break;
                    }

                    RecordStage("drop-save-free-passed");
                    Pass("drop-save-free");
                    yield return new WaitForSeconds(0.1f);
                    if (!Application.isEditor) Application.Quit(0);
                }
                else if (string.Equals(mode, "load-free", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath) || SaveRejected || !HasSavedPayload)
                    {
                        RecordStage("load-free-missing-or-rejected-save");
                        Fail("Missing, rejected, or un-restored save payload");
                        yield break;
                    }

                    if (DemonstrationItem.IsCarried || Brain.Actions.Held != null || !string.IsNullOrEmpty(DemonstrationInteractable.HeldBy) ||
                        DemonstrationItem.Body == null || DemonstrationItem.Body.isKinematic || DemonstrationItem.ItemCollider == null || DemonstrationItem.ItemCollider.isTrigger)
                    {
                        RecordStage("load-free-restored-verification-failed");
                        Fail("Restored free state verification failed");
                        yield break;
                    }
                    RecordStage("load-free-restored-verified");

                    Vector3 initialRestPos = DemonstrationItem.transform.position;
                    bool stable = true;
                    for (int i = 0; i < 50; i++)
                    {
                        yield return new WaitForFixedUpdate();
                        if (Vector3.Distance(DemonstrationItem.transform.position, initialRestPos) > 0.02f)
                        {
                            stable = false;
                            break;
                        }
                    }

                    if (!stable)
                    {
                        RecordStage("load-free-rest-unstable");
                        Fail("Item moved during initial 50 ticks of restored rest");
                        yield break;
                    }
                    RecordStage("load-free-rest-stable");

                    RecordStage("load-free-approach-start");
                    Transform approachTarget = DemonstrationInteractable.Approach != null
                        ? DemonstrationInteractable.Approach
                        : DemonstrationItem.transform;

                    Brain.SetPossession(true);
                    int stepTicks = 0;
                    const int maxStepTicks = 100;

                    float approachDist = Vector3.Distance(Brain.transform.position, approachTarget.position);
                    float eyeDist = Vector3.Distance(Brain.transform.position + Vector3.up, DemonstrationInteractable.SightPoint);

                    while ((approachDist > 0.50f || eyeDist > 1.50f) && stepTicks < maxStepTicks)
                    {
                        Vector3 toApproach = approachTarget.position - Brain.transform.position;
                        toApproach.y = 0f;
                        Vector3 dir = toApproach.sqrMagnitude > 0.0001f ? toApproach.normalized : Vector3.zero;
                        Brain.ManualDirection = dir;

                        Vector3 prevPos = Brain.transform.position;
                        yield return new WaitForFixedUpdate();

                        // Fallback step if NpcAutonomy.FixedUpdate was not ticking in current context
                        if (Vector3.Distance(Brain.transform.position, prevPos) < 0.0001f && Brain.Actor != null)
                        {
                            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
                            Vector3 motion = Brain.TerrainNavigation != null
                                ? Brain.TerrainNavigation.ConstrainMotion(Brain.transform.position, dir, Brain.Actor.WalkSpeed * dt)
                                : dir;
                            Brain.Actor.Step(motion, dt);
                        }

                        approachDist = Vector3.Distance(Brain.transform.position, approachTarget.position);
                        eyeDist = Vector3.Distance(Brain.transform.position + Vector3.up, DemonstrationInteractable.SightPoint);
                        stepTicks++;
                    }

                    // Stop input and motion before guarded pickup
                    Brain.ManualDirection = Vector3.zero;
                    Brain.SetPossession(false);
                    Brain.Pause();
                    if (Brain.Actor != null)
                    {
                        Brain.Actor.CancelGesture();
                        Brain.Actor.Step(Vector3.zero, Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f);
                    }
                    Physics.SyncTransforms();

                    // Settle for 5 fixed frames so any residual velocity is zero
                    for (int i = 0; i < 5; i++)
                    {
                        yield return new WaitForFixedUpdate();
                    }

                    approachDist = Vector3.Distance(Brain.transform.position, approachTarget.position);
                    eyeDist = Vector3.Distance(Brain.transform.position + Vector3.up, DemonstrationInteractable.SightPoint);

                    if (approachDist > 0.65f || eyeDist > 1.7f)
                    {
                        RecordStage("load-free-pickup-out-of-reach");
                        Fail($"Restored item out of reach: approach={approachDist:F4} (limit 0.65), eye={eyeDist:F4} (limit 1.7)");
                        yield break;
                    }
                    RecordStage("load-free-approach-passed");

                    RecordStage("load-free-pickup-attempt");
                    var pickupRes = Brain.ExecutePlayerAction(NpcActionKind.Pickup, DemonstrationItemId);
                    if (!pickupRes.success)
                    {
                        RecordStage("load-free-pickup-failed-" + pickupRes.code);
                        Fail("Pickup of restored free item failed: " + pickupRes.code);
                        yield break;
                    }
                    RecordStage("load-free-pickup-passed");

                    if (!DemonstrationItem.IsCarried || Brain.Actions.Held != DemonstrationInteractable ||
                        Vector3.Distance(DemonstrationItem.transform.lossyScale, Vector3.one) > 0.001f)
                    {
                        RecordStage("load-free-carried-verification-failed");
                        Fail("Carried state verification failed after re-pickup");
                        yield break;
                    }

                    RecordStage("load-free-passed");
                    Pass("load-free");
                    yield return new WaitForSeconds(0.1f);
                    if (!Application.isEditor) Application.Quit(0);
                }
                else
                {
                    // Default in-world combined flow (backward-compatible)
                    for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

                    float dist = Vector3.Distance(Brain.transform.position, DemonstrationItem.transform.position);
                    if (dist > 0.65f)
                    {
                        RecordStage("in-world-pickup-out-of-reach");
                        Fail("Item out of reach: " + dist);
                        yield break;
                    }

                    RecordStage("in-world-pickup-attempt");
                    var pickupRes = Brain.ExecutePlayerAction(NpcActionKind.Pickup, DemonstrationItemId);
                    if (!pickupRes.success)
                    {
                        string diag = DemonstrationItem != null ? (" (" + DemonstrationItem.GetDiagnosticMeasurements() + ")") : "";
                        RecordStage("in-world-pickup-failed-" + pickupRes.code + diag);
                        Fail("Pickup failed: " + pickupRes.code);
                        yield break;
                    }
                    RecordStage("in-world-pickup-passed");

                    for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();

                    RecordStage("in-world-drop-attempt");
                    var dropRes = Brain.ExecutePlayerAction(NpcActionKind.Drop, DemonstrationItemId);
                    if (!dropRes.success)
                    {
                        string diag = DemonstrationItem != null ? (" (" + DemonstrationItem.GetDiagnosticMeasurements() + ")") : "";
                        RecordStage("in-world-drop-failed-" + dropRes.code + diag);
                        Fail("Drop failed: " + dropRes.code);
                        yield break;
                    }
                    RecordStage("in-world-drop-passed");

                    bool settled = false;
                    int consecutiveSettled = 0;
                    for (int i = 0; i < 250; i++)
                    {
                        yield return new WaitForFixedUpdate();
                        if (DemonstrationItem.Body != null && !DemonstrationItem.IsCarried)
                        {
                            float speed = DemonstrationItem.Body.linearVelocity.magnitude;
                            float angSpeed = DemonstrationItem.Body.angularVelocity.magnitude;
                            if (speed <= 0.03f && angSpeed <= 0.05236f)
                            {
                                consecutiveSettled++;
                                if (consecutiveSettled >= 10)
                                {
                                    settled = true;
                                    break;
                                }
                            }
                            else
                            {
                                consecutiveSettled = 0;
                            }
                        }
                    }

                    if (!settled)
                    {
                        RecordStage("in-world-settle-timeout");
                        Fail("Settlement timeout after 250 ticks");
                        yield break;
                    }
                    RecordStage("in-world-settle-passed");

                    Vector3 settledPos = DemonstrationItem.transform.position;
                    bool driftPassed = true;
                    for (int i = 0; i < 100; i++)
                    {
                        yield return new WaitForFixedUpdate();
                        if (Vector3.Distance(DemonstrationItem.transform.position, settledPos) > 0.02f)
                        {
                            driftPassed = false;
                            break;
                        }
                    }

                    if (!driftPassed)
                    {
                        RecordStage("in-world-settle-drift-exceeded");
                        Fail("Rest drift exceeded 0.02m");
                        yield break;
                    }
                    RecordStage("in-world-drift-passed");

                    bool modelValid = Model.TryGetItem(DemonstrationItemId, out var snap) && snap.location == ItemLocationKind.Free;
                    if (!modelValid)
                    {
                        RecordStage("in-world-model-mismatch");
                        Fail("Model state mismatch: expected Free");
                        yield break;
                    }

                    if (!string.IsNullOrEmpty(savePath) && !SaveRejected)
                    {
                        RecordStage("in-world-save-attempt");
                        var payload = ItemPersistence.CreateSnapshot(Model, NpcAutonomy.AgentId);
                        if (ItemPersistence.SaveAtomic(savePath, payload))
                        {
                            RecordStage("in-world-save-passed");
                        }
                    }

                    RecordStage("in-world-diagnostic-passed");
                    Pass("in-world-pickup-drop");
                    yield return new WaitForSeconds(0.1f);
                    if (!Application.isEditor) Application.Quit(0);
                }
            }
            finally
            {
                if (wasRunning && Brain != null)
                {
                    Brain.Running = true;
                }
                DiagnosticRunning = false;
            }
        }
    }
}
