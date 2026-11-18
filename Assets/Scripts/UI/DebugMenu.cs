using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// In-game debug panel (Editor + development builds only). Creates itself on scene load:
/// a small DEBUG button top-left that opens a panel of test shortcuts.
/// Add a button: one Btn(...) line in BuildPanel().
public class DebugMenu : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;
        if (FindFirstObjectByType<DebugMenu>() != null) return;
        var canvas = GameObject.Find("UI Canvas");
        if (canvas == null) return;
        var go = new GameObject("DebugMenu", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<DebugMenu>();
    }

    static readonly Color PanelCol = new Color32(26, 22, 17, 240);
    static readonly Color BtnCol = new Color32(70, 62, 50, 255);
    static readonly Color Accent = new Color32(240, 160, 46, 255);
    static readonly Color Danger = new Color32(170, 70, 55, 255);

    GameObject openButton, panel;
    TMP_Text info, speedLabel;
    readonly float[] speeds = { 1f, 2f, 4f, 8f };
    int speedIndex;
    float infoT;
    Sprite round;

    void Start()
    {
        round = null;   // plain rectangles (the built-in UI sprite isn't loadable in device builds)
        BuildOpenButton();
        BuildPanel();
        panel.SetActive(false);
    }

    // ------------------------------------------------------------------ UI

    void BuildOpenButton()
    {
        var b = Btn("DEBUG", transform, () => Show(true), Accent, new Color32(34, 27, 19, 255));
        openButton = b.gameObject;
        var rt = (RectTransform)b.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(24f, -246f);   // under the stats panel
        rt.sizeDelta = new Vector2(150f, 62f);
    }

    void BuildPanel()
    {
        var p = NewRect("DebugPanel", transform);
        p.anchorMin = p.anchorMax = p.pivot = new Vector2(0f, 1f);
        p.anchoredPosition = new Vector2(16f, -100f);
        p.sizeDelta = new Vector2(540f, 0f);
        var bg = p.gameObject.AddComponent<Image>();
        bg.sprite = round; bg.type = Image.Type.Sliced; bg.color = PanelCol;
        var vl = p.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(16, 16, 12, 16);
        vl.spacing = 6;
        vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        var fit = p.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        panel = p.gameObject;

        // title row
        var title = NewRect("Title", p);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
        var tt = Label(title, "DEBUG", 34, Accent, TextAlignmentOptions.Left);
        var close = Btn("X", title, () => Show(false), Danger, Color.white);
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(56f, 50f);
        _ = tt;

        var infoRt = NewRect("Info", p);
        infoRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
        info = Label(infoRt, "", 20, new Color(0.9f, 0.88f, 0.82f), TextAlignmentOptions.TopLeft);
        info.fontStyle = FontStyles.Normal;

        var g = Section(p, "ECONOMY");
        Btn("+100 MATS", g, () => AddMats(100));
        Btn("+1000 MATS", g, () => AddMats(1000));
        Btn("ZERO MATS", g, () => { var e = Economy.Instance; if (e != null) e.TrySpend(e.materials); }, Danger);

        g = Section(p, "FACTORY");
        Btn("SPAWN 1 BOT", g, () => Spawn(1, false));
        Btn("SPAWN 5 BOTS", g, () => Spawn(5, false));
        Btn("5 + PROGRAM", g, () => Spawn(5, true));
        Btn("LEVEL +1", g, () => WithUpgrades(u => u.DevSetLevel(u.Level + 1)));
        Btn("MAX LEVEL", g, () => WithUpgrades(u => u.DevSetLevel(u.MaxLevel)));
        Btn("RESET TREE", g, () => WithUpgrades(u => u.DevReset()), Danger);

        g = Section(p, "BOTS");
        Btn("GIVE DISK", g, GiveDisk);
        Btn("PROGRAM ALL", g, ProgramAll);
        Btn("BATTERY 100%", g, () => SetBattery(1f));
        Btn("BATTERY 20%", g, () => SetBattery(0.2f));
        Btn("BATTERY 0%", g, () => SetBattery(0f), Danger);
        Btn("DELETE BOTS", g, DeleteBots, Danger);

        g = Section(p, "BATTERIES");
        Btn("GIVE COMMON", g, () => GiveBattery(Rarity.Common));
        Btn("GIVE RARE", g, () => GiveBattery(Rarity.Rare));
        Btn("GIVE 1 EACH", g, () => { foreach (Rarity r in System.Enum.GetValues(typeof(Rarity))) GiveBattery(r); });

        g = Section(p, "WORLD");
        speedLabel = Btn("SPEED x1", g, CycleSpeed).GetComponentInChildren<TMP_Text>();
        Btn("TP FACTORY", g, () => Teleport(Fac() != null ? Fac().transform : null, -4f));
        Btn("TP CHARGER", g, () => Teleport(ChargingStation.All.Count > 0 ? ChargingStation.All[0].transform : null, 4f));
        Btn("TP RECYCLER", g, () => Teleport(HomeBase.All.Count > 0 ? HomeBase.All[0].transform : null, -4.5f));
        TMP_Text freeLabel = null, fogLabel = null, pipeLabel = null;
        pipeLabel = Btn("PIPE: ON", g, () => { PipeTether.Disabled = !PipeTether.Disabled; pipeLabel.text = PipeTether.Disabled ? "PIPE: OFF" : "PIPE: ON"; }).GetComponentInChildren<TMP_Text>();
        Btn("PIPE LV +1", g, () => { var t = PipeTether.Instance; if (t != null) t.SetLevel(t.level + 1); });
        Btn("PIPE LV 1", g, () => { var t = PipeTether.Instance; if (t != null) t.SetLevel(1); }, Danger);
        freeLabel = Btn("FREE BUILD: OFF", g, () => { BuildMode.FreeBuild = !BuildMode.FreeBuild; freeLabel.text = BuildMode.FreeBuild ? "FREE BUILD: ON" : "FREE BUILD: OFF"; }).GetComponentInChildren<TMP_Text>();
        fogLabel = Btn("REVEAL MAP: OFF", g, () =>
        {
            var fog = FogOfWar.Instance;
            if (fog == null) return;
            fog.revealAll = !fog.revealAll;
            fog.Refresh();
            fogLabel.text = fog.revealAll ? "REVEAL MAP: ON" : "REVEAL MAP: OFF";
        }).GetComponentInChildren<TMP_Text>();
    }

    Transform Section(RectTransform parent, string name)
    {
        var h = NewRect(name, parent);
        h.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;
        Label(h, name, 22, Accent, TextAlignmentOptions.BottomLeft);

        var grid = NewRect(name + " Buttons", parent);
        var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(164f, 52f);
        gl.spacing = new Vector2(8f, 8f);
        gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gl.constraintCount = 3;
        return grid;
    }

    Button Btn(string text, Transform parent, Action onClick, Color? bg = null, Color? fg = null)
    {
        var rt = NewRect(text, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = round; img.type = Image.Type.Sliced; img.color = bg ?? BtnCol;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(() => onClick());
        var l = NewRect("Label", rt);
        l.anchorMin = Vector2.zero; l.anchorMax = Vector2.one; l.offsetMin = new Vector2(4, 2); l.offsetMax = new Vector2(-4, -2);
        var t = Label(l, text, 22, fg ?? Color.white, TextAlignmentOptions.Center);
        t.enableAutoSizing = true; t.fontSizeMin = 14; t.fontSizeMax = 22;
        return b;
    }

    static TMP_Text Label(RectTransform rt, string text, float size, Color c, TextAlignmentOptions align)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = c; t.alignment = align;
        t.fontStyle = FontStyles.Bold; t.raycastTarget = false;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    void Show(bool on)
    {
        panel.SetActive(on);
        openButton.SetActive(!on);
        transform.SetAsLastSibling();   // above popups / Workshop
        infoT = 0f;
    }

    // ------------------------------------------------------------------ actions

    /// The factory nearest to the player (there can be several).
    static BotFactory Fac()
    {
        var p = GameObject.FindWithTag("Player");
        return p != null ? BotFactory.Nearest(p.transform.position) : (BotFactory.All.Count > 0 ? BotFactory.All[0] : null);
    }

    static void AddMats(int n) { if (Economy.Instance != null) Economy.Instance.AddMaterials(n); }

    static void WithUpgrades(Action<BuildingUpgrades> a)
    {
        var f = Fac();
        if (f != null && f.Upgrades != null) a(f.Upgrades);
    }

    static DiskProgram FirstProgram()
    {
        foreach (var d in FindObjectsByType<DiskPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (d.program != null) return d.program;
        var inv = PlayerInventory.Instance;
        return inv != null && inv.disks.Count > 0 ? inv.disks[0] : null;
    }

    static void Spawn(int n, bool program)
    {
        var f = Fac();
        if (f == null) return;
        var prog = program ? FirstProgram() : null;
        int before = ScoutBot.All.Count;
        for (int i = 0; i < n; i++) f.DevSpawnNow();
        if (prog != null)
            for (int i = before; i < ScoutBot.All.Count; i++) ScoutBot.All[i].SetProgram(prog);
    }

    static void GiveDisk()
    {
        var prog = FirstProgram();
        if (prog != null && PlayerInventory.Instance != null) PlayerInventory.Instance.Add(prog);
    }

    static void GiveBattery(Rarity r)
    {
        var inv = PlayerInventory.Instance;
        if (inv == null) return;
        foreach (var b in BatteryItem.LoadAll())
            if (b.rarity == r) { if (!inv.AddBattery(b)) GameToast.Show("BATTERY SLOTS FULL", new Color(1f, 0.4f, 0.32f)); return; }
    }

    static void ProgramAll()
    {
        var prog = FirstProgram();
        if (prog == null) return;
        foreach (var b in ScoutBot.All) if (b.Program == null) b.SetProgram(prog);
    }

    static void SetBattery(float f)
    {
        foreach (var b in ScoutBot.All) b.SetBattery01(f);
    }

    static void DeleteBots()
    {
        var list = new List<ScoutBot>(ScoutBot.All);
        foreach (var b in list) Destroy(b.gameObject);
    }

    void CycleSpeed()
    {
        speedIndex = (speedIndex + 1) % speeds.Length;
        Time.timeScale = speeds[speedIndex];
        speedLabel.text = "SPEED x" + speeds[speedIndex];
    }

    static void Teleport(Transform target, float zOffset)
    {
        var p = GameObject.FindWithTag("Player");
        if (p == null || target == null) return;
        var cc = p.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        var pos = target.position + new Vector3(0f, 0f, zOffset);
        pos.y = GroundUtil.Height(pos, pos.y) + 0.1f;
        p.transform.position = pos;
        if (cc != null) cc.enabled = true;
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
    }

    // ------------------------------------------------------------------ info line

    void Update()
    {
        if (panel == null || !panel.activeSelf) return;
        infoT -= Time.unscaledDeltaTime;
        if (infoT > 0f) return;
        infoT = 0.4f;

        int total = ScoutBot.All.Count, dead = 0, charging = 0, idle = 0;
        foreach (var b in ScoutBot.All)
        {
            if (b.IsDead) dead++;
            else if (b.SeekingCharge) charging++;
            else if (b.Program == null) idle++;
        }
        int queue = 0;
        foreach (var s in ChargingStation.All) queue += s.Queued;
        var f = Fac();
        string fac = f != null ? "Factory Lv " + (f.Upgrades != null ? f.Upgrades.Level : 1) + "  ·  build " + f.BuildTime.ToString("0.#") + "s  ·  queued " + f.Queued : "";
        info.text = "Bots " + total + "  ·  no disk " + idle + "  ·  charging " + charging + "  ·  queue " + queue + "  ·  dead " + dead +
                    "\n" + fac + "  ·  " + Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)) + " fps";
    }
}
