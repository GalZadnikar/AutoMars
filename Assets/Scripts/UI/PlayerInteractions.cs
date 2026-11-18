using UnityEngine;

/// Shows the floating "BUILD BOTS" / "PROGRAM" buttons when the player is close enough.
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

    public float botRadius = 3f;
    public Vector3 factoryButtonOffset = new Vector3(0f, 5.2f, 0f);
    public Vector3 botButtonOffset = new Vector3(0f, 3.8f, 0f);

    BotFactory shownFactory;
    ScoutBot shownBot;
    TMPro.TMP_Text upgradeLabel;
    int upgradeAlert = -1;

    void Update()
    {
        if (UIState.ModalOpen) { HideAll(); return; }

        var f = BotFactory.Instance;
        if (f != null && !f.InRange(transform.position)) f = null;
        if (f != shownFactory)
        {
            if (shownFactory != null) shownFactory.SetHighlight(false);
            shownFactory = f;
            if (f != null)
            {
                f.SetHighlight(true);
                bool canUpgrade = upgradeButton != null && workshop != null && f.Upgrades != null;
                factoryButton.Show(f.transform, factoryButtonOffset, () => factoryPopup.Open(f), canUpgrade ? -factoryButtonSpread : Vector2.zero);
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
            if (best != null) botButton.Show(best.transform, botButtonOffset, () => botPopup.Open(best));
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
    }

    // "UPGRADE !" when there's a skill point to spend or a level-up is affordable
    void UpdateUpgradeAlert()
    {
        if (shownFactory == null || upgradeButton == null || shownFactory.Upgrades == null) return;
        var u = shownFactory.Upgrades;
        var inv = PlayerInventory.Instance;
        bool batteryWaiting = u.equippedBattery == null && inv != null && inv.batteries.Count > 0;
        bool diskWaiting = u.MasterDiskUnlocked && u.masterDisk == null && inv != null && inv.disks.Count > 0;
        int alert = u.UnspentPoints > 0 || u.CanAffordLevelUp || batteryWaiting || diskWaiting ? 1 : 0;
        if (alert == upgradeAlert) return;
        upgradeAlert = alert;
        if (upgradeLabel == null) upgradeLabel = upgradeButton.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (upgradeLabel != null) upgradeLabel.text = alert == 1 ? "UPGRADE !" : "UPGRADE";
    }
}
