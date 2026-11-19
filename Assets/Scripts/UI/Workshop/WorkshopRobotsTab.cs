using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// ROBOTS tab of a factory's Workshop (Gatherer Factory, Drone Bay): every robot this building made, with commands.
/// Two tab buttons in the Workshop's top bar (UPGRADES | ROBOTS n) switch the right-hand panel between the upgrade
/// tree and this list. Tap a robot to select it, then:
/// - RECALL / RESUME: stop working and wait at its spot by the factory / back to work;
/// - UPDATE: walk to the factory door, wait BotFactory.updateTime (5 s), get the factory's current level, perks and
///   battery (never a smaller battery) and its Master Disk if it has no disk;
/// - DESTROY: scrap for 50 % (tap twice).
/// Built in code the first time the Workshop opens for a factory (no builder step). Hidden for the player's tree.
public class WorkshopRobotsTab : MonoBehaviour
{
    WorkshopScreen ws;
    BotFactory factory;
    bool built, showing;

    Button upgradesTab, robotsTab;
    TMP_Text robotsTabLabel;
    GameObject panel;
    TMP_Text headerText, infoText, emptyText, detailTitle, detailBody;
    Button updateAllButton, recallButton, updateButton, destroyButton;
    TMP_Text updateAllLabel, recallLabel, updateLabel, destroyLabel;
    RectTransform listContent;
    GameObject detailBox;
    readonly List<Row> rows = new List<Row>();
    List<ScoutBot> robots = new List<ScoutBot>();
    ScoutBot selected;
    float refreshT, armT;
    bool armed;

    class Row
    {
        public GameObject go;
        public Image bg, stripe;
        public TMP_Text name, level, status, right;
        public ScoutBot bot;
    }

    public bool Showing => showing;

    /// The tab component on the Workshop (created on first use).
    public static WorkshopRobotsTab For(WorkshopScreen ws)
    {
        var t = ws.GetComponent<WorkshopRobotsTab>();
        if (t == null) { t = ws.gameObject.AddComponent<WorkshopRobotsTab>(); t.ws = ws; }
        return t;
    }

    /// Called by WorkshopScreen.Open: factory = null hides the tabs (player tree).
    public void Setup(BotFactory f)
    {
        factory = f;
        if (f != null && !built) Build();
        if (!built) return;
        upgradesTab.gameObject.SetActive(f != null);
        robotsTab.gameObject.SetActive(f != null);
        selected = null;
        armed = false;
        if (f != null)
            foreach (var t in ws.GetComponentsInChildren<TMP_Text>(true))
                if (t.transform.name == "Footnote") t.text = "For new " + f.UnitWord.ToLowerInvariant() + "s  ·  older ones: ROBOTS tab";
        Show(false);
    }

    public void Show(bool robotsOn)
    {
        showing = robotsOn && factory != null;
        if (!built) return;
        panel.SetActive(showing);
        if (showing) { ws.HidePicker(); panel.transform.SetAsLastSibling(); }
        Paint(upgradesTab, !showing);
        Paint(robotsTab, showing);
        refreshT = 0f;
        Refresh();
    }

    static void Paint(Button b, bool on)
    {
        b.targetGraphic.color = on ? WorkshopTheme.Orange : WorkshopTheme.ButtonOff;
        var l = b.GetComponentInChildren<TMP_Text>();
        if (l != null) l.color = on ? WorkshopTheme.DarkText : WorkshopTheme.White;
    }

    void Update()
    {
        if (!built || factory == null) return;
        refreshT -= Time.unscaledDeltaTime;
        if (refreshT > 0f) return;
        refreshT = 0.25f;
        Refresh();
        if (armed && Time.unscaledTime > armT) { armed = false; }
    }

    // ------------------------------------------------------------------ refresh

    void Refresh()
    {
        if (factory == null || !built) return;
        robots = factory.Robots();
        int outdated = 0;
        foreach (var b in robots) if (b.NeedsUpdate && b.CurrentOrder != ScoutBot.Order.Update) outdated++;
        robotsTabLabel.text = "ROBOTS  " + robots.Count + (outdated > 0 ? "  <color=" + (showing ? "#221B13" : WorkshopTheme.Hex(WorkshopTheme.Orange)) + ">!</color>" : "");
        if (!showing) return;

        var up = factory.Upgrades;
        string unit = factory.UnitWord.ToLowerInvariant();
        headerText.text = robots.Count + " " + factory.UnitWord + (robots.Count == 1 ? "" : "S") + " BUILT HERE";
        string makes = "Builds LV " + (up != null ? up.Level : 1);
        if (up != null && up.equippedBattery != null) makes += "  ·  <color=" + WorkshopTheme.Hex(up.equippedBattery.Color) + ">" + up.equippedBattery.displayName + "</color>";
        if (up != null && up.AutoProgram != null) makes += "  ·  disk <color=" + WorkshopTheme.Hex(up.AutoProgram.color) + ">" + up.AutoProgram.shortName + "</color>";
        infoText.text = makes + "  ·  update " + factory.updateTime.ToString("0.#") + " s";
        updateAllLabel.text = outdated > 0 ? "UPDATE ALL (" + outdated + ")" : "ALL UP TO DATE";
        updateAllButton.interactable = outdated > 0;
        emptyText.gameObject.SetActive(robots.Count == 0);
        emptyText.text = "No " + unit + "s yet.\nBuild some with BUILD " + factory.UnitWord + "S at the " + factory.idleText + ".";

        if (selected != null && !robots.Contains(selected)) selected = null;
        while (rows.Count < robots.Count) rows.Add(MakeRow());
        for (int i = 0; i < rows.Count; i++)
        {
            bool on = i < robots.Count;
            rows[i].go.SetActive(on);
            if (on) PaintRow(rows[i], robots[i], up);
        }
        RefreshDetail(up);
    }

    void PaintRow(Row r, ScoutBot b, BuildingUpgrades up)
    {
        r.bot = b;
        bool sel = b == selected;
        r.bg.color = sel ? new Color32(70, 58, 40, 255) : WorkshopTheme.PanelDark;
        Color state = StateColor(b);
        r.stripe.color = sel ? WorkshopTheme.Orange : state;
        r.name.text = b.BotName.ToUpper();
        int target = up != null ? up.Level : b.Level;
        r.level.text = b.NeedsUpdate && b.Level != target
            ? "LV " + b.Level + " <color=" + WorkshopTheme.Hex(WorkshopTheme.Orange) + ">> " + target + "</color>"
            : "LV " + b.Level;
        r.status.text = ShortState(b);
        r.status.color = state;
        string badge = b.IsUpdating ? "<color=#6FD3EE>UPDATING " + Mathf.CeilToInt(b.UpdateTimeLeft) + "s</color>"
                     : b.CurrentOrder == ScoutBot.Order.Update ? "<color=#6FD3EE>ON ITS WAY</color>"
                     : b.NeedsUpdate ? "<color=" + WorkshopTheme.Hex(WorkshopTheme.Orange) + ">UPDATE !</color>" : "";
        r.right.text = Mathf.RoundToInt(b.Battery01 * 100f) + "%" + (badge != "" ? "\n" + badge : "");
    }

    static Color StateColor(ScoutBot b)
    {
        if (b.IsDown) return WorkshopTheme.Red;
        if (b.CurrentOrder == ScoutBot.Order.Update) return WorkshopTheme.Cyan;
        if (b.CurrentOrder == ScoutBot.Order.Recall) return WorkshopTheme.Yellow;
        if (b.SeekingCharge) return WorkshopTheme.Cyan;
        if (b.Program == null && !b.Following) return WorkshopTheme.Muted;
        return WorkshopTheme.Green;
    }

    static string ShortState(ScoutBot b)
    {
        if (b.IsBroken) return "BROKEN";
        if (b.IsDead) return "NO POWER";
        if (b.CurrentOrder == ScoutBot.Order.Recall) return "RECALLED";
        if (b.CurrentOrder == ScoutBot.Order.Update) return b.IsUpdating ? "UPDATING" : "GOING FOR UPDATE";
        if (b.IsCharging) return "CHARGING";
        if (b.SeekingCharge) return "TO CHARGER";
        if (b.Following) return "FOLLOWING YOU";
        if (b.Program == null) return "NO DISK  ·  IDLE";
        return b.Program.displayName.ToUpper() + (b.ProgramFromMasterDisk ? "  (MASTER)" : "");
    }

    void RefreshDetail(BuildingUpgrades up)
    {
        detailBox.SetActive(robots.Count > 0);
        bool any = selected != null;
        recallButton.gameObject.SetActive(any);
        updateButton.gameObject.SetActive(any);
        destroyButton.gameObject.SetActive(any);
        if (!any)
        {
            detailTitle.text = "TAP A " + factory.UnitWord;
            detailBody.text = "<color=" + WorkshopTheme.Hex(WorkshopTheme.Muted) + ">RECALL it to the " + factory.idleText.ToLowerInvariant() + ", UPDATE it to the factory's current level, " +
                              "perks and battery (+ the Master Disk if it has no disk), or DESTROY it for half its cost.</color>";
            return;
        }
        var b = selected;
        string orange = WorkshopTheme.Hex(WorkshopTheme.Orange), green = WorkshopTheme.Hex(WorkshopTheme.Green), muted = WorkshopTheme.Hex(WorkshopTheme.Muted);
        detailTitle.text = b.BotName.ToUpper() + "   <color=" + orange + ">LV " + b.Level + "</color>";

        var sb = new System.Text.StringBuilder();
        sb.Append(b.Status).Append('\n');
        sb.Append("<color=").Append(muted).Append(">Battery</color> ").Append(Mathf.RoundToInt(b.Battery01 * 100f)).Append("%  of ").Append(Stats.Clock(b.batteryRuntime));
        sb.Append("   <color=").Append(muted).Append(">Disk</color> ").Append(b.Program != null ? b.Program.shortName : "none").Append('\n');
        if (up != null && b.NeedsUpdate)
        {
            float newRun = Mathf.Max(b.batteryRuntime, up.Current[StatType.BatteryRuntime]);
            sb.Append("<color=").Append(green).Append(">UPDATE: LV ").Append(b.Level).Append(" > ").Append(up.Level)
              .Append("  ·  battery ").Append(Stats.Clock(newRun));
            if (b.Program == null && up.AutoProgram != null && up.AutoProgram.FitsOn(b.robotType)) sb.Append("  ·  +").Append(up.AutoProgram.shortName).Append(" disk");
            sb.Append("</color>");
        }
        else sb.Append("<color=").Append(muted).Append(">Up to date with the factory</color>");
        detailBody.text = sb.ToString();

        bool down = b.IsDown;
        // RECALL / RESUME
        bool recalled = b.CurrentOrder == ScoutBot.Order.Recall;
        recallLabel.text = recalled ? "RESUME" : b.CurrentOrder == ScoutBot.Order.Update ? "CANCEL" : "RECALL";
        recallButton.interactable = !down;
        // UPDATE
        if (b.IsUpdating) { updateLabel.text = "UPDATING\n<size=70%>" + Mathf.CeilToInt(b.UpdateTimeLeft) + " s</size>"; updateButton.interactable = false; }
        else if (b.CurrentOrder == ScoutBot.Order.Update) { updateLabel.text = "ON ITS WAY"; updateButton.interactable = false; }
        else if (down) { updateLabel.text = "DOWN"; updateButton.interactable = false; }
        else if (!b.NeedsUpdate) { updateLabel.text = "UP TO DATE"; updateButton.interactable = false; }
        else { updateLabel.text = "UPDATE\n<size=70%>LV " + b.Level + " > " + (up != null ? up.Level : b.Level) + "</size>"; updateButton.interactable = true; }
        ((Image)updateButton.targetGraphic).color = updateButton.interactable ? WorkshopTheme.ButtonGreen : WorkshopTheme.ButtonOff;
        // DESTROY
        destroyLabel.text = armed ? "TAP AGAIN\n<size=70%>+" + b.ScrapRefund + " MATS</size>" : "DESTROY";
    }

    // ------------------------------------------------------------------ actions

    void Select(ScoutBot b)
    {
        selected = b;
        armed = false;
        Refresh();
    }

    void OnRecall()
    {
        var b = selected;
        if (b == null) return;
        if (b.CurrentOrder != ScoutBot.Order.None) { b.ClearOrder(); GameToast.Show(b.BotName.ToUpper() + " BACK TO WORK", WorkshopTheme.Green); }
        else if (b.Recall()) GameToast.Show(b.BotName.ToUpper() + " RECALLED", WorkshopTheme.Yellow);
        Refresh();
    }

    void OnUpdate()
    {
        var b = selected;
        if (b == null) return;
        if (b.RequestUpdate()) GameToast.Show(b.BotName.ToUpper() + " IS COMING FOR AN UPDATE", WorkshopTheme.Cyan);
        Refresh();
    }

    void OnUpdateAll()
    {
        int n = 0;
        foreach (var b in factory.Robots())
            if (b.NeedsUpdate && b.CurrentOrder != ScoutBot.Order.Update && b.RequestUpdate()) n++;
        if (n > 0) GameToast.Show(n + " " + factory.UnitWord + (n == 1 ? "" : "S") + " COMING FOR AN UPDATE", WorkshopTheme.Cyan);
        Refresh();
    }

    void OnDestroyTap()
    {
        var b = selected;
        if (b == null) return;
        if (!armed) { armed = true; armT = Time.unscaledTime + 3f; Refresh(); return; }
        armed = false;
        selected = null;
        b.Scrap();
        Refresh();
    }

    // ------------------------------------------------------------------ build (once, inside the Workshop's Content)

    void Build()
    {
        built = true;
        var content = ws.transform.Find("Content") as RectTransform;
        var top = content != null ? content.Find("TopBar") as RectTransform : null;
        if (content == null || top == null) { Debug.LogWarning("WorkshopRobotsTab: Workshop layout not found"); built = false; return; }

        // tabs in the top bar
        upgradesTab = Btn("Tab Upgrades", top, "UPGRADES", 28, new Vector2(0.5f, 0.5f), new Vector2(-620f, 0f), new Vector2(240f, 62f));
        robotsTab = Btn("Tab Robots", top, "ROBOTS", 28, new Vector2(0.5f, 0.5f), new Vector2(-365f, 0f), new Vector2(240f, 62f));
        robotsTabLabel = robotsTab.GetComponentInChildren<TMP_Text>();
        upgradesTab.onClick.AddListener(() => Show(false));
        robotsTab.onClick.AddListener(() => Show(true));

        // panel over the tree (same rect as the battery picker)
        var p = Rect("RobotsPanel", content, 605f, 104f, 670f, 956f);
        Img(p, WorkshopTheme.Panel, true);
        panel = p.gameObject;
        headerText = Txt("Title", p, -100f, 10f, 430f, 54f, "ROBOTS", 34, WorkshopTheme.White, TextAlignmentOptions.Left);
        headerText.enableAutoSizing = true; headerText.fontSizeMin = 20; headerText.fontSizeMax = 34;
        updateAllButton = Btn("UpdateAll", p, "UPDATE ALL", 22, new Vector2(0.5f, 1f), new Vector2(222f, -37f), new Vector2(210f, 54f));
        updateAllLabel = updateAllButton.GetComponentInChildren<TMP_Text>();
        ((Image)updateAllButton.targetGraphic).color = WorkshopTheme.ButtonGreen;
        updateAllButton.onClick.AddListener(OnUpdateAll);
        infoText = Txt("Info", p, 0f, 68f, 630f, 34f, "", 21, WorkshopTheme.Muted, TextAlignmentOptions.Left, FontStyles.Normal);
        infoText.enableAutoSizing = true; infoText.fontSizeMin = 15; infoText.fontSizeMax = 21;
        Img(Rect("Rule", p, 0f, 108f, 640f, 4f), WorkshopTheme.Orange, false);

        // scrolling list
        var view = Rect("List View", p, 0f, 122f, 650f, 560f);
        view.gameObject.AddComponent<RectMask2D>();
        Img(view, new Color(0f, 0f, 0f, 0f), true);
        listContent = NewRect("List", view);
        listContent.anchorMin = new Vector2(0f, 1f); listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.anchoredPosition = Vector2.zero;
        listContent.sizeDelta = Vector2.zero;
        var vl = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 8f;
        vl.padding = new RectOffset(5, 5, 0, 6);
        vl.childControlWidth = true; vl.childControlHeight = false;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        var fit = listContent.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var sr = view.gameObject.AddComponent<ScrollRect>();
        sr.viewport = view; sr.content = listContent;
        sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.decelerationRate = 0.1f; sr.scrollSensitivity = 30f;
        emptyText = Txt("Empty", p, 0f, 300f, 600f, 110f, "", 26, WorkshopTheme.Muted, TextAlignmentOptions.Center, FontStyles.Normal);

        // selected robot + commands
        var d = Rect("Selected", p, 0f, 696f, 650f, 250f);
        Img(d, WorkshopTheme.PanelDark, false);
        detailBox = d.gameObject;
        Img(Rect("Rule", d, -322f, 0f, 6f, 250f), WorkshopTheme.Orange, false);
        detailTitle = Txt("Name", d, 5f, 10f, 600f, 40f, "", 28, WorkshopTheme.White, TextAlignmentOptions.Left);
        detailBody = Txt("Body", d, 5f, 50f, 600f, 86f, "", 20, WorkshopTheme.White, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        detailBody.enableAutoSizing = true; detailBody.fontSizeMin = 14; detailBody.fontSizeMax = 20;
        recallButton = Btn("Recall", d, "RECALL", 28, new Vector2(0.5f, 1f), new Vector2(-207f, -192f), new Vector2(196f, 92f));
        updateButton = Btn("Update", d, "UPDATE", 28, new Vector2(0.5f, 1f), new Vector2(3f, -192f), new Vector2(196f, 92f));
        destroyButton = Btn("Destroy", d, "DESTROY", 28, new Vector2(0.5f, 1f), new Vector2(213f, -192f), new Vector2(196f, 92f));
        ((Image)destroyButton.targetGraphic).color = new Color32(150, 52, 40, 255);
        recallLabel = recallButton.GetComponentInChildren<TMP_Text>();
        updateLabel = updateButton.GetComponentInChildren<TMP_Text>();
        destroyLabel = destroyButton.GetComponentInChildren<TMP_Text>();
        foreach (var l in new[] { recallLabel, updateLabel, destroyLabel }) l.lineSpacing = -18f;
        recallButton.onClick.AddListener(OnRecall);
        updateButton.onClick.AddListener(OnUpdate);
        destroyButton.onClick.AddListener(OnDestroyTap);

        panel.SetActive(false);
    }

    Row MakeRow()
    {
        var r = new Row();
        var rt = NewRect("Robot", listContent);
        rt.sizeDelta = new Vector2(0f, 84f);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 84f; le.minHeight = 84f;
        r.go = rt.gameObject;
        r.bg = Img(rt, WorkshopTheme.PanelDark, true);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = r.bg;
        var cb = btn.colors; cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); btn.colors = cb;
        btn.onClick.AddListener(() => Select(r.bot));

        var stripe = NewRect("Stripe", rt);
        stripe.anchorMin = new Vector2(0f, 0f); stripe.anchorMax = new Vector2(0f, 1f);
        stripe.pivot = new Vector2(0f, 0.5f);
        stripe.offsetMin = Vector2.zero; stripe.offsetMax = new Vector2(8f, 0f);
        r.stripe = Img(stripe, WorkshopTheme.Green, false);

        r.name = Anchored("Name", rt, new Vector2(24f, -8f), new Vector2(330f, 36f), 24, WorkshopTheme.White, TextAlignmentOptions.Left, FontStyles.Bold);
        r.name.enableAutoSizing = true; r.name.fontSizeMin = 16; r.name.fontSizeMax = 24;
        r.level = Anchored("Level", rt, new Vector2(360f, -8f), new Vector2(120f, 36f), 22, WorkshopTheme.White, TextAlignmentOptions.Left, FontStyles.Bold);
        r.status = Anchored("Status", rt, new Vector2(24f, -46f), new Vector2(450f, 30f), 18, WorkshopTheme.Green, TextAlignmentOptions.Left, FontStyles.Normal);
        r.status.enableAutoSizing = true; r.status.fontSizeMin = 12; r.status.fontSizeMax = 18;
        r.right = Anchored("Right", rt, new Vector2(486f, -8f), new Vector2(140f, 70f), 20, WorkshopTheme.White, TextAlignmentOptions.TopRight, FontStyles.Bold);
        r.right.enableAutoSizing = true; r.right.fontSizeMin = 13; r.right.fontSizeMax = 20;
        return r;
    }

    // ------------------------------------------------------------------ helpers (x = centre, yTop = from the parent's top)

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static RectTransform Rect(string name, Transform parent, float x, float yTop, float w, float h)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, -(yTop + h * 0.5f));
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    static Image Img(RectTransform rt, Color c, bool raycast)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    static TextMeshProUGUI Text(RectTransform rt, string text, float size, Color c, TextAlignmentOptions align, FontStyles style)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = c; t.fontStyle = style;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    static TextMeshProUGUI Txt(string name, Transform parent, float x, float yTop, float w, float h, string text, float size, Color c,
                               TextAlignmentOptions align, FontStyles style = FontStyles.Bold)
        => Text(Rect(name, parent, x, yTop, w, h), text, size, c, align, style);

    // top-left anchored text inside a list row
    static TextMeshProUGUI Anchored(string name, Transform parent, Vector2 topLeft, Vector2 size, float fontSize, Color c, TextAlignmentOptions align, FontStyles style)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = topLeft;
        rt.sizeDelta = size;
        return Text(rt, "", fontSize, c, align, style);
    }

    static Button Btn(string name, Transform parent, string label, float fontSize, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = Img(rt, WorkshopTheme.ButtonOff, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.pressedColor = new Color(0.78f, 0.78f, 0.78f);
        cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.75f);
        b.colors = cb;
        var lt = NewRect("Label", rt);
        lt.anchorMin = Vector2.zero; lt.anchorMax = Vector2.one;
        lt.offsetMin = new Vector2(4f, 4f); lt.offsetMax = new Vector2(-4f, -4f);
        Text(lt, label, fontSize, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        return b;
    }
}
