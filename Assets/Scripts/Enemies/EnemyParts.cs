using UnityEngine;

/// Shared helpers for enemies that are built from code AND saved as prefabs (Tools > Trash Game > 13. Enemies (Make Prefabs)).
/// Everything here works in Edit mode too: materials are assets (a runtime `new Material` is lost when saved = pink),
/// colours live on a MeshTint component (a MaterialPropertyBlock alone is not saved), and Kill() uses DestroyImmediate
/// outside Play mode.
public static class EnemyParts
{
    public const string LitResource = "Enemies/EnemyLit";
    public const string UnlitResource = "Enemies/EnemyUnlit";

    static Material lit, unlit;

    public static Material Lit
    {
        get
        {
            if (lit == null) lit = Resources.Load<Material>(LitResource);
            if (lit == null) lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "EnemyLit (runtime)" };
            return lit;
        }
    }

    public static Material Unlit
    {
        get
        {
            if (unlit == null) unlit = Resources.Load<Material>(UnlitResource);
            if (unlit == null) unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "EnemyUnlit (runtime)" };
            return unlit;
        }
    }

    /// Destroy in Play mode, DestroyImmediate in Edit mode (prefab building).
    public static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }

    /// A coloured primitive without collider (projectiles hit the Health sphere, not colliders).
    public static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c, bool unlitMat = false)
    {
        var go = GameObject.CreatePrimitive(type);
        Kill(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = unlitMat ? Unlit : Lit;
        Tint(go, c, false);
        return go.transform;
    }

    /// Adds (or updates) a MeshTint: the colour is saved with the object and applied in Edit and Play mode.
    public static MeshTint Tint(GameObject go, Color c, bool children)
    {
        var t = go.GetComponent<MeshTint>();
        if (t == null) t = go.AddComponent<MeshTint>();
        t.includeChildren = children;
        t.Set(c);
        return t;
    }
}
