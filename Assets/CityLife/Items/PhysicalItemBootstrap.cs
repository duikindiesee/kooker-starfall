using System;
using System.Collections;
using System.IO;
using UnityEngine;
using CityLife.World;

namespace CityLife.Items
{
    /// <summary>
    /// Bounded runtime bootstrap for physical item foundation integration in the live canyon world.
    /// Explicitly authors and owns a movable demonstration physical item, binds authoritative ItemModel and
    /// PhysicalAuthority to NpcActionApi, and synchronizes dynamic physics transforms in FixedUpdate.
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
        public bool HasSavedPayload { get; private set; }

        private PhysicalSavePayload savedPayload;
        private bool restoreAttempted;
        private bool itemCreated;

        private void Awake()
        {
            if (Brain == null) Brain = GetComponent<NpcAutonomy>();
            if (Brain != null && Brain.PhysicalItems == null) Brain.PhysicalItems = this;

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-physicalSave", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string candidatePath = args[i + 1];
                    if (!string.IsNullOrEmpty(candidatePath) && Path.IsPathRooted(candidatePath))
                    {
                        PhysicalSavePath = Path.GetFullPath(candidatePath);
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
            Model = new ItemModel(worldId, "gen-01");
            Authority = new BasicItemActionAuthority();

            var def = new ItemDefinition
            {
                itemTypeId = DemonstrationItemTypeId,
                massKg = DemonstrationItemMassKg,
                dimensions = new PhysicalDimensions(DemonstrationItemDimensions.x, DemonstrationItemDimensions.y, DemonstrationItemDimensions.z),
                isContainer = false,
                isAnchored = false,
                requiresSupportToPlace = false
            };
            Model.RegisterDefinition(def);

            SetupDemonstrationItem();

            if (!string.IsNullOrEmpty(PhysicalSavePath))
            {
                if (File.Exists(PhysicalSavePath))
                {
                    bool loaded = ItemPersistence.TryLoad(PhysicalSavePath, worldId, "gen-01", actorId, Model, out savedPayload);
                    if (!loaded)
                    {
                        SaveRejected = true;
                        Debug.LogWarning($"[PhysicalItemBootstrap] Rejected invalid/foreign save file at '{PhysicalSavePath}'. Prior file preserved untouched; fresh start initialized.");
                    }
                    else
                    {
                        HasSavedPayload = true;
                        Debug.Log($"[PhysicalItemBootstrap] Valid physical save payload loaded from '{PhysicalSavePath}'.");
                    }
                }
                else
                {
                    Debug.Log($"[PhysicalItemBootstrap] Physical save path '{PhysicalSavePath}' not found on disk. Fresh start initialized.");
                }
            }
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

        public void OnActionsCreated(NpcActionApi actions)
        {
            if (actions == null) return;
            actions.PhysicalModel = Model;
            actions.PhysicalAuthority = Authority;

            if (HasSavedPayload && savedPayload != null && !restoreAttempted)
            {
                restoreAttempted = true;
                bool restored = ItemPersistence.RestoreRuntime(savedPayload, Model, actions, DemonstrationItem, DemonstrationInteractable);
                if (!restored)
                {
                    Debug.LogError("[PhysicalItemBootstrap] Failed to restore runtime state from save payload.");
                }
                else
                {
                    Debug.Log("[PhysicalItemBootstrap] Successfully restored runtime physical state from save payload.");
                }
            }
        }

        private void Update()
        {
            if (Brain != null && Brain.Actions != null && Brain.Actions.PhysicalModel == null)
            {
                OnActionsCreated(Brain.Actions);
            }
        }

        private void FixedUpdate()
        {
            if (Brain == null || Brain.Actions == null || Model == null || DemonstrationItem == null || DemonstrationInteractable == null)
                return;
            if (DemonstrationItem.gameObject != DemonstrationInteractable.gameObject)
                return;
            if (!DemonstrationItem.gameObject.scene.IsValid() || DemonstrationItem.gameObject.scene != Brain.gameObject.scene)
                return;
            if (DemonstrationInteractable.WorldId != Brain.InstanceWorldId)
                return;
            if (!DemonstrationItem.IsBoundTo(Model, Brain.InstanceWorldId, Model.GenerationId))
                return;

            Brain.Actions.SyncFreeTransform(DemonstrationItemId);
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
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            var vCol = visual.GetComponent<Collider>();
            if (vCol != null) DestroyImmediate(vCol);
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = DemonstrationItemDimensions;

            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                DemonstrationRenderer = renderer;
                if (DemonstrationMaterial != null)
                {
                    renderer.sharedMaterial = DemonstrationMaterial;
                }
            }

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
            DemonstrationItem = phys;

            Model.RegisterItem(DemonstrationItemId, DemonstrationItemTypeId, ItemLocationKind.Free, go.transform.position, go.transform.rotation);
            itemCreated = true;
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
