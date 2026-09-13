using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityLife.World
{
    /// <summary>
    /// Controls only the separately baked local cosmic study. No world generation, networking,
    /// analytics, preferences or saved-world access. Pointer capture always starts with RMB input.
    /// </summary>
    public sealed class CosmicPreviewExplorer : MonoBehaviour
    {
        public Camera Camera;
        public LayerMask GroundMask = ~0;
        public LayerMask CollisionMask = ~0;
        public float LookSensitivity = .10f;
        public float WalkSpeed = 3.6f;
        public float FlySpeed = 12f;

        public string CurrentMode => flying ? "Fly" : "Walk";
        public double TravelledMetres { get; private set; }
        public Vector3 CameraPosition => Camera != null ? Camera.transform.position : Vector3.zero;

        private const float EyeHeight = 1.85f;
        private const float BodyRadius = .28f;
        private const float MaximumSlopeCosine = .64f;
        private const float Skin = .025f;
        private bool ready, flying, looking, previousFast, previousLooking, previousFlying;
        private Vector3 previousInput;
        private Collider lastSweepCollider;
        private float yaw, pitch, messageUntil;
        private string message = "", evidenceDirectory;
        private GUIStyle titleStyle, bodyStyle, statusStyle;

        private void Start()
        {
            if (Camera == null) Camera = GetComponent<Camera>();
            if (Camera == null) Camera = UnityEngine.Camera.main;
            if (Camera == null) { Debug.LogError("COSMIC_PREVIEW: no camera assigned."); enabled = false; return; }
            Vector3 angles = Camera.transform.eulerAngles;
            yaw = angles.y; pitch = angles.x > 180f ? angles.x - 360f : angles.x;
            Vector3 start = ClampStage(Camera.transform.position);
            if (TryGround(start, out RaycastHit ground)) start.y = ground.point.y + EyeHeight;
            else { flying = true; start.y = Mathf.Max(start.y, EyeHeight); Notice("Ground collider unavailable; starting in fly mode."); }
            Camera.transform.position = start;
            previousFlying = flying;
            ResolveEvidenceDirectory();
            ready = true;
            // Deliberately do not touch Cursor here, including when the preview gains focus.
        }

        private void Update()
        {
            if (CosmicPreviewSmoke.Requested || !ready) return;
            if (!Application.isFocused)
            {
                ReleasePointer();
                RecordChangedInput(Vector3.zero, false);
                return;
            }
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            if (escape) ReleasePointer();
            if (mouse == null || mouse.rightButton.wasReleasedThisFrame) ReleasePointer();
            if (!escape && mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (looking && mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                SetLook(yaw + delta.x * LookSensitivity, pitch - delta.y * LookSensitivity);
            }
            Vector3 input = Vector3.zero;
            bool fast = false;
            if (keyboard != null)
            {
                if (keyboard.fKey.wasPressedThisFrame) ToggleMode();
                if (keyboard.f12Key.wasPressedThisFrame) CaptureRequested();
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                input.z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                if (flying) input.y = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
                fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            }
            if (input.sqrMagnitude > 0f) Move(Vector3.ClampMagnitude(input, 1f), fast, Mathf.Min(Time.unscaledDeltaTime, .05f));
            RecordChangedInput(input, fast);
        }

        private void Move(Vector3 input, bool fast, float seconds)
        {
            lastSweepCollider = null;
            Vector3 before = Camera.transform.position;
            if (flying)
            {
                Vector3 direction = Camera.transform.right * input.x + Vector3.up * input.y + Camera.transform.forward * input.z;
                Vector3 destination = ClampStage(before + direction * (FlySpeed * (fast ? 2.5f : 1f) * seconds));
                destination.y = Mathf.Clamp(destination.y, -3f, 42f);
                if (TryGround(destination, out RaycastHit ground)) destination.y = Mathf.Max(destination.y, ground.point.y + .45f);
                Camera.transform.position = SweepSphere(before, destination);
            }
            else
            {
                Vector3 motion = Quaternion.Euler(0, yaw, 0) * new Vector3(input.x, 0, input.z) * (WalkSpeed * (fast ? 2f : 1f) * seconds);
                int steps = Mathf.Max(1, Mathf.CeilToInt(motion.magnitude / .25f));
                Vector3 position = before;
                for (int i = 0; i < steps; i++)
                {
                    Vector3 candidate = ClampStage(position + motion / steps);
                    if (!TryGround(candidate, out RaycastHit ground) || ground.normal.y < MaximumSlopeCosine) break;
                    candidate.y = ground.point.y + EyeHeight;
                    if (Mathf.Abs(candidate.y - position.y) > .35f) break;
                    Vector3 swept = SweepBody(position, candidate);
                    if (TryGround(swept, out RaycastHit finalGround)) swept.y = finalGround.point.y + EyeHeight;
                    if ((swept - position).sqrMagnitude < .000001f) break;
                    position = swept;
                }
                Camera.transform.position = position;
            }
            TravelledMetres += Vector3.Distance(before, Camera.transform.position);
        }

        private Vector3 SweepBody(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from; float distance = delta.magnitude;
            if (distance < .00001f) return from;
            Vector3 bottom = from - Vector3.up * (EyeHeight - BodyRadius - .055f);
            Vector3 top = from - Vector3.up * .25f;
            if (Physics.CapsuleCast(bottom, top, BodyRadius, delta / distance, out RaycastHit hit,
                distance + Skin, CollisionMask, QueryTriggerInteraction.Ignore))
            {
                lastSweepCollider = hit.collider;
                return from + delta / distance * Mathf.Max(0f, Mathf.Min(distance, hit.distance - Skin));
            }
            return to;
        }

        private Vector3 SweepSphere(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from; float distance = delta.magnitude;
            if (distance < .00001f) return from;
            if (Physics.SphereCast(from, .25f, delta / distance, out RaycastHit hit,
                distance + Skin, CollisionMask, QueryTriggerInteraction.Ignore))
            {
                lastSweepCollider = hit.collider;
                return from + delta / distance * Mathf.Max(0f, Mathf.Min(distance, hit.distance - Skin));
            }
            return to;
        }

        private bool TryGround(Vector3 point, out RaycastHit hit)
        {
            Vector3 origin = new Vector3(point.x, Mathf.Max(30f, point.y + 6f), point.z);
            return Physics.Raycast(origin, Vector3.down, out hit, 160f, GroundMask, QueryTriggerInteraction.Ignore);
        }

        private static Vector3 ClampStage(Vector3 position)
        {
            position.x = Mathf.Clamp(position.x, -27f, 27f);
            position.z = Mathf.Clamp(position.z, -19f, 35f);
            return position;
        }

        private void ToggleMode()
        {
            if (flying)
            {
                Vector3 position = ClampStage(Camera.transform.position);
                if (!TryGround(position, out RaycastHit ground) || ground.normal.y < MaximumSlopeCosine)
                {
                    Notice("No walkable ground below this position.");
                    LogInput("F: walk unavailable", Vector3.zero, false);
                    return;
                }
                position.y = ground.point.y + EyeHeight;
                Camera.transform.position = position;
                flying = false;
                Notice("Walk mode · F returns to flight.");
            }
            else { flying = true; Notice("Fly mode · Q / E change height."); }
        }

        internal void ReleasePointerForDisplay() => ReleasePointer();

        private void ReleasePointer()
        {
            if (CosmicPreviewSmoke.Requested) return;
            if (!looking) return;
            looking = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (CosmicPreviewSmoke.Requested || focused) return;
            ReleasePointer();
            if (ready) RecordChangedInput(Vector3.zero, false);
        }
        private void OnDisable() { ReleasePointer(); }

        private void RecordChangedInput(Vector3 input, bool fast)
        {
            if (input == previousInput && fast == previousFast && looking == previousLooking && flying == previousFlying) return;
            LogInput("controls changed", input, fast);
            previousInput = input; previousFast = fast; previousLooking = looking; previousFlying = flying;
        }

        private void LogInput(string action, Vector3 input, bool fast)
        {
            Debug.Log("COSMIC_PREVIEW_INPUT " + JsonUtility.ToJson(new InputRecord
            {
                action = action, mode = CurrentMode, secondsSinceStartup = Time.realtimeSinceStartup,
                position = CameraPosition, rotation = Camera.transform.eulerAngles, direction = input,
                looking = looking, fast = fast, travelledMetres = (float)TravelledMetres,
                automatic = CosmicPreviewSmoke.Requested
            }));
        }

        private void ResolveEvidenceDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "-previewEvidence") continue;
                try { if (Path.IsPathRooted(args[i + 1])) evidenceDirectory = Path.GetFullPath(args[i + 1]); }
                catch (Exception) { evidenceDirectory = null; }
                return;
            }
        }

        private void CaptureRequested()
        {
            if (string.IsNullOrEmpty(evidenceDirectory))
            {
                Notice("Capture disabled. An explicit -previewEvidence folder is required.");
                LogInput("F12: capture disabled", Vector3.zero, false);
                return;
            }
            try
            {
                Directory.CreateDirectory(evidenceDirectory);
                string file = "cosmic-preview-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".png";
                ScreenCapture.CaptureScreenshot(Path.Combine(evidenceDirectory, file));
                Notice("Screenshot requested: " + file);
                LogInput("F12: screenshot requested", Vector3.zero, false);
            }
            catch (Exception)
            {
                Notice("Screenshot could not be requested in the supplied folder.");
                LogInput("F12: screenshot request failed", Vector3.zero, false);
            }
        }

        private void Notice(string text) { message = text; messageUntil = Time.realtimeSinceStartup + 5f; }

        private void OnGUI()
        {
            if (CosmicPreviewSmoke.Requested || !ready) return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true };
                titleStyle.normal.textColor = new Color(1f, .83f, .52f);
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                bodyStyle.normal.textColor = new Color(.80f, .89f, .92f);
                statusStyle = new GUIStyle(bodyStyle) { fontSize = 11 };
            }
            float panelWidth = Mathf.Min(560f, Screen.width - 24f);
            Color priorBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(.025f, .045f, .085f, .86f);
            GUI.Box(new Rect(12, 12, panelWidth, 132), GUIContent.none);
            GUI.backgroundColor = priorBackground;
            GUI.Label(new Rect(24, 20, panelWidth - 24, 26), "Kooker: Starfall — WIP local preview " + Application.version, titleStyle);
            GUI.Label(new Rect(24, 48, panelWidth - 24, 35), "Tree and blue giant study · terrain, galaxy and living sea in development", bodyStyle);
            GUI.Label(new Rect(24, 84, panelWidth - 24, 35), "WASD move · hold RMB look · Shift faster · F walk/fly · Q / E fly · Esc release", bodyStyle);
            GUI.Label(new Rect(24, 120, panelWidth - 24, 20), CurrentMode + " · " + TravelledMetres.ToString("F1", CultureInfo.InvariantCulture) + " m travelled" + (evidenceDirectory == null ? "" : " · F12 capture"), statusStyle);
            if (Time.realtimeSinceStartup < messageUntil)
                GUI.Label(new Rect(24, 154, panelWidth - 24, 45), message, bodyStyle);
        }

        [Serializable] private sealed class InputRecord
        {
            public string action, mode;
            public float secondsSinceStartup, travelledMetres;
            public Vector3 position, rotation, direction;
            public bool looking, fast, automatic;
        }

        private void SetLook(float newYaw, float newPitch)
        {
            yaw = newYaw; pitch = Mathf.Clamp(newPitch, -85f, 85f);
            Camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        // These hooks exercise the same movement, casts and F transition as Update. They never
        // read input or manipulate a pointer; scenario setup/teleports are recorded by the caller.
        internal bool SmokeReady => ready;
        internal Collider SmokeLastSweepCollider => lastSweepCollider;
        internal bool SmokeGround(Vector3 point, out RaycastHit hit) => TryGround(point, out hit);
        internal void SmokeAim(Vector3 target)
        {
            if (!CosmicPreviewSmoke.Requested || !ready) throw new InvalidOperationException("Smoke hook unavailable.");
            Vector3 direction = target - CameraPosition;
            SetLook(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
                -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg);
        }
        internal void SmokeStep(Vector3 input, bool fast, float seconds)
        {
            if (!CosmicPreviewSmoke.Requested || !ready) throw new InvalidOperationException("Smoke hook unavailable.");
            Move(Vector3.ClampMagnitude(input, 1f), fast, Mathf.Clamp(seconds, 0f, .05f));
            RecordChangedInput(input, fast);
        }
        internal void SmokeToggleMode()
        {
            if (!CosmicPreviewSmoke.Requested || !ready) throw new InvalidOperationException("Smoke hook unavailable.");
            ToggleMode(); RecordChangedInput(Vector3.zero, false);
        }
    }
}
