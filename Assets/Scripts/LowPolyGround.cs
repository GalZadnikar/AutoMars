using System.Collections.Generic;
using UnityEngine;

/// Generates a flat-shaded low-poly ground with scattered rocks.
/// Submesh 0 = ground, submesh 1 = rocks (assign two materials).
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LowPolyGround : MonoBehaviour
{
    [Header("Ground")]
    public int cells = 40;
    public float size = 40f;
    [Range(0f, 0.5f)] public float jitter = 0.38f;
    public float height = 1.2f;
    public float noiseScale = 0.08f;
    [Tooltip("Small extra per-vertex bumpiness")]
    public float roughness = 0.15f;

    [Header("Rocks")]
    public int rockCount = 60;
    public Vector2 rockSize = new Vector2(0.15f, 0.6f);

    public int seed = 1;
    public bool addCollider = true;

    Mesh mesh;

    void OnEnable() => Build();

#if UNITY_EDITOR
    void OnValidate() => UnityEditor.EditorApplication.delayCall += () => { if (this) Build(); };
#endif

    float Height(float x, float z) =>
        Mathf.PerlinNoise(x * noiseScale + seed * 13.1f, z * noiseScale + seed * 7.7f) * height;

    [ContextMenu("Build")]
    public void Build()
    {
        var rnd = new System.Random(seed);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        cells = Mathf.Max(1, cells);
        int n = cells + 1; float step = size / cells;
        var g = new Vector3[n, n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            bool edge = x == 0 || z == 0 || x == cells || z == cells;
            float px = x * step - size / 2 + (edge ? 0 : R(-jitter, jitter) * step);
            float pz = z * step - size / 2 + (edge ? 0 : R(-jitter, jitter) * step);
            g[x, z] = new Vector3(px, Height(px, pz) + R(-roughness, roughness), pz);
        }

        var v = new List<Vector3>();
        var ground = new List<int>();
        var rocks = new List<int>();
        void Tri(List<int> t, Vector3 a, Vector3 b, Vector3 c)
        { t.Add(v.Count); v.Add(a); t.Add(v.Count); v.Add(b); t.Add(v.Count); v.Add(c); }

        for (int z = 0; z < cells; z++)
        for (int x = 0; x < cells; x++)
        {
            Vector3 a = g[x, z], b = g[x, z + 1], c = g[x + 1, z + 1], d = g[x + 1, z];
            if ((x + z) % 2 == 0) { Tri(ground, a, b, c); Tri(ground, a, c, d); }
            else                  { Tri(ground, a, b, d); Tri(ground, b, c, d); }
        }

        // Rocks: jittered low-poly lumps (ring of 5-6 + peak), half-sunk into ground
        for (int i = 0; i < rockCount; i++)
        {
            float px = R(-size / 2 + 1, size / 2 - 1), pz = R(-size / 2 + 1, size / 2 - 1);
            float s = Mathf.Lerp(rockSize.x, rockSize.y, Mathf.Pow((float)rnd.NextDouble(), 2.5f));
            var center = new Vector3(px, Height(px, pz) - s * 0.35f, pz);
            int k = rnd.Next(5, 7);
            float rot = R(0, Mathf.PI * 2);
            var ring = new Vector3[k];
            var mid = new Vector3[k];
            for (int j = 0; j < k; j++)
            {
                float ang = rot + j * Mathf.PI * 2 / k + R(-0.3f, 0.3f);
                float r = s * R(0.7f, 1.1f);
                ring[j] = center + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
                mid[j] = center + new Vector3(Mathf.Cos(ang) * r * 0.7f, s * R(0.5f, 0.8f), Mathf.Sin(ang) * r * 0.7f);
            }
            var top = center + new Vector3(R(-0.3f, 0.3f) * s, s * R(0.9f, 1.4f), R(-0.3f, 0.3f) * s);
            for (int j = 0; j < k; j++)
            {
                int nj = (j + 1) % k;
                Tri(rocks, ring[j], mid[j], mid[nj]);
                Tri(rocks, ring[j], mid[nj], ring[nj]);
                Tri(rocks, mid[j], top, mid[nj]);
            }
        }

        if (mesh == null) { mesh = new Mesh { name = "LowPolyGround" }; }
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(v);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(ground, 0);
        mesh.SetTriangles(rocks, 1);
        mesh.RecalculateNormals();   // unshared verts => flat shading
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().sharedMesh = mesh;

        if (addCollider)
        {
            var col = GetComponent<MeshCollider>();
            if (col == null) col = gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = null; col.sharedMesh = mesh;
        }
    }
}
