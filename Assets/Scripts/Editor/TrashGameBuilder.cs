using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// Builds the factory, bot prefab, disks and UI. Safe to re-run (replaces what it made before).
public static class TrashGameBuilder
{
    const string BotModelPath = "Assets/PolyOne/Free Stylized Sci-Fi Scout Bot/Prefabs/SR_01_Scout_Bot.prefab";

    static readonly Color Panel = new Color32(43, 34, 48, 248);
    static readonly Color PanelDark = new Color32(26, 20, 30, 255);
    static readonly Color Accent = new Color32(255, 201, 60, 255);
    static readonly Color Green = new Color32(76, 198, 107, 255);
    static readonly Color Red = new Color32(226, 87, 76, 255);
    static readonly Color Cyan = new Color32(90, 205, 235, 255);
    static readonly Color Muted = new Color32(207, 195, 214, 255);
    static readonly Color DarkText = new Color32(40, 28, 20, 255);

    // =====================================================================
    [MenuItem("Tools/Trash Game/1. Setup Factory, Bot Prefab & Disks")]
    public static void SetupScene()
    {
        EnsureFolder("Assets/Programs");
        EnsureFolder("Assets/Prefabs");
        var program = CreateProgram();
        var botPrefab = CreateBotPrefab();
        CreateFactory(botPrefab);
        CreateDisks(program);

        var player = GameObject.Find("Player");
        if (player.GetComponent<PlayerInventory>() == null) player.AddComponent<PlayerInventory>();
        if (player.GetComponent<PlayerInteractions>() == null) player.AddComponent<PlayerInteractions>();
        Save();
    }

    static CollectTrashProgram CreateProgram()
    {
        const string path = "Assets/Programs/Disk_CollectTrash.asset";
        var p = AssetDatabase.LoadAssetAtPath<CollectTrashProgram>(path);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<CollectTrashProgram>();
            AssetDatabase.CreateAsset(p, path);
        }
        p.displayName = "Collect Trash";
        p.shortName = "TRASH";
        p.description = "Collects trash from piles and brings it to the base.";
        p.color = new Color(1f, 0.78f, 0.2f);
        EditorUtility.SetDirty(p);
        return p;
    }

    static GameObject CreateBotPrefab()
    {
        var root = new GameObject("ScoutBot");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BotModelPath), root.transform);
        model.transform.localScale = Vector3.one * 1.25f;
        var anim = model.GetComponent<Animator>();
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;   // cheaper off-screen on mobile

        var bot = root.AddComponent<ScoutBot>();
        bot.animator = anim;
        var anchor = new GameObject("CarryAnchor").transform;
        anchor.SetParent(root.transform, false);
        anchor.localPosition = new Vector3(0f, 1.15f, -0.6f);
        bot.carryAnchor = anchor;
        bot.carryItemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CarriedTrash.prefab");
        bot.carryItemScale = 0.6f;
        bot.carrySpacing = 0.47f * 0.6f * 0.85f;
        bot.statusLabel = WorldLabel("Status", root.transform, new Vector3(0f, 3.0f, 0f), 5f);
        bot.statusLabel.text = "NO DISK";

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/ScoutBot.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void CreateFactory(GameObject botPrefab)
    {
        Delete("Bot Factory");
        var f = new GameObject("Bot Factory");
        f.transform.position = OnGround(15f, -9f);
        var bf = f.AddComponent<BotFactory>();

        var body = new GameObject("Body").transform;
        body.SetParent(f.transform, false);
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Sphere";
        sphere.transform.SetParent(body, false);
        sphere.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        sphere.transform.localScale = Vector3.one * 3f;
        sphere.GetComponent<Renderer>().sharedMaterial = LitMaterial("FactorySphere", new Color(0.75f, 0.93f, 1f), new Color(0.15f, 0.45f, 0.6f));

        var spawn = new GameObject("SpawnPoint").transform;
        spawn.SetParent(f.transform, false);
        spawn.localPosition = new Vector3(0f, 0f, -1.2f);

        bf.botPrefab = botPrefab;
        bf.body = body;
        bf.spawnPoint = spawn;
        bf.radius = 3.8f;
        bf.label = WorldLabel("Label", f.transform, new Vector3(0f, 3.9f, 0f), 7f);
        bf.label.text = bf.idleText;
        bf.ring = Ring(f.transform, 3.8f);
    }

    static void CreateDisks(DiskProgram program)
    {
        Delete("Disks");
        var prefab = CreateDiskPrefab(program);
        var root = new GameObject("Disks");
        Vector2[] spots = { new Vector2(-4f, 13f), new Vector2(22f, 2f), new Vector2(-22f, -2f), new Vector2(10f, -18f) };
        for (int i = 0; i < spots.Length; i++)
        {
            var d = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            d.name = "Disk " + (i + 1);
            d.transform.position = OnGround(spots[i].x, spots[i].y);
        }
    }

    static GameObject CreateDiskPrefab(DiskProgram program)
    {
        var root = new GameObject("Disk");
        var dp = root.AddComponent<DiskPickup>();
        dp.program = program;

        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        visual.localPosition = new Vector3(0f, 0.8f, 0f);
        Cube("Body", visual, Vector3.zero, new Vector3(1.1f, 0.14f, 1.1f), AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/HomeWhite.mat"));
        Cube("Label", visual, new Vector3(0f, 0.075f, 0.14f), new Vector3(0.75f, 0.02f, 0.6f), LitMaterial("DiskLabel_" + program.shortName, program.color, Color.black));
        Cube("Shutter", visual, new Vector3(0f, 0.075f, -0.36f), new Vector3(0.48f, 0.02f, 0.3f), LitMaterial("DiskShutter", new Color(0.55f, 0.57f, 0.62f), Color.black));
        dp.visual = visual;
        dp.ring = Ring(root.transform, 1.3f).gameObject;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Disk.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    // =====================================================================
    [MenuItem("Tools/Trash Game/2. Build UI")]
    public static void BuildUI()
    {
        var canvas = GameObject.Find("UI Canvas").transform;
        foreach (var n in new[] { "Prompts", "FactoryPopup", "BotProgramPopup" })
        {
            var old = canvas.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        // --- HUD: Materials | Disks | Trash bag ---
        var hud = canvas.Find("HUD");
        if (hud != null)
        {
            SetAnchorX(hud, "MaterialsCaption", 0.18f); SetAnchorX(hud, "Materials", 0.18f);
            SetAnchorX(hud, "CarryCaption", 0.82f); SetAnchorX(hud, "Carry", 0.82f);
            foreach (var n in new[] { "DisksCaption", "Disks" }) { var o = hud.Find(n); if (o != null) Object.DestroyImmediate(o.gameObject); }
            var cap = Object.Instantiate(hud.Find("MaterialsCaption").gameObject, hud);
            cap.name = "DisksCaption"; cap.GetComponent<TMP_Text>().text = "DISKS";
            var val = Object.Instantiate(hud.Find("Materials").gameObject, hud);
            val.name = "Disks"; val.GetComponent<TMP_Text>().text = "0";
            val.GetComponent<TMP_Text>().color = new Color(1f, 0.85f, 0.4f);
            SetAnchorX(hud, "DisksCaption", 0.5f); SetAnchorX(hud, "Disks", 0.5f);
            hud.GetComponent<GameHUD>().disksText = val.GetComponent<TMP_Text>();
            EditorUtility.SetDirty(hud.GetComponent<GameHUD>());
        }

        // --- floating world buttons (above joystick so taps hit them first) ---
        var prompts = NewRect("Prompts", canvas); Stretch(prompts);
        var factoryBtn = WorldBtn("FactoryButton", prompts, "BUILD BOTS", Accent, DarkText);
        var botBtn = WorldBtn("BotButton", prompts, "PROGRAM", Cyan, DarkText);

        var fp = BuildFactoryPopup(canvas);
        var bp = BotPopupBuilder.Build(canvas);   // landscape v2 (Module 5)

        prompts.SetAsLastSibling();
        fp.transform.SetAsLastSibling();
        bp.transform.SetAsLastSibling();

        var player = GameObject.Find("Player");
        var pi = player.GetComponent<PlayerInteractions>();
        if (pi == null) pi = player.AddComponent<PlayerInteractions>();
        pi.factoryButton = factoryBtn;
        pi.botButton = botBtn;
        pi.factoryPopup = fp;
        pi.botPopup = bp;
        EditorUtility.SetDirty(pi);
        Save();
    }

    static WorldButton WorldBtn(string name, Transform parent, string text, Color bg, Color fg)
    {
        var b = Btn(name, parent, text, bg, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(330, 120), 44, fg);
        var sh = b.gameObject.AddComponent<Shadow>();
        sh.effectDistance = new Vector2(0, -8); sh.effectColor = new Color(0, 0, 0, 0.45f);
        var wb = b.gameObject.AddComponent<WorldButton>();
        wb.button = b;
        b.gameObject.SetActive(false);
        return wb;
    }

    static FactoryPopup BuildFactoryPopup(Transform canvas)
    {
        var root = NewRect("FactoryPopup", canvas); Stretch(root);
        AddImage(root, new Color(0, 0, 0, 0.55f), sliced: false);
        var fp = root.gameObject.AddComponent<FactoryPopup>();

        var panel = Place(NewRect("Panel", root), Center, Vector2.zero, new Vector2(920, 1020));
        AddImage(panel, Panel);
        FitPopup(panel);
        fp.titleText = Text("Title", panel, "BOT FACTORY", 68, Accent, Top, new Vector2(0, -95), new Vector2(760, 100));
        fp.infoText = Text("Info", panel, "", 36, Muted, Top, new Vector2(0, -175), new Vector2(840, 70), FontStyles.Normal);
        fp.closeButton = Btn("Close", panel, "X", Red, new Vector2(1, 1), new Vector2(-75, -75), new Vector2(110, 110), 56);

        fp.minusButton = Btn("Minus", panel, "-", Accent, Center, new Vector2(-270, 140), new Vector2(180, 180), 120, DarkText);
        fp.countText = Text("Count", panel, "1", 140, Color.white, Center, new Vector2(0, 140), new Vector2(320, 200));
        fp.plusButton = Btn("Plus", panel, "+", Accent, Center, new Vector2(270, 140), new Vector2(180, 180), 110, DarkText);
        fp.maxButton = Btn("Max", panel, "MAX", PanelDark, Center, new Vector2(0, -20), new Vector2(240, 95), 42);

        fp.totalText = Text("Total", panel, "", 52, Color.white, Center, new Vector2(0, -135), new Vector2(840, 80));
        fp.haveText = Text("Have", panel, "", 38, Muted, Center, new Vector2(0, -200), new Vector2(840, 60), FontStyles.Normal);
        fp.queueText = Text("Queue", panel, "", 34, Cyan, Center, new Vector2(0, -260), new Vector2(840, 60));

        fp.confirmButton = Btn("Confirm", panel, "BUILD", Green, new Vector2(0.5f, 0), new Vector2(0, 125), new Vector2(660, 150), 54);
        fp.confirmText = fp.confirmButton.GetComponentInChildren<TMP_Text>();
        root.gameObject.SetActive(false);
        return fp;
    }

    static BotProgramPopup BuildBotPopup(Transform canvas)
    {
        var root = NewRect("BotProgramPopup", canvas); Stretch(root);
        AddImage(root, new Color(0, 0, 0, 0.55f), sliced: false);
        var bp = root.gameObject.AddComponent<BotProgramPopup>();
        bp.dragLayer = root;

        var panel = Place(NewRect("Panel", root), Center, Vector2.zero, new Vector2(960, 1460));
        AddImage(panel, Panel);
        FitPopup(panel);
        bp.titleText = Text("Title", panel, "SCOUT BOT", 64, Accent, Top, new Vector2(0, -90), new Vector2(760, 100));
        bp.statusText = Text("Status", panel, "", 36, Muted, Top, new Vector2(0, -165), new Vector2(860, 60), FontStyles.Normal);
        bp.closeButton = Btn("Close", panel, "X", Red, new Vector2(1, 1), new Vector2(-75, -75), new Vector2(110, 110), 56);

        Text("SlotCaption", panel, "PROGRAM SLOT", 36, Cyan, Top, new Vector2(0, -250), new Vector2(760, 60));
        var slot = Place(NewRect("RobotSlot", panel), Top, new Vector2(0, -460), new Vector2(330, 330));
        AddImage(slot, PanelDark);
        var ol = slot.gameObject.AddComponent<Outline>(); ol.effectColor = Cyan; ol.effectDistance = new Vector2(4, -4);
        var dz = slot.gameObject.AddComponent<DiskDropZone>(); dz.isRobotSlot = true;
        bp.robotSlot = slot;
        var hint = NewRect("Hint", slot); Stretch(hint, 20);
        bp.slotHint = AddText(hint, "Drag a disk here", 34, Muted, FontStyles.Normal);

        Text("InventoryCaption", panel, "YOUR DISKS", 36, Cyan, Top, new Vector2(0, -700), new Vector2(760, 60));
        var inv = Place(NewRect("Inventory", panel), Top, new Vector2(0, -960), new Vector2(860, 420));
        AddImage(inv, PanelDark);
        var dz2 = inv.gameObject.AddComponent<DiskDropZone>(); dz2.isRobotSlot = false;
        var grid = NewRect("Grid", inv); Stretch(grid, 26);
        var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(180, 180); gl.spacing = new Vector2(24, 24); gl.childAlignment = TextAnchor.UpperLeft;
        bp.inventoryGrid = grid;
        var empty = NewRect("Empty", inv); Stretch(empty, 30);
        bp.emptyText = AddText(empty, "No disks.\nFind disks on the map!", 36, Muted, FontStyles.Normal);

        bp.confirmButton = Btn("Confirm", panel, "CONFIRM", Green, new Vector2(0.5f, 0), new Vector2(0, 125), new Vector2(660, 150), 54);
        bp.iconTemplate = BuildDiskIcon(root);
        root.gameObject.SetActive(false);
        return bp;
    }

    static DiskIcon BuildDiskIcon(Transform parent)
    {
        var rt = Place(NewRect("DiskIconTemplate", parent), Center, Vector2.zero, new Vector2(180, 180));
        var body = AddImage(rt, new Color(0.93f, 0.93f, 0.95f));
        var label = Place(NewRect("Label", rt), Top, new Vector2(0, -62), new Vector2(140, 96));
        var labelImg = AddImage(label, Accent, raycast: false);
        var name = NewRect("Name", label); Stretch(name, 6);
        var nt = AddText(name, "TRASH", 30, DarkText);
        var shutter = Place(NewRect("Shutter", rt), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(84, 46));
        AddImage(shutter, new Color(0.55f, 0.57f, 0.62f), raycast: false);
        var sh = rt.gameObject.AddComponent<Shadow>(); sh.effectDistance = new Vector2(0, -6); sh.effectColor = new Color(0, 0, 0, 0.5f);
        rt.gameObject.AddComponent<CanvasGroup>();
        var di = rt.gameObject.AddComponent<DiskIcon>();
        di.body = body; di.label = labelImg; di.nameText = nt;
        rt.gameObject.SetActive(false);
        return di;
    }

    // keeps the (portrait-sized) popups inside a landscape screen: scaled down, never up
    static void FitPopup(RectTransform panel)
    {
        var fit = panel.gameObject.AddComponent<FitToSafeArea>();
        fit.designSize = panel.sizeDelta;
        fit.maxScale = 1f;
        fit.margin = 24f;
    }

    // =====================================================================
    // helpers
    static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    static readonly Vector2 Top = new Vector2(0.5f, 1f);

    static Sprite roundSprite;
    static Sprite RoundSprite => roundSprite != null ? roundSprite : (roundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"));

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

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

    static Image AddImage(RectTransform rt, Color c, bool raycast = true, bool sliced = true)
    {
        var img = rt.gameObject.AddComponent<Image>();
        if (sliced)
        {
            img.sprite = RoundSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 0.3f;
        }
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

    static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color c, Vector2 anchor, Vector2 pos, Vector2 sz,
                                FontStyles style = FontStyles.Bold)
        => AddText(Place(NewRect(name, parent), anchor, pos, sz), text, size, c, style);

    static Button Btn(string name, Transform parent, string label, Color bg, Vector2 anchor, Vector2 pos, Vector2 size, float fontSize,
                      Color? textColor = null)
    {
        var rt = Place(NewRect(name, parent), anchor, pos, size);
        var img = AddImage(rt, bg);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.pressedColor = new Color(0.78f, 0.78f, 0.78f);
        cb.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
        b.colors = cb;
        var lt = NewRect("Label", rt); Stretch(lt);
        AddText(lt, label, fontSize, textColor ?? Color.white);
        return b;
    }

    static void SetAnchorX(Transform parent, string child, float x)
    {
        var t = parent.Find(child) as RectTransform;
        if (t == null) return;
        t.anchorMin = new Vector2(x, t.anchorMin.y);
        t.anchorMax = new Vector2(x, t.anchorMax.y);
    }

    static TextMeshPro WorldLabel(string name, Transform parent, Vector3 localPos, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var t = go.AddComponent<TextMeshPro>();
        t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.fontStyle = FontStyles.Bold;
        t.rectTransform.sizeDelta = new Vector2(8f, 2f);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.outlineWidth = 0.25f; t.outlineColor = new Color32(40, 25, 20, 255);
        go.AddComponent<Billboard>();
        return t;
    }

    static GroundRing Ring(Transform parent, float radius)
    {
        var go = new GameObject("Ring");
        go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ZoneRing.mat");
        lr.alignment = LineAlignment.TransformZ;
        var r = go.AddComponent<GroundRing>();
        r.radius = radius;
        r.Rebuild();
        return r;
    }

    static GameObject Cube(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        c.name = name;
        Object.DestroyImmediate(c.GetComponent<Collider>());
        c.transform.SetParent(parent, false);
        c.transform.localPosition = pos;
        c.transform.localScale = size;
        var r = c.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return c;
    }

    static Material LitMaterial(string name, Color color, Color emission)
    {
        string path = "Assets/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", 0.3f);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Vector3 OnGround(float x, float z) => new Vector3(x, GroundUtil.Height(new Vector3(x, 0f, z), 0f), z);

    static void Delete(string name)
    {
        var go = GameObject.Find(name);
        if (go != null) Object.DestroyImmediate(go);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    static void Save()
    {
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }
}
