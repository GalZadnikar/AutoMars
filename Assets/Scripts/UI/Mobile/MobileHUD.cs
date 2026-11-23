using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Portrait (1080x1920) gameplay HUD:
///  - top right: coins + gems, and under them the collected materials (a material with 0 is hidden)
///  - left: the player's tools (tap one for its info)
///  - a backpack button that opens the inventory (tools, materials, player / XP)
///  - at the base: the tool upgrade panel appears by itself while the player stands in the base zone
/// Built by Tools > AutoMars > Mobile UI > Build Mobile UI; everything here is plain scene objects you can restyle.
public class MobileHUD : MonoBehaviour
{
    public static MobileHUD Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [Header("Counters (HUD + inventory)")]
    public ResourceRow[] rows;
    public TMP_Text xpText;
    public Button[] shopButtons;

    [Header("Base upgrade panel (shown while standing at a base)")]
    public RectTransform basePanel;

    [Header("Inventory")]
    public GameObject inventory;
    public Button inventoryButton, inventoryClose, inventoryBackdrop;

    [Header("Tool info popup")]
    public ToolInfoPopup toolInfo;

    Transform player;
    float baseT;
    int shownXp = -1;

    void Awake()
    {
        Instance = this;
        if (inventoryButton != null) inventoryButton.onClick.AddListener(OpenInventory);
        if (inventoryClose != null) inventoryClose.onClick.AddListener(CloseInventory);
        if (inventoryBackdrop != null) inventoryBackdrop.onClick.AddListener(CloseInventory);
        if (shopButtons != null)
            foreach (var b in shopButtons)
                if (b != null) b.onClick.AddListener(() => GameToast.Show("SHOP COMING SOON", Color.white));
        if (inventory != null) inventory.SetActive(false);
        if (toolInfo != null) toolInfo.gameObject.SetActive(false);
        if (basePanel != null) basePanel.gameObject.SetActive(false);
    }

    void Start()
    {
        var pc = FindFirstObjectByType<JoystickPlayerController>();
        if (pc != null) player = pc.transform;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        var eco = Economy.Instance;
        if (rows != null)
            foreach (var r in rows)
                if (r != null) r.Refresh(eco);

        int xp = eco != null ? eco.xp : 0;
        if (xpText != null && xp != shownXp) { shownXp = xp; xpText.text = "XP  " + xp; }

        UpdateBasePanel();
    }

    // ------------------------------------------------------------------ base panel

    void UpdateBasePanel()
    {
        if (basePanel == null) return;
        bool show = false;
        if (player != null && !UIState.ModalOpen && !UIState.BuildMode && !PlayerHealth.KnockedOut)
            foreach (var h in HomeBase.All)
                if (h != null && h.InRange(player.position)) { show = true; break; }

        if (show && !basePanel.gameObject.activeSelf) { basePanel.gameObject.SetActive(true); baseT = 0f; }
        else if (!show && basePanel.gameObject.activeSelf) basePanel.gameObject.SetActive(false);

        if (show && baseT < 1f)
        {
            baseT = Mathf.Min(1f, baseT + Time.unscaledDeltaTime * 6f);
            float e = 1f - (1f - baseT) * (1f - baseT);
            basePanel.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, e + Mathf.Sin(baseT * Mathf.PI) * 0.06f);
        }
    }

    // ------------------------------------------------------------------ inventory + tool info

    public void OpenInventory()
    {
        if (inventory == null || (UIState.ModalOpen && !IsMineOpen)) return;
        inventory.SetActive(true);
        inventory.transform.SetAsLastSibling();
        if (toolInfo != null) toolInfo.transform.SetAsLastSibling();
        UIState.ModalOpen = true;
    }

    public void CloseInventory()
    {
        if (inventory != null) inventory.SetActive(false);
        UIState.ModalOpen = toolInfo != null && toolInfo.IsOpen;
    }

    /// Tap on a tool: its level, numbers and what the next level costs.
    public void ShowTool(ToolKind kind)
    {
        if (toolInfo == null || (UIState.ModalOpen && !IsMineOpen)) return;
        toolInfo.Show(kind, 0);
        UIState.ModalOpen = true;
    }

    /// After an upgrade at the base: "UPGRADED", old > new numbers.
    public void ShowUpgraded(ToolKind kind, int fromLevel)
    {
        if (toolInfo == null) return;
        toolInfo.Show(kind, fromLevel);
        UIState.ModalOpen = true;
    }

    /// Called by the tool popup when it closes.
    public void ToolInfoClosed()
    {
        UIState.ModalOpen = inventory != null && inventory.activeSelf;
    }

    bool IsMineOpen => (inventory != null && inventory.activeSelf) || (toolInfo != null && toolInfo.IsOpen);
}
