using System;
using UnityEngine;

/// Age-of-Empires style fog of war.
///
/// Keeps a small grid over the level (one texel per cell):
///   R = visible right now (rebuilt every refresh)
///   G = explored (once seen, stays seen)
/// The grid is uploaded as a global texture and a full-screen pass (FogOfWar shader,
/// added to the URP renderer as a Full Screen Pass Renderer Feature) darkens the scene
/// based on it. Bilinear filtering on the small texture gives soft round edges for free.
///
/// Mobile-friendly: tiny texture, updated a few times per second, and we blend between
/// the previous and the current texture in the shader so movement still looks smooth.
[DefaultExecutionOrder(-50)]
public class FogOfWar : MonoBehaviour
{
    public static FogOfWar Instance { get; private set; }

    [Header("Area covered by the fog (X/Z)")]
    public Vector2 worldCenter = Vector2.zero;
    public Vector2 worldSize = new Vector2(80f, 80f);
    [Tooltip("Grid cells per side. 128 on an 80m map = ~0.6m per cell.")]
    [Range(32, 512)] public int resolution = 128;

    [Header("Updating")]
    [Tooltip("Seconds between fog recalculations. The shader blends in between.")]
    public float updateInterval = 0.12f;
    [Tooltip("Width of the soft edge at the border of a vision circle (world units).")]
    public float edgeSoftness = 2.5f;

    [Header("Look")]
    [Tooltip("Colour of never-explored areas.")]
    public Color fogColor = new Color(0.015f, 0.012f, 0.02f, 1f);
    [Tooltip("Brightness of explored areas that nobody is looking at right now.")]
    [Range(0f, 1f)] public float exploredBrightness = 0.42f;
    [Tooltip("Overlay colour on the minimap for unexplored areas.")]
    public Color minimapFogColor = new Color(0f, 0f, 0f, 0.92f);

    [Header("Debug")]
    public bool revealAll;

    public Texture2D MinimapTexture => minimapTex;
    public Vector2 Origin => worldCenter - worldSize * 0.5f;

    /// Raised after explored cells changed (e.g. for saving or UI).
    public event Action ExploredChanged;

    Texture2D curTex, prevTex, minimapTex;
    Color32[] curPx, prevPx, minimapPx;
    byte[] visible, explored;
    float timer, blend;
    bool exploredDirty, minimapDirty;
    float nextMinimapUpdate;
    int res;

    static readonly int FogTexId = Shader.PropertyToID("_FogTex");
    static readonly int FogPrevTexId = Shader.PropertyToID("_FogPrevTex");
    static readonly int FogParamsId = Shader.PropertyToID("_FogParams");
    static readonly int FogColorId = Shader.PropertyToID("_FogColor");
    static readonly int FogLookId = Shader.PropertyToID("_FogLook");
    static readonly int FogEnabledId = Shader.PropertyToID("_FogEnabled");

    // ------------------------------------------------------------------ lifecycle

    void OnEnable()
    {
        Instance = this;
        Allocate();
        Refresh();          // fill current
        Refresh();          // and previous, so we don't fade in from black
        Shader.SetGlobalFloat(FogEnabledId, 1f);
    }

    // revealers register in their own OnEnable, which can run after ours: refresh again
    // once everything is awake so the first frame already shows the correct fog
    void Start()
    {
        Refresh();
        Refresh();
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        Shader.SetGlobalFloat(FogEnabledId, 0f);
    }

    void OnDestroy()
    {
        if (curTex) Destroy(curTex);
        if (prevTex) Destroy(prevTex);
        if (minimapTex) Destroy(minimapTex);
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= updateInterval)
        {
            timer = 0f;
            Refresh();
        }

        blend = updateInterval > 0f ? Mathf.Clamp01(timer / updateInterval) : 1f;
        PushGlobals();

        if (minimapDirty && Time.unscaledTime >= nextMinimapUpdate)
        {
            nextMinimapUpdate = Time.unscaledTime + 0.25f;
            UpdateMinimapTexture();
        }

        if (exploredDirty)
        {
            exploredDirty = false;
            ExploredChanged?.Invoke();
        }
    }

    // ------------------------------------------------------------------ core

    void Allocate()
    {
        res = Mathf.Clamp(resolution, 8, 1024);
        int n = res * res;
        visible = new byte[n];
        if (explored == null || explored.Length != n) explored = new byte[n];
        curPx = new Color32[n];
        prevPx = new Color32[n];
        minimapPx = new Color32[n];
        curTex = NewTex("FogCur");
        prevTex = NewTex("FogPrev");
        minimapTex = NewTex("FogMinimap");
        minimapDirty = true;
        UpdateMinimapTexture();
    }

    Texture2D NewTex(string name)
    {
        return new Texture2D(res, res, TextureFormat.RGBA32, false, true)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
    }

    /// Recomputes the visibility grid from all revealers and uploads it.
    public void Refresh()
    {
        if (visible == null) return;

        if (revealAll)
        {
            for (int i = 0; i < visible.Length; i++) visible[i] = 255;
        }
        else
        {
            Array.Clear(visible, 0, visible.Length);
            var list = FogRevealer.All;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r != null && r.isActiveAndEnabled) Stamp(r.transform.position, r.radius);
            }
        }

        // the old "current" becomes "previous"; we write the new state into the other one
        (curTex, prevTex) = (prevTex, curTex);
        (curPx, prevPx) = (prevPx, curPx);

        for (int i = 0; i < visible.Length; i++)
        {
            byte v = visible[i];
            if (v > explored[i]) { explored[i] = v; exploredDirty = true; minimapDirty = true; }
            curPx[i] = new Color32(v, explored[i], 0, 255);
        }
        curTex.SetPixels32(curPx);
        curTex.Apply(false, false);

        timer = 0f;
        blend = 0f;
        PushGlobals();
    }

    void Stamp(Vector3 pos, float radius)
    {
        if (radius <= 0f) return;
        Vector2 o = Origin;
        float cellX = worldSize.x / res, cellZ = worldSize.y / res;
        float soft = Mathf.Max(0.01f, edgeSoftness);

        int x0 = Mathf.Max(0, Mathf.FloorToInt((pos.x - radius - o.x) / cellX));
        int x1 = Mathf.Min(res - 1, Mathf.CeilToInt((pos.x + radius - o.x) / cellX));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((pos.z - radius - o.y) / cellZ));
        int z1 = Mathf.Min(res - 1, Mathf.CeilToInt((pos.z + radius - o.y) / cellZ));
        float r2 = radius * radius;

        for (int z = z0; z <= z1; z++)
        {
            float dz = o.y + (z + 0.5f) * cellZ - pos.z;
            int row = z * res;
            for (int x = x0; x <= x1; x++)
            {
                float dx = o.x + (x + 0.5f) * cellX - pos.x;
                float d2 = dx * dx + dz * dz;
                if (d2 >= r2) continue;
                float t = (radius - Mathf.Sqrt(d2)) / soft;          // 0 at the rim, 1 inside
                byte b = t >= 1f ? (byte)255 : (byte)(t * t * (3f - 2f * t) * 255f);
                int i = row + x;
                if (b > visible[i]) visible[i] = b;
            }
        }
    }

    void PushGlobals()
    {
        Vector2 o = Origin;
        Shader.SetGlobalTexture(FogTexId, curTex);
        Shader.SetGlobalTexture(FogPrevTexId, prevTex);
        Shader.SetGlobalVector(FogParamsId, new Vector4(o.x, o.y, 1f / Mathf.Max(0.01f, worldSize.x), 1f / Mathf.Max(0.01f, worldSize.y)));
        Shader.SetGlobalColor(FogColorId, fogColor);
        Shader.SetGlobalVector(FogLookId, new Vector4(exploredBrightness, blend, 0f, 0f));
        Shader.SetGlobalFloat(FogEnabledId, 1f);
    }

    void UpdateMinimapTexture()
    {
        if (minimapTex == null) return;
        minimapDirty = false;
        Color32 c = minimapFogColor;
        for (int i = 0; i < explored.Length; i++)
        {
            byte a = (byte)(c.a * (255 - explored[i]) / 255);
            minimapPx[i] = new Color32(c.r, c.g, c.b, a);
        }
        minimapTex.SetPixels32(minimapPx);
        minimapTex.Apply(false, false);
    }

    // ------------------------------------------------------------------ queries

    int CellIndex(Vector3 p)
    {
        Vector2 o = Origin;
        int x = Mathf.FloorToInt((p.x - o.x) / worldSize.x * res);
        int z = Mathf.FloorToInt((p.z - o.y) / worldSize.y * res);
        if (x < 0 || z < 0 || x >= res || z >= res) return -1;
        return z * res + x;
    }

    /// Is this point inside someone's vision right now?
    public bool IsVisible(Vector3 worldPos)
    {
        if (revealAll) return true;
        int i = CellIndex(worldPos);
        return i >= 0 && visible[i] > 127;
    }

    /// Has this point ever been seen?
    public bool IsExplored(Vector3 worldPos)
    {
        if (revealAll) return true;
        int i = CellIndex(worldPos);
        return i >= 0 && explored[i] > 127;
    }

    /// 0..1 how much of the map has been explored.
    public float ExploredFraction()
    {
        int n = 0;
        for (int i = 0; i < explored.Length; i++) if (explored[i] > 127) n++;
        return (float)n / explored.Length;
    }

    // ------------------------------------------------------------------ save / load

    /// Copy of the explored grid, e.g. to store in a save file (Convert.ToBase64String).
    public byte[] GetExploredData() => (byte[])explored.Clone();

    public void SetExploredData(byte[] data)
    {
        if (data == null || data.Length != explored.Length) return;
        Buffer.BlockCopy(data, 0, explored, 0, data.Length);
        minimapDirty = true;
        Refresh();
        Refresh();
    }

    [ContextMenu("Reset Explored")]
    public void ResetExplored()
    {
        Array.Clear(explored, 0, explored.Length);
        minimapDirty = true;
        Refresh();
        Refresh();
    }
}
