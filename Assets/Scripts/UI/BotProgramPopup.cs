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

    float liveT;

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
    }

    public void Open(ScoutBot target)
    {
        Wire();
        bot = target;
        inventory.Clear();
        if (PlayerInventory.Instance != null) inventory.AddRange(PlayerInventory.Instance.disks);
        slot = bot.Program;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        UIState.ModalOpen = true;
        titleText.text = bot.BotName.ToUpper() + "  <color=#" + ColorUtility.ToHtmlStringRGB(BotLevelTag.LevelColor(bot.Level)) + ">LV " + bot.Level + "</color>";
        Rebuild();
        foreach (var fit in GetComponentsInChildren<FitToSafeArea>()) fit.Apply();
        if (preview != null && BotFactory.Instance != null) preview.Show(BotFactory.Instance.botPrefab);
        liveT = 0f;
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

    void RefreshLive()
    {
        if (bot == null) return;
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
                               (bot.IsDead ? "   OUT OF POWER" : bot.IsCharging ? "   CHARGING" : "");
        if (statsText != null)
            statsText.text = "Move  " + bot.moveSpeed.ToString("0.0") + " m/s\n" +
                             "Carry  " + bot.Carried + " / " + bot.capacity + "\n" +
                             "Collect  " + (1f / Mathf.Max(0.01f, bot.pickupInterval)).ToString("0.0") + "/s\n" +
                             "Full charge  " + Stats.Clock(bot.batteryRuntime);
        if (followLabel != null)
        {
            bool on = bot.Following;
            followLabel.text = on ? "FOLLOW: ON" : "FOLLOW: OFF";
            followLabel.color = on ? WorkshopTheme.DarkText : WorkshopTheme.White;
            if (followBg != null) followBg.color = on ? WorkshopTheme.Cyan : WorkshopTheme.ButtonOff;
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
    }

    DiskIcon Make(DiskProgram program, Transform parent, bool fromRobot, int index, int count = 1)
    {
        var ic = Instantiate(iconTemplate, parent);
        ic.gameObject.SetActive(true);
        ((RectTransform)ic.transform).anchoredPosition = Vector2.zero;
        ic.Setup(this, program, fromRobot, index, count);
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
