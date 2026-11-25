using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// In-game debug panel (Editor + development builds only). Creates itself on scene load:
/// a small DEBUG button top-left that opens a panel of test shortcuts.
/// Tabs (GAME / BOTS / DISKS / COMBAT / WORLD); add a button with one Btn(...) line in that tab's Build...Tab().
public class DebugMenu : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;
        if (FindFirstObjectByType<DebugMenu>() != null) return;
        if (PlayerLabDebug.IsLabScene) return;   // the PlayerLab scene has its own panel (PlayerLabDebug)
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

    // tabs: one page of buttons visible at a time (the last tab stays selected between openings)
    static readonly string[] TabNames = { "GAME", "BOTS", "DISKS", "COMBAT", "WORLD" };
    static int currentTab;
    readonly List<RectTransform> pages = new List<RectTransform>();
    readonly List<Image> tabBgs = new List<Image>();
    readonly List<TMP_Text> tabLabels = new List<TMP_Text>();
    RectTransform disksPage;

    void BuildPanel()
    {
        var p = NewRect("DebugPanel", transform);
        p.anchorMin = p.anchorMax = p.pivot = new Vector2(0f, 1f);
        p.anchoredPosition = new Vector2(16f, -100f);
        p.sizeDelta = new Vector2(560f, 0f);
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
        Label(title, "DEBUG", 34, Accent, TextAlignmentOptions.Left);
        var close = Btn("X", title, () => Show(false), Danger, Color.white);
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(56f, 50f);

        var infoRt = NewRect("Info", p);
        infoRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
        info = Label(infoRt, "", 20, new Color(0.9f, 0.88f, 0.82f), TextAlignmentOptions.TopLeft);
        info.fontStyle = FontStyles.Normal;

        // tab bar
        var bar = NewRect("Tabs", p);
        bar.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
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

        BuildGameTab(Page(p, "GAME"));
        BuildBotsTab(Page(p, "BOTS"));
        disksPage = Page(p, "DISKS");
        BuildCombatTab(Page(p, "COMBAT"));
        BuildWorldTab(Page(p, "WORLD"));
        SelectTab(currentTab);
    }

    RectTransform Page(RectTransform parent, string name)
    {
        var pg = NewRect("Page " + name, parent);
        var vl = pg.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(0, 0, 4, 0);
        vl.spacing = 6;
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
            tabLabels[k].color = on ? new Color32(34, 27, 19, 255) : Color.white;
        }
        if (pages[currentTab] == disksPage) BuildDisksTab();   // the disk list is read fresh each time
    }

    void BuildGameTab(RectTransform p)
    {
        var g = Section(p, "MATERIALS");
        Btn("+100 MATS", g, () => AddMats(100));
        Btn("+1000 MATS", g, () => AddMats(1000));
        Btn("ZERO MATS", g, () => { var e = Economy.Instance; if (e != null) e.TrySpend(e.materials); }, Danger);

        g = Section(p, "NEAREST FACTORY");
        Btn("LEVEL +1", g, () => WithUpgrades(u => u.DevSetLevel(u.Level + 1)));
        Btn("MAX LEVEL", g, () => WithUpgrades(u => u.DevSetLevel(u.MaxLevel)));
        Btn("RESET TREE", g, () => WithUpgrades(u => u.DevReset()), Danger);

        // the pipe's level follows the player's level
        g = Section(p, "PLAYER");
        Btn("PLAYER LV +1", g, () => { var u = PlayerUpgrades.Instance; if (u != null) u.Upgrades.DevSetLevel(u.Level + 1); });
        Btn("PLAYER MAX", g, () => { var u = PlayerUpgrades.Instance; if (u != null) u.Upgrades.DevSetLevel(u.Upgrades.MaxLevel); });
        Btn("PLAYER RESET", g, () => { var u = PlayerUpgrades.Instance; if (u != null) u.Upgrades.DevReset(); }, Danger);

        g = Section(p, "BATTERIES");
        Btn("GIVE COMMON", g, () => GiveBattery(Rarity.Common));
        Btn("GIVE RARE", g, () => GiveBattery(Rarity.Rare));
        Btn("GIVE 1 EACH", g, () => { foreach (Rarity r in System.Enum.GetValues(typeof(Rarity))) GiveBattery(r); });
    }

    void BuildBotsTab(RectTransform p)
    {
        var g = Section(p, "SPAWN");
        Btn("SPAWN 1 BOT", g, () => Spawn(1, false));
        Btn("SPAWN 5 BOTS", g, () => Spawn(5, false));
        Btn("5 + PROGRAM", g, () => Spawn(5, true));
        Btn("SPAWN DRONE", g, SpawnDrone);
        Btn("3 DRONES", g, () => { for (int i = 0; i < 3; i++) SpawnDrone(); });
        Btn("DELETE BOTS", g, DeleteBots, Danger);

        g = Section(p, "BATTERY (ALL BOTS)");
        Btn("BATTERY 100%", g, () => SetBattery(1f));
        Btn("BATTERY 20%", g, () => SetBattery(0.2f));
        Btn("BATTERY 0%", g, () => SetBattery(0f), Danger);

        g = Section(p, "HEALTH");
        Btn("DMG BOT -25", g, () => { var b = NearestBot(); if (b != null && b.Health != null) b.Health.Damage(25f); });
        Btn("BREAK BOT", g, () => { var b = NearestBot(); if (b != null && b.Health != null) b.Health.Kill(); }, Danger);
        Btn("REPAIR BOTS", g, () => { foreach (var b in ScoutBot.All) b.Repair(); });
    }

    // one button per disk program: give it to the player, or put it straight into every robot it fits
    void BuildDisksTab()
    {
        var p = disksPage;
        for (int i = p.childCount - 1; i >= 0; i--)
        {
            var c = p.GetChild(i).gameObject;
            c.SetActive(false);
            Destroy(c);
        }
        var all = AllDisks();
        var inv = PlayerInventory.Instance;

        var g = Section(p, "GIVE TO PLAYER  (tap = +1)");
        foreach (var d in all)
        {
            var disk = d;
            int have = inv != null ? inv.CountOf(disk) : 0;
            Btn(DiskText(disk) + (have > 0 ? "  x" + have : ""), g, () => { GiveDisk(disk); BuildDisksTab(); }, disk.color, new Color32(24, 20, 14, 255));
        }
        if (all.Count == 0) Btn("NO DISKS FOUND", g, () => { });

        g = Section(p, "INVENTORY");
        Btn("GIVE 1 EACH", g, () => { foreach (var d in AllDisks()) GiveDisk(d); BuildDisksTab(); });
        Btn("DRONE DISKS", g, () => { foreach (var d in AllDisks()) if (d.FitsOn(RobotType.Drone)) GiveDisk(d); BuildDisksTab(); });
        Btn("CLEAR DISKS", g, () => { if (inv != null) inv.SetDisks(new List<DiskProgram>()); BuildDisksTab(); }, Danger);

        g = Section(p, "PUT IN ALL ROBOTS IT FITS");
        foreach (var d in all)
        {
            var disk = d;
            Btn(DiskText(disk), g, () => ProgramAllWith(disk), disk.color, new Color32(24, 20, 14, 255));
        }
        Btn("REMOVE FROM ALL", g, () => { foreach (var b in ScoutBot.All) b.SetProgram(null); GameToast.Show("ALL ROBOTS: NO DISK", Accent); }, Danger);
    }

    static string DiskText(DiskProgram d) => d.displayName.ToUpper() + "  " + RobotTypes.Glyph(d.compatibleWith);

    /// Every disk program in the game: loaded assets plus anything on the map, in robots, factories or the inventory.
    static List<DiskProgram> AllDisks()
    {
        var set = new HashSet<DiskProgram>();
        foreach (var d in Resources.FindObjectsOfTypeAll<DiskProgram>()) if (d != null) set.Add(d);
        foreach (var d in FindObjectsByType<DiskPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (d.program != null) set.Add(d.program);
        foreach (var b in ScoutBot.All) if (b.Program != null) set.Add(b.Program);
        var inv = PlayerInventory.Instance;
        if (inv != null) foreach (var d in inv.disks) if (d != null) set.Add(d);
        var list = new List<DiskProgram>(set);
        // gatherer disks first, then drone disks, by name
        list.Sort((a, b) => a.compatibleWith != b.compatibleWith ? a.compatibleWith.CompareTo(b.compatibleWith) : string.CompareOrdinal(a.displayName, b.displayName));
        return list;
    }

    static void GiveDisk(DiskProgram d)
    {
        var inv = PlayerInventory.Instance;
        if (inv == null || d == null) return;
        inv.Add(d);
        GameToast.Show("+1 " + d.displayName.ToUpper() + " DISK", d.color);
    }

    static void ProgramAllWith(DiskProgram d)
    {
        int n = 0;
        foreach (var b in ScoutBot.All) if (d.FitsOn(b.robotType)) { b.SetProgram(d); n++; }
        GameToast.Show(n > 0 ? d.displayName.ToUpper() + " IN " + n + (n == 1 ? " ROBOT" : " ROBOTS") : "NO " + RobotTypes.Name(d.compatibleWith) + " TO PROGRAM", d.color);
    }

    void BuildCombatTab(RectTransform p)
    {
        var g = Section(p, "ENEMIES");
        Btn("SPAWN DUMMY", g, () => SpawnDummy(false));
        Btn("SPAWN SHOOTER", g, () => SpawnDummy(true));
        Btn("SPAWN SCOUT", g, () => SpawnEnemy(false));
        Btn("SPAWN ENEMY DRONE", g, () => SpawnEnemy(true));
        Btn("SPAWN LEECH", g, SpawnLeech);
        Btn("SPAWN SPIDER", g, SpawnSpider);
        Btn("DROP 5 TRASH", g, () => { var pt = PlayerT(); if (pt != null) LooseTrash.Drop(pt.position + pt.forward * 3f, 5, 1.5f, 0f); });
        Btn("FIRE AT ENEMY", g, FireAtEnemy);
        Btn("KILL ENEMIES", g, KillEnemies, Danger);

        g = Section(p, "PLAYER");
        Btn("DMG PLAYER -20", g, () => { var ph = PlayerHealth.Instance; if (ph != null) ph.Health.Damage(20f); });
        Btn("HEAL PLAYER", g, () => { var ph = PlayerHealth.Instance; if (ph != null) ph.Health.HealFull(); });
        Btn("KO PLAYER", g, () => { var ph = PlayerHealth.Instance; if (ph != null) ph.Health.Kill(); }, Danger);
        TMP_Text godLabel = null;
        godLabel = Btn("GOD MODE: " + (PlayerHealth.GodMode ? "ON" : "OFF"), g, () => { PlayerHealth.GodMode = !PlayerHealth.GodMode; godLabel.text = PlayerHealth.GodMode ? "GOD MODE: ON" : "GOD MODE: OFF"; }).GetComponentInChildren<TMP_Text>();

        g = Section(p, "WEAPONS + HUD A/B");
        TMP_Text hudLabel = null;
        hudLabel = Btn(HudName(), g, () => { MobileHUD.SetBarLayout(!MobileHUD.SavedBarLayout); hudLabel.text = HudName(); }).GetComponentInChildren<TMP_Text>();
        Btn("NEXT WEAPON", g, NextWeapon);
        Btn("DROP ALL WEAPONS", g, DropAllWeapons);
        TMP_Text shakeLabel = null;
        shakeLabel = Btn("SHAKE x" + CameraShake.Scale, g, () =>
        {
            // 0 > 0.25 > 0.5 > 1 > 0
            CameraShake.Scale = CameraShake.Scale >= 1f ? 0f : (CameraShake.Scale <= 0f ? 0.25f : CameraShake.Scale * 2f);
            shakeLabel.text = "SHAKE x" + CameraShake.Scale;
        }).GetComponentInChildren<TMP_Text>();
    }

void BuildWorldTab(RectTransform p)
    {
        var g = Section(p, "TELEPORT");
        Btn("TP FACTORY", g, () => Teleport(Fac() != null ? Fac().transform : null, -4f));
        Btn("TP CHARGER", g, () => Teleport(ChargingStation.All.Count > 0 ? ChargingStation.All[0].transform : null, 4f));
        Btn("TP BASE", g, () => Teleport(HomeBase.All.Count > 0 ? HomeBase.All[0].transform : null, -4.5f));

        g = Section(p, "TOGGLES");
        speedLabel = Btn("SPEED x1", g, CycleSpeed).GetComponentInChildren<TMP_Text>();
        TMP_Text freeLabel = null, fogLabel = null, pipeLabel = null, unplugLabel = null;
        pipeLabel = Btn("PIPE: ON", g, () => { PipeTether.Disabled = !PipeTether.Disabled; pipeLabel.text = PipeTether.Disabled ? "PIPE: OFF" : "PIPE: ON"; }).GetComponentInChildren<TMP_Text>();
        freeLabel = Btn("FREE BUILD: OFF", g, () => { BuildMode.FreeBuild = !BuildMode.FreeBuild; freeLabel.text = BuildMode.FreeBuild ? "FREE BUILD: ON" : "FREE BUILD: OFF"; }).GetComponentInChildren<TMP_Text>();
        fogLabel = Btn("REVEAL MAP: OFF", g, () =>
        {
            var fog = FogOfWar.Instance;
            if (fog == null) return;
            fog.revealAll = !fog.revealAll;
            fog.Refresh();
            fogLabel.text = fog.revealAll ? "REVEAL MAP: ON" : "REVEAL MAP: OFF";
        }).GetComponentInChildren<TMP_Text>();

        g = Section(p, "AIR (UNPLUG TRIP)");
        // UNPLUG ON = the pipe pops off at full stretch and the air counts down; OFF = the old snap back
        bool unplugOn = PipeTether.Instance == null || PipeTether.Instance.unplugAtFullStretch;
        unplugLabel = Btn(unplugOn ? "UNPLUG: ON" : "UNPLUG: OFF", g, () =>
        {
            var pt = PipeTether.Instance;
            if (pt == null) return;
            pt.unplugAtFullStretch = !pt.unplugAtFullStretch;
            unplugLabel.text = pt.unplugAtFullStretch ? "UNPLUG: ON" : "UNPLUG: OFF";
        }).GetComponentInChildren<TMP_Text>();
        Btn("REFILL AIR", g, () => { if (AirSupply.Instance != null) AirSupply.Instance.Refill(); });
        Btn("AIR +10s", g, () => { if (AirSupply.Instance != null) { AirSupply.Instance.airSeconds += 10f; GameToast.Show("AIR TANK " + Mathf.RoundToInt(AirSupply.Instance.airSeconds) + " s", Color.white); } });
    }

    static Transform PlayerT()
    {
        var p = GameObject.FindWithTag("Player");
        return p != null ? p.transform : null;
    }

    static ScoutBot NearestBot()
    {
        var p = PlayerT();
        ScoutBot best = null; float bd = float.MaxValue;
        foreach (var b in ScoutBot.All)
        {
            float d = p != null ? (b.transform.position - p.position).sqrMagnitude : 0f;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    static void SpawnDummy(bool shooter)
    {
        var p = PlayerT();
        if (p == null) return;
        Vector3 fwd = p.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        var d = TargetDummy.Spawn(p.position + fwd.normalized * (shooter ? 9f : 5f), shooter);
        d.transform.rotation = Quaternion.LookRotation(-fwd);
        GameToast.Show(shooter ? "SHOOTER DUMMY SPAWNED" : "DUMMY SPAWNED", new Color(1f, 0.6f, 0.25f));
    }

    static void SpawnEnemy(bool drone)
    {
        var p = PlayerT();
        if (p == null) return;
        Vector3 fwd = p.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        Vector3 pos = p.position + fwd.normalized * (drone ? 15f : 12f);
        // like the dummy: stands where it is spawned (no patrol, not hidden by fog) and attacks when something comes in range
        if (drone) { var d = EnemyDrone.Spawn(pos, pos); d.homeRadius = 0f; d.hideInFog = false; }
        else EnemyScout.Spawn(pos);
        GameToast.Show(drone ? "ENEMY DRONE SPAWNED" : "ENEMY SCOUT SPAWNED", new Color(1f, 0.4f, 0.3f));
    }

    // a spider 8 m ahead: it notices the player at 9 m, so it comes straight in
    static void SpawnSpider()
    {
        var p = PlayerT();
        if (p == null) return;
        Vector3 fwd = p.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        var s = EnemySpider.Spawn(p.position + fwd.normalized * 8f);
        GameToast.Show(s != null ? "SPIDER SPAWNED" : "NO SPIDER PREFAB (Tools > Trash Game > 13. Enemies (Spider Prefab))", new Color(1f, 0.4f, 0.3f));
    }

    static readonly ShotSpec debugShot = new ShotSpec { damage = 20f, speed = 24f };

    // E3: a leech hole 15 m ahead (it wakes up within 14 m, so step toward it)
    static void SpawnLeech()
    {
        var p = PlayerT();
        if (p == null) return;
        Vector3 fwd = p.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        EnemyLeech.Spawn(p.position + fwd.normalized * 15f);
        GameToast.Show("LEECH HOLE 15 m AHEAD", new Color(1f, 0.45f, 0.55f));
    }

    static void FireAtEnemy()
    {
        var p = PlayerT();
        if (p == null) return;
        var t = Targeting.FindNearest(Team.Enemy, p.position, 40f);
        if (t == null) { GameToast.Show("NO ENEMY IN RANGE", new Color(1f, 0.6f, 0.25f)); return; }
        Projectile.Fire(p.position + Vector3.up * 1.4f + p.forward * 0.6f, t, debugShot, Team.Player, p.gameObject);
    }

    // C3: a combat drone next to the player (from the Drone Bay prefab, built with that bay's stats if there is one)
    static void SpawnDrone()
    {
        BotFactory bay = null;
        foreach (var f in BotFactory.All) if (f.botPrefab != null && f.botPrefab.GetComponent<CombatDrone>() != null) { bay = f; break; }
        if (bay != null) { bay.DevSpawnNow(); GameToast.Show("DRONE SPAWNED AT THE BAY", new Color(0.45f, 0.95f, 1f)); return; }
        var prefab = Resources.FindObjectsOfTypeAll<CombatDrone>();
        CombatDrone src = null;
        foreach (var c in prefab) if (!c.gameObject.scene.IsValid()) { src = c; break; }
        var p = PlayerT();
        if (src == null || p == null) { GameToast.Show("NO DRONE PREFAB (RUN BUILDER 12)", new Color(1f, 0.45f, 0.35f)); return; }
        var go = Instantiate(src.gameObject, p.position + p.right * 2.5f, Quaternion.identity);
        var d = go.GetComponent<CombatDrone>();
        d.SourcePrefab = src.gameObject;
        d.Init(go.transform.position, 900 + ScoutBot.All.Count);
        GameToast.Show("DRONE SPAWNED (NO BAY)", new Color(0.45f, 0.95f, 1f));
    }

    static string HudName() => MobileHUD.SavedBarLayout ? "HUD: B BAR + TAP" : "HUD: A SIDE + AUTO";

    /// Puts the next weapon of Resources/Weapons into the selected weapon slot.
    static void NextWeapon()
    {
        var pw = PlayerWeapon.Instance;
        var all = WeaponDef.LoadAll();
        if (pw == null || all.Length == 0) return;
        var d = all[(Array.IndexOf(all, pw.weapon) + 1) % all.Length];
        pw.Equip(d);
        GameToast.Show(d.displayName, d.color);
    }

    /// A pickup of every weapon in a circle around the player.
    static void DropAllWeapons()
    {
        var pt = PlayerT();
        var all = WeaponDef.LoadAll();
        if (pt == null) return;
        for (int i = 0; i < all.Length; i++)
        {
            float a = i * Mathf.PI * 2f / all.Length;
            Vector3 p = pt.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.4f;
            p.y = GroundUtil.Height(p, pt.position.y);
            WeaponPickup.Spawn(all[i], p);
        }
    }

    static void KillEnemies()
    {
        var list = new List<Health>(Health.All(Team.Enemy));
        foreach (var h in list) if (h != null && !h.IsDead) h.Kill();
        foreach (var l in new List<EnemyLeech>(EnemyLeech.All)) l.Kill();   // also the ones hiding in their hole
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
            if (d.program != null && d.program.FitsOn(RobotType.Gatherer)) return d.program;
        var inv = PlayerInventory.Instance;
        if (inv != null) foreach (var d in inv.disks) if (d.FitsOn(RobotType.Gatherer)) return d;
        return null;
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

    static void GiveBattery(Rarity r)
    {
        var inv = PlayerInventory.Instance;
        if (inv == null) return;
        foreach (var b in BatteryItem.LoadAll())
            if (b.rarity == r) { if (!inv.AddBattery(b)) GameToast.Show("BATTERY SLOTS FULL", new Color(1f, 0.4f, 0.32f)); return; }
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
            if (b.IsDown) dead++;
            else if (b.SeekingCharge) charging++;
            else if (b.Program == null) idle++;
        }
        int queue = 0;
        foreach (var s in ChargingStation.All) queue += s.Queued;
        var f = Fac();
        string fac = f != null ? "Factory Lv " + (f.Upgrades != null ? f.Upgrades.Level : 1) + "  ·  build " + f.BuildTime.ToString("0.#") + "s  ·  queued " + f.Queued : "";
        var ph = PlayerHealth.Instance;
        string hp = ph != null ? "HP " + Mathf.CeilToInt(ph.Health.Current) + "/" + Mathf.RoundToInt(ph.Health.maxHP) + (PlayerHealth.GodMode ? " GOD" : "") + "  ·  enemies " + Health.All(Team.Enemy).Count + "  ·  " : "";
        info.text = hp + "Bots " + total + "  ·  no disk " + idle + "  ·  charging " + charging + "  ·  queue " + queue + "  ·  dead " + dead +
                    "\n" + fac + "  ·  " + Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)) + " fps";
    }
}
