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

        // 3. catalog (the Drone Bay joins once builder 11 has made its definition)
        var defs = new System.Collections.Generic.List<BuildingDef>
        {
            Def("Build_ChargingStation", "Charging Station", "2 more charging pads", 40, chargerPrefab, WorkshopTheme.Cyan, "C"),
            Def("Build_Recycler", "Recycler", "Bots drop trash here", 80, recyclerPrefab, new Color(0.40f, 0.92f, 0.50f), "R"),
            Def("Build_GathererFactory", "Gatherer Factory", "More Scout Bots, own upgrades", 150, factoryPrefab, WorkshopTheme.Orange, "F"),
        };

        var bayDef = AssetDatabase.LoadAssetAtPath<BuildingDef>(DefFolder + "/Build_DroneBay.asset");
        if (bayDef != null && bayDef.prefab != null) defs.Add(bayDef);
        var tradeDef = AssetDatabase.LoadAssetAtPath<BuildingDef>(DefFolder + "/Build_TradePost.asset");   // builder 14
        if (tradeDef != null && tradeDef.prefab != null) defs.Add(tradeDef);

        // 4. materials (Sprites/Default = unlit, transparent, vertex colours)
        var ghostMat = SpriteMaterial("BuildGhost", null);
        var overlayMat = SpriteMaterial("BuildGridOverlay", GridTexture());

        // 5. player upgrade tree (PLAYER tab) lives on the player
        PlayerUpgradesBuilder.Setup();

        // 6. UI
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

        const float W = 1180f, H = 664f;
        var cat = NewRect("Panel", menu);
        cat.anchorMin = cat.anchorMax = cat.pivot = new Vector2(0.5f, 0.5f);
        cat.anchoredPosition = Vector2.zero;
        cat.sizeDelta = new Vector2(W, H);
        AddImage(cat, new Color(0.10f, 0.086f, 0.067f, 0.97f));   // also swallows taps so the panel itself doesn't close
        AddImage(R("Rule", cat, 0, 0, 1180, 5), WorkshopTheme.Orange, false);
        Txt("Title", cat, -412, 14, 300, 52, "BASE", 42, WorkshopTheme.Orange, TextAlignmentOptions.Left).characterSpacing = 4f;
        bm.menuMaterialsText = Txt("Materials", cat, 190, 20, 360, 44, "MATERIALS  0", 30, WorkshopTheme.Yellow, TextAlignmentOptions.Right);
        bm.menuCloseButton = Btn("Close", cat, "CLOSE", WorkshopTheme.ButtonOff, Color.white, 30, new Vector2(0.5f, 1f), new Vector2(480f, -46f), new Vector2(170f, 60f), new Vector2(0.5f, 0.5f));

        // tabs: PLAYER (player + pipe upgrades) | BUILD (buildings with 3D previews)
        string[] tabNames = { "PLAYER", "BUILD" };
        string[] tabSubs = { "Level up to get a longer pipe, better stats and skill points for the tree", "Tap a building to place it  ·  faded = not enough materials" };
        bm.tabButtons = new Button[tabNames.Length];
        bm.tabPages = new GameObject[tabNames.Length];
        for (int i = 0; i < tabNames.Length; i++)
        {
            bm.tabButtons[i] = Btn("Tab " + tabNames[i], cat, tabNames[i], i == 0 ? WorkshopTheme.Orange : WorkshopTheme.ButtonOff,
                i == 0 ? WorkshopTheme.DarkText : Color.white, 30, new Vector2(0.5f, 1f), new Vector2(-W * 0.5f + 45f + i * 252f, -88f),
                new Vector2(236f, 66f), new Vector2(0f, 1f));
            var page = R("Page " + tabNames[i], cat, 0, 172, W, H - 172);
            bm.tabPages[i] = page.gameObject;
            Txt("Sub", page, -W * 0.5f + 45f + 400f, 0, 800, 34, tabSubs[i], 22, WorkshopTheme.Muted, TextAlignmentOptions.Left, FontStyles.Normal);
            page.gameObject.SetActive(i == 0);
        }
        AddImage(R("TabRule", cat, 0, 160, W - 90f, 3), new Color(WorkshopTheme.Orange.r, WorkshopTheme.Orange.g, WorkshopTheme.Orange.b, 0.45f), false);

        // PLAYER page: astronaut | level, points, stats | LEVEL UP + UPGRADE TREE
        var playerPage = bm.tabPages[0].transform;
        var pv = R("PreviewBg", playerPage, -400, 46, 290, 400);
        AddImage(pv, WorkshopTheme.PanelDark, false);
        var pvImg = NewRect("Preview", pv); Stretch(pvImg);
        var raw = pvImg.gameObject.AddComponent<RawImage>();
        raw.color = new Color(1f, 1f, 1f, 0f);
        raw.raycastTarget = false;
        var mp = pvImg.gameObject.AddComponent<ModelPreview>();
        mp.rigPosition = new Vector3(0f, 1500f, 0f);   // above the base (fog of war darkens rigs over unexplored ground)
        mp.elevation = 8f;
        mp.lightFollowsCamera = true;
        mp.fillIntensity = 3f;
        mp.padding = 0.9f;
        mp.startYaw = -25f;
        mp.maxTextureSize = 512;
        bm.playerPreview = mp;

        bm.playerLevelText = Txt("Level", playerPage, -5, 50, 440, 50, "PLAYER LEVEL 1 / 6", 34, Color.white, TextAlignmentOptions.Left);
        bm.playerPointsText = Txt("Points", playerPage, -5, 100, 440, 32, "", 22, WorkshopTheme.Orange, TextAlignmentOptions.Left);
        bm.playerStatsText = Txt("Stats", playerPage, -5, 140, 440, 220, "", 25, WorkshopTheme.Muted, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        bm.playerStatsText.lineSpacing = 18f;

        bm.playerLevelUpButton = Btn("LevelUp", playerPage, "LEVEL UP", WorkshopTheme.ButtonGreen, Color.white, 34,
            new Vector2(0.5f, 1f), new Vector2(390f, -116f), new Vector2(300f, 140f), new Vector2(0.5f, 0.5f));
        bm.playerLevelUpLabel = bm.playerLevelUpButton.GetComponentInChildren<TMPro.TMP_Text>();
        bm.playerNextText = Txt("Next", playerPage, 390, 196, 300, 120, "", 20, WorkshopTheme.Muted, TextAlignmentOptions.Top, FontStyles.Normal);
        bm.playerTreeButton = Btn("Tree", playerPage, "UPGRADE TREE", WorkshopTheme.Orange, WorkshopTheme.DarkText, 30,
            new Vector2(0.5f, 1f), new Vector2(390f, -378f), new Vector2(300f, 116f), new Vector2(0.5f, 0.5f));
        bm.playerTreeLabel = bm.playerTreeButton.GetComponentInChildren<TMPro.TMP_Text>();
        bm.cardTemplate = CardTemplate(cat, hex);

        var buildPage = bm.tabPages[1].transform;
        bm.cardRow = CardGrid("Cards", buildPage, 44, H - 172 - 44 - 18);
        bm.buildingCardTemplate = BuildingCardTemplate(cat, hex);

        // ---------- placement: title + status, building chips (switch), ROTATE / EXIT / PLACE ----------
        const float PW = 1500f, PH = 236f;
        var place = Panel("Placement", root, new Vector2(PW, PH));
        bm.placePanel = place.gameObject;
        float left = -PW * 0.5f;
        bm.placeTitle = Txt("Title", place, left + 24f + 210f, 12, 420, 44, "CHARGING STATION", 32, WorkshopTheme.Orange, TextAlignmentOptions.Left);
        bm.placeTitle.enableAutoSizing = true; bm.placeTitle.fontSizeMin = 20; bm.placeTitle.fontSizeMax = 32;
        bm.placeStatus = Txt("Status", place, left + 460f + 260f, 12, 520, 44, "", 22, Color.white, TextAlignmentOptions.Left, FontStyles.Normal);
        bm.placeStatus.enableAutoSizing = true; bm.placeStatus.fontSizeMin = 14; bm.placeStatus.fontSizeMax = 22;
        bm.chipRow = CardRow("Chips", place, 62, 162, left + 12f + 470f, 940f, 12f, 4f);
        bm.chipTemplate = ChipTemplate(place, hex);
        bm.rotateButton = Btn("Rotate", place, "ROTATE", WorkshopTheme.ButtonOff, Color.white, 28, new Vector2(0.5f, 1f), new Vector2(326f, -136f), new Vector2(140f, 150f), new Vector2(0.5f, 0.5f));
        bm.cancelButton = Btn("Exit", place, "EXIT", WorkshopTheme.ButtonOff, Color.white, 28, new Vector2(0.5f, 1f), new Vector2(482f, -136f), new Vector2(140f, 150f), new Vector2(0.5f, 0.5f));
        bm.placeButton = Btn("Place", place, "PLACE", WorkshopTheme.ButtonGreen, Color.white, 38, new Vector2(0.5f, 1f), new Vector2(650f, -136f), new Vector2(180f, 150f), new Vector2(0.5f, 0.5f));
        AddImage(R("Divider", place, 245f, 62f, 3f, 150f), new Color(1f, 1f, 1f, 0.08f), false);

        // ---------- hint ----------
        var hintRt = NewRect("Hint", root);
        hintRt.anchorMin = hintRt.anchorMax = hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.anchoredPosition = new Vector2(0f, 276f);   // above the placement panel
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
    // Scrolls sideways (drag) once there are more cards than fit; the returned content row is where cards get added.
    static RectTransform CardRow(string name, Transform parent, float yTop, float h = 168f, float x = 0f, float w = 1090f, float spacing = 18f, float pad = 13f)
    {
        var view = R(name + " View", parent, x, yTop, w, h);
        view.gameObject.AddComponent<RectMask2D>();
        AddImage(view, new Color(0f, 0f, 0f, 0f));   // invisible, but takes drags in the gaps between cards
        var row = NewRect(name, view);
        row.anchorMin = new Vector2(0f, 0f); row.anchorMax = new Vector2(0f, 1f);
        row.pivot = new Vector2(0f, 0.5f);
        row.anchoredPosition = Vector2.zero;
        row.sizeDelta = new Vector2(w, 0f);
        var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = spacing; hl.childAlignment = TextAnchor.MiddleLeft;
        hl.padding = new RectOffset((int)pad, (int)pad, 0, 0);
        hl.childControlWidth = false; hl.childControlHeight = false;
        hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        var fit = row.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        var sr = view.gameObject.AddComponent<ScrollRect>();
        sr.viewport = view; sr.content = row;
        sr.horizontal = true; sr.vertical = false;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.inertia = true; sr.decelerationRate = 0.1f;
        sr.scrollSensitivity = 30f;
        return row;
    }

    // BUILD tab: grid of small tiles, 5 per row, scrolls up/down when there are more rows than fit
    public const float TileW = 200f, TileH = 206f;
    static RectTransform CardGrid(string name, Transform parent, float yTop, float h)
    {
        const float w = 1090f;
        var view = R(name + " View", parent, 0, yTop, w, h);
        view.gameObject.AddComponent<RectMask2D>();
        AddImage(view, new Color(0f, 0f, 0f, 0f));   // takes drags in the gaps
        var grid = NewRect(name, view);
        grid.anchorMin = new Vector2(0f, 1f); grid.anchorMax = new Vector2(1f, 1f);
        grid.pivot = new Vector2(0.5f, 1f);
        grid.anchoredPosition = Vector2.zero;
        grid.sizeDelta = Vector2.zero;
        var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(TileW, TileH);
        gl.spacing = new Vector2(16f, 16f);
        gl.padding = new RectOffset(13, 13, 0, 8);
        gl.startCorner = GridLayoutGroup.Corner.UpperLeft;
        gl.startAxis = GridLayoutGroup.Axis.Horizontal;
        gl.childAlignment = TextAnchor.UpperLeft;
        gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gl.constraintCount = 5;
        var fit = grid.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var sr = view.gameObject.AddComponent<ScrollRect>();
        sr.viewport = view; sr.content = grid;
        sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.inertia = true; sr.decelerationRate = 0.1f;
        sr.scrollSensitivity = 30f;
        return grid;
    }

    // bottom-bar chip in build mode: hex glyph + size, name, cost
    static BuildChip ChipTemplate(Transform parent, Sprite hex)
    {
        var rt = R("ChipTemplate", parent, 0, 0, 172, 150);
        var bg = AddImage(rt, WorkshopTheme.Panel);
        var chip = rt.gameObject.AddComponent<BuildChip>();
        chip.background = bg;
        chip.group = rt.gameObject.AddComponent<CanvasGroup>();
        chip.button = rt.gameObject.AddComponent<Button>();
        chip.button.targetGraphic = bg;
        var cb = chip.button.colors; cb.pressedColor = new Color(0.78f, 0.78f, 0.78f); chip.button.colors = cb;

        var ic = Place(NewRect("Badge", rt), new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(46f, 40f));
        chip.icon = AddImage(ic, WorkshopTheme.Orange, false);
        chip.icon.sprite = hex; chip.icon.preserveAspect = true;
        var g = NewRect("Glyph", ic); Stretch(g);
        chip.glyph = AddText(g, "C", 24, WorkshopTheme.DarkText);
        chip.infoText = Txt("Size", rt, 36, 12, 90, 30, "3×3", 20, WorkshopTheme.Muted, TextAlignmentOptions.Right, FontStyles.Normal);

        chip.nameText = Txt("Name", rt, 0, 54, 156, 56, "CHARGING STATION", 21, Color.white, TextAlignmentOptions.TopLeft);
        chip.nameText.enableAutoSizing = true; chip.nameText.fontSizeMin = 14; chip.nameText.fontSizeMax = 21;
        chip.costText = Txt("Cost", rt, 0, 114, 156, 28, "40 MATS", 22, WorkshopTheme.Yellow, TextAlignmentOptions.Left);
        rt.gameObject.SetActive(false);
        return chip;
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

    // building card: spinning 3D model on top, name / size / build time / description, cost at the bottom
    static BuildCard BuildingCardTemplate(Transform parent, Sprite hex)
    {
        const float W = TileW, PV = 118f;
        var rt = R("BuildingCardTemplate", parent, 0, 0, W, TileH);
        var bg = AddImage(rt, WorkshopTheme.Panel);
        var card = rt.gameObject.AddComponent<BuildCard>();
        card.compact = true;
        card.button = rt.gameObject.AddComponent<Button>();
        card.button.targetGraphic = bg;
        rt.gameObject.AddComponent<CanvasGroup>();

        var pv = R("PreviewBg", rt, 0, 0, W, PV);
        AddImage(pv, WorkshopTheme.PanelDark, false);
        var img = NewRect("Preview", pv); Stretch(img);
        var raw = img.gameObject.AddComponent<RawImage>();
        raw.color = new Color(1f, 1f, 1f, 0f);   // ModelPreview turns it on with its render texture
        raw.raycastTarget = false;                // taps go to the card
        var mp = img.gameObject.AddComponent<ModelPreview>();
        mp.elevation = 26f;
        mp.padding = 0.95f;
        mp.lightFollowsCamera = true;
        mp.fillIntensity = 2.5f;
        mp.autoSpin = 24f;
        mp.startYaw = -30f;
        mp.maxTextureSize = 384;
        card.preview = mp;

        // colour bar under the preview + small hex badge with the glyph
        var bar = R("ColorBar", rt, 0, PV, W, 5);
        card.icon = AddImage(bar, WorkshopTheme.Orange, false);
        var ic = Place(NewRect("Badge", rt), new Vector2(0f, 1f), new Vector2(22f, -20f), new Vector2(34f, 30f));
        var badge = AddImage(ic, WorkshopTheme.Orange, false);
        badge.sprite = hex; badge.preserveAspect = true;
        var g = NewRect("Glyph", ic); Stretch(g);
        card.glyph = AddText(g, "C", 19, WorkshopTheme.DarkText);
        card.badge = badge;

        card.nameText = Txt("Name", rt, 0, PV + 9, W - 18, 28, "CHARGING STATION", 20, Color.white, TextAlignmentOptions.Left);
        card.nameText.enableAutoSizing = true; card.nameText.fontSizeMin = 13; card.nameText.fontSizeMax = 20;
        card.infoText = Txt("Info", rt, 0, PV + 38, W - 18, 22, "3×3  ·  5s", 16, WorkshopTheme.Muted, TextAlignmentOptions.Left, FontStyles.Normal);
        card.costText = Txt("Cost", rt, 0, PV + 60, W - 18, 26, "40 MATERIALS", 19, WorkshopTheme.Yellow, TextAlignmentOptions.Left);
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
