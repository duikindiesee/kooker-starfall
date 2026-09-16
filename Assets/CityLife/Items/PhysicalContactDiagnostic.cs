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
    /// Standalone opt-in compiled-player contact diagnostic validating physical settling,
    /// dynamic friction, penetration limits, and rest drift across 6 sequential cube/ramp cases.
    /// Runs strictly on normal fixed-step simulation (never Physics.Simulate or timeScale modification).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalContactDiagnostic : MonoBehaviour
    {
        [Serializable]
        public sealed class CaseSummary
        {
            public int caseIndex;
            public string caseName;
            public float cubeSize;
            public float massKg;
            public float slopeDegrees;
            public bool passed;
            public string status;
            public float normalClearanceM;
            public float actualInitialVerticalClearanceM;
            public float settleDurationSeconds;
            public float observationDisplacementM;
            public float maxLinearSpeedMPerS;
            public float maxAngularSpeedDegPerS;
            public float maxObservationLinearSpeedMPerS;
            public float maxObservationAngularSpeedDegPerS;
            public bool observationContactLost;
            public float maxPenetrationM;
            public float impactSanityBoundReferenceM;
            public float impactSanityBoundMPerS;
            public bool contactAchieved;
            public int totalSteps;
        }

        [Serializable]
        public sealed class DiagnosticSummary
        {
            public string schema = "starfall.physical-contact-diagnostic.v1";
            public string status;
            public string scope = "Isolated staged physics fixture contact diagnostic; separate from normal human play acceptance";
            public string applicationVersion;
            public string hardwareProcessor;
            public string hardwareGpu;
            public string hardwareOs;
            public string hardwareDeviceModel;
            public float physicsGravityX;
            public float physicsGravityY;
            public float physicsGravityZ;
            public float fixedDeltaTime;
            public float staticFriction;
            public float dynamicFriction;
            public float bounciness;
            public string frictionCombine;
            public string bounceCombine;
            public float rampWidth;
            public float rampThickness;
            public float rampLength;
            public float impactSanityBoundReferenceM;
            public float totalDurationSeconds;
            public int totalCases;
            public int passedCases;
            public int failedCases;
            public CaseSummary[] cases;
        }

        private sealed class ContactTracker : MonoBehaviour
        {
            public bool InContact { get; private set; }
            public Collider ExpectedCollider;

            private void OnCollisionEnter(Collision collision)
            {
                if (ExpectedCollider == null || collision.collider == ExpectedCollider)
                    InContact = true;
            }

            private void OnCollisionStay(Collision collision)
            {
                if (ExpectedCollider == null || collision.collider == ExpectedCollider)
                    InContact = true;
            }

            private void OnCollisionExit(Collision collision)
            {
                if (ExpectedCollider == null || collision.collider == ExpectedCollider)
                    InContact = false;
            }
        }

        public string EvidenceDirectory { get; set; }

        private readonly Vector3 fixtureOrigin = new Vector3(1500f, 300f, 1500f);
        private readonly Vector3 rampSize = new Vector3(8.0f, 1.0f, 16.0f);

        private Camera targetCamera;
        private NpcPlayerControls cameraControls;
        private CharacterPreviewCamera previewCamera;
        private bool previousSuppressView;
        private bool previousExternalView;
        private bool aimCamera;

        private GameObject fixtureRoot;
        private Material sharedVisualMaterial;
        private PhysicsMaterial diagnosticFrictionMaterial;

        private bool isRunning;
        private int currentCaseIndex;
        private string currentCaseName = "initializing";
        private string currentCaseState = "idle";
        private float currentCaseElapsed;
        private float currentLinearSpeed;
        private float currentAngularSpeedDeg;
        private float currentPenetration;
        private float currentDisplacement;

        private bool suiteCompleted;
        private bool suiteAllPassed;
        private int suitePassedCount;
        private float suiteTotalTime;
        private float autoQuitCountdown = -1f;

        private readonly StringBuilder csvBuilder = new StringBuilder(65536);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnSceneLoaded()
        {
            string evidenceDir = GetCommandLineArg("-physicalContactEvidence");
            if (string.IsNullOrEmpty(evidenceDir))
                return;

            if (!Path.IsPathRooted(evidenceDir))
            {
                Debug.LogWarning($"[PhysicalContactDiagnostic] -physicalContactEvidence path '{evidenceDir}' is not absolute. Aborting attachment.");
                return;
            }

            evidenceDir = Path.GetFullPath(evidenceDir);

            if (Directory.Exists(evidenceDir))
            {
                var entries = Directory.GetFileSystemEntries(evidenceDir);
                if (entries.Length > 0)
                {
                    Debug.LogWarning($"[PhysicalContactDiagnostic] Target directory '{evidenceDir}' is not empty ({entries.Length} entries). Aborting attachment.");
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
                    Debug.LogError($"[PhysicalContactDiagnostic] Failed to create output directory '{evidenceDir}': {ex.Message}");
                    return;
                }
            }

            var host = new GameObject("PHYSICAL CONTACT DIAGNOSTIC");
            host.hideFlags = HideFlags.DontSave;
            var diagnostic = host.AddComponent<PhysicalContactDiagnostic>();
            diagnostic.EvidenceDirectory = evidenceDir;
        }

        private static string GetCommandLineArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private IEnumerator Start()
        {
            // Wait for live scene, camera and actor initialization
            yield return null;
            yield return new WaitForEndOfFrame();

            yield return RunDiagnosticSuite();
        }

        private IEnumerator RunDiagnosticSuite()
        {
            isRunning = true;
            float suiteStartTime = Time.realtimeSinceStartup;

            // Initialize CSV header
            csvBuilder.AppendLine("case_index,case_name,step_index,time_s,pos_x,pos_y,pos_z,vel_x,vel_y,vel_z,vel_mag,ang_vel_x,ang_vel_y,ang_vel_z,ang_vel_deg,in_contact,penetration_m");

            // Setup camera targeting
            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                targetCamera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            }

            // Camera controls may reside on actor or camera: find via FindFirstObjectByType and preserve previous state
            cameraControls = UnityEngine.Object.FindFirstObjectByType<NpcPlayerControls>();
            if (cameraControls != null)
            {
                previousSuppressView = cameraControls.SuppressView;
                cameraControls.SuppressView = true;
            }

            previewCamera = UnityEngine.Object.FindFirstObjectByType<CharacterPreviewCamera>();
            if (previewCamera != null)
            {
                previousExternalView = previewCamera.ExternalView;
                previewCamera.ExternalView = true;
            }

            if (targetCamera != null)
            {
                aimCamera = true;
            }

            // Create isolated fixture root
            fixtureRoot = new GameObject("PHYSICAL CONTACT DIAGNOSTIC fixture");
            fixtureRoot.hideFlags = HideFlags.DontSave;
            fixtureRoot.transform.position = fixtureOrigin;

            // Add dedicated local point light to ensure clear illumination
            var lightGo = new GameObject("PHYSICAL CONTACT DIAGNOSTIC - Light");
            lightGo.hideFlags = HideFlags.DontSave;
            lightGo.transform.SetParent(fixtureRoot.transform, false);
            lightGo.transform.localPosition = new Vector3(-3f, 8f, -4f);
            var ptLight = lightGo.AddComponent<Light>();
            ptLight.type = LightType.Point;
            ptLight.range = 35f;
            ptLight.intensity = 2.5f;
            ptLight.color = Color.white;

            // Reuse serialized PhysicalItemBootstrap.DemonstrationMaterial where available to avoid magenta
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<PhysicalItemBootstrap>();
            if (bootstrap != null && bootstrap.DemonstrationMaterial != null)
            {
                sharedVisualMaterial = bootstrap.DemonstrationMaterial;
            }
            if (sharedVisualMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader != null)
                {
                    sharedVisualMaterial = new Material(shader) { name = "PhysicalContactDiagnosticMaterial" };
                    sharedVisualMaterial.color = new Color(0.56f, 0.54f, 0.52f);
                    sharedVisualMaterial.SetFloat("_Smoothness", 0.15f);
                }
            }

            // Explicit friction material static/dynamic 0.6, bounce 0, minimum combine
            diagnosticFrictionMaterial = new PhysicsMaterial("DiagnosticFrictionMaterial")
            {
                staticFriction = 0.6f,
                dynamicFriction = 0.6f,
                bounciness = 0.0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            // Define 6 sequential cases: small 0.25m cube 2.5kg and large 1m cube 20kg, each flat/15deg/30deg
            var casesConfig = new[]
            {
                new { name = "small-0.25m-flat", size = 0.25f, mass = 2.5f, slope = 0.0f },
                new { name = "small-0.25m-15deg", size = 0.25f, mass = 2.5f, slope = 15.0f },
                new { name = "small-0.25m-30deg", size = 0.25f, mass = 2.5f, slope = 30.0f },
                new { name = "large-1.0m-flat", size = 1.0f, mass = 20.0f, slope = 0.0f },
                new { name = "large-1.0m-15deg", size = 1.0f, mass = 20.0f, slope = 15.0f },
                new { name = "large-1.0m-30deg", size = 1.0f, mass = 20.0f, slope = 30.0f }
            };

            var results = new List<CaseSummary>();
            suiteAllPassed = true;

            for (int i = 0; i < casesConfig.Length; i++)
            {
                // Verify whole suite duration bound <= 80s
                if (Time.realtimeSinceStartup - suiteStartTime >= 80f)
                {
                    Debug.LogWarning("[PhysicalContactDiagnostic] Whole suite runtime budget (80s) reached.");
                    for (int rem = i; rem < casesConfig.Length; rem++)
                    {
                        results.Add(new CaseSummary
                        {
                            caseIndex = rem,
                            caseName = casesConfig[rem].name,
                            cubeSize = casesConfig[rem].size,
                            massKg = casesConfig[rem].mass,
                            slopeDegrees = casesConfig[rem].slope,
                            passed = false,
                            status = "FAIL: Suite budget timeout (80s)",
                            normalClearanceM = 1.0f,
                            actualInitialVerticalClearanceM = -1f,
                            impactSanityBoundReferenceM = 1.0f,
                            impactSanityBoundMPerS = 2.0f * Mathf.Sqrt(2.0f * Mathf.Abs(Physics.gravity.y) * 1.0f) + 0.25f
                        });
                        suiteAllPassed = false;
                    }
                    break;
                }

                currentCaseIndex = i;
                currentCaseName = casesConfig[i].name;
                currentCaseState = "Preparing";

                var cfg = casesConfig[i];
                yield return RunSingleCase(i, cfg.name, cfg.size, cfg.mass, cfg.slope, r => results.Add(r));

                if (!results[i].passed)
                {
                    suiteAllPassed = false;
                }
                else
                {
                    suitePassedCount++;
                }

                // Short settle wait between cases
                yield return new WaitForFixedUpdate();
            }

            suiteTotalTime = Time.realtimeSinceStartup - suiteStartTime;
            suiteCompleted = true;
            isRunning = false;

            // Write output evidence
            WriteEvidence(results, suiteTotalTime);

            // Don't autoquit until 5s after final evidence
            for (float t = 5.0f; t > 0f; t -= Time.unscaledDeltaTime)
            {
                autoQuitCountdown = t;
                yield return null;
            }
            autoQuitCountdown = 0f;

            RestoreCameraState();

            if (!Application.isEditor)
            {
                Application.Quit(suiteAllPassed ? 0 : 1);
            }
        }

        private static float ComputeRampSurfaceY(Vector3 rampTopCenter, Vector3 rampUp, float x, float z)
        {
            if (Mathf.Abs(rampUp.y) < 0.0001f) return rampTopCenter.y;
            return rampTopCenter.y - (rampUp.x * (x - rampTopCenter.x) + rampUp.z * (z - rampTopCenter.z)) / rampUp.y;
        }

        private IEnumerator RunSingleCase(
            int caseIndex,
            string caseName,
            float cubeSize,
            float massKg,
            float slopeDegrees,
            Action<CaseSummary> onComplete)
        {
            currentCaseElapsed = 0f;
            currentLinearSpeed = 0f;
            currentAngularSpeedDeg = 0f;
            currentPenetration = 0f;
            currentDisplacement = 0f;
            currentCaseState = "Setup";

            // Create anchored ramp for this case
            var rampGo = new GameObject($"PHYSICAL CONTACT DIAGNOSTIC - Ramp {caseIndex}");
            rampGo.hideFlags = HideFlags.DontSave;
            rampGo.transform.SetParent(fixtureRoot.transform, false);
            rampGo.transform.position = fixtureOrigin;
            rampGo.transform.rotation = Quaternion.Euler(slopeDegrees, 0f, 0f);
            rampGo.transform.localScale = Vector3.one;

            var rampRb = rampGo.AddComponent<Rigidbody>();
            rampRb.isKinematic = true;
            rampRb.useGravity = false;

            var rampCol = rampGo.AddComponent<BoxCollider>();
            rampCol.size = rampSize;
            rampCol.center = Vector3.zero;
            rampCol.sharedMaterial = diagnosticFrictionMaterial;

            var rampVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rampVisual.name = "Visual";
            var rvCol = rampVisual.GetComponent<Collider>();
            if (rvCol != null) UnityEngine.Object.DestroyImmediate(rvCol);
            rampVisual.transform.SetParent(rampGo.transform, false);
            rampVisual.transform.localPosition = Vector3.zero;
            rampVisual.transform.localRotation = Quaternion.identity;
            rampVisual.transform.localScale = rampSize;
            var rRen = rampVisual.GetComponent<MeshRenderer>();
            if (rRen != null && sharedVisualMaterial != null) rRen.sharedMaterial = sharedVisualMaterial;

            // Calculate drop initial center such that bottom face is 1m normal clearance above ramp center
            Vector3 rampTopCenter = rampGo.transform.TransformPoint(new Vector3(0f, rampSize.y * 0.5f, 0f));
            const float normalClearance = 1.0f;
            Vector3 dropPos = rampTopCenter + rampGo.transform.up * (normalClearance + cubeSize * 0.5f);
            Quaternion dropRot = rampGo.transform.rotation;

            // Calculate actual initial vertical clearance from lowest cube point to ramp surface
            float actualVerticalClearance = float.MaxValue;
            float halfExtent = cubeSize * 0.5f;
            for (int cx = -1; cx <= 1; cx += 2)
            {
                for (int cy = -1; cy <= 1; cy += 2)
                {
                    for (int cz = -1; cz <= 1; cz += 2)
                    {
                        Vector3 corner = dropPos + dropRot * new Vector3(cx * halfExtent, cy * halfExtent, cz * halfExtent);
                        float rampY = ComputeRampSurfaceY(rampTopCenter, rampGo.transform.up, corner.x, corner.z);
                        float vertDist = corner.y - rampY;
                        if (vertDist < actualVerticalClearance) actualVerticalClearance = vertDist;
                    }
                }
            }

            // Create test cube
            var cubeGo = new GameObject($"PHYSICAL CONTACT DIAGNOSTIC - Cube {caseIndex}");
            cubeGo.hideFlags = HideFlags.DontSave;
            cubeGo.transform.SetParent(fixtureRoot.transform, false);
            cubeGo.transform.position = dropPos;
            cubeGo.transform.rotation = dropRot;
            cubeGo.transform.localScale = Vector3.one;

            var cubeCol = cubeGo.AddComponent<BoxCollider>();
            cubeCol.size = Vector3.one * cubeSize;
            cubeCol.center = Vector3.zero;
            cubeCol.sharedMaterial = diagnosticFrictionMaterial;

            var cubeRb = cubeGo.AddComponent<Rigidbody>();
            cubeRb.mass = massKg;
            cubeRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            cubeRb.linearDamping = 0.05f;
            cubeRb.angularDamping = 0.05f;
            cubeRb.useGravity = true;
            cubeRb.isKinematic = false;
            cubeRb.linearVelocity = Vector3.zero;
            cubeRb.angularVelocity = Vector3.zero;

            var cubeVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeVisual.name = "Visual";
            var cvCol = cubeVisual.GetComponent<Collider>();
            if (cvCol != null) UnityEngine.Object.DestroyImmediate(cvCol);
            cubeVisual.transform.SetParent(cubeGo.transform, false);
            cubeVisual.transform.localPosition = Vector3.zero;
            cubeVisual.transform.localRotation = Quaternion.identity;
            cubeVisual.transform.localScale = Vector3.one * cubeSize;
            var cRen = cubeVisual.GetComponent<MeshRenderer>();
            if (cRen != null && sharedVisualMaterial != null) cRen.sharedMaterial = sharedVisualMaterial;

            var tracker = cubeGo.AddComponent<ContactTracker>();
            tracker.ExpectedCollider = rampCol;

            // Conservative impact sanity bound: 2 * sqrt(2 * abs(gravity.y) * dropHeightRef) + 0.25
            const float sanityBoundReferenceHeight = 1.0f;
            float impactSanityBound = 2.0f * Mathf.Sqrt(2.0f * Mathf.Abs(Physics.gravity.y) * sanityBoundReferenceHeight) + 0.25f;

            float caseElapsed = 0f;
            int stepIndex = 0;
            bool firstContactAchieved = false;
            float firstContactTime = -1f;
            bool settled = false;
            float settledTime = -1f;
            Vector3 settledPosition = Vector3.zero;
            float observationStartTime = -1f;
            bool observationCompleted = false;

            float maxLinearSpeed = 0f;
            float maxAngularSpeedDeg = 0f;
            float maxObsLinearSpeed = 0f;
            float maxObsAngularSpeedDeg = 0f;
            bool obsContactLost = false;
            float maxPenetration = 0f;
            float maxObsDisplacement = 0f;

            bool penetrationExceeded = false;
            bool displacementExceeded = false;
            bool sanitySpeedExceeded = false;
            string failureReason = null;

            currentCaseState = "Falling";

            // Run case loop up to 10.0s bound
            while (caseElapsed < 10.0f)
            {
                yield return new WaitForFixedUpdate();
                float dt = Time.fixedDeltaTime;
                caseElapsed += dt;
                currentCaseElapsed = caseElapsed;
                stepIndex++;

                Vector3 pos = cubeGo.transform.position;
                Vector3 vel = cubeRb.linearVelocity;
                Vector3 angVel = cubeRb.angularVelocity;
                float linSpeed = vel.magnitude;
                float angSpeedDeg = angVel.magnitude * Mathf.Rad2Deg;

                currentLinearSpeed = linSpeed;
                currentAngularSpeedDeg = angSpeedDeg;

                maxLinearSpeed = Mathf.Max(maxLinearSpeed, linSpeed);
                maxAngularSpeedDeg = Mathf.Max(maxAngularSpeedDeg, angSpeedDeg);

                // Check impact sanity bound
                if (linSpeed > impactSanityBound)
                {
                    sanitySpeedExceeded = true;
                    if (failureReason == null)
                        failureReason = $"Linear speed {linSpeed:F3} m/s exceeded impact sanity bound {impactSanityBound:F3} m/s";
                }

                // Check penetration via Physics.ComputePenetration each sample
                bool overlap = Physics.ComputePenetration(
                    cubeCol, pos, cubeGo.transform.rotation,
                    rampCol, rampGo.transform.position, rampGo.transform.rotation,
                    out Vector3 penDir, out float penDist);
                float penetration = overlap ? penDist : 0.0f;
                maxPenetration = Mathf.Max(maxPenetration, penetration);
                currentPenetration = penetration;

                if (penetration > 0.01f)
                {
                    penetrationExceeded = true;
                    if (failureReason == null)
                        failureReason = $"Penetration {penetration:F4} m exceeded maximum limit 0.01 m";
                }

                bool inContact = tracker.InContact || (overlap && penDist > 0.0001f);

                // Append CSV row
                csvBuilder.Append(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4},{14:F2},{15},{16:F5}\n",
                    caseIndex, caseName, stepIndex, caseElapsed,
                    pos.x, pos.y, pos.z,
                    vel.x, vel.y, vel.z, linSpeed,
                    angVel.x, angVel.y, angVel.z, angSpeedDeg,
                    inContact ? 1 : 0, penetration));

                // State progression
                if (!firstContactAchieved)
                {
                    if (inContact)
                    {
                        firstContactAchieved = true;
                        firstContactTime = caseElapsed;
                        currentCaseState = "Settling (allow <=5s)";
                    }
                }
                else if (!settled)
                {
                    float settleElapsed = caseElapsed - firstContactTime;

                    // Fail honestly if first settlement is later than 5s deadline after first contact
                    if (settleElapsed > 5.0f)
                    {
                        if (failureReason == null)
                            failureReason = $"Failed to settle within 5.0s deadline of contact (elapsed={settleElapsed:F3}s, lin={linSpeed:F4} m/s, ang={angSpeedDeg:F2} deg/s, inContact={inContact})";
                        break;
                    }

                    // Settlement requires inContact AND speed thresholds (checked before deadline)
                    if (inContact && linSpeed <= 0.03f && angSpeedDeg <= 3.0f)
                    {
                        settled = true;
                        settledTime = caseElapsed;
                        settledPosition = pos;
                        observationStartTime = caseElapsed;
                        currentCaseState = "Observing (2s rest <=0.02m)";
                    }
                }
                else if (!observationCompleted)
                {
                    float obsElapsed = caseElapsed - observationStartTime;
                    float disp = Vector3.Distance(pos, settledPosition);
                    maxObsDisplacement = Mathf.Max(maxObsDisplacement, disp);
                    currentDisplacement = disp;

                    maxObsLinearSpeed = Mathf.Max(maxObsLinearSpeed, linSpeed);
                    maxObsAngularSpeedDeg = Mathf.Max(maxObsAngularSpeedDeg, angSpeedDeg);

                    // Throughout 2s observation require inContact and linear<=0.03 and angular<=3 each sample
                    if (!inContact)
                    {
                        obsContactLost = true;
                        if (failureReason == null)
                            failureReason = $"Observation contact lost at obsElapsed={obsElapsed:F3}s (unsupported motion)";
                        break;
                    }

                    if (linSpeed > 0.03f)
                    {
                        if (failureReason == null)
                            failureReason = $"Observation linear speed excursion: {linSpeed:F4} m/s exceeded 0.03 m/s at obsElapsed={obsElapsed:F3}s";
                        break;
                    }

                    if (angSpeedDeg > 3.0f)
                    {
                        if (failureReason == null)
                            failureReason = $"Observation angular speed excursion: {angSpeedDeg:F2} deg/s exceeded 3.0 deg/s at obsElapsed={obsElapsed:F3}s";
                        break;
                    }

                    if (disp > 0.02f)
                    {
                        displacementExceeded = true;
                        if (failureReason == null)
                            failureReason = $"Displacement {disp:F4} m exceeded 0.02 m during 2s observation";
                        break;
                    }

                    if (obsElapsed >= 2.0f)
                    {
                        observationCompleted = true;
                        currentCaseState = "Completed";
                        break;
                    }
                }
            }

            if (!firstContactAchieved && failureReason == null)
                failureReason = "Contact absence: cube never contacted ramp within timeout";
            else if (!settled && failureReason == null)
                failureReason = "Failed to settle before case ended";
            else if (!observationCompleted && failureReason == null)
                failureReason = "Observation phase incomplete before case ended";

            bool passed = firstContactAchieved && settled && observationCompleted &&
                          !penetrationExceeded && !displacementExceeded && !sanitySpeedExceeded &&
                          !obsContactLost && failureReason == null;

            float finalSettleDuration = settled ? (settledTime - firstContactTime) : -1f;

            var summary = new CaseSummary
            {
                caseIndex = caseIndex,
                caseName = caseName,
                cubeSize = cubeSize,
                massKg = massKg,
                slopeDegrees = slopeDegrees,
                passed = passed,
                status = passed ? "PASS" : ("FAIL: " + failureReason),
                normalClearanceM = normalClearance,
                actualInitialVerticalClearanceM = actualVerticalClearance,
                settleDurationSeconds = finalSettleDuration,
                observationDisplacementM = maxObsDisplacement,
                maxLinearSpeedMPerS = maxLinearSpeed,
                maxAngularSpeedDegPerS = maxAngularSpeedDeg,
                maxObservationLinearSpeedMPerS = maxObsLinearSpeed,
                maxObservationAngularSpeedDegPerS = maxObsAngularSpeedDeg,
                observationContactLost = obsContactLost,
                maxPenetrationM = maxPenetration,
                impactSanityBoundReferenceM = sanityBoundReferenceHeight,
                impactSanityBoundMPerS = impactSanityBound,
                contactAchieved = firstContactAchieved,
                totalSteps = stepIndex
            };

            Debug.Log($"[PhysicalContactDiagnostic] Case {caseIndex + 1}/6: {caseName} => {summary.status}");

            // Cleanup objects
            UnityEngine.Object.DestroyImmediate(cubeGo);
            UnityEngine.Object.DestroyImmediate(rampGo);

            onComplete(summary);
        }

        private void WriteEvidence(List<CaseSummary> results, float totalDuration)
        {
            if (string.IsNullOrEmpty(EvidenceDirectory))
                return;

            try
            {
                Directory.CreateDirectory(EvidenceDirectory);

                // Write per-fixed-step CSV
                File.WriteAllText(Path.Combine(EvidenceDirectory, "fixed_steps.csv"), csvBuilder.ToString());

                // Build summary JSON
                var summary = new DiagnosticSummary
                {
                    status = suiteAllPassed ? "PASS" : "FAIL",
                    applicationVersion = Application.version,
                    hardwareProcessor = SystemInfo.processorType,
                    hardwareGpu = SystemInfo.graphicsDeviceName,
                    hardwareOs = SystemInfo.operatingSystem,
                    hardwareDeviceModel = SystemInfo.deviceModel,
                    physicsGravityX = Physics.gravity.x,
                    physicsGravityY = Physics.gravity.y,
                    physicsGravityZ = Physics.gravity.z,
                    fixedDeltaTime = Time.fixedDeltaTime,
                    staticFriction = diagnosticFrictionMaterial.staticFriction,
                    dynamicFriction = diagnosticFrictionMaterial.dynamicFriction,
                    bounciness = diagnosticFrictionMaterial.bounciness,
                    frictionCombine = diagnosticFrictionMaterial.frictionCombine.ToString(),
                    bounceCombine = diagnosticFrictionMaterial.bounceCombine.ToString(),
                    rampWidth = rampSize.x,
                    rampThickness = rampSize.y,
                    rampLength = rampSize.z,
                    impactSanityBoundReferenceM = 1.0f,
                    totalDurationSeconds = totalDuration,
                    totalCases = results.Count,
                    passedCases = suitePassedCount,
                    failedCases = results.Count - suitePassedCount,
                    cases = results.ToArray()
                };

                string json = JsonUtility.ToJson(summary, true);
                File.WriteAllText(Path.Combine(EvidenceDirectory, "summary.json"), json);

                if (suiteAllPassed)
                {
                    File.WriteAllText(Path.Combine(EvidenceDirectory, "passed.txt"),
                        $"PHYSICAL CONTACT DIAGNOSTIC PASS ({suitePassedCount}/{results.Count} cases)\nTotal duration: {totalDuration:F2}s\n");
                }
                else
                {
                    File.WriteAllText(Path.Combine(EvidenceDirectory, "failed.txt"),
                        $"PHYSICAL CONTACT DIAGNOSTIC FAIL ({suitePassedCount}/{results.Count} passed)\nTotal duration: {totalDuration:F2}s\n");
                }

                Debug.Log($"[PhysicalContactDiagnostic] Evidence written to '{EvidenceDirectory}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhysicalContactDiagnostic] Failed to write evidence: {ex.Message}");
            }
        }

        private void LateUpdate()
        {
            if (aimCamera && targetCamera != null)
            {
                targetCamera.transform.position = fixtureOrigin + new Vector3(-6f, 4.5f, -7f);
                targetCamera.transform.LookAt(fixtureOrigin + new Vector3(0f, 1.2f, 0f));
            }
        }

        private void OnGUI()
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.90f);

            var boxStyle = new GUIStyle(GUI.skin.box);
            GUILayout.BeginArea(new Rect(20, 20, 520, 240), boxStyle);
            GUILayout.Space(4);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan }
            };
            GUILayout.Label("PHYSICAL CONTACT DIAGNOSTIC", titleStyle);

            var textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            if (isRunning)
            {
                GUILayout.Label($"Case {currentCaseIndex + 1}/6: {currentCaseName}", textStyle);
                GUILayout.Label($"State: {currentCaseState} | Elapsed: {currentCaseElapsed:F2}s / 10.0s", textStyle);
                GUILayout.Label($"Linear Speed: {currentLinearSpeed:F4} m/s (limit <= 0.03)", textStyle);
                GUILayout.Label($"Angular Speed: {currentAngularSpeedDeg:F2} deg/s (limit <= 3.0)", textStyle);
                GUILayout.Label($"Penetration: {currentPenetration:F4} m (limit <= 0.01)", textStyle);
                GUILayout.Label($"Observation Disp: {currentDisplacement:F4} m (limit <= 0.02)", textStyle);
            }
            else if (suiteCompleted)
            {
                var termStyle = new GUIStyle(titleStyle)
                {
                    normal = { textColor = suiteAllPassed ? Color.green : Color.red }
                };
                GUILayout.Label($"TERMINAL STATUS: {(suiteAllPassed ? "PASS" : "FAIL")}", termStyle);
                GUILayout.Label($"Passed: {suitePassedCount}/6 cases | Total time: {suiteTotalTime:F1}s", textStyle);
                if (autoQuitCountdown > 0f)
                {
                    GUILayout.Label($"Auto-exit in: {autoQuitCountdown:F1}s", textStyle);
                }
            }

            GUILayout.EndArea();
            GUI.backgroundColor = oldBg;
        }

        private void RestoreCameraState()
        {
            aimCamera = false;
            if (cameraControls != null)
            {
                cameraControls.SuppressView = previousSuppressView;
            }
            if (previewCamera != null)
            {
                previewCamera.ExternalView = previousExternalView;
            }
        }

        private void OnDestroy()
        {
            RestoreCameraState();

            if (fixtureRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(fixtureRoot);
            }
        }
    }
}
