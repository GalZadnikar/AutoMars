using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Tools > AutoMars > Weapon > Build Pistol: makes the pistol model in the player's right hand (primitives, like the
/// pickaxe), adds / wires PlayerPistol and PlayerTools, renders its icon and adds the weapon tile to the mobile HUD.
/// Safe to re-run: it replaces the model and the tile, and keeps the numbers on an existing PlayerPistol.
public static class PistolBuilder
{
    static readonly Color Steel = new Color(0.26f, 0.28f, 0.33f);
    static readonly Color Light = new Color(0.62f, 0.65f, 0.7f);
    static readonly Color Grip = new Color(0.5f, 0.33f, 0.2f);
    static readonly Color Accent = new Color(1f, 0.62f, 0.15f);

    [MenuItem("Tools/AutoMars/Weapon/Build Pistol (model + HUD tile)")]
    public static void Build()
    {
        var tools = Object.FindFirstObjectByType<PlayerTools>(FindObjectsInactive.Include);
        if (tools == null || tools.rightForearm == null) { Debug.LogError("Pistol: no PlayerTools with a right forearm in the scene"); return; }
        var fore = tools.rightForearm;
        Transform hand = fore.childCount > 0 ? fore.Find(fore.name + "_end") : null;
        if (hand == null) hand = fore;

        var old = fore.Find("Pistol");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // origin = the grip (in the hand), barrel along +Z, top = +Y
        var root = new GameObject("Pistol");
        Part(root, "Slide", new Vector3(0f, 0.075f, 0.19f), new Vector3(0.085f, 0.1f, 0.46f), Vector3.zero, Steel);
        Part(root, "Barrel", new Vector3(0f, 0.08f, 0.45f), new Vector3(0.055f, 0.055f, 0.1f), Vector3.zero, Light);
        Part(root, "Top Stripe", new Vector3(0f, 0.13f, 0.17f), new Vector3(0.035f, 0.02f, 0.3f), Vector3.zero, Accent);
        Part(root, "Sight", new Vector3(0f, 0.145f, 0.38f), new Vector3(0.025f, 0.035f, 0.04f), Vector3.zero, Light);
        Part(root, "Grip", new Vector3(0f, -0.07f, 0.0f), new Vector3(0.075f, 0.22f, 0.11f), new Vector3(14f, 0f, 0f), Grip);
        Part(root, "Guard", new Vector3(0f, -0.035f, 0.13f), new Vector3(0.03f, 0.025f, 0.13f), Vector3.zero, Steel);
        Part(root, "Guard Front", new Vector3(0f, 0.0f, 0.19f), new Vector3(0.03f, 0.07f, 0.025f), Vector3.zero, Steel);
        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(root.transform, false);
        muzzle.localPosition = new Vector3(0f, 0.08f, 0.52f);

        root.transform.position = hand.position;
        root.transform.rotation = Quaternion.LookRotation(fore.up, Vector3.up);
        root.transform.SetParent(fore, true);   // the arm bones are scaled; this keeps the gun's world size

        var pistol = tools.GetComponent<PlayerPistol>();
        if (pistol == null) pistol = Undo.AddComponent<PlayerPistol>(tools.gameObject);
        pistol.model = root.transform;
        pistol.muzzle = muzzle;
        pistol.hand = hand;
        pistol.rightUpperArm = tools.rightUpperArm;
        pistol.rightForearm = fore;
        tools.pistol = pistol;
        EditorUtility.SetDirty(pistol);
        EditorUtility.SetDirty(tools);

        MobileUIBuilder.RenderPistolIcon(true);
        root.SetActive(tools.tool == PlayerTool.Pistol);
        MobileUIBuilder.AddWeaponSlot();

        EditorSceneManager.MarkSceneDirty(tools.gameObject.scene);
        Debug.Log("Pistol built: model under " + fore.name + ", PlayerPistol on " + tools.name + ", weapon tile in the HUD.");
    }

    static void Part(GameObject root, string name, Vector3 pos, Vector3 scale, Vector3 euler, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale;
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Enemies/EnemyLit.mat");
        if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.AddComponent<MeshTint>().Set(color);
    }
}
