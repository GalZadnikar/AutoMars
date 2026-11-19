using UnityEngine;

/// Colours a renderer (or all renderers below it) through a MaterialPropertyBlock (_BaseColor), so many parts can
/// share one material asset. Saved with the object, applied in Edit mode too (prefabs show their real colours).
[ExecuteAlways]
[DisallowMultipleComponent]
public class MeshTint : MonoBehaviour
{
    public Color color = Color.white;
    [Tooltip("Tint every renderer below this object too (e.g. a whole imported model)")]
    public bool includeChildren;

    static MaterialPropertyBlock mpb;

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    public void Set(Color c)
    {
        color = c;
        Apply();
    }

    public void Apply()
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        mpb.Clear();
        mpb.SetColor("_BaseColor", color);
        if (includeChildren)
            foreach (var r in GetComponentsInChildren<Renderer>(true)) Apply(r);
        else
        {
            var r = GetComponent<Renderer>();
            if (r != null) Apply(r);
        }
    }

    static void Apply(Renderer r)
    {
        if (r is SpriteRenderer || r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer) return;
        r.SetPropertyBlock(mpb);
    }
}
