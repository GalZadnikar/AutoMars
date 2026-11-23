using TMPro;
using UnityEngine;

/// One counter (coins, gems, sand, stone ...): icon + number. MobileHUD refreshes it every frame.
/// With hideWhenZero the whole row disappears while the player has none of it.
public class ResourceRow : MonoBehaviour
{
    public ResourceKind kind;
    public TMP_Text valueText;
    public bool hideWhenZero;

    int shown = int.MinValue;
    float punch;

    public void Refresh(Economy eco)
    {
        int v = eco != null ? eco.Get(kind) : 0;
        if (hideWhenZero)
        {
            bool on = v > 0;
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
        }
        if (v != shown)
        {
            if (shown != int.MinValue && v > shown) punch = 1f;
            shown = v;
            if (valueText != null) valueText.text = Format(v);
        }
        if (punch > 0f && valueText != null)
        {
            punch = Mathf.MoveTowards(punch, 0f, Time.unscaledDeltaTime * 6f);
            valueText.transform.localScale = Vector3.one * (1f + 0.25f * punch);
        }
    }

    public static string Format(int v)
    {
        if (v >= 1000000) return (v / 1000000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M";
        if (v >= 10000) return (v / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K";
        return v.ToString();
    }
}
