using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Starfall.Food;

namespace CityLife.World
{
    // Read-only player map and place history HUD.
    // Configured at runtime with actual Screen dimensions and safe bounding boxes to avoid
    // overlapping the existing top-right controls (~280x80 in small window, 570x154 at reference),
    // the top-left inhabitant decision panel, and the bottom-right weather panel.
    // Uses ConstantPixelSize canvas scaling with minimum 14px font to ensure full legibility
    // at 800x450, 1280x720, and 1600x900 without halving text to 6px.
    // Features a compact unobtrusive map summary during play, and 'M' toggles the expanded map
    // with deliberate selectable/tabbed layout (Grid, Beliefs, History) that avoids covering
    // the inhabitant permanently or stacking HUD menus.
    [DefaultExecutionOrder(-40)]
    public sealed class StarfallMapHud : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public StarfallSurvivalAutonomy Survival;
        public IntegratedFoodRuntime Food;
        public Camera View;
        public bool Visible = true;

        // Expanded state: toggled via 'M' key or clicking the compact summary badge.
        // When false, renders compact unobtrusive summary (does not cover inhabitant or stack menus).
        // When true, renders full deliberate map layout with selectable tabs.
        public bool Expanded { get; set; } = false;

        public int ActiveTab => activeTab;

        [NonSerialized] public Keyboard TestKeyboard;

        public StarfallMapViewModel ViewModel { get; private set; } = new StarfallMapViewModel();

        public Text StatusLabel => statusLabel;
        public Text ProvenanceLabel => provenanceLabel;
        public Text GridLabel => gridLabel;
        public Text BeliefsLabel => beliefsLabel;
        public Text EventsLabel => eventsLabel;
        public Text LegendLabel => legendLabel;
        public Text FooterLabel => footerLabel;
        public Text CompactStatusText => compactStatusText;
        public Text CompactTitleText => compactTitleText;
        public Button CollapseButton => collapseButton;

        public Canvas Canvas => canvas;
        public GameObject PanelObject => panelObject;
        public bool IsPanelActive => panelObject != null && panelObject.activeSelf;
        public bool IsCanvasEnabled => canvas != null && canvas.enabled;
        public void StepUpdate() => Update();
        public void StepLateUpdate() => LateUpdate();

        private Canvas canvas;
        private CanvasScaler canvasScaler;
        private GameObject panelObject;
        private RectTransform panelRect;

        // Compact summary group (unobtrusive during exploration)
        private GameObject compactGroup;
        private Text compactTitleText, compactStatusText;

        // Expanded map group (deliberate layout with tabs)
        private GameObject expandedGroup;
        private Text titleLabel, statusLabel, provenanceLabel;
        private Button collapseButton;
        private RectTransform collapseBtnRt;
        private GameObject tabBar;
        private RectTransform tab0Rt, tab1Rt, tab2Rt;
        private GameObject tab0Container, tab1Container, tab2Container;
        private Image tab0Bg, tab1Bg, tab2Bg;
        private Text tab0Text, tab1Text, tab2Text;
        private Text gridLabel, legendLabel, beliefsLabel, eventsLabel, footerLabel;

        // Visual Map components (God of War / Ghost Recon Wildlands style)
        private Texture2D topoTexture;
        private Texture2D fogTexture;
        private Color32[] fogPixels;
        private RawImage topoRawImage;
        private RawImage fogRawImage;
        private GameObject visualMapContainer;
        private RectTransform visualMapRt;
        private RectTransform playerChevronRt;
        private Text gpsLabel;
        private readonly System.Collections.Generic.List<(Vector3 worldPos, string label, RectTransform rt, Text txt)> poiBadges = new System.Collections.Generic.List<(Vector3, string, RectTransform, Text)>();
        private bool showVisualMap = true;
        private int lastRevealedCellCount = -1;

        private int activeTab = 0; // 0: Tactical Map / Grid, 1: Beliefs, 2: History
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;

        private Func<Vector3> actorPositionProvider;
        private bool isInitialized;
        private bool dirty = true;
        private float lastSampleTime = -1f;
        private bool lastDisplayedValid;
        private string lastDependencyError;

        public static bool ValidateDependencySet(NpcAutonomy brain, IntegratedFoodRuntime food, StarfallSurvivalAutonomy survival, out string error)
        {
            if (food == null || food.Model == null || food.Model.State == null)
            {
                error = "Food runtime or state missing";
                return false;
            }
            if (brain == null)
            {
                error = "Brain missing";
                return false;
            }
            if (food.Brain != brain)
            {
                error = "Food runtime Brain reference does not match brain";
                return false;
            }
            if (brain.gameObject.scene != food.gameObject.scene)
            {
                error = "Brain and food runtime reside in different scenes";
                return false;
            }
            if (survival != null)
            {
                if (survival.Brain != brain || survival.Food != food)
                {
                    error = "Survival autonomy not bound to matching brain and food set";
                    return false;
                }
                if (survival.gameObject.scene != brain.gameObject.scene)
                {
                    error = "Survival autonomy resides in a different scene";
                    return false;
                }
            }
            if (string.IsNullOrEmpty(brain.InstanceWorldId) || food.Model.State.world != brain.InstanceWorldId)
            {
                error = $"World mismatch: brain={brain.InstanceWorldId}, food={food.Model.State.world}";
                return false;
            }
            if (food.Model.State.actorId != NpcAutonomy.AgentId)
            {
                error = $"Actor mismatch: expected {NpcAutonomy.AgentId}, food state has {food.Model.State.actorId}";
                return false;
            }
            if (food.Model.State.generation != IntegratedFoodRuntime.Generation)
            {
                error = $"Generation mismatch: expected {IntegratedFoodRuntime.Generation}, food state has {food.Model.State.generation}";
                return false;
            }
            if (!FoodModel.Valid(food.Model.State, brain.InstanceWorldId, IntegratedFoodRuntime.Generation))
            {
                error = "Food state failed authoritative validation";
                return false;
            }
            error = null;
            return true;
        }

        // Calculates explicit safe bounding boxes that avoid overlapping:
        // 1. Top-right controls panel (154 screen pixels + 12px margin = 166px down from top)
        // 2. Bottom-right weather panel (Screen.height - 140)
        // 3. Top-left decision panel (width ~260px at 800x450, ~520px at 1600x900)
        public static void CalculateSafeBounds(int sw, int sh, bool expanded, out Vector2 anchoredPos, out Vector2 sizeDelta, out float topOffset, out float maxAvailableHeight)
        {
            if (sw <= 0) sw = 1600;
            if (sh <= 0) sh = 900;

            // Safe vertical clearance below top-right controls (154 screen pixels + 12px margin = 166px)
            // 175f provides safe clearance across all resolutions without mistaking screenshot scale for screen height.
            topOffset = 175f;

            // Safe vertical clearance above bottom-right weather (weather box starts at Screen.height - 140)
            float bottomWeatherOffset = 145f;

            float marginRight = 12f;

            // Horizontal bounds: leave ample room for left-side decision panel and center inhabitant
            float maxRightWidth;
            if (sw <= 900)
            {
                // 800x450: panelWidth = 360f -> left edge at 800 - 360 - 12 = 428f (decision panel ends at 260f)
                maxRightWidth = Mathf.Min(360f, sw - 280f);
            }
            else if (sw <= 1300)
            {
                // 1280x720: panelWidth = 440f -> left edge at 1280 - 440 - 12 = 828f (decision panel ends at 420f)
                maxRightWidth = Mathf.Min(440f, sw - 450f);
            }
            else
            {
                // 1600x900: panelWidth = 480f -> left edge at 1600 - 480 - 12 = 1108f (decision panel ends at 530f)
                maxRightWidth = Mathf.Min(480f, sw - 560f);
            }
            maxRightWidth = Mathf.Max(300f, maxRightWidth);

            maxAvailableHeight = Mathf.Max(40f, (sh - bottomWeatherOffset) - topOffset);
            anchoredPos = new Vector2(-marginRight, -topOffset);

            if (!expanded)
            {
                // Compact unobtrusive summary: 2 readable lines safely positioned below top controls
                sizeDelta = new Vector2(maxRightWidth, 68f);
            }
            else
            {
                // Expanded map: deliberate layout bounded to available vertical safe height
                float targetHeight;
                if (sh <= 500) targetHeight = Mathf.Min(maxAvailableHeight, 130f);
                else if (sh <= 750) targetHeight = Mathf.Min(maxAvailableHeight, 380f);
                else targetHeight = Mathf.Min(maxAvailableHeight, 520f);

                sizeDelta = new Vector2(maxRightWidth, Mathf.Max(100f, targetHeight));
            }
        }

        public void SetActiveTab(int index)
        {
            activeTab = Mathf.Clamp(index, 0, 2);
            UpdateResponsiveLayout();
        }

        public void Initialize(FoodModel foodModel, string world, string generation, string actorId, Func<Vector3> posProvider)
        {
            if (ViewModel == null) ViewModel = new StarfallMapViewModel();
            ViewModel.Bind(world, generation, actorId);
            actorPositionProvider = posProvider;
            isInitialized = true;
            dirty = true;
            BuildUi();
        }

        public void NotifyStateChanged()
        {
            dirty = true;
        }

        public void RenderDependencyMismatch(string setErr)
        {
            if (panelObject == null) BuildUi();
            if (titleLabel == null) return;

            string mismatchStatus = "[Map unavailable: Dependency set mismatch]";
            if (statusLabel != null) statusLabel.text = mismatchStatus;
            if (compactStatusText != null) compactStatusText.text = mismatchStatus;

            if (provenanceLabel != null) provenanceLabel.text = "Place Memory: Unavailable";

            if (gridLabel != null) gridLabel.text = "";
            if (beliefsLabel != null) beliefsLabel.text = "Map fails closed: " + (setErr ?? "dependencies mismatched");
            if (eventsLabel != null) eventsLabel.text = "";
        }

        private void Awake()
        {
            if (ViewModel == null) ViewModel = new StarfallMapViewModel();
            BuildUi();
        }

        private void EnsureMapTextures()
        {
            if (topoTexture == null)
            {
                topoTexture = StarfallVisualMapGenerator.GenerateTopographicalTexture();
            }
            if (fogTexture == null)
            {
                fogTexture = StarfallVisualMapGenerator.CreateFogOfWarTexture();
                fogPixels = fogTexture.GetPixels32();
                lastRevealedCellCount = -1;
            }
        }

        public static string GetRegionName(Vector3 pos)
        {
            if (pos.x < -100f && pos.z > 60f) return "Refuge Cavern & West Ridge";
            if (pos.x > 80f && pos.z < -40f) return "Freshwater Spring Oasis";
            if (Mathf.Abs(pos.x) < 50f && Mathf.Abs(pos.z) < 80f) return "Whispering River Shallows";
            if (pos.z > 120f) return "North River Meander & Cliffs";
            if (pos.z < -120f) return "South Canyon Basin";
            return "Sunlit Canyon Terrace";
        }

        private void OnDestroy()
        {
            if (topoTexture != null) { Destroy(topoTexture); topoTexture = null; }
            if (fogTexture != null) { Destroy(fogTexture); fogTexture = null; }
            if (canvas != null && canvas.gameObject != null && canvas.gameObject != gameObject)
            {
                if (Application.isPlaying) Destroy(canvas.gameObject);
                else DestroyImmediate(canvas.gameObject);
            }
        }

        private void Start()
        {
            if (Brain == null) Brain = GetComponentInParent<NpcAutonomy>() ?? GetComponentInChildren<NpcAutonomy>();
            if (Survival == null) Survival = GetComponentInParent<StarfallSurvivalAutonomy>() ?? GetComponentInChildren<StarfallSurvivalAutonomy>();
            if (Food == null) Food = GetComponentInParent<IntegratedFoodRuntime>() ?? GetComponentInChildren<IntegratedFoodRuntime>();
            if (View == null) View = GetComponent<Camera>() ?? Camera.main;

            if (ValidateDependencySet(Brain, Food, Survival, out _))
            {
                string world = Brain.InstanceWorldId;
                string gen = IntegratedFoodRuntime.Generation;
                string actor = NpcAutonomy.AgentId;
                Initialize(Food.Model, world, gen, actor, () => Brain != null ? Brain.transform.position : Food.Model.State.actorPosition);
            }
        }

        private void Update()
        {
            if (Brain != null && Brain.MenuPaused) return;

            var key = TestKeyboard ?? Keyboard.current;
            if (key == null) return;

            // 'M' toggles expanded map mode; when collapsed, unobtrusive compact summary remains
            if (key.mKey.wasPressedThisFrame)
            {
                Expanded = !Expanded;
                UpdateResponsiveLayout();
            }

            if (Expanded)
            {
                // Tab shortcuts for quick keyboard navigation
                if (key.digit1Key.wasPressedThisFrame || key.numpad1Key.wasPressedThisFrame) SetActiveTab(0);
                else if (key.digit2Key.wasPressedThisFrame || key.numpad2Key.wasPressedThisFrame) SetActiveTab(1);
                else if (key.digit3Key.wasPressedThisFrame || key.numpad3Key.wasPressedThisFrame) SetActiveTab(2);
                else if (key.hKey.wasPressedThisFrame) SetActiveTab(2); // 'H' for History
                else if (key.tKey.wasPressedThisFrame) SetActiveTab((activeTab + 1) % 3); // 'T' to cycle tabs
                else if (key.gKey.wasPressedThisFrame && activeTab == 0)
                {
                    showVisualMap = !showVisualMap;
                    UpdateResponsiveLayout();
                }
            }
        }

        private void LateUpdate()
        {
            if (Brain != null && Brain.MenuPaused)
            {
                if (panelObject != null && panelObject.activeSelf) panelObject.SetActive(false);
                return;
            }

            if (!Visible)
            {
                if (panelObject != null && panelObject.activeSelf) panelObject.SetActive(false);
                return;
            }

            if (panelObject != null && !panelObject.activeSelf) panelObject.SetActive(true);

            // Responsive layout: update safe bounding boxes on screen resolution or window size changes
            int currentSw = Screen.width;
            int currentSh = Screen.height;
            if (currentSw != lastScreenWidth || currentSh != lastScreenHeight)
            {
                lastScreenWidth = currentSw;
                lastScreenHeight = currentSh;
                UpdateResponsiveLayout();
            }

            // Bounded cadence: sample actor position at most ~12 times per second unless explicitly dirty
            float now = Time.unscaledTime;
            if (!dirty && now - lastSampleTime < 0.08f) return;
            lastSampleTime = now;

            if (!ValidateDependencySet(Brain, Food, Survival, out string setErr))
            {
                dirty = false;
                if (lastDisplayedValid || lastDependencyError != setErr)
                {
                    lastDisplayedValid = false;
                    lastDependencyError = setErr;
                    if (ViewModel != null && ViewModel.IsValid)
                    {
                        ViewModel.Invalidate();
                    }
                    RenderDependencyMismatch(setErr);
                }
                return;
            }
            lastDependencyError = null;

            if (!isInitialized)
            {
                string world = Brain.InstanceWorldId;
                string gen = IntegratedFoodRuntime.Generation;
                string actor = NpcAutonomy.AgentId;
                Initialize(Food.Model, world, gen, actor, () => Brain != null ? Brain.transform.position : Food.Model.State.actorPosition);
            }

            Vector3 pos = actorPositionProvider != null ? actorPositionProvider() : Brain.transform.position;
            bool updateOk = ViewModel.Update(Food.Model.State, pos, forceRevalidate: dirty);
            dirty = false;

            if (ViewModel.DisplayChanged || (!updateOk && lastDisplayedValid))
            {
                lastDisplayedValid = updateOk;
                RefreshUi();
            }

            // Update dynamic Fog of War reveal on cell changes
            if (fogPixels != null && fogTexture != null && ViewModel != null && ViewModel.IsValid)
            {
                if (ViewModel.ExploredCellCount != lastRevealedCellCount)
                {
                    var cells = ViewModel.GetExploredCells();
                    if (cells != null)
                    {
                        for (int i = 0; i < cells.Length; i++)
                        {
                            StarfallVisualMapGenerator.RevealCell(fogPixels, cells[i].X, cells[i].Z, 22f);
                        }
                        fogTexture.SetPixels32(fogPixels);
                        fogTexture.Apply();
                        lastRevealedCellCount = cells.Length;
                    }
                }
            }

            // Update player marker & POIs on visual map
            if (visualMapRt != null && visualMapContainer != null && visualMapContainer.activeSelf)
            {
                Vector3 actorPos = actorPositionProvider != null ? actorPositionProvider() : (Brain != null ? Brain.transform.position : Vector3.zero);
                Vector2 uv = StarfallVisualMapGenerator.WorldToMapUV(actorPos);
                Vector2 mapSize = visualMapRt.sizeDelta;

                if (playerChevronRt != null)
                {
                    playerChevronRt.anchoredPosition = new Vector2(uv.x * mapSize.x, (uv.y - 1f) * mapSize.y);
                    float yaw = 0f;
                    if (Brain != null) yaw = Brain.transform.eulerAngles.y;
                    else if (View != null) yaw = View.transform.eulerAngles.y;
                    playerChevronRt.localEulerAngles = new Vector3(0, 0, -yaw);

                    if (gpsLabel != null)
                    {
                        string region = GetRegionName(actorPos);
                        gpsLabel.text = $"GRID: [{actorPos.x:+0.0;-0.0;0.0}, {actorPos.z:+0.0;-0.0;0.0}] · ALT: {actorPos.y:0.0}m · HDG: {Mathf.Repeat(yaw, 360f):0}°\nREGION: {region}  ·  FOG: {(ViewModel.ExploredCellCount > 0 ? "Revealing" : "Shrouded")}";
                    }
                }

                // Update POI pins
                for (int i = 0; i < poiBadges.Count; i++)
                {
                    var (wpos, name, pRt, pTxt) = poiBadges[i];
                    Vector2 poiUv = StarfallVisualMapGenerator.WorldToMapUV(wpos);
                    pRt.anchoredPosition = new Vector2(poiUv.x * mapSize.x, (poiUv.y - 1f) * mapSize.y);
                }
            }
        }

        private static string FormatCell(object cell)
        {
            if (cell == null) return "(--)";
            if (cell is Vector2Int v2) return $"({v2.x},{v2.y})";
            if (cell is Vector3Int v3) return $"({v3.x},{v3.z})";
            var type = cell.GetType();
            var xMember = (System.Reflection.MemberInfo)type.GetField("x") ?? type.GetProperty("x") ?? (System.Reflection.MemberInfo)type.GetField("Item1");
            var zMember = (System.Reflection.MemberInfo)type.GetField("z") ?? type.GetProperty("z") ?? (System.Reflection.MemberInfo)type.GetField("y") ?? type.GetProperty("y") ?? (System.Reflection.MemberInfo)type.GetField("Item2");
            if (xMember != null && zMember != null)
            {
                object xv = xMember is System.Reflection.FieldInfo fi ? fi.GetValue(cell) : ((System.Reflection.PropertyInfo)xMember).GetValue(cell, null);
                object zv = zMember is System.Reflection.FieldInfo fiz ? fiz.GetValue(cell) : ((System.Reflection.PropertyInfo)zMember).GetValue(cell, null);
                return $"({xv},{zv})";
            }
            string s = cell.ToString();
            if (s.StartsWith("(") && s.EndsWith(")")) return s;
            return $"({s})";
        }

        public void UpdateResponsiveLayout()
        {
            if (panelRect == null) return;

            int sw = Screen.width;
            int sh = Screen.height;
            if (sw <= 0) sw = 1600;
            if (sh <= 0) sh = 900;

            CalculateSafeBounds(sw, sh, Expanded, out Vector2 pos, out Vector2 size, out _, out _);

            panelRect.anchoredPosition = pos;
            panelRect.sizeDelta = size;

            float usableW = size.x - 24f;

            if (compactGroup != null) compactGroup.SetActive(!Expanded);
            if (expandedGroup != null) expandedGroup.SetActive(Expanded);

            if (!Expanded)
            {
                if (compactTitleText != null)
                {
                    compactTitleText.rectTransform.anchoredPosition = new Vector2(12f, -4f);
                    compactTitleText.rectTransform.sizeDelta = new Vector2(usableW, 30f);
                }
                if (compactStatusText != null)
                {
                    compactStatusText.rectTransform.anchoredPosition = new Vector2(12f, -36f);
                    compactStatusText.rectTransform.sizeDelta = new Vector2(usableW, 28f);
                }
            }
            else
            {
                // Reflow Header: Title, Collapse button, status
                float collapseW = 96f;
                if (collapseBtnRt != null)
                {
                    collapseBtnRt.anchoredPosition = new Vector2(size.x - 12f - collapseW, -6f);
                    collapseBtnRt.sizeDelta = new Vector2(collapseW, 32f);
                }
                if (titleLabel != null)
                {
                    titleLabel.rectTransform.anchoredPosition = new Vector2(12f, -6f);
                    titleLabel.rectTransform.sizeDelta = new Vector2(Mathf.Max(120f, usableW - collapseW - 8f), 32f);
                }
                if (statusLabel != null)
                {
                    statusLabel.rectTransform.anchoredPosition = new Vector2(12f, -42f);
                    statusLabel.rectTransform.sizeDelta = new Vector2(usableW, 56f);
                }

                // Reflow Tab bar with larger tab heights (shifted down 28px for 2-line expanded status)
                if (tabBar != null)
                {
                    var tabRt = tabBar.GetComponent<RectTransform>();
                    tabRt.anchoredPosition = new Vector2(12f, -102f);
                    tabRt.sizeDelta = new Vector2(usableW, 36f);

                    float tabW = (usableW - 8f) / 3f;
                    if (tab0Rt != null) { tab0Rt.anchoredPosition = new Vector2(0f, 0f); tab0Rt.sizeDelta = new Vector2(tabW, 36f); }
                    if (tab1Rt != null) { tab1Rt.anchoredPosition = new Vector2(tabW + 4f, 0f); tab1Rt.sizeDelta = new Vector2(tabW, 36f); }
                    if (tab2Rt != null) { tab2Rt.anchoredPosition = new Vector2((tabW + 4f) * 2f, 0f); tab2Rt.sizeDelta = new Vector2(tabW, 36f); }
                    if (tab0Text != null) tab0Text.rectTransform.sizeDelta = new Vector2(tabW, 36f);
                    if (tab1Text != null) tab1Text.rectTransform.sizeDelta = new Vector2(tabW, 36f);
                    if (tab2Text != null) tab2Text.rectTransform.sizeDelta = new Vector2(tabW, 36f);
                }

                if (tab0Container != null) tab0Container.SetActive(activeTab == 0);
                if (tab1Container != null) tab1Container.SetActive(activeTab == 1);
                if (tab2Container != null) tab2Container.SetActive(activeTab == 2);

                Color activeBg = new Color(.18f, .42f, .58f, 0.95f);
                Color inactiveBg = new Color(.06f, .12f, .18f, 0.8f);
                Color activeTxt = Color.white;
                Color inactiveTxt = new Color(.65f, .75f, .82f);

                if (tab0Bg != null) tab0Bg.color = activeTab == 0 ? activeBg : inactiveBg;
                if (tab1Bg != null) tab1Bg.color = activeTab == 1 ? activeBg : inactiveBg;
                if (tab2Bg != null) tab2Bg.color = activeTab == 2 ? activeBg : inactiveBg;

                if (tab0Text != null) tab0Text.color = activeTab == 0 ? activeTxt : inactiveTxt;
                if (tab1Text != null) tab1Text.color = activeTab == 1 ? activeTxt : inactiveTxt;
                if (tab2Text != null) tab2Text.color = activeTab == 2 ? activeTxt : inactiveTxt;

                float contentTop = -144f;
                float contentH = Mathf.Max(60f, size.y - 144f - 34f);
                if (tab0Container != null)
                {
                    var cRt = tab0Container.GetComponent<RectTransform>();
                    cRt.anchoredPosition = new Vector2(12f, contentTop);
                    cRt.sizeDelta = new Vector2(usableW, contentH);
                }
                if (tab1Container != null)
                {
                    var cRt = tab1Container.GetComponent<RectTransform>();
                    cRt.anchoredPosition = new Vector2(12f, contentTop);
                    cRt.sizeDelta = new Vector2(usableW, contentH);
                }
                if (tab2Container != null)
                {
                    var cRt = tab2Container.GetComponent<RectTransform>();
                    cRt.anchoredPosition = new Vector2(12f, contentTop);
                    cRt.sizeDelta = new Vector2(usableW, contentH);
                }

                // Reflow Tab 0 contents: visual map and GPS banner or grid
                float mapDim = Mathf.Min(usableW, contentH - 52f);
                mapDim = Mathf.Max(120f, mapDim);

                if (visualMapRt != null)
                {
                    visualMapRt.anchoredPosition = new Vector2((usableW - mapDim) * 0.5f, 0f);
                    visualMapRt.sizeDelta = new Vector2(mapDim, mapDim);
                }
                if (visualMapContainer != null)
                {
                    visualMapContainer.SetActive(showVisualMap);
                }

                if (gpsLabel != null)
                {
                    gpsLabel.rectTransform.anchoredPosition = new Vector2(0f, -mapDim - 4f);
                    gpsLabel.rectTransform.sizeDelta = new Vector2(usableW, 46f);
                    gpsLabel.gameObject.SetActive(showVisualMap);
                }

                float legendH = 26f;
                float gridH = Mathf.Max(30f, contentH - legendH - 4f);
                if (gridLabel != null)
                {
                    gridLabel.rectTransform.anchoredPosition = Vector2.zero;
                    gridLabel.rectTransform.sizeDelta = new Vector2(usableW, gridH);
                    gridLabel.gameObject.SetActive(!showVisualMap);
                }
                if (legendLabel != null)
                {
                    legendLabel.rectTransform.anchoredPosition = new Vector2(0f, -contentH + legendH);
                    legendLabel.rectTransform.sizeDelta = new Vector2(usableW, legendH);
                    legendLabel.gameObject.SetActive(!showVisualMap);
                }

                // Reflow Tab 1 contents: beliefs
                if (beliefsLabel != null)
                {
                    beliefsLabel.rectTransform.anchoredPosition = Vector2.zero;
                    beliefsLabel.rectTransform.sizeDelta = new Vector2(usableW, contentH);
                }

                // Reflow Tab 2 contents: history
                if (eventsLabel != null)
                {
                    eventsLabel.rectTransform.anchoredPosition = Vector2.zero;
                    eventsLabel.rectTransform.sizeDelta = new Vector2(usableW, contentH);
                }

                // Reflow Footer
                if (footerLabel != null)
                {
                    footerLabel.rectTransform.anchoredPosition = new Vector2(12f, -size.y + 26f);
                    footerLabel.rectTransform.sizeDelta = new Vector2(usableW, 26f);
                    footerLabel.text = "[M] close · 1/2/3: tabs · [G] toggle grid · Fog of war active";
                }
            }
        }

        public void BuildUi()
        {
            if (panelObject != null) return;

            var root = new GameObject("Starfall map canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.layer = 5;
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = View != null ? View : Camera.main;
            canvas.planeDistance = .5f;
            canvas.sortingOrder = 12;

            if (root.GetComponent<GraphicRaycaster>() == null)
                root.AddComponent<GraphicRaycaster>();

            // ConstantPixelSize ensures 1 Canvas unit = 1 physical screen pixel.
            // Minimum 14px font stays 14 physical pixels on screen at 800x450 and never halves to 6px.
            canvasScaler = root.GetComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasScaler.scaleFactor = 1.0f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 24);

            panelObject = new GameObject("Map panel background", typeof(RectTransform), typeof(Image));
            panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.SetParent(root.transform, false);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(1, 1);
            panelRect.pivot = new Vector2(1, 1);
            panelObject.GetComponent<Image>().color = new Color(.02f, .045f, .07f, .94f);

            Text MakeLabel(GameObject parent, string name, Vector2 pos, Vector2 size, int fontSize, Color color, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Text));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent.transform, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;

                var txt = go.GetComponent<Text>();
                txt.font = font;
                txt.fontSize = Mathf.Max(14, fontSize);
                txt.fontStyle = style;
                txt.color = color;
                txt.alignment = align;
                txt.supportRichText = false;
                txt.horizontalOverflow = HorizontalWrapMode.Wrap;
                txt.verticalOverflow = VerticalWrapMode.Truncate;
                txt.raycastTarget = false;
                return txt;
            }

            // 1. Compact Unobtrusive Summary Group
            compactGroup = new GameObject("Compact summary group", typeof(RectTransform), typeof(Image), typeof(Button));
            var compactRt = compactGroup.GetComponent<RectTransform>();
            compactRt.SetParent(panelObject.transform, false);
            compactRt.anchorMin = Vector2.zero;
            compactRt.anchorMax = Vector2.one;
            compactRt.offsetMin = Vector2.zero;
            compactRt.offsetMax = Vector2.zero;

            var compactImg = compactGroup.GetComponent<Image>();
            compactImg.color = Color.clear;
            compactImg.raycastTarget = true;

            compactTitleText = MakeLabel(compactGroup, "CompactTitle", new Vector2(12, -4), new Vector2(336, 30), 26, new Color(.6f, .93f, .93f), FontStyle.Bold);
            compactTitleText.text = "STARFALL / PLACE MEMORY";

            compactStatusText = MakeLabel(compactGroup, "CompactStatus", new Vector2(12, -36), new Vector2(336, 28), 24, new Color(.78f, .86f, .91f));
            compactStatusText.text = "0 cells · 0 places · Open map";

            var compactBtn = compactGroup.GetComponent<Button>();
            compactBtn.targetGraphic = compactImg;
            compactBtn.onClick.AddListener(() => { Expanded = true; UpdateResponsiveLayout(); });

            // 2. Expanded Map Group (Deliberate Tabbed Layout)
            expandedGroup = new GameObject("Expanded map group", typeof(RectTransform));
            var expandedRt = expandedGroup.GetComponent<RectTransform>();
            expandedRt.SetParent(panelObject.transform, false);
            expandedRt.anchorMin = Vector2.zero;
            expandedRt.anchorMax = Vector2.one;
            expandedRt.offsetMin = Vector2.zero;
            expandedRt.offsetMax = Vector2.zero;

            // Header: Title & Collapse button on top row; Status on second row
            titleLabel = MakeLabel(expandedGroup, "Title", new Vector2(12, -6), new Vector2(230, 32), 26, new Color(.6f, .93f, .93f), FontStyle.Bold);
            titleLabel.text = "STARFALL / SPATIAL MEMORY";

            // Clickable Collapse button
            var collapseGo = new GameObject("CollapseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            collapseBtnRt = collapseGo.GetComponent<RectTransform>();
            collapseBtnRt.SetParent(expandedGroup.transform, false);
            collapseBtnRt.anchorMin = collapseBtnRt.anchorMax = new Vector2(0, 1);
            collapseBtnRt.pivot = new Vector2(0, 1);
            collapseBtnRt.anchoredPosition = new Vector2(336f - 96f, -6f);
            collapseBtnRt.sizeDelta = new Vector2(96f, 32f);

            var collapseImg = collapseGo.GetComponent<Image>();
            collapseImg.color = new Color(.18f, .34f, .48f, 0.95f);
            collapseImg.raycastTarget = true;

            var collapseTxt = MakeLabel(collapseGo, "Label", Vector2.zero, new Vector2(96f, 32f), 22, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            collapseTxt.text = "Collapse";

            collapseButton = collapseGo.GetComponent<Button>();
            collapseButton.targetGraphic = collapseImg;
            collapseButton.onClick.AddListener(() => { Expanded = false; UpdateResponsiveLayout(); });

            // Redundant provenance heading hidden to preserve header layout with larger typography
            provenanceLabel = MakeLabel(expandedGroup, "Provenance", new Vector2(12, -6), Vector2.zero, 24, new Color(.55f, .65f, .72f), FontStyle.Normal, TextAnchor.UpperRight);
            provenanceLabel.gameObject.SetActive(false);

            statusLabel = MakeLabel(expandedGroup, "Status", new Vector2(12, -42), new Vector2(336, 56), 24, new Color(.78f, .86f, .91f));
            statusLabel.text = "Cell (--)\n0 explored cells · 0 places";

            // Tab bar: 3 clickable tabs
            tabBar = new GameObject("Tab bar", typeof(RectTransform));
            var tabRt = tabBar.GetComponent<RectTransform>();
            tabRt.SetParent(expandedGroup.transform, false);
            tabRt.anchorMin = tabRt.anchorMax = new Vector2(0, 1);
            tabRt.pivot = new Vector2(0, 1);
            tabRt.anchoredPosition = new Vector2(12, -102);
            tabRt.sizeDelta = new Vector2(336, 36);

            GameObject MakeTabButton(string tabName, string labelText, int tabIdx, float x, float w, out Image bgImg, out Text label, out RectTransform btnRect)
            {
                var btnGo = new GameObject(tabName, typeof(RectTransform), typeof(Image), typeof(Button));
                var bRt = btnGo.GetComponent<RectTransform>();
                bRt.SetParent(tabBar.transform, false);
                bRt.anchorMin = bRt.anchorMax = new Vector2(0, 1);
                bRt.pivot = new Vector2(0, 1);
                bRt.anchoredPosition = new Vector2(x, 0);
                bRt.sizeDelta = new Vector2(w, 36);

                bgImg = btnGo.GetComponent<Image>();
                bgImg.color = new Color(.06f, .12f, .18f, .8f);
                bgImg.raycastTarget = true;

                label = MakeLabel(btnGo, "Label", new Vector2(0, 0), new Vector2(w, 36), 22, new Color(.65f, .75f, .82f), FontStyle.Bold, TextAnchor.MiddleCenter);
                label.text = labelText;

                var btn = btnGo.GetComponent<Button>();
                btn.targetGraphic = bgImg;
                int capturedIdx = tabIdx;
                btn.onClick.AddListener(() => SetActiveTab(capturedIdx));
                btnRect = bRt;
                return btnGo;
            }

            float tabW = 108f;
            MakeTabButton("Tab0", "1: Map", 0, 0, tabW, out tab0Bg, out tab0Text, out tab0Rt);
            MakeTabButton("Tab1", "2: Beliefs", 1, tabW + 4, tabW, out tab1Bg, out tab1Text, out tab1Rt);
            MakeTabButton("Tab2", "3: History", 2, (tabW + 4) * 2, tabW, out tab2Bg, out tab2Text, out tab2Rt);

            // Tab content containers
            GameObject MakeContainer(string name)
            {
                var cGo = new GameObject(name, typeof(RectTransform));
                var cRt = cGo.GetComponent<RectTransform>();
                cRt.SetParent(expandedGroup.transform, false);
                cRt.anchorMin = cRt.anchorMax = new Vector2(0, 1);
                cRt.pivot = new Vector2(0, 1);
                cRt.anchoredPosition = new Vector2(12, -144);
                cRt.sizeDelta = new Vector2(336, 120);
                return cGo;
            }

            // Tab 0: Visual Tactical Map & Grid
            tab0Container = MakeContainer("Tab0Container");
            EnsureMapTextures();

            visualMapContainer = new GameObject("VisualMapContainer", typeof(RectTransform), typeof(Image));
            visualMapRt = visualMapContainer.GetComponent<RectTransform>();
            visualMapRt.SetParent(tab0Container.transform, false);
            visualMapRt.anchorMin = visualMapRt.anchorMax = new Vector2(0, 1);
            visualMapRt.pivot = new Vector2(0, 1);
            visualMapContainer.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.08f, 0.95f);

            var topoGo = new GameObject("TopographicalMapImage", typeof(RectTransform), typeof(RawImage));
            var topoRt = topoGo.GetComponent<RectTransform>();
            topoRt.SetParent(visualMapRt, false);
            topoRt.anchorMin = Vector2.zero;
            topoRt.anchorMax = Vector2.one;
            topoRt.sizeDelta = Vector2.zero;
            topoRawImage = topoGo.GetComponent<RawImage>();
            topoRawImage.texture = topoTexture;
            topoRawImage.raycastTarget = false;

            var fogGo = new GameObject("FogOfWarImage", typeof(RectTransform), typeof(RawImage));
            var fogRt = fogGo.GetComponent<RectTransform>();
            fogRt.SetParent(visualMapRt, false);
            fogRt.anchorMin = Vector2.zero;
            fogRt.anchorMax = Vector2.one;
            fogRt.sizeDelta = Vector2.zero;
            fogRawImage = fogGo.GetComponent<RawImage>();
            fogRawImage.texture = fogTexture;
            fogRawImage.raycastTarget = false;

            // POI Badges
            poiBadges.Clear();
            var landmarks = new[]
            {
                (new Vector3(-165f, 0, 118f), "🏛️ Refuge"),
                (new Vector3(121f, 0, -58f), "💧 Spring"),
                (new Vector3(126f, 0, -80f), "🍒 Berries"),
                (new Vector3(10f, 0, -25f), "🪨 Pebbles"),
                (new Vector3(120f, 0, -80f), "🔥 Hearth")
            };
            foreach (var (wpos, name) in landmarks)
            {
                var pGo = new GameObject("POI_" + name, typeof(RectTransform), typeof(Text));
                var pRt = pGo.GetComponent<RectTransform>();
                pRt.SetParent(visualMapRt, false);
                pRt.anchorMin = pRt.anchorMax = new Vector2(0, 1);
                pRt.pivot = new Vector2(0.5f, 0.5f);
                pRt.sizeDelta = new Vector2(85f, 20f);
                var pTxt = pGo.GetComponent<Text>();
                pTxt.font = font;
                pTxt.fontSize = 14;
                pTxt.fontStyle = FontStyle.Bold;
                pTxt.color = new Color(1f, 0.92f, 0.65f, 0.95f);
                pTxt.alignment = TextAnchor.MiddleCenter;
                pTxt.text = name;
                poiBadges.Add((wpos, name, pRt, pTxt));
            }

            // Player chevron marker
            var chevGo = new GameObject("PlayerChevron", typeof(RectTransform), typeof(Text));
            playerChevronRt = chevGo.GetComponent<RectTransform>();
            playerChevronRt.SetParent(visualMapRt, false);
            playerChevronRt.anchorMin = playerChevronRt.anchorMax = new Vector2(0, 1);
            playerChevronRt.pivot = new Vector2(0.5f, 0.5f);
            playerChevronRt.sizeDelta = new Vector2(24f, 24f);
            var chevTxt = chevGo.GetComponent<Text>();
            chevTxt.font = font;
            chevTxt.fontSize = 20;
            chevTxt.fontStyle = FontStyle.Bold;
            chevTxt.color = new Color(1f, 0.88f, 0.2f, 1f);
            chevTxt.alignment = TextAnchor.MiddleCenter;
            chevTxt.text = "▲";

            gpsLabel = MakeLabel(tab0Container, "GpsBanner", new Vector2(0, -260), new Vector2(336, 46), 14, new Color(0.85f, 0.88f, 0.92f), FontStyle.Normal, TextAnchor.MiddleLeft);
            gpsLabel.text = "TACTICAL GPS INITIALIZING...";

            gridLabel = MakeLabel(tab0Container, "AsciiGrid", new Vector2(0, 0), new Vector2(336, 88), 16, new Color(.95f, .90f, .72f));
            gridLabel.lineSpacing = 1.0f;
            gridLabel.text = "[Grid initializing]";

            legendLabel = MakeLabel(tab0Container, "Legend", new Vector2(0, -92), new Vector2(336, 26), 14, new Color(.65f, .75f, .82f));
            legendLabel.text = "@ You  · Explored  ░ Unknown  B/S/R Beliefs";

            // Default: show visual map
            gridLabel.gameObject.SetActive(false);
            legendLabel.gameObject.SetActive(false);

            // Tab 1: Beliefs
            tab1Container = MakeContainer("Tab1Container");
            beliefsLabel = MakeLabel(tab1Container, "Beliefs", new Vector2(0, 0), new Vector2(336, 120), 24, new Color(.94f, .88f, .73f));
            beliefsLabel.text = "REMEMBERED PLACES:\nNone observed yet";

            // Tab 2: History (Events)
            tab2Container = MakeContainer("Tab2Container");
            eventsLabel = MakeLabel(tab2Container, "Events", new Vector2(0, 0), new Vector2(336, 120), 24, new Color(.80f, .85f, .88f));
            eventsLabel.text = "RECENT EVENTS:\nNo observations recorded";

            // Footer
            footerLabel = MakeLabel(expandedGroup, "Footer", new Vector2(12, -240), new Vector2(336, 26), 22, new Color(.55f, .65f, .72f));
            footerLabel.text = "[M] close · 1/2/3: tabs · [G] toggle grid · Fog of war active";

            UpdateResponsiveLayout();
            panelObject.SetActive(Visible);
        }

        public void RefreshUi()
        {
            if (panelObject == null) BuildUi();

            if (canvas != null && canvas.worldCamera == null)
                canvas.worldCamera = View != null ? View : Camera.main;

            if (titleLabel == null) return;

            if (!ViewModel.IsValid)
            {
                string unavailableStatus = "[Map unavailable: " + ViewModel.StatusMessage + "]";
                statusLabel.text = unavailableStatus;
                if (compactStatusText != null) compactStatusText.text = unavailableStatus;

                provenanceLabel.text = "Place Memory: Unavailable";

                gridLabel.text = "";
                if (gpsLabel != null) gpsLabel.text = "[GPS UNAVAILABLE]";
                beliefsLabel.text = "Authoritative state failed validation or scope mismatch.";
                eventsLabel.text = "";
                return;
            }

            statusLabel.text = $"Cell {FormatCell(ViewModel.ActorCell)}\n{ViewModel.ExploredCellCount} explored cells · {ViewModel.ObservedPlaceCount} places";
            if (compactStatusText != null) compactStatusText.text = $"{ViewModel.ExploredCellCount} cells · {ViewModel.ObservedPlaceCount} places · Open map";

            provenanceLabel.text = "Place Memory: Synchronized";
            gridLabel.text = ViewModel.CachedAsciiGrid;
            beliefsLabel.text = ViewModel.CachedBeliefsText;
            eventsLabel.text = ViewModel.CachedEventsText;
        }
    }
}
