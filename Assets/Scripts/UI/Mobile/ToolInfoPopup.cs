using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Tool card. Two uses: tap a tool (its numbers, and in green what the next level would give + its cost),
/// and right after an upgrade at the base ("UPGRADED", old > new).
public class ToolInfoPopup : MonoBehaviour
{
    public RectTransform panel;
    public TMP_Text titleText, levelText, descText, nextText;
    public Image icon;
    public TMP_Text[] statLabels, statValues;
    public Button closeButton, okButton, backdrop;

    const string Good = "#9CFF6B";
    float t;
    bool hooked;

    public bool IsOpen => gameObject.activeSelf;

    void Hook()
    {
        if (hooked) return;
        hooked = true;
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (okButton != null) okButton.onClick.AddListener(Close);
        if (backdrop != null) backdrop.onClick.AddListener(Close);
    }

    /// fromLevel > 0: the tool was just upgraded from that level.
    public void Show(ToolKind kind, int fromLevel)
    {
        Hook();
        var tp = ToolProgress.Instance;
        if (tp == null) return;
        int lv = tp.Level(kind);
        bool upgraded = fromLevel > 0 && fromLevel < lv;
        bool max = tp.IsMax(kind);

        if (titleText != null) titleText.text = ToolProgress.Name(kind) + (upgraded ? " UPGRADED" : "");
        if (levelText != null) levelText.text = "LVL " + lv + (max ? "  (MAX)" : "");
        if (icon != null) { icon.sprite = tp.Icon(kind); icon.enabled = icon.sprite != null; }
        if (descText != null) descText.text = ToolProgress.Description(kind);

        // upgraded: old > new.   info: now > next level (if there is one)
        var a = tp.Stats(kind, upgraded ? fromLevel : lv);
        var b = upgraded ? tp.Stats(kind, lv) : max ? null : tp.Stats(kind, lv + 1);
        for (int i = 0; i < statLabels.Length; i++)
        {
            bool on = i < a.Count;
            if (statLabels[i] != null) statLabels[i].transform.parent.gameObject.SetActive(on);
            if (!on) continue;
            statLabels[i].text = a[i].label;
            bool changes = b != null && b[i].text != a[i].text;
            statValues[i].text = changes ? a[i].text + " <color=" + Good + ">> " + b[i].text + "</color>" : (upgraded ? b[i].text : a[i].text);
        }

        if (nextText != null)
        {
            if (upgraded) nextText.text = "";
            else if (max) nextText.text = "Fully upgraded";
            else
            {
                tp.NextCost(kind, out int sand, out int stone);
                string cost = sand > 0 ? sand + " SAND" : "";
                if (stone > 0) cost += (cost.Length > 0 ? " + " : "") + stone + " STONE";
                nextText.text = "Next level: <color=#FFD54A>" + cost + "</color>\nUpgrade at the base";
            }
        }

        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        t = 0f;
        if (panel != null) panel.localScale = Vector3.one * 0.85f;
    }

    public void Close()
    {
        gameObject.SetActive(false);
        if (MobileHUD.Instance != null) MobileHUD.Instance.ToolInfoClosed();
    }

    void Update()
    {
        if (panel == null || t >= 1f) return;
        t = Mathf.Min(1f, t + Time.unscaledDeltaTime * 7f);
        float e = 1f - (1f - t) * (1f - t);
        panel.localScale = Vector3.one * (Mathf.Lerp(0.85f, 1f, e) + Mathf.Sin(t * Mathf.PI) * 0.05f);
    }
}
