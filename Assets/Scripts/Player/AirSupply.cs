using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// The player's air for trips outside the pipe range.
/// While the pipe is plugged in the tank is full. When the pipe pops off (PipeTether.Detached) the air runs down:
/// the air circle around the player closes in and the small bar over the head empties. With no air left HP drains until
/// the player is knocked out (PlayerHealth then brings them back to the base). Once the pipe grabs the player
/// again the tank refills fast.
/// Creates itself on the player at scene load if the scene doesn't have one.
public class AirSupply : MonoBehaviour
{
    public static AirSupply Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<AirSupply>() != null) return;
        var pc = FindFirstObjectByType<JoystickPlayerController>();
        if (pc != null) pc.gameObject.AddComponent<AirSupply>();
    }

    [Header("Air")]
    [Tooltip("Seconds of air once the pipe is off")]
    public float airSeconds = 10f;
    [Tooltip("Seconds to fill an empty tank once the pipe is back on")]
    public float refillTime = 1.2f;
    [Tooltip("Below this part of the tank the warnings start")]
    [Range(0.05f, 0.9f)] public float lowAir = 0.3f;
    [Tooltip("HP lost per second with no air left")]
    public float suffocateDamage = 15f;
    [Tooltip("Seconds per heartbeat once the air is low. The red screen edge thumps in this rhythm, and with no air left each beat deals damage")]
    public float heartbeat = 0.85f;
    [Tooltip("How strong the red screen edge is at the top of a heartbeat")]
    [Range(0f, 1f)] public float edgeStrength = 0.2f;

    [Header("Air circle around the player")]
    public bool showCircle = true;
    [Tooltip("On = with a full tank the circle is as big as what the player can see (their FogRevealer radius). Off = Circle Radius Full")]
    public bool circleMatchesSight = true;
    [Tooltip("Where in the soft edge of the sight the circle sits: 0 = the very end of the sight, 1 = where the ground is fully bright")]
    [Range(0f, 1f)] public float sightEdge = 0.5f;
    [Tooltip("Circle radius with a full tank when it doesn't follow the sight (m)")]
    public float circleRadiusFull = 5.5f;
    [Tooltip("Circle radius just before the air runs out (m)")]
    public float circleRadiusEmpty = 0.8f;
    [Tooltip("Height of the soft wall that rises from the circle: just the start of a dome (m). 0 = only the line")]
    public float wallHeight = 0.7f;
    [Tooltip("How strong the wall is at the ground (it fades to nothing at its top)")]
    [Range(0f, 1f)] public float wallAlpha = 0.4f;
    [Tooltip("Width of the line on the ground (m)")]
    public float lineWidth = 0.13f;
    [Tooltip("On = outside, the player's sight closes in together with the circle: the less air, the less they see. It opens again as the tank refills")]
    public bool sightShrinksWithAir = true;

    public Color airColor = new Color(0.35f, 0.9f, 1f, 1f);
    public Color lowColor = new Color(1f, 0.25f, 0.15f, 1f);

    [Header("Bar over the head")]
    public Vector2 barSize = new Vector2(150f, 18f);
    [Tooltip("Height above the player's feet (m)")]
    public float barHeight = 2.6f;

    /// 1 = full tank, 0 = empty.
    public float Air01 => air;
    public float SecondsLeft => air * airSeconds;
    /// The pipe is off: the air is running down.
    public bool Outside => PipeTether.Instance != null && PipeTether.Instance.Detached;
    /// Outside with no air left (HP is draining).
    public bool Suffocating => Outside && air <= 0f;

    float air = 1f, beatT, circleAlpha, circlePunch, barAlpha;
    bool empty;
    int shownSeconds = -1;
    Health health;
    PipeTether hooked;

    GameObject circle;
    Mesh wallMesh;
    MeshRenderer wallRenderer;
    Material lineMat;
    LineRenderer ring;
    Vector3[] wallVerts;
    Color[] wallCols;
    FogRevealer revealer;
    float fullSight;
    bool sightShrunk;
    Canvas canvas;
    RectTransform barRoot, barFill;
    CanvasGroup barGroup;
    Image barFillImg, edge;
    TMP_Text barText;
    Texture2D edgeTex;

    static readonly Color Good = new Color(0.45f, 1f, 0.45f);
    const int Segments = 48;

    /// Fills the tank (debug, respawn).
    public void Refill() { air = 1f; empty = false; }

    // ------------------------------------------------------------------ lifecycle

    void Awake() { Instance = this; }

    void OnEnable() { PlayerHealth.Respawned += OnRespawned; }

void OnDisable()
    {
        PlayerHealth.Respawned -= OnRespawned;
        Hook(null);
        if (sightShrunk && revealer != null) revealer.radius = fullSight;   // give the sight back
        sightShrunk = false;
        if (circle != null) circle.SetActive(false);
        if (barRoot != null) barRoot.gameObject.SetActive(false);
        if (edge != null) edge.gameObject.SetActive(false);
    }

void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (circle != null) Destroy(circle);
        if (wallMesh != null) Destroy(wallMesh);
        if (lineMat != null) Destroy(lineMat);
        if (barRoot != null) Destroy(barRoot.gameObject);
        if (edge != null) Destroy(edge.gameObject);
        if (edgeTex != null) Destroy(edgeTex);
    }

    void Hook(PipeTether pt)
    {
        if (hooked == pt) return;
        if (hooked != null) { hooked.Unplugged -= OnUnplugged; hooked.Replugged -= OnReplugged; }
        hooked = pt;
        if (hooked != null) { hooked.Unplugged += OnUnplugged; hooked.Replugged += OnReplugged; }
    }

void OnUnplugged()
    {
        circlePunch = 1f;
        GameToast.Show("PIPE OFF  -  " + Mathf.CeilToInt(SecondsLeft) + " s OF AIR", airColor);
    }

void OnReplugged()
    {
        circlePunch = 1f;
        GameToast.Show(empty || air <= 0.15f ? "JUST IN TIME!" : "PIPE BACK ON", Good);
    }

    void OnRespawned() => Refill();

    // ------------------------------------------------------------------ air

void Update()
    {
        var pt = PipeTether.Instance;
        Hook(pt);
        if (health == null) health = GetComponent<Health>();
        bool outside = pt != null && pt.Detached;
        float dt = Time.deltaTime;

        if (outside)
        {
            if (!PlayerHealth.KnockedOut)
            {
                bool wasLow = air <= lowAir;
                air = Mathf.Max(0f, air - dt / Mathf.Max(0.1f, airSeconds));
                if (!wasLow && air <= lowAir && air > 0f)
                {
                    beatT = 0f;
                    GameToast.Show("LOW AIR  -  GET BACK TO THE PIPE", lowColor);
                }
                // the heartbeat runs while the air is low; with no air left every beat hurts
                bool beat = false;
                if (air <= lowAir)
                {
                    beatT += dt / Mathf.Max(0.2f, heartbeat);
                    if (beatT >= 1f) { beatT -= 1f; beat = true; }
                }
                if (air <= 0f)
                {
                    if (!empty)
                    {
                        empty = true;
                        beatT = 0f;
                        beat = true;
                        GameToast.Show("NO AIR!", lowColor);
                        CombatFX.Hit(transform.position + Vector3.up * 1.2f, Vector3.up, lowColor, 1.4f);   // the bubble pops
                    }
                    if (beat && health != null) health.Damage(suffocateDamage * Mathf.Max(0.2f, heartbeat), gameObject);
                }
            }
        }
        else
        {
            empty = false;   // (the Replugged toast has already read it)
            air = Mathf.Min(1f, air + dt / Mathf.Max(0.05f, refillTime));
        }

        float low01 = air < lowAir ? 1f - air / Mathf.Max(0.01f, lowAir) : 0f;
        // heartbeat: two soft thumps per beat (lub-dub), nothing in between
        float pulse = outside && low01 > 0f ? Mathf.Max(Bump(beatT, 0.1f), 0.6f * Bump(beatT, 0.36f)) : 0f;
        UpdateSight(outside);
        UpdateCircle(outside, low01, pulse, dt);
        UpdateBar(low01, pulse);
        UpdateEdge(outside, low01, pulse);
    }

    static float Bump(float x, float centre)
    {
        float d = (x - centre) / 0.09f;
        return Mathf.Exp(-d * d);
    }

    // ------------------------------------------------------------------ air circle

/// The circle of air around the player: a line on the ground with a low, soft wall (just the start of a dome).
    /// As big as the player's sight with a full tank; it closes in as the air runs down.
    void UpdateCircle(bool outside, float low01, float pulse, float dt)
    {
        float want = outside && showCircle && air > 0f ? 1f : 0f;
        circleAlpha = Mathf.MoveTowards(circleAlpha, want, dt * (want > circleAlpha ? 5f : 2.5f));
        circlePunch = Mathf.MoveTowards(circlePunch, 0f, dt * 3f);
        if (circleAlpha <= 0.001f)
        {
            if (circle != null && circle.activeSelf) circle.SetActive(false);
            return;
        }
        if (circle == null) BuildCircle();
        if (circle == null) return;
        if (!circle.activeSelf) circle.SetActive(true);

        float grow = circleAlpha * circleAlpha * (3f - 2f * circleAlpha);   // opens up as it appears, closes as it fades
        float radius = Mathf.Lerp(circleRadiusEmpty, FullRadius(), air) * Mathf.Lerp(0.6f, 1f, grow)
                     * (1f + 0.015f * Mathf.Sin(Time.time * 3f) + 0.08f * circlePunch + 0.04f * low01 * pulse);
        Color c = airColor;   // stays blue: the bar and the screen edge show the danger
        float blink = 1f;
        Vector3 p = transform.position;
        float h = Mathf.Min(wallHeight, radius * 0.6f);
        float topR = radius - h * 0.3f;   // the wall leans in a little, like the foot of a dome
        Color bottom = c; bottom.a = wallAlpha * circleAlpha * blink;
        Color top = c; top.a = 0f;
        for (int i = 0; i <= Segments; i++)
        {
            float a = (i % Segments) * Mathf.PI * 2f / Segments;
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            var q = new Vector3(p.x + cs * radius, 0f, p.z + sn * radius);
            q.y = GroundUtil.Height(q, p.y) + 0.07f;
            if (i < Segments) ring.SetPosition(i, q);
            wallVerts[i * 2] = q;
            wallVerts[i * 2 + 1] = new Vector3(p.x + cs * topR, q.y + h, p.z + sn * topR);
            wallCols[i * 2] = bottom;
            wallCols[i * 2 + 1] = top;
        }
        wallMesh.vertices = wallVerts;
        wallMesh.colors = wallCols;
        wallMesh.RecalculateBounds();
        wallRenderer.enabled = h > 0.01f && wallAlpha > 0f;
        c.a = circleAlpha * 0.95f * blink;
        ring.startColor = ring.endColor = c;
        ring.widthMultiplier = lineWidth;
    }

    /// Circle radius with a full tank: what the player can see around them.
float FullRadius()
    {
        if (!circleMatchesSight) return circleRadiusFull;
        if (revealer == null) revealer = GetComponent<FogRevealer>();
        if (revealer == null) return circleRadiusFull;
        float sight = sightShrunk ? fullSight : revealer.radius;
        return Mathf.Max(circleRadiusEmpty, sight - SightEdgeWidth());
    }

    /// How far inside the end of the sight the circle is drawn (the sight has a soft edge).
    float SightEdgeWidth() => FogOfWar.Instance != null ? FogOfWar.Instance.edgeSoftness * sightEdge : 0f;

    /// Outside, the player's sight closes in together with the air circle (and opens again as the tank refills).
    void UpdateSight(bool outside)
    {
        if (revealer == null) revealer = GetComponent<FogRevealer>();
        if (revealer == null) return;
        if (!(sightShrinksWithAir && (outside || air < 1f)))
        {
            if (sightShrunk) { revealer.radius = fullSight; sightShrunk = false; }
            else fullSight = revealer.radius;   // whatever else sets the player's sight (upgrades...) stays in charge
            return;
        }
        if (!sightShrunk) { fullSight = revealer.radius; sightShrunk = true; }
        float min = Mathf.Min(fullSight, circleRadiusEmpty + SightEdgeWidth());
        revealer.radius = Mathf.Lerp(min, fullSight, air);
    }

void BuildCircle()
    {
        var pt = PipeTether.Instance;
        Material src = pt != null ? pt.limitMaterial : null;
        if (src != null) lineMat = new Material(src);
        else
        {
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) { showCircle = false; return; }
            lineMat = new Material(sh);
        }
        lineMat.color = Color.white;   // the colour comes from the vertices

        // the wall: a strip standing on the circle, solid at the ground and fading out at its top
        circle = new GameObject("Air Circle");
        wallMesh = new Mesh { name = "Air Circle Wall" };
        wallMesh.MarkDynamic();
        wallVerts = new Vector3[(Segments + 1) * 2];
        wallCols = new Color[(Segments + 1) * 2];
        var tris = new int[Segments * 6];
        for (int i = 0; i < Segments; i++)
        {
            int v = i * 2, t = i * 6;
            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v + 2; tris[t + 4] = v + 1; tris[t + 5] = v + 3;
        }
        wallMesh.vertices = wallVerts;
        wallMesh.colors = wallCols;
        wallMesh.triangles = tris;
        circle.AddComponent<MeshFilter>().sharedMesh = wallMesh;
        wallRenderer = circle.AddComponent<MeshRenderer>();
        wallRenderer.sharedMaterial = lineMat;
        wallRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wallRenderer.receiveShadows = false;

        // the line on the ground
        var go = new GameObject("Line");
        go.transform.SetParent(circle.transform, false);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // lies flat
        ring = go.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = Segments;
        ring.widthMultiplier = lineWidth;
        ring.alignment = LineAlignment.TransformZ;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.sharedMaterial = lineMat;
    }

    // ------------------------------------------------------------------ bar over the head

    void UpdateBar(float low01, float pulse)
    {
        float want = air < 0.999f && !PlayerHealth.KnockedOut ? 1f : 0f;
        barAlpha = Mathf.MoveTowards(barAlpha, want, Time.unscaledDeltaTime * 5f);
        if (barAlpha <= 0.001f)
        {
            if (barRoot != null && barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(false);
            return;
        }
        if (barRoot == null) BuildBar();
        var cam = Camera.main;
        if (barRoot == null || cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * barHeight);
        bool onScreen = sp.z > 0f;
        if (barRoot.gameObject.activeSelf != onScreen) barRoot.gameObject.SetActive(onScreen);
        if (!onScreen) return;

        float k = canvas != null && canvas.scaleFactor > 0f ? 1f / canvas.scaleFactor : 1f;
        barRoot.anchoredPosition = new Vector2(sp.x, sp.y) * k;
        barGroup.alpha = barAlpha;
        barFill.anchorMax = new Vector2(Mathf.Clamp01(air), 1f);
        barFillImg.color = Color.Lerp(airColor, lowColor, Mathf.Clamp01(low01 * 1.5f));
        int s = Mathf.CeilToInt(SecondsLeft);
        if (s != shownSeconds)
        {
            shownSeconds = s;
            barText.text = s.ToString();
        }
        barText.color = low01 > 0f ? lowColor : Color.white;
        barRoot.localScale = Vector3.one * (1f + 0.12f * low01 * pulse);
    }

    void BuildBar()
    {
        var canvasGo = GameObject.Find("UI Canvas");
        if (canvasGo == null) return;
        canvas = canvasGo.GetComponent<Canvas>();
        barRoot = Rect("Air Bar", canvasGo.transform);
        var hpHud = canvasGo.transform.Find("PlayerHealthHUD");
        barRoot.SetSiblingIndex(hpHud != null ? hpHud.GetSiblingIndex() + 1 : 0);   // above the world, below popups
        barRoot.anchorMin = barRoot.anchorMax = Vector2.zero;
        barRoot.pivot = new Vector2(0.5f, 0f);
        barRoot.sizeDelta = barSize + new Vector2(6f, 6f);
        barGroup = barRoot.gameObject.AddComponent<CanvasGroup>();
        barGroup.blocksRaycasts = false;
        barGroup.interactable = false;
        var bg = barRoot.gameObject.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.06f, 0.09f, 0.85f);
        bg.raycastTarget = false;

        var track = Rect("Track", barRoot);
        track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(3f, 3f); track.offsetMax = new Vector2(-3f, -3f);
        barFill = Rect("Fill", track);
        barFill.anchorMin = Vector2.zero; barFill.anchorMax = Vector2.one;
        barFill.offsetMin = barFill.offsetMax = Vector2.zero;
        barFillImg = barFill.gameObject.AddComponent<Image>();
        barFillImg.color = airColor;
        barFillImg.raycastTarget = false;

        var txt = Rect("Seconds", barRoot);
        txt.anchorMin = txt.anchorMax = new Vector2(1f, 0.5f);
        txt.pivot = new Vector2(0f, 0.5f);
        txt.anchoredPosition = new Vector2(8f, 0f);
        txt.sizeDelta = new Vector2(90f, 40f);
        barText = txt.gameObject.AddComponent<TextMeshProUGUI>();
        barText.fontSize = 30f;
        barText.fontStyle = FontStyles.Bold;
        barText.alignment = TextAlignmentOptions.MidlineLeft;
        barText.raycastTarget = false;
        barText.outlineWidth = 0.25f;
        barText.outlineColor = new Color32(10, 15, 20, 255);
    }

    // ------------------------------------------------------------------ low-air screen edge

    void UpdateEdge(bool outside, float low01, float pulse)
    {
        // faint steady red, thumping up in the heartbeat rhythm
        float a = outside && low01 > 0f && !PlayerHealth.KnockedOut ? edgeStrength * Mathf.Lerp(0.5f, 1f, low01) * (0.2f + 0.8f * pulse) : 0f;
        if (a <= 0.001f)
        {
            if (edge != null && edge.gameObject.activeSelf) edge.gameObject.SetActive(false);
            return;
        }
        if (edge == null) BuildEdge();
        if (edge == null) return;
        if (!edge.gameObject.activeSelf) edge.gameObject.SetActive(true);
        var c = lowColor; c.a = a;
        edge.color = c;
    }

    void BuildEdge()
    {
        var canvasGo = GameObject.Find("UI Canvas");
        if (canvasGo == null) return;
        var rt = Rect("Low Air Edge", canvasGo.transform);
        var hpHud = canvasGo.transform.Find("PlayerHealthHUD");
        rt.SetSiblingIndex(hpHud != null ? hpHud.GetSiblingIndex() : 0);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        edge = rt.gameObject.AddComponent<Image>();
        edge.raycastTarget = false;

        const int n = 64;
        edgeTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * 0.6f + Mathf.Sqrt(dx * dx + dy * dy) * 0.4f;
                float al = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.05f, d));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(al * 255));
            }
        edgeTex.SetPixels32(px);
        edgeTex.Apply();
        edge.sprite = Sprite.Create(edgeTex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
