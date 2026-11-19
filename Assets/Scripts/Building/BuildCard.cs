using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// One building in the build catalog: icon, name, size, cost. Tap to start placing it.
public class BuildCard : MonoBehaviour
{
    public Button button;
    public Image icon;
    public TMP_Text glyph, nameText, infoText, costText;
    [Tooltip("Optional: spinning 3D model of the building (building cards only)")]
    public ModelPreview preview;
    [Tooltip("Optional second image tinted with the building colour")]
    public Image badge;

    GameObject previewPrefab;

    Action onTap;

    void Awake()
    {
        if (button != null) button.onClick.AddListener(() => onTap?.Invoke());
    }

    public void Setup(BuildingDef def, Action tap)
    {
        onTap = tap;
        icon.color = def.color;
        if (badge != null) badge.color = def.color;
        glyph.text = def.glyph;
        nameText.text = def.displayName.ToUpper();
        var fp = def.Footprint;
        string size = fp != null ? fp.size.x + "×" + fp.size.y + " tiles" : "";
        if (preview != null) size += "  ·  " + def.buildTime.ToString("0.#") + "s to build";
        infoText.text = size + (string.IsNullOrEmpty(def.description) ? "" : "\n" + def.description);
        previewPrefab = def.prefab;
    }

    /// Spins the building model while the BUILD tab is open; hidden otherwise (its camera stops rendering).
    public void ShowPreview(bool on)
    {
        if (preview == null) return;
        if (on && previewPrefab != null) preview.Show(previewPrefab);
        else preview.Hide();
    }

    /// A card that isn't a building (e.g. the base's PIPE LENGTH upgrade).
    public void SetupCustom(string glyphText, Color color, Action tap)
    {
        onTap = tap;
        icon.color = color;
        glyph.text = glyphText;
    }

    public void SetTexts(string title, string info, string cost, Color costColor)
    {
        nameText.text = title;
        infoText.text = info;
        costText.text = cost;
        costText.color = costColor;
    }

    public void SetCost(int cost, bool affordable)
    {
        costText.text = cost + " MATERIALS";
        costText.color = affordable ? WorkshopTheme.Yellow : WorkshopTheme.Red;
    }
}
