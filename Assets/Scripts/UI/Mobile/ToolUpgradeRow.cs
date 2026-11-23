using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// One row of the base panel: a tool, what the next level gives, what it costs (have / need) and UPGRADE.
public class ToolUpgradeRow : MonoBehaviour
{
    public ToolKind kind;
    public Image icon;
    public TMP_Text titleText, statText, slotLevelText;
    public GameObject sandChip, stoneChip;
    public TMP_Text sandText, stoneText;
    public Button button;
    public Image buttonImage;
    public TMP_Text buttonLabel;
    public Sprite canSprite, cantSprite;

    const string Good = "#9CFF6B", Bad = "#FF6A5A";

    void Awake()
    {
        if (button != null) button.onClick.AddListener(Click);
    }

    void Click()
    {
        var tp = ToolProgress.Instance;
        if (tp == null || tp.IsMax(kind)) return;
        int from = tp.Level(kind);
        if (tp.TryUpgrade(kind))
        {
            if (MobileHUD.Instance != null) MobileHUD.Instance.ShowUpgraded(kind, from);
        }
        else GameToast.Show("NOT ENOUGH MATERIALS", new Color(1f, 0.45f, 0.4f));
    }

    void Update()
    {
        var tp = ToolProgress.Instance;
        if (tp == null) return;
        int lv = tp.Level(kind);
        bool max = tp.IsMax(kind);
        if (icon != null && tp.Icon(kind) != null) icon.sprite = tp.Icon(kind);
        if (slotLevelText != null) slotLevelText.text = "LVL " + lv;
        if (titleText != null)
            titleText.text = ToolProgress.Name(kind) + (max ? "  <color=#FFD54A>MAX</color>" : "  <size=80%><color=#FFFFFFAA>LVL " + lv + "</color> <color=" + Good + ">> " + (lv + 1) + "</color></size>");

        if (statText != null)
        {
            if (max) statText.text = "FULLY UPGRADED";
            else
            {
                var a = tp.Stats(kind, lv);
                var b = tp.Stats(kind, lv + 1);
                string line = "";
                for (int i = 0; i < a.Count; i++)
                    if (a[i].text != b[i].text) { line = a[i].label + "  " + a[i].text + " <color=" + Good + ">> " + b[i].text + "</color>"; break; }
                statText.text = line;
            }
        }

        tp.NextCost(kind, out int sand, out int stone);
        var eco = Economy.Instance;
        Chip(sandChip, sandText, sand, eco != null ? eco.Get(ResourceKind.Sand) : 0, max);
        Chip(stoneChip, stoneText, stone, eco != null ? eco.Get(ResourceKind.Stone) : 0, max);

        bool can = tp.CanAfford(kind);
        if (buttonImage != null && canSprite != null && cantSprite != null) buttonImage.sprite = can ? canSprite : cantSprite;
        if (buttonLabel != null) buttonLabel.text = max ? "MAX" : "UPGRADE";
        if (button != null) button.interactable = !max;
    }

    static void Chip(GameObject chip, TMP_Text text, int need, int have, bool max)
    {
        if (chip == null) return;
        bool on = !max && need > 0;
        if (chip.activeSelf != on) chip.SetActive(on);
        if (on && text != null)
            text.text = "<color=" + (have >= need ? Good : Bad) + ">" + ResourceRow.Format(have) + "</color>/" + need;
    }
}
