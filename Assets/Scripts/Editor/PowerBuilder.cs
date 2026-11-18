using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Module 3: Charging Station near the base + battery bar on the Scout Bot prefab. Safe to re-run.
public static class PowerBuilder
{
    const string BotPrefabPath = "Assets/Prefabs/ScoutBot.prefab";
    const string WhitePath = "Assets/UI/Generated/White.png";
    static readonly Vector2 StationSpot = new Vector2(0f, -12f);

    [MenuItem("Tools/Trash Game/5. Power (Charging Station + Battery Bar)")]
    public static void Build()
    {
        var white = WhiteSprite();
        AddBatteryBar(white);
        CreateStation();

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Power built: Charging Station placed, bots now use batteries.");
    }

    // ------------------------------------------------------------------ bot
    static void AddBatteryBar(Sprite white)
    {
        var root = PrefabUtility.LoadPrefabContents(BotPrefabPath);
        try
        {
            var bot = root.GetComponent<ScoutBot>();
            var old = root.transform.Find("BatteryBar");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var bar = new GameObject("BatteryBar");
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0f, 3.45f, 0f);   // between status label (3.0) and level tag (3.9)
            bar.AddComponent<Billboard>();
            var bb = bar.AddComponent<BatteryBar>();
            bb.background = Quad("Bg", bar.transform, white, 0);
            bb.fill = Quad("Fill", bar.transform, white, 1);
            bot.batteryBar = bb;
            bot.batteryRuntime = 120f;   // = "Common battery: 2 minutes" from the design doc
            PrefabUtility.SaveAsPrefabAsset(root, BotPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static SpriteRenderer Quad(string name, Transform parent, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
        sr.sortingOrder = order;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        return sr;
    }

    static Sprite WhiteSprite()
    {
        if (!AssetDatabase.IsValidFolder("Assets/UI")) AssetDatabase.CreateFolder("Assets", "UI");
        if (!AssetDatabase.IsValidFolder("Assets/UI/Generated")) AssetDatabase.CreateFolder("Assets/UI", "Generated");
        if (!File.Exists(WhitePath))
        {
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var px = new Color32[64];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            File.WriteAllBytes(WhitePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(WhitePath, ImportAssetOptions.ForceUpdate);
        }
        var ti = (TextureImporter)AssetImporter.GetAtPath(WhitePath);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 8;   // 1 world unit, scaled by the bar
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(WhitePath);
    }

    // ------------------------------------------------------------------ station
    static void CreateStation()
    {
        var existing = GameObject.Find("Charging Station");
        Vector3 pos = existing != null ? existing.transform.position : OnGround(StationSpot.x, StationSpot.y);
        Quaternion rot = existing != null ? existing.transform.rotation : Quaternion.identity;
        if (existing != null) Object.DestroyImmediate(existing);

        var root = new GameObject("Charging Station");
        root.transform.SetPositionAndRotation(pos, rot);
        var st = root.AddComponent<ChargingStation>();
        var fp = root.AddComponent<GridFootprint>();   // build grid: base + pads
        fp.size = new Vector2Int(3, 3);
        fp.centerOffset = new Vector2(0f, -1f);

        var dark = LitMaterial("ChargerBody", new Color(0.32f, 0.34f, 0.38f), Color.black);
        var light = LitMaterial("ChargerLight", new Color(0.45f, 0.95f, 1f), new Color(0.3f, 0.9f, 1.2f));
        var padMat = LitMaterial("ChargerPad", new Color(0.25f, 0.55f, 0.65f), new Color(0.1f, 0.25f, 0.3f));

        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        var baseGo = Prim(PrimitiveType.Cylinder, "Base", body, new Vector3(0f, 0.15f, 0f), new Vector3(2.8f, 0.15f, 2.8f), dark, keepCollider: true);
        Prim(PrimitiveType.Cylinder, "Pillar", body, new Vector3(0f, 1.1f, 0f), new Vector3(0.7f, 0.9f, 0.7f), dark);
        Prim(PrimitiveType.Cylinder, "Ring", body, new Vector3(0f, 1.9f, 0f), new Vector3(1.3f, 0.06f, 1.3f), light);
        var core = Prim(PrimitiveType.Cube, "Core", body, new Vector3(0f, 2.6f, 0f), new Vector3(0.8f, 0.8f, 0.8f), light);
        core.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        st.core = core.transform;

        // two pads in front (local -Z), waiting line continues further out
        var pads = new Transform[2];
        var padR = new Renderer[2];
        for (int i = 0; i < 2; i++)
        {
            var pad = Prim(PrimitiveType.Cylinder, "Pad " + (i + 1), root.transform, new Vector3(i == 0 ? -1.7f : 1.7f, 0.03f, -2.6f),
                           new Vector3(1.5f, 0.03f, 1.5f), padMat);
            pad.transform.position = new Vector3(pad.transform.position.x, GroundUtil.Height(pad.transform.position, pos.y) + 0.03f, pad.transform.position.z);
            pads[i] = pad.transform;
            padR[i] = pad.GetComponent<Renderer>();
        }
        st.pads = pads;
        st.padRenderers = padR;
        st.queueStart = new Vector3(0f, 0f, -4.8f);
        st.queueStep = new Vector3(0f, 0f, -1.4f);

        var label = new GameObject("Label");
        label.transform.SetParent(root.transform, false);
        label.transform.localPosition = new Vector3(0f, 4.0f, 0f);
        var t = label.AddComponent<TextMeshPro>();
        t.fontSize = 7f; t.alignment = TextAlignmentOptions.Center; t.fontStyle = FontStyles.Bold;
        t.rectTransform.sizeDelta = new Vector2(12f, 3f);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.outlineWidth = 0.25f; t.outlineColor = new Color32(40, 25, 20, 255);
        t.text = st.idleText;
        label.AddComponent<Billboard>();
        st.label = t;

        // reveal fog around it like the other buildings
        var fog = root.AddComponent<FogRevealer>();
        var factory = GameObject.Find("Bot Factory");
        var src = factory != null ? factory.GetComponent<FogRevealer>() : null;
        if (src != null) EditorUtility.CopySerialized(src, fog);

        _ = baseGo;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
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
        m.SetFloat("_Smoothness", 0.35f);
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
}
