using UnityEngine;

/// Pickup / drop zone circle drawn on the ground. Follows the terrain bumps.
/// Edit mode: the circle follows the object when you move it (or change the radius) - no need to press play.
/// Right-click the component > "Rebuild Ring" (or Tools > Trash Game > Refresh Ground Rings) to force a refresh.
[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public class GroundRing : MonoBehaviour
{
    public float radius = 3.5f;
    public int segments = 64;
    public float lift = 0.08f;
    public Color idleColor = new Color(1f, 1f, 1f, 0.55f);
    public Color activeColor = new Color(1f, 0.85f, 0.25f, 1f);
    public float idleWidth = 0.12f;
    public float activeWidth = 0.25f;

    [Header("Fog of war")]
    [Tooltip("As soon as any part of this zone is seen, the whole circle (and the object in it) comes out of the fog for good")]
    public bool revealFogWhenSeen = true;

    LineRenderer lr;
    bool highlighted;
    float k = -1f;
    // what the circle was last built for (edit mode: rebuild when any of it changes)
    Vector3 builtPos;
    float builtRadius = -1f;
    int builtSegments = -1;
    float builtLift = -1f;
    int builtGroundVersion = -1;
    bool fogRevealed;
    float fogCheckT;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        Rebuild();
    }

    [ContextMenu("Rebuild Ring")]
    public void Rebuild()
    {
        if (lr == null) lr = GetComponent<LineRenderer>();
        lr.loop = true;
        lr.useWorldSpace = true;
        lr.positionCount = segments;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        Vector3 c = transform.position;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            var p = c + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            p.y = GroundUtil.Height(p, c.y) + lift;
            lr.SetPosition(i, p);
        }
        builtPos = c; builtRadius = radius; builtSegments = segments; builtLift = lift;
        builtGroundVersion = GroundUtil.Version;
        Apply(Mathf.Max(k, 0f));
    }

    public void SetHighlight(bool on) => highlighted = on;

    void Update()
    {
        if (!Application.isPlaying)
        {
            // edit mode: follow the object / settings
            if (transform.position != builtPos || !Mathf.Approximately(radius, builtRadius) || segments != builtSegments ||
                !Mathf.Approximately(lift, builtLift) || GroundUtil.Version != builtGroundVersion)
                Rebuild();
            return;
        }

        if (GroundUtil.Version != builtGroundVersion) Rebuild();   // ground leveled / regenerated

        if (revealFogWhenSeen && !fogRevealed)
        {
            fogCheckT -= Time.deltaTime;
            if (fogCheckT <= 0f) { fogCheckT = 0.2f + Random.value * 0.1f; CheckFog(); }
        }

        float target = highlighted ? 1f : 0f;
        if (Mathf.Approximately(k, target)) return;
        k = Mathf.MoveTowards(Mathf.Max(k, 0f), target, Time.deltaTime * 6f);
        Apply(k);
    }

    // any part of the zone in someone's vision (centre or 8 points on the rim) -> the whole zone is explored
    void CheckFog()
    {
        var fog = FogOfWar.Instance;
        if (fog == null) return;
        Vector3 c = transform.position;
        bool seen = fog.IsVisible(c);
        for (int i = 0; i < 8 && !seen; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            seen = fog.IsVisible(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
        }
        if (!seen) return;
        fog.RevealExplored(c, radius + 1f);
        fogRevealed = true;
    }

    void Apply(float t)
    {
        float w = Mathf.Lerp(idleWidth, activeWidth, t);
        lr.startWidth = lr.endWidth = w;
        var col = Color.Lerp(idleColor, activeColor, t);
        lr.startColor = lr.endColor = col;
    }
}
