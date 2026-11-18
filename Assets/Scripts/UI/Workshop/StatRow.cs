using TMPro;
using UnityEngine;

/// "Move Speed ........ 4.4 m/s  +0.9" line in the Workshop stats panel.
public class StatRow : MonoBehaviour
{
    public TMP_Text label;
    public TMP_Text value;
    public TMP_Text delta;

    public void Set(string labelText, string valueText, string deltaText, bool deltaGood)
    {
        label.text = labelText;
        value.text = valueText;
        delta.text = deltaText;
        delta.color = deltaGood ? WorkshopTheme.Green : WorkshopTheme.Red;
    }
}
