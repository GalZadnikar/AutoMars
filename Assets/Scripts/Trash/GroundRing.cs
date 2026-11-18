using UnityEngine;

/// Pickup / drop zone circle drawn on the ground. Follows the terrain bumps.
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

    LineRenderer lr;
    bool highlighted;
    float k = -1f;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        Rebuild();
    }

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
        Apply(0f);
    }

    public void SetHighlight(bool on) => highlighted = on;

    void Update()
    {
        float target = highlighted ? 1f : 0f;
        if (Mathf.Approximately(k, target)) return;
        k = Mathf.MoveTowards(Mathf.Max(k, 0f), target, Time.deltaTime * 6f);
        Apply(k);
    }

    void Apply(float t)
    {
        float w = Mathf.Lerp(idleWidth, activeWidth, t);
        lr.startWidth = lr.endWidth = w;
        var col = Color.Lerp(idleColor, activeColor, t);
        lr.startColor = lr.endColor = col;
    }
}
