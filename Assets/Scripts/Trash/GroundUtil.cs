using UnityEngine;

/// Samples the LowPolyGround height, ignoring everything else in the scene.
public static class GroundUtil
{
    static Collider ground;

    public static float Height(Vector3 p, float fallback)
    {
        if (ground == null)
        {
            var g = Object.FindFirstObjectByType<LowPolyGround>();
            if (g != null) ground = g.GetComponent<Collider>();
        }
        if (ground != null && ground.Raycast(new Ray(new Vector3(p.x, 200f, p.z), Vector3.down), out var hit, 400f))
            return hit.point.y;
        return fallback;
    }
}
