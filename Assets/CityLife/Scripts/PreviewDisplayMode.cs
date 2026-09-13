using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityLife.World
{
    /// <summary>Changes only this player's display mode; the scene and controller stay alive.</summary>
    public sealed class PreviewDisplayMode : MonoBehaviour
    {
        public bool MenuOnly;
        public bool IsChanging => changing;
        public void ToggleFromMenu() => Toggle("pause-menu");
        public void ToggleFromShortcut() => Toggle("F11");
        private int windowWidth = 1600, windowHeight = 900;
        private bool changing;
        private string notice = "", evidenceFile;
        private float noticeUntil;
        private GUIStyle labelStyle, buttonStyle;

        private void Start()
        {
            RememberWindowSize();
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-previewEvidence");
            if (at < 0 || at + 1 >= args.Length || !Path.IsPathFullyQualified(args[at + 1])) return;
            try
            {
                string directory = Path.GetFullPath(args[at + 1]);
                Directory.CreateDirectory(directory);
                evidenceFile = Path.Combine(directory, "display-mode-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl");
            }
            catch (Exception) { evidenceFile = null; }
        }

        private void Update()
        {
            if (CosmicPreviewSmoke.Requested || MenuOnly) return;
            if (!changing) RememberWindowSize();
            Keyboard keyboard = Keyboard.current;
            if (Application.isFocused && keyboard != null &&
                (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed) &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                Toggle("Alt+Enter");
        }

        private void RememberWindowSize()
        {
            if (Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width >= 320 && Screen.height >= 240)
            { windowWidth = Screen.width; windowHeight = Screen.height; }
        }

        private void Toggle(string source)
        {
            if (changing || CosmicPreviewSmoke.Requested) return;
            // Release inspection look before a display transition, including keyboard activation.
            GetComponent<CosmicPreviewExplorer>()?.ReleasePointerForDisplay();
            StartCoroutine(ChangeMode(source));
        }

        private IEnumerator ChangeMode(string source)
        {
            RememberWindowSize();
            bool toFullscreen = Screen.fullScreenMode == FullScreenMode.Windowed;
            var mode = toFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            int displayWidth = Screen.mainWindowDisplayInfo.width;
            int displayHeight = Screen.mainWindowDisplayInfo.height;
            if (displayWidth < 320 || displayHeight < 240)
            { displayWidth = Screen.currentResolution.width; displayHeight = Screen.currentResolution.height; }
            int maxWidth = Mathf.Max(320, displayWidth - 64), maxHeight = Mathf.Max(240, displayHeight - 96);
            int width = toFullscreen ? displayWidth : Mathf.Clamp(windowWidth, Mathf.Min(800, maxWidth), maxWidth);
            int height = toFullscreen ? displayHeight : Mathf.Clamp(windowHeight, Mathf.Min(450, maxHeight), maxHeight);
            var explorer = GetComponent<CosmicPreviewExplorer>();
            var record = new Transition
            {
                utc = DateTime.UtcNow.ToString("O"), version = Application.version, source = source,
                beforeMode = Screen.fullScreenMode.ToString(), requestedMode = mode.ToString(),
                beforeWidth = Screen.width, beforeHeight = Screen.height,
                requestedWidth = width, requestedHeight = height,
                rememberedWidth = windowWidth, rememberedHeight = windowHeight,
                beforePosition = transform.position, beforeRotation = transform.eulerAngles,
                beforeTravelled = explorer == null ? 0 : explorer.TravelledMetres,
                beforeControllerMode = explorer == null ? "" : explorer.CurrentMode,
                beforeSceneHandle = gameObject.scene.handle.GetRawData(), beforeFrame = Time.frameCount
            };
            changing = true;
            Screen.SetResolution(width, height, mode);
            // Unity applies display changes asynchronously. Wait for stable observed dimensions.
            float deadline = Time.realtimeSinceStartup + 8f;
            int stableFrames = 0;
            do
            {
                yield return null;
                stableFrames = Screen.fullScreenMode == mode && Screen.width == width && Screen.height == height
                    ? stableFrames + 1 : 0;
            } while (stableFrames < 3 && Time.realtimeSinceStartup < deadline);
            record.afterMode = Screen.fullScreenMode.ToString();
            record.afterWidth = Screen.width; record.afterHeight = Screen.height;
            record.afterPosition = transform.position; record.afterRotation = transform.eulerAngles;
            record.afterTravelled = explorer == null ? 0 : explorer.TravelledMetres;
            record.afterControllerMode = explorer == null ? "" : explorer.CurrentMode;
            record.afterSceneHandle = gameObject.scene.handle.GetRawData(); record.afterFrame = Time.frameCount;
            record.displayApplied = stableFrames >= 3;
            record.scenePreserved = record.beforeSceneHandle == record.afterSceneHandle;
            record.playerStateUnchanged = record.beforePosition == record.afterPosition &&
                record.beforeRotation == record.afterRotation && record.beforeTravelled == record.afterTravelled &&
                record.beforeControllerMode == record.afterControllerMode;
            string json = JsonUtility.ToJson(record);
            Debug.Log("STARFALL_DISPLAY_TRANSITION " + json);
            if (evidenceFile != null)
            {
                try { File.AppendAllText(evidenceFile, json + Environment.NewLine); }
                catch (Exception) { evidenceFile = null; }
            }
            notice = record.displayApplied ? "" : "Display change did not settle. Try again.";
            noticeUntil = Time.realtimeSinceStartup + 5f;
            changing = false;
        }

        private void OnGUI()
        {
            if (CosmicPreviewSmoke.Requested || MenuOnly) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                labelStyle.normal.textColor = new Color(.85f, .93f, 1f);
                buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            }
            float x = Mathf.Max(12, Screen.width - 232), y = Screen.width < 820 ? 166 : 12;
            GUI.Box(new Rect(x, y, 220, 78), GUIContent.none);
            string current = Screen.fullScreenMode == FullScreenMode.Windowed ? "Windowed" : "Fullscreen";
            GUI.Label(new Rect(x + 6, y + 3, 208, 25), current + "  ·  Alt+Enter", labelStyle);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !changing;
            if (GUI.Button(new Rect(x + 10, y + 33, 200, 34), changing ? "Switching…" :
                current == "Windowed" ? "Enter fullscreen" : "Return to window", buttonStyle)) Toggle("button");
            GUI.enabled = wasEnabled;
            if (Time.realtimeSinceStartup < noticeUntil && notice.Length > 0)
                GUI.Label(new Rect(x - 100, y + 82, 320, 28), notice, labelStyle);
        }

        [Serializable] private sealed class Transition
        {
            public string utc, version, source, beforeMode, requestedMode, afterMode, beforeControllerMode, afterControllerMode;
            public int beforeWidth, beforeHeight, requestedWidth, requestedHeight, rememberedWidth, rememberedHeight,
                afterWidth, afterHeight, beforeFrame, afterFrame;
            public ulong beforeSceneHandle, afterSceneHandle;
            public Vector3 beforePosition, afterPosition, beforeRotation, afterRotation;
            public double beforeTravelled, afterTravelled;
            public bool displayApplied, scenePreserved, playerStateUnchanged;
        }
    }
}
