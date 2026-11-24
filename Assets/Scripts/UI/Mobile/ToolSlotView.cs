using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// A tool tile (HUD, left side, and in the inventory): icon, "LVL n", a frame around the tool that is in the
/// player's hands and a green arrow when its next level is affordable. Tap = tool info.
public class ToolSlotView : MonoBehaviour
{
    public ToolKind kind;
    public Image icon;
    public TMP_Text levelText;
    [Tooltip("Shown while this tool is the one in the player's hands")]
    public GameObject activeFrame;
    [Tooltip("Shown while the next level can be bought (at the base)")]
    public RectTransform upgradeArrow;
    public Button button;

    PlayerTools tools;
    int shownLevel = -1;
    Vector2 arrowPos;

    void Awake()
    {
        if (button != null) button.onClick.AddListener(() => { if (MobileHUD.Instance != null) MobileHUD.Instance.ShowTool(kind); });
        if (upgradeArrow != null) arrowPos = upgradeArrow.anchoredPosition;
    }

    void OnEnable() { shownLevel = -1; }

    void Update()
    {
        var tp = ToolProgress.Instance;
        if (tp == null) return;
        if (tools == null) tools = tp.GetComponent<PlayerTools>();

        int lv = tp.Level(kind);
        if (lv != shownLevel)
        {
            shownLevel = lv;
            if (levelText != null) levelText.text = "LVL " + lv;
            if (icon != null && tp.Icon(kind) != null) icon.sprite = tp.Icon(kind);
        }

        if (activeFrame != null)
        {
            bool inHands = tools != null && (kind == ToolKind.Pickaxe ? tools.tool == PlayerTool.Pickaxe : tools.tool == PlayerTool.Shovel || tools.tool == PlayerTool.Gun);
            if (activeFrame.activeSelf != inHands) activeFrame.SetActive(inHands);
        }

        if (upgradeArrow != null)
        {
            bool can = tp.CanAfford(kind);
            if (upgradeArrow.gameObject.activeSelf != can) upgradeArrow.gameObject.SetActive(can);
            if (can) upgradeArrow.anchoredPosition = arrowPos + Vector2.up * (Mathf.Sin(Time.unscaledTime * 6f) * 5f);
        }
    }
}
