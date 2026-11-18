using TMPro;
using UnityEngine;

/// Makes a "looks only" copy of a prefab: no scripts, colliders, labels or HUD sprites run or register.
/// Used for build-mode ghosts.
public static class VisualCopy
{
    public static GameObject Make(GameObject prefab, Transform parent)
    {
        // instantiate under an inactive holder so no Awake/OnEnable ever runs
        var holder = new GameObject("holder");
        holder.SetActive(false);
        var go = Object.Instantiate(prefab, holder.transform);
        go.name = prefab.name + " (ghost)";

        foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) if (t != null) Object.DestroyImmediate(t.gameObject);
        foreach (var r in go.GetComponentsInChildren<SpriteRenderer>(true)) if (r != null) Object.DestroyImmediate(r.gameObject);
        foreach (var r in go.GetComponentsInChildren<LineRenderer>(true)) if (r != null) Object.DestroyImmediate(r.gameObject);
        var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = behaviours.Length - 1; i >= 0; i--) if (behaviours[i] != null) Object.DestroyImmediate(behaviours[i]);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        go.transform.SetParent(parent, false);
        Object.Destroy(holder);
        return go;
    }
}
