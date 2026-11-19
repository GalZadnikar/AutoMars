using System.IO;
using UnityEditor;
using UnityEngine;

/// 13. Enemies (E1 / E2 / E3): copies the Voodoo drone prefab to Assets/Resources/Enemies/EnemyDroneModel.prefab (the enemy
/// drone's look; without it the drone is built from primitives) and adds hand-placed spawners.
/// 13. Enemies (Make Prefabs) saves the four enemies as prefabs in Assets/Resources/Enemies (Enemy Scout, Enemy Drone,
/// Leech Hole, Target Dummy): drag them into the scene to place them. Spawn() / spawners / the debug menu use the same prefabs.
public static class EnemyBuilder
{
    const string ModelPath = "Assets/VoodooPlay/Prefab/Drone.prefab";
    const string TargetPath = "Assets/Resources/Enemies/EnemyDroneModel.prefab";

    const string ScoutPrefab = "Assets/Prefabs/ScoutBot.prefab";
    const string ScoutTarget = "Assets/Resources/Enemies/EnemyScoutModel.prefab";

    /// The enemy scout looks like the player's Scout Bot (just the animated mesh, no scripts / labels), tinted red at runtime.
    [MenuItem("Tools/Trash Game/13. Enemies (Scout Model)")]
    public static void BuildScoutModel()
    {
        if (!Directory.Exists("Assets/Resources/Enemies")) Directory.CreateDirectory("Assets/Resources/Enemies");
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(ScoutPrefab);
        if (src == null) { Debug.LogError("[Enemies] " + ScoutPrefab + " not found"); return; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var mesh = inst.transform.Find("SR_01_Scout_Bot");
        if (mesh == null) { Object.DestroyImmediate(inst); Debug.LogError("[Enemies] SR_01_Scout_Bot not found in the scout prefab"); return; }
        mesh.SetParent(null, false);
        PrefabUtility.SaveAsPrefabAsset(mesh.gameObject, ScoutTarget);
        Object.DestroyImmediate(mesh.gameObject);
        Object.DestroyImmediate(inst);
        AssetDatabase.SaveAssets();
        Debug.Log("[Enemies] created " + ScoutTarget);
    }

    [MenuItem("Tools/Trash Game/13. Enemies (Drone Model)")]
    public static void BuildModel()
    {
        if (!Directory.Exists("Assets/Resources/Enemies")) Directory.CreateDirectory("Assets/Resources/Enemies");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath) != null) { Debug.Log("[Enemies] model already exists: " + TargetPath); return; }
        if (!AssetDatabase.CopyAsset(ModelPath, TargetPath)) { Debug.LogError("[Enemies] could not copy " + ModelPath); return; }
        AssetDatabase.SaveAssets();
        Debug.Log("[Enemies] created " + TargetPath);
    }

    [MenuItem("Tools/Trash Game/13. Enemies (Add Scout Spawner)")]
    public static void AddScoutSpawner() => AddSpawner(EnemySpawner.Kind.Scout);

    [MenuItem("Tools/Trash Game/13. Enemies (Add Drone Spawner)")]
    public static void AddDroneSpawner() => AddSpawner(EnemySpawner.Kind.Drone);

    static void AddSpawner(EnemySpawner.Kind kind)
    {
        var go = new GameObject(kind == EnemySpawner.Kind.Scout ? "Enemy Spawner (Scout)" : "Enemy Spawner (Drone)");
        var sv = SceneView.lastActiveSceneView;
        go.transform.position = sv != null ? sv.pivot : Vector3.zero;
        var sp = go.AddComponent<EnemySpawner>();
        sp.kind = kind;
        sp.maxAlive = kind == EnemySpawner.Kind.Scout ? 2 : 1;
        Undo.RegisterCreatedObjectUndo(go, "Add Enemy Spawner");
        Selection.activeGameObject = go;
    }

    // ---------------------------------------------------------------- prefabs

    const string Folder = "Assets/Resources/Enemies";
    public const string ScoutPrefabPath = Folder + "/Enemy Scout.prefab";
    public const string DronePrefabPath = Folder + "/Enemy Drone.prefab";
    public const string LeechPrefabPath = Folder + "/Leech Hole.prefab";
    public const string DummyPrefabPath = Folder + "/Target Dummy.prefab";

    /// Makes the missing enemy prefabs (existing ones keep your edits; delete one to get it rebuilt).
    [MenuItem("Tools/Trash Game/13. Enemies (Make Prefabs)")]
    public static void MakePrefabs()
    {
        if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        Mat(EnemyParts.LitResource, "Universal Render Pipeline/Lit");
        Mat(EnemyParts.UnlitResource, "Universal Render Pipeline/Unlit");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ScoutTarget) == null) BuildScoutModel();
        BuildModel();
        BuildLeechMaterial();

        Make(ScoutPrefabPath, "Enemy Scout", go => EnemyScout.Create(go));
        Make(DronePrefabPath, "Enemy Drone", go => EnemyDrone.Create(go));
        Make(LeechPrefabPath, "Leech Hole", go => { go.AddComponent<LeechBody>(); go.AddComponent<EnemyLeech>(); });
        Make(DummyPrefabPath, "Target Dummy", go => TargetDummy.Create(go));
        AssetDatabase.SaveAssets();
    }

    static void Mat(string resource, string shader)
    {
        string path = "Assets/Resources/" + resource + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        var m = new Material(Shader.Find(shader)) { name = Path.GetFileNameWithoutExtension(path) };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
        AssetDatabase.CreateAsset(m, path);
    }

    static void Make(string path, string name, System.Action<GameObject> setup)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) { Debug.Log("[Enemies] prefab already exists (delete it to rebuild): " + path); return; }
        var go = new GameObject(name);
        try
        {
            setup(go);
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Debug.Log("[Enemies] created " + path);
        }
        finally { Object.DestroyImmediate(go); }
    }

    // ---------------------------------------------------------------- E3 leech

    const string LeechMat = "Assets/Resources/Enemies/LeechFlesh.mat";

    /// The leech's base material (AutoMars/LeechFlesh, fog-masked). Kept in Resources so the shader ships in builds.
    [MenuItem("Tools/Trash Game/13. Enemies (Leech Material)")]
    public static void BuildLeechMaterial()
    {
        if (!Directory.Exists("Assets/Resources/Enemies")) Directory.CreateDirectory("Assets/Resources/Enemies");
        if (AssetDatabase.LoadAssetAtPath<Material>(LeechMat) != null) { Debug.Log("[Enemies] leech material already exists: " + LeechMat); return; }
        var sh = Shader.Find("AutoMars/LeechFlesh");
        if (sh == null) { Debug.LogError("[Enemies] shader AutoMars/LeechFlesh not found"); return; }
        AssetDatabase.CreateAsset(new Material(sh) { name = "LeechFlesh" }, LeechMat);
        AssetDatabase.SaveAssets();
        Debug.Log("[Enemies] created " + LeechMat);
    }

    /// A leech hole at the Scene view pivot (best in a dark edge / fog). Makes the material first if needed.
    [MenuItem("Tools/Trash Game/13. Enemies (Add Leech Hole)")]
    public static void AddLeechHole()
    {
        BuildLeechMaterial();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(LeechPrefabPath) == null) MakePrefabs();
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(LeechPrefabPath));
        var sv = SceneView.lastActiveSceneView;
        go.transform.position = sv != null ? sv.pivot : Vector3.zero;
        Undo.RegisterCreatedObjectUndo(go, "Add Leech Hole");
        Selection.activeGameObject = go;
    }
}
