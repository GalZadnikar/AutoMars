using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Module 2: adds the [Lv N] tag to the Scout Bot prefab and ProximityTags to the Player. Safe to re-run.
public static class LevelTagBuilder
{
    const string BotPrefabPath = "Assets/Prefabs/ScoutBot.prefab";

    [MenuItem("Tools/Trash Game/4. Robot Level Tags")]
    public static void Build()
    {
        var root = PrefabUtility.LoadPrefabContents(BotPrefabPath);
        try
        {
            var bot = root.GetComponent<ScoutBot>();
            var old = root.transform.Find("LevelTag");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var go = new GameObject("LevelTag");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 3.9f, 0f);   // just above the status label (y 3.0)
            var t = go.AddComponent<TextMeshPro>();
            t.text = "[Lv 1]";
            t.fontSize = 6f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(8f, 2f);
            t.outlineWidth = 0.28f;
            t.outlineColor = new Color32(30, 20, 15, 255);
            go.AddComponent<Billboard>();
            var tag = go.AddComponent<BotLevelTag>();
            tag.text = t;
            bot.levelTag = tag;

            PrefabUtility.SaveAsPrefabAsset(root, BotPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var player = GameObject.Find("Player");
        if (player.GetComponent<ProximityTags>() == null) player.AddComponent<ProximityTags>();
        EditorUtility.SetDirty(player);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Level tags added. Bots show [Lv N] only when you walk close (Player > ProximityTags).");
    }
}
