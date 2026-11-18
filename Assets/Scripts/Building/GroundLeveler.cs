using System.Collections.Generic;
using UnityEngine;

/// Flattens the low-poly ground under a new building (Age of Empires II style):
/// the footprint becomes one flat pad at the average height, a soft border blends it back into the slope.
/// Rocks inside the area move with the ground; ground under other buildings is never touched.
public static class GroundLeveler
{
    // every pad leveled this session; re-applied if the ground mesh gets regenerated
    static readonly List<Pad> applied = new List<Pad>();
    static readonly List<GridFootprint> preplaced = new List<GridFootprint>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        applied.Clear();
        preplaced.Clear();
        LowPolyGround.Rebuilt -= Reapply;
    }

    static void Reapply()
    {
        if (!Application.isPlaying || applied.Count == 0) return;
        Apply(applied, 2.5f);
        foreach (var fp in preplaced)
            if (fp != null) foreach (var ring in fp.GetComponentsInChildren<GroundRing>(true)) ring.Rebuild();
    }

    /// Height the pad will get = average ground height over the footprint.
    public static float PadHeight(Vector3 center, Vector2 half)
    {
        float sum = 0f;
        int n = 0;
        for (int i = 0; i <= 4; i++)
            for (int j = 0; j <= 4; j++)
            {
                var p = new Vector3(center.x + Mathf.Lerp(-half.x, half.x, i / 4f), 0f, center.z + Mathf.Lerp(-half.y, half.y, j / 4f));
                sum += GroundUtil.Height(p, center.y);
                n++;
            }
        return sum / n;
    }

    /// Flattens ground inside the rect (centre ± half, world X/Z) to its average height. Returns that height.
    /// ignore = footprint of the thing being built (its cells may already be registered).
    public static float Flatten(Vector3 center, Vector2 half, float margin, GridFootprint ignore = null)
    {
        var pads = new List<Pad> { new Pad { center = center, half = half, owner = ignore, target = PadHeight(center, half) } };
        FlattenMany(pads, margin);
        return pads[0].target;
    }

    public class Pad
    {
        public Vector3 center;
        public Vector2 half;
        public GridFootprint owner;   // its own cells may be leveled
        public float target;
    }

    /// One mesh pass + one collider re-bake for several pads (targets must be filled in).
    public static void FlattenMany(List<Pad> pads, float margin)
    {
        if (pads == null || pads.Count == 0) return;
        applied.AddRange(pads);
        Apply(pads, margin);
    }

    static void Apply(List<Pad> pads, float margin)
    {
        var ground = Object.FindFirstObjectByType<LowPolyGround>();
        if (ground == null) return;
        var mf = ground.GetComponent<MeshFilter>();
        var mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null) return;
        var tr = ground.transform;

        var verts = mesh.vertices;
        var groundTris = mesh.GetTriangles(0);
        var rockTris = mesh.subMeshCount > 1 ? mesh.GetTriangles(1) : new int[0];
        float reach = margin + 0.01f;

        // ground triangles near the area (old heights) – used to find the ground under rock vertices
        var nearTris = new List<int>();
        for (int t = 0; t < groundTris.Length; t += 3)
        {
            var a = tr.TransformPoint(verts[groundTris[t]]);
            bool close = false;
            foreach (var pd in pads)
                if (Mathf.Abs(a.x - pd.center.x) <= pd.half.x + reach + 2f && Mathf.Abs(a.z - pd.center.z) <= pd.half.y + reach + 2f) { close = true; break; }
            if (!close) continue;
            nearTris.Add(t);
        }

        var isRock = new HashSet<int>();
        foreach (var i in rockTris) isRock.Add(i);

        var newVerts = (Vector3[])verts.Clone();
        for (int i = 0; i < verts.Length; i++)
        {
            var w = tr.TransformPoint(verts[i]);
            Pad pad = null;
            float d = float.MaxValue;
            foreach (var pd in pads)
            {
                float dx = Mathf.Max(Mathf.Abs(w.x - pd.center.x) - pd.half.x, 0f);
                float dz = Mathf.Max(Mathf.Abs(w.z - pd.center.z) - pd.half.y, 0f);
                float dd = Mathf.Sqrt(dx * dx + dz * dz);
                if (dd < d) { d = dd; pad = pd; }
            }
            if (pad == null || d >= reach) continue;
            float target = pad.target;

            // keep the ground under other buildings as it is
            if (d > 0f)
            {
                var occ = BuildGrid.OccupantOf(BuildGrid.WorldToCell(w));
                if (occ != null && occ != pad.owner) continue;
            }

            float oldGround = isRock.Contains(i) ? GroundBelow(w, verts, groundTris, nearTris, tr) : w.y;
            float k = d <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, d / margin);
            float newGround = Mathf.Lerp(target, oldGround, k);
            w.y += newGround - oldGround;
            newVerts[i] = tr.InverseTransformPoint(w);
        }

        mesh.vertices = newVerts;
        mesh.RecalculateNormals();   // unshared verts => stays flat-shaded
        mesh.RecalculateBounds();
        var col = ground.GetComponent<MeshCollider>();
        if (col != null) { col.sharedMesh = null; col.sharedMesh = mesh; }
        GroundUtil.MarkChanged();
    }

    /// At scene start: level the ground under every hand-placed building, set it on its pad, redraw its ring.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void LevelPreplaced()
    {
        var pads = new List<Pad>();
        var owners = new List<GridFootprint>();
        foreach (var fp in Object.FindObjectsByType<GridFootprint>(FindObjectsSortMode.None))
        {
            if (!fp.levelGround) continue;
            var dims = BuildGrid.Dims(fp.size, fp.QuarterTurns);
            var half = new Vector2(dims.x, dims.y) * BuildGrid.CellSize * 0.5f;
            var c = fp.Center;
            pads.Add(new Pad { center = c, half = half, owner = fp, target = PadHeight(c, half) });
            owners.Add(fp);
        }
        if (pads.Count == 0) return;
        FlattenMany(pads, 2.5f);
        preplaced.AddRange(owners);
        LowPolyGround.Rebuilt -= Reapply;
        LowPolyGround.Rebuilt += Reapply;   // the Editor regenerates the ground right after loading
        for (int i = 0; i < owners.Count; i++)
        {
            var t = owners[i].transform;
            t.position = new Vector3(t.position.x, pads[i].target, t.position.z);
            foreach (var ring in owners[i].GetComponentsInChildren<GroundRing>(true)) ring.Rebuild();
        }
    }

    // old ground height under a point, from the original ground triangles
    static float GroundBelow(Vector3 w, Vector3[] verts, int[] tris, List<int> near, Transform tr)
    {
        foreach (var t in near)
        {
            var a = tr.TransformPoint(verts[tris[t]]);
            var b = tr.TransformPoint(verts[tris[t + 1]]);
            var c = tr.TransformPoint(verts[tris[t + 2]]);
            if (Barycentric(w, a, b, c, out float u, out float v, out float s)) return a.y * u + b.y * v + c.y * s;
        }
        return w.y;
    }

    static bool Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float u, out float v, out float w)
    {
        float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
        u = v = w = 0f;
        if (Mathf.Abs(d) < 1e-6f) return false;
        u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d;
        v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d;
        w = 1f - u - v;
        const float e = -0.001f;
        return u >= e && v >= e && w >= e;
    }
}
