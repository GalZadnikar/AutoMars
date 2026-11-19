using System.Collections.Generic;
using UnityEngine;

/// One piece of trash lying on the ground (E1). The player walks over it to pick it up (PlayerCarrier),
/// CollectTrash bots go for it too (nearest pile or loose trash). Comes from a knocked-out player's bag,
/// scrapped bots and killed Enemy Scouts: LooseTrash.Drop(pos, count).
public class LooseTrash : MonoBehaviour
{
    public static readonly List<LooseTrash> All = new List<LooseTrash>();

    /// More than this on the map: the oldest ones are removed.
    public static int MaxOnMap = 120;
    public float pickupRadius = 1.4f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); mat = null; }

    float enableTime;
    bool taken;
    Vector3 tossFrom, tossTo;
    float tossT = 1f, tossHeight = 1.2f;
    static Material mat;

    /// Not picked up yet and past its pickup delay (a fresh drop stays a moment so it doesn't fly straight back).
    public bool Available => !taken && Time.time >= enableTime;
    public Vector3 TakePoint => transform.position + Vector3.up * 0.25f;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// Takes it (it disappears). `from` = where the item flies from.
    public bool TryTake(out Vector3 from)
    {
        from = TakePoint;
        if (!Available) return false;
        taken = true;
        All.Remove(this);
        Destroy(gameObject);
        return true;
    }

    public bool InRange(Vector3 p)
    {
        Vector3 d = p - transform.position; d.y = 0f;
        return d.sqrMagnitude <= pickupRadius * pickupRadius;
    }

    public static LooseTrash Nearest(Vector3 pos, float maxDist = float.MaxValue, bool onlyAvailable = true)
    {
        LooseTrash best = null;
        float bd = maxDist * maxDist;
        for (int i = 0; i < All.Count; i++)
        {
            var t = All[i];
            if (t == null || t.taken || (onlyAvailable && !t.Available)) continue;
            Vector3 d = t.transform.position - pos; d.y = 0f;
            float m = d.sqrMagnitude;
            if (m < bd) { bd = m; best = t; }
        }
        return best;
    }

    /// Drops `count` pieces around `pos` (tossed out in an arc). Returns how many were made.
    public static int Drop(Vector3 pos, int count, float scatter = 1.5f, float pickupDelay = 1f)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 r = Random.insideUnitCircle * scatter;
            if (r.sqrMagnitude < 0.25f) r = r.normalized * 0.5f + new Vector2(0.01f * i, 0f);
            Vector3 to = new Vector3(pos.x + r.x, 0f, pos.z + r.y);
            to.y = GroundUtil.Height(to, pos.y);
            Create(pos + Vector3.up * 0.8f, to, pickupDelay);
        }
        while (All.Count > MaxOnMap) { var o = All[0]; All.RemoveAt(0); if (o != null) Destroy(o.gameObject); }
        return count;
    }

    static LooseTrash Create(Vector3 from, Vector3 to, float delay)
    {
        var go = new GameObject("Loose Trash");
        go.transform.position = from;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        if (mat == null) mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        // a random lump of junk: a box, a can or a ball in a dull colour
        int kind = Random.Range(0, 3);
        var type = kind == 0 ? PrimitiveType.Cube : kind == 1 ? PrimitiveType.Cylinder : PrimitiveType.Sphere;
        var v = GameObject.CreatePrimitive(type);
        Destroy(v.GetComponent<Collider>());
        v.transform.SetParent(go.transform, false);
        float s = Random.Range(0.3f, 0.45f);
        v.transform.localScale = new Vector3(s, s * (kind == 1 ? 0.8f : Random.Range(0.6f, 1f)), s * Random.Range(0.8f, 1.2f));
        v.transform.localPosition = Vector3.up * (v.transform.localScale.y * 0.5f);
        v.transform.localRotation = Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f));
        Color[] cols = { new Color(0.45f, 0.4f, 0.33f), new Color(0.33f, 0.36f, 0.3f), new Color(0.5f, 0.45f, 0.42f), new Color(0.38f, 0.33f, 0.4f), new Color(0.55f, 0.4f, 0.25f) };
        var r = v.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", cols[Random.Range(0, cols.Length)]);
        r.SetPropertyBlock(mpb);

        var lt = go.AddComponent<LooseTrash>();
        lt.enableTime = Time.time + delay;
        lt.tossFrom = from; lt.tossTo = to; lt.tossT = 0f;
        lt.tossHeight = Random.Range(0.8f, 1.6f);
        go.AddComponent<FogHideable>().hide = FogHideable.Mode.WhenUnexplored;
        return lt;
    }

    void Update()
    {
        if (tossT >= 1f) return;
        tossT = Mathf.Min(1f, tossT + Time.deltaTime / 0.45f);
        transform.position = Vector3.Lerp(tossFrom, tossTo, tossT) + Vector3.up * Mathf.Sin(tossT * Mathf.PI) * tossHeight;
    }
}
