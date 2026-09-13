using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CityLife.World
{
    [DefaultExecutionOrder(-50)]
    public sealed class NpcPlayerControls : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public NpcDecisionHud Hud;
        public CharacterPreviewCamera View;
        public PreviewDisplayMode Display;
        public bool AllowUnfocusedTestInput;
        public bool SuppressInput;
        public bool SuppressView;
        public bool PersistentMouseCapture;
        [Range(.04f, .3f)] public float LookSensitivity = .12f;
        public Vector3 CameraMinimum = new Vector3(-22, .7f, -22), CameraMaximum = new Vector3(22, 18, 22);
        private bool resumeCapture;
        private int captureFrame = -10;
        [NonSerialized] public Keyboard TestKeyboard;
        [NonSerialized] public Mouse TestMouse;
        public bool MenuOpen { get; private set; }
        public string Page { get; private set; } = "Root";
        public bool Looking { get; private set; }
        public bool ExitRequested { get; private set; }
        public bool FreeSpectator { get; private set; }
        public bool DisplayShortcutActive { get; private set; }
        private float shortcutTimeScale;
        private bool shortcutPaused;
        public string Mode => Brain.Possessed ? "Possession" : FreeSpectator ? "Spectator / free camera" : "Autonomous NPC / follow";
        public Text PageTitle, PageBody;
        public Vector3 SpectatorPosition => freePosition;
        private GameObject overlay;
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<Action> actions = new List<Action>();
        private int selected;
        private Vector3 freePosition, cameraMotion;
        private float yaw, pitch, savedTimeScale = 1;
        private bool initialized;

        private void Start()
        {
            View.ExternalView = true;
            freePosition = View.transform.position; yaw = View.transform.eulerAngles.y;
            pitch = View.transform.eulerAngles.x; if (pitch > 180) pitch -= 360;
            BuildMenu(); initialized = true;
        }
        private void Update()
        {
            if (!initialized || !Brain.Ready) return;
            Brain.ManualDirection = Vector3.zero; cameraMotion = Vector3.zero;
            if (SuppressInput || DisplayShortcutActive) return;
            if (!Application.isFocused && !AllowUnfocusedTestInput) { ReleasePointer(); return; }
            var key = TestKeyboard ?? Keyboard.current; var mouse = TestMouse ?? Mouse.current;
            if (key == null) return;
            if (key.f11Key.wasPressedThisFrame && !Display.IsChanging) { StartCoroutine(ToggleDisplayShortcut()); return; }
            if (key.pKey.wasPressedThisFrame) { if (MenuOpen) Resume(); else OpenMenu(); }
            else if (key.escapeKey.wasPressedThisFrame)
            {
                if (!MenuOpen) OpenMenu();
                else if (Page != "Root") ShowPage("Root");
                else Resume();
            }
            if (MenuOpen)
            {
                if (key.downArrowKey.wasPressedThisFrame) Select((selected + 1) % actions.Count);
                if (key.upArrowKey.wasPressedThisFrame) Select((selected + actions.Count - 1) % actions.Count);
                if (key.enterKey.wasPressedThisFrame || key.numpadEnterKey.wasPressedThisFrame) actions[selected]();
                return;
            }
            if (key.tabKey.wasPressedThisFrame) TogglePossession();
            if (key.fKey.wasPressedThisFrame && !Brain.Possessed)
            {
                FreeSpectator = !FreeSpectator;
                if (FreeSpectator) freePosition = View.transform.position;
            }
            if (key.rKey.wasPressedThisFrame) Brain.ToggleAutonomy();
            if (key.lKey.wasPressedThisFrame) Hud.Detailed = !Hud.Detailed;
            if (mouse != null)
            {
                if (mouse.rightButton.wasPressedThisFrame || (PersistentMouseCapture && mouse.leftButton.wasPressedThisFrame &&
                    (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))) CapturePointer();
                if (!PersistentMouseCapture && mouse.rightButton.wasReleasedThisFrame) ReleasePointer();
                if (Looking && (!PersistentMouseCapture || Time.frameCount > captureFrame + 1))
                {
                    Vector2 delta = Vector2.ClampMagnitude(mouse.delta.ReadValue(), 400);
                    yaw = Mathf.Repeat(yaw + delta.x * LookSensitivity, 360);
                    pitch = Mathf.Clamp(pitch - delta.y * LookSensitivity, -75, 75);
                }
            }
            // Movement alone never changes ownership. The same axes serve the explicitly selected mode.
            float x = ((key.dKey.isPressed || key.rightArrowKey.isPressed) ? 1 : 0) -
                ((key.aKey.isPressed || key.leftArrowKey.isPressed) ? 1 : 0);
            float z = ((key.wKey.isPressed || key.upArrowKey.isPressed) ? 1 : 0) -
                ((key.sKey.isPressed || key.downArrowKey.isPressed) ? 1 : 0);
            Vector3 motion = Quaternion.Euler(0, yaw, 0) * new Vector3(x, 0, z);
            if (Brain.Possessed) Brain.ManualDirection = Vector3.ClampMagnitude(motion, 1);
            else if (FreeSpectator)
            {
                motion.y = (key.eKey.isPressed ? 1 : 0) - (key.qKey.isPressed ? 1 : 0);
                cameraMotion = Vector3.ClampMagnitude(motion, 1) * (key.leftShiftKey.isPressed ? 9 : 4);
            }
        }
        private void FixedUpdate()
        {
            if (!initialized || MenuOpen || DisplayShortcutActive || Brain.Possessed || !FreeSpectator) return;
            freePosition += cameraMotion * NpcAutonomy.StepSeconds;
            freePosition = new Vector3(Mathf.Clamp(freePosition.x, CameraMinimum.x, CameraMaximum.x), Mathf.Clamp(freePosition.y, CameraMinimum.y, CameraMaximum.y), Mathf.Clamp(freePosition.z, CameraMinimum.z, CameraMaximum.z));
        }
        private void LateUpdate()
        {
            if (!initialized || SuppressView) return;
            if (!MenuOpen && !DisplayShortcutActive)
            {
                if (Brain.Possessed || !FreeSpectator) { View.Yaw = yaw; View.Pitch = Mathf.Clamp(pitch, -8, 65); View.Follow(); }
                else View.transform.SetPositionAndRotation(freePosition, Quaternion.Euler(pitch, yaw, 0));
            }
            if (MenuOpen && Page == "Graphics") UpdateGraphicsText();
        }
        public void TogglePossession()
        {
            ReleasePointer();
            Brain.SetPossession(!Brain.Possessed);
            if (Brain.Possessed) { yaw = View.Yaw; pitch = View.Pitch; }
            else
            {
                // Observe from the current following pose; do not reset or replace the inhabitant.
                freePosition = View.transform.position; yaw = View.transform.eulerAngles.y;
                pitch = View.transform.eulerAngles.x; if (pitch > 180) pitch -= 360;
            }
            if (MenuOpen && Page == "Controls") ShowPage("Controls");
        }
        public void OpenMenu()
        {
            if (MenuOpen) return;
            resumeCapture = PersistentMouseCapture && Looking;
            ReleasePointer(); savedTimeScale = Time.timeScale; Time.timeScale = 0;
            Brain.MenuPaused = true; Brain.ManualDirection = Vector3.zero; cameraMotion = Vector3.zero;
            if (Brain.OptionalPlanner != null) Brain.OptionalPlanner.Cancel("options-opened");
            MenuOpen = true; overlay.SetActive(true); ShowPage("Root");
        }
        public void Resume()
        {
            if (!MenuOpen) return;
            ReleasePointer(); MenuOpen = false; overlay.SetActive(false);
            Brain.MenuPaused = false; Time.timeScale = savedTimeScale;
            if (resumeCapture && (Application.isFocused || AllowUnfocusedTestInput)) CapturePointer();
            resumeCapture = false;
        }
        public void ReleasePointer()
        {
            Looking = false;
            if (!AllowUnfocusedTestInput) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
        private void CapturePointer()
        {
            if (MenuOpen || DisplayShortcutActive) return;
            captureFrame = Time.frameCount;
            Looking = true;
            if (!AllowUnfocusedTestInput) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }
        private void OnDisable()
        {
            ReleasePointer();
            if (DisplayShortcutActive) { Time.timeScale = shortcutTimeScale; Brain.MenuPaused = shortcutPaused; DisplayShortcutActive = false; }
            if (MenuOpen) { Time.timeScale = savedTimeScale; Brain.MenuPaused = false; }
        }
        private void Exit()
        {
            ExitRequested = true;
            if (!NpcPreviewSmoke.Requested) Application.Quit();
        }
        private IEnumerator ToggleDisplayShortcut()
        {
            bool restoreCapture = PersistentMouseCapture && Looking;
            ReleasePointer(); shortcutTimeScale = Time.timeScale; shortcutPaused = Brain.MenuPaused;
            DisplayShortcutActive = true; Time.timeScale = 0; Brain.MenuPaused = true;
            Brain.ManualDirection = Vector3.zero; cameraMotion = Vector3.zero;
            if (Brain.OptionalPlanner != null) Brain.OptionalPlanner.Cancel("display-shortcut");
            try
            {
                Display.ToggleFromShortcut(); yield return null;
                while (Display.IsChanging) yield return null;
            }
            finally
            { Time.timeScale = shortcutTimeScale; Brain.MenuPaused = shortcutPaused; DisplayShortcutActive = false;
                if (restoreCapture && !MenuOpen && (Application.isFocused || AllowUnfocusedTestInput)) CapturePointer(); }
        }
        private void BuildMenu()
        {
            if (Hud.Canvas.GetComponent<GraphicRaycaster>() == null)
                Hud.Canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                var events = new GameObject("Menu input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                var module = events.GetComponent<InputSystemUIInputModule>(); module.AssignDefaultActions();
                // Pointer buttons use the UI module; keyboard navigation is handled once above.
                module.move = null; module.submit = null; module.cancel = null;
                if (NpcPreviewSmoke.Requested) module.enabled = false;
            }
            overlay = new GameObject("Paused options", typeof(RectTransform), typeof(Image));
            var outer = overlay.GetComponent<RectTransform>(); outer.SetParent(Hud.Canvas.transform, false);
            outer.anchorMin = Vector2.zero; outer.anchorMax = Vector2.one; outer.offsetMin = outer.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(.01f, .025f, .04f, .95f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform Rect(GameObject o, float x, float y, float w, float h)
            {
                var rect = o.GetComponent<RectTransform>(); rect.SetParent(overlay.transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
                rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
            }
            Text Label(string name, float y, float height, int size)
            {
                var o = new GameObject(name, typeof(RectTransform), typeof(Text)); Rect(o, 0, y, 930, height);
                var text = o.GetComponent<Text>(); text.font = font; text.fontSize = size; text.color = Color.white;
                text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; return text;
            }
            PageTitle = Label("Options title", 80, 65, 36);
            PageBody = Label("Options description", 160, 290, 21);
            for (int i = 0; i < 4; i++)
            {
                int buttonIndex = i;
                var o = new GameObject("Option " + i, typeof(RectTransform), typeof(Image), typeof(Button));
                Rect(o, 0, 480 + i * 65, 700, 52);
                var button = o.GetComponent<Button>(); button.onClick.AddListener(() => { if (buttonIndex < actions.Count) actions[buttonIndex](); });
                var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
                var rect = textObject.GetComponent<RectTransform>(); rect.SetParent(o.transform, false);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                var label = textObject.GetComponent<Text>(); label.font = font; label.fontSize = 23; label.color = Color.white;
                label.alignment = TextAnchor.MiddleCenter; buttons.Add(button);
            }
            var help = Label("Menu navigation", 785, 60, 20);
            help.text = "↑ / ↓ choose · Enter activate · Mouse click supported\nEscape: back, then resume · P: resume from any page";
            overlay.SetActive(false);
        }
        private void ShowPage(string page)
        {
            Page = page; selected = 0; actions.Clear(); var labels = new List<string>();
            void Option(string label, Action action) { labels.Add(label); actions.Add(action); }
            if (page == "Root")
            {
                PageTitle.text = "PAUSED / Options";
                PageBody.text = "NPC simulation, movement and animation are paused.\nMode: " + Mode +
                    "\nThe same inhabitant, cargo, goal context and decision history remain.\nResume continues this world; Exit preview closes only this player.";
                Option("Resume", Resume); Option("Controls", () => ShowPage("Controls"));
                Option("Graphics", () => ShowPage("Graphics")); Option("Exit preview", Exit);
            }
            else if (page == "Controls")
            {
                PageTitle.text = "PAUSED / Controls";
                PageBody.text = "Current mode: " + Mode + "   |   F11: fullscreen / windowed (also Options > Graphics)." +
                    "\nTab: deliberately enter / leave possession of the same NPC." +
                    "\nAutonomous NPC: follow its activity. F: switch follow / free spectator." +
                    "\nSpectator: WASD or arrows move the camera; Q/E down/up. NPC autonomy continues." +
                    "\nPossession: WASD or arrows move the NPC. Autonomy is suspended." +
                    "\nHold right mouse: look. R: pause/resume autonomy in observation modes." +
                    "\nL: show/hide decisions. P: pause/options. Escape: back/resume; outside menus, pause and release pointer.";
                Option(Brain.Possessed ? "Release NPC and resume autonomy" : "Possess this NPC", TogglePossession);
                if (Brain.OptionalPlanner != null) Option("Local thoughts", () => ShowPage("Thoughts"));
                if (PersistentMouseCapture) Option("Mouse look sensitivity", () => ShowPage("Mouse"));
                Option("Back", () => ShowPage("Root"));
            }
            else if (page == "Mouse")
            {
                PageTitle.text = "PAUSED / Mouse look";
                PageBody.text = "Sensitivity: " + LookSensitivity.ToString("F2") + " degrees per input pixel.\n" +
                    (PersistentMouseCapture ? "Click or right-click in the world to capture the pointer.\nEscape releases it and opens options; Resume restores the prior capture state." : "Hold right mouse to look; release it to free the pointer.") +
                    "\nMenus keep the pointer visible. Capture/resize motion is discarded to avoid jumps.\nThis setting applies to both spectator and possessed-character views.";
                Option("Lower sensitivity", () => { LookSensitivity = Mathf.Max(.04f, LookSensitivity - .02f); ShowPage("Mouse"); });
                Option("Higher sensitivity", () => { LookSensitivity = Mathf.Min(.3f, LookSensitivity + .02f); ShowPage("Mouse"); });
                Option("Reset sensitivity", () => { LookSensitivity = .12f; ShowPage("Mouse"); });
                Option("Back", () => ShowPage("Controls"));
            }
            else if (page == "Thoughts")
            {
                var planner = Brain.OptionalPlanner;
                PageTitle.text = "PAUSED / Local thoughts";
                PageBody.text = planner.Status + "\nOptional local goals, plans and fictional dialogue. Rules validate every action." +
                    "\nOff by default. A configured, already-loaded model is required. Unavailable or invalid replies use rules." +
                    "\nAt most 12 requests per session; short timeout and cancellation on control changes." +
                    "\nL shows the decision log and last model text. Reflection is generated text, not learning.";
                Option(planner.EnabledByUser ? "Turn local thoughts off" : "Turn local thoughts on", () => { planner.SetEnabled(!planner.EnabledByUser); ShowPage("Thoughts"); });
                Option("Back", () => ShowPage("Controls"));
            }
            else
            {
                PageTitle.text = "PAUSED / Graphics"; UpdateGraphicsText();
                Option("Toggle fullscreen / windowed", () => { ReleasePointer(); Display.ToggleFromMenu(); });
                Option("Back", () => ShowPage("Root"));
            }
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].gameObject.SetActive(i < labels.Count);
                if (i < labels.Count) buttons[i].GetComponentInChildren<Text>().text = labels[i];
            }
            Select(0);
        }
        private void UpdateGraphicsText()
        {
            PageBody.text = "Display: " + Screen.fullScreenMode + "  " + Screen.width + " × " + Screen.height +
                "\n" + (Display.IsChanging ? "Applying display change…" : "The previous window size is remembered.") +
                "\nThe existing display control changes only this player's display.\nNPC simulation stays paused. World, identity, cargo and decisions are retained.";
        }
        private void Select(int index)
        {
            selected = index;
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].GetComponent<Image>().color = i == selected ? new Color(.13f, .47f, .5f, 1) : new Color(.1f, .18f, .24f, 1);
        }
    }
}
