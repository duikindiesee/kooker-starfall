using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using CityLife.Items;
using CityLife.Food;

namespace CityLife.World
{
    [DefaultExecutionOrder(-50)]
    public sealed class NpcPlayerControls : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public NpcDecisionHud Hud;
        public CharacterPreviewCamera View;
        public PreviewDisplayMode Display;
        public PhysicalContainerPanel ContainerPanel;
        public bool AllowUnfocusedTestInput;
        public bool SuppressInput;
        public bool SuppressView;
        public bool PersistentMouseCapture;
        public bool ExternalMovementInput;
        public NpcInteractable CurrentPickupTarget { get; private set; }
        public string CurrentPickupTargetLabel { get; private set; }
        public Text TargetPromptText;
        [Range(.04f, .3f)] public float LookSensitivity = .12f;
        public Vector3 CameraMinimum = new Vector3(-600f, -10f, -500f), CameraMaximum = new Vector3(600f, 300f, 1000f);
        public float SpectatorFlightSpeed = 12f;
        private bool resumeCapture;
        private bool resumeCaptureFromPanel;
        private int cyclePickupIndex = -1;
        private int captureFrame = -10;
        [NonSerialized] public Keyboard TestKeyboard;
        [NonSerialized] public Mouse TestMouse;
        public bool MenuOpen { get; private set; }
        public string Page { get; private set; } = "Root";
        public bool Looking { get; private set; }
        public bool ExitRequested { get; private set; }
        public bool FreeSpectator { get; private set; }
        public bool ScriptedScenicCapture { get; set; }
        public bool DisplayShortcutActive { get; private set; }
        private float shortcutTimeScale;
        private bool shortcutPaused;
        public string Mode => ScriptedScenicCapture ? "Scripted scenic observer / actor autonomous" : Brain.Possessed ? "Possession" : FreeSpectator ? $"Spectator / free camera ({SpectatorFlightSpeed:F0}m/s)" : $"Autonomous NPC / {View?.ViewMode}";
        public Text PageTitle, PageBody;
        public Vector3 SpectatorPosition => freePosition;
        private GameObject overlay;
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<Action> actions = new List<Action>();
        private int selected;
        private Vector3 freePosition, cameraMotion;
        private float yaw, pitch, savedTimeScale = 1;
        private bool initialized;

        public void EnsureInitialized()
        {
            if (initialized) return;
            if (View != null)
            {
                View.ExternalView = true;
                freePosition = View.transform.position; yaw = View.transform.eulerAngles.y;
                pitch = View.transform.eulerAngles.x; if (pitch > 180) pitch -= 360;
            }
            if (Hud == null) Hud = FindFirstObjectByType<NpcDecisionHud>();
            BuildMenu();
            initialized = true;
        }

        private void Start()
        {
            EnsureInitialized();
        }

        public void Update()
        {
            EnsureInitialized();
            if (Brain == null || !Brain.Ready) return;
            if (!ExternalMovementInput) Brain.ManualDirection = Vector3.zero;
            cameraMotion = Vector3.zero;
            if (SuppressInput || DisplayShortcutActive) return;
            if (!Application.isFocused && !AllowUnfocusedTestInput) { ReleasePointer(); return; }
            var key = TestKeyboard ?? Keyboard.current; var mouse = TestMouse ?? Mouse.current;
            if (key == null) return;
            if (key.f11Key.wasPressedThisFrame && !Display.IsChanging) { StartCoroutine(ToggleDisplayShortcut()); return; }
            if (key.pKey.wasPressedThisFrame) { if (MenuOpen) Resume(); else OpenMenu(); }
            else if (key.escapeKey.wasPressedThisFrame)
            {
                if (MenuOpen)
                {
                    if (Page != "Root") ShowPage("Root");
                    else Resume();
                    return;
                }
                if (ContainerPanel != null && ContainerPanel.IsOpen)
                {
                    ContainerPanel.Close();
                    return;
                }
                OpenMenu();
                return;
            }
            if (MenuOpen)
            {
                if (key.downArrowKey.wasPressedThisFrame) Select((selected + 1) % actions.Count);
                if (key.upArrowKey.wasPressedThisFrame) Select((selected + actions.Count - 1) % actions.Count);
                if (key.enterKey.wasPressedThisFrame || key.numpadEnterKey.wasPressedThisFrame) actions[selected]();
                return;
            }
            if (ContainerPanel != null && ContainerPanel.IsOpen)
            {
                if (key.cKey.wasPressedThisFrame)
                {
                    ContainerPanel.Close();
                    return;
                }
                if (key.tabKey.wasPressedThisFrame)
                {
                    ContainerPanel.Close();
                    TogglePossession();
                    return;
                }
                if (key.tKey.wasPressedThisFrame)
                {
                    ContainerPanel.CycleSelection();
                }
                if (!ExternalMovementInput) Brain.ManualDirection = Vector3.zero;
                cameraMotion = Vector3.zero;
                ReleasePointer();
                return;
            }
            if (key.tabKey.wasPressedThisFrame) TogglePossession();
            if (key.fKey.wasPressedThisFrame && !Brain.Possessed)
            {
                FreeSpectator = !FreeSpectator;
                if (FreeSpectator) freePosition = View.transform.position;
            }
            if (key.vKey.wasPressedThisFrame && View != null)
            {
                View.CycleViewMode();
            }
            if (key.lKey.wasPressedThisFrame && Hud != null) Hud.ToggleDetailed();
            if (key.cKey.wasPressedThisFrame && Brain.Possessed)
            {
                if (Brain.Actions != null && Brain.Actions.Held != null)
                {
                    var heldTarget = Brain.Actions.Held.GetComponent<CityLife.Items.PhysicalItem>();
                    if (heldTarget != null && HearthCooking.CanRoast(heldTarget.itemTypeId) && HearthCooking.TryRoastHeldItem(Brain))
                    {
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                        if (Brain.Survival != null) Brain.Survival.RememberCurrentWorld();
                        UpdatePickupTargetLabel();
                        return;
                    }
                }
                if (ContainerPanel != null)
                {
                    ContainerPanel.Open();
                    if (!ExternalMovementInput) Brain.ManualDirection = Vector3.zero;
                    cameraMotion = Vector3.zero;
                    return;
                }
            }
            if (key.tKey.wasPressedThisFrame && Brain.Possessed)
            {
                CyclePickupTarget();
            }
            if (Brain.Possessed)
            {
                RefreshPickupTarget();
            }
            if (Brain.Possessed && (key.eKey.wasPressedThisFrame || key.gKey.wasPressedThisFrame))
            {
                InteractCurrentTarget(key.gKey.wasPressedThisFrame);
            }
            else if (key.gKey.wasPressedThisFrame)
            {
                InteractPhysicalItem();
            }
            if (Brain.Possessed && key.hKey.wasPressedThisFrame)
            {
                TryEatInventoryFruit();
            }
            if (key.xKey.wasPressedThisFrame)
            {
                ToggleClubHolster();
            }
            if (Brain.Possessed && key.bKey.wasPressedThisFrame)
            {
                InteractWaistMoonbag();
            }
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
            if (Brain.Possessed)
            {
                if (!ExternalMovementInput) Brain.ManualDirection = Vector3.ClampMagnitude(motion, 1);
                bool wantsSprint = key.leftShiftKey.isPressed || key.rightShiftKey.isPressed;
                if (Brain.Actor != null)
                {
                    if (wantsSprint && motion.sqrMagnitude > 0.01f && Brain.Actor.Stamina > 5f)
                    {
                        Brain.Actor.IsSprinting = true;
                    }
                    else if (!wantsSprint || motion.sqrMagnitude <= 0.01f || Brain.Actor.Stamina <= 0f)
                    {
                        Brain.Actor.IsSprinting = false;
                    }
                }
            }
            else if (FreeSpectator)
            {
                float up = (key.spaceKey.isPressed || key.eKey.isPressed) ? 1f : 0f;
                float down = (key.leftCtrlKey.isPressed || key.cKey.isPressed || key.qKey.isPressed) ? 1f : 0f;
                motion.y = up - down;

                // Mouse wheel adjusts flight speed exponentially
                float scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    SpectatorFlightSpeed = Mathf.Clamp(SpectatorFlightSpeed + Mathf.Sign(scroll) * 3.5f, 3f, 80f);
                }

                float boost = (key.leftShiftKey.isPressed || key.rightShiftKey.isPressed) ? 2.5f : 1.0f;
                cameraMotion = motion * (SpectatorFlightSpeed * boost);

                freePosition += cameraMotion * Time.unscaledDeltaTime;
                freePosition = new Vector3(
                    Mathf.Clamp(freePosition.x, CameraMinimum.x, CameraMaximum.x),
                    Mathf.Clamp(freePosition.y, CameraMinimum.y, CameraMaximum.y),
                    Mathf.Clamp(freePosition.z, CameraMinimum.z, CameraMaximum.z));
            }
        }
        private void FixedUpdate()
        {
            if (!initialized || MenuOpen || DisplayShortcutActive || Brain.Possessed || !FreeSpectator) return;
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
                if (ContainerPanel != null && ContainerPanel.IsOpen) ContainerPanel.Close();
                CurrentPickupTarget = null;
                UpdatePickupTargetLabel();
                // Observe from the current following pose; do not reset or replace the inhabitant.
                freePosition = View.transform.position; yaw = View.transform.eulerAngles.y;
                pitch = View.transform.eulerAngles.x; if (pitch > 180) pitch -= 360;
            }
            if (MenuOpen && Page == "Controls") ShowPage("Controls");
        }
        public void OnContainerPanelOpened()
        {
            resumeCaptureFromPanel = PersistentMouseCapture && Looking;
            ReleasePointer();
            if (!ExternalMovementInput && Brain != null) Brain.ManualDirection = Vector3.zero;
            cameraMotion = Vector3.zero;
            if (TargetPromptText != null) TargetPromptText.gameObject.SetActive(false);
        }
        public void OnContainerPanelClosed()
        {
            if (resumeCaptureFromPanel && Brain != null && Brain.Possessed && (Application.isFocused || AllowUnfocusedTestInput))
            {
                CapturePointer();
            }
            resumeCaptureFromPanel = false;
            RefreshPickupTarget();
        }
        public List<NpcInteractable> GetEligiblePickupTargets()
        {
            var list = new List<NpcInteractable>();
            if (Brain == null || Brain.PhysicalItems == null) return list;

            Vector3 actorPos = Brain.transform.position;
            Vector3 sightPos = actorPos + Vector3.up;

            foreach (var cand in Brain.PhysicalItems.AllInteractables)
            {
                if (cand == null || !cand.isActiveAndEnabled || !cand.Permission || cand.Approach == null) continue;
                if (Brain.Actions != null && cand == Brain.Actions.Held) continue;

                var phys = cand.GetComponent<CityLife.Items.PhysicalItem>();
                if (phys == null || phys.IsStored) continue;

                float distApproach = Vector3.Distance(actorPos, cand.Approach.position);
                float distSight = Vector3.Distance(sightPos, cand.SightPoint);
                if (distApproach > 0.65f || distSight > 1.7f) continue;

                list.Add(cand);
            }

            list.Sort((a, b) => string.CompareOrdinal(a.StableId, b.StableId));
            return list;
        }
        public void CyclePickupTarget()
        {
            var eligible = GetEligiblePickupTargets();
            if (eligible.Count == 0)
            {
                CurrentPickupTarget = null;
                cyclePickupIndex = -1;
                UpdatePickupTargetLabel();
                return;
            }

            int currentIdx = -1;
            if (CurrentPickupTarget != null)
            {
                for (int i = 0; i < eligible.Count; i++)
                {
                    if (eligible[i] == CurrentPickupTarget)
                    {
                        currentIdx = i;
                        break;
                    }
                }
            }

            cyclePickupIndex = (currentIdx + 1) % eligible.Count;
            CurrentPickupTarget = eligible[cyclePickupIndex];
            UpdatePickupTargetLabel();
        }
        public void RefreshPickupTarget()
        {
            var eligible = GetEligiblePickupTargets();
            if (eligible.Count == 0)
            {
                CurrentPickupTarget = null;
                cyclePickupIndex = -1;
            }
            else
            {
                if (CurrentPickupTarget == null || !eligible.Contains(CurrentPickupTarget))
                {
                    CurrentPickupTarget = GetBestNearbyPhysicalInteractable();
                }
            }
            UpdatePickupTargetLabel();
        }
        private enum NearbyResourceType { None, Berry, Spring, River }
        private NearbyResourceType currentNearbyResource = NearbyResourceType.None;

        private void DetectNearbySurvivalResources()
        {
            currentNearbyResource = NearbyResourceType.None;
            if (Brain == null || !Brain.Possessed) return;

            var food = (Brain.Survival != null) ? Brain.Survival.Food : null;
            if (food == null) food = FindFirstObjectByType<Starfall.Food.IntegratedFoodRuntime>();
            if (food == null || food.Model == null) return;

            Vector3 actorPos = Brain.transform.position;

            // Check spring
            if (food.Spring != null)
            {
                float d = Vector3.Distance(actorPos, food.SpringPosition);
                if (d < 3.2f)
                {
                    currentNearbyResource = NearbyResourceType.Spring;
                    return;
                }
            }

            // Check primary berry bush
            if (food.Berry != null)
            {
                float dXZ = Vector2.Distance(new Vector2(actorPos.x, actorPos.z), new Vector2(food.BerryPosition.x, food.BerryPosition.z));
                float dY = Mathf.Abs(actorPos.y - food.BerryPosition.y);
                if (dXZ < 3.8f && dY < 2.8f)
                {
                    currentNearbyResource = NearbyResourceType.Berry;
                    return;
                }
            }

            // Check additional berry bushes
            if (food.AdditionalBerryBushes != null)
            {
                foreach (var bush in food.AdditionalBerryBushes)
                {
                    if (bush != null)
                    {
                        float dXZ = Vector2.Distance(new Vector2(actorPos.x, actorPos.z), new Vector2(bush.transform.position.x, bush.transform.position.z));
                        float dY = Mathf.Abs(actorPos.y - bush.transform.position.y);
                        if (dXZ < 3.8f && dY < 2.8f)
                        {
                            currentNearbyResource = NearbyResourceType.Berry;
                            return;
                        }
                    }
                }
            }

            // Check freshwater river (requires physical contact with shallow water or river waterline)
            if (CoastalTerrain.CanDrinkFromRiver(actorPos.x, actorPos.z, actorPos.y, CoastalWater.CurrentLevel))
            {
                currentNearbyResource = NearbyResourceType.River;
                return;
            }
        }

        public void UpdatePickupTargetLabel()
        {
            DetectNearbySurvivalResources();

            if (currentNearbyResource == NearbyResourceType.Spring)
            {
                CurrentPickupTargetLabel = "[E] Drink Freshwater";
            }
            else if (currentNearbyResource == NearbyResourceType.River)
            {
                CurrentPickupTargetLabel = "[E] Drink Fresh River Water";
            }
            else if (currentNearbyResource == NearbyResourceType.Berry)
            {
                var food = (Brain != null && Brain.Survival != null) ? Brain.Survival.Food : null;
                if (food == null) food = FindFirstObjectByType<Starfall.Food.IntegratedFoodRuntime>();
                int fruitStock = food != null && food.Model != null ? food.Model.State.fruitStock : 2;
                if (fruitStock <= 0) fruitStock = 1;
                var moonbag = Brain != null ? Brain.GetComponentInChildren<HunterMoonbag>() : null;
                int mbCount = moonbag != null ? moonbag.StoredCount : 0;
                int freeHands = GetFreeHandCount();
                var s = food != null && food.Model != null ? food.Model.State : null;
                int totalCarried = GetTotalCarriedBerries(s, Brain);

                if (fruitStock > 0)
                {
                    if (totalCarried >= 4)
                    {
                        CurrentPickupTargetLabel = "Inventory Full: 4/4 Berries Carried [H: Eat | G: Drop]";
                    }
                    else if (freeHands > 0)
                    {
                        CurrentPickupTargetLabel = "[E] Pick Berry into Hand";
                    }
                    else if (moonbag != null && moonbag.CanStore)
                    {
                        CurrentPickupTargetLabel = $"[E] Pick Berry into Moonbag ({mbCount}/2)";
                    }
                    else
                    {
                        CurrentPickupTargetLabel = "Hands Full (Club in hand, X: holster) [H: Eat | G: Drop]";
                    }
                }
                else if (mbCount > 0 && freeHands > 0)
                {
                    CurrentPickupTargetLabel = $"[B] Retrieve Berry from Moonbag ({mbCount}/2)  |  [H] Eat";
                }
                else
                {
                    CurrentPickupTargetLabel = "Sourfig Berry Bush [Depleted]";
                }
            }
            else if (Brain != null && Brain.Actions != null && Brain.Actions.Held != null)
            {
                var heldTarget = Brain.Actions.Held.GetComponent<CityLife.Items.PhysicalItem>();
                var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();
                int mbCount = moonbag != null ? moonbag.StoredCount : 0;

                var cooker = Brain.GetComponentInChildren<HearthCooking>();
                if (cooker == null) cooker = FindFirstObjectByType<HearthCooking>();
                bool nearHearth = cooker != null && cooker.IsNearHearth(Brain.transform.position, out _);

                if (heldTarget != null && HearthCooking.CanRoast(heldTarget.itemTypeId) && nearHearth)
                {
                    string name = HearthCooking.GetItemDisplayName(heldTarget.itemTypeId);
                    CurrentPickupTargetLabel = $"Held: {name} [C / E: Roast on Hearth Fire | H: Eat | G: Drop]";
                }
                else if (heldTarget != null && heldTarget.itemTypeId == "food-cooked-fish")
                {
                    CurrentPickupTargetLabel = "Held: Roasted River Barber [H: Feast | G: Drop]";
                }
                else if (heldTarget != null && heldTarget.itemTypeId == "food-cooked-crab")
                {
                    CurrentPickupTargetLabel = "Held: Roasted Shore Crab [H: Feast | G: Drop]";
                }
                else if (heldTarget != null && heldTarget.itemTypeId == "food-protein-crab")
                {
                    CurrentPickupTargetLabel = "Held: Protein Shore Crab [H: Eat | G: Drop]";
                }
                else if (heldTarget != null && heldTarget.itemTypeId == "food-river-fish")
                {
                    CurrentPickupTargetLabel = "Held: Freshwater River Barber [H: Eat | G: Drop]";
                }
                else if (heldTarget != null && heldTarget.itemTypeId == "food-river-carp")
                {
                    CurrentPickupTargetLabel = "Held: Gauteng Common Carp [H: Eat | G: Drop]";
                }
                else if (heldTarget != null && heldTarget.itemTypeId == "food-sourfig-berry")
                {
                    CurrentPickupTargetLabel = $"Held: Sourfig Berry [H: Eat | B: Store in Moonbag ({mbCount}/2) | G: Drop]";
                }
                else
                {
                    CurrentPickupTargetLabel = $"Held: {Brain.Actions.Held.StableId} [G: Drop]";
                }
            }
            else if (CurrentPickupTarget != null)
            {
                var targetPickup = CurrentPickupTarget.GetComponent<CityLife.Items.PhysicalItem>();
                if (targetPickup != null && targetPickup.itemTypeId == "food-protein-crab")
                {
                    CurrentPickupTargetLabel = "Target: [E: Catch Protein Crab]";
                }
                else if (targetPickup != null && targetPickup.itemTypeId == "food-river-fish")
                {
                    CurrentPickupTargetLabel = "Target: [E: Catch River Barber]";
                }
                else if (targetPickup != null && targetPickup.itemTypeId == "food-river-carp")
                {
                    CurrentPickupTargetLabel = "Target: [E: Catch Common Carp]";
                }
                else if (targetPickup != null && targetPickup.itemTypeId == "wood-driftwood-log")
                {
                    CurrentPickupTargetLabel = "Target: [E: Pick Up Driftwood Log]";
                }
                else
                {
                    CurrentPickupTargetLabel = $"Target: {CurrentPickupTarget.StableId} [E: Pick Up | T: Cycle]";
                }
            }
            else
            {
                var moonbag = Brain != null ? Brain.GetComponentInChildren<HunterMoonbag>() : null;
                int mbCount = moonbag != null ? moonbag.StoredCount : 0;
                var food = (Brain != null && Brain.Survival != null) ? Brain.Survival.Food : null;
                if (food == null) food = FindFirstObjectByType<Starfall.Food.IntegratedFoodRuntime>();

                if (mbCount > 0)
                {
                    CurrentPickupTargetLabel = $"Waist Moonbag: {mbCount}/2 [B: Take Berry | H: Eat]";
                    if (food != null && food.Model != null && food.Model.State.freshwaterMl >= 250 && food.Model.State.hydration < 8500)
                        CurrentPickupTargetLabel += $" | Water: {food.Model.State.freshwaterMl}ml [E: Drink]";
                }
                else if (food != null && food.Model != null && food.Model.State.carriedFruit > 0)
                {
                    CurrentPickupTargetLabel = $"Carried Fruit: {food.Model.State.carriedFruit} [H: Eat]";
                    if (food.Model.State.freshwaterMl >= 250 && food.Model.State.hydration < 8500)
                        CurrentPickupTargetLabel += $" | Water: {food.Model.State.freshwaterMl}ml [E: Drink]";
                }
                else if (food != null && food.Model != null && food.Model.State.freshwaterMl >= 250 && food.Model.State.hydration < 8500)
                {
                    CurrentPickupTargetLabel = $"Water container: {food.Model.State.freshwaterMl}ml [E: Drink]";
                }
                else
                {
                    CurrentPickupTargetLabel = "";
                }
            }

            if (TargetPromptText == null)
            {
                if (Hud == null) Hud = FindFirstObjectByType<NpcDecisionHud>();
                if (Hud != null && Hud.Canvas != null)
                {
                    var promptObj = new GameObject("Pickup prompt label", typeof(RectTransform), typeof(Text));
                    var pRect = promptObj.GetComponent<RectTransform>();
                    pRect.SetParent(Hud.Canvas.transform, false);
                    pRect.anchorMin = new Vector2(0.5f, 0f);
                    pRect.anchorMax = new Vector2(0.5f, 0f);
                    pRect.pivot = new Vector2(0.5f, 0f);
                    pRect.anchoredPosition = new Vector2(0f, 30f);
                    pRect.sizeDelta = new Vector2(600f, 40f);
                    TargetPromptText = promptObj.GetComponent<Text>();
                    TargetPromptText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    TargetPromptText.fontSize = 18;
                    TargetPromptText.color = new Color(0.6f, 0.93f, 0.93f, 1f);
                    TargetPromptText.alignment = TextAnchor.MiddleCenter;
                    TargetPromptText.supportRichText = false;
                }
            }

            if (TargetPromptText != null)
            {
                TargetPromptText.text = CurrentPickupTargetLabel;
                TargetPromptText.gameObject.SetActive(!string.IsNullOrEmpty(CurrentPickupTargetLabel) && Brain != null && Brain.Possessed && (ContainerPanel == null || !ContainerPanel.IsOpen));
            }
        }
        public NpcInteractable GetBestNearbyPhysicalInteractable()
        {
            if (Brain == null || Brain.PhysicalItems == null) return null;

            NpcInteractable best = null;
            float bestScore = float.MinValue;
            Vector3 actorPos = Brain.transform.position;
            Vector3 camPos = View != null ? View.transform.position : (Camera.main != null ? Camera.main.transform.position : actorPos);
            Vector3 camFwd = View != null ? View.transform.forward : (Camera.main != null ? Camera.main.transform.forward : Brain.transform.forward);

            foreach (var cand in Brain.PhysicalItems.AllInteractables)
            {
                if (cand == null || !cand.isActiveAndEnabled || !cand.Permission || cand.Approach == null) continue;
                if (Brain.Actions != null && cand == Brain.Actions.Held) continue;

                var phys = cand.GetComponent<CityLife.Items.PhysicalItem>();
                if (phys == null || phys.IsStored) continue;

                float distApproach = Vector3.Distance(actorPos, cand.Approach.position);
                float distSight = Vector3.Distance(actorPos + Vector3.up, cand.SightPoint);
                if (distApproach > 0.65f || distSight > 1.7f) continue;

                Vector3 toCand = (cand.transform.position - camPos).normalized;
                float dot = Vector3.Dot(camFwd, toCand);

                // Deterministic score with distance penalty and StableId tie-break
                float score = dot * 10f - distApproach;
                if (best == null || score > bestScore || (Mathf.Abs(score - bestScore) < 0.001f && string.CompareOrdinal(cand.StableId, best.StableId) < 0))
                {
                    best = cand;
                    bestScore = score;
                }
            }

            return best;
        }
        public void InteractPhysicalItem()
        {
            if (Brain == null || Brain.Actions == null || !Brain.Possessed) return;
            if (Brain.Actions.Held != null)
            {
                var heldPhys = Brain.Actions.Held.GetComponent<CityLife.Items.PhysicalItem>();
                if (heldPhys != null)
                {
                    string heldTypeId = heldPhys.itemTypeId;
                    Brain.ExecutePlayerAction(NpcActionKind.Drop, Brain.Actions.Held.StableId);
                    RefreshPickupTarget();
                    if (heldTypeId == "food-sourfig-berry" || heldTypeId == "fruit")
                    {
                        var food = (Brain.Survival != null) ? Brain.Survival.Food : null;
                        if (food == null) food = FindFirstObjectByType<Starfall.Food.IntegratedFoodRuntime>();
                        if (food != null && food.Model != null)
                        {
                            food.Model.State.carriedFruit = Mathf.Max(0, food.Model.State.carriedFruit - 1);
                            ReconcileCarriedFruit(food.Model.State, Brain, this);
                        }
                    }
                    if (ContainerPanel != null && ContainerPanel.IsOpen)
                    {
                        ContainerPanel.UpdateContent();
                    }
                }
            }
            else
            {
                NpcInteractable candidate = CurrentPickupTarget;
                if (candidate == null || !candidate.isActiveAndEnabled || !candidate.Permission || candidate.Approach == null)
                {
                    candidate = GetBestNearbyPhysicalInteractable();
                }

                if (candidate == null && Brain.PhysicalItems != null)
                {
                    var fallback = Brain.PhysicalItems.DemonstrationInteractable;
                    if (fallback != null && fallback.isActiveAndEnabled && fallback.Permission && fallback.Approach != null)
                    {
                        float dist = Vector3.Distance(Brain.transform.position, fallback.Approach.position);
                        if (dist <= 0.65f) candidate = fallback;
                    }
                }

                if (candidate != null)
                {
                    float distApproach = Vector3.Distance(Brain.transform.position, candidate.Approach.position);
                    float distSight = Vector3.Distance(Brain.transform.position + Vector3.up, candidate.SightPoint);
                    if (distApproach <= 0.65f && distSight <= 1.7f)
                    {
                        Brain.ExecutePlayerAction(NpcActionKind.Pickup, candidate.StableId);
                        RefreshPickupTarget();
                        if (ContainerPanel != null && ContainerPanel.IsOpen)
                        {
                            ContainerPanel.UpdateContent();
                        }
                    }
                }
            }
        }
        public void InteractCurrentTarget(bool isDropKey)
        {
            if (Brain == null || !Brain.Possessed) return;

            // If G key was pressed and we are holding an item, drop it
            if (isDropKey && Brain.Actions != null && Brain.Actions.Held != null)
            {
                InteractPhysicalItem();
                return;
            }

            // If near hearth holding raw roastable item, roast it on E
            if (!isDropKey && Brain.Actions != null && Brain.Actions.Held != null)
            {
                var heldPhys = Brain.Actions.Held.GetComponent<CityLife.Items.PhysicalItem>();
                if (heldPhys != null && HearthCooking.CanRoast(heldPhys.itemTypeId) && HearthCooking.TryRoastHeldItem(Brain))
                {
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                    if (Brain.Survival != null) Brain.Survival.RememberCurrentWorld();
                    UpdatePickupTargetLabel();
                    return;
                }
            }

            // If near survival resource, interact with it on E (or G if nothing held)
            if (currentNearbyResource == NearbyResourceType.Spring)
            {
                var food = (Brain.Survival != null) ? Brain.Survival.Food : null;
                if (food == null) food = FindFirstObjectByType<Starfall.Food.IntegratedFoodRuntime>();
                if (food != null && food.Model != null)
                {
                    var s = food.Model.State;
                    var receipt = food.Model.Execute(s.world, s.generation, s.lastRequest + 1, Starfall.Food.FoodAction.Drink, "spring", food);
                    if (receipt.success)
                    {
                        s.knowsSpring = true;
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                        if (Brain.Survival != null) Brain.Survival.RememberCurrentWorld();
                    }
                    UpdatePickupTargetLabel();
                }
                return;
            }
            else if (currentNearbyResource == NearbyResourceType.River)
            {
                var food = (Brain.Survival != null) ? Brain.Survival.Food : null;
                if (food == null) food = FindFirstObjectByType<Starfall.Food.IntegratedFoodRuntime>();
                if (food != null && food.Model != null)
                {
                    var s = food.Model.State;
                    s.hydration = Mathf.Min(10000, s.hydration + 2500);
                    s.freshwaterMl = 2000;
                    Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 1800);
                    if (Brain.Survival != null)
                    {
                        Brain.Survival.BoostPlaceAffinity("freshwater-river", 15);
                        Brain.Survival.RememberCurrentWorld();
                    }
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                    UpdatePickupTargetLabel();
                }
                return;
            }
            else if (currentNearbyResource == NearbyResourceType.Berry)
            {
                var food = (Brain.Survival != null) ? Brain.Survival.Food : null;
                if (food == null) food = FindFirstObjectByType<Starfall.Food.IntegratedFoodRuntime>();
                if (food != null && food.Model != null)
                {
                    var s = food.Model.State;
                    ReconcileCarriedFruit(s, Brain, this);
                    var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();
                    if (moonbag == null) moonbag = Brain.gameObject.AddComponent<HunterMoonbag>();

                    if (!s.knowsBerry)
                    {
                        var inspectReceipt = food.Model.Execute(s.world, s.generation, s.lastRequest + 1, Starfall.Food.FoodAction.Inspect, "berry", food);
                        if (inspectReceipt.success) s.knowsBerry = true;
                    }
                    if (s.fruitStock <= 0)
                    {
                        s.fruitStock = 1;
                    }

                    int totalCarried = GetTotalCarriedBerries(s, Brain);
                    int freeHands = GetFreeHandCount();

                    if (s.fruitStock > 0)
                    {
                        if (totalCarried >= 4)
                        {
                            CurrentPickupTargetLabel = "[Cannot pick: Inventory full (4/4 carried) - Press H to eat]";
                        }
                        else if (freeHands > 0)
                        {
                            var receipt = food.Model.Execute(s.world, s.generation, s.lastRequest + 1, Starfall.Food.FoodAction.Gather, "berry", food);
                            if (receipt.success)
                            {
                                s.knowsBerry = true;
                                food.HarvestBerry();
                                food.SyncFruitVisual();
                                SpawnBerryInHand();
                                s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
                                if (Brain.Actor != null) Brain.Actor.Gesture();
                                if (Brain.Survival != null) Brain.Survival.RememberCurrentWorld();
                                CurrentPickupTargetLabel = "Picked ripe sourfig berry into hand";
                            }
                            else
                            {
                                CurrentPickupTargetLabel = $"[Cannot pick: {receipt.code}]";
                            }
                        }
                        else if (moonbag != null && moonbag.CanStore)
                        {
                            var receipt = food.Model.Execute(s.world, s.generation, s.lastRequest + 1, Starfall.Food.FoodAction.Gather, "berry", food);
                            if (receipt.success)
                            {
                                s.knowsBerry = true;
                                food.HarvestBerry();
                                moonbag.StoreFruit();
                                food.SyncFruitVisual();
                                s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
                                if (Brain.Actor != null) Brain.Actor.Gesture();
                                if (Brain.Survival != null) Brain.Survival.RememberCurrentWorld();
                                CurrentPickupTargetLabel = $"Stored berry in waist moonbag ({moonbag.StoredCount}/2)";
                            }
                            else
                            {
                                CurrentPickupTargetLabel = $"[Cannot pick: {receipt.code}]";
                            }
                        }
                        else
                        {
                            CurrentPickupTargetLabel = "Hands & Moonbag Full [H: Eat | G: Drop]";
                        }
                    }
                    else if (moonbag.CanRetrieve && freeHands > 0)
                    {
                        moonbag.RetrieveFruit();
                        SpawnBerryInHand();
                        if (Brain.Actor != null) Brain.Actor.Gesture();
                    }
                    UpdatePickupTargetLabel();
                }
                return;
            }

            // If pressing E with nothing targeted and thirsty, drink from carried freshwater container
            if (!isDropKey && CurrentPickupTarget == null && currentNearbyResource == NearbyResourceType.None)
            {
                var food = (Brain.Survival != null) ? Brain.Survival.Food : null;
                if (food == null) food = FindFirstObjectByType<Starfall.Food.IntegratedFoodRuntime>();
                if (food != null && food.Model != null && food.Model.State.freshwaterMl >= 250 && food.Model.State.hydration < 8500)
                {
                    var s = food.Model.State;
                    s.freshwaterMl -= 250;
                    s.hydration = Mathf.Min(10000, s.hydration + 2000);
                    Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 1500);
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                    if (Brain.Survival != null) Brain.Survival.RememberCurrentWorld();
                    UpdatePickupTargetLabel();
                    return;
                }
            }

            // Otherwise, interact with physical item (pick up or drop)
            InteractPhysicalItem();
        }

        public bool IsLeftHandFree()
        {
            var carry = Brain != null ? Brain.GetComponentInChildren<HunterClubCarry>() : null;
            if (carry != null && !carry.Stowed) return false;
            if (Brain != null && Brain.Actions != null && Brain.Actions.HeldLeft != null) return false;
            return true;
        }

        public bool IsRightHandFree()
        {
            if (Brain != null && Brain.Actions != null && Brain.Actions.HeldRight != null) return false;
            return true;
        }

        public int GetFreeHandCount()
        {
            return (IsRightHandFree() ? 1 : 0) + (IsLeftHandFree() ? 1 : 0);
        }

        public static int GetTotalCarriedBerries(Starfall.Food.FoodState s, NpcAutonomy brain)
        {
            if (brain == null) return s != null ? s.carriedFruit : 0;
            int physicalInHands = 0;
            if (brain.Actions != null)
            {
                if (brain.Actions.HeldRight != null && IsFruitStatic(brain.Actions.HeldRight)) physicalInHands++;
                if (brain.Actions.HeldLeft != null && IsFruitStatic(brain.Actions.HeldLeft)) physicalInHands++;
            }
            var moonbag = brain.GetComponentInChildren<HunterMoonbag>();
            int inMoonbag = moonbag != null ? moonbag.StoredCount : 0;
            int looseStock = s != null ? s.carriedFruit : 0;
            return physicalInHands + inMoonbag + looseStock;
        }

        public static void MaterializeSavedCarriedFruit(Starfall.Food.FoodState s, NpcAutonomy brain, NpcPlayerControls controls)
        {
            if (s == null || brain == null || s.carriedFruit <= 0) return;
            var moonbag = brain.GetComponentInChildren<HunterMoonbag>();

            // 1. If moonbag has room, deposit saved fruit into moonbag first
            while (s.carriedFruit > 0 && moonbag != null && moonbag.CanStore)
            {
                moonbag.StoreFruit();
                s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
            }

            // 2. If hands have room, spawn in hand
            if (s.carriedFruit > 0 && controls != null && controls.GetFreeHandCount() > 0)
            {
                controls.SpawnBerryInHand();
                s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
            }
        }

        public static void ReconcileCarriedFruit(Starfall.Food.FoodState s, NpcAutonomy brain, NpcPlayerControls controls = null)
        {
            if (s == null) return;
            if (brain == null)
            {
                s.carriedFruit = 0;
                return;
            }

            if (controls == null)
            {
                controls = brain.GetComponent<NpcPlayerControls>() ?? UnityEngine.Object.FindFirstObjectByType<NpcPlayerControls>();
            }

            if (s.carriedFruit > 0 && controls != null)
            {
                MaterializeSavedCarriedFruit(s, brain, controls);
            }

            int physicalInHands = 0;
            if (brain.Actions != null)
            {
                if (brain.Actions.HeldRight != null && IsFruitStatic(brain.Actions.HeldRight)) physicalInHands++;
                if (brain.Actions.HeldLeft != null && IsFruitStatic(brain.Actions.HeldLeft)) physicalInHands++;
            }
            var moonbag = brain.GetComponentInChildren<HunterMoonbag>();
            int inMoonbag = moonbag != null ? moonbag.StoredCount : 0;

            if (physicalInHands == 0 && inMoonbag == 0)
            {
                s.carriedFruit = 0;
            }
            else
            {
                int maxRemaining = Mathf.Max(0, 4 - (physicalInHands + inMoonbag));
                s.carriedFruit = Mathf.Clamp(s.carriedFruit, 0, maxRemaining);
            }
        }

        public static bool IsFruitStatic(NpcInteractable item)
        {
            if (item == null) return false;
            var phys = item.GetComponent<CityLife.Items.PhysicalItem>();
            if (phys != null && (phys.itemTypeId == "food-sourfig-berry" || phys.itemTypeId == "fruit")) return true;
            return item.StableId.Contains("berry") || item.StableId.Contains("fruit");
        }

        public void ToggleClubHolster()
        {
            if (Brain == null) return;
            var carry = Brain.GetComponentInChildren<HunterClubCarry>();
            if (carry == null) return;

            if (!carry.Stowed)
            {
                carry.SetStowed(true);
                if (Brain.Log != null)
                    Brain.Log.Record(Brain.Tick, "DECISION", Brain.DescribePerception(), "holster club", "Player key X input", "Club holstered onto back");
                Brain.LastResult = "Holstered hunter's club onto back";
            }
            else
            {
                if (Brain.Actions != null && Brain.Actions.HeldLeft != null)
                {
                    Brain.LastResult = "Cannot draw club while left hand is holding an item (press G to drop)";
                    if (Brain.Log != null)
                        Brain.Log.Record(Brain.Tick, "DECISION", Brain.DescribePerception(), "draw club", "Left hand occupied", "Cannot draw club while holding item");
                    UpdatePickupTargetLabel();
                    return;
                }
                carry.SetStowed(false);
                if (Brain.Log != null)
                    Brain.Log.Record(Brain.Tick, "DECISION", Brain.DescribePerception(), "draw club", "Player key X input", "Club drawn to left hand");
                Brain.LastResult = "Drew hunter's club from back";
            }
            UpdatePickupTargetLabel();
        }

        public void InteractWaistMoonbag()
        {
            if (Brain == null || !Brain.Possessed) return;
            var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();
            if (moonbag == null) moonbag = Brain.gameObject.AddComponent<HunterMoonbag>();

            // 1. If holding edible fruit / berry in hand, store into moonbag!
            NpcInteractable heldFruit = null;
            if (Brain.Actions != null)
            {
                if (Brain.Actions.HeldRight != null && IsFruitItem(Brain.Actions.HeldRight))
                    heldFruit = Brain.Actions.HeldRight;
                else if (Brain.Actions.HeldLeft != null && IsFruitItem(Brain.Actions.HeldLeft))
                    heldFruit = Brain.Actions.HeldLeft;
            }

            if (heldFruit != null)
            {
                if (moonbag.CanStore)
                {
                    moonbag.StoreFruit();
                    var heldObj = heldFruit.gameObject;
                    Brain.ExecutePlayerAction(NpcActionKind.Drop, heldFruit.StableId);
                    Destroy(heldObj);
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                    UpdatePickupTargetLabel();
                    return;
                }
            }

            // 2. If hands have room and moonbag has fruit, retrieve fruit into hand!
            if (moonbag.CanRetrieve && GetFreeHandCount() > 0)
            {
                moonbag.RetrieveFruit();
                SpawnBerryInHand();
                if (Brain.Actor != null) Brain.Actor.Gesture();
                UpdatePickupTargetLabel();
                return;
            }
        }

        private bool IsFruitItem(NpcInteractable item)
        {
            if (item == null) return false;
            var phys = item.GetComponent<PhysicalItem>();
            if (phys != null && (phys.itemTypeId == "food-sourfig-berry" || phys.itemTypeId == "fruit")) return true;
            return item.StableId.Contains("berry") || item.StableId.Contains("fruit");
        }

        private static int dynamicBerryIdCounter = 200;
        public GameObject SpawnBerryInHand()
        {
            if (Brain == null || Brain.Actions == null) return null;
            bool rightFree = IsRightHandFree();
            bool leftFree = IsLeftHandFree();
            if (!rightFree && !leftFree) return null;

            Transform targetHand = rightFree ? Brain.Actions.RightHandTransform : Brain.Actions.LeftHandTransform;
            bool isLeft = !rightFree;
            if (targetHand == null) return null;

            string berryId = $"held-sourfig-berry-{++dynamicBerryIdCounter}";
            var berryGo = new GameObject(berryId);
            berryGo.layer = 11;

            var col = berryGo.AddComponent<BoxCollider>();
            col.size = new Vector3(0.09f, 0.09f, 0.09f);
            col.isTrigger = true;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual";
            var vCol = visual.GetComponent<Collider>();
            if (vCol != null) UnityEngine.Object.DestroyImmediate(vCol);
            visual.transform.SetParent(berryGo.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = new Vector3(0.08f, 0.09f, 0.08f);

            var mr = visual.GetComponent<MeshRenderer>();
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var berryMat = new Material(litShader) { name = "Sourfig Berry Prop" };
            berryMat.SetColor("_BaseColor", new Color(0.72f, 0.18f, 0.52f, 1f));
            mr.sharedMaterial = berryMat;

            var crownGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crownGo.name = "Crown";
            var crownCol = crownGo.GetComponent<Collider>();
            if (crownCol != null) UnityEngine.Object.DestroyImmediate(crownCol);
            crownGo.transform.SetParent(visual.transform, false);
            crownGo.transform.localPosition = new Vector3(0, 0.52f, 0);
            crownGo.transform.localScale = new Vector3(0.5f, 0.25f, 0.5f);
            var crownMat = new Material(litShader) { name = "Sourfig Crown Prop" };
            crownMat.SetColor("_BaseColor", new Color(0.38f, 0.55f, 0.22f, 1f));
            crownGo.GetComponent<MeshRenderer>().sharedMaterial = crownMat;

            var approach = new GameObject(berryId + " approach");
            approach.transform.SetParent(berryGo.transform, false);
            approach.transform.localPosition = Vector3.zero;

            var ni = berryGo.AddComponent<NpcInteractable>();
            ni.StableId = berryId;
            ni.WorldId = Brain.InstanceWorldId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;

            var phys = berryGo.AddComponent<PhysicalItem>();
            phys.itemId = berryId;
            phys.itemTypeId = "food-sourfig-berry";
            phys.massKg = 0.08f;
            phys.dimensions = new PhysicalDimensions(0.08f, 0.08f, 0.08f);

            Brain.Actions.HoldItemDirect(ni, isLeft);

            if (Brain.Registry != null)
            {
                var list = new List<NpcInteractable>(Brain.Registry) { ni };
                Brain.Registry = list.ToArray();
            }

            return berryGo;
        }

        private static int dynamicFishIdCounter = 100;
        public GameObject SpawnFishInHand()
        {
            if (Brain == null || Brain.Actions == null) return null;
            bool rightFree = IsRightHandFree();
            bool leftFree = IsLeftHandFree();
            if (!rightFree && !leftFree) return null;

            Transform targetHand = rightFree ? Brain.Actions.RightHandTransform : Brain.Actions.LeftHandTransform;
            bool isLeft = !rightFree;
            if (targetHand == null) return null;

            string fishId = $"held-river-fish-{++dynamicFishIdCounter}";
            var fishGo = new GameObject(fishId);
            fishGo.layer = 11;

            var col = fishGo.AddComponent<BoxCollider>();
            col.size = new Vector3(0.14f, 0.14f, 0.42f);
            col.isTrigger = true;

            var visual = new GameObject("Visual", typeof(MeshFilter), typeof(MeshRenderer));
            visual.transform.SetParent(fishGo.transform, false);
            visual.transform.localPosition = new Vector3(0, 0, 0.04f);
            visual.transform.localRotation = Quaternion.Euler(0, 0, 90f); // Orient horizontally across survivor's grip
            visual.transform.localScale = Vector3.one * 0.25f; // ~48cm caught freshwater barber

            var mf = visual.GetComponent<MeshFilter>();
            var mr = visual.GetComponent<MeshRenderer>();
            mf.sharedMesh = RiverFishSchool.SharedBarberMesh ?? RiverFishSchool.CreateProceduralFishMesh();
            mr.sharedMaterial = RiverFishSchool.SharedBarberMaterial ?? RiverFishSchool.CreateFishMaterial();

            var approach = new GameObject(fishId + " approach");
            approach.transform.SetParent(fishGo.transform, false);
            approach.transform.localPosition = Vector3.zero;

            var ni = fishGo.AddComponent<NpcInteractable>();
            ni.StableId = fishId;
            ni.WorldId = Brain.InstanceWorldId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;

            var phys = fishGo.AddComponent<PhysicalItem>();
            phys.itemId = fishId;
            phys.itemTypeId = "food-river-fish";
            phys.massKg = 0.65f;
            phys.dimensions = new PhysicalDimensions(0.22f, 0.12f, 0.12f);

            Brain.Actions.HoldItemDirect(ni, isLeft);

            if (Brain.Registry != null)
            {
                var list = new List<NpcInteractable>(Brain.Registry) { ni };
                Brain.Registry = list.ToArray();
            }

            return fishGo;
        }

        private static int dynamicCarpIdCounter = 100;
        public GameObject SpawnCarpInHand(bool? preferLeft = null)
        {
            if (Brain == null || Brain.Actions == null) return null;
            bool rightFree = IsRightHandFree();
            bool leftFree = IsLeftHandFree();
            if (!rightFree && !leftFree) return null;

            bool isLeft;
            if (preferLeft.HasValue)
            {
                isLeft = preferLeft.Value;
                if (isLeft && !leftFree) return null;
                if (!isLeft && !rightFree) return null;
            }
            else
            {
                isLeft = !rightFree;
            }

            Transform targetHand = isLeft ? Brain.Actions.LeftHandTransform : Brain.Actions.RightHandTransform;
            if (targetHand == null) return null;

            string carpId = $"held-river-carp-{++dynamicCarpIdCounter}";
            var carpGo = new GameObject(carpId);
            carpGo.layer = 11;

            var col = carpGo.AddComponent<BoxCollider>();
            col.size = new Vector3(0.16f, 0.12f, 0.42f);
            col.isTrigger = true;

            var visual = new GameObject("Visual", typeof(MeshFilter), typeof(MeshRenderer));
            visual.transform.SetParent(carpGo.transform, false);
            visual.transform.localPosition = new Vector3(0, 0, 0.05f);
            visual.transform.localRotation = Quaternion.Euler(0, 90f, 0); // Orient horizontally in survivor's grip
            visual.transform.localScale = Vector3.one * 0.22f; // ~42cm caught freshwater carp

            var mf = visual.GetComponent<MeshFilter>();
            var mr = visual.GetComponent<MeshRenderer>();
            mf.sharedMesh = RiverFishSchool.SharedCarpMesh ?? RiverFishSchool.CreateProceduralFishMesh();
            mr.sharedMaterial = RiverFishSchool.SharedCarpMaterial ?? RiverFishSchool.CreateFishMaterial();

            var approach = new GameObject(carpId + " approach");
            approach.transform.SetParent(carpGo.transform, false);
            approach.transform.localPosition = Vector3.zero;

            var ni = carpGo.AddComponent<NpcInteractable>();
            ni.StableId = carpId;
            ni.WorldId = Brain.InstanceWorldId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;

            var phys = carpGo.AddComponent<PhysicalItem>();
            phys.itemId = carpId;
            phys.itemTypeId = "food-river-carp";
            phys.massKg = 0.85f;
            phys.dimensions = new PhysicalDimensions(0.42f, 0.16f, 0.10f);

            Brain.Actions.HoldItemDirect(ni, isLeft);

            if (Brain.Registry != null)
            {
                var list = new List<NpcInteractable>(Brain.Registry) { ni };
                Brain.Registry = list.ToArray();
            }

            return carpGo;
        }

        private static int dynamicCrabIdCounter = 100;
        public GameObject SpawnCrabInHand()
        {
            if (Brain == null || Brain.Actions == null) return null;
            bool rightFree = IsRightHandFree();
            bool leftFree = IsLeftHandFree();
            if (!rightFree && !leftFree) return null;

            Transform targetHand = rightFree ? Brain.Actions.RightHandTransform : Brain.Actions.LeftHandTransform;
            bool isLeft = !rightFree;
            if (targetHand == null) return null;

            string crabId = $"held-protein-crab-{++dynamicCrabIdCounter}";
            var crabGo = new GameObject(crabId);
            crabGo.layer = 11;

            var col = crabGo.AddComponent<BoxCollider>();
            col.size = new Vector3(0.16f, 0.08f, 0.14f);
            col.isTrigger = true;

            var visual = new GameObject("Visual", typeof(MeshFilter), typeof(MeshRenderer));
            visual.transform.SetParent(crabGo.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * 0.75f; // Sits neatly in survivor's hand

            visual.GetComponent<MeshFilter>().sharedMesh = CoastalCrabDistribution.GetOrCreateCrabMesh();
            visual.GetComponent<MeshRenderer>().sharedMaterial = CoastalCrabDistribution.GetOrCreateCrabMaterial();

            var approach = new GameObject(crabId + " approach");
            approach.transform.SetParent(crabGo.transform, false);
            approach.transform.localPosition = Vector3.zero;

            var ni = crabGo.AddComponent<NpcInteractable>();
            ni.StableId = crabId;
            ni.WorldId = Brain.InstanceWorldId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;

            var phys = crabGo.AddComponent<PhysicalItem>();
            phys.itemId = crabId;
            phys.itemTypeId = "food-protein-crab";
            phys.massKg = 0.45f;
            phys.dimensions = new PhysicalDimensions(0.16f, 0.08f, 0.14f);

            Brain.Actions.HoldItemDirect(ni, isLeft);

            if (Brain.Registry != null)
            {
                var list = new List<NpcInteractable>(Brain.Registry) { ni };
                Brain.Registry = list.ToArray();
            }

            return crabGo;
        }

        private static int dynamicMeatIdCounter = 100;
        public GameObject SpawnMeatInHand()
        {
            if (Brain == null || Brain.Actions == null) return null;
            bool rightFree = IsRightHandFree();
            bool leftFree = IsLeftHandFree();
            if (!rightFree && !leftFree) return null;

            Transform targetHand = rightFree ? Brain.Actions.RightHandTransform : Brain.Actions.LeftHandTransform;
            bool isLeft = !rightFree;
            if (targetHand == null) return null;

            string meatId = $"held-wolf-meat-{++dynamicMeatIdCounter}";
            var meatGo = new GameObject(meatId);
            meatGo.layer = 11;

            var col = meatGo.AddComponent<BoxCollider>();
            col.size = new Vector3(0.18f, 0.10f, 0.14f);
            col.isTrigger = true;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            var vCol = visual.GetComponent<Collider>();
            if (vCol != null) UnityEngine.Object.DestroyImmediate(vCol);
            visual.transform.SetParent(meatGo.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = new Vector3(0.16f, 0.08f, 0.12f);

            var mr = visual.GetComponent<MeshRenderer>();
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var meatMat = new Material(litShader) { name = "Venison Hand Prop" };
            meatMat.SetColor("_BaseColor", new Color(0.68f, 0.14f, 0.12f, 1f));
            mr.sharedMaterial = meatMat;

            var approach = new GameObject(meatId + " approach");
            approach.transform.SetParent(meatGo.transform, false);
            approach.transform.localPosition = Vector3.zero;

            var ni = meatGo.AddComponent<NpcInteractable>();
            ni.StableId = meatId;
            ni.WorldId = Brain.InstanceWorldId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;

            var phys = meatGo.AddComponent<PhysicalItem>();
            phys.itemId = meatId;
            phys.itemTypeId = "food-wolf-meat";
            phys.massKg = 1.4f;
            phys.dimensions = new PhysicalDimensions(0.24f, 0.16f, 0.10f);

            Brain.Actions.HoldItemDirect(ni, isLeft);

            if (Brain.Registry != null)
            {
                var list = new List<NpcInteractable>(Brain.Registry) { ni };
                Brain.Registry = list.ToArray();
            }

            return meatGo;
        }

        private static int dynamicLeatherIdCounter = 100;
        public GameObject SpawnLeatherInHand()
        {
            if (Brain == null || Brain.Actions == null) return null;
            bool rightFree = IsRightHandFree();
            bool leftFree = IsLeftHandFree();
            if (!rightFree && !leftFree) return null;

            Transform targetHand = rightFree ? Brain.Actions.RightHandTransform : Brain.Actions.LeftHandTransform;
            bool isLeft = !rightFree;
            if (targetHand == null) return null;

            string leatherId = $"held-wolf-leather-{++dynamicLeatherIdCounter}";
            var leatherGo = new GameObject(leatherId);
            leatherGo.layer = 11;

            var col = leatherGo.AddComponent<BoxCollider>();
            col.size = new Vector3(0.22f, 0.12f, 0.14f);
            col.isTrigger = true;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "Visual";
            var vCol = visual.GetComponent<Collider>();
            if (vCol != null) UnityEngine.Object.DestroyImmediate(vCol);
            visual.transform.SetParent(leatherGo.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            visual.transform.localScale = new Vector3(0.10f, 0.14f, 0.10f);

            var mr = visual.GetComponent<MeshRenderer>();
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var leatherMat = new Material(litShader) { name = "Leather Hand Prop" };
            leatherMat.SetColor("_BaseColor", new Color(0.55f, 0.42f, 0.30f, 1f));
            mr.sharedMaterial = leatherMat;

            var approach = new GameObject(leatherId + " approach");
            approach.transform.SetParent(leatherGo.transform, false);
            approach.transform.localPosition = Vector3.zero;

            var ni = leatherGo.AddComponent<NpcInteractable>();
            ni.StableId = leatherId;
            ni.WorldId = Brain.InstanceWorldId;
            ni.Kind = NpcObjectKind.Item;
            ni.Permission = true;
            ni.Approach = approach.transform;

            var phys = leatherGo.AddComponent<PhysicalItem>();
            phys.itemId = leatherId;
            phys.itemTypeId = "material-wolf-leather";
            phys.massKg = 0.95f;
            phys.dimensions = new PhysicalDimensions(0.35f, 0.22f, 0.08f);

            Brain.Actions.HoldItemDirect(ni, isLeft);

            if (Brain.Registry != null)
            {
                var list = new List<NpcInteractable>(Brain.Registry) { ni };
                Brain.Registry = list.ToArray();
            }

            return leatherGo;
        }

        public void TryEatInventoryFruit()
        {
            if (Brain == null || !Brain.Possessed) return;
            var food = (Brain.Survival != null) ? Brain.Survival.Food : null;
            if (food == null) food = FindFirstObjectByType<Starfall.Food.IntegratedFoodRuntime>();

            // 1. Check if holding an edible item in hand
            NpcInteractable foodToEat = null;
            if (Brain.Actions != null)
            {
                if (Brain.Actions.HeldRight != null && IsEdible(Brain.Actions.HeldRight))
                    foodToEat = Brain.Actions.HeldRight;
                else if (Brain.Actions.HeldLeft != null && IsEdible(Brain.Actions.HeldLeft))
                    foodToEat = Brain.Actions.HeldLeft;
            }

            if (foodToEat != null)
            {
                var phys = foodToEat.GetComponent<PhysicalItem>();
                string typeId = phys != null ? phys.itemTypeId : foodToEat.StableId;
                if (food != null && food.Model != null)
                {
                    var s = food.Model.State;
                    if (typeId == "food-cooked-meat")
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 4500);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 5000);
                        s.satiety = Mathf.Min(10000, s.satiety + 4500);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 4000);
                    }
                    else if (typeId == "food-wolf-meat")
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 2500);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 3200);
                        s.satiety = Mathf.Min(10000, s.satiety + 2200);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 2500);
                    }
                    else if (typeId == "food-cooked-fish")
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 3500);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 4000);
                        s.satiety = Mathf.Min(10000, s.satiety + 3500);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 3500);
                    }
                    else if (typeId == "food-cooked-crab")
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 3000);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 3800);
                        s.satiety = Mathf.Min(10000, s.satiety + 3000);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 3200);
                    }
                    else if (typeId == "food-protein-crab")
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 2500);
                        s.satiety = Mathf.Min(10000, s.satiety + 2000);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 2500);
                    }
                    else if (typeId == "food-river-fish" || typeId == "food-river-carp")
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                        s.body.protein = Mathf.Min(10000, s.body.protein + 2500);
                        s.satiety = Mathf.Min(10000, s.satiety + 2000);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 2200);
                    }
                    else // berry / fruit
                    {
                        s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                        s.hydration = Mathf.Min(10000, s.hydration + 600);
                        s.satiety = Mathf.Min(10000, s.satiety + 1500);
                        Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 1500);
                        s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
                    }
                    s.knowsMealBenefit = true;
                }

                var heldGo = foodToEat.gameObject;
                Brain.ExecutePlayerAction(NpcActionKind.Drop, foodToEat.StableId);
                Destroy(heldGo);
                if (Brain.Actor != null) Brain.Actor.Gesture();
                if (Brain.Survival != null) Brain.Survival.RememberCurrentWorld();
                UpdatePickupTargetLabel();
                return;
            }

            // 2. Check waist moonbag
            var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();
            if (moonbag != null && moonbag.CanRetrieve)
            {
                moonbag.RetrieveFruit();
                if (food != null && food.Model != null)
                {
                    var s = food.Model.State;
                    s.body.stomach = Mathf.Min(10000, s.body.stomach + 2000);
                    s.hydration = Mathf.Min(10000, s.hydration + 600);
                    s.satiety = Mathf.Min(10000, s.satiety + 1500);
                    Starfall.Food.FoodPhysiology.ApplyDriveReduction(s.body, 1500);
                    s.knowsMealBenefit = true;
                    s.carriedFruit = Mathf.Max(0, s.carriedFruit - 1);
                }
                if (Brain.Actor != null) Brain.Actor.Gesture();
                if (Brain.Survival != null) Brain.Survival.RememberCurrentWorld();
                UpdatePickupTargetLabel();
                return;
            }

            // 3. Fallback to carried fruit
            if (food != null && food.Model != null && food.Model.State.carriedFruit > 0)
            {
                var s = food.Model.State;
                var receipt = food.Model.Execute(s.world, s.generation, s.lastRequest + 1, Starfall.Food.FoodAction.Eat, "inventory", food);
                if (receipt.success)
                {
                    s.knowsMealBenefit = true;
                    if (Brain.Actor != null) Brain.Actor.Gesture();
                    if (Brain.Survival != null) Brain.Survival.RememberCurrentWorld();
                }
                UpdatePickupTargetLabel();
            }
        }

        private bool IsEdible(NpcInteractable item)
        {
            if (item == null) return false;
            var phys = item.GetComponent<PhysicalItem>();
            if (phys != null)
            {
                return phys.itemTypeId == "food-protein-crab" ||
                       phys.itemTypeId == "food-cooked-crab" ||
                       phys.itemTypeId == "food-river-fish" ||
                       phys.itemTypeId == "food-river-carp" ||
                       phys.itemTypeId == "food-cooked-fish" ||
                       phys.itemTypeId == "food-wolf-meat" ||
                       phys.itemTypeId == "food-cooked-meat" ||
                       phys.itemTypeId == "food-sourfig-berry" ||
                       phys.itemTypeId == "fruit";
            }
            return item.StableId.Contains("berry") || item.StableId.Contains("fish") || item.StableId.Contains("carp") || item.StableId.Contains("crab") || item.StableId.Contains("meat");
        }
        public void OpenMenu()
        {
            if (MenuOpen) return;
            if (ContainerPanel != null && ContainerPanel.IsOpen) ContainerPanel.Close();
            if (TargetPromptText != null) TargetPromptText.gameObject.SetActive(false);
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
            RefreshPickupTarget();
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

            if (TargetPromptText == null && Hud != null && Hud.Canvas != null)
            {
                var promptObj = new GameObject("Pickup prompt label", typeof(RectTransform), typeof(Text));
                var pRect = promptObj.GetComponent<RectTransform>();
                pRect.SetParent(Hud.Canvas.transform, false);
                pRect.anchorMin = new Vector2(0.5f, 0f);
                pRect.anchorMax = new Vector2(0.5f, 0f);
                pRect.pivot = new Vector2(0.5f, 0f);
                pRect.anchoredPosition = new Vector2(0f, 30f);
                pRect.sizeDelta = new Vector2(600f, 40f);
                TargetPromptText = promptObj.GetComponent<Text>();
                TargetPromptText.font = font;
                TargetPromptText.fontSize = 18;
                TargetPromptText.color = new Color(0.6f, 0.93f, 0.93f, 1f);
                TargetPromptText.alignment = TextAnchor.MiddleCenter;
                TargetPromptText.supportRichText = false;
                promptObj.SetActive(false);
            }
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
                    "\nC: container panel / roast at hearth. E: gather/drink/roast. H: eat. G: drop. B: moonbag. X: toggle club. T: cycle target. L: show/hide decisions. M: map/beliefs. Escape: back/resume.";
                Option(Brain.Possessed ? "Release NPC and resume autonomy" : "Possess this NPC", TogglePossession);
                if (Brain.OptionalPlanner != null || Brain.Survival != null) Option("Local thoughts", () => ShowPage("Thoughts"));
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
                var surv = Brain.Survival;
                bool isEnabled = (planner != null && planner.EnabledByUser) || (surv != null && surv.LocalModelEnabled);
                string status = surv != null
                    ? (surv.LocalModelEnabled ? "Survival local thoughts / model: ON" : "Survival local thoughts: OFF (deterministic rules active)")
                    : (planner != null ? planner.Status : "Local thoughts off");
                PageTitle.text = "PAUSED / Local thoughts";
                PageBody.text = status + "\nOptional local goals, plans and fictional dialogue. Grounded rules validate every action." +
                    "\nA configured local model endpoint is used when enabled; otherwise deterministic grounded survival rules govern the inhabitant." +
                    "\nL shows the decision log and thoughts modal. Reflection summarizes vital drives, memory, and terrain.";
                Option(isEnabled ? "Turn local thoughts off" : "Turn local thoughts on", () =>
                {
                    bool nextState = !isEnabled;
                    if (planner != null) planner.SetEnabled(nextState);
                    if (surv != null) surv.LocalModelEnabled = nextState;
                    ShowPage("Thoughts");
                });
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
