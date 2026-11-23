using UnityEngine;

/// Shows the floating "BUILD BOTS" / "PROGRAM" buttons (and "BASE" at the home base) when the player is close enough.
public class PlayerInteractions : MonoBehaviour
{
    public WorldButton factoryButton;
    public WorldButton botButton;
    public FactoryPopup factoryPopup;
    public BotProgramPopup botPopup;
    [Header("Workshop (upgrade tree)")]
    public WorldButton upgradeButton;
    public WorkshopScreen workshop;
    [Tooltip("Screen offset so BUILD BOTS and UPGRADE sit side by side")]
    public Vector2 factoryButtonSpread = new Vector2(178f, 0f);

    [Header("Base (opens the build popup)")]
    [Tooltip("Looked up automatically when empty")]
    public BuildMode buildMenu;
    [Tooltip("Off = no floating BASE button (the mobile HUD shows its own upgrade panel at the base)")]
    public bool showBaseButton = true;
    public Vector3 baseButtonOffset = new Vector3(0f, 5.4f, 0f);

    public float botRadius = 3f;
    public Vector3 factoryButtonOffset = new Vector3(0f, 5.2f, 0f);
    public Vector3 botButtonOffset = new Vector3(0f, 3.8f, 0f);

    BotFactory shownFactory;
    ScoutBot shownBot;
    TMPro.TMP_Text upgradeLabel;
    int upgradeAlert = -1;
    WorldButton baseButton;
    bool baseShown;
    HomeBase shownBase;

    [Header("Trade Post (opens the shop popup)")]
    public Vector3 tradeButtonOffset = new Vector3(0f, 6.2f, 0f);
    WorldButton tradeButton;
    TradePost shownTrade;

    void Update()
    {
        if (UIState.ModalOpen || UIState.BuildMode) { HideAll(); return; }

        // nearest factory whose zone we're standing in (there can be several)
        BotFactory f = null;
        float fd = float.MaxValue;
        foreach (var bf in BotFactory.All)
        {
            if (!bf.InRange(transform.position)) continue;
            float d = (bf.transform.position - transform.position).sqrMagnitude;
            if (d < fd) { fd = d; f = bf; }
        }
        if (f != shownFactory)
        {
            if (shownFactory != null) shownFactory.SetHighlight(false);
            shownFactory = f;
            if (f != null)
            {
                f.SetHighlight(true);
                bool canUpgrade = upgradeButton != null && workshop != null && f.Upgrades != null;
                factoryButton.Show(f.transform, factoryButtonOffset, () => factoryPopup.Open(f), canUpgrade ? -factoryButtonSpread : Vector2.zero);
                var fl = factoryButton.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (fl != null) fl.text = "BUILD " + f.UnitWord + "S";   // BUILD BOTS / BUILD DRONES
                if (canUpgrade) upgradeButton.Show(f.transform, factoryButtonOffset, () => workshop.Open(f), factoryButtonSpread);
                upgradeAlert = -1;
            }
            else
            {
                factoryButton.Hide();
                if (upgradeButton != null) upgradeButton.Hide();
            }
        }
        UpdateUpgradeAlert();
        UpdateBaseButton();
        UpdateTradeButton();

        ScoutBot best = null;
        float bestD = botRadius * botRadius;
        Vector3 me = transform.position;
        foreach (var b in ScoutBot.All)
        {
            Vector3 d = b.transform.position - me; d.y = 0f;
            float m = d.sqrMagnitude;
            if (m < bestD) { bestD = m; best = b; }
        }
        if (best != shownBot)
        {
            shownBot = best;
            if (best != null) botButton.Show(best.transform, botButtonOffset + Vector3.up * best.ButtonHeight, () => botPopup.Open(best));
            else botButton.Hide();
        }
    }

    void HideAll()
    {
        if (shownFactory != null) shownFactory.SetHighlight(false);
        shownFactory = null;
        shownBot = null;
        factoryButton.Hide();
        botButton.Hide();
        if (upgradeButton != null) upgradeButton.Hide();
        if (baseButton != null) baseButton.Hide();
        baseShown = false;
        shownBase = null;
        if (tradeButton != null) tradeButton.Hide();
        if (shownTrade != null) shownTrade.SetHighlight(false);
        shownTrade = null;
    }

    // ------------------------------------------------------------------ TRADE button (Trade Post shop)

    void UpdateTradeButton()
    {
        var tp = TradePost.InRangeOf(transform.position);
        if (tp == shownTrade) return;
        if (shownTrade != null) shownTrade.SetHighlight(false);
        shownTrade = tp;
        if (tp == null) { if (tradeButton != null) tradeButton.Hide(); return; }
        if (tradeButton == null)
        {
            if (factoryButton == null) return;
            var go = Instantiate(factoryButton.gameObject, factoryButton.transform.parent);   // same look as BUILD BOTS
            go.name = "TradeButton";
            go.SetActive(false);
            tradeButton = go.GetComponent<WorldButton>();
            var label = go.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label != null) label.text = "TRADE";
        }
        tp.SetHighlight(true);
        tradeButton.Show(tp.transform, tradeButtonOffset, () => TradePostPopup.Open(tp));
    }

    // ------------------------------------------------------------------ BASE button

    // BASE button at any Base (the home base and every one you build): same popup everywhere
    // every Base gets the BASE button: the nearest one whose zone the player is standing in
    HomeBase BaseInRange()
    {
        HomeBase best = null;
        float bestD = float.MaxValue;
        foreach (var h in HomeBase.All)
        {
            if (h == null || !h.InRange(transform.position)) continue;
            float d = (h.transform.position - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = h; }
        }
        return best;
    }

    void UpdateBaseButton()
    {
        var hb = showBaseButton ? BaseInRange() : null;
        if (hb == null)
        {
            if (baseShown && baseButton != null) baseButton.Hide();
            baseShown = false;
            shownBase = null;
            return;
        }
        if (baseButton == null) MakeBaseButton();
        if (baseButton == null) return;
        if (baseShown && hb == shownBase) return;
        baseShown = true;
        shownBase = hb;
        baseButton.Show(hb.transform, baseButtonOffset, OpenBase);
    }

    // a copy of the factory button (same look), so no scene setup is needed
    void MakeBaseButton()
    {
        if (factoryButton == null) return;
        var go = Instantiate(factoryButton.gameObject, factoryButton.transform.parent);
        go.name = "BaseButton";
        go.SetActive(false);
        baseButton = go.GetComponent<WorldButton>();
        var label = go.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (label != null) label.text = "BASE";
    }

    void OpenBase()
    {
        if (buildMenu == null) buildMenu = FindFirstObjectByType<BuildMode>(FindObjectsInactive.Include);
        if (buildMenu != null) buildMenu.OpenMenu();
    }

    // "UPGRADE !" when there's a skill point to spend or a level-up is affordable
    void UpdateUpgradeAlert()
    {
        if (shownFactory == null || upgradeButton == null || shownFactory.Upgrades == null) return;
        var u = shownFactory.Upgrades;
        var inv = PlayerInventory.Instance;
        bool batteryWaiting = u.equippedBattery == null && inv != null && inv.batteries.Count > 0;
        bool diskWaiting = u.MasterDiskUnlocked && u.masterDisk == null && inv != null && inv.disks.Exists(d => d != null && d.FitsOn(u.OutputType));
        int alert = u.UnspentPoints > 0 || u.CanAffordLevelUp || batteryWaiting || diskWaiting ? 1 : 0;
        if (alert == upgradeAlert) return;
        upgradeAlert = alert;
        if (upgradeLabel == null) upgradeLabel = upgradeButton.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (upgradeLabel != null) upgradeLabel.text = alert == 1 ? "UPGRADE !" : "UPGRADE";
    }
}
