using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Bot popup: drag a disk from your inventory into the bot's program slot (or tap it), then Confirm.
/// Changes are only applied on Confirm; X cancels.
public class BotProgramPopup : MonoBehaviour
{
    public TMP_Text titleText, statusText, slotHint, emptyText;
    public RectTransform robotSlot, inventoryGrid, dragLayer;
    public DiskIcon iconTemplate;
    public Button confirmButton, closeButton;
    public float slotIconScale = 1.45f;

    [Header("v2 (landscape): robot, battery, follow")]
    public ModelPreview preview;
    public Image batteryFill;
    public TMP_Text batteryText, statsText;
    public Button followButton;
    public Image followBg;
    public TMP_Text followLabel;
    [Header("Formation (drones, C4)")]
    public Button formationButton;
    public Image formationBg;
    public TMP_Text formationLabel;
    [Tooltip("Small text under the FOLLOW / FORMATION buttons")]
    public TMP_Text followHint;

    [Header("Scrap (R1)")]
    public Button destroyButton;
    public Image destroyBg;
    public TMP_Text destroyLabel;
    [Tooltip("Seconds the red button stays armed after the first tap")]
    public float destroyArmTime = 3f;
    float destroyArmed;   // > 0 while the first tap is waiting for the confirm tap

    float liveT;

    [Header("Disk info (what the disk does)")]
    [Tooltip("Optional; created at runtime under the FOLLOW hint when empty")]
    public TMP_Text diskInfo;
    public Vector2 diskInfoPos = new Vector2(-40f, -702f);
    public Vector2 diskInfoSize = new Vector2(460f, 110f);
    DiskProgram infoDisk;   // the disk whose description is shown (last tapped / pressed, else the one in the slot)

    ScoutBot bot;
    readonly List<DiskProgram> inventory = new List<DiskProgram>();
    DiskProgram slot;
    readonly List<GameObject> icons = new List<GameObject>();
    bool wired;

    void Wire()
    {
        if (wired) return;
        wired = true;
        confirmButton.onClick.AddListener(Confirm);
        closeButton.onClick.AddListener(Close);
        if (followButton != null) followButton.onClick.AddListener(ToggleFollow);
        if (formationButton != null) formationButton.onClick.AddListener(ToggleFormation);
        if (destroyButton != null) destroyButton.onClick.AddListener(RequestScrap);
    }

    public void Open(ScoutBot target)
    {
        Wire();
        bot = target;
        inventory.Clear();
        if (PlayerInventory.Instance != null) inventory.AddRange(PlayerInventory.Instance.disks);
        slot = bot.Program;
        infoDisk = slot;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        UIState.ModalOpen = true;
        titleText.text = bot.BotName.ToUpper() + "  <color=#" + ColorUtility.ToHtmlStringRGB(BotLevelTag.LevelColor(bot.Level)) + ">LV " + bot.Level + "</color>";
        Rebuild();
        foreach (var fit in GetComponentsInChildren<FitToSafeArea>()) fit.Apply();
        if (preview != null) preview.Show(bot.SourcePrefab != null ? bot.SourcePrefab : BotFactory.Instance != null ? BotFactory.Instance.botPrefab : null);
        liveT = 0f;
        destroyArmed = 0f;
        RefreshLive();
    }

    public void Close()
    {
        if (preview != null) preview.Hide();
        ClearIcons();
        gameObject.SetActive(false);
        UIState.ModalOpen = false;
        bot = null;
    }

    void Update()
    {
        if (bot != null) statusText.text = bot.Status;
        if (destroyArmed > 0f)
        {
            destroyArmed -= Time.unscaledDeltaTime;
            if (destroyArmed <= 0f) RefreshDestroy();
        }
        liveT -= Time.unscaledDeltaTime;
        if (liveT <= 0f) { liveT = 0.2f; RefreshLive(); }
    }

    // Follow applies right away (it's a command, not part of the disk change)
    void ToggleFollow()
    {
        if (bot == null) return;
        bot.SetFollow(!bot.Following);
        GameToast.Show(bot.Following ? bot.BotName.ToUpper() + " IS FOLLOWING YOU" : bot.BotName.ToUpper() + " BACK TO WORK", new Color(0.45f, 0.95f, 1f));
        RefreshLive();
    }

    // First tap arms the button, second tap scraps the bot.
    void RequestScrap()
    {
        if (bot == null) return;
        if (destroyArmed <= 0f)
        {
            destroyArmed = destroyArmTime;
            RefreshDestroy();
            return;
        }
        var b = bot;
        Close();      // unconfirmed disk moves are dropped, like CLOSE
        b.Scrap();
    }

    void RefreshDestroy()
    {
        if (destroyLabel == null || bot == null) return;
        bool armed = destroyArmed > 0f;
        destroyLabel.text = armed ? "TAP AGAIN TO SCRAP (+" + bot.ScrapRefund + " MATS)" : "DESTROY";
        if (destroyBg != null) destroyBg.color = armed ? new Color(0.95f, 0.18f, 0.12f) : new Color(0.62f, 0.14f, 0.1f);
    }

    // FORMATION (drones): lead a squad / disband it / leave the squad you're in
    void ToggleFormation()
    {
        var d = bot as CombatDrone;
        if (d == null) return;
        var gold = new Color(1f, 0.82f, 0.28f);
        if (d.InSquad && !d.IsSquadLeader)
        {
            d.LeaveSquad(false);
            GameToast.Show(d.BotName.ToUpper() + " LEFT THE SQUAD", gold);
        }
        else if (d.IsSquadLeader)
        {
            d.SetFormation(false);
            GameToast.Show("SQUAD DISBANDED", gold);
        }
        else
        {
            int n = d.SetFormation(true);
            if (n == 0) GameToast.Show("NO OTHER DRONE WITH A DISK AVAILABLE", new Color(1f, 0.45f, 0.35f));
            else GameToast.Show("SQUAD: LEADER + " + n + (n == 1 ? " DRONE" : " DRONES"), gold);
        }
        RefreshLive();
    }

    void RefreshLive()
    {
        if (bot == null) return;
        RefreshDestroy();
        float v = bot.Battery01;
        if (batteryFill != null)
        {
            var rt = batteryFill.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Max(0.01f, v), 1f);
            batteryFill.color = bot.IsDead ? WorkshopTheme.Red : bot.IsCharging ? WorkshopTheme.Cyan
                              : v > 0.5f ? WorkshopTheme.Green : v > 0.2f ? WorkshopTheme.Yellow : WorkshopTheme.Red;
        }
        if (batteryText != null)
            batteryText.text = Mathf.RoundToInt(v * 100f) + "%   " + Stats.Clock(v * bot.batteryRuntime) + " left" +
                               (bot.IsBroken ? "   BROKEN" : bot.IsDead ? "   OUT OF POWER" : bot.IsCharging ? "   CHARGING" : "");
        if (statsText != null)
            statsText.text = bot.PopupStats();
        var drone = bot as CombatDrone;
        bool member = drone != null && drone.InSquad && !drone.IsSquadLeader;
        if (followLabel != null)
        {
            bool on = bot.Following && !member;
            followLabel.text = member ? "FOLLOW: SQUAD" : on ? "FOLLOW: ON" : "FOLLOW: OFF";
            followLabel.color = on ? WorkshopTheme.DarkText : WorkshopTheme.Muted;   // quiet when off
            if (followBg != null) followBg.color = on ? WorkshopTheme.Cyan : WorkshopTheme.ButtonOff;
        }
        if (followButton != null) followButton.interactable = !member;
        if (formationButton != null)
        {
            formationButton.gameObject.SetActive(drone != null);
            if (drone != null && formationLabel != null)
            {
                bool lead = drone.IsSquadLeader;
                formationLabel.text = member ? "LEAVE SQUAD" : lead ? "FORMATION: ON" : "FORMATION: OFF";
                formationLabel.color = lead ? WorkshopTheme.DarkText : WorkshopTheme.Muted;
                if (formationBg != null) formationBg.color = lead ? WorkshopTheme.Orange : WorkshopTheme.ButtonOff;
            }
        }
        if (followHint != null)
        {
            string squad = drone != null ? drone.SquadText : "";
            followHint.text = squad.Length > 0 ? squad
                : drone != null ? "FOLLOW: escorts you.  FORMATION: up to 2 drones that have a disk fly in a V behind this one."
                : "FOLLOW: the bot escorts you; OFF sends it back to work.";
        }
    }

    void Confirm()
    {
        if (PlayerInventory.Instance != null) PlayerInventory.Instance.SetDisks(inventory);
        if (bot != null && slot != bot.Program) bot.SetProgram(slot);
        Close();
    }

    // ---------- disk moves (called by DiskIcon) ----------

    public void HandleDrop(DiskIcon icon, DiskDropZone zone)
    {
        if (zone.isRobotSlot) PutInRobot(icon); else TakeFromRobot(icon);
    }

    public void Tap(DiskIcon icon)
    {
        if (icon.fromRobot) TakeFromRobot(icon); else PutInRobot(icon);
    }

    void PutInRobot(DiskIcon icon)
    {
        if (!icon.fromRobot && icon.index >= 0 && icon.index < inventory.Count)
        {
            var disk = inventory[icon.index];
            infoDisk = disk;
            if (!disk.FitsOn(bot.robotType))
            {
                GameToast.Show("THIS DISK DOESN'T FIT A " + RobotTypes.Name(bot.robotType), new Color(1f, 0.45f, 0.35f));
                Rebuild();   // the dragged icon bounces back
                return;
            }
            inventory.RemoveAt(icon.index);
            if (slot != null) inventory.Add(slot);   // swap the old disk back
            slot = disk;
        }
        Rebuild();
    }

    void TakeFromRobot(DiskIcon icon)
    {
        if (icon.fromRobot && slot != null)
        {
            inventory.Add(slot);
            slot = null;
        }
        infoDisk = null;
        Rebuild();
    }

    // ---------- UI ----------

    void Rebuild()
    {
        ClearIcons();
        var shown = new HashSet<DiskProgram>();
        for (int i = 0; i < inventory.Count; i++)
        {
            if (!shown.Add(inventory[i])) continue;   // identical disks stack into one icon ("x2")
            int n = 0;
            foreach (var d in inventory) if (d == inventory[i]) n++;
            Make(inventory[i], inventoryGrid, false, i, n);
        }
        if (slot != null)
        {
            var ic = Make(slot, robotSlot, true, -1);
            ic.transform.localScale = Vector3.one * slotIconScale;
        }
        slotHint.gameObject.SetActive(slot == null);
        emptyText.gameObject.SetActive(inventory.Count == 0);
        RefreshDiskInfo();
    }

    /// Pressing a disk (before the tap / drag finishes) shows what it does.
    public void Preview(DiskProgram disk)
    {
        if (disk == null) return;
        infoDisk = disk;
        RefreshDiskInfo();
    }

    void RefreshDiskInfo()
    {
        if (diskInfo == null) MakeDiskInfo();
        if (diskInfo == null) return;
        var d = infoDisk != null ? infoDisk : slot;
        if (d == null)
        {
            diskInfo.text = "<color=#" + ColorUtility.ToHtmlStringRGB(WorkshopTheme.Muted) + ">" +
                            (bot != null && bot.robotType == RobotType.Drone
                                ? "No disk: the drone stays at its spot and only fights back.\nTap a disk to see what it does."
                                : "No disk: the bot waits at its spot.\nTap a disk to see what it does.") + "</color>";
            return;
        }
        bool fits = bot == null || d.FitsOn(bot.robotType);
        string head = "<color=#" + ColorUtility.ToHtmlStringRGB(d.color) + "><b>" + d.displayName.ToUpper() + "</b></color>" +
                      "<color=#" + ColorUtility.ToHtmlStringRGB(WorkshopTheme.Muted) + ">  ·  " + RobotTypes.Name(d.compatibleWith) +
                      (d == slot ? "  ·  IN SLOT" : "") + "</color>";
        string body = string.IsNullOrEmpty(d.description) ? "" : "\n" + d.description;
        if (!fits) body += "\n<color=#" + ColorUtility.ToHtmlStringRGB(WorkshopTheme.Red) + ">Doesn't fit this robot.</color>";
        else if (d == slot && slot != bot.Program) body += "\n<color=#" + ColorUtility.ToHtmlStringRGB(WorkshopTheme.Orange) + ">Press CONFIRM to start it.</color>";
        diskInfo.text = head + body;
    }

    // a copy of the FOLLOW hint text, placed in the free space above DESTROY
    void MakeDiskInfo()
    {
        if (followHint == null) return;
        diskInfo = Instantiate(followHint, followHint.transform.parent);
        diskInfo.name = "DiskInfo";
        var rt = diskInfo.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = diskInfoPos;
        rt.sizeDelta = diskInfoSize;
        diskInfo.textWrappingMode = TextWrappingModes.Normal;
        diskInfo.enableAutoSizing = true;
        diskInfo.fontSizeMin = 15f;
        diskInfo.fontSizeMax = 23f;
        diskInfo.alignment = TextAlignmentOptions.Top;
        diskInfo.color = Color.white;
        diskInfo.raycastTarget = false;
    }

    DiskIcon Make(DiskProgram program, Transform parent, bool fromRobot, int index, int count = 1)
    {
        var ic = Instantiate(iconTemplate, parent);
        ic.gameObject.SetActive(true);
        ((RectTransform)ic.transform).anchoredPosition = Vector2.zero;
        ic.Setup(this, program, fromRobot, index, count, bot == null || program.FitsOn(bot.robotType));
        icons.Add(ic.gameObject);
        return ic;
    }

    void ClearIcons()
    {
        foreach (var go in icons)
        {
            if (go == null) continue;
            go.SetActive(false);   // leave the layout immediately
            Destroy(go);
        }
        icons.Clear();
    }
}
