using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CityLife.World
{
    // Camera-space UI is present in the real player and in its offscreen render captures.
    [DefaultExecutionOrder(-100)]
    public sealed class NpcDecisionHud : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public Camera View;
        public Text Summary, Perceptions, History;
        public Canvas Canvas;
        public bool Detailed;
        public NpcPlayerControls Controls;
        private RectTransform backgroundRect, footerRect;
        private Text footer, thoughts;
        private GameObject thoughtsBackground;
        public string LivingMemoryText;
        private GameObject panelGroup;
        private Canvas decisionCanvas;
        public Canvas DecisionCanvas => decisionCanvas;
        public GameObject PanelGroup => panelGroup;
        public bool IsDecisionActive => panelGroup != null && panelGroup.activeSelf;
        public void StepLateUpdate() => LateUpdate();
        private void Awake()
        {
            var root = new GameObject("NPC decision panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.layer = 5;
            Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.worldCamera = View; Canvas.planeDistance = .5f; Canvas.sortingOrder = 10;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            panelGroup = new GameObject("Decision panel content", typeof(RectTransform), typeof(Canvas));
            decisionCanvas = panelGroup.GetComponent<Canvas>();
            var groupRt = panelGroup.GetComponent<RectTransform>();
            groupRt.SetParent(root.transform, false);
            groupRt.anchorMin = Vector2.zero; groupRt.anchorMax = Vector2.one;
            groupRt.offsetMin = Vector2.zero; groupRt.offsetMax = Vector2.zero;

            RectTransform Rect(GameObject o, float x, float y, float w, float h)
            {
                var rect = o.GetComponent<RectTransform>(); rect.SetParent(panelGroup.transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
            }
            var panel = new GameObject("Decision panel background", typeof(RectTransform), typeof(Image));
            backgroundRect = Rect(panel, 20, 20, 500, 855); panel.GetComponent<Image>().color = new Color(.02f, .045f, .07f, .94f);
            Text Label(string name, float y, float h, int size, Color color)
            {
                var o = new GameObject(name, typeof(RectTransform), typeof(Text)); Rect(o, 40, y, 460, h);
                var t = o.GetComponent<Text>(); t.font = font; t.fontSize = size; t.color = color;
                t.supportRichText = false; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
                return t;
            }
            var title = Label("Title", 35, 50, 28, new Color(.6f, .93f, .93f)); title.text = "STARFALL / Inhabitant decisions";
            Summary = Label("Current decision", 90, 160, 20, Color.white);
            Perceptions = Label("Perceived objects", 260, 170, 19, new Color(.78f, .86f, .91f));
            History = Label("Action log", 442, 346, 18, new Color(.94f, .88f, .73f));
            footer = Label("Controls", 798, 72, 17, new Color(.65f, .75f, .82f)); footerRect = footer.rectTransform;
            footer.text = "P options · Tab possess · F spectator · M map\nE interact/drink · C container/roast · H eat · G drop\nL decisions · R autonomy · Shift run · RMB look";
            if (Brain.OptionalPlanner != null)
            {
                // Keep memory/reflection inside the centre lane, clear of the
                // top-right Controls/Options overlay in the actual player.
                thoughtsBackground = new GameObject("Local thoughts background", typeof(RectTransform), typeof(Image));
                Rect(thoughtsBackground, 535, 20, 550, 430);
                thoughtsBackground.GetComponent<Image>().color = new Color(.02f, .045f, .07f, .86f);
                var o = new GameObject("Optional local thoughts", typeof(RectTransform), typeof(Text)); Rect(o, 555, 35, 510, 390);
                thoughts = o.GetComponent<Text>(); thoughts.font = font; thoughts.fontSize = 20; thoughts.color = new Color(.8f, .94f, .97f);
                thoughts.supportRichText = false; thoughts.horizontalOverflow = HorizontalWrapMode.Wrap;
                footer.text = "P options · Tab possess · F spectator · M map\nE interact/drink · C container/roast · H eat · G drop\nL decisions · R autonomy · Shift run · F11 display";
            }
        }
        private void LateUpdate() => Refresh();
        public void Refresh()
        {
            bool modal = Brain != null && Brain.MenuPaused;
            if (panelGroup != null && panelGroup.activeSelf == modal)
            {
                panelGroup.SetActive(!modal);
                if (decisionCanvas != null) decisionCanvas.enabled = !modal;
            }
            if (modal) return;
            if (!Brain.Ready || Summary == null) return;
            if (thoughts != null)
            {
                var planner = Brain.OptionalPlanner; thoughts.gameObject.SetActive(Detailed);
                if(thoughtsBackground!=null)thoughtsBackground.SetActive(Detailed);
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
                    thoughts.text = "SURVIVAL MIND / LOCAL THOUGHTS\n" + survStatus + " (" + providerInfo + ")\nAdvisory plan: " + survPlan +
                        "\n\nInner monologue: " + survDialogue + "\n\nGenerated reflection: " + survReflection +
                        "\n\nActions use deterministic checks & physics.";
                }
                else
                {
                    thoughts.text = "OPTIONAL LOCAL THOUGHTS\n" + planner.Status + "\nAdvisory plan: " + planner.Plan +
                        "\n\nFictional dialogue: " + planner.Dialogue + "\n\nGenerated reflection: " + planner.Reflection +
                        "\n\nActions use deterministic checks. No learning.";
                }
                if (!string.IsNullOrEmpty(LivingMemoryText)) thoughts.text = LivingMemoryText;
                if(thoughtsBackground!=null && !string.IsNullOrEmpty(LivingMemoryText))
                {
                    // The optional planner needs a long card. Ordinary memory
                    // status is normally only a few lines: size to its visible
                    // content so it does not mask the canyon/giant during play.
                    int lines=LivingMemoryText.Split('\n').Length;
                    float height=Mathf.Clamp(43f+lines*27f,125f,245f);
                    thoughtsBackground.GetComponent<RectTransform>().sizeDelta=new Vector2(550f,height);
                    thoughts.rectTransform.sizeDelta=new Vector2(510f,height-25f);
                }
                else if(thoughtsBackground!=null)
                {
                    thoughtsBackground.GetComponent<RectTransform>().sizeDelta=new Vector2(550f,430f);
                    thoughts.rectTransform.sizeDelta=new Vector2(510f,390f);
                }
                footer.text = "P options · Tab possess/release · F spectator\nL decisions · R autonomy · RMB look\nF11 display · Planner " + (planner.EnabledByUser ? "on" : "off") +
                    (!string.IsNullOrEmpty(LivingMemoryText) ? " · memory status shown" : "");
            }
            Perceptions.gameObject.SetActive(Detailed); History.gameObject.SetActive(Detailed);
            backgroundRect.sizeDelta = new Vector2(500, Detailed ? 855 : 340);
            footerRect.anchoredPosition = new Vector2(40, Detailed ? -798 : -260);
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
            Summary.text = mode + "  |  " + (Brain.Possessed ? "Autonomy suspended" : Brain.Running ? "Autonomy on" : "Autonomy stopped") +
                "\nTick " + Brain.Tick + "  |  " + Brain.Phase +
                "\nGoal: " + currentGoal +
                "\nCargo: " + cargo +
                "\nResult: " + Brain.LastResult;
            var foodRuntime = Brain.Survival != null ? Brain.Survival.Food : null;
            if (foodRuntime == null) foodRuntime = FindAnyObjectByType<Starfall.Food.IntegratedFoodRuntime>();
            if (foodRuntime != null && foodRuntime.Model != null)
            {
                var food = foodRuntime.Model.State;
                int visitedCount = food.observedPlaces != null ? food.observedPlaces.Count : 0;
                int exploredCount = food.exploredCells != null ? food.exploredCells.Count : 0;
                int healthPct = Mathf.Clamp(food.body.health / 100, 0, 100);
                int strengthPct = Mathf.Clamp((10000 - food.body.fatigue) / 100, 0, 100);
                int hungerPct = Mathf.Clamp(food.satiety / 100, 0, 100);
                int thirstPct = Mathf.Clamp(food.hydration / 100, 0, 100);

                string airAlert = "";
                if (food.body.submerged)
                {
                    int airSec = Mathf.Max(0, 15 - food.body.submergedSeconds);
                    airAlert = airSec > 0 ? $"  |  AIR: {airSec}s [SUBMERGED]" : "  |  AIR: 0s [DROWNING!]";
                }

                if (food.body.dead)
                {
                    Summary.text = mode + " | Tick " + Brain.Tick +
                        "\nBODY DEAD: " + food.body.cause +
                        "\nHealth: 0%  |  World & memory preserved" +
                        "\nAwaiting verified safe return to refuge";
                }
                else
                {
                    int staminaPct = Brain.Actor != null ? Mathf.Clamp(Mathf.RoundToInt(Brain.Actor.Stamina), 0, 100) : 100;
                    var moonbag = Brain.GetComponentInChildren<HunterMoonbag>();
                    int mbCount = moonbag != null ? moonbag.StoredCount : 0;
                    Summary.text = mode + " | Tick " + Brain.Tick +
                        "\nGoal: " + currentGoal + "  |  Cargo: " + cargo +
                        $"\nHealth: {healthPct}%  |  Stamina: {staminaPct}%  |  Strength: {strengthPct}%" +
                        $"\nHunger: {hungerPct}%  |  Thirst: {thirstPct}%  |  Water: {food.freshwaterMl}ml" + airAlert +
                        $"\nExplored: {exploredCount} cells  |  Places: {visitedCount}  |  Moonbag: {mbCount}/2";
                }
                footer.text = "M map · Shift sprint · X holster club · B moonbag\nE pick/fish/drink · G drop · H eat from hand · Tab possess";
            }
            var perceived = new StringBuilder("PERCEPTION / radius + line of sight\n");
            foreach (var x in Brain.Perception.Current)
                perceived.Append(x.id).Append("  ").Append(x.distanceMillimetres / 1000f).Append("m  ")
                    .Append(!x.permission ? "DENIED" : !x.available ? "UNAVAILABLE" : "ELIGIBLE").Append('\n');
            perceived.Append("Occluded contacts: ").Append(Brain.Perception.OccludedCount);
            Perceptions.text = perceived.ToString();
            var history = new StringBuilder("DECISIONS / ACTIONS / OUTCOMES\n");
            foreach (var row in Brain.Log.Entries.Where(x => x.phase != "perception").TakeLast(3))
            {
                history.Append('#').Append(row.sequence).Append(" t").Append(row.tick).Append(' ').Append(row.phase.ToUpperInvariant())
                    .Append("  ").Append(row.goal).Append('\n').Append(row.action).Append(" → ").Append(row.result).Append('\n');
                if (row.fallback.Length > 0) history.Append("Fallback: ").Append(row.fallback).Append('\n');
            }
            History.text = history.ToString();
            UnityEngine.Canvas.ForceUpdateCanvases();
        }
    }
}
