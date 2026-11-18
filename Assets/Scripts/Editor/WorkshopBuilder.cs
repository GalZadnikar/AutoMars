using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static StatType;
using Object = UnityEngine.Object;

/// Module 1: factory upgrade tree + DRG-style Workshop screen (landscape 1920x1080). Safe to re-run.
/// The tree asset is only filled the first time; use "Reset Gatherer Factory Tree" to overwrite your edits.
public static class WorkshopBuilder
{
    const string GenFolder = "Assets/UI/Generated";
    const string TreePath = "Assets/Upgrades/GathererFactory_Tree.asset";
    const string PreviewLayer = "WorkshopPreview";
    static readonly Vector2 Design = new Vector2(1920, 1080);

    static Sprite hexFill, hexRing, lockIcon, batteryIcon, diskIcon;

    // =====================================================================
    [MenuItem("Tools/Trash Game/3. Build Workshop (Upgrade Tree)")]
    public static void Build()
    {
        ApplyLandscape();
        GenerateSprites();
        var tree = CreateTree(false);
        SetupFactory(tree);
        int layer = EnsureLayer(PreviewLayer);
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            cam.cullingMask &= ~(1 << layer);
            EditorUtility.SetDirty(cam);
        }
        BuildUI();
        Save();
        Debug.Log("Workshop built. Press Play, walk to the Gatherer Factory and tap UPGRADE.");
    }

    [MenuItem("Tools/Trash Game/Reset Gatherer Factory Tree (overwrites asset)")]
    public static void ResetTree()
    {
        CreateTree(true);
        AssetDatabase.SaveAssets();
    }

    /// Landscape-only app, canvas designed at 1920x1080 and scaled by height,
    /// old portrait popups scaled down to fit.
    [MenuItem("Tools/Trash Game/Apply Landscape Setup")]
    public static void ApplyLandscape()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

        var canvasGo = GameObject.Find("UI Canvas");
        if (canvasGo == null) return;
        var cs = canvasGo.GetComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        cs.matchWidthOrHeight = 1f;
        EditorUtility.SetDirty(cs);

        foreach (var n in new[] { "FactoryPopup/Panel", "BotProgramPopup/Panel" })
        {
            var panel = canvasGo.transform.Find(n) as RectTransform;
            if (panel == null || panel.GetComponent<FitToSafeArea>() != null) continue;
            var fit = panel.gameObject.AddComponent<FitToSafeArea>();
            fit.designSize = panel.sizeDelta;
            fit.maxScale = 1f;
            fit.margin = 24f;
            EditorUtility.SetDirty(panel.gameObject);
        }
    }

    // =====================================================================
    // tree content
    static BuildingUpgradeTree CreateTree(bool overwrite)
    {
        EnsureFolder("Assets/Upgrades");
        var tree = AssetDatabase.LoadAssetAtPath<BuildingUpgradeTree>(TreePath);
        bool isNew = tree == null;
        if (isNew)
        {
            tree = ScriptableObject.CreateInstance<BuildingUpgradeTree>();
            AssetDatabase.CreateAsset(tree, TreePath);
        }
        if (!isNew && !overwrite) return tree;

        tree.displayName = "Gatherer Factory";
        tree.category = "Production Building";
        tree.outputName = "Scout Bot";
        tree.levelCosts = new List<int> { 30, 60, 120, 200, 320 };
        tree.masterDiskUnlockLevel = 4;
        // robot tech level: every factory level makes the base bot a bit better
        tree.perLevel = new List<StatMod> { Pct(MoveSpeed, 5), Flat(CarryCapacity, 1), Pct(CollectRate, 5) };

        tree.tiers = new List<UpgradeTier>
        {
            Tier(MakePerk("Servo Legs", "SPD", "Faster trips to far-off piles.", Pct(MoveSpeed, 25)),
                 MakePerk("Cargo Rack", "CAP", "More trash per trip.", Pct(CarryCapacity, 50))),
            Tier(MakePerk("Quick Grabbers", "GRB", "Fill up faster at the pile.", Pct(CollectRate, 40)),
                 MakePerk("Assembly Line", "BLD", "The factory builds faster.", Pct(BuildTime, -25)),
                 MakePerk("Bulk Discount", "$", "Cheaper bots.", Pct(BotCost, -30))),
            Tier(MakePerk("Overdrive Motors", "OVR", null, Pct(MoveSpeed, 20)),
                 MakePerk("Compact Stacking", "STK", null, Flat(CarryCapacity, 2)),
                 MakePerk("Dump Chute", "DMP", "Unload at the base in a blink.", Pct(UnloadRate, 100))),
            Tier(MakePerk("Heavy Lifter", "HVY", "Big loads, slower legs.", Flat(CarryCapacity, 4), Pct(MoveSpeed, -10)),
                 MakePerk("Twin Arms", "ARM", "Grab and drop faster.", Pct(CollectRate, 35), Pct(UnloadRate, 35))),
            Tier(MakePerk("Turbo Core", "TRB", "Overclock: blazing fast, small backpack.", Pct(MoveSpeed, 45), Flat(CarryCapacity, -2)),
                 MakePerk("Mega Hauler", "MEG", "Overclock: double load, heavy steps.", Pct(CarryCapacity, 100), Pct(MoveSpeed, -20)),
                 MakePerk("Mass Production", "MAS", "Overclock: churn out bots.", Pct(BuildTime, -40), Pct(BotCost, -20))),
        };
        EditorUtility.SetDirty(tree);
        Debug.Log("Upgrade tree written to " + TreePath);
        return tree;
    }

    static StatMod Pct(StatType s, float v) => new StatMod(s, ModOp.AddPercent, v);
    static StatMod Flat(StatType s, float v) => new StatMod(s, ModOp.AddFlat, v);
    static UpgradeTier Tier(params Perk[] perks) => new UpgradeTier { options = new List<Perk>(perks) };
    static Perk MakePerk(string title, string glyph, string flavor, params StatMod[] mods) =>
        new Perk { title = title, glyph = glyph, flavor = flavor, mods = new List<StatMod>(mods) };

    static void SetupFactory(BuildingUpgradeTree tree)
    {
        var go = GameObject.Find("Bot Factory");
        if (go == null) { Debug.LogError("No 'Bot Factory' in the scene. Run Tools > Trash Game > 1 first."); return; }
        var up = go.GetComponent<BuildingUpgrades>();
        if (up == null) up = go.AddComponent<BuildingUpgrades>();
        up.tree = tree;
        EditorUtility.SetDirty(up);

        var bf = go.GetComponent<BotFactory>();
        bf.idleText = "Gatherer Factory";
        if (bf.label != null) { bf.label.text = bf.idleText; EditorUtility.SetDirty(bf.label); }
        EditorUtility.SetDirty(bf);
    }

    static int EnsureLayer(string name)
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < 32; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
        for (int i = 31; i >= 8; i--)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(p.stringValue)) continue;
            p.stringValue = name;
            tm.ApplyModifiedProperties();
            return i;
        }
        Debug.LogWarning("No free layer for " + name + ", using 31");
        return 31;
    }

    // =====================================================================
    // UI
    static void BuildUI()
    {
        var canvas = GameObject.Find("UI Canvas").transform;
        var old = canvas.Find("Workshop");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var prompts = canvas.Find("Prompts");
        var oldBtn = prompts.Find("UpgradeButton");
        if (oldBtn != null) Object.DestroyImmediate(oldBtn.gameObject);
        var upgradeBtn = WorldBtn("UpgradeButton", prompts, "UPGRADE", WorkshopTheme.Orange, WorkshopTheme.DarkText);

        var ws = BuildWorkshop(canvas);
        ws.transform.SetAsLastSibling();

        var pi = GameObject.Find("Player").GetComponent<PlayerInteractions>();
        pi.upgradeButton = upgradeBtn;
        pi.workshop = ws;
        EditorUtility.SetDirty(pi);
    }

    // Layout (1920x1080, x = centre offset, y = from top):
    //   top bar ............................................................ 0-84
    //   LEFT  (x -940..250): title, robot showcase + slots + stats  (like DRG's weapon side)
    //   RIGHT (x 270..940):  upgrade tree, perk details, factory level  (like DRG's gear mods)
    static WorkshopScreen BuildWorkshop(Transform canvas)
    {
        var root = NewRect("Workshop", canvas);
        Stretch(root);
        AddImage(root, WorkshopTheme.Bg);
        var ws = root.gameObject.AddComponent<WorkshopScreen>();
        ws.group = root.gameObject.AddComponent<CanvasGroup>();

        var c = NewRect("Content", root);
        var fitter = c.gameObject.AddComponent<FitToSafeArea>();
        fitter.designSize = Design;
        c.sizeDelta = Design;

        // ---------------- top bar ----------------
        var top = R("TopBar", c, 0, 0, Design.x, 84);
        AddImage(top, WorkshopTheme.PanelDark);
        ws.devButton = Btn("DevButton", top, "+100 DEV", WorkshopTheme.ButtonOff, Color.white, 24, new Vector2(0.5f, 0.5f), new Vector2(-855, 0), new Vector2(170, 62));
        ws.materialsText = Txt("Materials", top, 470, 12, 520, 60, "MATERIALS 0", 36, WorkshopTheme.White);
        ws.closeButton = Btn("Close", top, "CLOSE", WorkshopTheme.Orange, WorkshopTheme.DarkText, 34, new Vector2(0.5f, 0.5f), new Vector2(840, 0), new Vector2(210, 64));
        AddImage(R("Rule", c, 0, 84, Design.x, 5), WorkshopTheme.Rule, raycast: false);

        // ================= LEFT: showcase =================
        AddImage(R("ShowcaseBg", c, -345, 104, 1190, 956), new Color(0, 0, 0, 0.18f), raycast: false);
        ws.titleText = Txt("Title", c, -345, 110, 1150, 84, "GATHERER FACTORY", 74, WorkshopTheme.Orange);
        ws.titleText.characterSpacing = -2f;
        ws.categoryText = Txt("Category", c, -345, 192, 1150, 40, "PRODUCTION BUILDING", 28, WorkshopTheme.White);
        ws.categoryText.characterSpacing = 10f;

        var glow = R("PreviewGlow", c, -610, 300, 560, 485);
        AddImage(glow, new Color(1f, 0.85f, 0.6f, 0.05f), raycast: false, sprite: hexFill).preserveAspect = true;
        var prevRt = R("Preview", c, -610, 250, 620, 620);
        var raw = prevRt.gameObject.AddComponent<RawImage>();
        raw.color = new Color(1, 1, 1, 0);
        ws.preview = prevRt.gameObject.AddComponent<ModelPreview>();
        Txt("DragHint", c, -610, 872, 620, 30, "DRAG TO ROTATE", 20, WorkshopTheme.Muted, style: FontStyles.Normal).characterSpacing = 6f;
        ws.outputText = Txt("Output", c, -610, 910, 620, 52, "OUTPUT: SCOUT BOT", 30, WorkshopTheme.White);

        // slots (live in Modules 4 / 6), DRG puts these right next to the weapon
        ws.batterySlot = Slot("BatterySlot", c, -120, 262, "BATTERY", batteryIcon, out ws.batteryState, out _, out _);
        ws.diskSlot = Slot("DiskSlot", c, 80, 262, "MASTER DISK", diskIcon, out ws.diskState, out ws.diskIcon, out ws.diskLock);

        // stats
        var stats = R("Stats", c, -20, 470, 480, 440);
        AddImage(stats, WorkshopTheme.PanelDark, raycast: false);
        AddImage(R("StatsRule", stats, 0, 0, 480, 5), WorkshopTheme.Orange, raycast: false);
        var list = NewRect("List", stats);
        Stretch(list);
        var vl = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(20, 18, 18, 44);
        vl.spacing = 3;
        vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        ws.statsList = list;
        ws.statRowTemplate = StatRowTemplate(list);
        var foot = Txt("Footnote", stats, 0, 0, 440, 30, "Applies to newly built bots", 20, WorkshopTheme.Muted, style: FontStyles.Italic);
        var frt = foot.rectTransform;
        frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0f);
        frt.anchoredPosition = new Vector2(0, 26);

        // ================= RIGHT: tree =================
        AddImage(R("TreeBg", c, 605, 104, 670, 956), WorkshopTheme.Panel, raycast: false);
        Txt("TreeTitle", c, 485, 112, 410, 58, "UPGRADE TREE", 38, WorkshopTheme.White, TextAlignmentOptions.Left).characterSpacing = 4f;
        var badge = R("PointsBadge", c, 800, 118, 250, 48);
        ws.pointsBadge = AddImage(badge, WorkshopTheme.Orange, raycast: false, sprite: RoundSprite, sliced: true);
        var pt = NewRect("Text", badge); Stretch(pt);
        ws.pointsText = AddText(pt, "1 SKILL POINT", 24, WorkshopTheme.DarkText);
        AddImage(R("TreeRule", c, 605, 176, 640, 4), new Color(1f, 0.63f, 0.18f, 0.6f), raycast: false);

        var tree = R("Tree", c, 605, 186, 650, 560);
        ws.treeList = tree;
        ws.rowHeight = 112f;
        ws.hexSpacing = 160f;
        ws.tierRowTemplate = TierRowTemplate(tree, 650f, 104f);
        ws.hexTemplate = HexTemplate(tree, new Vector2(114, 99));

        // perk details
        var detail = R("Detail", c, 605, 758, 650, 148);
        AddImage(detail, WorkshopTheme.PanelDark, raycast: false);
        AddImage(R("DetailRule", detail, -322, 0, 6, 148), WorkshopTheme.Orange, raycast: false);
        ws.detailTitle = Txt("Title", detail, -95, 12, 420, 44, "UPGRADE TREE", 32, WorkshopTheme.Orange, TextAlignmentOptions.Left);
        ws.detailBody = Txt("Body", detail, -95, 56, 420, 86, "", 24, WorkshopTheme.White, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        ws.detailBody.enableAutoSizing = true;
        ws.detailBody.fontSizeMin = 17; ws.detailBody.fontSizeMax = 24;
        ws.equipButton = Btn("Equip", detail, "EQUIP", WorkshopTheme.Orange, WorkshopTheme.DarkText, 34, new Vector2(0.5f, 1f), new Vector2(215, -74), new Vector2(190, 96));
        ws.equipLabel = ws.equipButton.GetComponentInChildren<TMP_Text>();

        // factory level
        ws.levelText = Txt("Level", c, 475, 920, 390, 42, "FACTORY LEVEL 1 / 6", 28, WorkshopTheme.White, TextAlignmentOptions.Left);
        var pips = R("Pips", c, 475, 972, 390, 24);
        var hl = pips.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
        ws.pipRow = pips;
        var pip = NewRect("PipTemplate", pips);
        ws.pipTemplate = AddImage(pip, WorkshopTheme.LockedFill, raycast: false, sprite: RoundSprite, sliced: true);
        pip.gameObject.SetActive(false);

        ws.levelUpButton = Btn("LevelUp", c, "LEVEL UP", WorkshopTheme.ButtonGreen, Color.white, 32, new Vector2(0.5f, 1f), new Vector2(800, -968), new Vector2(250, 100));
        ws.levelUpLabel = ws.levelUpButton.GetComponentInChildren<TMP_Text>();
        ws.levelUpLabel.lineSpacing = -20f;

        // ---------------- toast (over the showcase) ----------------
        ws.toastText = Txt("Toast", c, -345, 560, 1100, 80, "", 46, WorkshopTheme.Yellow);
        ws.toastText.outlineWidth = 0.25f;
        ws.toastText.outlineColor = new Color32(20, 15, 10, 255);
        var tc = ws.toastText.color; tc.a = 0f; ws.toastText.color = tc;

        root.gameObject.SetActive(false);
        return ws;
    }

    static Button Slot(string name, Transform parent, float x, float yTop, string caption, Sprite icon,
                       out TMP_Text state, out Image iconImg, out GameObject lockGo)
    {
        var rt = R(name, parent, x, yTop, 150, 150);
        var frame = AddImage(rt, WorkshopTheme.PanelDark);
        var ol = rt.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(1f, 1f, 1f, 0.35f);
        ol.effectDistance = new Vector2(3, -3);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = frame;

        var ic = R("Icon", rt, 0, 16, 90, 90);
        iconImg = AddImage(ic, WorkshopTheme.Muted, raycast: false, sprite: icon);
        iconImg.preserveAspect = true;
        state = Txt("State", rt, 0, 110, 146, 32, "EMPTY", 22, WorkshopTheme.Muted);

        var lk = R("Lock", rt, 56, -14, 52, 52);
        AddImage(lk, WorkshopTheme.Orange, raycast: false, sprite: lockIcon).preserveAspect = true;
        lockGo = lk.gameObject;
        lockGo.SetActive(false);

        Txt("Caption", parent, x, yTop + 158, 200, 30, caption, 22, WorkshopTheme.White).characterSpacing = 2f;
        return b;
    }

    static StatRow StatRowTemplate(Transform parent)
    {
        var rt = NewRect("StatRowTemplate", parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 46;
        var row = rt.gameObject.AddComponent<StatRow>();

        var l = NewRect("Label", rt); Anchor(l, 0f, 0.55f);
        row.label = AddText(l, "Move Speed", 26, new Color(0.95f, 0.93f, 0.89f, 0.85f), FontStyles.Normal);
        row.label.alignment = TextAlignmentOptions.Left;
        var v = NewRect("Value", rt); Anchor(v, 0.55f, 0.83f);
        row.value = AddText(v, "3.5 m/s", 28, WorkshopTheme.Orange);
        row.value.alignment = TextAlignmentOptions.Right;
        var d = NewRect("Delta", rt); Anchor(d, 0.85f, 1f);
        row.delta = AddText(d, "", 24, WorkshopTheme.Green);
        row.delta.alignment = TextAlignmentOptions.Left;
        rt.gameObject.SetActive(false);
        return row;
    }

    static TierRow TierRowTemplate(Transform parent, float width, float height)
    {
        var rt = R("TierRowTemplate", parent, 0, 0, width, height);
        var row = rt.gameObject.AddComponent<TierRow>();
        var bg = NewRect("Background", rt); Stretch(bg);
        row.background = AddImage(bg, WorkshopTheme.Panel, raycast: false);

        float badgeX = -width * 0.5f + 70f;
        var ring = Place(NewRect("BadgeRing", rt), new Vector2(0.5f, 0.5f), new Vector2(badgeX, 0), new Vector2(128, 110));
        row.badgeRing = AddImage(ring, WorkshopTheme.Orange, raycast: false, sprite: hexRing);
        row.badgeRing.preserveAspect = true;
        row.badgeRing.enabled = false;
        var badge = Place(NewRect("Badge", rt), new Vector2(0.5f, 0.5f), new Vector2(badgeX, 0), new Vector2(108, 93));
        row.badge = AddImage(badge, WorkshopTheme.White, raycast: false, sprite: hexFill);
        row.badge.preserveAspect = true;
        var bt = NewRect("Text", badge); Stretch(bt);
        row.badgeText = AddText(bt, "T1", 26, WorkshopTheme.DarkText);

        // options area = everything right of the badge
        float optLeft = badgeX + 70f, optRight = width * 0.5f - 10f;
        var opts = Place(NewRect("Options", rt), new Vector2(0.5f, 0.5f), new Vector2((optLeft + optRight) * 0.5f, 0), new Vector2(optRight - optLeft, height));
        row.options = opts;
        var line = Place(NewRect("Line", opts), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 8));
        row.line = AddImage(line, WorkshopTheme.Orange, raycast: false);

        rt.gameObject.SetActive(false);
        return row;
    }

    static PerkHex HexTemplate(Transform parent, Vector2 size)
    {
        var rt = Place(NewRect("HexTemplate", parent), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        var hex = rt.gameObject.AddComponent<PerkHex>();
        var ring = Place(NewRect("Ring", rt), new Vector2(0.5f, 0.5f), Vector2.zero, size * 1.16f);
        hex.ring = AddImage(ring, Color.white, raycast: false, sprite: hexRing);
        hex.ring.preserveAspect = true;
        hex.ring.enabled = false;
        var fill = Place(NewRect("Fill", rt), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        hex.fill = AddImage(fill, WorkshopTheme.Orange, raycast: true, sprite: hexFill);
        hex.fill.preserveAspect = true;
        var g = NewRect("Glyph", rt); Stretch(g, 10);
        hex.glyph = AddText(g, "SPD", size.y * 0.29f, WorkshopTheme.DarkText);
        hex.glyph.characterSpacing = -2f;
        var ic = Place(NewRect("Icon", rt), new Vector2(0.5f, 0.5f), Vector2.zero, size * 0.55f);
        hex.icon = AddImage(ic, WorkshopTheme.DarkText, raycast: false);
        hex.icon.preserveAspect = true;
        ic.gameObject.SetActive(false);
        rt.gameObject.SetActive(false);
        return hex;
    }

    static WorldButton WorldBtn(string name, Transform parent, string text, Color bg, Color fg)
    {
        var b = Btn(name, parent, text, bg, fg, 44, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(330, 120), legacySprite: true);
        var sh = b.gameObject.AddComponent<Shadow>();
        sh.effectDistance = new Vector2(0, -8); sh.effectColor = new Color(0, 0, 0, 0.45f);
        var wb = b.gameObject.AddComponent<WorldButton>();
        wb.button = b;
        b.gameObject.SetActive(false);
        return wb;
    }

    // =====================================================================
    // generated sprites (white + alpha, tinted by Image.color)
    static void GenerateSprites()
    {
        EnsureFolder("Assets/UI");
        EnsureFolder(GenFolder);
        const float r = 109.5f, round = 12f;
        hexFill = MakeSprite("HexFill", 256, 222, (x, y) => SdHex(x, y, r - round) - round);
        hexRing = MakeSprite("HexRing", 256, 222, (x, y) =>
        {
            float d = SdHex(x, y, r - round) - round;
            return Mathf.Max(d, -(d + 12f));
        });
        lockIcon = MakeSprite("Lock", 96, 96, LockShape);
        batteryIcon = MakeSprite("Battery", 128, 128, BatteryShape);
        diskIcon = MakeSprite("Disk", 128, 128, DiskShape);
    }

    static Sprite MakeSprite(string name, int w, int h, Func<float, float, float> sdf)
    {
        string path = GenFolder + "/" + name + ".png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = sdf(x + 0.5f - w * 0.5f, y + 0.5f - h * 0.5f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255f));
            }
        tex.SetPixels32(px);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // exact signed distance to a flat-top hexagon with inradius r (Inigo Quilez)
    static float SdHex(float px, float py, float r)
    {
        const float kx = -0.866025404f, ky = 0.5f, kz = 0.577350269f;
        px = Mathf.Abs(px); py = Mathf.Abs(py);
        float d = Mathf.Min(kx * px + ky * py, 0f);
        px -= 2f * d * kx; py -= 2f * d * ky;
        px -= Mathf.Clamp(px, -kz * r, kz * r);
        py -= r;
        return Mathf.Sqrt(px * px + py * py) * Mathf.Sign(py);
    }

    static float SdBox(float x, float y, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(x) - hx + r, qy = Mathf.Abs(y) - hy + r;
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    static float LockShape(float x, float y)
    {
        float body = SdBox(x, y + 16f, 32f, 24f, 7f);
        body = Mathf.Max(body, -(Mathf.Sqrt(x * x + (y + 12f) * (y + 12f)) - 6f));   // keyhole
        const float cy = 6f, rad = 18f, t = 5.5f;
        float shackle = y >= cy
            ? Mathf.Abs(Mathf.Sqrt(x * x + (y - cy) * (y - cy)) - rad) - t
            : Mathf.Max(Mathf.Abs(Mathf.Abs(x) - rad) - t, -(y + 10f));
        return Mathf.Min(body, shackle);
    }

    static float BatteryShape(float x, float y)
    {
        float shell = Mathf.Abs(SdBox(x, y + 4f, 32f, 50f, 9f)) - 4f;
        float nub = SdBox(x, y - 52f, 13f, 7f, 3f);
        float d = Mathf.Min(shell, nub);
        for (int i = 0; i < 3; i++) d = Mathf.Min(d, SdBox(x, y + 30f - i * 26f, 20f, 9f, 3f));
        return d;
    }

    static float DiskShape(float x, float y)
    {
        float d = SdBox(x, y, 50f, 50f, 9f);
        d = Mathf.Max(d, -SdBox(x, y - 18f, 34f, 26f, 4f));      // label
        d = Mathf.Max(d, -SdBox(x - 10f, y + 36f, 8f, 10f, 2f)); // shutter slot
        return d;
    }

    // =====================================================================
    // helpers (positions: x = centre, yTop = distance from parent's top edge)
    static Sprite roundSprite;
    static Sprite RoundSprite => roundSprite != null ? roundSprite : (roundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"));

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static RectTransform R(string name, Transform parent, float x, float yTop, float w, float h)
        => Place(NewRect(name, parent), new Vector2(0.5f, 1f), new Vector2(x, -(yTop + h * 0.5f)), new Vector2(w, h));

    static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static void Stretch(RectTransform rt, float pad = 0f)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
    }

    static void Anchor(RectTransform rt, float xMin, float xMax)
    {
        rt.anchorMin = new Vector2(xMin, 0f); rt.anchorMax = new Vector2(xMax, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Image AddImage(RectTransform rt, Color c, bool raycast = true, Sprite sprite = null, bool sliced = false)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        if (sliced) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.5f; }
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    static TextMeshProUGUI AddText(RectTransform rt, string text, float size, Color c, FontStyles style = FontStyles.Bold)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = c; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        return t;
    }

    static TextMeshProUGUI Txt(string name, Transform parent, float x, float yTop, float w, float h, string text, float size, Color c,
                               TextAlignmentOptions align = TextAlignmentOptions.Center, FontStyles style = FontStyles.Bold)
    {
        var t = AddText(R(name, parent, x, yTop, w, h), text, size, c, style);
        t.alignment = align;
        return t;
    }

    static Button Btn(string name, Transform parent, string label, Color bg, Color fg, float fontSize,
                      Vector2 anchor, Vector2 pos, Vector2 size, bool legacySprite = false)
    {
        var rt = Place(NewRect(name, parent), anchor, pos, size);
        var img = AddImage(rt, bg, sprite: RoundSprite, sliced: true);
        if (legacySprite) img.pixelsPerUnitMultiplier = 0.3f;   // match the existing world buttons
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.pressedColor = new Color(0.78f, 0.78f, 0.78f);
        cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.8f);
        b.colors = cb;
        var lt = NewRect("Label", rt); Stretch(lt, 4);
        AddText(lt, label, fontSize, fg);
        return b;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void Save()
    {
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }
}
