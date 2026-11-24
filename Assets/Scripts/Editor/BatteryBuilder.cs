using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Module 4: battery assets (Resources/Batteries), battery pickups around the map
/// (rarer = further out), and the bots' built-in scrap cell. Safe to re-run.
public static class BatteryBuilder
{
    const string Folder = "Assets/Resources/Batteries";
    const string BotPrefabPath = "Assets/Prefabs/ScoutBot.prefab";

    // runtime in seconds per rarity (design doc: Common 2 min, Rare 5 min)
    static readonly (Rarity rarity, string name, float seconds)[] Defs =
    {
        (Rarity.Common, "Common Battery", 120f),
        (Rarity.Uncommon, "Uncommon Battery", 180f),
        (Rarity.Rare, "Rare Battery", 300f),
        (Rarity.Epic, "Epic Battery", 420f),
        (Rarity.Legendary, "Legendary Battery", 600f),
    };

    // where pickups lie: commons near the base, rare+ far out in the fog
    static readonly (float x, float z, Rarity r)[] Spots =
    {
        (4f, 6f, Rarity.Common),
        (14f, 3f, Rarity.Common),
        (-8f, -5f, Rarity.Common),
        (-20f, 12f, Rarity.Uncommon),
        (22f, -22f, Rarity.Uncommon),
        (-30f, 26f, Rarity.Rare),
        (32f, 18f, Rarity.Rare),
        (-33f, -30f, Rarity.Epic),
        (30f, 35f, Rarity.Legendary),
    };

    [MenuItem("Tools/Trash Game/6. Batteries (Loot + Rarity)")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Batteries");

        var items = new BatteryItem[Defs.Length];
        for (int i = 0; i < Defs.Length; i++)
        {
            string path = Folder + "/Battery_" + Defs[i].rarity + ".asset";
            var b = AssetDatabase.LoadAssetAtPath<BatteryItem>(path);
            if (b == null)
            {
                b = ScriptableObject.CreateInstance<BatteryItem>();
                b.rarity = Defs[i].rarity;
                b.displayName = Defs[i].name;
                b.runtimeSeconds = Defs[i].seconds;
                AssetDatabase.CreateAsset(b, path);
            }
            items[i] = b;
        }

        // bots without a battery in the factory get a weak built-in cell
        var root = PrefabUtility.LoadPrefabContents(BotPrefabPath);
        try
        {
            root.GetComponent<ScoutBot>().batteryRuntime = 60f;
            PrefabUtility.SaveAsPrefabAsset(root, BotPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        PlacePickups(items);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Batteries built: " + Spots.Length + " pickups placed, bot built-in cell = 1:00.");
    }

    static void PlacePickups(BatteryItem[] items)
    {
        var old = GameObject.Find("Batteries");
        if (old != null) Object.DestroyImmediate(old);
        var parent = new GameObject("Batteries").transform;
        int n = 0;
        foreach (var s in Spots)
        {
            var item = items[(int)s.r];
            var pos = new Vector3(s.x, GroundUtil.Height(new Vector3(s.x, 0f, s.z), 0f), s.z);
            CreatePickup(item, parent, pos, item.displayName + " " + (++n));
        }
    }

    /// One battery pickup: the battery model in its rarity colour, a ground ring and (rare and better) a loot beam.
    public static GameObject CreatePickup(BatteryItem item, Transform parent, Vector3 pos, string name)
    {
        var ringMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ZoneRing.mat");
        var mat = LitMaterial("Battery_" + item.rarity, item.Color, item.Color * (item.rarity >= Rarity.Rare ? 1.4f : 0.6f));

        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var bp = go.AddComponent<BatteryPickup>();
        bp.battery = item;

        var visual = new GameObject("Visual").transform;
        visual.SetParent(go.transform, false);
        visual.localPosition = new Vector3(0f, 1.0f, 0f);
        BuildVisual(visual, item.rarity);
        bp.visual = visual;

        if (item.rarity >= Rarity.Rare)   // loot beam
            bp.beam = Prim(PrimitiveType.Cylinder, "Beam", go.transform, new Vector3(0f, 3f, 0f), new Vector3(0.12f, 3f, 0.12f), mat);

        var ringGo = new GameObject("Ring");
        ringGo.transform.SetParent(go.transform, false);
        ringGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var lr = ringGo.AddComponent<LineRenderer>();
        lr.sharedMaterial = ringMat;
        lr.alignment = LineAlignment.TransformZ;
        var ring = ringGo.AddComponent<GroundRing>();
        ring.radius = 1.3f;
        ring.idleColor = new Color(item.Color.r, item.Color.g, item.Color.b, 0.8f);
        ring.Rebuild();
        bp.ring = ringGo;
        return go;
    }

    const string BarrelPrefab = "Assets/WONILMAX/Sci-Fi Barrel Pack/Prefabs/Sci-Fi Barrel_Grey.prefab";
    const string BarrelMaterial = "Assets/WONILMAX/Sci-Fi Barrel Pack/Materials/Sci-Fi Barrel Grey.mat";
    const string ModelMatFolder = "Assets/Materials/Batteries";
    const string PickupPrefabFolder = "Assets/Prefabs/Batteries";

    /// The battery model (Sci-Fi Barrel pack) under a pickup's "Visual", tinted in the rarity's colour.
    /// Replaces whatever model was there. The Visual's pivot is the middle of the battery (it spins and bobs around it).
    public static void BuildVisual(Transform visual, Rarity rarity)
    {
        for (int i = visual.childCount - 1; i >= 0; i--) Object.DestroyImmediate(visual.GetChild(i).gameObject);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarrelPrefab);
        if (prefab == null) { Debug.LogError("Batteries: model not found: " + BarrelPrefab); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, visual);
        go.name = "Model";
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * 0.9f;
        go.transform.localPosition = new Vector3(0f, -0.55f, 0f);
        var mat = ModelMaterial(rarity);
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
    }

    /// One material per rarity: a copy of the pack's grey battery, tinted and glowing in the rarity colour
    /// (the same colours as the battery cards in the UI), so all five share one set of textures.
    static Material ModelMaterial(Rarity rarity)
    {
        if (!AssetDatabase.IsValidFolder(ModelMatFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Batteries");
        string path = ModelMatFolder + "/BatteryModel_" + rarity + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(AssetDatabase.LoadAssetAtPath<Material>(BarrelMaterial));
            AssetDatabase.CreateAsset(m, path);
        }
        Color c = BatteryItem.RarityColor(rarity);
        // the pack's grey texture is dark, so the tint is pushed above 1 to get a clear colour
        Color tint = Color.Lerp(Color.white, c, 0.8f) * 1.8f;
        tint.a = 1f;
        m.SetColor("_BaseColor", rarity == Rarity.Common ? Color.white : tint);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * (rarity >= Rarity.Rare ? 2.2f : 1.2f));
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// Gives every battery pickup in the open scene the battery model, and makes one pickup prefab per rarity
    /// (Assets/Prefabs/Batteries) to drag into a scene.
    [MenuItem("Tools/Trash Game/6b. Batteries (New Model In This Scene + Pickup Prefabs)")]
    public static void ApplyModel()
    {
        int n = 0;
        foreach (var bp in Object.FindObjectsByType<BatteryPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (bp.battery == null || bp.visual == null || bp.visual == bp.transform) continue;
            BuildVisual(bp.visual, bp.battery.rarity);
            EditorUtility.SetDirty(bp);
            n++;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PickupPrefabFolder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Batteries");
        int made = 0;
        foreach (var item in BatteryItem.LoadAll())
        {
            var go = CreatePickup(item, null, Vector3.zero, "Battery Pickup (" + item.rarity + ")");
            PrefabUtility.SaveAsPrefabAsset(go, PickupPrefabFolder + "/Battery Pickup (" + item.rarity + ").prefab");
            Object.DestroyImmediate(go);
            made++;
        }
        AssetDatabase.SaveAssets();
        if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Batteries: new model on " + n + " pickups in this scene, " + made + " pickup prefabs in " + PickupPrefabFolder + ".");
    }
    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
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
        m.SetFloat("_Smoothness", 0.5f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m);
        return m;
    }
}
