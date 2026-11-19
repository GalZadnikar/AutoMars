using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Full-screen building Workshop, Deep Rock Galactic style:
/// robot showcase + slots, stats with deltas, hex upgrade tree, perk details, level bar.
/// Built by Tools > Trash Game > 3. Build Workshop. Rows / hexes / pips are spawned from templates
/// at runtime so the tree asset decides how many there are.
public class WorkshopScreen : MonoBehaviour
{
    [Header("Header")]
    public CanvasGroup group;
    public TMP_Text titleText, categoryText, materialsText;
    public Button closeButton, devButton;

    [Header("Showcase")]
    public ModelPreview preview;
    public TMP_Text outputText;
    public Button batterySlot, diskSlot;
    public TMP_Text batteryState, diskState;
    public Image diskIcon;
    public GameObject diskLock;
    public Image batteryIcon;

    [Header("Battery picker (replaces the tree panel while open)")]
    public GameObject batteryPanel;
    public RectTransform batteryGrid;
    public BatteryCard batteryCardTemplate;
    public TMP_Text batteryEquippedText, batteryDetailText, batteryEmptyText, batteryEquipLabel;
    public Button batteryEquipButton, batteryRemoveButton, batteryBackButton;

    [Header("Stats")]
    public RectTransform statsList;
    public StatRow statRowTemplate;

    [Header("Tree")]
    public TMP_Text pointsText;
    public Image pointsBadge;
    public RectTransform treeList;
    public TierRow tierRowTemplate;
    public PerkHex hexTemplate;
    public float rowHeight = 118f;
    public float hexSpacing = 235f;

    [Header("Perk details")]
    public TMP_Text detailTitle, detailBody;
    public Button equipButton;
    public TMP_Text equipLabel;

    [Header("Level")]
    public TMP_Text levelText;
    public RectTransform pipRow;
    public Image pipTemplate;
    public Button levelUpButton;
    public TMP_Text levelUpLabel;
    public TMP_Text toastText;

    [Header("Testing")]
    [Tooltip("+100 materials button, only in the Editor and development builds")]
    public bool showDevButton = true;
    public int devAmount = 100;

    BuildingUpgrades up;
    System.Action onClosed;
    StatType[] shown = Stats.FactoryStats;
    BuildingUpgradeTree builtFor;
    readonly List<StatRow> statRows = new List<StatRow>();
    readonly List<TierRow> rows = new List<TierRow>();
    readonly List<List<PerkHex>> hexes = new List<List<PerkHex>>();
    readonly List<Image> pips = new List<Image>();
    readonly List<GameObject> spawned = new List<GameObject>();
    readonly List<BatteryCard> cards = new List<BatteryCard>();
    bool pickerOpen;
    int pickIndex = -1;

    int inspectTier = -1, inspectOption = -1;
    int shownMaterials = -1;
    float toastT, pollT, matPunch, lvlPunch;
    bool wired;

    public bool IsOpen => gameObject.activeSelf && up != null;

    void Wire()
    {
        if (wired) return;
        wired = true;
        closeButton.onClick.AddListener(Close);
        if (outputText != null)
        {
            outputText.textWrappingMode = TextWrappingModes.NoWrap;
            outputText.enableAutoSizing = true;
            outputText.fontSizeMin = 18f;
            outputText.fontSizeMax = outputText.fontSize;
        }
        levelUpButton.onClick.AddListener(LevelUp);
        equipButton.onClick.AddListener(EquipInspected);
        if (batterySlot != null) batterySlot.onClick.AddListener(() => { WorkshopRobotsTab.For(this).Show(false); OpenPicker(PickerMode.Battery); });
        if (batteryBackButton != null) batteryBackButton.onClick.AddListener(ClosePicker);
        if (batteryEquipButton != null) batteryEquipButton.onClick.AddListener(EquipPicked);
        if (batteryRemoveButton != null) batteryRemoveButton.onClick.AddListener(RemoveBattery);
        if (diskSlot != null) diskSlot.onClick.AddListener(OpenDiskSlot);
        if (devButton != null)
        {
            devButton.onClick.AddListener(() => { if (Economy.Instance != null) Economy.Instance.AddMaterials(devAmount); });
            devButton.gameObject.SetActive(showDevButton && (Application.isEditor || Debug.isDebugBuild));
        }
    }

    // ------------------------------------------------------------------ open / close

    public void Open(BotFactory f)
    {
        if (f == null) return;
        Open(f.Upgrades, f.PreviewPrefab, null);
    }

    /// Opens the Workshop for any upgrade tree (a factory, the player). previewPrefab = the model in the showcase;
    /// onClosed runs after CLOSE (e.g. back to the base popup).
    public void Open(BuildingUpgrades upgrades, GameObject previewPrefab, System.Action closed)
    {
        if (upgrades == null || upgrades.tree == null)
        {
            Debug.LogWarning("WorkshopScreen: no BuildingUpgrades / tree.");
            return;
        }
        Wire();
        up = upgrades;
        onClosed = closed;
        shown = up.tree.shownStats != null && up.tree.shownStats.Count > 0 ? up.tree.shownStats.ToArray() : Stats.FactoryStats;
        if (batterySlot != null) batterySlot.gameObject.SetActive(up.tree.hasSlots);
        if (diskSlot != null) diskSlot.gameObject.SetActive(up.tree.hasSlots);
        // slot captions ("BATTERY", "MASTER DISK") and the "applies to newly built bots" note are factory-only
        if (batterySlot != null)
            foreach (Transform c in batterySlot.transform.parent)
                if (c.name == "Caption") c.gameObject.SetActive(up.tree.hasSlots);
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "Footnote") t.gameObject.SetActive(up.tree.hasSlots);
        up.OnChanged += OnUpgradesChanged;

        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        UIState.ModalOpen = true;
        if (group != null) group.alpha = 0f;

        inspectTier = inspectOption = -1;
        pickerOpen = false;
        pickIndex = -1;
        if (batteryPanel != null) batteryPanel.SetActive(false);
        toastT = 0f;
        shownMaterials = -1;
        BuildDynamic();
        foreach (var fit in GetComponentsInChildren<FitToSafeArea>()) fit.Apply();   // size the preview correctly on first open
        if (preview != null) preview.Show(previewPrefab);
        // UPGRADES | ROBOTS tabs (factories only; the player's tree has no robots)
        WorkshopRobotsTab.For(this).Setup(upgrades.GetComponent<BotFactory>());
        Refresh();
    }

    /// Closes the battery / master-disk picker (the ROBOTS tab takes the right-hand panel).
    public void HidePicker()
    {
        if (!pickerOpen) return;
        ClosePicker();
    }

    public void Close()
    {
        if (up != null) up.OnChanged -= OnUpgradesChanged;
        if (preview != null) preview.Hide();
        gameObject.SetActive(false);
        UIState.ModalOpen = false;
        up = null;
        var cb = onClosed;
        onClosed = null;
        cb?.Invoke();
    }

    void OnUpgradesChanged() => Refresh();

    // ------------------------------------------------------------------ actions

    void OnHexTap(int tier, int option)
    {
        // second tap on the inspected perk equips it
        if (tier == inspectTier && option == inspectOption && up.IsTierUnlocked(tier) && up.GetPick(tier) != option)
        {
            EquipInspected();
            return;
        }
        inspectTier = tier;
        inspectOption = option;
        Refresh();
    }

    void EquipInspected()
    {
        if (up == null || inspectTier < 0) return;
        var perk = up.tree.tiers[inspectTier].options[inspectOption];
        if (up.Equip(inspectTier, inspectOption)) Toast("EQUIPPED  " + perk.title.ToUpper());
    }

    void LevelUp()
    {
        if (up == null) return;
        if (!up.TryLevelUp()) { Toast("NOT ENOUGH MATERIALS"); return; }
        lvlPunch = 1f;
        inspectTier = inspectOption = -1;
        int newTier = up.Level - 2;
        Toast(newTier < up.TierCount ? "LEVEL " + up.Level + "!   +1 SKILL POINT" : "LEVEL " + up.Level + "!");
        Refresh();
    }

    // ------------------------------------------------------------------ building rows from the tree

    void BuildDynamic()
    {
        var tree = up.tree;
        if (builtFor == tree && rows.Count == tree.tiers.Count && statRows.Count == shown.Length + 1) return;
        foreach (var go in spawned) if (go != null) Destroy(go);
        spawned.Clear(); statRows.Clear(); rows.Clear(); hexes.Clear(); pips.Clear();
        builtFor = tree;

        // stats: robot level + every stat
        for (int i = 0; i < shown.Length + 1; i++)
        {
            var r = Instantiate(statRowTemplate, statsList);
            r.gameObject.SetActive(true);
            spawned.Add(r.gameObject);
            statRows.Add(r);
        }

        // tiers
        for (int t = 0; t < tree.tiers.Count; t++)
        {
            var row = Instantiate(tierRowTemplate, treeList);
            row.gameObject.SetActive(true);
            row.name = "Tier " + (t + 1);
            spawned.Add(row.gameObject);
            ((RectTransform)row.transform).anchoredPosition = new Vector2(0f, -rowHeight * (t + 0.5f));

            var opts = tree.tiers[t].options;
            int k = opts.Count;
            var lineRt = (RectTransform)row.line.transform;
            lineRt.sizeDelta = new Vector2(Mathf.Max(0f, (k - 1) * hexSpacing), lineRt.sizeDelta.y);
            row.line.gameObject.SetActive(k > 1);

            var list = new List<PerkHex>();
            for (int i = 0; i < k; i++)
            {
                var h = Instantiate(hexTemplate, row.options);
                h.gameObject.SetActive(true);
                h.name = "Perk " + (t + 1) + "." + (i + 1);
                ((RectTransform)h.transform).anchoredPosition = new Vector2((i - (k - 1) * 0.5f) * hexSpacing, 0f);
                int tt = t, ii = i;
                h.Setup(opts[i], () => OnHexTap(tt, ii));
                list.Add(h);
            }
            rows.Add(row);
            hexes.Add(list);
        }

        // level pips
        for (int i = 0; i < up.MaxLevel; i++)
        {
            var p = Instantiate(pipTemplate, pipRow);
            p.gameObject.SetActive(true);
            spawned.Add(p.gameObject);
            pips.Add(p);
        }
    }

    // ------------------------------------------------------------------ refresh

    void Refresh()
    {
        if (up == null) return;
        var tree = up.tree;
        var cur = up.Current;
        int pts = up.UnspentPoints;

        titleText.text = tree.displayName.ToUpper();
        categoryText.text = tree.category.ToUpper();
        outputText.text = tree.outputPrefix + tree.outputName.ToUpper() + "   <color=" + WorkshopTheme.Hex(WorkshopTheme.Orange) + ">LV " + up.Level + "</color>" +
                          (up.masterDisk != null && up.MasterDiskUnlocked ? "   <color=" + WorkshopTheme.Hex(up.masterDisk.color) + ">AUTO: " + up.masterDisk.shortName.ToUpper() + "</color>" : "");

        // --- stats (with deltas while a perk is inspected) ---
        StatBlock preview = pickerOpen
            ? (pickIndex >= 0 && pickerMode == PickerMode.Battery ? up.ComputeStats(-1, -1, true, InventoryBattery(pickIndex)) : null)
            : (inspectTier >= 0 ? up.ComputeStats(inspectTier, inspectOption) : null);
        statRows[0].Set(tree.levelRowLabel, cur.Level.ToString(), "", true);
        for (int i = 0; i < shown.Length; i++)
        {
            var st = shown[i];
            float v = cur[st];
            string d = "";
            bool good = true;
            if (preview != null)
            {
                float dv = preview[st] - v;
                d = Stats.FormatDelta(st, dv);
                good = Stats.DeltaIsGood(st, dv);
            }
            statRows[i + 1].Set(Stats.Label(st), Stats.Format(st, v), d, good);
        }

        // --- tree ---
        for (int t = 0; t < rows.Count; t++)
        {
            bool unlocked = up.IsTierUnlocked(t);
            int pick = up.GetPick(t);
            rows[t].SetState(t, unlocked, unlocked && pick < 0, tree.TierUnlockLevel(t));
            for (int i = 0; i < hexes[t].Count; i++)
            {
                var state = !unlocked ? PerkHex.State.Locked : pick == i ? PerkHex.State.Equipped : PerkHex.State.Available;
                hexes[t][i].SetState(state, t == inspectTier && i == inspectOption);
            }
        }
        pointsText.text = pts == 1 ? "1 SKILL POINT" : pts + " SKILL POINTS";
        pointsText.color = pts > 0 ? WorkshopTheme.DarkText : WorkshopTheme.Muted;
        pointsBadge.color = pts > 0 ? WorkshopTheme.Orange : WorkshopTheme.ButtonOff;

        RefreshDetail(pts);

        // --- slots (live in Modules 4 and 6) ---
        var eq = up.equippedBattery;
        var invB = PlayerInventory.Instance;
        int owned = invB != null ? invB.batteries.Count : 0;
        if (batteryState != null)
        {
            batteryState.text = eq != null ? "<color=" + WorkshopTheme.Hex(eq.Color) + ">" + eq.rarity.ToString().ToUpper() + "</color>"
                              : owned > 0 ? "<color=" + WorkshopTheme.Hex(WorkshopTheme.Orange) + ">+ EQUIP</color>" : "EMPTY";
        }
        if (batteryIcon != null) batteryIcon.color = eq != null ? eq.Color : WorkshopTheme.Muted;
        if (pickerOpen) RefreshPicker();
        bool diskOpen = up.MasterDiskUnlocked;
        var md = up.masterDisk;
        int disksOwned = invB != null ? invB.disks.Count : 0;
        if (diskState != null)
            diskState.text = !diskOpen ? "LV " + tree.masterDiskUnlockLevel
                           : md != null ? "<color=" + WorkshopTheme.Hex(md.color) + ">" + md.shortName.ToUpper() + "</color>"
                           : disksOwned > 0 ? "<color=" + WorkshopTheme.Hex(WorkshopTheme.Orange) + ">+ EQUIP</color>" : "EMPTY";
        if (diskLock != null) diskLock.SetActive(!diskOpen);
        if (diskIcon != null) diskIcon.color = !diskOpen ? WorkshopTheme.LockedGlyph : md != null ? md.color : WorkshopTheme.Muted;

        // --- level ---
        levelText.text = tree.levelLabel + "  <color=" + WorkshopTheme.Hex(WorkshopTheme.Orange) + ">" + up.Level + "</color> / " + up.MaxLevel;
        for (int i = 0; i < pips.Count; i++)
        {
            bool reached = i < up.Level;
            bool milestone = i + 1 == tree.masterDiskUnlockLevel;
            pips[i].color = reached ? (milestone ? WorkshopTheme.Cyan : WorkshopTheme.Orange)
                                    : (milestone ? new Color(0.25f, 0.42f, 0.48f) : WorkshopTheme.LockedFill);
        }
        RefreshEconomy();
    }

    void RefreshDetail(int pts)
    {
        var tree = up.tree;
        if (inspectTier < 0)
        {
            int first = -1;
            for (int t = 0; t < up.TierCount && first < 0; t++) if (up.IsTierUnlocked(t) && up.GetPick(t) < 0) first = t;
            if (first >= 0)
            {
                detailTitle.text = "TIER " + (first + 1) + " UNLOCKED";
                detailBody.text = "Tap a perk to see what it does.\nTap it again (or EQUIP) to choose it.";
            }
            else if (up.IsMaxLevel)
            {
                detailTitle.text = "FULLY UPGRADED";
                detailBody.text = "You can swap perks any time for free.";
            }
            else
            {
                detailTitle.text = "UPGRADE TREE";
                detailBody.text = "Level up to unlock Tier " + (up.Level) + " (+1 skill point).\nTap any perk to preview it.";
            }
            equipButton.gameObject.SetActive(false);
            return;
        }

        var perk = tree.tiers[inspectTier].options[inspectOption];
        detailTitle.text = perk.title.ToUpper();
        var sb = new StringBuilder();
        foreach (var m in perk.mods)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<color=").Append(WorkshopTheme.Hex(m.IsGood ? WorkshopTheme.Green : WorkshopTheme.Red)).Append('>')
              .Append(m.Describe()).Append("</color>");
        }
        if (!string.IsNullOrEmpty(perk.flavor))
            sb.Append('\n').Append("<color=").Append(WorkshopTheme.Hex(WorkshopTheme.Muted)).Append('>').Append(perk.flavor).Append("</color>");
        detailBody.text = sb.ToString();

        equipButton.gameObject.SetActive(true);
        bool unlocked = up.IsTierUnlocked(inspectTier);
        int pick = up.GetPick(inspectTier);
        if (!unlocked)
        {
            equipButton.interactable = false;
            equipLabel.text = "LV " + tree.TierUnlockLevel(inspectTier);
        }
        else if (pick == inspectOption)
        {
            equipButton.interactable = false;
            equipLabel.text = "EQUIPPED";
        }
        else
        {
            equipButton.interactable = true;
            equipLabel.text = pick >= 0 ? "SWAP" : "EQUIP";
        }
    }

    void RefreshEconomy()
    {
        if (up == null) return;
        int m = Economy.Instance != null ? Economy.Instance.materials : 0;
        if (m != shownMaterials)
        {
            if (shownMaterials >= 0) matPunch = 1f;
            shownMaterials = m;
            materialsText.text = "MATERIALS  <color=" + WorkshopTheme.Hex(WorkshopTheme.Orange) + ">" + m + "</color>";
        }

        var img = levelUpButton.targetGraphic as Image;
        if (up.IsMaxLevel)
        {
            levelUpButton.interactable = false;
            levelUpLabel.text = "MAX LEVEL";
            if (img != null) img.color = WorkshopTheme.ButtonOff;
        }
        else
        {
            bool afford = up.CanAffordLevelUp;
            levelUpButton.interactable = true;   // tapping while poor shows a toast
            levelUpLabel.text = "LEVEL UP\n<size=70%>" + up.NextLevelCost + " MATERIALS</size>";
            levelUpLabel.color = afford ? Color.white : WorkshopTheme.Muted;
            if (img != null) img.color = afford ? WorkshopTheme.ButtonGreen : WorkshopTheme.ButtonOff;
        }
    }

    // ------------------------------------------------------------------ slot picker (battery / master disk)
    // One panel that covers the tree; the stats on the left stay visible for comparing.

    enum PickerMode { Battery, Disk }
    PickerMode pickerMode;

    BatteryItem InventoryBattery(int i)
    {
        var inv = PlayerInventory.Instance;
        if (inv == null) return null;
        var stacks = inv.BatteryStacks();
        return i >= 0 && i < stacks.Count ? stacks[i].item : null;
    }

    DiskProgram InventoryDisk(int i)
    {
        var inv = PlayerInventory.Instance;
        if (inv == null) return null;
        var stacks = inv.DiskStacks();
        return i >= 0 && i < stacks.Count ? stacks[i].item : null;
    }

    void OpenPicker(PickerMode mode)
    {
        if (up == null || batteryPanel == null) return;
        pickerMode = mode;
        pickerOpen = true;
        pickIndex = -1;
        inspectTier = inspectOption = -1;
        batteryPanel.SetActive(true);
        var title = batteryPanel.transform.Find("Title");
        if (title != null) title.GetComponent<TMP_Text>().text = mode == PickerMode.Battery ? "BATTERIES" : "MASTER DISK";
        batteryEmptyText.text = mode == PickerMode.Battery ? "No batteries yet.\nExplore the map to find them!" : "No disks.\nFind disks on the map!";
        Refresh();
    }

    void OpenDiskSlot()
    {
        if (up == null) return;
        WorkshopRobotsTab.For(this).Show(false);
        if (!up.MasterDiskUnlocked) { Toast("UNLOCKS AT FACTORY LEVEL " + up.tree.masterDiskUnlockLevel); return; }
        OpenPicker(PickerMode.Disk);
    }

    void ClosePicker()
    {
        pickerOpen = false;
        pickIndex = -1;
        if (batteryPanel != null) batteryPanel.SetActive(false);
        Refresh();
    }

    void EquipPicked()
    {
        if (pickerMode == PickerMode.Disk)
        {
            var disk = InventoryDisk(pickIndex);
            if (disk == null) return;
            if (!disk.FitsOn(up.OutputType)) { Toast("THIS DISK DOESN'T FIT A " + RobotTypes.Name(up.OutputType)); return; }
            pickIndex = -1;
            if (up.EquipMasterDisk(disk)) Toast("MASTER DISK: " + disk.displayName.ToUpper());
            return;
        }
        var b = InventoryBattery(pickIndex);
        if (b == null) return;
        pickIndex = -1;
        if (up.EquipBattery(b)) Toast("EQUIPPED  " + b.displayName.ToUpper());
    }

    void RemoveBattery()
    {
        pickIndex = -1;
        if (pickerMode == PickerMode.Disk)
        {
            if (up.masterDisk != null && up.UnequipMasterDisk()) Toast("MASTER DISK REMOVED");
            return;
        }
        if (up.equippedBattery == null) return;
        if (up.UnequipBattery()) Toast("BATTERY REMOVED");
        else Toast("BATTERY SLOTS FULL");
    }

    void RefreshPicker()
    {
        var inv = PlayerInventory.Instance;
        bool disk = pickerMode == PickerMode.Disk;
        // identical items share one card ("x2"), so the picker lists stacks, not single items
        var diskStacks = disk && inv != null ? inv.DiskStacks() : null;
        var batStacks = !disk && inv != null ? inv.BatteryStacks() : null;
        int count = disk ? (diskStacks != null ? diskStacks.Count : 0) : (batStacks != null ? batStacks.Count : 0);
        if (pickIndex >= count) pickIndex = -1;

        while (cards.Count < count) cards.Add(Instantiate(batteryCardTemplate, batteryGrid));
        for (int i = 0; i < cards.Count; i++)
        {
            bool on = i < count;
            cards[i].gameObject.SetActive(on);
            if (!on) continue;
            int ii = i;
            System.Action tap = () => { pickIndex = ii; Refresh(); };
            if (disk)
            {
                var p = diskStacks[i].item;
                bool fits = p.FitsOn(up.OutputType);
                cards[i].SetupCustom(p.displayName, fits ? "AUTO-PROGRAM" : RobotTypes.Name(p.compatibleWith) + " ONLY", p.color, diskIcon != null ? diskIcon.sprite : null, tap, diskStacks[i].count);
            }
            else cards[i].Setup(batStacks[i].item, tap, batStacks[i].count);
            cards[i].SetSelected(i == pickIndex);
        }
        batteryEmptyText.gameObject.SetActive(count == 0);

        bool hasEquipped;
        if (disk)
        {
            var md = up.masterDisk;
            hasEquipped = md != null;
            batteryEquippedText.text = md != null
                ? "IN SLOT:  <color=" + WorkshopTheme.Hex(md.color) + ">" + md.displayName.ToUpper() + "</color>"
                : "IN SLOT:  <color=" + WorkshopTheme.Hex(WorkshopTheme.Muted) + ">EMPTY (new bots come out idle)</color>";
            var pick = InventoryDisk(pickIndex);
            batteryDetailText.text = pick != null
                ? "<color=" + WorkshopTheme.Hex(pick.color) + ">" + pick.displayName.ToUpper() + "</color>\n" +
                  (string.IsNullOrEmpty(pick.description) ? "" : pick.description + "\n") +
                  (pick.FitsOn(up.OutputType) ? "Every new " + up.tree.unitWord.ToLowerInvariant() + " rolls out already running this program."
                                              : "<color=" + WorkshopTheme.Hex(WorkshopTheme.Red) + ">Doesn't fit a " + RobotTypes.Name(up.OutputType).ToLowerInvariant() + ": this disk is for " + RobotTypes.Name(pick.compatibleWith).ToLowerInvariant() + "s.</color>")
                : md != null && !string.IsNullOrEmpty(md.description)
                    ? "<color=" + WorkshopTheme.Hex(md.color) + ">" + md.displayName.ToUpper() + "</color>\n" + md.description + "\nEvery new " + up.tree.unitWord.ToLowerInvariant() + " rolls out running it. Tap a disk to see what it does."
                    : "Put a disk here and every bot this factory builds starts working right away.\nTap a disk to see what it does. The disk stays in the factory (REMOVE gives it back).";
            batteryEquipButton.interactable = pick != null && pick.FitsOn(up.OutputType);
        }
        else
        {
            var eq = up.equippedBattery;
            hasEquipped = eq != null;
            float now = up.Current[StatType.BatteryRuntime];
            batteryEquippedText.text = eq != null
                ? "IN SLOT:  <color=" + WorkshopTheme.Hex(eq.Color) + ">" + eq.displayName.ToUpper() + "</color>   " + Stats.Clock(eq.runtimeSeconds)
                : "IN SLOT:  <color=" + WorkshopTheme.Hex(WorkshopTheme.Muted) + ">BUILT-IN SCRAP CELL</color>";
            var pick = InventoryBattery(pickIndex);
            if (pick != null)
            {
                float after = up.ComputeStats(-1, -1, true, pick)[StatType.BatteryRuntime];
                float dd = after - now;
                batteryDetailText.text = "<color=" + WorkshopTheme.Hex(pick.Color) + ">" + pick.displayName.ToUpper() + "</color>\n" +
                    "New bots: " + Stats.Clock(after) + " per charge  <color=" + WorkshopTheme.Hex(dd >= 0 ? WorkshopTheme.Green : WorkshopTheme.Red) + ">" +
                    Stats.FormatDelta(StatType.BatteryRuntime, dd) + "</color>";
            }
            else
            {
                batteryDetailText.text = "Every bot this factory builds gets the battery's runtime.\n" +
                    "Battery slots: " + count + " / " + (inv != null ? inv.batterySlots : 0) + "   Tap a battery to compare.";
            }
            batteryEquipButton.interactable = pick != null;
        }

        if (batteryEquipLabel != null) batteryEquipLabel.text = hasEquipped ? "SWAP" : "EQUIP";
        batteryRemoveButton.interactable = hasEquipped;
        var rl = batteryRemoveButton.GetComponentInChildren<TMP_Text>();
        if (rl != null) rl.alpha = hasEquipped ? 1f : 0.35f;
        batteryEquipButton.GetComponentInChildren<TMP_Text>().alpha = batteryEquipButton.interactable ? 1f : 0.45f;
    }

    // ------------------------------------------------------------------ feedback

    void Toast(string text)
    {
        if (toastText == null) return;
        toastText.text = text;
        toastT = 1.8f;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (group != null && group.alpha < 1f) group.alpha = Mathf.MoveTowards(group.alpha, 1f, dt * 7f);

        pollT -= dt;
        if (pollT <= 0f) { pollT = 0.2f; RefreshEconomy(); }

        if (toastText != null)
        {
            toastT = Mathf.Max(0f, toastT - dt);
            var c = toastText.color;
            c.a = Mathf.Clamp01(toastT / 0.3f);
            toastText.color = c;
            toastText.transform.localScale = Vector3.one * (1f + 0.15f * Mathf.Clamp01((toastT - 1.6f) / 0.2f));
        }

        matPunch = Mathf.MoveTowards(matPunch, 0f, dt * 5f);
        materialsText.transform.localScale = Vector3.one * (1f + 0.15f * matPunch);
        lvlPunch = Mathf.MoveTowards(lvlPunch, 0f, dt * 4f);
        levelText.transform.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(lvlPunch * Mathf.PI));
    }
}
