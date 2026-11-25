using System.Collections.Generic;
using UnityEngine;

/// Plays effect prefabs (Epic Toon FX and the like) from a pool: FxPool.Play(prefab, position, rotation, scale).
/// Every instance is prepared once: its particle systems follow the transform scale (the pack's effects are made
/// for a bigger world, so they are played at 0.3 - 0.7), colliders are removed and its AudioSources are switched
/// by FxPool.Sound (off by default: the game has no other sound yet).
public static class FxPool
{
    /// Play the sounds that come with the effect prefabs. Read when an instance is made.
    public static bool Sound;

    class Live
    {
        public GameObject go, prefab;
        public float until;
    }

    static readonly Dictionary<GameObject, Stack<GameObject>> pools = new Dictionary<GameObject, Stack<GameObject>>();
    static readonly List<Live> live = new List<Live>();
    static Transform root, staging;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pools.Clear(); live.Clear(); root = null; staging = null; Sound = false; }

    static void Ensure()
    {
        if (root != null) return;
        var go = new GameObject("FX Pool");
        root = go.transform;
        go.AddComponent<FxPoolRunner>();
        // new instances are made under an inactive object, so nothing plays (or beeps) before it is prepared
        var st = new GameObject("Staging");
        st.SetActive(false);
        staging = st.transform;
        staging.SetParent(root, false);
    }

    static GameObject Make(GameObject prefab)
    {
        var go = Object.Instantiate(prefab, staging);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var m = ps.main;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            m.stopAction = ParticleSystemStopAction.None;
        }
        foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) a.enabled = Sound;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        // Hovl Studio prefabs bring their own mover / laser scripts (HS_...); the game drives the effect itself
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetType().Name.StartsWith("HS_")) Object.DestroyImmediate(mb);
        return go;
    }

    /// A prepared instance that stays under `parent` (a projectile's trail, a pickup's glow). Not pooled.
    public static GameObject Create(GameObject prefab, Transform parent, float scale = 1f)
    {
        if (prefab == null) return null;
        Ensure();
        var go = Make(prefab);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    /// Plays a one-shot effect and takes it back after `life` seconds.
    public static GameObject Play(GameObject prefab, Vector3 pos, Quaternion rot, float scale = 1f, float life = 1.5f)
    {
        if (prefab == null || !Application.isPlaying) return null;
        Ensure();
        Stack<GameObject> pool;
        if (!pools.TryGetValue(prefab, out pool)) pools[prefab] = pool = new Stack<GameObject>();
        GameObject go = null;
        while (pool.Count > 0 && go == null) go = pool.Pop();
        if (go == null)
        {
            go = Make(prefab);
            go.SetActive(false);
            go.transform.SetParent(root, false);
        }
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = Vector3.one * scale;
        go.SetActive(true);
        var ps = go.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            ps.Clear(true);
            ps.Play(true);
        }
        live.Add(new Live { go = go, prefab = prefab, until = Time.time + life });
        return go;
    }

    internal static void Tick()
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var l = live[i];
            if (l.go == null) { live.RemoveAt(i); continue; }
            if (Time.time < l.until) continue;
            l.go.SetActive(false);
            pools[l.prefab].Push(l.go);
            live.RemoveAt(i);
        }
    }
}
