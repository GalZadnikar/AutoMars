using UnityEngine;

/// Samples the LowPolyGround height, ignoring everything else in the scene.
public static class GroundUtil
{
    static Collider ground;

    /// Bumps when the ground mesh changes (building leveling) so height caches can reset.
    public static int Version { get; private set; }
    public static void MarkChanged() => Version++;

    static Collider Ground
    {
        get
        {
            if (ground == null)
            {
                var g = Object.FindFirstObjectByType<LowPolyGround>();
                if (g != null) ground = g.GetComponent<Collider>();
            }
            return ground;
        }
    }

    public static float Height(Vector3 p, float fallback)
    {
        var g = Ground;
        if (g != null && g.Raycast(new Ray(new Vector3(p.x, 200f, p.z), Vector3.down), out var hit, 400f))
            return hit.point.y;
        return fallback;
    }

    /// Where a ray (e.g. from the camera through the screen) hits the ground.
    public static bool Raycast(Ray ray, out Vector3 point)
    {
        var g = Ground;
        if (g != null && g.Raycast(ray, out var hit, 2000f)) { point = hit.point; return true; }
        var plane = new Plane(Vector3.up, Vector3.zero);   // flat fallback
        if (plane.Raycast(ray, out float d)) { point = ray.GetPoint(d); return true; }
        point = default;
        return false;
    }
}
