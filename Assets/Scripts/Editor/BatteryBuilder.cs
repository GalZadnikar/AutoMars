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
        var white = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/HomeWhite.mat");
        var ringMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ZoneRing.mat");

        int n = 0;
        foreach (var s in Spots)
        {
            var item = items[(int)s.r];
            var mat = LitMaterial("Battery_" + s.r, item.Color, item.Color * (s.r >= Rarity.Rare ? 1.4f : 0.6f));

            var go = new GameObject(item.displayName + " " + (++n));
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(s.x, GroundUtil.Height(new Vector3(s.x, 0f, s.z), 0f), s.z);
            var bp = go.AddComponent<BatteryPickup>();
            bp.battery = item;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            visual.localPosition = new Vector3(0f, 1.0f, 0f);
            Prim(PrimitiveType.Cylinder, "Body", visual, Vector3.zero, new Vector3(0.6f, 0.55f, 0.6f), mat);
            Prim(PrimitiveType.Cylinder, "Cap", visual, new Vector3(0f, 0.62f, 0f), new Vector3(0.28f, 0.08f, 0.28f), white);
            Prim(PrimitiveType.Cylinder, "Band", visual, new Vector3(0f, 0.2f, 0f), new Vector3(0.63f, 0.06f, 0.63f), white);
            bp.visual = visual;

            if (s.r >= Rarity.Rare)   // loot beam
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
        }
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
