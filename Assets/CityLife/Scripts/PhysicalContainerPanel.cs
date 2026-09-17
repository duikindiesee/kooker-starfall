using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using CityLife.Items;

namespace CityLife.World
{
    /// <summary>
    /// Live player container inspection and slot grid panel.
    /// Operates in inhabitant possession mode; suppresses movement/look while open
    /// so the mouse pointer can interact with container slots and store/retrieve actions.
    /// Reuses the existing HUD Canvas and EventSystem without creating duplicate event systems.
    /// Directly projects authoritative ItemModel container slots without maintaining a separate inventory list.
    /// Provides explicit normal save trigger and real action receipt feedback.
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public sealed class PhysicalContainerPanel : MonoBehaviour
    {
        public NpcPlayerControls Controls;
        public PhysicalItemBootstrap Bootstrap;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;
        public string SelectedContainerId { get; private set; }
        public string LastReceipt { get; private set; } = "idle";
        public NpcInteractable SelectedTarget { get; private set; }

        private GameObject panelRoot;
        private Text titleLabel;
        private Text statusLabel;
        private Text statsLabel;
        private Text receiptLabel;
        private readonly Button[] slotButtons = new Button[4];
        private readonly Text[] slotTexts = new Text[4];
        private Button storeButton;
        private Text storeButtonText;
        private Button saveButton;
        private Text saveButtonText;
        private Button cycleButton;
        private bool initialized;

        private void Start()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            EnsureInitialized();

            if (!initialized) return;

            if (IsOpen)
            {
                // Strict close conditions: options menu opened, possession lost, or focus lost
                if (Controls == null || Controls.MenuOpen || Controls.Brain == null || !Controls.Brain.Possessed ||
                    (!Application.isFocused && !Controls.AllowUnfocusedTestInput))
                {
                    Close();
                    return;
                }

                ValidateOrUpdateSelection();
                UpdateContent();
            }
        }

        public void EnsureInitialized()
        {
            if (initialized) return;

            if (Controls == null) Controls = GetComponent<NpcPlayerControls>();
            if (Controls == null && Camera.main != null) Controls = Camera.main.GetComponent<NpcPlayerControls>();

            if (Bootstrap == null && Controls != null && Controls.Brain != null)
            {
                Bootstrap = Controls.Brain.PhysicalItems;
            }

            if (Controls != null && Controls.Hud != null && Controls.Hud.Canvas != null &&
                Controls.Brain != null && Controls.Brain.Ready)
            {
                BuildPanel();
                initialized = true;
            }
        }

        private void BuildPanel()
        {
            var canvas = Controls.Hud.Canvas;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            panelRoot = new GameObject("Physical container panel", typeof(RectTransform), typeof(Image));
            var rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetParent(canvas.transform, false);
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(1, 1);
            rootRect.pivot = new Vector2(1, 1);
            rootRect.anchoredPosition = new Vector2(-20, -20);
            rootRect.sizeDelta = new Vector2(500, 520);
            panelRoot.GetComponent<Image>().color = new Color(0.02f, 0.045f, 0.07f, 0.94f);

            RectTransform Rect(GameObject o, float x, float y, float w, float h)
            {
                var r = o.GetComponent<RectTransform>();
                r.SetParent(panelRoot.transform, false);
                r.anchorMin = r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0, 1);
                r.anchoredPosition = new Vector2(x, -y);
                r.sizeDelta = new Vector2(w, h);
                return r;
            }

            Text Label(string name, float x, float y, float w, float h, int size, Color color)
            {
                var o = new GameObject(name, typeof(RectTransform), typeof(Text));
                Rect(o, x, y, w, h);
                var t = o.GetComponent<Text>();
                t.font = font;
                t.fontSize = size;
                t.color = color;
                t.supportRichText = false;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                return t;
            }

            titleLabel = Label("Title", 20, 15, 460, 32, 22, new Color(0.6f, 0.93f, 0.93f));
            titleLabel.text = "CONTAINER INSPECTOR / SLOTS";

            statusLabel = Label("Status", 20, 50, 460, 24, 16, new Color(0.85f, 0.88f, 0.92f));
            statsLabel = Label("Stats", 20, 75, 460, 24, 15, new Color(0.72f, 0.85f, 0.78f));

            // Four stable-slot cells (2x2 grid)
            float slotW = 220, slotH = 75;
            float[] slotX = { 20, 260, 20, 260 };
            float[] slotY = { 108, 108, 192, 192 };

            for (int i = 0; i < 4; i++)
            {
                int slotIndex = i;
                var slotObj = new GameObject($"SlotCell {i}", typeof(RectTransform), typeof(Image), typeof(Button));
                Rect(slotObj, slotX[i], slotY[i], slotW, slotH);
                slotObj.GetComponent<Image>().color = new Color(0.08f, 0.16f, 0.22f, 1f);

                var btn = slotObj.GetComponent<Button>();
                btn.onClick.AddListener(() => OnSlotClicked(slotIndex));
                slotButtons[i] = btn;

                var textObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
                var tRect = textObj.GetComponent<RectTransform>();
                tRect.SetParent(slotObj.transform, false);
                tRect.anchorMin = Vector2.zero;
                tRect.anchorMax = Vector2.one;
                tRect.offsetMin = new Vector2(8, 4);
                tRect.offsetMax = new Vector2(-8, -4);

                var txt = textObj.GetComponent<Text>();
                txt.font = font;
                txt.fontSize = 14;
                txt.color = Color.white;
                txt.supportRichText = false;
                txt.alignment = TextAnchor.MiddleCenter;
                slotTexts[i] = txt;
            }

            // Store button
            var storeObj = new GameObject("StoreButton", typeof(RectTransform), typeof(Image), typeof(Button));
            Rect(storeObj, 20, 280, 460, 42);
            storeObj.GetComponent<Image>().color = new Color(0.12f, 0.35f, 0.40f, 1f);
            storeButton = storeObj.GetComponent<Button>();
            storeButton.onClick.AddListener(OnStoreClicked);

            var sTextObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var sRect = sTextObj.GetComponent<RectTransform>();
            sRect.SetParent(storeObj.transform, false);
            sRect.anchorMin = Vector2.zero;
            sRect.anchorMax = Vector2.one;
            sRect.offsetMin = sRect.offsetMax = Vector2.zero;
            storeButtonText = sTextObj.GetComponent<Text>();
            storeButtonText.font = font;
            storeButtonText.fontSize = 16;
            storeButtonText.color = Color.white;
            storeButtonText.alignment = TextAnchor.MiddleCenter;

            // Cycle container button
            var cycleObj = new GameObject("CycleButton", typeof(RectTransform), typeof(Image), typeof(Button));
            Rect(cycleObj, 20, 330, 225, 36);
            cycleObj.GetComponent<Image>().color = new Color(0.10f, 0.22f, 0.30f, 1f);
            cycleButton = cycleObj.GetComponent<Button>();
            cycleButton.onClick.AddListener(CycleSelection);

            var cTextObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var cRect = cTextObj.GetComponent<RectTransform>();
            cRect.SetParent(cycleObj.transform, false);
            cRect.anchorMin = Vector2.zero;
            cRect.anchorMax = Vector2.one;
            cRect.offsetMin = cRect.offsetMax = Vector2.zero;
            var cText = cTextObj.GetComponent<Text>();
            cText.font = font;
            cText.fontSize = 15;
            cText.color = Color.white;
            cText.alignment = TextAnchor.MiddleCenter;
            cText.text = "Cycle Target (T)";

            // Save state button
            var saveObj = new GameObject("SaveButton", typeof(RectTransform), typeof(Image), typeof(Button));
            Rect(saveObj, 255, 330, 225, 36);
            saveObj.GetComponent<Image>().color = new Color(0.18f, 0.32f, 0.22f, 1f);
            saveButton = saveObj.GetComponent<Button>();
            saveButton.onClick.AddListener(OnSaveClicked);

            var svTextObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var svRect = svTextObj.GetComponent<RectTransform>();
            svRect.SetParent(saveObj.transform, false);
            svRect.anchorMin = Vector2.zero;
            svRect.anchorMax = Vector2.one;
            svRect.offsetMin = svRect.offsetMax = Vector2.zero;
            var svText = svTextObj.GetComponent<Text>();
            svText.font = font;
            svText.fontSize = 15;
            svText.color = Color.white;
            svText.alignment = TextAnchor.MiddleCenter;
            svText.text = "Save Physical World";
            saveButtonText = svText;

            // Receipt label (sized to comfortably wrap full Windows paths)
            receiptLabel = Label("Receipt", 20, 372, 460, 60, 13, new Color(0.95f, 0.85f, 0.65f));
            receiptLabel.verticalOverflow = VerticalWrapMode.Overflow;
            receiptLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            receiptLabel.text = "Receipt: idle";

            // Footer navigation hint
            var hintLabel = Label("Hint", 20, 435, 460, 45, 14, new Color(0.60f, 0.70f, 0.78f));
            hintLabel.text = "C / Esc: close panel · T: cycle container · Click slot: retrieve · Click Store: store\nMouse pointer active for container interaction.";

            panelRoot.SetActive(false);
        }

        public void Open()
        {
            EnsureInitialized();
            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
                if (Controls != null) Controls.OnContainerPanelOpened();
                RefreshSelection();
                UpdateContent();
            }
        }

        public void Close()
        {
            if (panelRoot != null && panelRoot.activeSelf)
            {
                panelRoot.SetActive(false);
                if (Controls != null) Controls.OnContainerPanelClosed();
            }
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void RefreshSelection()
        {
            ValidateOrUpdateSelection();
        }

        public void CycleSelection()
        {
            var eligible = GetEligibleContainers();
            if (eligible.Count == 0)
            {
                SelectedContainerId = null;
                SelectedTarget = null;
                return;
            }

            int currentIdx = -1;
            for (int i = 0; i < eligible.Count; i++)
            {
                if (string.Equals(eligible[i].StableId, SelectedContainerId, StringComparison.Ordinal))
                {
                    currentIdx = i;
                    break;
                }
            }

            int nextIdx = (currentIdx + 1) % eligible.Count;
            SelectedContainerId = eligible[nextIdx].StableId;
            SelectedTarget = eligible[nextIdx];
            LastReceipt = $"selected-container-{SelectedContainerId}";
            UpdateContent();
        }

        private List<NpcInteractable> GetEligibleContainers()
        {
            var result = new List<NpcInteractable>();
            if (Bootstrap == null || Bootstrap.Model == null) return result;

            var model = Bootstrap.Model;
            Transform actorT = Controls != null && Controls.Brain != null && Controls.Brain.Actor != null
                ? Controls.Brain.Actor.transform
                : transform;

            // 1. Check held item first if it is a container
            if (Controls != null && Controls.Brain != null && Controls.Brain.Actions != null && Controls.Brain.Actions.Held != null)
            {
                var held = Controls.Brain.Actions.Held;
                if (model.TryGetItem(held.StableId, out var heldSnap) &&
                    model.TryGetDefinition(heldSnap.itemTypeId, out var heldDef) &&
                    heldDef.isContainer)
                {
                    result.Add(held);
                }
            }

            // 2. Check nearby free containers
            var freeCandidates = new List<NpcInteractable>();
            foreach (var inter in Bootstrap.AllInteractables)
            {
                if (inter == null || !inter.isActiveAndEnabled || !inter.Permission) continue;
                if (Controls != null && Controls.Brain != null && inter == Controls.Brain.Actions?.Held) continue;

                if (model.TryGetItem(inter.StableId, out var snap) &&
                    snap.location == ItemLocationKind.Free &&
                    model.TryGetDefinition(snap.itemTypeId, out var def) &&
                    def.isContainer)
                {
                    float dist = Vector3.Distance(actorT.position, inter.transform.position);
                    if (dist <= 3.0f)
                    {
                        freeCandidates.Add(inter);
                    }
                }
            }

            // Deterministic sort by StableId
            freeCandidates.Sort((a, b) => string.CompareOrdinal(a.StableId, b.StableId));
            result.AddRange(freeCandidates);
            return result;
        }

        private void ValidateOrUpdateSelection()
        {
            var eligible = GetEligibleContainers();
            if (eligible.Count == 0)
            {
                SelectedContainerId = null;
                SelectedTarget = null;
                return;
            }

            // Check if current selection is still in eligible list
            bool currentValid = false;
            if (!string.IsNullOrEmpty(SelectedContainerId))
            {
                for (int i = 0; i < eligible.Count; i++)
                {
                    if (string.Equals(eligible[i].StableId, SelectedContainerId, StringComparison.Ordinal))
                    {
                        currentValid = true;
                        SelectedTarget = eligible[i];
                        break;
                    }
                }
            }

            if (!currentValid)
            {
                SelectedContainerId = eligible[0].StableId;
                SelectedTarget = eligible[0];
            }
        }

        public void UpdateContent()
        {
            if (!initialized || panelRoot == null) return;

            var model = Bootstrap != null ? Bootstrap.Model : null;

            if (model == null || string.IsNullOrEmpty(SelectedContainerId) ||
                !model.TryGetItem(SelectedContainerId, out var containerSnap) ||
                !model.TryGetDefinition(containerSnap.itemTypeId, out var containerDef) ||
                !containerDef.isContainer)
            {
                if (statusLabel != null) statusLabel.text = model == null ? "Model unavailable" : "No container selected or in reach.";
                if (statsLabel != null) statsLabel.text = "Approach a container and press C or T to inspect.";
                for (int i = 0; i < 4; i++)
                {
                    if (slotTexts[i] != null) slotTexts[i].text = $"Slot {i}\n[ Inactive ]";
                    if (slotButtons[i] != null)
                    {
                        slotButtons[i].interactable = false;
                        var img = slotButtons[i].GetComponent<Image>();
                        if (img != null) img.color = new Color(0.06f, 0.10f, 0.14f, 1f);
                    }
                }
                if (storeButton != null)
                {
                    storeButton.interactable = false;
                    var sImg = storeButton.GetComponent<Image>();
                    if (sImg != null) sImg.color = new Color(0.10f, 0.16f, 0.20f, 1f);
                }
                if (storeButtonText != null) storeButtonText.text = "[ No Container Selected ]";
            }
            else
            {
                string locStr = containerSnap.location == ItemLocationKind.Carried ? "Carried in hands" : "Free on ground";
                Transform actorT = Controls != null && Controls.Brain != null && Controls.Brain.Actor != null
                    ? Controls.Brain.Actor.transform
                    : (Controls != null ? Controls.transform : transform);
                float approachDist = SelectedTarget != null && SelectedTarget.Approach != null
                    ? Vector3.Distance(actorT.position, SelectedTarget.Approach.position)
                    : (SelectedTarget != null ? Vector3.Distance(actorT.position, SelectedTarget.transform.position) : 0f);
                bool inReach = approachDist <= 0.65f;
                string reachStr = inReach ? "In reach" : $"Advisory browse ({approachDist:F2}m > 0.65m reach)";
                if (statusLabel != null) statusLabel.text = $"Container: {SelectedContainerId} ({containerDef.itemTypeId})  |  {locStr}  |  {reachStr}";

                float curMass = model.GetContainerContainedMassKg(SelectedContainerId);
                float curVol = model.GetContainerContainedVolumeM3(SelectedContainerId);
                var occupants = model.GetContainerSlotOccupants(SelectedContainerId);

                if (statsLabel != null)
                    statsLabel.text = $"Mass: {curMass:F2} / {containerDef.maxContainedMassKg:F2} kg   Vol: {curVol:F3} / {containerDef.maxContainedVolumeM3:F3} m³   Slots: {occupants.Count} / {containerDef.maxContainedSlots}";

                // Populate stable 4-slot grid from authoritative model
                for (int s = 0; s < 4; s++)
                {
                    if (slotTexts[s] == null || slotButtons[s] == null) continue;

                    if (s >= containerDef.maxContainedSlots)
                    {
                        slotTexts[s].text = $"Slot {s}\n[ N/A ]";
                        slotButtons[s].interactable = false;
                        var img = slotButtons[s].GetComponent<Image>();
                        if (img != null) img.color = new Color(0.05f, 0.08f, 0.12f, 0.8f);
                        continue;
                    }

                    if (occupants.TryGetValue(s, out string itemId))
                    {
                        string itemType = "item";
                        float itemMass = 0f;
                        if (model.TryGetItem(itemId, out var itemSnap) && model.TryGetDefinition(itemSnap.itemTypeId, out var itemDef))
                        {
                            itemType = itemDef.itemTypeId;
                            itemMass = itemDef.massKg;
                        }

                        // Verify slot consistency with model.TryGetItemContainerSlot
                        model.TryGetItemContainerSlot(itemId, out int verifiedSlot);
                        slotTexts[s].text = $"Slot {s}: {itemId}\n[{itemType}] {itemMass:F1}kg\n(Click to Retrieve)";
                        slotButtons[s].interactable = true;
                        var img = slotButtons[s].GetComponent<Image>();
                        if (img != null) img.color = new Color(0.12f, 0.32f, 0.38f, 1f);
                    }
                    else
                    {
                        // Persistent empty hole
                        slotTexts[s].text = $"Slot {s}\n[ Empty Hole ]";
                        slotButtons[s].interactable = false;
                        var img = slotButtons[s].GetComponent<Image>();
                        if (img != null) img.color = new Color(0.08f, 0.14f, 0.18f, 1f);
                    }
                }

                // Store button state
                if (Controls != null && Controls.Brain != null && Controls.Brain.Actions != null && Controls.Brain.Actions.Held != null)
                {
                    string heldId = Controls.Brain.Actions.Held.StableId;
                    if (storeButtonText != null) storeButtonText.text = $"Store Held Item: {heldId} -> {SelectedContainerId}";
                    if (storeButton != null)
                    {
                        storeButton.interactable = true;
                        var sImg = storeButton.GetComponent<Image>();
                        if (sImg != null) sImg.color = new Color(0.15f, 0.45f, 0.35f, 1f);
                    }
                }
                else
                {
                    if (storeButtonText != null) storeButtonText.text = "[ Hands Empty - Cannot Store ]";
                    if (storeButton != null)
                    {
                        storeButton.interactable = false;
                        var sImg = storeButton.GetComponent<Image>();
                        if (sImg != null) sImg.color = new Color(0.10f, 0.16f, 0.20f, 1f);
                    }
                }
            }

            // ALWAYS update save button and receipt independently of container selection!
            if (saveButton != null && saveButtonText != null)
            {
                saveButton.interactable = (Bootstrap != null);
                if (Bootstrap != null && Bootstrap.SourceSaveRejected)
                {
                    saveButtonText.text = "Save Recovery State";
                    var img = saveButton.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.35f, 0.25f, 0.12f, 1f);
                }
                else
                {
                    saveButtonText.text = "Save Physical World";
                    var img = saveButton.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.18f, 0.32f, 0.22f, 1f);
                }
            }

            if (receiptLabel != null)
            {
                if (string.Equals(LastReceipt, "idle", StringComparison.Ordinal) && Bootstrap != null)
                {
                    if (Bootstrap.SourceSaveRejected)
                    {
                        string recTarget = Bootstrap.GetRecoverySavePath();
                        receiptLabel.text = $"Recovery target: {recTarget}\n(Source rejected: {Bootstrap.RejectedSourcePath})";
                    }
                    else
                    {
                        string saveTarget = !string.IsNullOrEmpty(Bootstrap.PhysicalSavePath)
                            ? Bootstrap.PhysicalSavePath
                            : PhysicalItemBootstrap.GetDefaultSavePath(Bootstrap.Brain != null ? Bootstrap.Brain.InstanceWorldId : null);
                        receiptLabel.text = $"Save target: {saveTarget}";
                    }
                }
                else
                {
                    receiptLabel.text = $"Receipt: {LastReceipt}";
                }
            }
        }

        public Button GetSlotButton(int index) => (index >= 0 && index < slotButtons.Length) ? slotButtons[index] : null;
        public Text GetSlotText(int index) => (index >= 0 && index < slotTexts.Length) ? slotTexts[index] : null;
        public Button StoreButton => storeButton;
        public Text StoreButtonText => storeButtonText;
        public Button SaveButton => saveButton;
        public Text SaveButtonText => saveButtonText;
        public Button CycleButton => cycleButton;
        public Text StatusLabel => statusLabel;
        public Text StatsLabel => statsLabel;
        public Text ReceiptLabel => receiptLabel;

        private void OnSlotClicked(int slotIndex)
        {
            if (Bootstrap == null || Bootstrap.Model == null || string.IsNullOrEmpty(SelectedContainerId) || Controls == null || Controls.Brain == null)
                return;

            var occupants = Bootstrap.Model.GetContainerSlotOccupants(SelectedContainerId);
            if (!occupants.TryGetValue(slotIndex, out string itemId))
            {
                LastReceipt = $"slot-{slotIndex}-empty";
                UpdateContent();
                return;
            }

            var res = Controls.Brain.ExecutePlayerAction(NpcActionKind.Retrieve, itemId, SelectedContainerId);
            LastReceipt = res.success
                ? $"retrieve-success: {itemId} from slot {slotIndex} ({res.code})"
                : $"retrieve-refused: {res.code}";
            UpdateContent();
        }

        private void OnStoreClicked()
        {
            if (Bootstrap == null || Controls == null || Controls.Brain == null || Controls.Brain.Actions == null ||
                Controls.Brain.Actions.Held == null || string.IsNullOrEmpty(SelectedContainerId))
                return;

            string heldId = Controls.Brain.Actions.Held.StableId;
            var res = Controls.Brain.ExecutePlayerAction(NpcActionKind.Store, heldId, SelectedContainerId);
            LastReceipt = res.success
                ? $"store-success: {heldId} into {SelectedContainerId} ({res.code})"
                : $"store-refused: {res.code}";
            UpdateContent();
        }

        private void OnSaveClicked()
        {
            if (Bootstrap == null)
            {
                LastReceipt = "save-failed: bootstrap-absent";
                UpdateContent();
                return;
            }

            if (Bootstrap.SourceSaveRejected)
            {
                bool ok = Bootstrap.SaveRecoveryState(out string recoveryPath);
                LastReceipt = ok
                    ? $"recovery-saved-success: {recoveryPath}"
                    : $"recovery-saved-failed: {Bootstrap.LastSaveError ?? "refused"}";
            }
            else
            {
                bool ok = Bootstrap.SaveCurrentState();
                LastReceipt = ok
                    ? $"saved-state-success: {Bootstrap.PhysicalSavePath}"
                    : $"save-failed: {Bootstrap.LastSaveError ?? "refused"}";
            }
            UpdateContent();
        }
    }
}
