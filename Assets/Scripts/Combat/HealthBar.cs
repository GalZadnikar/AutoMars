using UnityEngine;

/// Small billboard HP bar over a unit (same look as BatteryBar). Built at runtime by Health.
/// Hidden at full HP; shows after damage and fades out a few seconds after the unit is full again.
public class HealthBar : MonoBehaviour
{
    public float height = 0.13f;
    [Tooltip("Seconds the bar stays after the last hit when HP is full again")]
    public float linger = 2.5f;

    Health hp;
    SpriteRenderer bg, fill, lag;
    float width, k, lagValue = 1f, flash;

    static Sprite white;
    static readonly Color Green = new Color(0.45f, 0.9f, 0.4f);
    static readonly Color Yellow = new Color(1f, 0.82f, 0.25f);
    static readonly Color Red = new Color(1f, 0.32f, 0.25f);
    static readonly Color EnemyRed = new Color(1f, 0.25f, 0.2f);

    public static Sprite White
    {
        get
        {
            if (white == null)
                white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return white;
        }
    }

    public static HealthBar Create(Health h)
    {
        var go = new GameObject("HealthBar");
        go.transform.SetParent(h.transform, false);
        go.transform.localPosition = Vector3.up * h.barHeight;
        go.AddComponent<Billboard>();
        var bar = go.AddComponent<HealthBar>();
        bar.hp = h;
        bar.width = h.barWidth;
        bar.bg = bar.Part("Bg", 0, 0f);
        bar.lag = bar.Part("Lag", 1, -0.005f);
        bar.fill = bar.Part("Fill", 2, -0.01f);
        h.OnDamaged += (a, s) => bar.flash = 1f;
        bar.SetVisible(false);
        return bar;
    }

    SpriteRenderer Part(string name, int order, float z)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, z);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = White;
        r.sortingOrder = 1 + order;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    void SetVisible(bool on)
    {
        if (bg.enabled == on) return;
        bg.enabled = lag.enabled = fill.enabled = on;
    }

    void LateUpdate()
    {
        if (hp == null) { Destroy(gameObject); return; }
        float v = hp.Fraction;
        bool show = hp.showBar && !hp.IsDead && (!hp.IsFull || hp.SinceDamaged < linger);
        if (hp.IsDead && hp.showBar) show = true;   // down units keep an empty bar (repair progress fills it)
        k = Mathf.MoveTowards(k, show ? 1f : 0f, Time.deltaTime * 6f);
        SetVisible(k > 0.01f);
        if (k <= 0.01f) { lagValue = v; return; }

        // white "lag" chunk that drains after the real value (shows how much a hit took)
        lagValue = v > lagValue ? v : Mathf.MoveTowards(lagValue, v, Time.deltaTime * 0.8f);
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 5f);

        Size(fill.transform, Mathf.Max(v, 0.001f));
        Size(lag.transform, Mathf.Max(lagValue, 0.001f));
        bg.transform.localScale = new Vector3(width + 0.08f, height + 0.08f, 1f);

        Color c = hp.team == Team.Enemy ? EnemyRed : v > 0.5f ? Green : v > 0.25f ? Yellow : Red;
        c = Color.Lerp(c, Color.white, flash * 0.7f);
        c.a = k;
        fill.color = c;
        lag.color = new Color(1f, 0.95f, 0.85f, 0.8f * k);
        bg.color = new Color(0.08f, 0.06f, 0.05f, 0.75f * k);
    }

    void Size(Transform t, float f)
    {
        t.localScale = new Vector3(width * f, height, 1f);
        var p = t.localPosition;
        t.localPosition = new Vector3(-width * (1f - f) * 0.5f, 0f, p.z);
    }
}
