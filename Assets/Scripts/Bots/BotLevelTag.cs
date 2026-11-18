using TMPro;
using UnityEngine;

/// Floating "[Lv 2]" tag above a robot. Hidden by default; ProximityTags turns it on
/// for the few bots closest to the player so the map stays clean as the fleet grows.
public class BotLevelTag : MonoBehaviour
{
    public TMP_Text text;
    public float fadeSpeed = 7f;

    ScoutBot bot;
    Renderer rend;
    Vector3 baseScale;
    int shownLevel = -1;
    float k;
    bool visible;

    public bool Visible => visible;

    void Awake()
    {
        if (text == null) text = GetComponent<TMP_Text>();
        bot = GetComponentInParent<ScoutBot>();
        rend = GetComponent<Renderer>();
        baseScale = transform.localScale;
        Apply(0f);
    }

    public void SetVisible(bool on)
    {
        visible = on;
        if (on && rend != null) rend.enabled = true;
    }

    void LateUpdate()
    {
        float target = visible ? 1f : 0f;
        if (Mathf.Approximately(k, target)) return;
        if (bot != null && bot.Level != shownLevel)
        {
            shownLevel = bot.Level;
            text.text = "[Lv " + shownLevel + "]";
        }
        k = Mathf.MoveTowards(k, target, Time.deltaTime * fadeSpeed);
        Apply(k);
    }

    void Apply(float t)
    {
        if (text == null) return;
        var c = LevelColor(shownLevel < 0 && bot != null ? bot.Level : shownLevel);
        c.a = t;
        text.color = c;
        float s = t >= 1f ? 1f : Mathf.Lerp(0.5f, 1f, EaseOutBack(t));
        transform.localScale = baseScale * s;
        if (rend != null) rend.enabled = t > 0.001f;
    }

    /// Colour per tech level (grey → green → blue → purple → orange → gold).
    public static Color LevelColor(int level) => level switch
    {
        <= 1 => new Color32(225, 225, 225, 255),
        2 => new Color32(120, 230, 110, 255),
        3 => new Color32(90, 190, 255, 255),
        4 => new Color32(200, 130, 255, 255),
        5 => new Color32(255, 160, 50, 255),
        _ => new Color32(255, 215, 60, 255),
    };

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
