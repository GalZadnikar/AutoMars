using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Adds the pipe (base ↔ player tether) to the home base. Safe to re-run.
public static class PipeTetherBuilder
{
    const string PipeMat = "Assets/Materials/Pipe.mat";
    const string LimitMat = "Assets/Materials/PipeRange.mat";

    [MenuItem("Tools/Trash Game/9. Pipe Tether (Base to Player)")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        HomeBase home = null;
        foreach (var h in Object.FindObjectsByType<HomeBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (home == null || h.name == "Base_Home") home = h;
        if (home == null) { Debug.LogError("Pipe Tether: no HomeBase in the scene"); return; }

        var tether = home.GetComponent<PipeTether>();
        if (tether == null) tether = Undo.AddComponent<PipeTether>(home.gameObject);
        var player = Object.FindFirstObjectByType<JoystickPlayerController>(FindObjectsInactive.Include);
        if (player != null) tether.player = player.transform;
        tether.pipeMaterial = PipeMaterial();
        tether.limitMaterial = LimitMaterial();
        EditorUtility.SetDirty(tether);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Pipe Tether added to " + home.name);
    }

    static Material PipeMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(PipeMat);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", new Color(0.66f, 0.63f, 0.6f));
        m.SetFloat("_Smoothness", 0.35f);
        m.SetFloat("_Metallic", 0.1f);
        AssetDatabase.CreateAsset(m, PipeMat);
        return m;
    }

    static Material LimitMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(LimitMat);
        if (m != null) return m;
        m = new Material(Shader.Find("Sprites/Default"));   // unlit, vertex colour, transparent
        AssetDatabase.CreateAsset(m, LimitMat);
        return m;
    }
}
