using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// One battery in the Workshop battery list. Tap to select (stats preview), then EQUIP.
public class BatteryCard : MonoBehaviour
{
    public Button button;
    public Image stripe;
    public Image icon;
    public Image selectedFrame;
    public TMP_Text nameText;
    public TMP_Text runtimeText;

    Action onTap;
    Sprite batterySprite;

    void Awake()
    {
        if (button != null) button.onClick.AddListener(() => onTap?.Invoke());
        if (icon != null) batterySprite = icon.sprite;   // remember the default icon (cards are reused for disks)
    }

    public void Setup(BatteryItem item, Action tap)
    {
        onTap = tap;
        var c = item.Color;
        if (batterySprite != null) icon.sprite = batterySprite;
        stripe.color = c;
        icon.color = c;
        nameText.text = item.displayName.ToUpper();
        nameText.color = c;
        runtimeText.text = Stats.Clock(item.runtimeSeconds) + "  RUNTIME";
    }

    /// Same card for other slot items (e.g. Master Disk programs).
    public void SetupCustom(string title, string subtitle, Color color, Sprite iconSprite, Action tap)
    {
        onTap = tap;
        stripe.color = color;
        if (iconSprite != null) icon.sprite = iconSprite;
        icon.color = color;
        nameText.text = title.ToUpper();
        nameText.color = color;
        runtimeText.text = subtitle;
    }

    public void SetSelected(bool on)
    {
        if (selectedFrame != null) selectedFrame.enabled = on;
    }
}
