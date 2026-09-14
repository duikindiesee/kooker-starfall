using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CityLife.World
{
    /// <summary>The same player movement methods serve keyboard input and the optional evidence route.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class IslandExplorer : MonoBehaviour
    {
        private enum TravelMode { Fly, Walk, Orbit }
        private const float EyeHeight = 1.85f;
        private const float ShoreLimit = 1.2f;
        private const float MaximumWalkGradient = 0.85f;
        private IslandField field;
        private Camera worldCamera;
        private IslandRenderer islandRenderer;
        private TravelMode mode;
        private float yaw, pitch, flightSpeed = 85f, orbitDistance;
        private Vector3 orbitTarget, reserve;
        private bool ready, showHud = true, night, looking, automatedRoute;
        private readonly bool isolatedSmoke = Array.IndexOf(Environment.GetCommandLineArgs(), "-citylifeSmoke") >= 0;
        private string currentView = "Island vista", toast = "", benchmarkPhase = "";
        private float toastUntil, initializedAt, nextStatsUpdate;
        private double travelledMeters;
        private int viewChanges, blockedWalkSteps;
        private float maximumGroundPenetration;
        private readonly Queue<float> recentFrames = new Queue<float>(360);
        private FrameStats recentStats = new FrameStats();
        private readonly List<float> benchmarkFrames = new List<float>(4096);
        private readonly List<PhaseResult> benchmarkPhases = new List<PhaseResult>();
        private readonly List<string> captures = new List<string>();
        private readonly List<ScreenshotCheck> captureChecks = new List<ScreenshotCheck>();
        private bool benchmarkRunning, benchmarkSampling;
        private string evidenceDirectory, runPrefix;
        private GUIStyle titleStyle, eyebrowStyle, bodyStyle, smallStyle, numberStyle, buttonStyle, activeButtonStyle;
        private Texture2D panelTexture, buttonTexture, activeTexture;
        private Vector3 previousKeyboardDirection;
        private bool previousKeyboardFast;
        private int runtimeErrorCount;
        private string firstRuntimeError = "";
        private RenderTexture offscreenTarget;
        private UniversalRenderPipeline.SingleCameraRequest offscreenRequest;
        private int offscreenRenderedFrames;
        private bool offscreenRenderingFailed;
        private string RenderPath => isolatedSmoke ? "urp-single-camera-offscreen" : "player-framebuffer";
        public string CurrentMode => mode.ToString();
        public double TravelledMeters => travelledMeters;
        public int ViewChanges => viewChanges;
        public Vector3 CameraPosition => worldCamera != null ? worldCamera.transform.position : Vector3.zero;

        public void Initialize(IslandField islandField, Camera camera, IslandRenderer renderer)
        {
            field = islandField;
            worldCamera = camera;
            islandRenderer = renderer;
            reserve = FindReserve();
            worldCamera.nearClipPlane = 0.2f;
            worldCamera.farClipPlane = field.Definition.Width * 3f;
            worldCamera.fieldOfView = 60f;
            initializedAt = Time.realtimeSinceStartup;
            evidenceDirectory = Path.Combine(Application.persistentDataPath, "Evidence");
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-citylifeEvidence" && Path.IsPathRooted(args[i + 1]))
                    evidenceDirectory = Path.GetFullPath(args[i + 1]);
            }
            if (isolatedSmoke && !InitializeOffscreenRendering()) return;
            ready = true;
            SetView(0);
            Notice("Welcome to CityLife. Hold right mouse to look; WASD to explore.", 8f);
            if (isolatedSmoke)
                StartCoroutine(RunBenchmark(true, true));
        }

        private bool InitializeOffscreenRendering()
        {
            offscreenTarget = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "CityLife isolated evidence target",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            offscreenRequest = new UniversalRenderPipeline.SingleCameraRequest { destination = offscreenTarget };
            if (!offscreenTarget.Create() || !RenderPipeline.SupportsRenderRequest(worldCamera, offscreenRequest))
            {
                offscreenRenderingFailed = true;
                Debug.LogError("CITYLIFE_OFFSCREEN_UNSUPPORTED: render target creation or URP SingleCameraRequest support failed.");
                Application.Quit(4);
                return false;
            }
            worldCamera.enabled = false;
            worldCamera.aspect = offscreenTarget.width / (float)offscreenTarget.height;
            Debug.Log("CITYLIFE_RENDER_PATH urp-single-camera-offscreen 1600x900; main camera automatic rendering disabled; scene only, no HUD or desktop presentation.");
            return true;
        }

        private void LateUpdate()
        {
            if (!ready || !isolatedSmoke || offscreenRenderingFailed || offscreenTarget == null) return;
            try
            {
                // Execution order 100 runs after the renderer queues this frame's terrain/flora.
                // Every route frame renders, so timings include the offscreen graphics workload.
                RenderPipeline.SubmitRenderRequest(worldCamera, offscreenRequest);
                offscreenRenderedFrames++;
            }
            catch (Exception error)
            {
                offscreenRenderingFailed = true;
                Debug.LogException(error);
            }
        }

        private void Update()
        {
            if (!ready) return;
            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (Time.realtimeSinceStartup - initializedAt > 4f && frameMs > 0f)
            {
                recentFrames.Enqueue(frameMs);
                if (recentFrames.Count > 360) recentFrames.Dequeue();
                if (benchmarkSampling) benchmarkFrames.Add(frameMs);
            }
            if (Time.realtimeSinceStartup >= nextStatsUpdate)
            {
                recentStats = FrameStats.From(recentFrames.ToArray());
                nextStatsUpdate = Time.realtimeSinceStartup + 0.5f;
            }
            if (!automatedRoute && !isolatedSmoke) ReadInput();
            ValidateCameraFloor();
        }

        private void ReadInput()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard != null)
            {
                if (keyboard.homeKey.wasPressedThisFrame) PerformInput("keyboard", "Home: vista", () => SetView(0));
                if (keyboard.digit1Key.wasPressedThisFrame) PerformInput("keyboard", "1: landing", () => SetView(1));
                if (keyboard.digit2Key.wasPressedThisFrame) PerformInput("keyboard", "2: highlands", () => SetView(2));
                if (keyboard.digit3Key.wasPressedThisFrame) PerformInput("keyboard", "3: reserve", () => SetView(3));
                if (keyboard.digit4Key.wasPressedThisFrame) PerformInput("keyboard", "4: ground detail", () => SetView(4));
                if (keyboard.fKey.wasPressedThisFrame) PerformInput("keyboard", "F: walk/fly", ToggleWalkMode);
                if (keyboard.oKey.wasPressedThisFrame) PerformInput("keyboard", "O: orbit", ToggleOrbit);
                if (keyboard.hKey.wasPressedThisFrame) PerformInput("keyboard", "H: HUD visibility", () => showHud = !showHud);
                if (keyboard.nKey.wasPressedThisFrame) PerformInput("keyboard", "N: lighting", ToggleNight);
                if (keyboard.f12Key.wasPressedThisFrame) PerformInput("keyboard", "F12: capture", CaptureEvidence);
                if (keyboard.f9Key.wasPressedThisFrame)
                {
                    bool automatic = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                    PerformInput("keyboard", automatic ? "Shift+F9: automatic route" : "F9: benchmark", () => StartBenchmark(automatic));
                }
                if (keyboard.escapeKey.wasPressedThisFrame) PerformInput("keyboard", "Escape: release pointer", ReleaseMouse);
                Vector3 direction = new Vector3(
                    (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                    (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
                bool fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                Move(direction, Mathf.Min(Time.unscaledDeltaTime, 0.1f), fast);
                if (direction != previousKeyboardDirection || (direction != Vector3.zero && fast != previousKeyboardFast))
                {
                    string action = direction == Vector3.zero ? "movement stopped" : previousKeyboardDirection == Vector3.zero ? "movement started" : "movement changed";
                    RecordInput("keyboard", action, direction, fast);
                    previousKeyboardDirection = direction;
                    previousKeyboardFast = fast;
                }
            }
            if (mouse == null) return;
            if (mouse.rightButton.wasPressedThisFrame)
            {
                looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                RecordInput("mouse", "right button: look started");
            }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                ReleaseMouse();
                RecordInput("mouse", "right button: look stopped");
            }
            if (looking)
            {
                Vector2 delta = mouse.delta.ReadValue();
                ApplyLook(delta.x * 0.13f, -delta.y * 0.13f);
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (mode == TravelMode.Orbit)
                {
                    orbitDistance = Mathf.Clamp(orbitDistance * Mathf.Exp(-scroll * 0.0015f), 12f, field.Definition.Width * 1.8f);
                    UpdateOrbit();
                }
                else if (mode == TravelMode.Fly)
                    flightSpeed = Mathf.Clamp(flightSpeed * Mathf.Exp(scroll * 0.001f), 10f, 350f);
            }
        }

        public void Move(Vector3 localDirection, float deltaSeconds, bool fast)
        {
            if (!ready || localDirection.sqrMagnitude < 0.0001f || deltaSeconds <= 0f) return;
            localDirection = Vector3.ClampMagnitude(localDirection, 1f);
            Vector3 before = worldCamera.transform.position;
            Quaternion planarRotation = Quaternion.Euler(0f, yaw, 0f);
            float speed = mode == TravelMode.Walk ? (fast ? 14f : 6f) : flightSpeed * (fast ? 3f : 1f);
            Vector3 motion;
            if (mode == TravelMode.Walk)
            {
                motion = planarRotation * new Vector3(localDirection.x, 0f, localDirection.z) * (speed * deltaSeconds);
                int steps = Mathf.Max(1, Mathf.CeilToInt(motion.magnitude));
                Vector3 position = before;
                for (int i = 0; i < steps; i++)
                {
                    Vector3 candidate = ClampHorizontal(position + motion / steps);
                    float ground = field.Ground(candidate.x, candidate.z);
                    float previousGround = field.Ground(position.x, position.z);
                    float distance = new Vector2(candidate.x - position.x, candidate.z - position.z).magnitude;
                    if (ground < ShoreLimit || Mathf.Abs(ground - previousGround) > distance * MaximumWalkGradient + 0.03f)
                    {
                        blockedWalkSteps++;
                        if (Time.realtimeSinceStartup > toastUntil)
                            Notice(ground < ShoreLimit ? "Shoreline reached. Press F to fly across the water." : "Steep ground. Choose a gentler route or press F to fly.", 3f);
                        break;
                    }
                    candidate.y = ground + EyeHeight;
                    position = candidate;
                }
                worldCamera.transform.position = position;
            }
            else if (mode == TravelMode.Orbit)
            {
                motion = planarRotation * new Vector3(localDirection.x, 0f, localDirection.z) * (speed * deltaSeconds);
                orbitTarget = ClampHorizontal(orbitTarget + motion);
                orbitTarget.y = Mathf.Max(0f, field.Ground(orbitTarget.x, orbitTarget.z));
                UpdateOrbit();
            }
            else
            {
                motion = (worldCamera.transform.right * localDirection.x + Vector3.up * localDirection.y + worldCamera.transform.forward * localDirection.z) * (speed * deltaSeconds);
                Vector3 position = ClampHorizontal(before + motion, 1.5f);
                position.y = Mathf.Clamp(position.y, Mathf.Max(3f, field.Ground(position.x, position.z) + EyeHeight), field.Definition.Width * 1.8f);
                worldCamera.transform.position = position;
            }
            travelledMeters += Vector3.Distance(before, worldCamera.transform.position);
        }

        public void ApplyLook(float horizontalDegrees, float verticalDegrees)
        {
            yaw += horizontalDegrees;
            pitch = Mathf.Clamp(pitch + verticalDegrees, mode == TravelMode.Orbit ? 7f : -85f, 85f);
            if (mode == TravelMode.Orbit) UpdateOrbit();
            else worldCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        public void SetView(int view)
        {
            if (!ready) return;
            ReleaseMouse();
            mode = TravelMode.Fly;
            float width = field.Definition.Width;
            Vector3 target;
            Vector3 position;
            switch (view)
            {
                case 1:
                    currentView = "Landing coast";
                    target = field.Landing + Vector3.up * 5f;
                    position = target + new Vector3(-140f, 105f, -180f);
                    break;
                case 2:
                    currentView = "Highlands";
                    target = field.Summit;
                    position = target + new Vector3(width * 0.11f, 180f, -width * 0.12f);
                    break;
                case 3:
                    currentView = "Neighbourhood reserve";
                    target = reserve;
                    position = target + new Vector3(-width * 0.10f, 220f, -width * 0.10f);
                    break;
                case 4:
                    currentView = "Ground detail";
                    target = islandRenderer.DetailFocus + Vector3.up * 0.35f;
                    position = islandRenderer.DetailFocus + new Vector3(-2.2f, 0f, -3f);
                    position.y = field.Ground(position.x, position.z) + EyeHeight;
                    break;
                default:
                    currentView = "Island vista";
                    target = new Vector3(0f, field.MaxHeight * 0.25f, 0f);
                    position = new Vector3(-width * 0.34f, width * 0.57f, -width * 0.63f);
                    break;
            }
            position.y = Mathf.Max(position.y, field.Ground(position.x, position.z) + (view == 4 ? EyeHeight : 12f));
            worldCamera.transform.position = position;
            worldCamera.transform.LookAt(target);
            ReadCameraAngles();
            orbitTarget = target;
            orbitDistance = Vector3.Distance(position, target);
            viewChanges++;
            Debug.Log("CITYLIFE_VIEW " + currentView + " automatic=" + automatedRoute + " position=" + position.ToString("F2"));
        }

        public void ToggleWalkMode()
        {
            if (!ready) return;
            if (mode == TravelMode.Walk)
            {
                mode = TravelMode.Fly;
                Notice("Flight mode. Q / E change height; scroll adjusts speed.");
                return;
            }
            Vector3 position = ClampHorizontal(worldCamera.transform.position);
            bool movedToFallback = false;
            if (!IsSafeWalkPoint(field, position))
            {
                if (!TryFindSafeWalkLanding(field, field.Landing, out position))
                {
                    mode = TravelMode.Fly;
                    Notice("No safe walking ground is available after the saved terrain edits; remaining in flight mode.", 7f);
                    return;
                }
                movedToFallback = true;
            }
            mode = TravelMode.Walk;
            Notice(movedToFallback ? "Walking from nearby safe ground; the previous position or saved landing was unsafe." : "Walking. WASD to move; Shift to run; F returns to flight.", movedToFallback ? 6f : 4f);
            position.y = field.Ground(position.x, position.z) + EyeHeight;
            worldCamera.transform.position = position;
            pitch = Mathf.Clamp(pitch, -40f, 35f);
            worldCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void ToggleOrbit()
        {
            if (mode == TravelMode.Orbit)
            {
                mode = TravelMode.Fly;
                ReadCameraAngles();
                Notice("Flight mode.");
                return;
            }
            Vector3 origin = worldCamera.transform.position;
            Vector3 direction = worldCamera.transform.forward;
            Vector3 target = origin + direction * 300f;
            bool hit = false;
            for (float distance = 10f; distance <= field.Definition.Width * 1.5f; distance += 10f)
            {
                Vector3 sample = origin + direction * distance;
                if (sample.y <= Mathf.Max(0f, field.Ground(sample.x, sample.z)))
                {
                    target = sample;
                    hit = true;
                    break;
                }
            }
            if (!hit && direction.y > -0.05f) target = origin + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 150f;
            orbitTarget = ClampHorizontal(target);
            orbitTarget.y = Mathf.Max(0f, field.Ground(orbitTarget.x, orbitTarget.z));
            orbitDistance = Mathf.Clamp(Vector3.Distance(origin, orbitTarget), 20f, field.Definition.Width * 1.8f);
            mode = TravelMode.Orbit;
            pitch = Mathf.Max(15f, pitch);
            UpdateOrbit();
            Notice("Orbit mode. Hold right mouse to circle; scroll to zoom; WASD moves the focus.");
        }

        private void UpdateOrbit()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 position = orbitTarget - rotation * Vector3.forward * orbitDistance;
            position.y = Mathf.Max(position.y, Mathf.Max(3f, field.Ground(position.x, position.z) + EyeHeight));
            worldCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(orbitTarget - position));
        }

        private void ToggleNight()
        {
            night = !night;
            islandRenderer.SetNight(night);
            Notice(night ? "Night lighting." : "Daylight.");
        }

        private Vector3 ClampHorizontal(Vector3 position, float widthFraction = 0.5f)
        {
            float extent = field.Definition.Width * widthFraction - 2f;
            position.x = Mathf.Clamp(position.x, -extent, extent);
            position.z = Mathf.Clamp(position.z, -extent, extent);
            return position;
        }

        private float SlopeAt(Vector3 point)
        {
            return SlopeAt(field, point);
        }

        public static bool IsSafeWalkPoint(IslandField island, Vector3 point)
        {
            if (island == null) return false;
            return island.Ground(point.x, point.z) >= ShoreLimit && SlopeAt(island, point) <= MaximumWalkGradient;
        }

        public static bool TryFindSafeWalkLanding(IslandField island, Vector3 preferred, out Vector3 landing)
        {
            landing = preferred;
            if (island == null) return false;
            float extent = island.Definition.Width * 0.5f - 2f;
            preferred.x = Mathf.Clamp(preferred.x, -extent, extent);
            preferred.z = Mathf.Clamp(preferred.z, -extent, extent);
            if (IsSafeWalkPoint(island, preferred))
            {
                preferred.y = island.Ground(preferred.x, preferred.z);
                landing = preferred;
                return true;
            }
            float step = (float)island.Definition.cellMetres;
            int rings = Math.Min(128, island.Definition.cells / 2);
            for (int ring = 1; ring <= rings; ring++)
            {
                for (int z = -ring; z <= ring; z++)
                for (int x = -ring; x <= ring; x++)
                {
                    if (Math.Abs(x) != ring && Math.Abs(z) != ring) continue;
                    var candidate = new Vector3(Mathf.Clamp(preferred.x + x * step, -extent, extent), 0f, Mathf.Clamp(preferred.z + z * step, -extent, extent));
                    if (!IsSafeWalkPoint(island, candidate)) continue;
                    candidate.y = island.Ground(candidate.x, candidate.z);
                    landing = candidate;
                    return true;
                }
            }
            return false;
        }

        private static float SlopeAt(IslandField island, Vector3 point)
        {
            float dx = (island.Ground(point.x + 2f, point.z) - island.Ground(point.x - 2f, point.z)) * 0.25f;
            float dz = (island.Ground(point.x, point.z + 2f) - island.Ground(point.x, point.z - 2f)) * 0.25f;
            return new Vector2(dx, dz).magnitude;
        }

        private Vector3 FindReserve()
        {
            float width = field.Definition.Width;
            Vector3 chosen = field.Landing;
            float best = float.NegativeInfinity;
            for (int z = -12; z <= 12; z++)
            for (int x = -12; x <= 12; x++)
            {
                Vector3 point = new Vector3(x * width / 32f, 0f, z * width / 32f);
                point.y = field.Ground(point.x, point.z);
                if (point.y < 6f || point.y > field.MaxHeight * 0.55f) continue;
                float slope = SlopeAt(point);
                if (slope > 0.15f) continue;
                float distance = Vector3.Distance(point, field.Landing);
                float score = Mathf.Min(distance, width * 0.25f) - slope * width - Mathf.Abs(point.y - 25f) * 2f;
                if (score > best) { best = score; chosen = point; }
            }
            return chosen;
        }

        private void ReadCameraAngles()
        {
            Vector3 angles = worldCamera.transform.eulerAngles;
            yaw = angles.y;
            pitch = angles.x > 180f ? angles.x - 360f : angles.x;
        }

        private void ValidateCameraFloor()
        {
            Vector3 position = worldCamera.transform.position;
            float ground = field.Ground(position.x, position.z);
            maximumGroundPenetration = Mathf.Max(maximumGroundPenetration, ground + 0.1f - position.y);
            if (position.y < ground + EyeHeight)
            {
                position.y = ground + EyeHeight;
                worldCamera.transform.position = position;
            }
        }

        private void ReleaseMouse()
        {
            looking = false;
            if (isolatedSmoke) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnEnable() { Application.logMessageReceived += ObserveRuntimeLog; }
        private void OnApplicationFocus(bool focus) { if (!focus) ReleaseMouse(); }
        private void OnDisable()
        {
            Application.logMessageReceived -= ObserveRuntimeLog;
            ReleaseMouse();
        }
        private void ObserveRuntimeLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            runtimeErrorCount++;
            if (string.IsNullOrEmpty(firstRuntimeError)) firstRuntimeError = condition.Length > 1200 ? condition.Substring(0, 1200) : condition;
        }
        private void Notice(string message, float seconds = 4f) { toast = message; toastUntil = Time.realtimeSinceStartup + seconds; }

        private void PerformInput(string source, string action, Action operation)
        {
            operation();
            RecordInput(source, action);
        }

        private void RecordInput(string source, string action, Vector3 localDirection = default, bool fast = false)
        {
            Debug.Log("CITYLIFE_INPUT " + JsonUtility.ToJson(new InputEvidence
            {
                timestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                source = source,
                action = action,
                automaticRoute = automatedRoute,
                frame = Time.frameCount,
                mode = mode.ToString(),
                view = currentView,
                position = worldCamera.transform.position,
                rotationDegrees = worldCamera.transform.eulerAngles,
                travelledMeters = (float)travelledMeters,
                localDirection = localDirection,
                fast = fast,
                hudVisible = showHud,
                night = night
            }));
        }

        public void CaptureEvidence()
        {
            StartCoroutine(CaptureScreenshot("manual-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)));
        }

        private IEnumerator CaptureScreenshot(string name)
        {
            Directory.CreateDirectory(evidenceDirectory);
            string path = Path.Combine(evidenceDirectory, name + ".png");
            yield return new WaitForEndOfFrame();
            Texture2D screenshot = isolatedSmoke ? ReadOffscreenImage() : ScreenCapture.CaptureScreenshotAsTexture();
            if (screenshot == null)
            {
                Debug.LogError("CITYLIFE_SCREENSHOT_FAILED " + path + " reason=no framebuffer texture");
                Notice("Screenshot failed: no framebuffer image was available.", 6f);
                yield break;
            }
            ScreenshotCheck check = CheckScreenshot(screenshot, path);
            check.renderPath = RenderPath;
            check.includesHud = !isolatedSmoke && showHud;
            check.renderedFrames = offscreenRenderedFrames;
            File.WriteAllBytes(path, screenshot.EncodeToPNG());
            Destroy(screenshot);
            captures.Add(path);
            captureChecks.Add(check);
            Debug.Log("CITYLIFE_SCREENSHOT " + path + " contentCheck=" + JsonUtility.ToJson(check));
            Notice(check.nonUniformContent ? "Screenshot saved to Evidence / " + name + ".png" : "Screenshot retained, but its frame is blank. Inspect the renderer log.", 5f);
        }

        private Texture2D ReadOffscreenImage()
        {
            if (offscreenTarget == null || !offscreenTarget.IsCreated() || offscreenRenderedFrames == 0) return null;
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(offscreenTarget.width, offscreenTarget.height, TextureFormat.RGB24, false, false);
            try
            {
                RenderTexture.active = offscreenTarget;
                image.ReadPixels(new Rect(0, 0, offscreenTarget.width, offscreenTarget.height), 0, 0, false);
                image.Apply(false, false);
                return image;
            }
            catch
            {
                Destroy(image);
                throw;
            }
            finally { RenderTexture.active = previous; }
        }

        private static ScreenshotCheck CheckScreenshot(Texture2D texture, string path)
        {
            var check = new ScreenshotCheck { path = path, widthPixels = texture.width, heightPixels = texture.height, minimumLuminance = 1f };
            float total = 0f;
            // A small evenly spaced sample detects uniform or blank frames without treating
            // image validity as a substitute for a person's visual review of the island.
            for (int y = 0; y < 18; y++)
            for (int x = 0; x < 32; x++)
            {
                Color color = texture.GetPixel(Mathf.Min(texture.width - 1, (int)((x + 0.5f) * texture.width / 32f)), Mathf.Min(texture.height - 1, (int)((y + 0.5f) * texture.height / 18f)));
                float luminance = color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
                check.minimumLuminance = Mathf.Min(check.minimumLuminance, luminance);
                check.maximumLuminance = Mathf.Max(check.maximumLuminance, luminance);
                total += luminance;
                check.sampledPixels++;
            }
            check.meanLuminance = total / check.sampledPixels;
            check.nonUniformContent = check.maximumLuminance > 0.025f && check.maximumLuminance - check.minimumLuminance > 0.008f;
            return check;
        }

        public void StartBenchmark(bool automatic = false)
        {
            if (!benchmarkRunning)
            {
                if (automatic) ReleaseMouse();
                StartCoroutine(RunBenchmark(automatic, false));
            }
        }

        private IEnumerator RunBenchmark(bool automatic, bool quitWhenFinished)
        {
            if (benchmarkRunning) yield break;
            benchmarkRunning = true;
            automatedRoute = automatic;
            int errorsBeforeBenchmark = runtimeErrorCount;
            benchmarkFrames.Clear();
            benchmarkPhases.Clear();
            captures.Clear();
            captureChecks.Clear();
            runPrefix = (automatic ? "smoke-" : "benchmark-") + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            benchmarkPhase = "Warmup";
            float warmupUntil = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < warmupUntil) yield return null;
            double distanceBefore = travelledMeters;
            int blocksBefore = blockedWalkSteps;
            maximumGroundPenetration = 0f;
            if (automatic)
            {
                SetView(0);
                yield return SamplePhase("Overview", 8f, Vector3.zero);
                yield return CaptureScreenshot(runPrefix + "-01-overview");
                SetView(3);
                yield return SamplePhase("Flight", 8f, new Vector3(0.38f, 0.08f, 0.22f));
                yield return CaptureScreenshot(runPrefix + "-02-flight");
                SetView(1);
                worldCamera.transform.position = field.Landing + Vector3.up * 20f;
                ToggleWalkMode();
                yaw = FindWalkingHeading(field.Landing);
                pitch = -3f;
                worldCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                yield return SamplePhase("Walking", 8f, Vector3.forward);
                yield return CaptureScreenshot(runPrefix + "-03-walking");
                SetView(4);
                // A close view of an existing procedural tuft; no scenery is added for the image.
                yield return new WaitForSecondsRealtime(1f);
                yield return CaptureScreenshot(runPrefix + "-04-ground-detail");
            }
            else
            {
                Notice("Benchmark collecting for 24 seconds. Explore normally; screenshots and JSON follow.", 7f);
                yield return SamplePhase("Manual exploration", 24f, Vector3.zero);
                yield return CaptureScreenshot(runPrefix + "-manual");
            }
            benchmarkSampling = false;
            benchmarkPhase = "Saving evidence";
            float captureDeadline = Time.realtimeSinceStartup + 6f;
            while (!AllCapturesExist() && Time.realtimeSinceStartup < captureDeadline) yield return null;
            bool screenshotsSaved = AllCapturesExist();
            bool screenshotsHaveContent = captureChecks.Count == captures.Count && captureChecks.Count > 0;
            foreach (ScreenshotCheck check in captureChecks) screenshotsHaveContent &= check.nonUniformContent;
            FrameStats summary = FrameStats.From(benchmarkFrames.ToArray());
            bool routeMoved = !automatic || (benchmarkPhases.Count == 3 && benchmarkPhases[1].movedMeters > 30f && benchmarkPhases[2].movedMeters > 5f);
            bool renderCoverage = !offscreenRenderingFailed;
            if (isolatedSmoke)
                foreach (PhaseResult phase in benchmarkPhases) renderCoverage &= phase.offscreenFrames >= phase.stats.frameCount - 2;
            bool valid = summary.frameCount > 30 && summary.sampleSeconds >= 20f && screenshotsSaved && screenshotsHaveContent && maximumGroundPenetration < 0.1f && routeMoved && runtimeErrorCount == 0 && renderCoverage;
            var report = new BenchmarkReport
            {
                schema = "citylife.unity.exploration-evidence.v1",
                timestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                worldId = field.Definition.worldId,
                generator = field.Definition.generator,
                seed = field.Definition.seed,
                widthMeters = field.Definition.Width,
                landAreaKm2 = field.LandAreaKm2,
                flatAreaKm2 = field.FlatAreaKm2,
                generatedInMilliseconds = field.GenerationMilliseconds,
                automaticRoute = automatic,
                evidenceChecksPassed = valid,
                runtimeErrorsBeforeBenchmark = errorsBeforeBenchmark,
                runtimeErrorsDuringBenchmark = runtimeErrorCount - errorsBeforeBenchmark,
                firstRuntimeError = firstRuntimeError,
                renderPath = RenderPath,
                offscreenRenderedFrames = offscreenRenderedFrames,
                captureScope = isolatedSmoke ? "Offscreen URP scene renders; excludes HUD, desktop presentation, focus and native input." : "Player framebuffer; includes HUD when visible. Native input needs separate verification.",
                validationScope = "Rendered route, frame timings, camera floor, image writes, sampled nonuniform image content, per-phase render coverage and no observed runtime errors. Visual quality requires image review; this is not full gameplay parity.",
                unityVersion = Application.unityVersion,
                operatingSystem = SystemInfo.operatingSystem,
                processor = SystemInfo.processorType,
                logicalProcessors = SystemInfo.processorCount,
                memoryMiB = SystemInfo.systemMemorySize,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsMemoryMiB = SystemInfo.graphicsMemorySize,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                widthPixels = isolatedSmoke ? offscreenTarget.width : Screen.width,
                heightPixels = isolatedSmoke ? offscreenTarget.height : Screen.height,
                vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                movedMeters = (float)(travelledMeters - distanceBefore),
                blockedWalkSteps = blockedWalkSteps - blocksBefore,
                maximumGroundPenetrationMeters = maximumGroundPenetration,
                visibleChunksAtEnd = islandRenderer.VisibleChunks,
                highDetailChunksAtEnd = islandRenderer.HighDetailChunks,
                renderedTrianglesAtEnd = islandRenderer.RenderedTriangles,
                floraInstancesAtEnd = islandRenderer.FloraInstances,
                summary = summary,
                phases = benchmarkPhases.ToArray(),
                screenshots = captures.ToArray(),
                screenshotChecks = captureChecks.ToArray(),
                frameMilliseconds = benchmarkFrames.ToArray()
            };
            string reportPath = Path.Combine(evidenceDirectory, runPrefix + ".json");
            bool reportSaved = TryWriteEvidence(
                () => { Directory.CreateDirectory(evidenceDirectory); File.WriteAllText(reportPath, JsonUtility.ToJson(report, true)); },
                RestoreBenchmarkControls,
                out string writeError);
            if (reportSaved)
                Debug.Log("CITYLIFE_BENCHMARK " + reportPath + " passed=" + valid + " meanMs=" + summary.meanMs.ToString("F2", CultureInfo.InvariantCulture) + " p95Ms=" + summary.p95Ms.ToString("F2", CultureInfo.InvariantCulture));
            else
                Debug.LogError("CITYLIFE_BENCHMARK_WRITE_FAILED " + writeError);
            Notice(reportSaved ? ((valid ? "Evidence checks passed. " : "Evidence checks failed; inspect the saved report. ") + "Mean " + summary.meanMs.ToString("F1") + " ms; p95 " + summary.p95Ms.ToString("F1") + " ms.") : "Evidence report could not be saved; controls were restored. " + writeError, 10f);
            if (quitWhenFinished && !Application.isEditor)
            {
                yield return null;
                Application.Quit(valid && reportSaved ? 0 : 3);
            }
        }

        private void RestoreBenchmarkControls()
        {
            benchmarkSampling = false;
            benchmarkRunning = false;
            automatedRoute = false;
            benchmarkPhase = "";
        }

        public static bool TryWriteEvidence(Action write, Action restoreControls, out string error)
        {
            error = "";
            try
            {
                write();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                restoreControls();
            }
        }

        private IEnumerator SamplePhase(string phase, float seconds, Vector3 automaticMotion)
        {
            benchmarkPhase = phase;
            if (automatedRoute) RecordInput("automatic", phase + ": route started", automaticMotion);
            int firstFrame = benchmarkFrames.Count;
            int firstRenderedFrame = offscreenRenderedFrames;
            double firstDistance = travelledMeters;
            Vector3 startPosition = worldCamera.transform.position;
            float started = Time.realtimeSinceStartup;
            benchmarkSampling = true;
            while (Time.realtimeSinceStartup - started < seconds)
            {
                if (automatedRoute) Move(automaticMotion, Mathf.Min(Time.unscaledDeltaTime, 0.1f), false);
                yield return null;
            }
            benchmarkSampling = false;
            var phaseFrames = benchmarkFrames.GetRange(firstFrame, benchmarkFrames.Count - firstFrame).ToArray();
            benchmarkPhases.Add(new PhaseResult
            {
                name = phase,
                mode = mode.ToString(),
                startPosition = startPosition,
                endPosition = worldCamera.transform.position,
                movedMeters = (float)(travelledMeters - firstDistance),
                visibleChunks = islandRenderer.VisibleChunks,
                highDetailChunks = islandRenderer.HighDetailChunks,
                renderedTriangles = islandRenderer.RenderedTriangles,
                offscreenFrames = offscreenRenderedFrames - firstRenderedFrame,
                stats = FrameStats.From(phaseFrames)
            });
            if (automatedRoute) RecordInput("automatic", phase + ": route finished");
        }

        private float FindWalkingHeading(Vector3 start)
        {
            float bestHeading = 0f, bestLength = -1f;
            for (int heading = 0; heading < 360; heading += 15)
            {
                Vector3 direction = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
                float previous = field.Ground(start.x, start.z);
                float length = 0f;
                for (int step = 1; step <= 60; step++)
                {
                    Vector3 point = start + direction * step;
                    float height = field.Ground(point.x, point.z);
                    if (height < ShoreLimit || Mathf.Abs(height - previous) > MaximumWalkGradient) break;
                    previous = height;
                    length = step;
                }
                if (length > bestLength) { bestLength = length; bestHeading = heading; }
            }
            return bestHeading;
        }

        private bool AllCapturesExist()
        {
            if (captures.Count == 0) return false;
            foreach (string path in captures)
                if (!File.Exists(path) || new FileInfo(path).Length < 1024) return false;
            return true;
        }

        private void EnsureStyles()
        {
            if (panelTexture != null) return;
            panelTexture = Solid(new Color(0.025f, 0.07f, 0.105f, 0.91f));
            buttonTexture = Solid(new Color(0.09f, 0.19f, 0.23f, 0.95f));
            activeTexture = Solid(new Color(0.13f, 0.43f, 0.40f, 0.98f));
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 29, fontStyle = FontStyle.Bold, padding = new RectOffset(0, 0, 0, 0) };
            titleStyle.normal.textColor = Color.white;
            eyebrowStyle = new GUIStyle(titleStyle) { fontSize = 10, fontStyle = FontStyle.Bold };
            eyebrowStyle.normal.textColor = new Color(0.45f, 0.87f, 0.77f);
            bodyStyle = new GUIStyle(titleStyle) { fontSize = 13, fontStyle = FontStyle.Normal };
            bodyStyle.normal.textColor = new Color(0.91f, 0.95f, 0.94f);
            smallStyle = new GUIStyle(bodyStyle) { fontSize = 11 };
            smallStyle.normal.textColor = new Color(0.65f, 0.76f, 0.77f);
            numberStyle = new GUIStyle(bodyStyle) { fontSize = 17, fontStyle = FontStyle.Bold };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Normal, border = new RectOffset(0, 0, 0, 0), padding = new RectOffset(6, 6, 3, 3) };
            buttonStyle.normal.background = buttonTexture;
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.background = activeTexture;
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.background = activeTexture;
            buttonStyle.active.textColor = Color.white;
            activeButtonStyle = new GUIStyle(buttonStyle);
            activeButtonStyle.normal.background = activeTexture;
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void OnGUI()
        {
            if (!ready || !showHud || isolatedSmoke) return;
            EnsureStyles();
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.4f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale, height = Screen.height / scale;
            GUI.DrawTexture(new Rect(22, 22, 332, 186), panelTexture);
            GUI.Label(new Rect(40, 38, 292, 17), "A NEW CHAPTER  /  UNITY WORLD FOUNDATION", eyebrowStyle);
            GUI.Label(new Rect(40, 60, 292, 42), "CITYLIFE", titleStyle);
            GUI.Label(new Rect(40, 105, 292, 24), currentView, bodyStyle);
            GUI.Label(new Rect(40, 136, 292, 20), string.Format(CultureInfo.InvariantCulture, "{0:0.0} km across  ·  {1:0.00} km² of land", field.Definition.Width / 1000f, field.LandAreaKm2), bodyStyle);
            GUI.Label(new Rect(40, 163, 292, 30), "SEED " + field.Definition.seed + "  /  " + field.Definition.generator, smallStyle);

            GUI.DrawTexture(new Rect(width - 294, 22, 272, 147), panelTexture);
            GUI.Label(new Rect(width - 276, 39, 234, 18), "LIVE PLAYER  /  " + (night ? "NIGHT" : "DAYLIGHT"), eyebrowStyle);
            string frameSummary = recentStats.frameCount > 0 ? recentStats.meanMs.ToString("F1") + " ms mean  /  " + recentStats.p95Ms.ToString("F1") + " p95" : "Warming up frame samples...";
            GUI.Label(new Rect(width - 276, 67, 242, 23), frameSummary, bodyStyle);
            GUI.Label(new Rect(width - 276, 94, 244, 21), islandRenderer.VisibleChunks + " visible chunks  ·  " + islandRenderer.HighDetailChunks + " near", smallStyle);
            GUI.Label(new Rect(width - 276, 119, 244, 24), (islandRenderer.RenderedTriangles / 1000f).ToString("F0") + "k triangles  ·  " + (travelledMeters / 1000f).ToString("F2") + " km explored", smallStyle);

            Vector3 position = worldCamera.transform.position;
            GUI.DrawTexture(new Rect(22, height - 183, 460, 101), panelTexture);
            GUI.Label(new Rect(40, height - 169, 427, 20), mode.ToString().ToUpperInvariant() + "  /  " + (mode == TravelMode.Walk ? "6 m/s  ·  Shift 14 m/s" : flightSpeed.ToString("F0") + " m/s  ·  Shift ×3"), eyebrowStyle);
            GUI.Label(new Rect(40, height - 141, 427, 20), "WASD move   ·   Hold right mouse to look   ·   Q / E height", bodyStyle);
            GUI.Label(new Rect(40, height - 114, 427, 20), "F walk / fly   ·   O orbit   ·   Scroll zoom / speed   ·   H hide UI", smallStyle);
            GUI.Label(new Rect(width - 380, height - 102, 358, 20), string.Format(CultureInfo.InvariantCulture, "X {0:0}    Z {1:0}    ALT {2:0} m", position.x, position.z, position.y), bodyStyle);

            float buttonX = 22f;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = !benchmarkRunning && !isolatedSmoke;
            if (GUI.Button(new Rect(width - 190, height - 173, 168, 39), "Automatic tour", activeButtonStyle))
                PerformInput("gui", "Automatic tour", () => StartBenchmark(true));
            GUI.Label(new Rect(width - 188, height - 127, 166, 19), "~30 seconds  /  three views", smallStyle);
            GUI.enabled = !automatedRoute && !isolatedSmoke;
            DrawButton(ref buttonX, height, "Home  Vista", 108, () => SetView(0));
            DrawButton(ref buttonX, height, "1  Landing", 106, () => SetView(1));
            DrawButton(ref buttonX, height, "2  Highlands", 115, () => SetView(2));
            DrawButton(ref buttonX, height, "3  Reserve", 106, () => SetView(3));
            DrawButton(ref buttonX, height, "4  Detail", 100, () => SetView(4));
            DrawButton(ref buttonX, height, mode == TravelMode.Walk ? "F  Fly" : "F  Walk", 91, ToggleWalkMode, mode == TravelMode.Walk);
            DrawButton(ref buttonX, height, "O  Orbit", 90, ToggleOrbit, mode == TravelMode.Orbit);
            DrawButton(ref buttonX, height, night ? "N  Day" : "N  Night", 89, ToggleNight, night);
            DrawButton(ref buttonX, height, "F12  Capture", 113, CaptureEvidence);
            if (buttonX + 137 < width) DrawButton(ref buttonX, height, "F9  Benchmark", 128, () => StartBenchmark());
            GUI.enabled = previousEnabled;
            if (benchmarkRunning)
            {
                GUI.DrawTexture(new Rect(width * 0.5f - 192, 23, 384, 57), panelTexture);
                GUI.Label(new Rect(width * 0.5f - 174, 35, 348, 20), "EVIDENCE ROUTE  /  " + benchmarkPhase.ToUpperInvariant(), eyebrowStyle);
                GUI.Label(new Rect(width * 0.5f - 174, 56, 348, 19), "Measured frames: " + benchmarkFrames.Count + "  ·  Images + JSON", smallStyle);
            }
            else if (Time.realtimeSinceStartup < toastUntil)
            {
                GUI.DrawTexture(new Rect(width * 0.5f - 325, height - 237, 650, 39), panelTexture);
                GUI.Label(new Rect(width * 0.5f - 312, height - 227, 628, 26), toast, smallStyle);
            }
            GUI.matrix = previousMatrix;
        }

        private void DrawButton(ref float x, float height, string label, float width, Action action, bool active = false)
        {
            if (GUI.Button(new Rect(x, height - 62, width, 39), label, active ? activeButtonStyle : buttonStyle)) PerformInput("gui", label, action);
            x += width + 7f;
        }

        private void OnDestroy()
        {
            if (offscreenTarget != null)
            {
                offscreenTarget.Release();
                Destroy(offscreenTarget);
                offscreenTarget = null;
            }
            if (panelTexture != null) Destroy(panelTexture);
            if (buttonTexture != null) Destroy(buttonTexture);
            if (activeTexture != null) Destroy(activeTexture);
        }

        [Serializable]
        private sealed class ScreenshotCheck
        {
            public string path, renderPath;
            public int widthPixels, heightPixels, sampledPixels, renderedFrames;
            public float minimumLuminance, maximumLuminance, meanLuminance;
            public bool nonUniformContent, includesHud;
        }

        [Serializable]
        private sealed class InputEvidence
        {
            public string timestampUtc, source, action, mode, view;
            public bool automaticRoute, fast, hudVisible, night;
            public int frame;
            public Vector3 position, rotationDegrees, localDirection;
            public float travelledMeters;
        }

        [Serializable]
        public sealed class FrameStats
        {
            public int frameCount;
            public float sampleSeconds, meanMs, medianMs, p95Ms, p99Ms, worstMs, averageFramesPerSecond;
            public static FrameStats From(float[] values)
            {
                var stats = new FrameStats { frameCount = values.Length };
                if (values.Length == 0) return stats;
                double total = 0;
                foreach (float value in values) total += value;
                Array.Sort(values);
                stats.sampleSeconds = (float)(total / 1000.0);
                stats.meanMs = (float)(total / values.Length);
                stats.medianMs = values[Mathf.Clamp(Mathf.CeilToInt(values.Length * 0.50f) - 1, 0, values.Length - 1)];
                stats.p95Ms = values[Mathf.Clamp(Mathf.CeilToInt(values.Length * 0.95f) - 1, 0, values.Length - 1)];
                stats.p99Ms = values[Mathf.Clamp(Mathf.CeilToInt(values.Length * 0.99f) - 1, 0, values.Length - 1)];
                stats.worstMs = values[values.Length - 1];
                stats.averageFramesPerSecond = stats.meanMs > 0 ? 1000f / stats.meanMs : 0f;
                return stats;
            }
        }

        [Serializable]
        private sealed class PhaseResult
        {
            public string name, mode;
            public Vector3 startPosition, endPosition;
            public float movedMeters;
            public int visibleChunks, highDetailChunks, renderedTriangles, offscreenFrames;
            public FrameStats stats;
        }

        [Serializable]
        private sealed class BenchmarkReport
        {
            public string schema, timestampUtc, worldId, generator;
            public int seed;
            public float widthMeters, landAreaKm2, flatAreaKm2;
            public long generatedInMilliseconds;
            public bool automaticRoute, evidenceChecksPassed;
            public int runtimeErrorsBeforeBenchmark, runtimeErrorsDuringBenchmark;
            public string firstRuntimeError, renderPath, captureScope;
            public int offscreenRenderedFrames;
            public string validationScope, unityVersion, operatingSystem, processor, graphicsDevice, graphicsApi;
            public int logicalProcessors, memoryMiB, graphicsMemoryMiB, widthPixels, heightPixels, vSyncCount, targetFrameRate;
            public float movedMeters, maximumGroundPenetrationMeters;
            public int blockedWalkSteps, visibleChunksAtEnd, highDetailChunksAtEnd, renderedTrianglesAtEnd, floraInstancesAtEnd;
            public FrameStats summary;
            public PhaseResult[] phases;
            public string[] screenshots;
            public ScreenshotCheck[] screenshotChecks;
            public float[] frameMilliseconds;
        }
    }
}
