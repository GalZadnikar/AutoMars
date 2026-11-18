using UnityEngine;

/// Hides this object's renderers depending on the fog.
///  - WhenNotVisible: like enemy units in RTS games, only shown while inside your vision.
///  - WhenUnexplored: like resources/buildings, shown once the area was discovered.
public class FogHideable : MonoBehaviour
{
    public enum Mode { WhenNotVisible, WhenUnexplored }

    public Mode hide = Mode.WhenNotVisible;
    [Tooltip("Seconds between checks.")]
    public float checkInterval = 0.2f;

    Renderer[] renderers;
    Canvas[] canvases;
    bool shown = true;
    float next;

    public bool IsShown => shown;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        canvases = GetComponentsInChildren<Canvas>(true);
    }

    void OnEnable() => next = Random.value * checkInterval; // spread checks over frames

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + checkInterval;

        var fog = FogOfWar.Instance;
        bool show = fog == null || (hide == Mode.WhenNotVisible
            ? fog.IsVisible(transform.position)
            : fog.IsExplored(transform.position));
        if (show == shown) return;

        shown = show;
        foreach (var r in renderers) if (r != null) r.enabled = show;
        foreach (var c in canvases) if (c != null) c.enabled = show;
    }
}
