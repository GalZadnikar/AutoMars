using UnityEngine;

/// Keeps a fixed-size design (e.g. 1920x1080) fully visible: scales it to fit the screen's safe area
/// (notches, home bar) and centres it there. Children can use plain pixel positions.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class FitToSafeArea : MonoBehaviour
{
    public Vector2 designSize = new Vector2(1920f, 1080f);
    [Tooltip("1 = never bigger than designed (popups). Higher lets full screens grow on big displays.")]
    public float maxScale = 10f;
    [Tooltip("Space kept free around the content, in canvas units")]
    public float margin = 0f;

    void OnEnable() => Apply();
    void LateUpdate() => Apply();

    public void Apply()
    {
        var rt = (RectTransform)transform;
        var parent = rt.parent as RectTransform;
        if (parent == null) return;

        Rect pr = parent.rect;
        float sw = Screen.width, sh = Screen.height;
        Rect sa = sw > 0f && sh > 0f ? Screen.safeArea : new Rect(0f, 0f, 1f, 1f);
        if (sw <= 0f || sh <= 0f) { sw = 1f; sh = 1f; }

        float xMin = pr.xMin + pr.width * (sa.xMin / sw);
        float xMax = pr.xMin + pr.width * (sa.xMax / sw);
        float yMin = pr.yMin + pr.height * (sa.yMin / sh);
        float yMax = pr.yMin + pr.height * (sa.yMax / sh);
        float w = xMax - xMin, h = yMax - yMin;
        if (w <= 0f || h <= 0f) return;

        float s = Mathf.Min(Mathf.Min((w - 2f * margin) / designSize.x, (h - 2f * margin) / designSize.y), maxScale);
        if (s <= 0f) return;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = designSize;
        rt.localScale = new Vector3(s, s, 1f);
        rt.anchoredPosition = new Vector2((xMin + xMax) * 0.5f - pr.center.x, (yMin + yMax) * 0.5f - pr.center.y);
    }
}
