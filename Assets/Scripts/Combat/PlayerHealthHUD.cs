using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Landscape HUD for the player's HP: bar at the top centre, red screen-edge flash on hit,
/// "HEALING" hint inside the base, and the KNOCKED OUT overlay with the respawn countdown.
/// Builds itself under "UI Canvas" at scene load (no builder step).
public class PlayerHealthHUD : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<PlayerHealthHUD>() != null) return;
        var canvas = GameObject.Find("UI Canvas");
        if (canvas == null) return;
        var go = new GameObject("PlayerHealthHUD", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(canvas.transform, false);
        var hud = canvas.transform.Find("HUD");
        if (hud != null) go.transform.SetSiblingIndex(hud.GetSiblingIndex() + 1);   // above the world, below popups
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<PlayerHealthHUD>();
    }

    public Vector2 barSize = new Vector2(560f, 30f);
    public float topMargin = 26f;

    static readonly Color Green = new Color(0.45f, 0.9f, 0.4f);
    static readonly Color Yellow = new Color(1f, 0.82f, 0.25f);
    static readonly Color Red = new Color(1f, 0.32f, 0.25f);
    static readonly Color Panel = new Color(0.10f, 0.086f, 0.067f, 0.72f);

    RectTransform root, fill, lag;
    Image fillImg, frameImg, edge, koShade;
    TMP_Text valueText, healText, koTitle, koCount;
    GameObject koGroup;
    PlayerHealth ph;
    float lagValue = 1f, edgeK, punch, shownHP = -1f;
    Vector3 rootScale;

    void Start()
    {
        BuildEdge();
        BuildBar();
        BuildKnockOut();
        rootScale = root.localScale;
    }

    void BuildBar()
    {
        root = Rect("HP Bar", transform);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -topMargin);
        root.sizeDelta = new Vector2(barSize.x + 150f, barSize.y + 24f);
        frameImg = root.gameObject.AddComponent<Image>();
        frameImg.color = Panel; frameImg.raycastTarget = false;

        var cap = Text(Rect("Caption", root), "HP", 26f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.MidlineLeft);
        var c = cap.rectTransform;
        c.anchorMin = new Vector2(0f, 0f); c.anchorMax = new Vector2(0f, 1f); c.pivot = new Vector2(0f, 0.5f);
        c.anchoredPosition = new Vector2(16f, 0f); c.sizeDelta = new Vector2(50f, 0f);

        var track = Rect("Track", root);
        track.anchorMin = track.anchorMax = track.pivot = new Vector2(0f, 0.5f);
        track.anchoredPosition = new Vector2(62f, 0f);
        track.sizeDelta = barSize;
        var ti = track.gameObject.AddComponent<Image>(); ti.color = new Color(0f, 0f, 0f, 0.55f); ti.raycastTarget = false;

        lag = Rect("Lag", track); Stretch(lag);
        var li = lag.gameObject.AddComponent<Image>(); li.color = new Color(1f, 0.95f, 0.85f, 0.85f); li.raycastTarget = false;
        fill = Rect("Fill", track); Stretch(fill);
        fillImg = fill.gameObject.AddComponent<Image>(); fillImg.color = Green; fillImg.raycastTarget = false;

        valueText = Text(Rect("Value", root), "100", 26f, Color.white, TextAlignmentOptions.MidlineRight);
        var v = valueText.rectTransform;
        v.anchorMin = new Vector2(1f, 0f); v.anchorMax = new Vector2(1f, 1f); v.pivot = new Vector2(1f, 0.5f);
        v.anchoredPosition = new Vector2(-14f, 0f); v.sizeDelta = new Vector2(80f, 0f);

        healText = Text(Rect("Healing", root), "+ HEALING", 22f, Green, TextAlignmentOptions.Top);
        var h = healText.rectTransform;
        h.anchorMin = h.anchorMax = h.pivot = new Vector2(0.5f, 0f);
        h.anchoredPosition = new Vector2(0f, -30f); h.sizeDelta = new Vector2(300f, 30f);
        healText.gameObject.SetActive(false);
    }

    void BuildEdge()
    {
        var e = Rect("Hit Edge", transform); Stretch(e);
        edge = e.gameObject.AddComponent<Image>();
        edge.sprite = VignetteSprite();
        edge.color = new Color(1f, 0.1f, 0.05f, 0f);
        edge.raycastTarget = false;
    }

    void BuildKnockOut()
    {
        koGroup = Rect("Knocked Out", transform).gameObject;
        var r = (RectTransform)koGroup.transform; Stretch(r);
        koShade = koGroup.AddComponent<Image>();
        koShade.color = new Color(0.12f, 0f, 0f, 0.55f);
        koShade.raycastTarget = true;   // swallow taps while down
        koTitle = Text(Rect("Title", r), "KNOCKED OUT", 96f, new Color(1f, 0.35f, 0.3f), TextAlignmentOptions.Center);
        koTitle.rectTransform.anchoredPosition = new Vector2(0f, 60f);
        koTitle.rectTransform.sizeDelta = new Vector2(1200f, 140f);
        koCount = Text(Rect("Count", r), "", 40f, Color.white, TextAlignmentOptions.Center);
        koCount.rectTransform.anchoredPosition = new Vector2(0f, -40f);
        koCount.rectTransform.sizeDelta = new Vector2(1000f, 60f);
        koGroup.SetActive(false);
    }

    void Update()
    {
        if (ph == null)
        {
            ph = PlayerHealth.Instance;
            if (ph == null) { root.gameObject.SetActive(false); return; }
            ph.Health.OnDamaged += OnHit;
            root.gameObject.SetActive(true);
        }
        var h = ph.Health;
        float v = h.Fraction;
        float dt = Time.unscaledDeltaTime;

        if (!Mathf.Approximately(h.Current, shownHP))
        {
            shownHP = h.Current;
            valueText.text = Mathf.CeilToInt(h.Current).ToString();
        }
        lagValue = v > lagValue ? v : Mathf.MoveTowards(lagValue, v, dt * 0.6f);
        SetFill(fill, v);
        SetFill(lag, lagValue);
        Color c = v > 0.5f ? Green : v > 0.25f ? Yellow : Red;
        if (ph.IsHealing) c = Color.Lerp(c, Color.white, Mathf.PingPong(Time.time * 2f, 1f) * 0.35f);
        if (PlayerHealth.GodMode) c = new Color(0.45f, 0.95f, 1f);
        fillImg.color = c;
        valueText.color = v <= 0.25f ? Red : Color.white;
        healText.gameObject.SetActive(ph.IsHealing);
        if (ph.IsHealing) healText.alpha = 0.6f + 0.4f * Mathf.PingPong(Time.time * 2f, 1f);

        punch = Mathf.MoveTowards(punch, 0f, dt * 5f);
        root.localScale = rootScale * (1f + 0.06f * punch);

        // red screen edge: flash on hit, steady glow when low
        edgeK = Mathf.MoveTowards(edgeK, 0f, dt * 2.2f);
        float low = !PlayerHealth.KnockedOut && v < 0.3f ? (0.3f - v) / 0.3f * (0.25f + 0.15f * Mathf.Sin(Time.time * 5f)) : 0f;
        edge.color = new Color(1f, 0.1f, 0.05f, Mathf.Clamp01(Mathf.Max(edgeK * 0.85f, low)));

        bool ko = PlayerHealth.KnockedOut;
        if (koGroup.activeSelf != ko) { koGroup.SetActive(ko); if (ko) koGroup.transform.SetAsLastSibling(); }
        if (ko)
        {
            koCount.text = "BACK TO THE BASE IN " + Mathf.CeilToInt(ph.KnockOutLeft);
            koTitle.transform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(Time.time * 6f));
        }
    }

void OnHit(float amount, GameObject src)
    {
        if (ph != null && src == ph.gameObject)
        {
            // hurting from the inside (no air): a softer thump than an enemy's blow
            edgeK = Mathf.Max(edgeK, 0.4f);
            punch = 0.5f;
            return;
        }
        edgeK = Mathf.Clamp01(0.45f + amount / 25f);
        punch = 1f;
    }

    void OnDestroy() { if (ph != null) ph.Health.OnDamaged -= OnHit; }

    static void SetFill(RectTransform r, float f)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = new Vector2(Mathf.Clamp01(f), 1f);
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    // ------------------------------------------------------------------ helpers

    static Sprite vignette;
    static Sprite VignetteSprite()
    {
        if (vignette != null) return vignette;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * 0.6f + Mathf.Sqrt(dx * dx + dy * dy) * 0.4f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1.05f, d));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        vignette = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return vignette;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    static TMP_Text Text(RectTransform rt, string s, float size, Color c, TextAlignmentOptions align)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = s; t.fontSize = size; t.color = c; t.alignment = align;
        t.fontStyle = FontStyles.Bold; t.raycastTarget = false;
        return t;
    }
}
