using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// Debug panel for the PlayerLab scene only (it replaces the normal DebugMenu there): live -/+ controls for every
/// number that decides how the player FEELS - pickaxe, suction gun, movement - plus one-tap progression stages
/// (1 SHOVEL / 2 FIRST GUN / 3 PRO GUN) to compare the start of the game with the end.
/// Changes are for the running session only; LOG VALUES prints them to the Console so they can be copied
/// into the Inspector. Add a control with one Stat(...) / Toggle(...) / Btn(...) line in a Build...Tab().
public class PlayerLabDebug : MonoBehaviour
{
    public const string SceneName = "PlayerLab";
    public static bool IsLabScene => SceneManager.GetActiveScene().name == SceneName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;
        if (!IsLabScene || FindFirstObjectByType<PlayerLabDebug>() != null) return;
        var canvas = GameObject.Find("UI Canvas");
        if (canvas == null) return;
        var go = new GameObject("PlayerLabDebug", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<PlayerLabDebug>();
    }

    static readonly Color PanelCol = new Color32(26, 22, 17, 240);
    static readonly Color BtnCol = new Color32(70, 62, 50, 255);
    static readonly Color Accent = new Color32(240, 160, 46, 255);
    static readonly Color Danger = new Color32(170, 70, 55, 255);
    static readonly Color OnCol = new Color32(70, 130, 70, 255);
    static readonly Color Dark = new Color32(34, 27, 19, 255);

    // ------------------------------------------------------------------ progression stages (first guesses, tune freely)

    // sandTool: 0 = shovel, 1 = suction gun
    static readonly string[] StageNames = { "1 SHOVEL", "2 FIRST GUN", "3 PRO GUN" };
    static readonly Dictionary<string, float>[] Stages =
    {
        // the start: everything by hand, every bite and every hit is work
        new Dictionary<string, float> { { "sandTool", 0 }, { "bite", 1.0f }, { "digReach", 1.5f }, { "digSpeed", 0.9f }, { "digMove", 0.25f },
            { "damage", 2 }, { "attackSpeed", 0.8f }, { "reach", 0.9f }, { "capacity", 20 }, { "moveSpeed", 3.8f } },
        // first gun: no more swinging at sand and he never crawls; it is weak in width, range and tank, not in pace
        new Dictionary<string, float> { { "sandTool", 1 }, { "gunPower", 1 }, { "gunRange", 2.6f }, { "gunAngle", 24 }, { "suckRate", 1.8f }, { "mouthPower", 2.5f },
            { "suckMove", 0.8f }, { "capacity", 40 }, { "damage", 3 }, { "attackSpeed", 1.0f }, { "reach", 1.0f }, { "moveSpeed", 4.0f } },
        // upgraded gun: wide cone, runs through sand at full speed
        new Dictionary<string, float> { { "sandTool", 1 }, { "gunPower", 2 }, { "gunRange", 5.0f }, { "gunAngle", 42 }, { "suckRate", 3.2f }, { "mouthPower", 5.0f },
            { "suckMove", 1.0f }, { "capacity", 150 }, { "damage", 9 }, { "attackSpeed", 1.4f }, { "reach", 1.3f }, { "moveSpeed", 5.0f } },
    };

    class StatRow
    {
        public string key, name, fmt;
        public Func<float> get;
        public Action<float> set, setRaw;   // set snaps to the step (the -/+ buttons), setRaw only clamps (stages, reset)
        public float initial;
        public TMP_Text value;
    }

    readonly List<StatRow> stats = new List<StatRow>();
    readonly List<Action> refreshers = new List<Action>();
    readonly List<RectTransform> pages = new List<RectTransform>();
    readonly List<Image> tabBgs = new List<Image>();
    readonly List<TMP_Text> tabLabels = new List<TMP_Text>();
    readonly List<GameObject> spawned = new List<GameObject>();
    static readonly string[] TabNames = { "MELEE", "SAND", "PLAYER" };
    static int currentTab;

    GameObject openButton, panel, rockTemplate;
    TMP_Text info, speedLabel;
    readonly float[] speeds = { 1f, 0.5f, 0.25f, 2f };
    int speedIndex;
    float infoT, shownSpeed;
    Vector3 lastPos;

    PlayerMelee melee;
    PlayerTools tools;
    JoystickPlayerController mover;
    SuctionGun gun;
    PlayerShovel shovel;

    void Awake()
    {
        mover = FindFirstObjectByType<JoystickPlayerController>();
        if (mover != null)
        {
            melee = mover.GetComponent<PlayerMelee>();
            tools = mover.GetComponent<PlayerTools>();
            shovel = mover.GetComponent<PlayerShovel>();
            lastPos = mover.transform.position;
        }
        gun = FindFirstObjectByType<SuctionGun>();

        // a clean, hidden copy of the scene's rock to spawn more from
        if (MeleeTarget.All.Count > 0 && MeleeTarget.All[0] != null)
        {
            rockTemplate = Instantiate(MeleeTarget.All[0].gameObject);
            rockTemplate.name = "Melee Target (Rock)";
            rockTemplate.SetActive(false);
        }
    }

    void Start()
    {
        BuildOpenButton();
        BuildPanel();
        panel.SetActive(false);
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
        if (rockTemplate != null) Destroy(rockTemplate);
    }

    // ------------------------------------------------------------------ tabs

    void BuildMeleeTab(RectTransform p)
    {
        if (melee == null) { Header(p, "NO PlayerMelee ON THE PLAYER"); return; }
        Header(p, "PICKAXE");
        Stat(p, "damage", "DAMAGE", () => melee.damage, v => melee.damage = v, 1f, 1f, 99f, "0");
        Stat(p, "attackSpeed", "ATTACK SPEED", () => melee.attackSpeed, v => melee.attackSpeed = v, 0.1f, 0.3f, 3f, "0.0");
        Stat(p, "reach", "REACH (m)", () => melee.range, v => melee.range = v, 0.1f, 0.5f, 3f, "0.0");
        Stat(p, "combo", "COMBO TIME (s)", () => melee.comboTime, v => melee.comboTime = v, 0.02f, 0.2f, 0.62f, "0.00");
        Stat(p, "hitStop", "HIT-STOP (s)", () => melee.hitStop, v => melee.hitStop = v, 0.01f, 0f, 0.3f, "0.00");
        Stat(p, "killStop", "KILL HIT-STOP (s)", () => melee.killHitStop, v => melee.killHitStop = v, 0.02f, 0f, 0.5f, "0.00");

        Header(p, "TARGET (ALL ROCKS)");
        Stat(p, "targetHP", "HP", () => FromTarget(t => t.HP.maxHP, 24f), v => EachTarget(t => t.HP.SetMax(v)), 6f, 6f, 600f, "0");
        Stat(p, "flash", "WHITE FLASH (s)", () => FromTarget(t => t.flashTime, 0.07f), v => EachTarget(t => t.flashTime = v), 0.01f, 0f, 0.3f, "0.00");
        Stat(p, "punch", "SQUASH", () => FromTarget(t => t.punchScale, 0.16f), v => EachTarget(t => t.punchScale = v), 0.02f, 0f, 0.5f, "0.00");
        Stat(p, "respawn", "RESPAWN (s)", () => FromTarget(t => t.respawnDelay, 2.5f), v => EachTarget(t => t.respawnDelay = v), 0.5f, 0.5f, 30f, "0.0");

        var g = Grid(p);
        Btn("SPAWN ROCK", g, SpawnRock);
        Btn("HEAL ROCKS", g, () => EachTarget(t => t.HP.HealFull()));
        Btn("REMOVE SPAWNED", g, () => { foreach (var s in spawned) if (s != null) Destroy(s); spawned.Clear(); }, Danger);
    }

    void BuildSandTab(RectTransform p)
    {
        if (gun == null) { Header(p, "NO SuctionGun IN THE SCENE"); return; }
        Header(p, "SUCTION GUN");
        Stat(p, "gunPower", "GUN LEVEL (vs crust)", () => gun.power, v => gun.power = Mathf.RoundToInt(v), 1f, 1f, 5f, "0");
        Stat(p, "gunRange", "RANGE (m)", () => gun.range, v => gun.range = v, 0.5f, 1f, 10f, "0.0");
        Stat(p, "gunAngle", "CONE HALF ANGLE", () => gun.halfAngle, v => gun.halfAngle = v, 4f, 10f, 80f, "0");
        Stat(p, "suckRate", "SUCK RATE", () => gun.suckRate, v => gun.suckRate = v, 0.2f, 0.2f, 10f, "0.0");
        Stat(p, "capacity", "TANK CAPACITY", () => gun.capacity, v => gun.capacity = Mathf.RoundToInt(v), 10f, 10f, 990f, "0");

        Header(p, "WALKING IN SAND");
        Stat(p, "mouthPower", "MOUTH POWER", () => gun.mouthPower, v => gun.mouthPower = v, 0.25f, 0f, 10f, "0.00");
        Stat(p, "mouthRange", "MOUTH RANGE (m)", () => gun.mouthRange, v => gun.mouthRange = v, 0.2f, 0.6f, 4f, "0.0");
        Stat(p, "suckMove", "SPEED WHILE SUCKING", () => gun.suckMoveSpeed, v => gun.suckMoveSpeed = v, 0.05f, 0.1f, 1f, "0.00");
        Stat(p, "minSand", "SPEED IN DEEP SAND", () => gun.minSandSpeed, v => gun.minSandSpeed = v, 0.05f, 0.05f, 1f, "0.00");

        if (shovel != null)
        {
            Header(p, "SHOVEL");
            Stat(p, "bite", "BITE PER SWING", () => shovel.bite, v => shovel.bite = v, 0.1f, 0.2f, 5f, "0.0");
            Stat(p, "digReach", "DIG REACH (m)", () => shovel.reach, v => shovel.reach = v, 0.1f, 0.8f, 4f, "0.0");
            Stat(p, "digSpeed", "DIG SPEED", () => shovel.attackSpeed, v => shovel.attackSpeed = v, 0.1f, 0.3f, 3f, "0.0");
            Stat(p, "digMove", "WALK WHILE DIGGING", () => shovel.digMoveSpeed, v => shovel.digMoveSpeed = v, 0.05f, 0.05f, 1f, "0.00");
            Stat(p, "digStop", "DIG FREEZE (s)", () => shovel.hitStop, v => shovel.hitStop = v, 0.01f, 0f, 0.2f, "0.00");
            var dg = Grid(p);
            // A/B: quick small bites, or heavy (press in, snap out) bigger chunks. Heavy values chosen by the user in play.
            Btn("DIG A: QUICK", dg, () => { shovel.heavyStyle = false; SetStats(new Dictionary<string, float> { { "bite", 1.0f }, { "digReach", 1.5f }, { "digSpeed", 0.9f }, { "digStop", 0.02f }, { "digMove", 0.25f } }, "DIG A: QUICK SMALL BITES"); });
            Btn("DIG B: HEAVY", dg, () => { shovel.heavyStyle = true; SetStats(new Dictionary<string, float> { { "bite", 1.8f }, { "digReach", 2.2f }, { "digSpeed", 0.9f }, { "digStop", 0.05f }, { "digMove", 0.3f } }, "DIG B: PRESS IN, SNAP OUT"); });
            Toggle("STEADY DIG WALK", dg, () => shovel.steadyWalk, v => shovel.steadyWalk = v);
        }

        var g = Grid(p);
        Toggle("UNLIMITED TANK", g, () => gun.unlimitedCapacity, v => gun.unlimitedCapacity = v);
        Toggle("AUTO-AIM", g, () => gun.autoAim, v => gun.autoAim = v);
        Toggle("SAND SLOWS", g, () => gun.sandSlowsWalking, v => gun.sandSlowsWalking = v);
        Btn("REFILL SAND", g, () => { foreach (var f in SandField.All) f.Build(); GameToast.Show("SAND REFILLED", Accent); });
        Btn("EMPTY TANK", g, () => gun.DevSetStored(0f), Danger);
    }

    void BuildPlayerTab(RectTransform p)
    {
        Header(p, "PROGRESSION STAGE");
        var g = Grid(p);
        for (int i = 0; i < StageNames.Length; i++)
        {
            int ii = i;
            Btn(StageNames[i], g, () => ApplyStage(ii), Accent, Dark);
        }
        Btn("RESET ALL", g, ResetAll, Danger);
        Btn("LOG VALUES", g, LogValues);

        if (mover != null)
        {
            Header(p, "MOVEMENT");
            Stat(p, "moveSpeed", "MOVE SPEED (m/s)", () => mover.moveSpeed, v => mover.moveSpeed = v, 0.2f, 1f, 12f, "0.0");
            Stat(p, "turnSpeed", "TURN SPEED (deg/s)", () => mover.turnSpeed, v => mover.turnSpeed = v, 60f, 120f, 1440f, "0");
        }

        if (tools != null)
        {
            Header(p, "TOOL IN HANDS");
            g = Grid(p);
            Toggle("AUTO SWITCH", g, () => tools.autoSwitch, v => tools.SetAuto(v));
            Btn("PICKAXE", g, () => { tools.Equip(PlayerTool.Pickaxe); Refresh(); });
            Btn("SHOVEL", g, () => { tools.sandTool = PlayerTool.Shovel; tools.Equip(PlayerTool.Shovel); Refresh(); });
            Btn("GUN", g, () => { tools.sandTool = PlayerTool.Gun; tools.Equip(PlayerTool.Gun); Refresh(); });
        }

        Header(p, "GAME");
        g = Grid(p);
        speedLabel = Btn("TIME x1", g, CycleSpeed).GetComponentInChildren<TMP_Text>();
        Toggle("GOD MODE", g, () => PlayerHealth.GodMode, v => PlayerHealth.GodMode = v);
    }

    // ------------------------------------------------------------------ actions

    /// Sets several stats by key at once (presets), shows a toast.
    void SetStats(Dictionary<string, float> values, string toast)
    {
        foreach (var s in stats)
            if (values.TryGetValue(s.key, out float v)) s.setRaw(v);
        Refresh();
        GameToast.Show(toast, Accent);
    }

    void ApplyStage(int i)
    {
        foreach (var s in stats)
            if (Stages[i].TryGetValue(s.key, out float v)) s.setRaw(v);
        if (shovel != null && Stages[i].ContainsKey("bite")) shovel.heavyStyle = false;
        if (tools != null && Stages[i].TryGetValue("sandTool", out float st))
        {
            tools.SetAuto(true);
            tools.SetSandTool(st < 0.5f ? PlayerTool.Shovel : PlayerTool.Gun);
        }
        Refresh();
        GameToast.Show("STAGE: " + StageNames[i], Accent);
    }

    void ResetAll()
    {
        foreach (var s in stats) s.setRaw(s.initial);
        Refresh();
        GameToast.Show("VALUES RESET", Accent);
    }

    void LogValues()
    {
        var sb = new System.Text.StringBuilder("PlayerLab values:\n");
        foreach (var s in stats) sb.AppendLine("  " + s.name + " = " + s.get().ToString(s.fmt) + (Mathf.Approximately(s.get(), s.initial) ? "" : "   (was " + s.initial.ToString(s.fmt) + ")"));
        Debug.Log(sb.ToString());
        GameToast.Show("VALUES IN THE CONSOLE", Accent);
    }

    void SpawnRock()
    {
        if (rockTemplate == null || mover == null) return;
        Transform p = mover.transform;
        Vector3 fwd = p.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        Vector3 pos = p.position + fwd.normalized * 3.5f;
        pos.y = GroundUtil.Height(pos, pos.y);
        var go = Instantiate(rockTemplate, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
        go.SetActive(true);
        spawned.Add(go);
    }

    void EachTarget(Action<MeleeTarget> a)
    {
        foreach (var t in new List<MeleeTarget>(MeleeTarget.All)) if (t != null) a(t);
        if (rockTemplate != null) a(rockTemplate.GetComponent<MeleeTarget>());
    }

    float FromTarget(Func<MeleeTarget, float> f, float fallback)
    {
        var t = rockTemplate != null ? rockTemplate.GetComponent<MeleeTarget>() : MeleeTarget.All.Count > 0 ? MeleeTarget.All[0] : null;
        return t != null && t.HP != null ? f(t) : fallback;
    }

    void CycleSpeed()
    {
        speedIndex = (speedIndex + 1) % speeds.Length;
        Time.timeScale = speeds[speedIndex];
        speedLabel.text = "TIME x" + speeds[speedIndex];
    }

    void Refresh()
    {
        foreach (var s in stats) s.value.text = s.get().ToString(s.fmt);
        foreach (var r in refreshers) r();
    }

    // ------------------------------------------------------------------ info line

    void Update()
    {
        if (mover != null)
        {
            // smoothed real speed of the player
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                Vector3 d = mover.transform.position - lastPos; d.y = 0f;
                shownSpeed = Mathf.Lerp(shownSpeed, d.magnitude / dt, 1f - Mathf.Exp(-dt * 8f));
            }
            lastPos = mover.transform.position;
        }
        if (panel == null || !panel.activeSelf) return;
        infoT -= Time.unscaledDeltaTime;
        if (infoT > 0f) return;
        infoT = 0.2f;

        string tool = tools != null ? tools.tool.ToString().ToUpper() + (tools.autoSwitch ? " (auto)" : "") : "-";
        string tank = gun != null ? Mathf.FloorToInt(gun.Stored) + (gun.unlimitedCapacity ? "" : "/" + gun.capacity) + (gun.Sucking ? "  SUCKING" : "") : "-";
        string dps = melee != null ? (melee.damage * melee.attackSpeed / Mathf.Max(0.1f, melee.comboTime + 0.06f)).ToString("0.0") : "-";
        info.text = "speed " + shownSpeed.ToString("0.0") + " m/s  ·  tool " + tool + "\ntank " + tank + "  ·  pickaxe " + dps + " dmg/s  ·  "
                  + Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)) + " fps";
    }

    // ------------------------------------------------------------------ UI building

    void BuildOpenButton()
    {
        var b = Btn("LAB", transform, () => Show(true), Accent, Dark);
        openButton = b.gameObject;
        var rt = (RectTransform)b.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(24f, -246f);
        rt.sizeDelta = new Vector2(150f, 62f);
    }

    void BuildPanel()
    {
        var p = NewRect("LabPanel", transform);
        p.anchorMin = p.anchorMax = p.pivot = new Vector2(0f, 1f);
        p.anchoredPosition = new Vector2(16f, -60f);
        p.sizeDelta = new Vector2(560f, 0f);
        p.gameObject.AddComponent<Image>().color = PanelCol;
        var vl = p.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(16, 16, 10, 14);
        vl.spacing = 5;
        vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        p.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        panel = p.gameObject;

        var title = NewRect("Title", p);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 46;
        Label(title, "PLAYER LAB", 32, Accent, TextAlignmentOptions.Left);
        var close = Btn("X", title, () => Show(false), Danger, Color.white);
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(56f, 46f);

        var infoRt = NewRect("Info", p);
        infoRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
        info = Label(infoRt, "", 20, new Color(0.9f, 0.88f, 0.82f), TextAlignmentOptions.TopLeft);
        info.fontStyle = FontStyles.Normal;

        var bar = NewRect("Tabs", p);
        bar.gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
        var hl = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
        for (int i = 0; i < TabNames.Length; i++)
        {
            int ii = i;
            var tb = Btn(TabNames[i], bar, () => SelectTab(ii));
            tabBgs.Add((Image)tb.targetGraphic);
            tabLabels.Add(tb.GetComponentInChildren<TMP_Text>());
        }

        BuildMeleeTab(Page(p, "MELEE"));
        BuildSandTab(Page(p, "SAND"));
        BuildPlayerTab(Page(p, "PLAYER"));
        SelectTab(currentTab);
    }

    RectTransform Page(RectTransform parent, string name)
    {
        var pg = NewRect("Page " + name, parent);
        var vl = pg.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(0, 0, 2, 0);
        vl.spacing = 5;
        vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        pages.Add(pg);
        return pg;
    }

    void SelectTab(int i)
    {
        currentTab = Mathf.Clamp(i, 0, pages.Count - 1);
        for (int k = 0; k < pages.Count; k++)
        {
            bool on = k == currentTab;
            pages[k].gameObject.SetActive(on);
            tabBgs[k].color = on ? Accent : BtnCol;
            tabLabels[k].color = on ? (Color)Dark : Color.white;
        }
    }

    void Show(bool on)
    {
        panel.SetActive(on);
        openButton.SetActive(!on);
        transform.SetAsLastSibling();
        infoT = 0f;
        if (on) Refresh();
    }

    void Header(RectTransform parent, string name)
    {
        var h = NewRect(name, parent);
        h.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
        Label(h, name, 21, Accent, TextAlignmentOptions.BottomLeft);
    }

    Transform Grid(RectTransform parent)
    {
        var grid = NewRect("Buttons", parent);
        var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(170f, 50f);
        gl.spacing = new Vector2(9f, 7f);
        gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gl.constraintCount = 3;
        return grid;
    }

    /// One row: NAME            [-]  value  [+]
    void Stat(RectTransform page, string key, string name, Func<float> get, Action<float> set, float step, float min, float max, string fmt)
    {
        var s = new StatRow { key = key, name = name, fmt = fmt, get = get, initial = get() };
        s.set = v => set(Mathf.Clamp(Mathf.Round(v / step) * step, min, max));
        s.setRaw = v => set(Mathf.Clamp(v, min, max));
        stats.Add(s);

        var row = NewRect(name, page);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
        row.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        var l = NewRect("Name", row);
        l.anchorMin = Vector2.zero; l.anchorMax = Vector2.one; l.offsetMin = new Vector2(10f, 0f); l.offsetMax = new Vector2(-260f, 0f);
        var nt = Label(l, name, 20, Color.white, TextAlignmentOptions.Left);
        nt.enableAutoSizing = true; nt.fontSizeMin = 13; nt.fontSizeMax = 20;

        var minus = Btn("-", row, () => { s.set(s.get() - step); Refresh(); });
        PlaceRight((RectTransform)minus.transform, 186f, 64f);
        var v = NewRect("Value", row);
        PlaceRight(v, 70f, 110f);
        s.value = Label(v, get().ToString(fmt), 24, Accent, TextAlignmentOptions.Center);
        var plus = Btn("+", row, () => { s.set(s.get() + step); Refresh(); });
        PlaceRight((RectTransform)plus.transform, 0f, 64f);
    }

    static void PlaceRight(RectTransform rt, float fromRight, float width)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-fromRight, 0f);
        rt.sizeDelta = new Vector2(width, 40f);
    }

    void Toggle(string name, Transform parent, Func<bool> get, Action<bool> set)
    {
        Button b = null;
        b = Btn(name, parent, () => { set(!get()); Refresh(); });
        var img = (Image)b.targetGraphic;
        var txt = b.GetComponentInChildren<TMP_Text>();
        Action r = () => { bool on = get(); img.color = on ? OnCol : BtnCol; txt.text = name + (on ? ": ON" : ": OFF"); };
        refreshers.Add(r);
        r();
    }

    Button Btn(string text, Transform parent, Action onClick, Color? bg = null, Color? fg = null)
    {
        var rt = NewRect(text, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = bg ?? BtnCol;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(() => onClick());
        var l = NewRect("Label", rt);
        l.anchorMin = Vector2.zero; l.anchorMax = Vector2.one; l.offsetMin = new Vector2(4, 2); l.offsetMax = new Vector2(-4, -2);
        var t = Label(l, text, 22, fg ?? Color.white, TextAlignmentOptions.Center);
        t.enableAutoSizing = true; t.fontSizeMin = 13; t.fontSizeMax = 22;
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
}
