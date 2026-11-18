using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Simple UI minimap. Shows the whole level from above with the player and the main
/// objects (home, factory, trash piles, disks, bots). Everything inside is built at runtime,
/// so you only need this component on an empty RectTransform under the canvas.
[RequireComponent(typeof(RectTransform))]
public class Minimap : MonoBehaviour
{
    [Header("World area shown on the map (X/Z)")]
    public Vector2 worldCenter = Vector2.zero;
    public Vector2 worldSize = new Vector2(80f, 80f);
    [Tooltip("Keep the player in the middle and scroll the map around them.")]
    public bool followPlayer = true;
    [Tooltip("How many world units fit across the map when following the player.")]
    public float viewSize = 50f;
    [Tooltip("Home / Factory icons stick to the map edge when they are out of view.")]
    public bool pinBuildingsToEdge = true;
    public bool showLevelBorder = true;
    [Tooltip("Darken unexplored areas and hide undiscovered trash/disks (needs a FogOfWar in the scene).")]
    public bool showFogOfWar = true;
    [Tooltip("Rotate the map so that 'up' on the map matches the direction the camera looks.")]
    public bool rotateWithCamera = true;

    [Header("Look")]
    public float padding = 10f;
    public Color backgroundColor = new Color(0.13f, 0.09f, 0.08f, 0.9f);
    public Color borderColor = new Color(1f, 1f, 1f, 0.45f);
    public Color levelBorderColor = new Color(1f, 0.85f, 0.7f, 0.35f);
    public Color playerColor = Color.white;
    public Color homeColor = new Color(0.40f, 0.92f, 0.50f);
    public Color factoryColor = new Color(1f, 0.62f, 0.22f);
    public Color trashColor = new Color(0.86f, 0.72f, 0.38f);
    public Color diskColor = new Color(0.35f, 0.85f, 1f);
    public Color botColor = new Color(0.70f, 0.70f, 0.75f);
    public Color chargerColor = new Color(0.45f, 0.90f, 1f);
    public Color deadBotColor = new Color(1f, 0.3f, 0.25f);

    [Header("Icon sizes (canvas units)")]
    public float playerSize = 36f;
    public float buildingSize = 34f;
    public float trashSize = 26f;
    public float diskSize = 18f;
    public float botSize = 14f;

    enum Kind { Home, Factory, Trash, Disk, Bot, Charger }

    class Marker
    {
        public Kind kind;
        public Component source;
        public RectTransform icon;
        public Image image;
        public bool upright;
    }

    RectTransform viewport, content, playerIcon, levelBorder;
    RawImage fogOverlay;
    Transform player;
    readonly Dictionary<Component, Marker> markers = new Dictionary<Component, Marker>();
    readonly List<Component> toRemove = new List<Component>();
    float nextSync;

    static Sprite sCircle, sRoundFill, sRoundFrame, sArrow, sDiamond;

    void Awake()
    {
        BuildSprites();
        Build();
    }

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        Sync();
    }

    // ---------------- building ----------------

    void Build()
    {
        var bg = GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        bg.sprite = sRoundFill; bg.type = Image.Type.Sliced; bg.color = backgroundColor; bg.raycastTarget = false;

        viewport = NewRect("Viewport", transform);
        Stretch(viewport, padding);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = NewRect("Content", viewport);
        content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
        content.sizeDelta = Vector2.zero;

        var frame = NewImage("Frame", transform, sRoundFrame, borderColor);
        frame.type = Image.Type.Sliced;
        Stretch(frame.rectTransform, 0f);

        if (showLevelBorder)
        {
            var lb = NewImage("LevelBorder", content, sRoundFrame, levelBorderColor);
            lb.type = Image.Type.Sliced;
            lb.pixelsPerUnitMultiplier = 2f;
            levelBorder = lb.rectTransform;
        }

        if (showFogOfWar)
        {
            var fogGo = new GameObject("FogOfWar", typeof(RectTransform));
            fogGo.transform.SetParent(content, false);
            fogOverlay = fogGo.AddComponent<RawImage>();
            fogOverlay.raycastTarget = false;
            fogOverlay.enabled = false;
        }

        var pi = NewImage("PlayerIcon", content, sArrow, playerColor);
        playerIcon = pi.rectTransform;
        playerIcon.sizeDelta = Vector2.one * playerSize;
        AddOutline(pi);
    }

    Marker CreateMarker(Kind kind, Component source)
    {
        Sprite sprite; Color color; float size; string letter = null;
        switch (kind)
        {
            case Kind.Home: sprite = sRoundFill; color = homeColor; size = buildingSize; letter = "H"; break;
            case Kind.Factory: sprite = sRoundFill; color = factoryColor; size = buildingSize; letter = "F"; break;
            case Kind.Charger: sprite = sRoundFill; color = chargerColor; size = buildingSize; letter = "C"; break;
            case Kind.Trash: sprite = sCircle; color = trashColor; size = trashSize; break;
            case Kind.Disk: sprite = sDiamond; color = diskColor; size = diskSize; break;
            default: sprite = sCircle; color = botColor; size = botSize; break;
        }

        var img = NewImage(kind + "Icon", content, sprite, color);
        if (kind == Kind.Home || kind == Kind.Factory || kind == Kind.Charger) img.type = Image.Type.Sliced;
        img.rectTransform.sizeDelta = Vector2.one * size;
        AddOutline(img);

        if (letter != null)
        {
            var go = new GameObject("Letter", typeof(RectTransform));
            go.transform.SetParent(img.transform, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = letter;
            t.fontSize = size * 0.62f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0.08f, 0.08f, 0.08f);
            t.raycastTarget = false;
            Stretch((RectTransform)go.transform, 0f);
        }

        var m = new Marker { kind = kind, source = source, icon = img.rectTransform, image = img, upright = letter != null };
        markers[source] = m;
        playerIcon.SetAsLastSibling(); // player always on top
        return m;
    }

    // ---------------- runtime ----------------

    /// Picks up objects that appeared (spawned bots, new piles...) every half second.
    void Sync()
    {
        foreach (var h in FindObjectsByType<HomeBase>(FindObjectsSortMode.None)) Ensure(Kind.Home, h);
        foreach (var f in FindObjectsByType<BotFactory>(FindObjectsSortMode.None)) Ensure(Kind.Factory, f);
        foreach (var d in FindObjectsByType<DiskPickup>(FindObjectsSortMode.None)) Ensure(Kind.Disk, d);
        foreach (var p in TrashPile.All) Ensure(Kind.Trash, p);
        foreach (var b in ScoutBot.All) Ensure(Kind.Bot, b);
        foreach (var c in ChargingStation.All) Ensure(Kind.Charger, c);
    }

    void Ensure(Kind kind, Component c)
    {
        if (c != null && !markers.ContainsKey(c)) CreateMarker(kind, c);
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextSync)
        {
            nextSync = Time.unscaledTime + 0.5f;
            Sync();
        }

        float yaw = 0f;
        if (rotateWithCamera && Camera.main != null) yaw = Camera.main.transform.eulerAngles.y;
        content.localRotation = Quaternion.Euler(0f, 0f, yaw);
        Quaternion upright = Quaternion.Euler(0f, 0f, -yaw);

        toRemove.Clear();
        foreach (var kv in markers)
        {
            var m = kv.Value;
            if (m.source == null) { toRemove.Add(kv.Key); continue; }

            bool visible = m.source.gameObject.activeInHierarchy;
            float scale = 1f;
            if (m.kind == Kind.Trash)
            {
                var pile = (TrashPile)m.source;
                visible &= !pile.IsEmpty;
                scale = Mathf.Lerp(0.55f, 1f, pile.maxAmount > 0 ? (float)pile.amount / pile.maxAmount : 0f);
            }
            else if (m.kind == Kind.Bot)
            {
                var bot = (ScoutBot)m.source;
                m.image.color = bot.IsDead ? deadBotColor : bot.Program != null ? bot.Program.color : botColor;
            }

            if (showFogOfWar && (m.kind == Kind.Trash || m.kind == Kind.Disk) && FogOfWar.Instance != null)
                visible &= FogOfWar.Instance.IsExplored(m.source.transform.position);

            if (m.icon.gameObject.activeSelf != visible) m.icon.gameObject.SetActive(visible);
            if (!visible) continue;

            Vector2 pos = WorldToMap(m.source.transform.position);
            if (pinBuildingsToEdge && m.upright)
            {
                // clamp to the visible square (done in viewport space so it works when rotated)
                Vector2 v = Rotate(pos, yaw);
                float hx = viewport.rect.width * 0.5f - buildingSize * 0.55f;
                float hy = viewport.rect.height * 0.5f - buildingSize * 0.55f;
                float k = Mathf.Max(Mathf.Abs(v.x) / hx, Mathf.Abs(v.y) / hy);
                if (k > 1f) pos = Rotate(v / k, -yaw);
            }
            m.icon.anchoredPosition = pos;
            m.icon.localScale = Vector3.one * scale;
            if (m.upright) m.icon.localRotation = upright;
        }
        foreach (var k in toRemove)
        {
            if (markers[k].icon != null) Destroy(markers[k].icon.gameObject);
            markers.Remove(k);
        }

        if (fogOverlay != null)
        {
            var fog = FogOfWar.Instance;
            bool on = fog != null && fog.isActiveAndEnabled && fog.MinimapTexture != null;
            if (fogOverlay.enabled != on) fogOverlay.enabled = on;
            if (on)
            {
                if (fogOverlay.texture != fog.MinimapTexture) fogOverlay.texture = fog.MinimapTexture;
                var rt = fogOverlay.rectTransform;
                rt.anchoredPosition = (fog.worldCenter - MapCenter) * Scale;
                rt.sizeDelta = fog.worldSize * Scale;
            }
        }

        if (levelBorder != null)
        {
            levelBorder.anchoredPosition = (worldCenter - MapCenter) * Scale;
            levelBorder.sizeDelta = worldSize * Scale;
        }

        if (player != null)
        {
            playerIcon.anchoredPosition = WorldToMap(player.position);
            playerIcon.localRotation = Quaternion.Euler(0f, 0f, -player.eulerAngles.y);
        }
    }

    float Scale => viewport.rect.width / Mathf.Max(1f, followPlayer ? viewSize : Mathf.Max(worldSize.x, worldSize.y));

    Vector2 MapCenter => followPlayer && player != null ? new Vector2(player.position.x, player.position.z) : worldCenter;

    Vector2 WorldToMap(Vector3 world)
    {
        return (new Vector2(world.x, world.z) - MapCenter) * Scale;
    }

    // ---------------- helpers ----------------

    static Vector2 Rotate(Vector2 p, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
        return new Vector2(p.x * c - p.y * sn, p.x * sn + p.y * c);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        return img;
    }

    static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
    }

    static void AddOutline(Graphic g)
    {
        var o = g.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.65f);
        o.effectDistance = new Vector2(1.5f, -1.5f);
    }

    // ---------------- procedural sprites ----------------

    static void BuildSprites()
    {
        if (sCircle != null) return;
        const float r = 0.45f; // corner radius in -1..1 space
        sCircle = MakeSprite(64, (u, v) => u * u + v * v <= 1f, Vector4.zero);
        sDiamond = MakeSprite(64, (u, v) => Mathf.Abs(u) + Mathf.Abs(v) <= 1f, Vector4.zero);
        sRoundFill = MakeSprite(64, (u, v) => RoundRectSdf(u, v, r) <= 0f, Vector4.one * 16f);
        sRoundFrame = MakeSprite(64, (u, v) => { float d = RoundRectSdf(u, v, r); return d <= 0f && d >= -0.09f; }, Vector4.one * 16f);
        var arrow = new[] { new Vector2(0f, 0.95f), new Vector2(0.78f, -0.8f), new Vector2(0f, -0.38f), new Vector2(-0.78f, -0.8f) };
        sArrow = MakeSprite(64, (u, v) => InPolygon(new Vector2(u, v), arrow), Vector4.zero);
    }

    static float RoundRectSdf(float u, float v, float radius)
    {
        float qx = Mathf.Abs(u) - (1f - radius), qy = Mathf.Abs(v) - (1f - radius);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    static bool InPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    static Sprite MakeSprite(int size, System.Func<float, float, bool> inside, Vector4 border)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        var px = new Color32[size * size];
        const int ss = 4; // supersampling for smooth edges
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int hits = 0;
            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
            {
                float u = (x + (sx + 0.5f) / ss) / size * 2f - 1f;
                float v = (y + (sy + 0.5f) / ss) / size * 2f - 1f;
                if (inside(u, v)) hits++;
            }
            px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * hits / (ss * ss)));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }
}
