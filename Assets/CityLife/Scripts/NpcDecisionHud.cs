using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CityLife.World
{
    // Unified Starfall Decision & Vitals HUD.
    // Replaces monolithic screen-blocking debug boxes with a sleek Top Vitals Header,
    // dynamic gauge bars (HP, Stamina, Food, Protein, H2O), persistent build identity,
    // a 4-tab Collapsible Survival Inspector Drawer ([L]), and a docked bottom hotkey strip.
    [DefaultExecutionOrder(-100)]
    public sealed class NpcDecisionHud : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public Camera View;
        public Text Summary, Perceptions, History;
        public Canvas Canvas;
        public bool Detailed
        {
            get => _detailed;
            set
            {
                _detailed = value;
                if (drawerPanel != null) drawerPanel.SetActive(_detailed);
            }
        }
        private bool _detailed;

        public void ToggleDetailed()
        {
            Detailed = !Detailed;
        }
        public NpcPlayerControls Controls;
        public string LivingMemoryText;
        public int DrawerTab = 0; // 0: Diary, 1: Mind & Plan, 2: Senses, 3: Milestones

        public Canvas DecisionCanvas => decisionCanvas;
        public GameObject PanelGroup => panelGroup;
        public bool IsDecisionActive => panelGroup != null && panelGroup.activeSelf;
        public void StepLateUpdate() => LateUpdate();

        private GameObject panelGroup;
        private Canvas decisionCanvas;
        private RectTransform backgroundRect, footerRect;
        private Text footer, thoughts;
        private GameObject thoughtsBackground;

        // Top Vitals Header UI
        private GameObject headerPanel;
        private Text buildBadgeText;
        private Text statsInfoText;
        private RectTransform healthBarFill, staminaBarFill, fullnessBarFill, proteinBarFill, hydrationBarFill;
        private Text healthLabel, staminaLabel, fullnessLabel, proteinLabel, hydrationLabel;

        // Collapsible Drawer UI
        private GameObject drawerPanel;
        private GameObject[] tabContainers = new GameObject[4];
        private Image[] tabBgs = new Image[4];
        private Text[] tabTexts = new Text[4];
        private Text diaryText;
        private Text milestonesText;

        private static Material _alwaysOnTopMaterial;
        public static Material AlwaysOnTopMaterial
        {
            get
            {
                if (_alwaysOnTopMaterial == null)
                {
                    var shader = Shader.Find("UI/Default");
                    if (shader != null)
                    {
                        _alwaysOnTopMaterial = new Material(shader);
                        _alwaysOnTopMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                    }
                }
                return _alwaysOnTopMaterial;
            }
        }

        public const string BuildVersion = "STARFALL v0.0.11 · round-297-livingworld";

        private void Awake()
        {
            var root = new GameObject("NPC decision panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.layer = 5;
            Canvas = root.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.worldCamera = View != null ? View : Camera.main;
            float near = Canvas.worldCamera != null ? Canvas.worldCamera.nearClipPlane : 0.15f;
            Canvas.planeDistance = Mathf.Max(0.18f, near + 0.02f);
            Canvas.sortingOrder = 10;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = .5f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            panelGroup = new GameObject("Decision panel content", typeof(RectTransform), typeof(Canvas));
            decisionCanvas = panelGroup.GetComponent<Canvas>();
            var groupRt = panelGroup.GetComponent<RectTransform>();
            groupRt.SetParent(root.transform, false);
            groupRt.anchorMin = Vector2.zero;
            groupRt.anchorMax = Vector2.one;
            groupRt.offsetMin = Vector2.zero;
            groupRt.offsetMax = Vector2.zero;

            RectTransform MakeRt(GameObject o, Transform parent, float x, float y, float w, float h)
            {
                var rect = o.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(x, -y);
                rect.sizeDelta = new Vector2(w, h);
                return rect;
            }

            Text MakeLabel(GameObject parent, string name, float x, float y, float w, float h, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
            {
                var o = new GameObject(name, typeof(RectTransform), typeof(Text));
                MakeRt(o, parent.transform, x, y, w, h);
                var t = o.GetComponent<Text>();
                t.font = font;
                t.fontSize = size;
                t.fontStyle = style;
                t.color = color;
                t.alignment = align;
                t.supportRichText = true;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                t.raycastTarget = false;
                if (AlwaysOnTopMaterial != null) t.material = AlwaysOnTopMaterial;
                return t;
            }

            // ==========================================
            // 1. Sleek Top Vitals Header (1560 x 78)
            // ==========================================
            headerPanel = new GameObject("Top vitals header", typeof(RectTransform), typeof(Image));
            MakeRt(headerPanel, panelGroup.transform, 20, 10, 1560, 78);
            var headerImg = headerPanel.GetComponent<Image>();
            headerImg.color = new Color(0.02f, 0.045f, 0.07f, 0.90f);
            if (AlwaysOnTopMaterial != null) headerImg.material = AlwaysOnTopMaterial;

            // Persistent Build Identity Badge (top right)
            buildBadgeText = MakeLabel(headerPanel, "Build badge", 1210, 8, 335, 24, 15, new Color(0.38f, 0.88f, 1.0f), FontStyle.Bold, TextAnchor.MiddleRight);
            buildBadgeText.text = BuildVersion;

            // Decision & Mode Status Pill (top left)
            Summary = MakeLabel(headerPanel, "Current decision", 15, 6, 1180, 32, 16, Color.white);
            Summary.text = "STARFALL / Autonomous NPC  |  Tick 0";

            // 5 Visual Dynamic Gauge Bars
            void CreateGauge(string name, float x, Color fillColor, out RectTransform fillRt, out Text valLabel)
            {
                var bg = new GameObject(name + " bg", typeof(RectTransform), typeof(Image));
                MakeRt(bg, headerPanel.transform, x, 44, 165, 22);
                var bgImg = bg.GetComponent<Image>();
                bgImg.color = new Color(0.06f, 0.12f, 0.18f, 0.95f);
                if (AlwaysOnTopMaterial != null) bgImg.material = AlwaysOnTopMaterial;

                var fill = new GameObject(name + " fill", typeof(RectTransform), typeof(Image));
                fillRt = MakeRt(fill, bg.transform, 0, 0, 165, 22);
                var fillImg = fill.GetComponent<Image>();
                fillImg.color = fillColor;
                if (AlwaysOnTopMaterial != null) fillImg.material = AlwaysOnTopMaterial;

                valLabel = MakeLabel(bg, name + " label", 0, 0, 165, 22, 13, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            }

            CreateGauge("HP", 15, new Color(0.85f, 0.22f, 0.22f, 0.92f), out healthBarFill, out healthLabel);
            CreateGauge("STA", 190, new Color(0.92f, 0.72f, 0.18f, 0.92f), out staminaBarFill, out staminaLabel);
            CreateGauge("FOOD", 365, new Color(0.92f, 0.52f, 0.18f, 0.92f), out fullnessBarFill, out fullnessLabel);
            CreateGauge("PROT", 540, new Color(0.82f, 0.35f, 0.85f, 0.92f), out proteinBarFill, out proteinLabel);
            CreateGauge("H2O", 715, new Color(0.22f, 0.72f, 0.95f, 0.92f), out hydrationBarFill, out hydrationLabel);

            statsInfoText = MakeLabel(headerPanel, "Stats info", 890, 44, 655, 22, 14, new Color(0.72f, 0.84f, 0.92f), FontStyle.Normal, TextAnchor.MiddleLeft);

            // ==========================================
            // 2. Collapsible Survival Inspector Drawer (520 x 480)
            // ==========================================
            drawerPanel = new GameObject("Survival inspector drawer", typeof(RectTransform), typeof(Image));
            backgroundRect = MakeRt(drawerPanel, panelGroup.transform, 20, 96, 520, 480);
            var drawerImg = drawerPanel.GetComponent<Image>();
            drawerImg.color = new Color(0.02f, 0.045f, 0.07f, 0.95f);
            if (AlwaysOnTopMaterial != null) drawerImg.material = AlwaysOnTopMaterial;

            // Tab Bar across top of drawer
            var tabTitles = new[] { "1: Diary", "2: Mind", "3: Senses", "4: Quests" };
            float tabW = 122f;
            for (int i = 0; i < 4; i++)
            {
                int tabIdx = i;
                var tabBtn = new GameObject("Tab " + i, typeof(RectTransform), typeof(Image), typeof(Button));
                MakeRt(tabBtn, drawerPanel.transform, 10 + i * (tabW + 6), 10, tabW, 30);
                tabBgs[i] = tabBtn.GetComponent<Image>();
                tabBgs[i].color = (i == 0) ? new Color(0.18f, 0.42f, 0.58f, 0.95f) : new Color(0.06f, 0.12f, 0.18f, 0.8f);
                if (AlwaysOnTopMaterial != null) tabBgs[i].material = AlwaysOnTopMaterial;

                tabTexts[i] = MakeLabel(tabBtn, "Text", 0, 0, tabW, 30, 14, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
                tabTexts[i].text = tabTitles[i];

                var btn = tabBtn.GetComponent<Button>();
                btn.onClick.AddListener(() => SetDrawerTab(tabIdx));

                // Container for each tab
                var container = new GameObject("Tab " + i + " Container", typeof(RectTransform));
                MakeRt(container, drawerPanel.transform, 14, 48, 492, 420);
                tabContainers[i] = container;
                container.SetActive(i == 0);
            }

            // Tab 0: Survival Diary Chronicle
            diaryText = MakeLabel(tabContainers[0], "Diary text", 0, 0, 492, 420, 15, new Color(0.88f, 0.94f, 0.98f));

            // Tab 1: Mind & Plan (Thoughts + Actions Log)
            thoughts = MakeLabel(tabContainers[1], "Optional local thoughts", 0, 0, 492, 220, 15, new Color(0.55f, 0.92f, 0.98f));
            History = MakeLabel(tabContainers[1], "Action log", 0, 230, 492, 185, 14, new Color(0.95f, 0.88f, 0.72f));

            // Tab 2: Senses / Perception
            Perceptions = MakeLabel(tabContainers[2], "Perceived objects", 0, 0, 492, 420, 15, new Color(0.78f, 0.88f, 0.95f));

            // Tab 3: Milestones & Gamification
            milestonesText = MakeLabel(tabContainers[3], "Milestones text", 0, 0, 492, 420, 14, new Color(1.0f, 0.90f, 0.65f));

            // Legacy thoughtsBackground compatibility hook
            thoughtsBackground = drawerPanel;

            // ==========================================
            // 3. Docked Bottom Hotkey Pill Strip
            // ==========================================
            var footerPanel = new GameObject("Bottom hotkey strip", typeof(RectTransform), typeof(Image));
            footerRect = MakeRt(footerPanel, panelGroup.transform, 250, 856, 1100, 34);
            var footerImg = footerPanel.GetComponent<Image>();
            footerImg.color = new Color(0.02f, 0.045f, 0.07f, 0.88f);
            if (AlwaysOnTopMaterial != null) footerImg.material = AlwaysOnTopMaterial;

            footer = MakeLabel(footerPanel, "Controls footer", 10, 0, 1080, 34, 14, new Color(0.72f, 0.82f, 0.90f), FontStyle.Normal, TextAnchor.MiddleCenter);
            footer.text = "[Tab] Possess  ·  [E] Gather/Drink/Roast  ·  [H] Eat  ·  [G] Drop  ·  [X] Club  ·  [B] Moonbag  ·  [M] Map  ·  [L] Inspector Drawer  ·  [Shift] Sprint";

            drawerPanel.SetActive(Detailed);
        }

        public void SetDrawerTab(int index)
        {
            DrawerTab = Mathf.Clamp(index, 0, 3);
            Color activeBg = new Color(0.18f, 0.42f, 0.58f, 0.95f);
            Color inactiveBg = new Color(0.06f, 0.12f, 0.18f, 0.8f);

            for (int i = 0; i < 4; i++)
            {
                if (tabContainers[i] != null) tabContainers[i].SetActive(i == DrawerTab);
                if (tabBgs[i] != null) tabBgs[i].color = (i == DrawerTab) ? activeBg : inactiveBg;
                if (tabTexts[i] != null) tabTexts[i].color = (i == DrawerTab) ? Color.white : new Color(0.65f, 0.75f, 0.82f);
            }
        }

        private void Update()
        {
            var key = Keyboard.current;
            if (key == null) return;

            if (Detailed)
            {
                if (key.digit1Key.wasPressedThisFrame || key.numpad1Key.wasPressedThisFrame) SetDrawerTab(0);
                else if (key.digit2Key.wasPressedThisFrame || key.numpad2Key.wasPressedThisFrame) SetDrawerTab(1);
                else if (key.digit3Key.wasPressedThisFrame || key.numpad3Key.wasPressedThisFrame) SetDrawerTab(2);
                else if (key.digit4Key.wasPressedThisFrame || key.numpad4Key.wasPressedThisFrame) SetDrawerTab(3);
            }
        }

        private void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (Canvas != null)
            {
                if (Canvas.worldCamera == null && (View != null || Camera.main != null))
                    Canvas.worldCamera = View != null ? View : Camera.main;
                if (Canvas.worldCamera != null)
                {
                    float near = Canvas.worldCamera.nearClipPlane;
                    float desired = Mathf.Max(0.18f, near + 0.02f);
                    if (Mathf.Abs(Canvas.planeDistance - desired) > 0.001f)
                        Canvas.planeDistance = desired;
                }
            }

            bool modal = Brain != null && Brain.MenuPaused;
            if (panelGroup != null && panelGroup.activeSelf == modal)
            {
                panelGroup.SetActive(!modal);
                if (decisionCanvas != null) decisionCanvas.enabled = !modal;
            }
            if (modal) return;
            if (!Brain.Ready || Summary == null) return;

            if (drawerPanel != null && drawerPanel.activeSelf != Detailed)
            {
                drawerPanel.SetActive(Detailed);
            }

            string mode = Brain.MenuPaused ? "PAUSED / " : "";
            mode += Brain.Possessed ? "Possession" : Controls != null && Controls.FreeSpectator ? "Spectator" : "Autonomous NPC";

            string currentGoal;
            if (Brain.GoalId.Length > 0)
            {
                currentGoal = Brain.GoalId;
            }
            else if (Brain.Survival != null && Brain.Survival.Enabled)
            {
                currentGoal = !string.IsNullOrEmpty(Brain.Survival.routePurpose)
                    ? Brain.Survival.routePurpose
                    : (!string.IsNullOrEmpty(Brain.Survival.LastChoice) ? Brain.Survival.LastChoice : "survive");
            }
            else if (Brain.Foraging != null && !string.IsNullOrEmpty(Brain.Foraging.TargetResourceId))
            {
                currentGoal = Brain.Foraging.TargetResourceId;
            }
            else
            {
                currentGoal = "observe / wait";
            }

            string FormatCargo()
            {
                if (Brain.Actions == null) return "none";
                var r = Brain.Actions.HeldRight;
                var l = Brain.Actions.HeldLeft;
                if (r != null && l != null) return $"R:{r.StableId} | L:{l.StableId}";
                if (r != null) return $"R:{r.StableId}";
                if (l != null) return $"L:{l.StableId}";
                return "none";
            }
            string cargo = FormatCargo();

            var carry = Brain.GetComponentInChildren<HunterClubCarry>();
            string weaponStatus = carry != null ? (carry.Stowed ? "Back" : "In Hand") : "None";

            var foodRuntime = Brain.Survival != null ? Brain.Survival.Food : null;
            if (foodRuntime == null) foodRuntime = FindAnyObjectByType<Starfall.Food.IntegratedFoodRuntime>();

            if (foodRuntime != null && foodRuntime.Model != null)
            {
                var food = foodRuntime.Model.State;
                int visitedCount = food.observedPlaces != null ? food.observedPlaces.Count : 0;
                int exploredCount = food.exploredCells != null ? food.exploredCells.Count : 0;
                int healthPct = Mathf.Clamp(food.body.health / 100, 0, 100);
                int fullnessPct = Mathf.Clamp(food.satiety / 100, 0, 100);
                int hydrationPct = Mathf.Clamp(food.hydration / 100, 0, 100);
                int staminaPct = Brain.Actor != null ? Mathf.Clamp(Mathf.RoundToInt(Brain.Actor.Stamina), 0, 100) : 100;
                int proteinPct = food.body != null ? Mathf.Clamp(food.body.protein / 100, 0, 100) : 100;

                var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();
                int mbCount = moonbag != null ? moonbag.StoredCount : 0;

                string airAlert = "";
                if (food.body.submerged)
                {
                    int airSec = Mathf.Max(0, 15 - food.body.submergedSeconds);
                    airAlert = airSec > 0 ? $"  |  AIR: {airSec}s [SUBMERGED]" : "  |  AIR: 0s [DROWNING!]";
                }

                string wolfAlert = "";
                if (Brain.Perception != null && Brain.Perception.Current != null)
                {
                    var nearWolf = Brain.Perception.Current.FirstOrDefault(x => x != null && x.id.Contains("wolf") && x.distanceMillimetres <= 18000);
                    if (nearWolf != null)
                    {
                        wolfAlert = $"  |  <color=#FF4444><b>[WOLF {(nearWolf.distanceMillimetres / 1000f):F1}m - Press X]</b></color>";
                    }
                }

                Summary.text = $"<b>{mode}</b>  |  Tick {Brain.Tick}  |  Goal: <color=#FFE680>{currentGoal}</color>  |  Cargo: {cargo}  |  Club: {weaponStatus}{airAlert}{wolfAlert}";

                // Update Vitals Gauge Bar Fills
                void UpdateBar(RectTransform fillRt, Text valTxt, string label, int pct)
                {
                    if (fillRt != null) fillRt.sizeDelta = new Vector2(165f * (pct / 100f), 22f);
                    if (valTxt != null) valTxt.text = $"{label}: {pct}%";
                }

                UpdateBar(healthBarFill, healthLabel, "HP", healthPct);
                UpdateBar(staminaBarFill, staminaLabel, "STA", staminaPct);
                UpdateBar(fullnessBarFill, fullnessLabel, "FOOD", fullnessPct);
                UpdateBar(proteinBarFill, proteinLabel, "PROT", proteinPct);
                UpdateBar(hydrationBarFill, hydrationLabel, "H2O", hydrationPct);

                if (statsInfoText != null)
                {
                    statsInfoText.text = $"<b>Water:</b> {food.freshwaterMl}ml  ·  <b>Moonbag:</b> {mbCount}/2  ·  <b>Cells:</b> {exploredCount}  ·  <b>Places:</b> {visitedCount}";
                }
            }
            else
            {
                Summary.text = $"{mode}  |  Tick {Brain.Tick}  |  Goal: {currentGoal}  |  Cargo: {cargo}";
            }

            // Tab 0: Survival Diary
            var diary = Brain.GetComponent<StarfallSurvivalDiary>() ?? FindFirstObjectByType<StarfallSurvivalDiary>();
            if (diaryText != null && diary != null)
            {
                diaryText.text = "<b>INHABITANT'S SURVIVAL CHRONICLE:</b>\n\n" + diary.GetRecentDiarySummary(4);
            }

            // Tab 1: Mind & Plan (Thoughts + Actions Log)
            if (thoughts != null)
            {
                if (Brain.Survival != null && Brain.Survival.Enabled)
                {
                    string survStatus = Brain.Survival.Status;
                    string survPlan = Brain.Survival.Plan;
                    string survDialogue = Brain.Survival.Dialogue;
                    string survReflection = Brain.Survival.Reflection;
                    if (string.IsNullOrEmpty(survPlan))
                    {
                        Brain.Survival.SynthesizeGroundedNarrative(Brain.Survival.LastChoice);
                        survPlan = Brain.Survival.Plan;
                        survDialogue = Brain.Survival.Dialogue;
                        survReflection = Brain.Survival.Reflection;
                    }
                    string providerInfo = Brain.Survival.LastChoiceByModel ? "Local model active" : "Grounded survival mind active";
                    thoughts.text = "<b>SURVIVAL MIND / LOCAL THOUGHTS</b>\n" + survStatus + " (" + providerInfo + ")\n<b>Advisory plan:</b> " + survPlan +
                        "\n\n<b>Inner monologue:</b> " + survDialogue + "\n\n<b>Generated reflection:</b> " + survReflection +
                        "\n\nActions use deterministic checks & physics.";
                }
                else if (Brain.OptionalPlanner != null)
                {
                    var planner = Brain.OptionalPlanner;
                    thoughts.text = "<b>OPTIONAL LOCAL THOUGHTS</b>\n" + planner.Status + "\n<b>Advisory plan:</b> " + planner.Plan +
                        "\n\n<b>Fictional dialogue:</b> " + planner.Dialogue + "\n\n<b>Generated reflection:</b> " + planner.Reflection;
                }
                if (!string.IsNullOrEmpty(LivingMemoryText)) thoughts.text = LivingMemoryText;
            }

            if (History != null)
            {
                var history = new StringBuilder("<b>RECENT DECISIONS / ACTIONS:</b>\n");
                foreach (var row in Brain.Log.Entries.Where(x => x.phase != "perception").TakeLast(3))
                {
                    history.Append('#').Append(row.sequence).Append(" t").Append(row.tick).Append(' ').Append(row.phase.ToUpperInvariant())
                        .Append("  ").Append(row.goal).Append('\n').Append(row.action).Append(" → ").Append(row.result).Append('\n');
                    if (row.fallback.Length > 0) history.Append("Fallback: ").Append(row.fallback).Append('\n');
                }
                History.text = history.ToString();
            }

            // Tab 2: Senses / Perception
            if (Perceptions != null)
            {
                var perceived = new StringBuilder("<b>PERCEPTION / DIRECTIONAL FOV + LINE OF SIGHT:</b>\n");
                foreach (var x in Brain.Perception.Current)
                {
                    string zoneStr = x.visionZone == VisionZone.Focal ? "FOCAL" :
                                     x.visionZone == VisionZone.Peripheral ? "PERIPHERAL" :
                                     x.visionZone == VisionZone.DistantLandmark ? "LANDMARK" : "PROXIMITY";
                    perceived.Append(x.id).Append("  ").Append((x.distanceMillimetres / 1000f).ToString("F1")).Append("m  [")
                        .Append(zoneStr).Append("]  ")
                        .Append(!x.permission ? "DENIED" : !x.available ? "UNAVAILABLE" : "ELIGIBLE").Append('\n');
                }
                perceived.Append("Occluded contacts: ").Append(Brain.Perception.OccludedCount);
                Perceptions.text = perceived.ToString();
            }

            // Tab 3: Milestones
            if (milestonesText != null && diary != null)
            {
                milestonesText.text = diary.GetMilestonesSummary();
            }

            UnityEngine.Canvas.ForceUpdateCanvases();
        }
    }
}
