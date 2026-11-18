using TMPro;
using UnityEngine;

/// "Move Speed ........ 4.4 m/s  +0.9" line in the Workshop stats panel.
public class StatRow : MonoBehaviour
{
    public TMP_Text label;
    public TMP_Text value;
    public TMP_Text delta;

    void Awake()
    {
        if (delta != null)
        {
            delta.textWrappingMode = TextWrappingModes.NoWrap;
            delta.enableAutoSizing = true;
            delta.fontSizeMin = 14f;
            delta.fontSizeMax = delta.fontSize;
        }
    }

    public void Set(string labelText, string valueText, string deltaText, bool deltaGood)
    {
        label.text = labelText;
        value.text = valueText;
        delta.text = deltaText;
        delta.color = deltaGood ? WorkshopTheme.Green : WorkshopTheme.Red;
    }
}
