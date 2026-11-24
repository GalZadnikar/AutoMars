using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Tools > AutoMars > Models: swaps the primitive tool models on the player for the imported ones.
///  - Pickaxe: the three sci-fi pickaxes (Assets/3DBSciFiTool). All three sit under the player's "Pickaxe" object
///    and ToolProgress shows the one that belongs to the pickaxe level (1-2, 3-4, 5).
///  - Shovel: the MPL-50 shovel (Assets/Gruno Kromer Contents), scaled up to the size of the old one.
/// The "Pickaxe" / "Shovel" objects themselves, their Tip and Swing Trail stay, so nothing else has to be re-linked.
/// The packs' materials are unlit; lit copies are made in Assets/Materials/Tools so the tools take the scene's light.
/// Safe to re-run. Battery models: see BatteryBuilder.
public static class ToolModelBuilder
{
    const string MatFolder = "Assets/Materials/Tools";
    const string Picks = "Assets/3DBSciFiTool/";
    const string Shovel = "Assets/Gruno Kromer Contents/FREE Hand-Painted WW2 MPL-50 Shovel/";

    [MenuItem("Tools/AutoMars/Models/Replace Tool Models (pickaxe, shovel)")]
    public static void ReplaceTools()
    {
        var tools = Object.FindFirstObjectByType<PlayerTools>(FindObjectsInactive.Include);
        if (tools == null || tools.pickaxe == null || tools.shovelModel == null) { Debug.LogError("Tool models: no PlayerTools with a pickaxe and a shovel in this scene"); return; }
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Tools");
        var tp = tools.GetComponent<ToolProgress>();

        // ---- pickaxe: handle along +Y from the forearm, head across Z (thin along X), like the old one
        var pick = tools.pickaxe.transform;
        Clear(pick);
        var models = new GameObject[3];
        float top = 1f;
        for (int i = 0; i < 3; i++)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Picks + "cf_pickaxe" + (i + 1) + ".fbx");
            if (fbx == null) { Debug.LogError("Tool models: missing " + Picks + "cf_pickaxe" + (i + 1) + ".fbx"); return; }
            var mat = LitCopy("Pickaxe" + (i + 1), Picks + "Textures/CF_pickaxe" + (i + 1) + ".png", Picks + "Textures/CF_pickaxe" + (i + 1) + "_Ems.png");
            var m = Place(fbx, pick, "Model " + (i + 1), 0.88f, 0.1f, mat, out float t);
            if (i == 0) top = t;
            models[i] = m;
        }
        SetLocal(pick.Find("Tip"), new Vector3(0f, top - 0.06f, 0f));
        SetLocal(pick.Find("Swing Trail"), new Vector3(0f, top - 0.1f, 0f));

        // ---- shovel: blade at the top, flat side across Z
        var shovel = tools.shovelModel.transform;
        Clear(shovel);
        var sp = AssetDatabase.LoadAssetAtPath<GameObject>(Shovel + "Prefabs/WPN_USSR_Shovel_MPL50.prefab");
        if (sp == null) { Debug.LogError("Tool models: missing the shovel prefab"); return; }
        var smat = LitCopy("Shovel", Shovel + "Resources/Textures/WPN_USSR_Shovel_MPL50_TXT.png", null);
        Place(sp, shovel, "Model", 2.7f, 0.08f, smat, out float stop);
        SetLocal(shovel.Find("Tip"), new Vector3(0f, stop - 0.06f, 0f));
        SetLocal(shovel.Find("Swing Trail"), new Vector3(0f, stop - 0.25f, 0f));

        // ---- which pickaxe shows at which level + the HUD icons
        if (tp != null)
        {
            tp.pickaxeModels = models;
            var icons = new Sprite[3];
            bool wasActive = tools.pickaxe.activeSelf;
            for (int i = 0; i < 3; i++)
            {
                for (int k = 0; k < 3; k++) models[k].SetActive(k == i);
                icons[i] = MobileUIBuilder.RenderToolIcon(tools.pickaxe, "Assets/UI/Mobile/Icon_Pickaxe" + (i + 1) + ".png");
            }
            tp.pickaxeIcons = icons;
            tp.pickaxeIcon = icons[0];
            tp.shovelIcon = MobileUIBuilder.RenderToolIcon(tools.shovelModel, "Assets/UI/Mobile/Icon_Shovel.png");
            int look = tp.PickaxeLook();
            for (int k = 0; k < 3; k++) models[k].SetActive(k == look);
            tools.pickaxe.SetActive(wasActive);
            EditorUtility.SetDirty(tp);
        }
        else for (int k = 1; k < 3; k++) models[k].SetActive(false);

        EditorSceneManager.MarkSceneDirty(tools.gameObject.scene);
        Debug.Log("Tool models replaced (3 pickaxes by level, shovel). Lit materials in " + MatFolder + ".");
    }

    /// Removes the old model parts; the Tip and the Swing Trail stay.
    static void Clear(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var c = root.GetChild(i);
            if (c.name == "Tip" || c.name == "Swing Trail") continue;
            Object.DestroyImmediate(c.gameObject);
        }
    }

    static void SetLocal(Transform t, Vector3 p)
    {
        if (t != null) t.localPosition = p;
    }

    /// Puts a model under `root` standing on its lower end at height `bottom`, turned so its wide side is across Z.
    static GameObject Place(GameObject source, Transform root, string name, float scale, float bottom, Material mat, out float top)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(source, root);
        go.name = name;
        go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        go.transform.localScale = source.transform.localScale * scale;
        go.transform.localPosition = Vector3.zero;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            foreach (var v in mf.sharedMesh.vertices)
            {
                float y = root.InverseTransformPoint(mf.transform.TransformPoint(v)).y;
                lo = Mathf.Min(lo, y);
                hi = Mathf.Max(hi, y);
            }
            var r = mf.GetComponent<MeshRenderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
        }
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        go.transform.localPosition = new Vector3(0f, bottom - lo, 0f);
        top = hi + bottom - lo;
        return go;
    }

    static Material LitCopy(string name, string texPath, string emissionPath)
    {
        string path = MatFolder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.25f);
        m.SetFloat("_Metallic", 0f);
        var ems = emissionPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath) : null;
        if (ems != null)
        {
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", ems);
            m.SetColor("_EmissionColor", Color.white * 1.6f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(m);
        return m;
    }
}
