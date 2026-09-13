using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CityLife.World
{
    public static class NpcControlAcceptance
    {
        // Events enter the actual player's Input System and ordinary Update handlers.
        // These devices are local to this process; no OS input or focus is sent to another preview.
        public static IEnumerator Verify(NpcAutonomy brain, NpcPlayerControls controls, NpcDecisionHud hud,
            CharacterPreviewCamera view, Action<string, bool, string> need, Action<string> capture)
        {
            var keyboard = InputSystem.AddDevice<Keyboard>("NpcAcceptanceKeyboard");
            var mouse = InputSystem.AddDevice<Mouse>("NpcAcceptanceMouse");
            controls.TestKeyboard = keyboard; controls.TestMouse = mouse; controls.SuppressInput = false;
            IEnumerator Tap(Key key)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            }
            try
            {
                brain.ResetState(); hud.Detailed = false;
                for (int i = 0; i < 1500 && !(brain.Actions.Held != null && brain.GoalId.StartsWith("depot", StringComparison.Ordinal)); i++)
                { brain.StepTick(); yield return null; }
                need("control-fixture-carrying-with-goal", brain.Actions.Held != null && brain.GoalId.Length > 0,
                    "Control tests begin with a real carried item and active delivery goal.");
                var identity = brain.gameObject.GetEntityId();
                string cargo = brain.Actions.Held.StableId, goal = brain.GoalId;
                var originalEvents = brain.Log.Entries.ToArray();
                capture("10-concise-autonomous-hud");
                need("autonomous-mode-explicit", !brain.Possessed && !controls.FreeSpectator &&
                    controls.Mode.StartsWith("Autonomous", StringComparison.Ordinal), "Default mode follows the autonomous NPC.");

                // A movement key in autonomous follow cannot silently possess the body.
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return null;
                for (int i = 0; i < 5; i++) { brain.StepTick(); yield return null; }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                need("movement-does-not-possess", !brain.Possessed && brain.Running, "W leaves ownership with autonomy.");

                yield return Tap(Key.R);
                Vector3 stopped = brain.transform.position;
                for (int i = 0; i < 12; i++) { brain.StepTick(); yield return null; }
                need("R-pauses-autonomy", !brain.Running && Vector3.Distance(stopped, brain.transform.position) < .01f,
                    "Actual R key event stops autonomous translation without clearing cargo or goal.");
                yield return Tap(Key.R);
                for (int i = 0; i < 8; i++) { brain.StepTick(); yield return null; }
                need("R-resumes-autonomy", brain.Running && Vector3.Distance(stopped, brain.transform.position) > .1f,
                    "Second R event resumes the existing route.");
                yield return Tap(Key.F);
                Vector3 freeBefore = view.transform.position, npcBefore = brain.transform.position;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return null;
                for (int i = 0; i < 12; i++) { brain.StepTick(); yield return null; }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                need("spectator-camera-and-autonomous-NPC", controls.FreeSpectator && !brain.Possessed &&
                    Vector3.Distance(freeBefore, view.transform.position) > .1f && Vector3.Distance(npcBefore, brain.transform.position) > .1f,
                    "F selects free spectator; W moves its camera while the same NPC continues autonomous movement.");
                capture("11-free-spectator");

                yield return Tap(Key.Tab);
                need("explicit-possession-preserves-context", brain.Possessed && brain.gameObject.GetEntityId() == identity &&
                    brain.Actions.Held?.StableId == cargo && brain.GoalId == goal &&
                    originalEvents.All(x => brain.Log.Entries.Contains(x)),
                    "Tab possesses the same body with cargo, goal context and prior decision events intact.");
                Vector3 manualOrigin = brain.transform.position;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.UpArrow)); yield return null;
                for (int i = 0; i < 12; i++) { brain.StepTick(); yield return null; }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                Vector3 arrowDelta = brain.transform.position - manualOrigin;
                brain.Actor.Place(manualOrigin);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return null;
                for (int i = 0; i < 12; i++) { brain.StepTick(); yield return null; }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                Vector3 wasdDelta = brain.transform.position - manualOrigin;
                need("arrow-and-WASD-equivalent-in-possession", arrowDelta.magnitude > .25f &&
                    Vector3.Distance(arrowDelta, wasdDelta) < .02f, "Up arrow and W produce equivalent movement through the actual input adapter and motor.");
                capture("12-explicit-possession");

                Quaternion lookBefore = view.transform.rotation;
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2, delta = new Vector2(35, -12) }); yield return null;
                yield return null;
                need("right-mouse-look", controls.Looking && Quaternion.Angle(lookBefore, view.transform.rotation) > 1,
                    "Actual right-button and delta events rotate the view.");
                InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
                need("right-mouse-release", !controls.Looking, "Releasing right mouse ends look capture.");

                yield return Tap(Key.P);
                int pausedTick = brain.Tick; Vector3 pausedPosition = brain.transform.position;
                Quaternion pausedHand = brain.Actor.Animator.GetBoneTransform(HumanBodyBones.RightHand).localRotation;
                for (int i = 0; i < 20; i++) { brain.StepTick(); yield return null; }
                need("P-is-true-simulation-pause", controls.MenuOpen && brain.MenuPaused && Time.timeScale == 0 &&
                    brain.Tick == pausedTick && brain.transform.position == pausedPosition &&
                    Quaternion.Angle(pausedHand, brain.Actor.Animator.GetBoneTransform(HumanBodyBones.RightHand).localRotation) < .01f,
                    "P opens the menu and freezes fixed ticks, body translation and animated pose.");
                capture("13-pause-options");
                yield return Tap(Key.DownArrow); yield return Tap(Key.Enter);
                need("controls-page-readable-guide", controls.Page == "Controls" &&
                    controls.PageBody.text.Contains("WASD or arrows") && controls.PageBody.text.Contains("Tab:") &&
                    controls.PageBody.text.Contains("R:") && controls.PageBody.text.Contains("Escape:"),
                    "Keyboard navigation opens the real Controls page with possession, movement, autonomy and Escape guidance.");
                capture("14-controls-page");
                yield return Tap(Key.Escape);
                need("Escape-submenu-goes-back", controls.MenuOpen && controls.Page == "Root" && brain.Tick == pausedTick,
                    "Escape closes the submenu first; simulation remains paused.");
                yield return Tap(Key.Escape);
                need("Escape-root-resumes", !controls.MenuOpen && !brain.MenuPaused && Time.timeScale == 1,
                    "Escape from the root menu resumes the same world.");

                yield return Tap(Key.P);
                yield return Tap(Key.DownArrow); yield return Tap(Key.DownArrow); yield return Tap(Key.Enter);
                need("graphics-page", controls.Page == "Graphics" && controls.MenuOpen, "Keyboard navigation reaches Graphics.");
                capture("15-graphics-windowed");
                var initialMode = Screen.fullScreenMode; int initialWidth = Screen.width, initialHeight = Screen.height;
                int displayTick = brain.Tick, eventCount = brain.Log.Entries.Count;
                Vector3 displayPosition = brain.transform.position;
                yield return Tap(Key.Enter);
                float deadline = Time.realtimeSinceStartup + 10;
                while (controls.Display.IsChanging && Time.realtimeSinceStartup < deadline) yield return null;
                need("fullscreen-applied-in-player", Screen.fullScreenMode != initialMode && !controls.Display.IsChanging,
                    "Menu invokes the existing display controller; actual Screen mode=" + Screen.fullScreenMode + " " + Screen.width + "x" + Screen.height);
                capture("16-graphics-fullscreen");
                yield return Tap(Key.Enter);
                deadline = Time.realtimeSinceStartup + 10;
                while (controls.Display.IsChanging && Time.realtimeSinceStartup < deadline) yield return null;
                need("windowed-restored-in-player", Screen.fullScreenMode == initialMode && Screen.width == initialWidth && Screen.height == initialHeight,
                    "Second menu display toggle restores the observed original mode and dimensions.");
                need("display-preserves-NPC-world-state", brain.Tick == displayTick && brain.transform.position == displayPosition &&
                    brain.gameObject.GetEntityId() == identity && brain.Actions.Held?.StableId == cargo &&
                    brain.GoalId == goal && brain.Log.Entries.Count == eventCount,
                    "Both display transitions preserve identity, cargo, goal, decision history and paused simulation ticks.");
                capture("17-graphics-restored");
                yield return Tap(Key.P);
                need("P-resumes-from-submenu", !controls.MenuOpen && !brain.MenuPaused, "P resumes directly from Graphics.");

                yield return Tap(Key.Tab);
                need("release-possession-resumes-same-NPC", !brain.Possessed && brain.Running &&
                    brain.gameObject.GetEntityId() == identity && brain.Actions.Held?.StableId == cargo && brain.GoalId == goal &&
                    originalEvents.All(x => brain.Log.Entries.Contains(x)),
                    "Tab releases the same NPC, retains context, replans from its current position and resumes autonomy.");
                Vector3 resumed = brain.transform.position;
                for (int i = 0; i < 15; i++) { brain.StepTick(); yield return null; }
                need("autonomy-moves-after-release", Vector3.Distance(resumed, brain.transform.position) > .15f,
                    "The retained NPC actually continues its delivery route after release.");
                capture("18-release-and-resume");

                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 }); yield return null;
                yield return Tap(Key.Escape);
                need("Escape-outside-menu-pauses-and-releases", controls.MenuOpen && brain.MenuPaused && !controls.Looking,
                    "Outside menus, Escape pauses the world and releases look capture.");
                InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
                yield return Tap(Key.DownArrow); yield return Tap(Key.DownArrow); yield return Tap(Key.DownArrow); yield return Tap(Key.Enter);
                need("exit-action-reaches-player-handler", controls.ExitRequested && controls.MenuOpen,
                    "Exit preview is selected through actual key events. Smoke records this request before its final Application.Quit; production exits immediately.");
                capture("19-exit-behavior");
                controls.Resume();
            }
            finally
            {
                controls.SuppressInput = true; controls.TestKeyboard = null; controls.TestMouse = null;
                InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
            }
        }
    }
}
