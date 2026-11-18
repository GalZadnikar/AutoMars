using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// Module 5: landscape bot popup in the Workshop style (robot preview, battery, stats,
/// program slot + disks, FOLLOW toggle). Replaces the old portrait popup. Safe to re-run.
public static class BotPopupBuilder
{
    static readonly Vector2 Design = new Vector2(1640, 880);

    [MenuItem("Tools/Trash Game/7. Bot Popup v2 (Follow)")]
    public static void BuildMenu()
    {
        var canvas = GameObject.Find("UI Canvas").transform;
        var bp = Build(canvas);
        var pi = GameObject.Find("Player").GetComponent<PlayerInteractions>();
        pi.botPopup = bp;
        EditorUtility.SetDirty(pi);
        // keep the Workshop on top of it
        var ws = canvas.Find("Workshop");
        if (ws != null) ws.SetAsLastSibling();

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Bot popup v2 built (landscape, Follow toggle).");
    }

    /// Also used by TrashGameBuilder "2. Build UI".
    public static BotProgramPopup Build(Transform canvas)
    {
        var old = canvas.Find("BotProgramPopup");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = NewRect("BotProgramPopup", canvas);
        Stretch(root);
        AddImage(root, new Color(0f, 0f, 0f, 0.6f));
        var bp = root.gameObject.AddComponent<BotProgramPopup>();
        bp.dragLayer = root;

        var panel = NewRect("Panel", root);
        panel.sizeDelta = Design;
        var fit = panel.gameObject.AddComponent<FitToSafeArea>();
        fit.designSize = Design; fit.maxScale = 1f; fit.margin = 20f;
        AddImage(panel, WorkshopTheme.Bg);
        AddImage(R("TopRule", panel, 0, 0, Design.x, 6), WorkshopTheme.Orange, false);

        // ---------- header ----------
        bp.titleText = Txt("Title", panel, -380, 20, 820, 64, "SCOUT BOT", 54, WorkshopTheme.Orange, TextAlignmentOptions.Left);
        bp.titleText.characterSpacing = -1f;
        bp.statusText = Txt("Status", panel, -380, 82, 820, 34, "", 26, WorkshopTheme.Muted, TextAlignmentOptions.Left, FontStyles.Normal);
        bp.closeButton = Btn("Close", panel, "CLOSE", WorkshopTheme.Orange, WorkshopTheme.DarkText, 34, new Vector2(0.5f, 1f), new Vector2(690, -58), new Vector2(210, 72));
        AddImage(R("Rule", panel, 0, 124, Design.x - 40, 4), new Color(1f, 0.63f, 0.18f, 0.6f), false);

        // ---------- left: robot + battery + stats ----------
        AddImage(R("RobotBg", panel, -560, 144, 480, 450), new Color(0f, 0f, 0f, 0.22f), false);
        var prev = R("Preview", panel, -560, 150, 440, 440);
        prev.gameObject.AddComponent<RawImage>().color = new Color(1, 1, 1, 0);
        bp.preview = prev.gameObject.AddComponent<ModelPreview>();
        bp.preview.rigPosition = new Vector3(0f, 700f, 0f);   // own rig above the Workshop's (same x/z: fog of war shades by map position)
        Txt("BatteryCaption", panel, -560, 608, 480, 34, "BATTERY", 24, WorkshopTheme.White, TextAlignmentOptions.Left).characterSpacing = 4f;
        var barBg = R("BatteryBar", panel, -560, 646, 480, 30);
        AddImage(barBg, WorkshopTheme.PanelDark, false);
        var fill = NewRect("Fill", barBg);
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0.6f, 1f);
        fill.offsetMin = new Vector2(3, 3); fill.offsetMax = new Vector2(0, -3);
        bp.batteryFill = AddImage(fill, WorkshopTheme.Green, false);
        bp.batteryText = Txt("BatteryText", panel, -560, 682, 480, 34, "", 24, WorkshopTheme.White, TextAlignmentOptions.Left, FontStyles.Normal);
        bp.statsText = Txt("Stats", panel, -560, 726, 480, 140, "", 24, new Color(0.95f, 0.93f, 0.89f, 0.9f), TextAlignmentOptions.TopLeft, FontStyles.Normal);
        bp.statsText.lineSpacing = 6f;

        // ---------- middle: program slot + follow ----------
        Txt("SlotCaption", panel, -40, 144, 460, 40, "PROGRAM SLOT", 28, WorkshopTheme.Cyan).characterSpacing = 4f;
        var slot = R("RobotSlot", panel, -40, 192, 300, 300);
        AddImage(slot, WorkshopTheme.PanelDark);
        var ol = slot.gameObject.AddComponent<Outline>(); ol.effectColor = WorkshopTheme.Cyan; ol.effectDistance = new Vector2(4, -4);
        var dz = slot.gameObject.AddComponent<DiskDropZone>(); dz.isRobotSlot = true;
        bp.robotSlot = slot;
        var hint = NewRect("Hint", slot); Stretch(hint, 20);
        bp.slotHint = AddText(hint, "Drag a disk here", 30, WorkshopTheme.Muted, FontStyles.Normal);

        var follow = R("Follow", panel, -40, 536, 440, 120);
        bp.followBg = AddImage(follow, WorkshopTheme.ButtonOff);
        bp.followButton = follow.gameObject.AddComponent<Button>();
        bp.followButton.targetGraphic = bp.followBg;
        var fl = NewRect("Label", follow); Stretch(fl, 6);
        bp.followLabel = AddText(fl, "FOLLOW: OFF", 42, Color.white);
        Txt("FollowHint", panel, -40, 668, 440, 80, "ON: the bot escorts you.\nOFF: it goes back to its job.", 22, WorkshopTheme.Muted, style: FontStyles.Normal);

        // ---------- right: disks ----------
        Txt("InventoryCaption", panel, 500, 144, 560, 40, "YOUR DISKS", 28, WorkshopTheme.Cyan).characterSpacing = 4f;
        var inv = R("Inventory", panel, 500, 192, 560, 470);
        AddImage(inv, WorkshopTheme.PanelDark);
        var dz2 = inv.gameObject.AddComponent<DiskDropZone>(); dz2.isRobotSlot = false;
        var grid = NewRect("Grid", inv); Stretch(grid, 22);
        var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(160, 160); gl.spacing = new Vector2(14, 14); gl.childAlignment = TextAnchor.UpperLeft;
        bp.inventoryGrid = grid;
        var empty = NewRect("Empty", inv); Stretch(empty, 30);
        bp.emptyText = AddText(empty, "No disks.\nFind disks on the map!", 30, WorkshopTheme.Muted, FontStyles.Normal);

        bp.confirmButton = Btn("Confirm", panel, "CONFIRM", WorkshopTheme.ButtonGreen, Color.white, 44, new Vector2(0.5f, 1f), new Vector2(500, -750), new Vector2(560, 112));
        bp.iconTemplate = BuildDiskIcon(root);
        root.gameObject.SetActive(false);
        return bp;
    }

    // same disk icon as before (DiskIcon drives drag + tap)
    static DiskIcon BuildDiskIcon(Transform parent)
    {
        var rt = Place(NewRect("DiskIconTemplate", parent), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180, 180));
        var body = AddImage(rt, new Color(0.93f, 0.93f, 0.95f));
        var label = Place(NewRect("Label", rt), new Vector2(0.5f, 1f), new Vector2(0, -62), new Vector2(140, 96));
        var labelImg = AddImage(label, WorkshopTheme.Yellow, false);
        var name = NewRect("Name", label); Stretch(name, 6);
        var nt = AddText(name, "TRASH", 30, WorkshopTheme.DarkText);
        var shutter = Place(NewRect("Shutter", rt), new Vector2(0.5f, 0f), new Vector2(0, 36), new Vector2(84, 46));
        AddImage(shutter, new Color(0.55f, 0.57f, 0.62f), false);
        var sh = rt.gameObject.AddComponent<Shadow>(); sh.effectDistance = new Vector2(0, -6); sh.effectColor = new Color(0, 0, 0, 0.5f);
        rt.gameObject.AddComponent<CanvasGroup>();
        var di = rt.gameObject.AddComponent<DiskIcon>();
        di.body = body; di.label = labelImg; di.nameText = nt;
        rt.gameObject.SetActive(false);
        return di;
    }

    // ---------------- helpers (x = centre, yTop = from parent's top edge) ----------------
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

    static Image AddImage(RectTransform rt, Color c, bool raycast = true)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    static TextMeshProUGUI AddText(RectTransform rt, string text, float size, Color c, FontStyles style = FontStyles.Bold)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = c; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
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

    static Button Btn(string name, Transform parent, string label, Color bg, Color fg, float fontSize, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = Place(NewRect(name, parent), anchor, pos, size);
        var img = AddImage(rt, bg);
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
}
