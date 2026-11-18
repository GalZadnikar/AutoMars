using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GroundRingTools
{
    [MenuItem("Tools/Trash Game/Refresh Ground Rings")]
    public static void RefreshAll()
    {
        int n = 0;
        foreach (var r in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            r.Rebuild();
            EditorUtility.SetDirty(r.GetComponent<LineRenderer>());
            n++;
        }
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("Refreshed " + n + " ground ring(s).");
    }
}
