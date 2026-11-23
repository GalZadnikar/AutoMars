using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// SAND PROTOTYPE - tank bar above the player ("22/60") and "CLEAR THE AREA xx%" at the top while near sand.
/// Builds its own small overlay canvas, so the game's UI is not touched.
[DefaultExecutionOrder(1000)]   // after the camera has moved
public class SandHUD : MonoBehaviour
{
    public SuctionGun gun;
    [Tooltip("Show the HUD when the player is this close to a sand field (m)")]
    public float showDistance = 10f;
    [Tooltip("Height of the tank bar above the player's feet (m)")]
    public float barHeight = 3.6f;
    [Tooltip("Old 'CLEAR THE AREA' panel at the top of the screen (each sand field now has its own % label)")]
    public bool showAreaPanel = false;

    static readonly Color Green = new Color(0.45f, 0.85f, 0.2f);
    static readonly Color Red = new Color(0.95f, 0.3f, 0.25f);
    static readonly Color Blue = new Color(0.25f, 0.62f, 1f);

    Canvas canvas;
    RectTransform canvasRect, tankRoot, tankFill, areaRoot, areaFill;
    Image tankFillImg;
    TMP_Text tankLabel, tankShadow, areaTitle, areaPct;
    Sprite rounded;
    Texture2D roundedTex;
    float shownFill;

    void OnEnable()
    {
        if (gun == null) gun = GetComponent<SuctionGun>();
        if (Application.isPlaying) Build();
    }

    void OnDisable()
    {
        if (canvas != null) Destroy(canvas.gameObject);
        if (rounded != null) Destroy(rounded);
        if (roundedTex != null) Destroy(roundedTex);
    }

    void LateUpdate()
    {
        if (canvas == null) return;
        if (gun == null || gun.player == null) { tankRoot.gameObject.SetActive(false); areaRoot.gameObject.SetActive(false); return; }

        Vector3 pos = gun.player.position;
        SandField near = null;
        float best = float.MaxValue;
        foreach (var f in SandField.All)
        {
            float d = f.DistanceTo(pos);
            if (d < best) { best = d; near = f; }
        }
        bool nearSand = near != null && best < showDistance;
        var cam = Camera.main;

        // --- tank bar above the player
        bool showTank = cam != null && (gun.Stored > 0.01f || nearSand);
        if (tankRoot.gameObject.activeSelf != showTank) tankRoot.gameObject.SetActive(showTank);
        if (showTank)
        {
            Vector3 sp = cam.WorldToScreenPoint(pos + Vector3.up * barHeight);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out var lp);
            tankRoot.anchoredPosition = lp;
            float frac = gun.unlimitedCapacity ? 1f : gun.capacity > 0 ? gun.Stored / gun.capacity : 0f;
            shownFill = Mathf.Lerp(shownFill, frac, Time.deltaTime * 12f);
            tankFill.anchorMax = new Vector2(Mathf.Clamp01(shownFill), 1f);
            tankFillImg.color = gun.IsFull ? Red : Green;
            string txt = gun.IsFull ? "FULL" : gun.unlimitedCapacity ? Mathf.FloorToInt(gun.Stored) + " / \u221E" : Mathf.FloorToInt(gun.Stored) + "/" + gun.capacity;
            tankLabel.text = txt; tankShadow.text = txt;
            tankLabel.color = gun.IsFull ? Red : Color.white;
        }

        // --- area progress at the top
        bool showArea = showAreaPanel && nearSand;
        if (areaRoot.gameObject.activeSelf != showArea) areaRoot.gameObject.SetActive(showArea);
        if (showArea)
        {
            float p = Mathf.Clamp01(near.Progress / 0.98f);   // the last scraps at the border don't count
            areaFill.anchorMax = new Vector2(p, 1f);
            areaPct.text = Mathf.FloorToInt(p * 100f) + "%";
            areaTitle.text = p >= 1f ? "AREA CLEARED!" : "CLEAR THE AREA";
        }
    }

    // ------------------------------------------------------------------ building

    void Build()
    {
        var go = new GameObject("Sand HUD (prototype)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        canvasRect = (RectTransform)go.transform;

        // same scaling as the game's own UI
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        foreach (var other in FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None))
        {
            if (other == scaler || other.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
            scaler.referenceResolution = other.referenceResolution;
            scaler.matchWidthOrHeight = other.matchWidthOrHeight;
            break;
        }

        rounded = MakeRounded();

        // tank bar
        tankRoot = Rect("Tank", canvasRect, new Vector2(220f, 80f));
        tankRoot.pivot = new Vector2(0.5f, 0f);
        var tankBg = Img("Bar", tankRoot, new Color(0.13f, 0.1f, 0.1f, 0.9f));
        Place(tankBg.rectTransform, new Vector2(0f, 0f), new Vector2(200f, 24f));
        tankFillImg = Img("Fill", tankBg.rectTransform, Green);
        tankFill = Stretch(tankFillImg.rectTransform, 3f);
        tankShadow = Text("Shadow", tankRoot, 38f, new Color(0f, 0f, 0f, 0.6f));
        Place(tankShadow.rectTransform, new Vector2(2f, 26f), new Vector2(220f, 46f));
        tankLabel = Text("Label", tankRoot, 38f, Color.white);
        Place(tankLabel.rectTransform, new Vector2(0f, 28f), new Vector2(220f, 46f));

        // area progress
        areaRoot = Rect("Area", canvasRect, new Vector2(360f, 100f));
        areaRoot.anchorMin = areaRoot.anchorMax = new Vector2(0.5f, 1f);
        areaRoot.pivot = new Vector2(0.5f, 1f);
        areaRoot.anchoredPosition = new Vector2(0f, -40f);
        var panel = Img("Panel", areaRoot, new Color(0.97f, 0.95f, 0.9f, 0.95f));
        Stretch(panel.rectTransform, 0f);
        areaTitle = Text("Title", areaRoot, 30f, new Color(0.25f, 0.22f, 0.3f));
        Place(areaTitle.rectTransform, new Vector2(0f, 22f), new Vector2(340f, 40f));
        var areaBg = Img("Bar", areaRoot, new Color(0.18f, 0.16f, 0.2f));
        Place(areaBg.rectTransform, new Vector2(0f, -20f), new Vector2(320f, 30f));
        areaFill = Stretch(Img("Fill", areaBg.rectTransform, Blue).rectTransform, 3f);
        areaPct = Text("Pct", areaBg.rectTransform, 24f, Color.white);
        Stretch(areaPct.rectTransform, 0f);

        tankRoot.gameObject.SetActive(false);
        areaRoot.gameObject.SetActive(false);
    }

    static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        return rt;
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    /// Fills the parent (minus a margin); the fill amount is set later through anchorMax.x.
    static RectTransform Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        return rt;
    }

    Image Img(string name, Transform parent, Color c)
    {
        var img = Rect(name, parent, Vector2.one * 10f).gameObject.AddComponent<Image>();
        img.sprite = rounded;
        img.type = Image.Type.Sliced;
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text Text(string name, Transform parent, float size, Color c)
    {
        var t = Rect(name, parent, Vector2.one * 10f).gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = c;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    Sprite MakeRounded()
    {
        const int N = 32, R = 12;
        roundedTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SandHUDRounded" };
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float cx = Mathf.Clamp(x + 0.5f, R, N - R), cy = Mathf.Clamp(y + 0.5f, R, N - R);
            float d = new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude;
            roundedTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(R - d + 0.5f)));
        }
        roundedTex.Apply();
        return Sprite.Create(roundedTex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
    }
}
