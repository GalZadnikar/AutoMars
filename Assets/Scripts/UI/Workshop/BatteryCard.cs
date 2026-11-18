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

    void Awake()
    {
        if (button != null) button.onClick.AddListener(() => onTap?.Invoke());
    }

    public void Setup(BatteryItem item, Action tap)
    {
        onTap = tap;
        var c = item.Color;
        stripe.color = c;
        icon.color = c;
        nameText.text = item.displayName.ToUpper();
        nameText.color = c;
        runtimeText.text = Stats.Clock(item.runtimeSeconds) + "  RUNTIME";
    }

    public void SetSelected(bool on)
    {
        if (selectedFrame != null) selectedFrame.enabled = on;
    }
}
