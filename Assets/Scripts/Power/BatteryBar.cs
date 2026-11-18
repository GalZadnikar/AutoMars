using UnityEngine;

/// Small battery bar above a bot. Only shows when it matters (low, charging, dead)
/// or when the bot is one of the few near the player (its level tag is showing).
public class BatteryBar : MonoBehaviour
{
    public SpriteRenderer background;
    public SpriteRenderer fill;
    public float width = 1.5f;
    public float height = 0.16f;
    [Range(0f, 1f)] public float showBelow = 0.35f;

    ScoutBot bot;
    float k;

    static readonly Color Green = new Color(0.45f, 0.9f, 0.4f);
    static readonly Color Yellow = new Color(1f, 0.82f, 0.25f);
    static readonly Color Red = new Color(1f, 0.32f, 0.25f);
    static readonly Color Cyan = new Color(0.45f, 0.95f, 1f);

    void Awake()
    {
        bot = GetComponentInParent<ScoutBot>();
        SetVisible(false);
    }

    void SetVisible(bool on)
    {
        if (background != null && background.enabled != on) background.enabled = on;
        if (fill != null && fill.enabled != on) fill.enabled = on;
    }

    void LateUpdate()
    {
        if (bot == null) return;
        float v = bot.Battery01;
        bool show = v < showBelow || bot.SeekingCharge || bot.IsDead || (bot.levelTag != null && bot.levelTag.Visible);
        k = Mathf.MoveTowards(k, show ? 1f : 0f, Time.deltaTime * 6f);
        SetVisible(k > 0.01f);
        if (k <= 0.01f) return;

        float shown = Mathf.Max(v, 0.02f);
        fill.transform.localScale = new Vector3(width * shown, height, 1f);
        fill.transform.localPosition = new Vector3(-width * (1f - shown) * 0.5f, 0f, -0.01f);
        background.transform.localScale = new Vector3(width + 0.08f, height + 0.08f, 1f);

        Color c = v > 0.5f ? Green : v > 0.2f ? Yellow : Red;
        if (bot.IsCharging) c = Color.Lerp(Cyan, Color.white, Mathf.PingPong(Time.time * 2f, 1f) * 0.5f);
        else if (bot.IsDead) c = Mathf.Repeat(Time.time, 0.8f) < 0.4f ? Red : new Color(0.4f, 0.1f, 0.1f);
        c.a = k;
        fill.color = c;
        background.color = new Color(0.08f, 0.06f, 0.05f, 0.75f * k);
    }
}
