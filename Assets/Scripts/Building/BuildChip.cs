using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Small building button in the build-mode bottom bar: glyph, name, cost. Tap = place this building instead.
/// Selected = orange; not affordable = faded with the cost in red.
public class BuildChip : MonoBehaviour
{
    public Button button;
    public Image background;
    public Image icon;
    public TMP_Text glyph, nameText, infoText, costText;
    public CanvasGroup group;

    Action onTap;
    BuildingDef def;
    public BuildingDef Def => def;

    void Awake()
    {
        if (button != null) button.onClick.AddListener(() => onTap?.Invoke());
    }

    public void Setup(BuildingDef d, Action tap)
    {
        def = d;
        onTap = tap;
        if (icon != null) icon.color = d.color;
        if (glyph != null) glyph.text = d.glyph;
        if (nameText != null) nameText.text = d.displayName.ToUpper();
        var fp = d.Footprint;
        if (infoText != null) infoText.text = fp != null ? fp.size.x + "×" + fp.size.y : "";
    }

    public void SetState(bool selected, bool affordable, bool free)
    {
        if (background != null) background.color = selected ? WorkshopTheme.Orange : WorkshopTheme.Panel;
        if (nameText != null) nameText.color = selected ? WorkshopTheme.DarkText : Color.white;
        if (infoText != null) infoText.color = selected ? WorkshopTheme.DarkText : WorkshopTheme.Muted;
        if (costText != null)
        {
            costText.text = free ? "FREE" : def != null ? def.cost + " MATS" : "";
            costText.color = selected ? WorkshopTheme.DarkText : affordable ? WorkshopTheme.Yellow : WorkshopTheme.Red;
        }
        if (group != null) group.alpha = affordable || selected ? 1f : 0.5f;
    }
}
