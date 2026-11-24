using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// Builds the portrait (1080x1920) gameplay UI under "UI Canvas" from the GUI Pro - Fantasy RPG sprites:
/// HUD (currencies + materials top right, tools on the left, backpack button), the base upgrade panel,
/// the tool info popup and the inventory. Safe to re-run: it replaces the "MobileUI" object.
/// Everything it makes is plain scene objects, so sprites / sizes / positions can be changed by hand afterwards
/// (a re-run overwrites those hand changes).
public static class MobileUIBuilder
{
    const string Pack = "Assets/Layer Lab/GUI Pro-FantasyRPG/ResourcesData/";
    const string Sprites = Pack + "Sprites/Component/";
    const string Out = "Assets/UI/Mobile";

    static readonly Color PanelCol = new Color(0.34f, 0.24f, 0.33f, 0.97f);
    static readonly Color HeaderCol = new Color(0.47f, 0.40f, 0.66f);
    static readonly Color BorderCol = new Color(0.95f, 0.47f, 0.20f);
    static readonly Color PillCol = new Color(0.16f, 0.13f, 0.24f, 0.88f);
    static readonly Color RowCol = new Color(0f, 0f, 0f, 0.24f);
    static readonly Color Soft = new Color(1f, 1f, 1f, 0.78f);

    static TMP_FontAsset font;
    static Material fontMat;

    // ------------------------------------------------------------------ menu

    [MenuItem("Tools/AutoMars/Mobile UI/Build Mobile UI (HUD + Base Panel + Inventory)")]
    public static void Build()
    {
        var canvasGo = GameObject.Find("UI Canvas");
        if (canvasGo == null) { Debug.LogError("Mobile UI: no 'UI Canvas' in the scene"); return; }
        if (!AssetDatabase.IsValidFolder(Out)) AssetDatabase.CreateFolder("Assets/UI", "Mobile");
        LoadFont();

        // portrait canvas
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
        EditorUtility.SetDirty(scaler);
        // above the sand tank bar's own canvas (order 40), so popups cover it
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.sortingOrder = 50;
        EditorUtility.SetDirty(canvas);

        // the landscape HUD pieces this replaces
        foreach (var n in new[] { "HUD", "Minimap" })
        {
            var t = canvasGo.transform.Find(n);
            if (t != null) t.gameObject.SetActive(false);
        }
        var old = canvasGo.transform.Find("MobileUI");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // icons
        var coin = Icon("Icon_ItemIcons/Shadow/256/icon_coin");
        var gem = Icon("Icon_ItemIcons/Shadow/256/icon_gem_0_blue");
        var stone = Icon("Icon_EquipIcons/Shadow/256/equip_icon_stone");
        var bag = Icon("Icon_ItemIcons/Shadow/256/icon_bag");
        EnsureIcons(false, out var shovelIcon, out var pickIcon, out var sand);

        // player: tool levels
        var tools = Object.FindFirstObjectByType<PlayerTools>(FindObjectsInactive.Include);
        if (tools != null)
        {
            var tp = tools.GetComponent<ToolProgress>();
            if (tp == null) tp = Undo.AddComponent<ToolProgress>(tools.gameObject);
            tp.shovelIcon = shovelIcon;
            tp.pickaxeIcon = pickIcon;
            EditorUtility.SetDirty(tp);
            var pi = tools.GetComponent<PlayerInteractions>();
            if (pi != null) { pi.showBaseButton = false; EditorUtility.SetDirty(pi); }
        }

        // ---------------------------------------------------------------- root
        var root = Rect("MobileUI", canvasGo.transform);
        Stretch(root);
        var joy = canvasGo.transform.Find("Floating Joystick");
        root.SetSiblingIndex(joy != null ? joy.GetSiblingIndex() + 1 : 0);   // above the joystick, under the old popups
        var hud = root.gameObject.AddComponent<MobileHUD>();
        var safe = Rect("SafeArea", root);
        Stretch(safe);
        safe.gameObject.AddComponent<SafeAreaFitter>();

        var rows = new List<ResourceRow>();
        var shop = new List<Button>();

        // ---------------------------------------------------------------- top right: currencies + materials
        var top = Rect("TopRight", safe);
        Place(top, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -84f), new Vector2(270f, 10f));
        Column(top, 10f, TextAnchor.UpperRight);
        rows.Add(CurrencyBar(top, ResourceKind.Coins, coin, shop));
        rows.Add(CurrencyBar(top, ResourceKind.Gems, gem, shop));
        rows.Add(MaterialRow(top, ResourceKind.Sand, sand));
        rows.Add(MaterialRow(top, ResourceKind.Stone, stone));

        // ---------------------------------------------------------------- left: tools
        var toolCol = Rect("Tools", safe);
        Place(toolCol, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 430f), new Vector2(160f, 10f));
        Column(toolCol, 18f, TextAnchor.UpperLeft);
        ToolSlot(toolCol, ToolKind.Shovel, shovelIcon, 156f, true);
        ToolSlot(toolCol, ToolKind.Pickaxe, pickIcon, 156f, true);
        WeaponSlot(toolCol, RenderPistolIcon(false), 156f);

        // ---------------------------------------------------------------- right: inventory button
        var inv = Img("InventoryButton", safe, S("Button/Button_Circle_01_Purple"), Color.white, true);
        Place(inv.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 470f), new Vector2(150f, 150f));
        var invBtn = inv.gameObject.AddComponent<Button>();
        invBtn.targetGraphic = inv;
        var invIcon = Img("Icon", inv.transform, bag, Color.white);
        Stretch(invIcon.rectTransform, 24f);
        invIcon.preserveAspect = true;
        var invLabel = Txt("Label", inv.transform, "BAG", 30f, Color.white, TextAlignmentOptions.Center);
        Place(invLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(200f, 40f));

        // ---------------------------------------------------------------- base panel
        var basePanel = Panel("BasePanel", safe, new Vector2(840f, 500f), "TOOL UPGRADES", false, out _);
        Place(basePanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -400f), new Vector2(840f, 500f));
        UpgradeRow(basePanel, ToolKind.Shovel, shovelIcon, sand, stone, -118f);
        UpgradeRow(basePanel, ToolKind.Pickaxe, pickIcon, sand, stone, -304f);

        // ---------------------------------------------------------------- inventory
        var invRoot = Rect("Inventory", root);
        Stretch(invRoot);
        var invBack = Backdrop(invRoot);
        var invPanel = Panel("Panel", invRoot, new Vector2(1000f, 1280f), "INVENTORY", true, out var invClose);
        Place(invPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 1280f));

        Section(invPanel, "TOOLS", -132f);
        for (int i = 0; i < 2; i++)
        {
            var kind = (ToolKind)i;
            var slot = ToolSlot(invPanel, kind, i == 0 ? shovelIcon : pickIcon, 210f, false);
            Place((RectTransform)slot.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f + i * 236f, -190f), new Vector2(210f, 210f));
            var nm = Txt(kind + " Name", invPanel, ToolProgress.Name(kind), 28f, Soft, TextAlignmentOptions.Center);
            Place(nm.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f + i * 236f, -404f), new Vector2(210f, 40f));
        }
        var tapHint = Txt("Hint", invPanel, "Tap a tool for details", 26f, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineRight);
        Place(tapHint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-44f, -132f), new Vector2(500f, 44f));

        Section(invPanel, "MATERIALS", -470f);
        var kinds = new[] { ResourceKind.Coins, ResourceKind.Gems, ResourceKind.Sand, ResourceKind.Stone };
        var icons = new[] { coin, gem, sand, stone };
        for (int i = 0; i < kinds.Length; i++)
            rows.Add(MaterialTile(invPanel, kinds[i], icons[i], new Vector2(44f + i * 230f, -528f)));

        Section(invPanel, "PLAYER", -810f);
        var pRow = Img("Player Row", invPanel, S("Frame/BasicFrame_Square_l"), RowCol);
        Place(pRow.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -868f), new Vector2(912f, 330f));
        var xp = Txt("XP", pRow.transform, "XP  0", 52f, new Color(1f, 0.84f, 0.3f), TextAlignmentOptions.MidlineLeft);
        Place(xp.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -24f), new Vector2(600f, 70f));
        var note = Txt("Note", pRow.transform, "Player upgrades will live here.\nYou will buy them with XP, earned by completing tasks.\n<color=#FFFFFF80>Coming soon</color>", 30f, Soft, TextAlignmentOptions.TopLeft);
        note.textWrappingMode = TextWrappingModes.Normal;
        Place(note.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -110f), new Vector2(850f, 200f));

        // ---------------------------------------------------------------- tool info popup
        var infoRoot = Rect("ToolInfo", root);
        Stretch(infoRoot);
        var info = infoRoot.gameObject.AddComponent<ToolInfoPopup>();
        info.backdrop = Backdrop(infoRoot);
        var infoPanel = Panel("Panel", infoRoot, new Vector2(860f, 1130f), "SHOVEL", true, out var infoClose);
        Place(infoPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 1130f));
        info.panel = infoPanel;
        info.closeButton = infoClose;
        info.titleText = infoPanel.Find("Header/Title").GetComponent<TextMeshProUGUI>();
        info.levelText = Txt("Level", infoPanel, "LVL 1", 50f, new Color(1f, 0.84f, 0.3f), TextAlignmentOptions.Center);
        Place(info.levelText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(700f, 64f));
        var bigSlot = Img("Slot", infoPanel, S("Frame/ItemFrame_02_Bg_Single_Purple"), Color.white);
        Place(bigSlot.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -192f), new Vector2(300f, 300f));
        info.icon = Img("Icon", bigSlot.transform, shovelIcon, Color.white);
        Stretch(info.icon.rectTransform, 26f);
        info.icon.preserveAspect = true;
        info.statLabels = new TMP_Text[3];
        info.statValues = new TMP_Text[3];
        for (int i = 0; i < 3; i++)
        {
            var row = Img("Stat " + (i + 1), infoPanel, S("Frame/BasicFrame_Square_l"), RowCol);
            Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -520f - i * 82f), new Vector2(740f, 72f));
            info.statLabels[i] = Txt("Label", row.transform, "STAT", 32f, Soft, TextAlignmentOptions.MidlineLeft);
            Stretch(info.statLabels[i].rectTransform, 24f, 0f, 24f, 0f);
            info.statValues[i] = Txt("Value", row.transform, "0", 36f, Color.white, TextAlignmentOptions.MidlineRight);
            Stretch(info.statValues[i].rectTransform, 24f, 0f, 24f, 0f);
        }
        info.descText = Txt("Description", infoPanel, "", 28f, Soft, TextAlignmentOptions.Top);
        info.descText.textWrappingMode = TextWrappingModes.Normal;
        Place(info.descText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -776f), new Vector2(740f, 84f));
        info.nextText = Txt("Next", infoPanel, "", 30f, Color.white, TextAlignmentOptions.Top);
        info.nextText.textWrappingMode = TextWrappingModes.Normal;
        Place(info.nextText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -866f), new Vector2(740f, 90f));
        info.okButton = Btn("OK", infoPanel, S("Button/Button_Rectangle_01_Convex_Green"), "OK", 44f, out _);
        Place((RectTransform)info.okButton.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(320f, 116f));

        // ---------------------------------------------------------------- wire up
        hud.rows = rows.ToArray();
        hud.shopButtons = shop.ToArray();
        hud.xpText = xp;
        hud.basePanel = basePanel;
        hud.inventory = invRoot.gameObject;
        hud.inventoryButton = invBtn;
        hud.inventoryClose = invClose;
        hud.inventoryBackdrop = invBack;
        hud.toolInfo = info;
        // popups + the base panel start hidden (MobileHUD also hides them in Awake)
        invRoot.gameObject.SetActive(false);
        infoRoot.gameObject.SetActive(false);
        basePanel.gameObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
        Debug.Log("Mobile UI built under 'UI Canvas/MobileUI' (canvas is now 1080x1920, match width).");
    }

    [MenuItem("Tools/AutoMars/Mobile UI/Re-render Tool Icons (from the player's models)")]
    public static void RenderIconsMenu()
    {
        EnsureIcons(true, out var shovel, out var pick, out _);
        var tp = Object.FindFirstObjectByType<ToolProgress>(FindObjectsInactive.Include);
        if (tp != null) { tp.shovelIcon = shovel; tp.pickaxeIcon = pick; EditorUtility.SetDirty(tp); }
    }

    // ------------------------------------------------------------------ pieces

    static ResourceRow CurrencyBar(Transform parent, ResourceKind kind, Sprite icon, List<Button> shop)
    {
        var r = Rect(kind + " Bar", parent);
        r.sizeDelta = new Vector2(262f, 68f);
        var bg = Img("Bg", r, S("UI_Etc/Resource_Bg"), PillCol);
        Stretch(bg.rectTransform);
        var val = Txt("Value", r, "0", 40f, Color.white, TextAlignmentOptions.Midline);
        Stretch(val.rectTransform, 62f, 0f, 62f, 0f);
        var ic = Img("Icon", r, icon, Color.white);
        Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(24f, 0f), new Vector2(84f, 84f));
        ic.preserveAspect = true;
        var add = Img("Add", r, S("Button/Button_Circle_01_Green"), Color.white, true);
        Place(add.rectTransform, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-30f, 0f), new Vector2(60f, 60f));
        var plus = Img("Plus", add.transform, S("IconMisc/Icon_Add_s"), Color.white);
        Stretch(plus.rectTransform, 15f);
        var b = add.gameObject.AddComponent<Button>();
        b.targetGraphic = add;
        shop.Add(b);
        var row = r.gameObject.AddComponent<ResourceRow>();
        row.kind = kind;
        row.valueText = val;
        return row;
    }

    static ResourceRow MaterialRow(Transform parent, ResourceKind kind, Sprite icon)
    {
        var r = Rect(kind + " Row", parent);
        r.sizeDelta = new Vector2(196f, 60f);
        var bg = Img("Bg", r, S("UI_Etc/Resource_Bg"), PillCol);
        Stretch(bg.rectTransform);
        var val = Txt("Value", r, "0", 36f, Color.white, TextAlignmentOptions.Midline);
        Stretch(val.rectTransform, 16f, 0f, 66f, 0f);
        var ic = Img("Icon", r, icon, Color.white);
        Place(ic.rectTransform, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-30f, 0f), new Vector2(76f, 76f));
        ic.preserveAspect = true;
        var row = r.gameObject.AddComponent<ResourceRow>();
        row.kind = kind;
        row.valueText = val;
        row.hideWhenZero = true;
        return row;
    }

    static ResourceRow MaterialTile(Transform parent, ResourceKind kind, Sprite icon, Vector2 pos)
    {
        var bg = Img(kind + " Tile", parent, S("Frame/ItemFrame_02_Bg_Single_Brown"), Color.white);
        Place(bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(210f, 210f));
        var ic = Img("Icon", bg.transform, icon, Color.white);
        Stretch(ic.rectTransform, 34f, 46f, 34f, 22f);
        ic.preserveAspect = true;
        var val = Txt("Value", bg.transform, "0", 40f, Color.white, TextAlignmentOptions.Center);
        Place(val.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(190f, 48f));
        var nm = Txt("Name", bg.transform, Economy.Name(kind), 28f, Soft, TextAlignmentOptions.Center);
        Place(nm.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(210f, 40f));
        var row = bg.gameObject.AddComponent<ResourceRow>();
        row.kind = kind;
        row.valueText = val;
        return row;
    }

    static ToolSlotView ToolSlot(Transform parent, ToolKind kind, Sprite icon, float size, bool showActive)
    {
        var bg = Img(kind + " Slot", parent, S("Frame/ItemFrame_02_Bg_Single_Blue"), Color.white, true);
        bg.rectTransform.sizeDelta = new Vector2(size, size);
        var view = bg.gameObject.AddComponent<ToolSlotView>();
        view.kind = kind;
        view.button = bg.gameObject.AddComponent<Button>();
        view.button.targetGraphic = bg;
        if (showActive)
        {
            var focus = Img("In Hands", bg.transform, S("Frame/ItemFrame_02_Focus_Yellow"), Color.white);
            Stretch(focus.rectTransform, -4f);
            view.activeFrame = focus.gameObject;
        }
        view.icon = Img("Icon", bg.transform, icon, Color.white);
        Stretch(view.icon.rectTransform, size * 0.1f, size * 0.16f, size * 0.1f, size * 0.06f);
        view.icon.preserveAspect = true;
        view.levelText = Txt("Level", bg.transform, "LVL 1", size * 0.2f, Color.white, TextAlignmentOptions.Center);
        Place(view.levelText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, size * 0.07f), new Vector2(size, size * 0.26f));
        var arrow = Img("Upgrade Arrow", bg.transform, S("IconMisc/Icon_Booster_Up"), new Color(0.45f, 1f, 0.35f));
        Place(arrow.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(size * 0.2f, -size * 0.2f), new Vector2(size * 0.3f, size * 0.3f));
        arrow.preserveAspect = true;
        view.upgradeArrow = arrow.rectTransform;
        return view;
    }

    /// The weapon tile: the gun's icon, one pip per bullet and the reload wipe (see WeaponSlotView).
    static WeaponSlotView WeaponSlot(Transform parent, Sprite icon, float size)
    {
        const int Bullets = 6;
        var bg = Img("Weapon Slot", parent, S("Frame/ItemFrame_02_Bg_Single_Blue"), new Color(1f, 0.8f, 0.78f), true);
        bg.rectTransform.sizeDelta = new Vector2(size, size);
        var view = bg.gameObject.AddComponent<WeaponSlotView>();
        view.group = bg.gameObject.AddComponent<CanvasGroup>();
        view.button = bg.gameObject.AddComponent<Button>();
        view.button.targetGraphic = bg;
        var focus = Img("In Hands", bg.transform, S("Frame/ItemFrame_02_Focus_Yellow"), Color.white);
        Stretch(focus.rectTransform, -4f);
        view.activeFrame = focus.gameObject;
        view.icon = Img("Icon", bg.transform, icon, Color.white);
        Stretch(view.icon.rectTransform, size * 0.1f, size * 0.22f, size * 0.1f, size * 0.05f);
        view.icon.preserveAspect = true;

        // reload: a dark wipe that runs around the tile and uncovers the icon
        var wipe = Img("Reload", bg.transform, bg.sprite, new Color(0.05f, 0.03f, 0.08f, 0.62f));
        Stretch(wipe.rectTransform, 5f);
        wipe.type = Image.Type.Filled;
        wipe.fillMethod = Image.FillMethod.Radial360;
        wipe.fillOrigin = (int)Image.Origin360.Top;
        wipe.fillClockwise = false;
        view.reloadFill = wipe;

        // one pip per bullet along the bottom (drawn over the wipe, so they stay readable while reloading)
        var dot = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        var row = Rect("Pips", bg.transform);
        Place(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, size * 0.085f), new Vector2(size * 0.84f, size * 0.18f));
        float w = size * 0.1f, h = size * 0.15f, step = size * 0.13f;
        view.pips = new Image[Bullets];
        for (int i = 0; i < Bullets; i++)
        {
            var back = Img("Pip " + (i + 1), row, dot, new Color(0.1f, 0.07f, 0.13f, 0.95f));
            Place(back.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((i - (Bullets - 1) * 0.5f) * step, 0f), new Vector2(w + 6f, h + 6f));
            var pip = Img("Bullet", back.transform, dot, new Color(1f, 0.84f, 0.3f));
            Place(pip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            view.pips[i] = pip;
        }

        var flash = Img("Flash", bg.transform, bg.sprite, new Color(1f, 1f, 1f, 0f));
        Stretch(flash.rectTransform, 2f);
        view.flash = flash;
        flash.gameObject.SetActive(false);
        wipe.gameObject.SetActive(false);
        return view;
    }

    /// Adds (or replaces) only the weapon tile in an existing mobile UI, so hand edits to the rest are kept.
    [MenuItem("Tools/AutoMars/Mobile UI/Add Weapon Tile (keeps the rest of the UI)")]
    public static void AddWeaponSlot()
    {
        var canvasGo = GameObject.Find("UI Canvas");
        var col = canvasGo != null ? canvasGo.transform.Find("MobileUI/SafeArea/Tools") : null;
        if (col == null) { Debug.LogError("Mobile UI: no 'UI Canvas/MobileUI/SafeArea/Tools' in the scene (build the mobile UI first)"); return; }
        LoadFont();
        var old = col.Find("Weapon Slot");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        WeaponSlot(col, RenderPistolIcon(false), 156f);
        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
    }

    /// Icon_Pistol.png, rendered from the pistol model on the player (side view, barrel to the upper right).
    public static Sprite RenderPistolIcon(bool force)
    {
        if (!AssetDatabase.IsValidFolder(Out)) AssetDatabase.CreateFolder("Assets/UI", "Mobile");
        string path = Out + "/Icon_Pistol.png";
        var pistol = Object.FindFirstObjectByType<PlayerPistol>(FindObjectsInactive.Include);
        if (pistol != null && pistol.model != null && (force || !File.Exists(path)))
            RenderModel(pistol.model.gameObject, path, Vector3.left, new Vector3(0f, 1f, 0.4f).normalized);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void UpgradeRow(RectTransform panel, ToolKind kind, Sprite icon, Sprite sand, Sprite stone, float y)
    {
        var bg = Img(kind + " Upgrade", panel, S("Frame/BasicFrame_Square_l"), RowCol);
        Place(bg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(792f, 172f));
        var row = bg.gameObject.AddComponent<ToolUpgradeRow>();
        row.kind = kind;

        var slot = Img("Slot", bg.transform, S("Frame/ItemFrame_02_Bg_Single_Blue"), Color.white);
        Place(slot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(146f, 146f));
        row.icon = Img("Icon", slot.transform, icon, Color.white);
        Stretch(row.icon.rectTransform, 14f, 22f, 14f, 8f);
        row.icon.preserveAspect = true;
        row.slotLevelText = Txt("Level", slot.transform, "LVL 1", 28f, Color.white, TextAlignmentOptions.Center);
        Place(row.slotLevelText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(146f, 36f));

        row.titleText = Txt("Title", bg.transform, ToolProgress.Name(kind), 40f, Color.white, TextAlignmentOptions.MidlineLeft);
        Place(row.titleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(178f, -10f), new Vector2(400f, 50f));
        row.statText = Txt("Stat", bg.transform, "", 27f, Soft, TextAlignmentOptions.MidlineLeft);
        Place(row.statText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(178f, -60f), new Vector2(400f, 38f));

        row.sandChip = CostChip(bg.transform, "Sand Cost", sand, new Vector2(178f, -106f), out row.sandText);
        row.stoneChip = CostChip(bg.transform, "Stone Cost", stone, new Vector2(372f, -106f), out row.stoneText);

        row.canSprite = S("Button/Button_Rectangle_01_Convex_Green");
        row.cantSprite = S("Button/Button_Rectangle_01_Convex_Gray");
        row.button = Btn("Upgrade", bg.transform, row.canSprite, "UPGRADE", 32f, out var label);
        Place((RectTransform)row.button.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(206f, 108f));
        row.buttonImage = row.button.GetComponent<Image>();
        row.buttonLabel = label;
    }

    static GameObject CostChip(Transform parent, string name, Sprite icon, Vector2 pos, out TMP_Text text)
    {
        var bg = Img(name, parent, S("UI_Etc/Resource_Bg"), PillCol);
        Place(bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(184f, 52f));
        var ic = Img("Icon", bg.transform, icon, Color.white);
        Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(26f, 0f), new Vector2(60f, 60f));
        ic.preserveAspect = true;
        var t = Txt("Value", bg.transform, "0/0", 28f, Color.white, TextAlignmentOptions.Midline);
        Stretch(t.rectTransform, 52f, 0f, 12f, 0f);
        text = t;
        return bg.gameObject;
    }

    static void Section(Transform panel, string title, float y)
    {
        var t = Txt(title + " Title", panel, title, 34f, new Color(1f, 0.84f, 0.3f), TextAlignmentOptions.MidlineLeft);
        Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, y), new Vector2(500f, 44f));
        var line = Img(title + " Line", panel, null, new Color(1f, 1f, 1f, 0.18f));
        Place(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - 46f), new Vector2(912f, 3f));
    }

    static Button Backdrop(RectTransform root)
    {
        var img = Img("Backdrop", root, null, new Color(0f, 0f, 0f, 0.68f), true);
        Stretch(img.rectTransform);
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        return b;
    }

    static RectTransform Panel(string name, Transform parent, Vector2 size, string title, bool withClose, out Button close)
    {
        var p = Rect(name, parent);
        p.sizeDelta = size;
        var bg = Img("Bg", p, S("Popup/Popup_02_White_Bg"), PanelCol, true);   // swallows taps, so they don't reach the backdrop
        Stretch(bg.rectTransform);
        var border = Img("Border", p, S("Popup/Popup_02_White_Border"), BorderCol);
        Stretch(border.rectTransform, -2f);
        var head = Img("Header", p, S("Frame/BasicFrame_Square_l"), HeaderCol);
        head.rectTransform.anchorMin = new Vector2(0f, 1f);
        head.rectTransform.anchorMax = new Vector2(1f, 1f);
        head.rectTransform.pivot = new Vector2(0.5f, 1f);
        head.rectTransform.offsetMin = new Vector2(14f, -100f);
        head.rectTransform.offsetMax = new Vector2(-14f, -14f);
        var t = Txt("Title", head.transform, title, 46f, Color.white, TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
        close = null;
        if (withClose)
        {
            close = Btn("Close", p, S("Button/Button_Circle_01_Red"), null, 0f, out _);
            Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-30f, -30f), new Vector2(96f, 96f));
            var x = Img("X", close.transform, S("IconMisc/Icon_Close"), Color.white);
            Stretch(x.rectTransform, 28f);
        }
        return p;
    }

    // ------------------------------------------------------------------ helpers

    static void Column(RectTransform r, float spacing, TextAnchor align)
    {
        var vl = r.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = spacing;
        vl.childAlignment = align;
        vl.childControlWidth = vl.childControlHeight = false;
        vl.childForceExpandWidth = vl.childForceExpandHeight = false;
        var fit = r.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void Place(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = pivot;
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    static void Stretch(RectTransform r, float inset = 0f) => Stretch(r, inset, inset, inset, inset);

    static void Stretch(RectTransform r, float left, float bottom, float right, float top)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = new Vector2(left, bottom);
        r.offsetMax = new Vector2(-right, -top);
    }

    static Image Img(string name, Transform parent, Sprite s, Color c, bool raycast = false)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        img.sprite = s;
        img.color = c;
        img.raycastTarget = raycast;
        if (s != null && s.border != Vector4.zero) img.type = Image.Type.Sliced;
        return img;
    }

    static TextMeshProUGUI Txt(string name, Transform parent, string text, float size, Color c, TextAlignmentOptions align)
    {
        var t = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        if (fontMat != null) t.fontSharedMaterial = fontMat;
        t.text = text;
        t.fontSize = size;
        t.color = c;
        t.alignment = align;
        t.raycastTarget = false;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    static Button Btn(string name, Transform parent, Sprite s, string label, float fontSize, out TextMeshProUGUI text)
    {
        var img = Img(name, parent, s, Color.white, true);
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        text = null;
        if (label != null)
        {
            text = Txt("Label", img.transform, label, fontSize, Color.white, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 4f, 10f, 4f, 2f);
        }
        return b;
    }

    static Sprite S(string path)
    {
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(Sprites + path + ".png");
        if (s == null) Debug.LogWarning("Mobile UI: sprite not found: " + Sprites + path + ".png");
        return s;
    }

    static Sprite Icon(string path) => S(path);

    static void LoadFont()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Pack + "Fonts/Alata-Regular SDF.asset");
        if (font == null) font = TMP_Settings.defaultFontAsset;
        string p = Out + "/HUD Text Outline.mat";
        fontMat = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (fontMat == null)
        {
            fontMat = new Material(font.material);
            AssetDatabase.CreateAsset(fontMat, p);
        }
        fontMat.EnableKeyword("OUTLINE_ON");
        fontMat.SetFloat("_FaceDilate", 0.1f);
        fontMat.SetFloat("_OutlineWidth", 0.22f);
        fontMat.SetColor("_OutlineColor", new Color(0.13f, 0.09f, 0.16f, 1f));
        if (fontMat.HasProperty("_UnderlayColor"))
        {
            fontMat.EnableKeyword("UNDERLAY_ON");
            fontMat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.55f));
            fontMat.SetFloat("_UnderlayOffsetX", 0f);
            fontMat.SetFloat("_UnderlayOffsetY", -0.7f);
            fontMat.SetFloat("_UnderlayDilate", 0.3f);
            fontMat.SetFloat("_UnderlaySoftness", 0.1f);
        }
        EditorUtility.SetDirty(fontMat);
    }

    // ------------------------------------------------------------------ icons rendered from the 3D models

    static void EnsureIcons(bool force, out Sprite shovel, out Sprite pickaxe, out Sprite sand)
    {
        if (!AssetDatabase.IsValidFolder(Out)) AssetDatabase.CreateFolder("Assets/UI", "Mobile");
        string sp = Out + "/Icon_Shovel.png", pp = Out + "/Icon_Pickaxe.png", dp = Out + "/Icon_Sand.png";
        var tools = Object.FindFirstObjectByType<PlayerTools>(FindObjectsInactive.Include);
        if (tools != null && tools.shovelModel != null && (force || !File.Exists(sp))) RenderModel(tools.shovelModel, sp);
        if (tools != null && tools.pickaxe != null && (force || !File.Exists(pp))) RenderModel(tools.pickaxe, pp);
        if (force || !File.Exists(dp)) RenderSand(dp);
        shovel = AssetDatabase.LoadAssetAtPath<Sprite>(sp);
        pickaxe = AssetDatabase.LoadAssetAtPath<Sprite>(pp);
        sand = AssetDatabase.LoadAssetAtPath<Sprite>(dp);
    }

    const int IconLayer = 31;
    static readonly Vector3 Far = new Vector3(0f, -500f, 0f);

    /// The player's tool models have their handle along local Y and are thin along local X.
    static void RenderModel(GameObject source, string path) => RenderModel(source, path, Vector3.right, new Vector3(0f, 1f, 1f).normalized);   // handle runs from bottom-left to top-right

    static void RenderModel(GameObject source, string path, Vector3 viewDir, Vector3 up)
    {
        var clone = Object.Instantiate(source);
        clone.hideFlags = HideFlags.HideAndDontSave;
        clone.transform.SetParent(null, false);
        clone.transform.position = Far;
        clone.transform.rotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        clone.SetActive(true);
        // colours live in property blocks, which Instantiate does not copy
        var src = source.GetComponentsInChildren<Renderer>(true);
        var dst = clone.GetComponentsInChildren<Renderer>(true);
        var mpb = new MaterialPropertyBlock();
        for (int i = 0; i < src.Length && i < dst.Length; i++)
        {
            src[i].GetPropertyBlock(mpb);
            dst[i].SetPropertyBlock(mpb);
        }
        foreach (var tr in clone.GetComponentsInChildren<TrailRenderer>(true)) Object.DestroyImmediate(tr);
        Save(Shoot(clone, viewDir, up, 1.12f, -1f), path);
        Object.DestroyImmediate(clone);
    }

    static void RenderSand(string path)
    {
        var root = new GameObject("Sand Icon") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = Far;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", new Color(1f, 0.80f, 0.38f));
        mat.SetFloat("_Smoothness", 0.05f);
        var parts = new[]
        {
            new Vector4(0f, 0f, 0f, 1.5f), new Vector4(0.62f, 0f, 0.25f, 0.95f), new Vector4(-0.6f, 0f, 0.2f, 0.85f), new Vector4(0.1f, 0f, -0.45f, 0.8f),
        };
        foreach (var p in parts)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(s.GetComponent<Collider>());
            s.hideFlags = HideFlags.HideAndDontSave;
            s.transform.SetParent(root.transform, false);
            s.transform.localPosition = new Vector3(p.x, 0f, p.z);
            s.transform.localScale = new Vector3(p.w, p.w * 0.95f, p.w);
            s.GetComponent<Renderer>().sharedMaterial = mat;
        }
        // looking a little down at the pile; everything under the ground line (y = 0) is cut away
        Save(Shoot(root, new Vector3(0f, -0.35f, 1f).normalized, Vector3.up, 1.12f, Far.y), path);
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(mat);
    }

    /// Renders the object on a transparent background (two shots, black and white, give the alpha) and adds a dark outline.
    /// cutBelowY > -1: pixels showing geometry under that world height are removed (a flat bottom for the sand pile).
    static Texture2D Shoot(GameObject root, Vector3 viewDir, Vector3 up, float pad, float cutBelowY)
    {
        const int N = 512;
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IconLayer;
        var rends = root.GetComponentsInChildren<MeshRenderer>();
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        bool cut = cutBelowY > -1f || cutBelowY < -1f;
        cut = cutBelowY != -1f;
        if (cut) b.SetMinMax(new Vector3(b.min.x, cutBelowY, b.min.z), b.max);

        var camGo = new GameObject("Icon Camera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.cullingMask = 1 << IconLayer;
        cam.allowHDR = false;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 40f;
        cam.transform.rotation = Quaternion.LookRotation(viewDir, up);
        cam.transform.position = b.center - viewDir * 15f;
        float m = 0.01f;
        for (int i = 0; i < 8; i++)
        {
            var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var lp = cam.transform.InverseTransformPoint(corner);
            m = Mathf.Max(m, Mathf.Abs(lp.x), Mathf.Abs(lp.y));
        }
        cam.orthographicSize = m * pad;

        var lightGo = new GameObject("Icon Light") { hideFlags = HideFlags.HideAndDontSave };
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.cullingMask = 1 << IconLayer;
        light.transform.rotation = cam.transform.rotation * Quaternion.Euler(35f, -30f, 0f);

        var rt = RenderTexture.GetTemporary(N, N, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 8);
        cam.targetTexture = rt;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        Color[] Shot(Color bg)
        {
            cam.backgroundColor = bg;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, N, N), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex.GetPixels();
        }
        var black = Shot(Color.black);
        var white = Shot(Color.white);
        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);

        // the screen row of the ground line (orthographic: one row for the whole line when looking along z)
        int cutRow = -1;
        if (cut)
        {
            var vp = cam.WorldToViewportPoint(new Vector3(b.center.x, cutBelowY, b.center.z));
            cutRow = Mathf.RoundToInt(vp.y * N);
        }
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(lightGo);

        var px = new Color[N * N];
        for (int i = 0; i < px.Length; i++)
        {
            float a = 1f - ((white[i].r - black[i].r) + (white[i].g - black[i].g) + (white[i].b - black[i].b)) / 3f;
            a = Mathf.Clamp01(a);
            if (cutRow >= 0 && i / N < cutRow) a = 0f;
            px[i] = a > 0.004f ? new Color(Mathf.Clamp01(black[i].r / a), Mathf.Clamp01(black[i].g / a), Mathf.Clamp01(black[i].b / a), a) : new Color(0f, 0f, 0f, 0f);
        }
        px = Outline(px, N, 9, new Color(0.13f, 0.09f, 0.16f));
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Color[] Outline(Color[] src, int n, int radius, Color col)
    {
        var offs = new List<Vector2Int>();
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
            if (x * x + y * y <= radius * radius) offs.Add(new Vector2Int(x, y));
        var dst = new Color[src.Length];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            var c = src[y * n + x];
            float m = c.a;
            if (m < 0.999f)
                for (int k = 0; k < offs.Count && m < 0.999f; k++)
                {
                    int xx = x + offs[k].x, yy = y + offs[k].y;
                    if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                    float a = src[yy * n + xx].a;
                    if (a > m) m = a;
                }
            var rgb = Color.Lerp(col, c, c.a);
            dst[y * n + x] = new Color(rgb.r, rgb.g, rgb.b, m);
        }
        return dst;
    }

    static void Save(Texture2D tex, string path)
    {
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.maxTextureSize = 512;
        imp.SaveAndReimport();
    }
}
