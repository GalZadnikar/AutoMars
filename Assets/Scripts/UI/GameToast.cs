using TMPro;
using UnityEngine;

/// Short message at the top of the screen ("+ RARE BATTERY", "BATTERY SLOTS FULL").
/// Creates itself under the UI Canvas the first time it's used.
public class GameToast : MonoBehaviour
{
    static GameToast instance;

    TMP_Text text;
    float t;
    const float Duration = 2.2f;

    public static void Show(string message, Color color)
    {
        if (instance == null)
        {
            var canvas = GameObject.Find("UI Canvas");
            if (canvas == null) return;
            var go = new GameObject("GameToast", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -235f);
            rt.sizeDelta = new Vector2(1200f, 80f);
            instance = go.AddComponent<GameToast>();
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 46; tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.outlineWidth = 0.25f;
            tmp.outlineColor = new Color32(20, 15, 10, 255);
            instance.text = tmp;
        }
        instance.transform.SetAsLastSibling();
        instance.text.text = message;
        instance.text.color = color;
        instance.t = Duration;
    }

    void Update()
    {
        t = Mathf.Max(0f, t - Time.unscaledDeltaTime);
        var c = text.color;
        c.a = Mathf.Clamp01(t / 0.35f);
        text.color = c;
        float pop = Mathf.Clamp01((t - (Duration - 0.2f)) / 0.2f);
        transform.localScale = Vector3.one * (1f + 0.2f * pop);
    }
}
