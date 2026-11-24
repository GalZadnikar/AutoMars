using System;
using System.Collections.Generic;
using UnityEngine;

/// How an object behaves in the fog of war. Put it on anything (rock, pickup, enemy) and pick a mode:
///  1. Always shown: nothing is hidden. Like the sand: the dark still dims it, so out there it is only faintly visible.
///  2. Only while in sight: like enemies and the leech, shown only inside somebody's vision right now.
///  3. Hidden until discovered: not there until somebody sees it for the first time, then it pops in and stays
///     for good (from then on it looks like mode 1: faint in the dark).
///  (old) On explored ground: shown where the ground is explored or in sight (loose trash uses it).
/// An object without this component behaves like mode 1.
public class FogHideable : MonoBehaviour
{
    public enum Mode
    {
        [InspectorName("2 - Only while in sight (enemies, leech)")] WhenNotVisible = 0,
        [InspectorName("(old) On explored ground")] WhenUnexplored = 1,
        [InspectorName("3 - Hidden until discovered, then stays")] UntilDiscovered = 2,
        [InspectorName("1 - Always shown (like sand)")] Never = 3,
    }

    public Mode hide = Mode.WhenNotVisible;
    [Tooltip("Seconds between checks.")]
    public float checkInterval = 0.2f;

    [Header("Hidden until discovered")]
    [Tooltip("Size of the object for the 'has somebody seen it' test (m): it counts as seen when its centre or its rim comes into sight. 0 = measured from its renderers")]
    public float discoverRadius;
    [Tooltip("Seconds of the pop-in when it is discovered. 0 = it just appears")]
    public float appearTime = 0.35f;
    [Tooltip("What scales up in the pop-in. Empty = this object")]
    public Transform appearTarget;
    [Tooltip("Small dust puff when it is discovered")]
    public bool appearPuff = true;

    public bool IsShown => shown;
    /// Mode 3: somebody has seen it (it stays shown from then on).
    public bool IsDiscovered => discovered;
    /// Mode 3: raised the moment it is discovered during play.
    public event Action Discovered;

    static readonly List<Renderer> rendererBuf = new List<Renderer>();
    static readonly List<Canvas> canvasBuf = new List<Canvas>();

    bool shown = true, discovered, started;
    float next, radius, appearT = -1f;
    Transform popTarget;
    Vector3 fullScale;

    void OnEnable() => next = Time.time + UnityEngine.Random.value * checkInterval; // spread checks over frames

    void Start()
    {
        started = true;
        radius = discoverRadius > 0f ? discoverRadius : MeasureRadius();
        Apply(Wanted(), true);   // right from the first frame, so nothing that should be hidden flashes up
    }

    void OnDisable()
    {
        if (appearT >= 0f && popTarget != null) popTarget.localScale = fullScale;   // don't leave it half grown
        appearT = -1f;
    }

    void Update()
    {
        if (appearT >= 0f) Pop();
        if (Time.time < next) return;
        next = Time.time + checkInterval;

        bool want = Wanted();
        if (want != shown) Apply(want, false);
        else if (!shown) SetRenderers(false);   // also hides what was added while hidden (an HP bar...)
    }

    /// For loading a save: mark a mode 3 object as found (no pop-in) or not found.
    public void SetDiscovered(bool value)
    {
        discovered = value;
        if (started) Apply(Wanted(), true);
    }

    bool Wanted()
    {
        var fog = FogOfWar.Instance;
        if (fog == null || hide == Mode.Never) return true;
        Vector3 p = transform.position;
        if (hide == Mode.WhenNotVisible) return fog.IsVisible(p);
        if (hide == Mode.WhenUnexplored) return fog.IsKnown(p);
        if (!discovered && Seen(fog, p)) discovered = true;
        return discovered;
    }

    /// In somebody's sight right now: the centre, or one of four points on its rim (so a big rock shows up
    /// when its near side comes into view).
    bool Seen(FogOfWar fog, Vector3 p)
    {
        if (fog.IsVisible(p)) return true;
        if (radius < 0.4f) return false;
        return fog.IsVisible(p + new Vector3(radius, 0f, 0f)) || fog.IsVisible(p - new Vector3(radius, 0f, 0f))
            || fog.IsVisible(p + new Vector3(0f, 0f, radius)) || fog.IsVisible(p - new Vector3(0f, 0f, radius));
    }

    void Apply(bool show, bool instant)
    {
        bool wasShown = shown;
        shown = show;
        SetRenderers(show);
        if (!show || wasShown || instant || hide != Mode.UntilDiscovered) return;
        // discovered just now
        if (appearTime > 0f)
        {
            popTarget = appearTarget != null ? appearTarget : transform;
            fullScale = popTarget.localScale;
            popTarget.localScale = fullScale * 0.01f;
            appearT = 0f;
        }
        if (appearPuff)
            CombatFX.Hit(transform.position + Vector3.up * 0.3f, Vector3.up, new Color(0.8f, 0.62f, 0.45f), Mathf.Clamp(radius, 0.8f, 2f));
        Discovered?.Invoke();
    }

    /// The pop-in: grows from nothing with a little overshoot.
    void Pop()
    {
        appearT += Time.deltaTime / Mathf.Max(0.01f, appearTime);
        if (popTarget == null) { appearT = -1f; return; }
        if (appearT >= 1f)
        {
            popTarget.localScale = fullScale;
            appearT = -1f;
            return;
        }
        float g = appearT - 1f;
        float back = 1f + 2.70158f * g * g * g + 1.70158f * g * g;   // ease out, a bit past full size and back
        popTarget.localScale = fullScale * Mathf.Max(0.01f, back);
    }

    /// Hides without touching Renderer.enabled, so it never fights with other scripts that switch renderers.
    void SetRenderers(bool show)
    {
        GetComponentsInChildren(true, rendererBuf);
        for (int i = 0; i < rendererBuf.Count; i++) rendererBuf[i].forceRenderingOff = !show;
        rendererBuf.Clear();
        GetComponentsInChildren(true, canvasBuf);
        for (int i = 0; i < canvasBuf.Count; i++) canvasBuf[i].enabled = show;
        canvasBuf.Clear();
    }

    float MeasureRadius()
    {
        GetComponentsInChildren(true, rendererBuf);
        Vector3 p = transform.position;
        float r = 0f;
        for (int i = 0; i < rendererBuf.Count; i++)
        {
            if (rendererBuf[i] is ParticleSystemRenderer) continue;
            Bounds b = rendererBuf[i].bounds;
            r = Mathf.Max(r, Mathf.Abs(b.center.x - p.x) + b.extents.x);
            r = Mathf.Max(r, Mathf.Abs(b.center.z - p.z) + b.extents.z);
        }
        rendererBuf.Clear();
        return Mathf.Min(r, 6f);
    }
}
