using System.Collections.Generic;
using UnityEngine;

/// Grid tiles drawn on the ground around the building being placed.
/// Footprint tiles: green = OK, red = blocked. Around it: faint white = free, faint red = taken.
/// One mesh, rebuilt only when the footprint moves or rotates. Vertices follow the terrain.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GridOverlay : MonoBehaviour
{
    public int radius = 6;
    public float lift = 0.12f;
    public Color okColor = new Color(0.35f, 1f, 0.45f, 0.85f);
    public Color badColor = new Color(1f, 0.28f, 0.22f, 0.9f);
    public Color freeColor = new Color(1f, 1f, 1f, 0.28f);
    public Color takenColor = new Color(1f, 0.35f, 0.3f, 0.35f);

    Mesh mesh;
    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<Color> cols = new List<Color>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<int> tris = new List<int>();
    readonly Dictionary<Vector2Int, float> heightCache = new Dictionary<Vector2Int, float>();   // grid corners
    int cacheVersion = -1;

    void Awake()
    {
        mesh = new Mesh { name = "BuildGridOverlay" };
        mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    /// footprintStates: state of each footprint cell (same order as cells inside bl..bl+dims).
    public void Show(Vector2Int bl, Vector2Int dims, Dictionary<Vector2Int, BuildGrid.CellState> footprintStates)
    {
        gameObject.SetActive(true);
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        if (cacheVersion != GroundUtil.Version) { heightCache.Clear(); cacheVersion = GroundUtil.Version; }   // ground was leveled
        var fog = FogOfWar.Instance;
        verts.Clear(); cols.Clear(); uvs.Clear(); tris.Clear();

        Vector2 center = new Vector2(bl.x + dims.x * 0.5f, bl.y + dims.y * 0.5f);
        float maxD = radius + Mathf.Max(dims.x, dims.y) * 0.5f;
        for (int x = bl.x - radius; x < bl.x + dims.x + radius; x++)
            for (int z = bl.y - radius; z < bl.y + dims.y + radius; z++)
            {
                var c = new Vector2Int(x, z);
                if (!BuildGrid.InBounds(c)) continue;
                Color col;
                if (footprintStates.TryGetValue(c, out var st)) col = st == BuildGrid.CellState.Free ? okColor : badColor;
                else
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, z + 0.5f), center);
                    float fade = Mathf.Clamp01(1f - d / maxD);
                    if (fade <= 0.02f) continue;
                    // only draw where you could build: no grid in the fog
                    if (fog != null && fog.isActiveAndEnabled && !fog.IsExplored(BuildGrid.CellCenter(c))) continue;
                    col = BuildGrid.IsOccupied(c) ? takenColor : freeColor;
                    col.a *= fade;
                }
                AddCell(c, col);
            }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }

    public void Hide() => gameObject.SetActive(false);

    void AddCell(Vector2Int c, Color col)
    {
        int i = verts.Count;
        verts.Add(Corner(c.x, c.y));
        verts.Add(Corner(c.x, c.y + 1));
        verts.Add(Corner(c.x + 1, c.y + 1));
        verts.Add(Corner(c.x + 1, c.y));
        uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
        for (int k = 0; k < 4; k++) cols.Add(col);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
    }

    Vector3 Corner(int gx, int gz)
    {
        float cs = BuildGrid.CellSize;
        var p = new Vector3(BuildGrid.Origin.x + gx * cs, 0f, BuildGrid.Origin.y + gz * cs);
        var key = new Vector2Int(gx, gz);
        if (!heightCache.TryGetValue(key, out float h))
        {
            h = GroundUtil.Height(p, 0f);
            heightCache[key] = h;
        }
        p.y = h + lift;
        return p;
    }
}
