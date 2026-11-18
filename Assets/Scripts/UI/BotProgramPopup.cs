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
        titleText.text = bot.BotName.ToUpper();
        Rebuild();
    }

    public void Close()
    {
        ClearIcons();
        gameObject.SetActive(false);
        UIState.ModalOpen = false;
        bot = null;
    }

    void Update()
    {
        if (bot != null) statusText.text = bot.Status;
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
        for (int i = 0; i < inventory.Count; i++) Make(inventory[i], inventoryGrid, false, i);
        if (slot != null)
        {
            var ic = Make(slot, robotSlot, true, -1);
            ic.transform.localScale = Vector3.one * slotIconScale;
        }
        slotHint.gameObject.SetActive(slot == null);
        emptyText.gameObject.SetActive(inventory.Count == 0);
    }

    DiskIcon Make(DiskProgram program, Transform parent, bool fromRobot, int index)
    {
        var ic = Instantiate(iconTemplate, parent);
        ic.gameObject.SetActive(true);
        ((RectTransform)ic.transform).anchoredPosition = Vector2.zero;
        ic.Setup(this, program, fromRobot, index);
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
