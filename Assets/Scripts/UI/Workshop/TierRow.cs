using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// One tier row of the Workshop tree: badge hex on the left (tier / unlock level), perk hexes on the right.
public class TierRow : MonoBehaviour
{
    public Image background;
    public Image badge;
    public Image badgeRing;
    public TMP_Text badgeText;
    public Image line;
    public RectTransform options;

    bool pulse;

    public void SetState(int tierIndex, bool unlocked, bool needsPick, int unlockLevel)
    {
        badge.color = unlocked ? WorkshopTheme.White : WorkshopTheme.LockedFill;
        badgeText.text = unlocked ? "T" + (tierIndex + 1) : "LV " + unlockLevel;
        badgeText.color = unlocked ? WorkshopTheme.DarkText : WorkshopTheme.LockedGlyph;

        var o = WorkshopTheme.Orange;
        line.color = unlocked ? new Color(o.r, o.g, o.b, 0.55f) : WorkshopTheme.LockedFill;

        var p = WorkshopTheme.Panel;
        background.color = needsPick ? new Color(o.r, o.g, o.b, 0.14f)
                         : unlocked ? new Color(p.r, p.g, p.b, 0.85f)
                         : new Color(p.r, p.g, p.b, 0.35f);
        pulse = needsPick;
        if (!pulse && badgeRing != null) { badgeRing.enabled = false; badgeRing.transform.localScale = Vector3.one; }
    }

    void Update()
    {
        if (!pulse || badgeRing == null) return;
        badgeRing.enabled = true;
        float s = Mathf.Sin(Time.unscaledTime * 5f);
        badgeRing.transform.localScale = Vector3.one * (1.08f + 0.06f * s);
        var c = WorkshopTheme.Orange;
        c.a = 0.65f + 0.35f * s;
        badgeRing.color = c;
    }
}
