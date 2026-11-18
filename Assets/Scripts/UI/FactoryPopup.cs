using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// "How many bots?" popup with - / + / MAX and Confirm.
public class FactoryPopup : MonoBehaviour
{
    public TMP_Text titleText, infoText, countText, totalText, haveText, queueText, confirmText;
    public Button minusButton, plusButton, maxButton, confirmButton, closeButton;
    public int maxPerOrder = 20;

    BotFactory factory;
    int count;
    bool wired;
    float refreshTimer;

    int Max => factory == null ? 0 : Mathf.Min(maxPerOrder, factory.MaxAffordable);

    void Wire()
    {
        if (wired) return;
        wired = true;
        minusButton.onClick.AddListener(() => Change(-1));
        plusButton.onClick.AddListener(() => Change(+1));
        maxButton.onClick.AddListener(() => { count = Max; Refresh(); });
        confirmButton.onClick.AddListener(Confirm);
        closeButton.onClick.AddListener(Close);
    }

    public void Open(BotFactory f)
    {
        Wire();
        factory = f;
        count = Mathf.Min(1, Max);
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        UIState.ModalOpen = true;
        Refresh();
    }

    public void Close()
    {
        gameObject.SetActive(false);
        UIState.ModalOpen = false;
        factory = null;
    }

    void Change(int delta)
    {
        count = Mathf.Clamp(count + delta, 0, Max);
        Refresh();
    }

    void Confirm()
    {
        if (factory != null && factory.Order(count)) Close();
        else Refresh();
    }

    void Update()
    {
        // materials can change while the popup is open (base still recycling)
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f) { refreshTimer = 0.25f; Refresh(); }
    }

    void Refresh()
    {
        if (factory == null) return;
        count = Mathf.Clamp(count, 0, Max);
        int have = Economy.Instance != null ? Economy.Instance.materials : 0;
        int lvl = factory.Upgrades != null ? factory.Upgrades.Level : 1;
        infoText.text = "Scout Bot Lv " + lvl + "  ·  " + factory.CostPerBot + " materials  ·  " + Mathf.RoundToInt(factory.BuildTime) + "s each" +
            (factory.Upgrades != null && factory.Upgrades.AutoProgram != null ? "  ·  auto: " + factory.Upgrades.AutoProgram.shortName : "");
        countText.text = count.ToString();
        totalText.text = "Total: " + (count * factory.CostPerBot) + " materials";
        haveText.text = "You have: " + have;
        queueText.text = factory.Queued > 0
            ? "In production: " + factory.Queued + "  (next in " + Mathf.CeilToInt(factory.TimeLeft) + "s)"
            : "";
        minusButton.interactable = count > 0;
        plusButton.interactable = count < Max;
        maxButton.interactable = Max > 0;
        confirmButton.interactable = count > 0;
        confirmText.text = Max == 0 ? "NOT ENOUGH MATERIALS" : count == 1 ? "BUILD 1 BOT" : "BUILD " + count + " BOTS";
    }
}
