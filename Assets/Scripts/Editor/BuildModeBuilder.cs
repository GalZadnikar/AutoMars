using System.IO;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// Module 7: grid-based base construction. Registers the existing buildings on the grid,
/// makes placeable prefabs + catalog, builds the base popup / placement UI (opened from the BASE button at the base). Safe to re-run.
/// The Recycler / Gatherer Factory prefabs are the source of truth once the scene's Base_Home / Bot Factory are instances of them:
/// edit the model in the prefab (or as a prefab override and apply it); the builder only (re)creates a prefab from the scene object if none is linked.
public static class BuildModeBuilder
{
    const string PrefabFolder = "Assets/Prefabs/Buildings";
    const string DefFolder = "Assets/Buildings";
    const string GridTexPath = "Assets/UI/Generated/GridCell.png";

    [MenuItem("Tools/Trash Game/8. Build Mode (Grid Construction)")]
    public static void Build()
    {
        EnsureFolder(PrefabFolder);
        EnsureFolder(DefFolder);

        // 1. existing buildings occupy grid cells
        var home = FindRoot("Base_Home") ?? FindRoot("Home");
        var factory = FindRoot("Bot Factory");   // may be inactive
        var charger = FindRoot("Charging Station");
        Footprint(home, new Vector2Int(4, 4), Vector2.zero);
        Footprint(factory, new Vector2Int(4, 4), Vector2.zero);
        Footprint(charger, new Vector2Int(3, 3), new Vector2(0f, -1f));

        // 2. placeable prefabs made from them
        var chargerPrefab = PrefabFor(charger, "ChargingStation");
        var recyclerPrefab = PrefabFor(home, "Recycler");
        var factoryPrefab = PrefabFor(factory, "GathererFactory");

        // 3. catalog
        var defs = new[]
        {
            Def("Build_ChargingStation", "Charging Station", "2 more charging pads", 40, chargerPrefab, WorkshopTheme.Cyan, "C"),
            Def("Build_Recycler", "Recycler", "Bots drop trash here", 80, recyclerPrefab, new Color(0.40f, 0.92f, 0.50f), "R"),
            Def("Build_GathererFactory", "Gatherer Factory", "More Scout Bots, own upgrades", 150, factoryPrefab, WorkshopTheme.Orange, "F"),
        };

        // 4. materials (Sprites/Default = unlit, transparent, vertex colours)
        var ghostMat = SpriteMaterial("BuildGhost", null);
        var overlayMat = SpriteMaterial("BuildGridOverlay", GridTexture());

        // 5. UI
        var bm = BuildUI();
        bm.catalog = new System.Collections.Generic.List<BuildingDef>(defs);
        bm.ghostMaterial = ghostMat;
        bm.overlayMaterial = overlayMat;
        bm.buildCamera = BuildCamera();
        EditorUtility.SetDirty(bm);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build mode ready: walk up to the base in Play mode and tap its BASE button.");
    }

    // ------------------------------------------------------------------ camera
    // Top-down copy of the follow camera; BuildMode turns it on and points it at the build cursor.
    static CinemachineCamera BuildCamera()
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var r in scene.GetRootGameObjects())
            if (r.name == "Build Camera") Object.DestroyImmediate(r);

        var follow = GameObject.Find("Follow Camera");
        if (follow == null) { Debug.LogError("No 'Follow Camera' in the scene"); return null; }
        var go = Object.Instantiate(follow);
        go.name = "Build Camera";
        var cam = go.GetComponent<CinemachineCamera>();
        var prio = cam.Priority; prio.Enabled = true; prio.Value = 20; cam.Priority = prio;
        cam.Target.TrackingTarget = null;
        cam.Target.LookAtTarget = null;
        var f = go.GetComponent<CinemachineFollow>();
        if (f != null)
        {
            f.FollowOffset = new Vector3(0f, 26f, -7f);   // steep, almost straight down
            var ts = f.TrackerSettings;
            ts.PositionDamping = Vector3.zero;   // stick to the cursor, no lag
            f.TrackerSettings = ts;
        }
        var rc = go.GetComponent<CinemachineRotationComposer>();
        if (rc != null) rc.Damping = Vector2.zero;
        go.SetActive(false);

        var brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
        if (brain != null)
        {
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.3f);
            EditorUtility.SetDirty(brain);
        }
        return cam;
    }

    // ------------------------------------------------------------------ buildings
    static void Footprint(GameObject go, Vector2Int size, Vector2 offset)
    {
        if (go == null) return;
        if (go.GetComponent<GridFootprint>() != null) return;   // already set up (lives on the prefab now)
        var fp = go.AddComponent<GridFootprint>();
        fp.size = size;
        fp.centerOffset = offset;
        EditorUtility.SetDirty(fp);
    }

    // root object by name, also when it is inactive (GameObject.Find skips those)
    static GameObject FindRoot(string name)
    {
        foreach (var r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (r.name == name) return r;
        return null;
    }

    // the prefab the scene object is an instance of; otherwise (re)made from the scene object
    static GameObject PrefabFor(GameObject sceneGo, string name)
    {
        string path = PrefabFolder + "/" + name + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (sceneGo != null && existing != null)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(sceneGo);
            if (src != null && AssetDatabase.GetAssetPath(src) == path) return existing;   // linked: the prefab is the source of truth
        }
        var prefab = MakePrefab(sceneGo, name);
        if (name == "GathererFactory") ResetFactoryPrefab(prefab);
        return prefab != null ? prefab : existing;
    }

    static GameObject MakePrefab(GameObject sceneGo, string name)
    {
        if (sceneGo == null) { Debug.LogError("Missing scene building for " + name); return null; }
        string path = PrefabFolder + "/" + name + ".prefab";
        var copy = Object.Instantiate(sceneGo);   // copy so the scene object stays as it is
        copy.name = name;
        copy.SetActive(true);
        copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        // the pipe belongs to the home base only, not to every recycler
        var pipe = copy.GetComponent<PipeTether>(); if (pipe != null) Object.DestroyImmediate(pipe);
        var pp = copy.transform.Find("Pipe Point (Base)"); if (pp != null) Object.DestroyImmediate(pp.gameObject);
        var prefab = PrefabUtility.SaveAsPrefabAsset(copy, path);
        Object.DestroyImmediate(copy);
        return prefab;
    }

    // a freshly built factory starts at level 1 with empty slots
    static void ResetFactoryPrefab(GameObject prefab)
    {
        if (prefab == null) return;
        string path = AssetDatabase.GetAssetPath(prefab);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var up = root.GetComponent<BuildingUpgrades>();
            if (up != null)
            {
                var so = new SerializedObject(up);
                so.FindProperty("level").intValue = 1;
                so.FindProperty("picks").ClearArray();
                so.FindProperty("equippedBattery").objectReferenceValue = null;
                so.FindProperty("masterDisk").objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var bf = root.GetComponent<BotFactory>();
            if (bf != null && bf.label != null) bf.label.text = bf.idleText;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static BuildingDef Def(string file, string name, string desc, int cost, GameObject prefab, Color color, string glyph)
    {
        string path = DefFolder + "/" + file + ".asset";
        var d = AssetDatabase.LoadAssetAtPath<BuildingDef>(path);
        bool isNew = d == null;
        if (isNew)
        {
            d = ScriptableObject.CreateInstance<BuildingDef>();
            AssetDatabase.CreateAsset(d, path);
            d.displayName = name;
            d.description = desc;
            d.cost = cost;
            d.color = color;
            d.glyph = glyph;
        }
        d.prefab = prefab;   // keep your name/cost tweaks on re-run, refresh the prefab link
        EditorUtility.SetDirty(d);
        return d;
    }

    static Material SpriteMaterial(string name, Texture tex)
    {
        string path = "Assets/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = Shader.Find("Sprites/Default");
        m.mainTexture = tex;
        m.color = Color.white;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Texture2D GridTexture()
    {
        EnsureFolder("Assets/UI/Generated");
        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, s - 1 - x), Mathf.Min(y, s - 1 - y));
                byte a = (byte)(edge < 3 ? 255 : 80);
                px[y * s + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        File.WriteAllBytes(GridTexPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(GridTexPath, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(GridTexPath);
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(GridTexPath);
    }

    // ------------------------------------------------------------------ UI
    static BuildMode BuildUI()
    {
        var canvas = GameObject.Find("UI Canvas").transform;
        var old = canvas.Find("BuildMode");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = NewRect("BuildMode", canvas);
        Stretch(root);
        // above the HUD, joystick and camera slider, below the popups / Workshop
        int idx = 0;
        foreach (var n in new[] { "Prompts", "CameraDistanceSlider", "Minimap", "HUD" })
        {
            var t = canvas.Find(n);
            if (t != null) idx = Mathf.Max(idx, t.GetSiblingIndex() + 1);
        }
        root.SetSiblingIndex(Mathf.Min(idx, canvas.childCount - 1));
        var bm = root.gameObject.AddComponent<BuildMode>();
        var hex = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Generated/HexFill.png");

        // ---------- base popup (opened from the BASE button floating over the base) ----------
        var menu = NewRect("BasePopup", root);
        Stretch(menu);
        var dim = AddImage(menu, new Color(0f, 0f, 0f, 0.6f));   // blocks the world behind it
        var backdrop = menu.gameObject.AddComponent<Button>();    // tap outside = close
        backdrop.targetGraphic = dim;
        backdrop.transition = Selectable.Transition.None;
        bm.menuPanel = menu.gameObject;
        bm.menuBackdrop = backdrop;

        var cat = NewRect("Panel", menu);
        cat.anchorMin = cat.anchorMax = cat.pivot = new Vector2(0.5f, 0.5f);
        cat.anchoredPosition = Vector2.zero;
        cat.sizeDelta = new Vector2(1180f, 566f);
        AddImage(cat, new Color(0.10f, 0.086f, 0.067f, 0.97f));   // also swallows taps so the panel itself doesn't close
        AddImage(R("Rule", cat, 0, 0, 1180, 5), WorkshopTheme.Orange, false);
        Txt("Title", cat, -412, 14, 300, 52, "BASE", 42, WorkshopTheme.Orange, TextAlignmentOptions.Left).characterSpacing = 4f;
        Txt("Sub", cat, -212, 68, 700, 40, "Upgrade the base or build on the map", 24, WorkshopTheme.Muted, TextAlignmentOptions.Left, FontStyles.Normal);
        bm.menuMaterialsText = Txt("Materials", cat, 190, 20, 360, 44, "MATERIALS  0", 30, WorkshopTheme.Yellow, TextAlignmentOptions.Right);
        bm.menuCloseButton = Btn("Close", cat, "CLOSE", WorkshopTheme.ButtonOff, Color.white, 30, new Vector2(0.5f, 1f), new Vector2(480f, -46f), new Vector2(170f, 60f), new Vector2(0.5f, 0.5f));

        // section 1: base upgrades
        Txt("UpgradesLabel", cat, -412, 112, 300, 34, "UPGRADES", 26, WorkshopTheme.Orange, TextAlignmentOptions.Left).characterSpacing = 3f;
        bm.upgradeRow = CardRow("UpgradeCards", cat, 150);
        // section 2: buildings
        Txt("BuildLabel", cat, -412, 338, 300, 34, "BUILD", 26, WorkshopTheme.Orange, TextAlignmentOptions.Left).characterSpacing = 3f;
        bm.cardRow = CardRow("Cards", cat, 376);
        bm.cardTemplate = CardTemplate(cat, hex);

        // ---------- placement ----------
        var place = Panel("Placement", root, new Vector2(1120f, 172f));
        bm.placePanel = place.gameObject;
        bm.placeTitle = Txt("Title", place, -300, 16, 480, 50, "CHARGING STATION", 36, WorkshopTheme.Orange, TextAlignmentOptions.Left);
        bm.placeStatus = Txt("Status", place, -300, 70, 480, 84, "", 24, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        bm.placeStatus.enableAutoSizing = true; bm.placeStatus.fontSizeMin = 16; bm.placeStatus.fontSizeMax = 24;
        bm.rotateButton = Btn("Rotate", place, "ROTATE", WorkshopTheme.ButtonOff, Color.white, 28, new Vector2(0.5f, 1f), new Vector2(90f, -90f), new Vector2(160f, 116f), new Vector2(0.5f, 0.5f));
        bm.cancelButton = Btn("Back", place, "BACK", WorkshopTheme.ButtonOff, Color.white, 28, new Vector2(0.5f, 1f), new Vector2(268f, -90f), new Vector2(160f, 116f), new Vector2(0.5f, 0.5f));
        bm.placeButton = Btn("Place", place, "PLACE", WorkshopTheme.ButtonGreen, Color.white, 38, new Vector2(0.5f, 1f), new Vector2(455f, -90f), new Vector2(190f, 124f), new Vector2(0.5f, 0.5f));

        // ---------- hint ----------
        var hintRt = NewRect("Hint", root);
        hintRt.anchorMin = hintRt.anchorMax = hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.anchoredPosition = new Vector2(0f, 210f);   // above the placement panel
        hintRt.sizeDelta = new Vector2(1300f, 50f);
        bm.hintText = AddText(hintRt, "Walk to move the blueprint. It snaps to the grid.", 30, Color.white);
        bm.hintText.outlineWidth = 0.25f;
        bm.hintText.outlineColor = new Color32(20, 15, 10, 255);

        menu.gameObject.SetActive(false);
        place.gameObject.SetActive(false);
        hintRt.gameObject.SetActive(false);
        return bm;
    }

    // left-aligned row of cards (same inset as the panel's text)
    static RectTransform CardRow(string name, Transform parent, float yTop)
    {
        var row = R(name, parent, 0, yTop, 1090, 168);
        var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 22; hl.childAlignment = TextAnchor.MiddleLeft;
        hl.padding = new RectOffset(13, 0, 0, 0);
        hl.childControlWidth = false; hl.childControlHeight = false;
        hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        return row;
    }

    static RectTransform Panel(string name, Transform parent, Vector2 size)
    {
        var p = NewRect(name, parent);
        p.anchorMin = p.anchorMax = p.pivot = new Vector2(0.5f, 0f);
        p.anchoredPosition = new Vector2(0f, 20f);
        p.sizeDelta = size;
        AddImage(p, new Color(0.10f, 0.086f, 0.067f, 0.95f));
        AddImage(R("Rule", p, 0, 0, size.x, 5), WorkshopTheme.Orange, false);
        return p;
    }

    static BuildCard CardTemplate(Transform parent, Sprite hex)
    {
        var rt = R("CardTemplate", parent, 0, 0, 340, 168);
        var bg = AddImage(rt, WorkshopTheme.Panel);
        var card = rt.gameObject.AddComponent<BuildCard>();
        card.button = rt.gameObject.AddComponent<Button>();
        card.button.targetGraphic = bg;

        var ic = Place(NewRect("Icon", rt), new Vector2(0f, 0.5f), new Vector2(64f, 6f), new Vector2(100f, 87f));
        card.icon = AddImage(ic, WorkshopTheme.Orange, false);
        card.icon.sprite = hex;
        card.icon.preserveAspect = true;
        var g = NewRect("Glyph", ic); Stretch(g);
        card.glyph = AddText(g, "C", 40, WorkshopTheme.DarkText);

        card.nameText = Txt("Name", rt, 60, 14, 210, 40, "CHARGING STATION", 26, Color.white, TextAlignmentOptions.Left);
        card.nameText.enableAutoSizing = true; card.nameText.fontSizeMin = 16; card.nameText.fontSizeMax = 26;
        card.infoText = Txt("Info", rt, 60, 54, 210, 62, "3×3 tiles", 19, WorkshopTheme.Muted, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        card.costText = Txt("Cost", rt, 60, 122, 210, 34, "40 MATERIALS", 24, WorkshopTheme.Yellow, TextAlignmentOptions.Left);
        rt.gameObject.SetActive(false);
        return card;
    }

    // ------------------------------------------------------------------ helpers (x = centre, yTop = from parent's top)
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

    static Button Btn(string name, Transform parent, string label, Color bg, Color fg, float fontSize,
                      Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = AddImage(rt, bg);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.pressedColor = new Color(0.78f, 0.78f, 0.78f);
        cb.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
        b.colors = cb;
        var lt = NewRect("Label", rt); Stretch(lt, 4);
        AddText(lt, label, fontSize, fg);
        return b;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
