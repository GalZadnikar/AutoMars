using System;
using System.Collections.Generic;
using UnityEngine;

/// BIOME PROTOTYPE - paints one biome onto an area of the ground.
/// Shapes: Circle, Box (rounded), Path (a line with a width) or Polygon (closed outline).
/// Move / rotate the object to move the zone; Path and Polygon points have handles in the Scene view.
/// Zones with a higher Order are drawn on top of lower ones.
[ExecuteAlways]
public class BiomeZone : MonoBehaviour
{
    public static readonly List<BiomeZone> All = new List<BiomeZone>();
    /// Something about the zones changed: BiomeMap re-bakes.
    public static event Action Changed;
    public static void NotifyChanged()
    {
        Changed?.Invoke();
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    public enum Shape { Circle, Box, Path, Polygon }

    public BiomeDef biome;
    public Shape shape = Shape.Circle;
    [Tooltip("Higher = drawn on top")]
    public int order = 0;
    [Range(0f, 1f)] public float strength = 1f;

    [Header("Shape")]
    public float radius = 6f;
    public Vector2 boxSize = new Vector2(10f, 10f);
    public float boxCornerRadius = 2f;
    public float pathWidth = 3f;
    [Tooltip("Path / Polygon points, relative to this object (drag them in the Scene view)")]
    public List<Vector3> points = new List<Vector3> { new Vector3(-4f, 0f, -2f), new Vector3(0f, 0f, 3f), new Vector3(4f, 0f, -2f) };

    [Header("Edge")]
    [Tooltip("Width of the blend at the border (m)")]
    public float softness = 1f;
    [Tooltip("How far the border wobbles in and out (m)")]
    public float wobble = 1.2f;
    [Tooltip("Size of the wobbles: smaller = longer, lazier curves")]
    public float wobbleScale = 0.15f;
    public int seed = 0;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); NotifyChanged(); }
    void OnDisable() { All.Remove(this); NotifyChanged(); }
    void OnValidate() => NotifyChanged();

    void Update()
    {
        if (!Application.isPlaying && transform.hasChanged)
        {
            transform.hasChanged = false;
            NotifyChanged();
        }
    }

    // ------------------------------------------------------------------ shape maths (XZ plane)

    Vector2 ToLocal(float wx, float wz)
    {
        Vector3 d = new Vector3(wx, 0f, wz) - transform.position;
        d = Quaternion.Euler(0f, -transform.eulerAngles.y, 0f) * d;
        return new Vector2(d.x, d.z);
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return (p - (a + ab * t)).magnitude;
    }

    /// Distance to the shape's border in metres: negative inside, positive outside.
    public float SignedDistance(float wx, float wz)
    {
        Vector2 p = ToLocal(wx, wz);
        switch (shape)
        {
            case Shape.Circle:
                return p.magnitude - radius;
            case Shape.Box:
            {
                float r = Mathf.Clamp(boxCornerRadius, 0f, Mathf.Min(boxSize.x, boxSize.y) * 0.5f);
                Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (boxSize * 0.5f - Vector2.one * r);
                return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
            }
            case Shape.Path:
            {
                if (points.Count == 0) return float.MaxValue;
                float d = float.MaxValue;
                if (points.Count == 1) d = (p - new Vector2(points[0].x, points[0].z)).magnitude;
                for (int i = 0; i + 1 < points.Count; i++)
                    d = Mathf.Min(d, SegmentDistance(p, new Vector2(points[i].x, points[i].z), new Vector2(points[i + 1].x, points[i + 1].z)));
                return d - pathWidth * 0.5f;
            }
            default: // Polygon
            {
                int n = points.Count;
                if (n < 3) return float.MaxValue;
                float d = float.MaxValue;
                bool inside = false;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    Vector2 a = new Vector2(points[i].x, points[i].z), b = new Vector2(points[j].x, points[j].z);
                    d = Mathf.Min(d, SegmentDistance(p, a, b));
                    if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                }
                return inside ? -d : d;
            }
        }
    }

    /// How much of this zone's biome is at a world point (0..1), wobbly soft border included.
    public float Coverage(float wx, float wz)
    {
        float sd = SignedDistance(wx, wz);
        if (wobble > 0f)
            sd += (Mathf.PerlinNoise(wx * wobbleScale + seed * 13.7f + 100f, wz * wobbleScale + seed * 7.3f + 100f) - 0.5f) * 2f * wobble;
        float soft = Mathf.Max(0.05f, softness);
        float t = Mathf.Clamp01((sd + soft * 0.5f) / soft);
        return strength * (1f - t * t * (3f - 2f * t));
    }

    /// World XZ rectangle that can be affected (for fast baking).
    public Rect WorldBounds()
    {
        float r;
        switch (shape)
        {
            case Shape.Circle: r = radius; break;
            case Shape.Box: r = boxSize.magnitude * 0.5f; break;
            default:
                r = 0f;
                foreach (var pt in points) r = Mathf.Max(r, new Vector2(pt.x, pt.z).magnitude);
                if (shape == Shape.Path) r += pathWidth * 0.5f;
                break;
        }
        r += softness + wobble + 0.5f;
        Vector3 c = transform.position;
        return new Rect(c.x - r, c.z - r, r * 2f, r * 2f);
    }
}
