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
        private void Awake()
        {
            var root = new GameObject("NPC decision panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.layer = 5;
            Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.worldCamera = View; Canvas.planeDistance = .5f; Canvas.sortingOrder = 10;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform Rect(GameObject o, float x, float y, float w, float h)
            {
                var rect = o.GetComponent<RectTransform>(); rect.SetParent(root.transform, false);
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
            Summary = Label("Current decision", 92, 130, 22, Color.white);
            Perceptions = Label("Perceived objects", 230, 200, 19, new Color(.78f, .86f, .91f));
            History = Label("Action log", 442, 346, 18, new Color(.94f, .88f, .73f));
            footer = Label("Controls", 798, 72, 17, new Color(.65f, .75f, .82f)); footerRect = footer.rectTransform;
            footer.text = "P options · Tab possess/release · F spectator\nL decisions · R autonomy · RMB look\nDeterministic rules; no LLM or learning.";
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
                footer.text = "P options · Tab possess/release · F spectator\nL decisions · R autonomy · RMB look\nF11 display · Local thoughts off by default";
            }
        }
        private void LateUpdate() => Refresh();
        public void Refresh()
        {
            if (!Brain.Ready || Summary == null) return;
            if (thoughts != null)
            {
                var planner = Brain.OptionalPlanner; thoughts.gameObject.SetActive(Detailed);
                if(thoughtsBackground!=null)thoughtsBackground.SetActive(Detailed);
                thoughts.text = "OPTIONAL LOCAL THOUGHTS\n" + planner.Status + "\nAdvisory plan: " + planner.Plan +
                    "\n\nFictional dialogue: " + planner.Dialogue + "\n\nGenerated reflection: " + planner.Reflection +
                    "\n\nActions use deterministic checks. No learning.";
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
            backgroundRect.sizeDelta = new Vector2(500, Detailed ? 855 : 310);
            footerRect.anchoredPosition = new Vector2(40, Detailed ? -798 : -238);
            string mode = Brain.MenuPaused ? "PAUSED / " : "";
            mode += Brain.Possessed ? "Possession" : Controls != null && Controls.FreeSpectator ? "Spectator" : "Autonomous NPC";
            Summary.text = mode + "  |  " + (Brain.Possessed ? "Autonomy suspended" : Brain.Running ? "Autonomy on" : "Autonomy stopped") +
                "\nTick " + Brain.Tick + "  |  " + Brain.Phase +
                "\nGoal: " + (Brain.GoalId.Length > 0 ? Brain.GoalId : "observe / wait") +
                "\nCargo: " + (Brain.Actions.Held != null ? Brain.Actions.Held.StableId : "none") +
                "\nResult: " + Brain.LastResult;
            if(Brain.Survival!=null && Brain.Survival.Enabled && Brain.Phase.StartsWith("Survive"))
            {
                var food=Brain.Survival.Food.Model.State;
                Summary.text=mode+" survivor | Tick "+Brain.Tick+
                    "\nEnergy "+food.satiety+" / water "+food.hydration+" / fruit "+food.carriedFruit+
                    "\n"+(Brain.Survival.LastChoiceByModel?"Model chose: ":"System state: ")+Brain.Survival.LastChoice+
                    "\nOutcome: "+Brain.Survival.LastOutcome;
                if(food.body.dead)
                    Summary.text=mode+" survivor | Tick "+Brain.Tick+
                        "\nBODY DEAD: "+food.body.cause+
                        "\nWorld and death record retained"
                        +"\nAwaiting verified safe return";
                footer.text="P options · Tab possess/release · F spectator\nL decisions · R autonomy · RMB look\nPlanner "+
                    (Brain.OptionalPlanner.EnabledByUser?"on":"off")+" · Survival model on";
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
