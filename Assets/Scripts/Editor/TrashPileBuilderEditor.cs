using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Inspector for the dynamic trash pile: builds it when it is first shown, rebuilds when Amount (or any look field) changes,
/// plus a Rebuild button (after moving the pile, or to re-roll the look).
[CustomEditor(typeof(TrashPileBuilder))]
[CanEditMultipleObjects]
public class TrashPileBuilderEditor : Editor
{
    void OnEnable()
    {
        if (Application.isPlaying) return;
        foreach (var o in targets)
        {
            var b = (TrashPileBuilder)o;
            if (b != null && b.autoSize && b.IsEmpty) EditorApplication.delayCall += () => { if (b != null) Rebuild(b); };
        }
    }

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool changed = EditorGUI.EndChangeCheck();

        var main = (TrashPileBuilder)target;
        if (main.autoSize)
            EditorGUILayout.HelpBox("Auto Size: Radius = how big the pile is (cyan sphere in the Scene view), Amount = how much trash / how dense. After moving the pile press Rebuild Pile.", MessageType.Info);

        if (Application.isPlaying) return;
        if (changed && main.autoSize)
            foreach (var o in targets) { var b = (TrashPileBuilder)o; EditorApplication.delayCall += () => { if (b != null) Rebuild(b); }; }

        if (GUILayout.Button("Rebuild Pile", GUILayout.Height(28)))
            foreach (var o in targets) Rebuild((TrashPileBuilder)o);
    }

    static void Rebuild(TrashPileBuilder b)
    {
        if (Application.isPlaying) return;
        if (b.mainMounds == null || b.mainMounds.Length == 0 || b.pieces == null || b.pieces.Length == 0)
        {
            Debug.LogWarning("TrashPileBuilder: assign the mound / piece prefabs first.", b);
            return;
        }
        b.Build();
        EditorUtility.SetDirty(b);
        var pile = b.GetComponent<TrashPile>();
        EditorUtility.SetDirty(pile);
        if (PrefabUtility.IsPartOfPrefabInstance(b)) { PrefabUtility.RecordPrefabInstancePropertyModifications(b); PrefabUtility.RecordPrefabInstancePropertyModifications(pile); }
        if (b.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
    }
}
