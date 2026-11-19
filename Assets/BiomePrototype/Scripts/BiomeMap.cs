using System.Collections.Generic;
using UnityEngine;

/// BIOME PROTOTYPE - bakes all BiomeZones into a small mask texture over the map and puts the biome
/// materials on LowPolyGround while this component is on. Turn it off (or delete its object) and the
/// ground gets its original materials back.
///
/// Up to 4 biomes at once: the Base Biome (everywhere no zone covers) + 3 more used by zones.
/// Zones are layered by Order: higher orders cover lower ones.
[ExecuteAlways]
[DefaultExecutionOrder(-100)]
public class BiomeMap : MonoBehaviour
{
    public static BiomeMap Instance { get; private set; }

    [Tooltip("The biome everywhere no zone covers")]
    public BiomeDef baseBiome;
    public Material groundMaterial;
    public Material rockMaterial;
    [Tooltip("Mask pixels per side over the whole ground. 256 on an 80 m map = ~0.3 m per pixel")]
    [Range(64, 1024)] public int resolution = 256;
    [Tooltip("How soft the biome borders look (lower = crisper)")]
    [Range(0.01f, 0.5f)] public float edgeSoftness = 0.12f;
    [Tooltip("Found automatically")]
    public LowPolyGround ground;

    [SerializeField, HideInInspector] Material[] originalMaterials;

    Texture2D mask;
    float[] weights;   // 4 per pixel
    Vector2 areaOrigin;
    float areaSize;
    int res;
    bool dirty = true;
    readonly List<BiomeDef> slots = new List<BiomeDef>();

    static readonly int MaskId = Shader.PropertyToID("_BiomeMask");
    static readonly int AreaId = Shader.PropertyToID("_BiomeArea");
    static readonly int EdgeId = Shader.PropertyToID("_EdgeSoftness");
    static readonly int RockModeId = Shader.PropertyToID("_RockMode");
    static readonly int[,] ColourIds =
    {
        { Shader.PropertyToID("_B0A"), Shader.PropertyToID("_B0B"), Shader.PropertyToID("_B0R") },
        { Shader.PropertyToID("_B1A"), Shader.PropertyToID("_B1B"), Shader.PropertyToID("_B1R") },
        { Shader.PropertyToID("_B2A"), Shader.PropertyToID("_B2B"), Shader.PropertyToID("_B2R") },
        { Shader.PropertyToID("_B3A"), Shader.PropertyToID("_B3B"), Shader.PropertyToID("_B3R") },
    };

    // ------------------------------------------------------------------ lifecycle

    void OnEnable()
    {
        Instance = this;
        if (ground == null) ground = FindFirstObjectByType<LowPolyGround>();
        BiomeZone.Changed += MarkDirty;
        ApplyMaterials();
        dirty = true;
        Bake();
    }

    void OnDisable()
    {
        BiomeZone.Changed -= MarkDirty;
        RestoreMaterials();
        if (mask != null) { DestroyImmediate(mask); mask = null; }
        if (Instance == this) Instance = null;
    }

    void OnValidate() => MarkDirty();

    void MarkDirty()
    {
        dirty = true;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    void Update()
    {
        if (dirty) Bake();
    }

    // ------------------------------------------------------------------ materials

    void ApplyMaterials()
    {
        if (ground == null || groundMaterial == null) return;
        var r = ground.GetComponent<MeshRenderer>();
        var cur = r.sharedMaterials;
        if (cur.Length > 0 && cur[0] != groundMaterial)
        {
            originalMaterials = cur;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        var mats = new Material[Mathf.Max(1, cur.Length)];
        mats[0] = groundMaterial;
        for (int i = 1; i < mats.Length; i++) mats[i] = rockMaterial != null ? rockMaterial : cur[i];
        r.sharedMaterials = mats;
    }

    void RestoreMaterials()
    {
        if (ground == null || originalMaterials == null || originalMaterials.Length == 0) return;
        var r = ground.GetComponent<MeshRenderer>();
        if (r != null) r.sharedMaterials = originalMaterials;
    }

    // ------------------------------------------------------------------ baking

    [ContextMenu("Bake Now")]
    public void Bake()
    {
        dirty = false;
        if (ground == null || groundMaterial == null) return;

        // which biomes are in use (base + up to 3 more)
        slots.Clear();
        slots.Add(baseBiome);
        var zones = new List<BiomeZone>(BiomeZone.All);
        zones.RemoveAll(z => z == null || z.biome == null || !z.isActiveAndEnabled);
        zones.Sort((a, b) => a.order.CompareTo(b.order));
        foreach (var z in zones)
        {
            if (slots.Contains(z.biome)) continue;
            if (slots.Count < 4) slots.Add(z.biome);
            else Debug.LogWarning("BiomeMap: more than 4 biomes in use, '" + z.biome.name + "' is skipped (zone " + z.name + ")", z);
        }

        // area = the whole ground
        res = Mathf.Clamp(resolution, 16, 2048);
        areaSize = Mathf.Max(1f, ground.size);
        areaOrigin = new Vector2(ground.transform.position.x - areaSize * 0.5f, ground.transform.position.z - areaSize * 0.5f);
        float px = areaSize / res;

        int n = res * res;
        if (weights == null || weights.Length != n * 4) weights = new float[n * 4];
        for (int i = 0; i < n; i++) { int k = i * 4; weights[k] = 1f; weights[k + 1] = weights[k + 2] = weights[k + 3] = 0f; }

        foreach (var z in zones)
        {
            int slot = slots.IndexOf(z.biome);
            if (slot < 0) continue;
            Rect b = z.WorldBounds();
            int x0 = Mathf.Max(0, Mathf.FloorToInt((b.xMin - areaOrigin.x) / px));
            int x1 = Mathf.Min(res - 1, Mathf.CeilToInt((b.xMax - areaOrigin.x) / px));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((b.yMin - areaOrigin.y) / px));
            int y1 = Mathf.Min(res - 1, Mathf.CeilToInt((b.yMax - areaOrigin.y) / px));
            for (int y = y0; y <= y1; y++)
            {
                float wz = areaOrigin.y + (y + 0.5f) * px;
                for (int x = x0; x <= x1; x++)
                {
                    float a = z.Coverage(areaOrigin.x + (x + 0.5f) * px, wz);
                    if (a <= 0f) continue;
                    int k = (y * res + x) * 4;
                    float keep = 1f - a;
                    weights[k] *= keep; weights[k + 1] *= keep; weights[k + 2] *= keep; weights[k + 3] *= keep;
                    weights[k + slot] += a;
                }
            }
        }

        if (mask == null || mask.width != res)
        {
            if (mask != null) DestroyImmediate(mask);
            mask = new Texture2D(res, res, TextureFormat.RGBA32, false, true)
            {
                name = "BiomeMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
        }
        var pixels = new Color32[n];
        for (int i = 0; i < n; i++)
        {
            int k = i * 4;
            pixels[i] = new Color32((byte)(weights[k + 1] * 255f + 0.5f), (byte)(weights[k + 2] * 255f + 0.5f), (byte)(weights[k + 3] * 255f + 0.5f), 255);
        }
        mask.SetPixels32(pixels);
        mask.Apply(false, false);

        Push(groundMaterial, 0f);
        if (rockMaterial != null) Push(rockMaterial, 1f);
    }

    void Push(Material m, float rockMode)
    {
        m.SetTexture(MaskId, mask);
        m.SetVector(AreaId, new Vector4(areaOrigin.x, areaOrigin.y, 1f / areaSize, 1f / areaSize));
        m.SetFloat(EdgeId, edgeSoftness);
        m.SetFloat(RockModeId, rockMode);
        for (int s = 0; s < 4; s++)
        {
            var def = s < slots.Count && slots[s] != null ? slots[s] : baseBiome;
            if (def == null) continue;
            m.SetColor(ColourIds[s, 0], def.GroundA);
            m.SetColor(ColourIds[s, 1], def.GroundB);
            m.SetColor(ColourIds[s, 2], def.Rock);
        }
    }

    // ------------------------------------------------------------------ gameplay queries

    /// The biome that covers most of this world point (base biome outside all zones).
    public BiomeDef BiomeAt(Vector3 p)
    {
        if (weights == null || res == 0) return baseBiome;
        int x = Mathf.Clamp((int)((p.x - areaOrigin.x) / areaSize * res), 0, res - 1);
        int y = Mathf.Clamp((int)((p.z - areaOrigin.y) / areaSize * res), 0, res - 1);
        int k = (y * res + x) * 4, best = 0;
        for (int s = 1; s < 4; s++) if (weights[k + s] > weights[k + best]) best = s;
        return best < slots.Count ? slots[best] : baseBiome;
    }
}
