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
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class PhysicalItemBootstrap : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public ItemModel Model { get; private set; }
        public IItemActionAuthority Authority { get; private set; }
        public PhysicalItem DemonstrationItem { get; private set; }
        public NpcInteractable DemonstrationInteractable { get; private set; }

        public string DemonstrationItemId = "canyon-artifact-01";
        public string DemonstrationItemTypeId = "canyon-stone";
        public float DemonstrationItemMassKg = 2.5f;
        public Vector3 DemonstrationItemDimensions = new Vector3(0.25f, 0.25f, 0.25f);

        public bool DiagnosticRunning { get; private set; }
        public string DiagnosticStatus { get; private set; } = "idle";

        private bool itemCreated;

        private void Awake()
        {
            if (Brain == null) Brain = GetComponent<NpcAutonomy>();
            if (Brain != null && Brain.PhysicalItems == null) Brain.PhysicalItems = this;

            string worldId = Brain != null ? Brain.InstanceWorldId : "starfall.coastal-canyon.v1";
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
        }

        private void Start()
        {
            if (Brain != null && Brain.Actions != null)
            {
                OnActionsCreated(Brain.Actions);
            }

            string[] args = Environment.GetCommandLineArgs();
            bool runDiag = false;
            string evidenceDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-physicalInWorldDiagnostic", StringComparison.OrdinalIgnoreCase))
                {
                    runDiag = true;
                }
                else if ((string.Equals(args[i], "-physicalEvidence", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(args[i], "-physicalItemEvidence", StringComparison.OrdinalIgnoreCase)) &&
                         i + 1 < args.Length)
                {
                    evidenceDir = args[i + 1];
                }
            }

            if (runDiag)
            {
                StartCoroutine(RunInWorldDiagnostic(evidenceDir));
            }
        }

        public void OnActionsCreated(NpcActionApi actions)
        {
            if (actions == null) return;
            actions.PhysicalModel = Model;
            actions.PhysicalAuthority = Authority;
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

        public IEnumerator RunInWorldDiagnostic(string evidenceDir)
        {
            DiagnosticRunning = true;
            DiagnosticStatus = "in-world-launch";

            void RecordStage(string stage)
            {
                DiagnosticStatus = stage;
                if (!string.IsNullOrEmpty(evidenceDir))
                {
                    Directory.CreateDirectory(evidenceDir);
                    File.AppendAllText(Path.Combine(evidenceDir, "stages.txt"), DateTime.UtcNow.ToString("o") + " " + stage + Environment.NewLine);
                }
            }

            RecordStage("in-world-launch");

            // Bounded wait for readiness (up to 100 fixed updates)
            int waitTicks = 0;
            while (Brain == null || !Brain.Ready || Brain.Actions == null || DemonstrationItem == null)
            {
                waitTicks++;
                if (waitTicks > 100)
                {
                    RecordStage("in-world-readiness-timeout");
                    yield break;
                }
                yield return new WaitForFixedUpdate();
            }

            // Capture running state and pause AFTER readiness and ResetState have completed
            bool wasRunning = Brain.Running;
            Brain.Pause();

            try
            {
                // Initial settlement wait: 10 fixed updates
                for (int i = 0; i < 10; i++)
                {
                    yield return new WaitForFixedUpdate();
                }

                // Honest reach verification: must be within reach (<= 0.65m) without any repositioning/teleport
                float dist = Vector3.Distance(Brain.transform.position, DemonstrationItem.transform.position);
                if (dist > 0.65f)
                {
                    RecordStage("in-world-pickup-out-of-reach");
                    yield break;
                }

                RecordStage("in-world-pickup-attempt");
                var pickupRes = Brain.ExecutePlayerAction(NpcActionKind.Pickup, DemonstrationItemId);
                if (!pickupRes.success)
                {
                    string diag = DemonstrationItem != null ? (" (" + DemonstrationItem.GetDiagnosticMeasurements() + ")") : "";
                    RecordStage("in-world-pickup-failed-" + pickupRes.code + diag);
                    yield break;
                }
                RecordStage("in-world-pickup-passed");

                // Natural carry wait: 20 fixed updates (0.4s)
                for (int i = 0; i < 20; i++)
                {
                    yield return new WaitForFixedUpdate();
                }

                RecordStage("in-world-drop-attempt");
                var dropRes = Brain.ExecutePlayerAction(NpcActionKind.Drop, DemonstrationItemId);
                if (!dropRes.success)
                {
                    string diag = DemonstrationItem != null ? (" (" + DemonstrationItem.GetDiagnosticMeasurements() + ")") : "";
                    RecordStage("in-world-drop-failed-" + dropRes.code + diag);
                    yield break;
                }
                RecordStage("in-world-drop-passed");

                // Natural falling, contact, and settling wait: budget <= 5.0s (250 ticks)
                // Thresholds: linear <= 0.03m/s, angular <= 3 deg/s (0.05236 rad/s) continuously for 10 ticks
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
                    yield break;
                }
                RecordStage("in-world-settle-passed");

                // Post-settle drift observation: <= 0.02m over 2.0s (100 ticks)
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
                    yield break;
                }
                RecordStage("in-world-drift-passed");

                // Authoritative ItemModel state verification
                bool modelValid = Model.TryGetItem(DemonstrationItemId, out var snap) &&
                                  snap.location == ItemLocationKind.Free;

                if (!modelValid)
                {
                    RecordStage("in-world-model-mismatch");
                    yield break;
                }

                RecordStage("in-world-diagnostic-passed");

                if (!string.IsNullOrEmpty(evidenceDir))
                {
                    Directory.CreateDirectory(evidenceDir);
                    var summary = "{\n  \"status\": \"PASS\",\n  \"diagnostic\": \"in-world-pickup-drop\",\n  \"itemId\": \"" + DemonstrationItemId + "\",\n  \"pickupCode\": \"" + pickupRes.code + "\",\n  \"dropCode\": \"" + dropRes.code + "\",\n  \"settled\": true\n}\n";
                    File.WriteAllText(Path.Combine(evidenceDir, "summary.json"), summary);
                    File.WriteAllText(Path.Combine(evidenceDir, "passed.txt"), "in-world-pickup-drop PASS\n");
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
