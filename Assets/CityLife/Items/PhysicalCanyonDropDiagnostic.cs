using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using CityLife.World;

namespace CityLife.Items
{
    /// <summary>
    /// Bounded opt-in diagnostic for scripted guarded canyon pickup, walk, and drop on real 12..18° terrain slope.
    /// Opt-in flag: -physicalCanyonDropEvidence <absolute empty output dir>
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PhysicalCanyonDropDiagnostic : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnSceneLoaded()
        {
            string evidenceDir = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-physicalCanyonDropEvidence", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string candidate = args[i + 1];
                    if (!string.IsNullOrEmpty(candidate) && Path.IsPathRooted(candidate))
                        evidenceDir = Path.GetFullPath(candidate);
                }
            }

            if (string.IsNullOrEmpty(evidenceDir)) return;

            // Reject non-empty evidence directory before attach
            if (Directory.Exists(evidenceDir))
            {
                try
                {
                    if (Directory.GetFileSystemEntries(evidenceDir).Length > 0)
                    {
                        Debug.LogError($"[PhysicalCanyonDropDiagnostic] Evidence directory '{evidenceDir}' is not empty. Attachment rejected.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PhysicalCanyonDropDiagnostic] Failed inspecting evidence directory '{evidenceDir}': {ex.Message}");
                    return;
                }
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(evidenceDir);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PhysicalCanyonDropDiagnostic] Failed to create evidence directory '{evidenceDir}': {ex.Message}");
                    return;
                }
            }

            var host = new GameObject("PHYSICAL CANYON DROP DIAGNOSTIC");
            host.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(host);
            var diag = host.AddComponent<PhysicalCanyonDropDiagnostic>();
            diag.EvidenceDirectory = evidenceDir;
        }

        public string EvidenceDirectory { get; set; }

        private PhysicalItemBootstrap bootstrap;
        private string diagnosticStatus = "INITIALIZING";
        private string diagnosticState = "Waiting for scene";
        private string failureReason;

        private bool originalBrainRunning;
        private bool originalBrainPossessed;
        private Vector3 originalBrainManualDirection;
        private bool originalBrainStateCaptured;

        private bool observeAutoSync;
        private int autoSyncChecks;
        private int autoSyncFailures;
        private float maxAutoSyncPosErrorM;
        private float maxAutoSyncRotErrorDeg;

        private float totalWalkDistanceM;
        private float straightLineDisplacementM;
        private float arrivalSlopeDeg;
        private float actualDropSlopeDeg;
        private float currentLinSpeed;
        private bool currentInContact;
        private float currentPen;

        private struct SlopeCandidate
        {
            public int dirIndex;
            public float angleDeg, radius, slopeDeg;
            public Vector3 rawPos, floor, normal;
            public bool walkable, dry;
        }

        [Serializable]
        public sealed class CanyonDropSummary
        {
            public string schema = "starfall.physical-canyon-drop-diagnostic.v1";
            public string status;
            public string scope = "Scripted guarded canyon drop diagnostic on real terrain mesh collider";
            public string failureReason;
            public string worldId;
            public string generationId;
            public string actorId;
            public string itemId;
            public string itemTypeId;
            public float itemMassKg;
            public Vector3 itemDimensions;

            public string hardwareProcessor;
            public string hardwareGpu;
            public string hardwareOs;
            public string hardwareDeviceModel;
            public string applicationVersion;

            public float physicsGravityX;
            public float physicsGravityY;
            public float physicsGravityZ;
            public float fixedDeltaTime;
            public string bodyCollisionDetectionMode;

            public Vector3 actorStartPos;
            public Vector3 actorArrivalPos;
            public float totalWalkDistanceM;
            public float straightLineDisplacementM;
            public float routeDurationSeconds;

            public int totalRadialSamplesScanned;
            public int slopeCandidatesFound;
            public int routeAttemptsCount;
            public float chosenCandidateSlopeDeg;
            public Vector3 chosenCandidatePos;

            public Vector3 arrivalTerrainNormal;
            public float arrivalTerrainSlopeDeg;
            public string terrainColliderName;
            public string terrainColliderType;
            public string terrainAttachedRigidbody;

            public bool pickupSuccess;
            public string pickupCode;
            public bool dropSuccess;
            public string dropCode;

            public bool contactAchieved;
            public float firstContactTimeSeconds;
            public bool settled;
            public float settleDurationSeconds;
            public bool observationCompleted;
            public float maxObservationDisplacementM;
            public float maxObservationLinearSpeedMPerS;
            public float maxObservationAngularSpeedDegPerS;
            public bool observationContactLost;

            public float maxPenetrationM;
            public bool penetrationExceeded;

            public float finalLocalSlopeDeg;
            public bool slopeWithinRange12To18;

            public int modelSyncCheckCount;
            public int modelSyncFailures;
            public float maxModelPosLagM;
            public float maxModelRotLagDeg;

            public string pickupScreenshot = "none";
            public string arrivalScreenshot = "none";
            public string dropEndScreenshot = "none";
            public string terminalScreenshot = "none";
        }

        private sealed class CanyonContactObserver : MonoBehaviour
        {
            public Collider ExpectedCollider;
            public bool InContact { get; private set; }
            public Vector3 LastNormal { get; private set; }
            public Vector3 LastPoint { get; private set; }
            public float LastMinSeparation { get; private set; }
            public float LastCallbackTime { get; private set; } = -1f;

            private void OnCollisionEnter(Collision col) => UpdateContact(col);
            private void OnCollisionStay(Collision col) => UpdateContact(col);
            private void OnCollisionExit(Collision col)
            {
                if (ExpectedCollider == null || col.collider == ExpectedCollider)
                    InContact = false;
            }

            private void UpdateContact(Collision col)
            {
                if (ExpectedCollider != null && col.collider != ExpectedCollider)
                    return;

                InContact = true;
                LastCallbackTime = Time.fixedTime;
                if (col.contactCount > 0)
                {
                    float minSep = float.MaxValue;
                    Vector3 norm = Vector3.zero;
                    Vector3 pt = Vector3.zero;
                    for (int i = 0; i < col.contactCount; i++)
                    {
                        var c = col.GetContact(i);
                        if (c.separation < minSep)
                        {
                            minSep = c.separation;
                            norm = c.normal;
                            pt = c.point;
                        }
                    }
                    LastNormal = norm;
                    LastPoint = pt;
                    LastMinSeparation = minSep;
                }
            }
        }

        private void FixedUpdate()
        {
            if (!observeAutoSync || bootstrap == null || bootstrap.Model == null || bootstrap.DemonstrationItem == null || bootstrap.Brain == null)
                return;

            autoSyncChecks++;
            string itemId = bootstrap.DemonstrationItemId;
            string itemTypeId = bootstrap.DemonstrationItemTypeId;
            Vector3 itemPos = bootstrap.DemonstrationItem.transform.position;
            Quaternion itemRot = bootstrap.DemonstrationItem.transform.rotation;

            if (bootstrap.Model.TryGetItem(itemId, out var snap) &&
                snap.location == ItemLocationKind.Free &&
                string.Equals(snap.itemId, itemId, StringComparison.Ordinal) &&
                string.Equals(snap.itemTypeId, itemTypeId, StringComparison.Ordinal) &&
                string.Equals(bootstrap.Model.WorldId, bootstrap.Brain.InstanceWorldId, StringComparison.Ordinal) &&
                bootstrap.DemonstrationItem.IsBoundTo(bootstrap.Model, bootstrap.Brain.InstanceWorldId, bootstrap.Model.GenerationId))
            {
                float posErr = Vector3.Distance(snap.position, itemPos);
                float rotErr = Quaternion.Angle(snap.rotation, itemRot);
                maxAutoSyncPosErrorM = Mathf.Max(maxAutoSyncPosErrorM, posErr);
                maxAutoSyncRotErrorDeg = Mathf.Max(maxAutoSyncRotErrorDeg, rotErr);

                if (posErr > 0.001f || rotErr > 0.01f)
                {
                    autoSyncFailures++;
                }
            }
            else
            {
                autoSyncFailures++;
            }
        }

        private IEnumerator Start()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return RunDiagnosticFlow();
        }

        private IEnumerator CaptureScreenshotWithWait(string filename)
        {
            if (string.IsNullOrEmpty(EvidenceDirectory)) yield break;
            try
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(EvidenceDirectory, filename));
            }
            catch { }
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
        }

        private IEnumerator RunDiagnosticFlow()
        {
            // 1. Wait for scene and bootstrap readiness
            diagnosticState = "Waiting for readiness";
            float waitStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - waitStart < 10f)
            {
                bootstrap = FindFirstObjectByType<PhysicalItemBootstrap>();
                if (bootstrap != null && bootstrap.Brain != null && bootstrap.Brain.Ready &&
                    bootstrap.DemonstrationItem != null && bootstrap.DemonstrationInteractable != null &&
                    bootstrap.Brain.Actions != null && bootstrap.Model != null)
                {
                    break;
                }
                yield return null;
            }

            if (bootstrap == null || bootstrap.Brain == null || !bootstrap.Brain.Ready ||
                bootstrap.DemonstrationItem == null || bootstrap.DemonstrationInteractable == null ||
                bootstrap.Brain.Actions == null || bootstrap.Model == null)
            {
                diagnosticStatus = "FAIL";
                failureReason = "Bootstrap, Brain, or DemonstrationItem not ready within 10s timeout";
                var failSummary = new CanyonDropSummary
                {
                    worldId = bootstrap != null && bootstrap.Brain != null ? bootstrap.Brain.InstanceWorldId : "unknown",
                    generationId = bootstrap != null && bootstrap.Model != null ? bootstrap.Model.GenerationId : "unknown",
                    actorId = NpcAutonomy.AgentId,
                    itemId = bootstrap != null ? bootstrap.DemonstrationItemId : "canyon-artifact-01",
                    itemTypeId = bootstrap != null ? bootstrap.DemonstrationItemTypeId : "canyon-stone",
                    hardwareProcessor = SystemInfo.processorType,
                    hardwareGpu = SystemInfo.graphicsDeviceName,
                    hardwareOs = SystemInfo.operatingSystem,
                    hardwareDeviceModel = SystemInfo.deviceModel,
                    applicationVersion = Application.version,
                    physicsGravityX = Physics.gravity.x,
                    physicsGravityY = Physics.gravity.y,
                    physicsGravityZ = Physics.gravity.z,
                    fixedDeltaTime = Time.fixedDeltaTime
                };
                yield return FinalizeExit(failSummary, null, null);
                yield break;
            }

            // Capture original brain state before pause
            if (!originalBrainStateCaptured)
            {
                originalBrainRunning = bootstrap.Brain.Running;
                originalBrainPossessed = bootstrap.Brain.Possessed;
                originalBrainManualDirection = bootstrap.Brain.ManualDirection;
                originalBrainStateCaptured = true;
            }

            // Pause scripted brain autonomy
            bootstrap.Brain.Pause();
            bootstrap.Brain.SetPossession(false);
            bootstrap.Brain.ManualDirection = Vector3.zero;

            string itemId = bootstrap.DemonstrationItemId;
            string itemTypeId = bootstrap.DemonstrationItemTypeId;
            var summary = new CanyonDropSummary
            {
                worldId = bootstrap.Brain.InstanceWorldId,
                generationId = bootstrap.Model.GenerationId,
                actorId = NpcAutonomy.AgentId,
                itemId = itemId,
                itemTypeId = itemTypeId,
                itemMassKg = bootstrap.DemonstrationItemMassKg,
                itemDimensions = bootstrap.DemonstrationItemDimensions,
                hardwareProcessor = SystemInfo.processorType,
                hardwareGpu = SystemInfo.graphicsDeviceName,
                hardwareOs = SystemInfo.operatingSystem,
                hardwareDeviceModel = SystemInfo.deviceModel,
                applicationVersion = Application.version,
                physicsGravityX = Physics.gravity.x,
                physicsGravityY = Physics.gravity.y,
                physicsGravityZ = Physics.gravity.z,
                fixedDeltaTime = Time.fixedDeltaTime,
                bodyCollisionDetectionMode = bootstrap.DemonstrationItem.Body != null ? bootstrap.DemonstrationItem.Body.collisionDetectionMode.ToString() : "none"
            };

            // 2. Real Pickup at normal spawn
            diagnosticState = "Pickup at spawn";
            float reachDist = Vector3.Distance(bootstrap.Brain.transform.position, bootstrap.DemonstrationItem.transform.position);
            float eyeDist = Vector3.Distance(bootstrap.Brain.transform.position + Vector3.up, bootstrap.DemonstrationInteractable.SightPoint);
            if (reachDist > 0.65f || eyeDist > 1.7f)
            {
                diagnosticStatus = "FAIL";
                failureReason = $"DemonstrationItem out of reach at spawn: dist={reachDist:F3}m (max 0.65m), eye={eyeDist:F3}m (max 1.7m)";
                yield return FinalizeExit(summary, null, null);
                yield break;
            }

            var pickupRes = bootstrap.Brain.ExecutePlayerAction(NpcActionKind.Pickup, itemId);
            summary.pickupSuccess = pickupRes.success;
            summary.pickupCode = pickupRes.code;
            if (!pickupRes.success)
            {
                diagnosticStatus = "FAIL";
                failureReason = $"Pickup action rejected: code={pickupRes.code}";
                yield return FinalizeExit(summary, null, null);
                yield break;
            }

            bool isCarried = bootstrap.DemonstrationItem.IsCarried;
            bool actionsHeld = bootstrap.Brain.Actions.Held != null && bootstrap.Brain.Actions.Held.StableId == itemId;
            bool modelCarried = bootstrap.Model.TryGetItem(itemId, out var snap) && snap.location == ItemLocationKind.Carried;
            if (!isCarried || !actionsHeld || !modelCarried)
            {
                diagnosticStatus = "FAIL";
                failureReason = $"Carried state check failed: isCarried={isCarried}, actionsHeld={actionsHeld}, modelCarried={modelCarried}";
                yield return FinalizeExit(summary, null, null);
                yield break;
            }

            summary.pickupScreenshot = "pickup.png";
            yield return CaptureScreenshotWithWait("pickup.png");

            // 3. Radial slope scanning 2..30m, 16 directions for 12..18° dry canyon slope
            diagnosticState = "Scanning terrain slope";
            var nav = bootstrap.Brain.TerrainNavigation;
            if (nav == null)
            {
                diagnosticStatus = "FAIL";
                failureReason = "TerrainNavigation component missing on Brain";
                yield return FinalizeExit(summary, null, null);
                yield break;
            }

            Vector3 actorStartPos = bootstrap.Brain.transform.position;
            summary.actorStartPos = actorStartPos;

            var allScanned = new List<SlopeCandidate>(240);
            var slopeCandidates = new List<SlopeCandidate>();

            for (int d = 0; d < 16; d++)
            {
                float angleRad = d * (2f * Mathf.PI / 16f);
                float angleDeg = d * 22.5f;
                Vector3 dir = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));

                for (float r = 2f; r <= 30f; r += 2f)
                {
                    Vector3 samplePos = actorStartPos + dir * r;
                    bool grounded = nav.TryGround(samplePos, out float h, out Vector3 normal);
                    float slopeDeg = grounded ? Vector3.Angle(Vector3.up, normal) : -1f;
                    bool walkable = nav.Walkable(samplePos, out Vector3 floor);
                    bool dry = grounded && (h > nav.WaterLevel(samplePos) + 0.1f);

                    var cand = new SlopeCandidate
                    {
                        dirIndex = d,
                        angleDeg = angleDeg,
                        radius = r,
                        rawPos = samplePos,
                        floor = floor,
                        normal = normal,
                        slopeDeg = slopeDeg,
                        walkable = walkable,
                        dry = dry
                    };
                    allScanned.Add(cand);

                    if (grounded && dry && walkable && slopeDeg >= 12.0f && slopeDeg <= 18.0f)
                    {
                        slopeCandidates.Add(cand);
                    }
                }
            }

            summary.totalRadialSamplesScanned = allScanned.Count;
            summary.slopeCandidatesFound = slopeCandidates.Count;

            slopeCandidates.Sort((a, b) => Vector3.Distance(actorStartPos, a.floor).CompareTo(Vector3.Distance(actorStartPos, b.floor)));

            Queue<Vector3> chosenRoute = null;
            SlopeCandidate chosenCandidate = default;
            bool candidateSelected = false;
            int routeAttempts = 0;

            foreach (var cand in slopeCandidates)
            {
                if (routeAttempts >= 4) break;
                routeAttempts++;
                var plan = nav.Plan(actorStartPos, cand.floor);
                if (plan != null && plan.Count > 0)
                {
                    chosenRoute = plan;
                    chosenCandidate = cand;
                    candidateSelected = true;
                    break;
                }
            }

            summary.routeAttemptsCount = routeAttempts;

            // Write radial candidates CSV
            var radialCsv = new StringBuilder();
            radialCsv.AppendLine("dir_index,angle_deg,radius_m,pos_x,pos_y,pos_z,floor_y,normal_x,normal_y,normal_z,slope_deg,walkable,dry,candidate_slope,route_chosen");
            foreach (var s in allScanned)
            {
                bool isCand = s.walkable && s.dry && s.slopeDeg >= 12.0f && s.slopeDeg <= 18.0f;
                bool isChosen = candidateSelected && Mathf.Approximately(s.radius, chosenCandidate.radius) && s.dirIndex == chosenCandidate.dirIndex;
                radialCsv.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1:F1},{2:F1},{3:F3},{4:F3},{5:F3},{6:F3},{7:F4},{8:F4},{9:F4},{10:F2},{11},{12},{13},{14}\n",
                    s.dirIndex, s.angleDeg, s.radius, s.rawPos.x, s.rawPos.y, s.rawPos.z, s.floor.y,
                    s.normal.x, s.normal.y, s.normal.z, s.slopeDeg, s.walkable ? 1 : 0, s.dry ? 1 : 0,
                    isCand ? 1 : 0, isChosen ? 1 : 0);
            }

            if (!candidateSelected)
            {
                diagnosticStatus = "BLOCKED_NO_REACHABLE_SLOPE";
                failureReason = $"No reachable 12..18° dry slope among {slopeCandidates.Count} candidates ({routeAttempts} routes planned)";
                yield return FinalizeExit(summary, radialCsv.ToString(), null);
                yield break;
            }

            summary.chosenCandidateSlopeDeg = chosenCandidate.slopeDeg;
            summary.chosenCandidatePos = chosenCandidate.floor;

            // 4. Walk existing route (bounded 50s)
            diagnosticState = "Walking to slope";
            bootstrap.Brain.SetPossession(true);
            float walkStartTime = Time.time;
            bool walkFailed = false;
            totalWalkDistanceM = 0f;

            while (chosenRoute.Count > 0 && (Time.time - walkStartTime) < 50f)
            {
                Vector3 wp = chosenRoute.Peek();
                Vector3 diff = wp - bootstrap.Brain.transform.position;
                diff.y = 0f;
                if (diff.magnitude < 0.45f)
                {
                    chosenRoute.Dequeue();
                    if (chosenRoute.Count == 0) break;
                    wp = chosenRoute.Peek();
                    diff = wp - bootstrap.Brain.transform.position;
                    diff.y = 0f;
                }

                Vector3 dir = diff.sqrMagnitude > 0.0001f ? diff.normalized : Vector3.zero;
                bootstrap.Brain.ManualDirection = dir;
                Vector3 prevPos = bootstrap.Brain.transform.position;
                yield return new WaitForFixedUpdate();

                Vector3 curPos = bootstrap.Brain.transform.position;
                if (Vector3.Distance(curPos, prevPos) < 0.0001f && bootstrap.Brain.Actor != null)
                {
                    float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
                    Vector3 motion = nav.ConstrainMotion(curPos, dir, bootstrap.Brain.Actor.WalkSpeed * dt);
                    bootstrap.Brain.Actor.Step(motion, dt);
                    curPos = bootstrap.Brain.transform.position;
                }

                totalWalkDistanceM += Vector3.Distance(prevPos, curPos);
                straightLineDisplacementM = Vector3.Distance(actorStartPos, curPos);

                if (Vector3.Distance(bootstrap.DemonstrationItem.transform.position, curPos) > 1.2f)
                {
                    walkFailed = true;
                    failureReason = "Held item failed to follow actor during walk";
                    break;
                }
                if (!bootstrap.Model.TryGetItem(itemId, out var cSnap) || cSnap.location != ItemLocationKind.Carried)
                {
                    walkFailed = true;
                    failureReason = "ItemModel state was not Carried during walk";
                    break;
                }
            }

            // Stop input and motion before arrival check
            bootstrap.Brain.ManualDirection = Vector3.zero;
            bootstrap.Brain.SetPossession(false);
            bootstrap.Brain.Pause();
            if (bootstrap.Brain.Actor != null)
            {
                bootstrap.Brain.Actor.CancelGesture();
                bootstrap.Brain.Actor.Step(Vector3.zero, Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f);
            }
            Physics.SyncTransforms();

            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();

            summary.routeDurationSeconds = Time.time - walkStartTime;
            Vector3 actorArrivalPos = bootstrap.Brain.transform.position;
            summary.actorArrivalPos = actorArrivalPos;
            straightLineDisplacementM = Vector3.Distance(actorStartPos, actorArrivalPos);
            summary.totalWalkDistanceM = totalWalkDistanceM;
            summary.straightLineDisplacementM = straightLineDisplacementM;

            if (!walkFailed && chosenRoute.Count > 0)
            {
                walkFailed = true;
                failureReason = "Route traversal timed out after 50s";
            }

            nav.TryGround(actorArrivalPos, out float arrH, out Vector3 arrNormal);
            arrivalSlopeDeg = Vector3.Angle(Vector3.up, arrNormal);
            summary.arrivalTerrainNormal = arrNormal;
            summary.arrivalTerrainSlopeDeg = arrivalSlopeDeg;

            Collider terrainCollider = null;
            if (Physics.Raycast(actorArrivalPos + Vector3.up * 2f, Vector3.down, out var rHit, 10f, 1 << 10))
            {
                terrainCollider = rHit.collider;
                summary.terrainColliderName = terrainCollider.name;
                summary.terrainColliderType = terrainCollider.GetType().Name;
                summary.terrainAttachedRigidbody = terrainCollider.attachedRigidbody != null ? terrainCollider.attachedRigidbody.name : "none";
            }

            summary.arrivalScreenshot = "arrival.png";
            yield return CaptureScreenshotWithWait("arrival.png");

            if (walkFailed)
            {
                diagnosticStatus = "FAIL";
                yield return FinalizeExit(summary, radialCsv.ToString(), null);
                yield break;
            }

            // Validate arrival 12..18° BEFORE Drop; otherwise BLOCKED_SCOPE_MISMATCH
            if (arrivalSlopeDeg < 12.0f || arrivalSlopeDeg > 18.0f)
            {
                diagnosticStatus = "BLOCKED_SCOPE_MISMATCH";
                failureReason = $"Observed arrival slope {arrivalSlopeDeg:F2}° (normal {arrNormal}) is outside 12..18° scope";
                yield return FinalizeExit(summary, radialCsv.ToString(), null);
                yield break;
            }

            // Validate terrain collider is MeshCollider BEFORE Drop
            if (terrainCollider == null || !(terrainCollider is MeshCollider))
            {
                diagnosticStatus = "FAIL";
                failureReason = $"Arrival terrain collider invalid: {(terrainCollider == null ? "null" : terrainCollider.GetType().Name)} (expected MeshCollider)";
                yield return FinalizeExit(summary, radialCsv.ToString(), null);
                yield break;
            }

            // Attach observer to item with ExpectedCollider BEFORE Drop
            var observer = bootstrap.DemonstrationItem.gameObject.AddComponent<CanyonContactObserver>();
            observer.ExpectedCollider = terrainCollider;

            // 5. Real Drop action at arrival slope
            diagnosticState = "Executing Drop";
            var dropRes = bootstrap.Brain.ExecutePlayerAction(NpcActionKind.Drop, itemId);
            summary.dropSuccess = dropRes.success;
            summary.dropCode = dropRes.code;
            if (!dropRes.success)
            {
                diagnosticStatus = "FAIL";
                failureReason = $"Drop action rejected by Brain: code={dropRes.code}";
                yield return FinalizeExit(summary, radialCsv.ToString(), null);
                yield break;
            }

            bool freeCleared = !bootstrap.DemonstrationItem.IsCarried &&
                               bootstrap.Brain.Actions.Held == null &&
                               string.IsNullOrEmpty(bootstrap.DemonstrationInteractable.HeldBy);
            bool bodyValid = bootstrap.DemonstrationItem.Body != null &&
                             !bootstrap.DemonstrationItem.Body.isKinematic &&
                             bootstrap.DemonstrationItem.Body.useGravity;
            bool colValid = bootstrap.DemonstrationItem.ItemCollider != null &&
                            !bootstrap.DemonstrationItem.ItemCollider.isTrigger;
            bool modelFree = bootstrap.Model.TryGetItem(itemId, out var dSnap) && dSnap.location == ItemLocationKind.Free;

            if (!freeCleared || !bodyValid || !colValid || !modelFree)
            {
                diagnosticStatus = "FAIL";
                failureReason = $"Post-drop verification failed: freeCleared={freeCleared}, bodyValid={bodyValid}, colValid={colValid}, modelFree={modelFree}";
                yield return FinalizeExit(summary, radialCsv.ToString(), null);
                yield break;
            }

            // Immediately after successful Drop, enable pre-physics auto-sync observation
            observeAutoSync = true;

            // 6. Observe PhysicalItem on MeshCollider for max 8s
            diagnosticState = "Observing Drop Physics";
            var fixedCsv = new StringBuilder();
            fixedCsv.AppendLine(
                "step_index,time_s,item_pos_x,item_pos_y,item_pos_z,actor_pos_x,actor_pos_y,actor_pos_z," +
                "rb_pos_x,rb_pos_y,rb_pos_z,rb_rot_x,rb_rot_y,rb_rot_z,rb_rot_w," +
                "tf_pos_x,tf_pos_y,tf_pos_z,tf_rot_x,tf_rot_y,tf_rot_z,tf_rot_w," +
                "vel_x,vel_y,vel_z,lin_speed,ang_vel_x,ang_vel_y,ang_vel_z,ang_speed_deg," +
                "penetration_m,in_contact,col_normal_x,col_normal_y,col_normal_z,col_min_sep_m,col_age_s," +
                "local_slope_deg,model_synced,model_pos_lag_m,model_rot_lag_deg");

            float dropElapsed = 0f;
            int stepIndex = 0;
            bool firstContactAchieved = false;
            float firstContactTime = -1f;
            bool settled = false;
            float settledTime = -1f;
            Vector3 settledPos = Vector3.zero;
            float obsStartTime = -1f;
            bool observationCompleted = false;
            float maxObsDisp = 0f;
            float maxObsLinSpeed = 0f;
            float maxObsAngSpeed = 0f;
            bool obsContactLost = false;
            float maxPenetration = 0f;
            bool penExceeded = false;
            float maxModelPosLag = 0f;
            float maxModelRotLag = 0f;

            while (dropElapsed < 8.0f)
            {
                yield return new WaitForFixedUpdate();
                float dt = Time.fixedDeltaTime;
                dropElapsed += dt;
                stepIndex++;

                Vector3 itemPos = bootstrap.DemonstrationItem.transform.position;
                Vector3 actorPos = bootstrap.Brain.transform.position;
                Vector3 rbPos = bootstrap.DemonstrationItem.Body != null ? bootstrap.DemonstrationItem.Body.position : itemPos;
                Quaternion rbRot = bootstrap.DemonstrationItem.Body != null ? bootstrap.DemonstrationItem.Body.rotation : bootstrap.DemonstrationItem.transform.rotation;
                Vector3 tfPos = itemPos;
                Quaternion tfRot = bootstrap.DemonstrationItem.transform.rotation;

                Vector3 vel = bootstrap.DemonstrationItem.Body != null ? bootstrap.DemonstrationItem.Body.linearVelocity : Vector3.zero;
                Vector3 angVel = bootstrap.DemonstrationItem.Body != null ? bootstrap.DemonstrationItem.Body.angularVelocity : Vector3.zero;
                float linSpeed = vel.magnitude;
                float angSpeedDeg = angVel.magnitude * Mathf.Rad2Deg;

                currentLinSpeed = linSpeed;
                bool inContact = observer.InContact;
                currentInContact = inContact;

                float penDist = 0f;
                if (bootstrap.DemonstrationItem.ItemCollider != null && terrainCollider != null)
                {
                    Physics.ComputePenetration(
                        bootstrap.DemonstrationItem.ItemCollider, itemPos, tfRot,
                        terrainCollider, terrainCollider.transform.position, terrainCollider.transform.rotation,
                        out Vector3 penDir, out penDist);
                }
                maxPenetration = Mathf.Max(maxPenetration, penDist);
                currentPen = penDist;

                if (penDist > 0.01f)
                {
                    penExceeded = true;
                    if (failureReason == null)
                        failureReason = $"Penetration {penDist:F4}m exceeded 0.01m limit";
                }

                float localSlopeDeg = 0f;
                if (nav.TryGround(itemPos, out _, out Vector3 itemNormal))
                {
                    localSlopeDeg = Vector3.Angle(Vector3.up, itemNormal);
                    actualDropSlopeDeg = localSlopeDeg;
                }

                float colAge = observer.LastCallbackTime >= 0f ? (Time.fixedTime - observer.LastCallbackTime) : -1f;

                // Post-physics lag telemetry (recorded for CSV/telemetry, NOT counted as sync failure)
                int modelSynced = 0;
                float modelPosLag = -1f;
                float modelRotLag = -1f;

                if (bootstrap.Model.TryGetItem(itemId, out var sSnap) && sSnap.location == ItemLocationKind.Free)
                {
                    modelPosLag = Vector3.Distance(sSnap.position, itemPos);
                    modelRotLag = Quaternion.Angle(sSnap.rotation, tfRot);
                    maxModelPosLag = Mathf.Max(maxModelPosLag, modelPosLag);
                    maxModelRotLag = Mathf.Max(maxModelRotLag, modelRotLag);
                    modelSynced = (modelPosLag <= 0.05f && modelRotLag <= 1.0f) ? 1 : 0;
                }

                fixedCsv.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4}," +
                    "{8:F4},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4},{14:F4}," +
                    "{15:F4},{16:F4},{17:F4},{18:F4},{19:F4},{20:F4},{21:F4}," +
                    "{22:F4},{23:F4},{24:F4},{25:F4},{26:F4},{27:F4},{28:F4},{29:F2}," +
                    "{30:F5},{31},{32:F4},{33:F4},{34:F4},{35:F5},{36:F4}," +
                    "{37:F2},{38},{39:F5},{40:F3}\n",
                    stepIndex, dropElapsed, itemPos.x, itemPos.y, itemPos.z, actorPos.x, actorPos.y, actorPos.z,
                    rbPos.x, rbPos.y, rbPos.z, rbRot.x, rbRot.y, rbRot.z, rbRot.w,
                    tfPos.x, tfPos.y, tfPos.z, tfRot.x, tfRot.y, tfRot.z, tfRot.w,
                    vel.x, vel.y, vel.z, linSpeed, angVel.x, angVel.y, angVel.z, angSpeedDeg,
                    penDist, inContact ? 1 : 0, observer.LastNormal.x, observer.LastNormal.y, observer.LastNormal.z,
                    observer.LastMinSeparation, colAge,
                    localSlopeDeg, modelSynced, modelPosLag, modelRotLag);

                if (!firstContactAchieved)
                {
                    if (inContact)
                    {
                        firstContactAchieved = true;
                        firstContactTime = dropElapsed;
                        diagnosticState = "Settling (allow <=5s)";
                    }
                }
                else if (!settled)
                {
                    float settleElapsed = dropElapsed - firstContactTime;
                    if (settleElapsed > 5.0f)
                    {
                        if (failureReason == null)
                            failureReason = $"Failed to settle within 5.0s of contact (elapsed={settleElapsed:F3}s, lin={linSpeed:F4}m/s, ang={angSpeedDeg:F2}°/s)";
                        break;
                    }

                    if (inContact && linSpeed <= 0.03f && angSpeedDeg <= 3.0f)
                    {
                        settled = true;
                        settledTime = dropElapsed;
                        settledPos = itemPos;
                        obsStartTime = dropElapsed;
                        diagnosticState = "Observing (2s rest <=0.02m)";
                    }
                }
                else if (!observationCompleted)
                {
                    float obsElapsed = dropElapsed - obsStartTime;
                    float disp = Vector3.Distance(itemPos, settledPos);
                    maxObsDisp = Mathf.Max(maxObsDisp, disp);
                    maxObsLinSpeed = Mathf.Max(maxObsLinSpeed, linSpeed);
                    maxObsAngSpeed = Mathf.Max(maxObsAngSpeed, angSpeedDeg);

                    if (!inContact)
                    {
                        obsContactLost = true;
                        if (failureReason == null)
                            failureReason = $"Observation contact lost at obsElapsed={obsElapsed:F3}s";
                        break;
                    }
                    if (linSpeed > 0.03f)
                    {
                        if (failureReason == null)
                            failureReason = $"Observation linear speed {linSpeed:F4}m/s exceeded 0.03m/s";
                        break;
                    }
                    if (angSpeedDeg > 3.0f)
                    {
                        if (failureReason == null)
                            failureReason = $"Observation angular speed {angSpeedDeg:F2}°/s exceeded 3.0°/s";
                        break;
                    }
                    if (disp > 0.02f)
                    {
                        if (failureReason == null)
                            failureReason = $"Observation rest drift {disp:F4}m exceeded 0.02m";
                        break;
                    }
                    if (obsElapsed >= 2.0f)
                    {
                        observationCompleted = true;
                        diagnosticState = "Completed";
                        break;
                    }
                }
            }

            observeAutoSync = false;
            if (observer != null) Destroy(observer);

            if (!firstContactAchieved && failureReason == null)
                failureReason = "Cube never contacted canyon terrain within observation timeout";
            else if (!settled && failureReason == null)
                failureReason = "Cube failed to settle before observation timeout";
            else if (!observationCompleted && failureReason == null)
                failureReason = "2s rest observation phase incomplete before timeout";

            bool slopeInRange = actualDropSlopeDeg >= 12.0f && actualDropSlopeDeg <= 18.0f;
            if (!slopeInRange && failureReason == null)
                failureReason = $"Actual drop local slope {actualDropSlopeDeg:F2}° outside required 12..18° range (scope mismatch)";

            if (autoSyncFailures > 0 && failureReason == null)
                failureReason = $"Model sync failure: {autoSyncFailures}/{autoSyncChecks} pre-physics checks failed (tol <=0.001m, <=0.01°)";
            else if (autoSyncChecks == 0 && failureReason == null)
                failureReason = "Model sync failure: zero pre-physics checks recorded";

            bool passed = firstContactAchieved && settled && observationCompleted &&
                          !penExceeded && !obsContactLost && slopeInRange &&
                          autoSyncChecks > 0 && autoSyncFailures == 0 && failureReason == null;

            summary.contactAchieved = firstContactAchieved;
            summary.firstContactTimeSeconds = firstContactTime;
            summary.settled = settled;
            summary.settleDurationSeconds = settled ? (settledTime - firstContactTime) : -1f;
            summary.observationCompleted = observationCompleted;
            summary.maxObservationDisplacementM = maxObsDisp;
            summary.maxObservationLinearSpeedMPerS = maxObsLinSpeed;
            summary.maxObservationAngularSpeedDegPerS = maxObsAngSpeed;
            summary.observationContactLost = obsContactLost;
            summary.maxPenetrationM = maxPenetration;
            summary.penetrationExceeded = penExceeded;
            summary.finalLocalSlopeDeg = actualDropSlopeDeg;
            summary.slopeWithinRange12To18 = slopeInRange;
            summary.modelSyncCheckCount = autoSyncChecks;
            summary.modelSyncFailures = autoSyncFailures;
            summary.maxModelPosLagM = maxAutoSyncPosErrorM;
            summary.maxModelRotLagDeg = maxAutoSyncRotErrorDeg;

            diagnosticStatus = passed ? "PASS" : ("FAIL: " + failureReason);

            summary.dropEndScreenshot = "drop_end.png";
            yield return CaptureScreenshotWithWait("drop_end.png");

            yield return FinalizeExit(summary, radialCsv.ToString(), fixedCsv.ToString());
        }

        private IEnumerator FinalizeExit(CanyonDropSummary summary, string radialCsv, string fixedCsv)
        {
            if (summary != null)
            {
                summary.status = diagnosticStatus;
                summary.failureReason = failureReason;
            }

            if (!string.IsNullOrEmpty(EvidenceDirectory))
            {
                try
                {
                    Directory.CreateDirectory(EvidenceDirectory);
                    if (!string.IsNullOrEmpty(radialCsv))
                        File.WriteAllText(Path.Combine(EvidenceDirectory, "radial_candidates.csv"), radialCsv);
                    if (!string.IsNullOrEmpty(fixedCsv))
                        File.WriteAllText(Path.Combine(EvidenceDirectory, "canyon_fixed_steps.csv"), fixedCsv);
                    if (summary != null)
                    {
                        summary.terminalScreenshot = "terminal.png";
                        File.WriteAllText(Path.Combine(EvidenceDirectory, "canyon_summary.json"), JsonUtility.ToJson(summary, true));
                    }

                    if (string.Equals(diagnosticStatus, "PASS", StringComparison.OrdinalIgnoreCase))
                    {
                        File.WriteAllText(Path.Combine(EvidenceDirectory, "passed.txt"), "PHYSICAL CANYON DROP DIAGNOSTIC PASS\n");
                    }
                    else if (diagnosticStatus.StartsWith("BLOCKED", StringComparison.OrdinalIgnoreCase))
                    {
                        File.WriteAllText(Path.Combine(EvidenceDirectory, "blocked.txt"), $"PHYSICAL CANYON DROP DIAGNOSTIC BLOCKED: {failureReason}\n");
                    }
                    else
                    {
                        File.WriteAllText(Path.Combine(EvidenceDirectory, "failed.txt"), $"PHYSICAL CANYON DROP DIAGNOSTIC FAIL: {failureReason}\n");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PhysicalCanyonDropDiagnostic] Failed writing evidence: {ex.Message}");
                }
            }

            yield return CaptureScreenshotWithWait("terminal.png");

            observeAutoSync = false;

            // Graceful cleanup: restore original brain / control state
            if (bootstrap != null && bootstrap.Brain != null && originalBrainStateCaptured)
            {
                bootstrap.Brain.SetPossession(originalBrainPossessed);
                bootstrap.Brain.ManualDirection = originalBrainManualDirection;
                bootstrap.Brain.Running = originalBrainRunning;
            }

            Debug.Log($"[PhysicalCanyonDropDiagnostic] Finished with status: {diagnosticStatus}");

            // Auto-quit after 5 seconds for bounded owned process
            yield return new WaitForSeconds(5.0f);
            if (!Application.isEditor)
            {
                bool isPass = string.Equals(diagnosticStatus, "PASS", StringComparison.OrdinalIgnoreCase);
                Application.Quit(isPass ? 0 : 1);
            }
        }

        private void OnGUI()
        {
            Rect box = new Rect(20, Screen.height - 110, 520, 95);
            GUI.Box(box, "");
            string label =
                $"SCRIPTED GUARDED CANYON DROP (not model autonomy)\n" +
                $"Status: {diagnosticStatus} | State: {diagnosticState}\n" +
                $"Walk: {totalWalkDistanceM:F2}m (disp {straightLineDisplacementM:F2}m) | ArrSlope: {arrivalSlopeDeg:F1}° | DropSlope: {actualDropSlopeDeg:F1}°\n" +
                $"Item lin: {currentLinSpeed:F4} m/s | Contact: {currentInContact} | Pen: {currentPen:F4}m";
            GUI.Label(new Rect(box.x + 10, box.y + 10, box.width - 20, box.height - 20), label);
        }
    }
}
