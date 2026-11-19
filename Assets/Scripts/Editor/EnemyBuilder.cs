using System.IO;
using UnityEditor;
using UnityEngine;

/// 13. Enemies (E1 / E2 / E3): copies the Voodoo drone prefab to Assets/Resources/Enemies/EnemyDroneModel.prefab (the enemy
/// drone's look; without it the drone is built from primitives) and adds hand-placed spawners.
/// Enemies themselves are built in code, so there is nothing else to set up.
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
        var go = new GameObject("Leech Hole");
        var sv = SceneView.lastActiveSceneView;
        go.transform.position = sv != null ? sv.pivot : Vector3.zero;
        go.AddComponent<EnemyLeech>();
        Undo.RegisterCreatedObjectUndo(go, "Add Leech Hole");
        Selection.activeGameObject = go;
    }
}
