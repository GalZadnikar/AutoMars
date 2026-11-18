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

    BotFactory factory;
    BuildingUpgrades up;
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
        levelUpButton.onClick.AddListener(LevelUp);
        equipButton.onClick.AddListener(EquipInspected);
        if (batterySlot != null) batterySlot.onClick.AddListener(OpenPicker);
        if (batteryBackButton != null) batteryBackButton.onClick.AddListener(ClosePicker);
        if (batteryEquipButton != null) batteryEquipButton.onClick.AddListener(EquipPicked);
        if (batteryRemoveButton != null) batteryRemoveButton.onClick.AddListener(RemoveBattery);
        if (diskSlot != null) diskSlot.onClick.AddListener(() =>
            Toast(up != null && up.MasterDiskUnlocked ? "MASTER DISK SLOT  -  COMING SOON"
                                                      : "UNLOCKS AT FACTORY LEVEL " + (up != null ? up.tree.masterDiskUnlockLevel : 4)));
        if (devButton != null)
        {
            devButton.onClick.AddListener(() => { if (Economy.Instance != null) Economy.Instance.AddMaterials(devAmount); });
            devButton.gameObject.SetActive(showDevButton && (Application.isEditor || Debug.isDebugBuild));
        }
    }

    // ------------------------------------------------------------------ open / close

    public void Open(BotFactory f)
    {
        if (f == null || f.Upgrades == null || f.Upgrades.tree == null)
        {
            Debug.LogWarning("WorkshopScreen: factory has no BuildingUpgrades / tree.");
            return;
        }
        Wire();
        factory = f;
        up = f.Upgrades;
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
        if (preview != null) preview.Show(f.botPrefab);
        Refresh();
    }

    public void Close()
    {
        if (up != null) up.OnChanged -= OnUpgradesChanged;
        if (preview != null) preview.Hide();
        gameObject.SetActive(false);
        UIState.ModalOpen = false;
        factory = null;
        up = null;
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
        if (builtFor == tree && rows.Count == tree.tiers.Count) return;
        foreach (var go in spawned) if (go != null) Destroy(go);
        spawned.Clear(); statRows.Clear(); rows.Clear(); hexes.Clear(); pips.Clear();
        builtFor = tree;

        // stats: robot level + every stat
        for (int i = 0; i < Stats.All.Length + 1; i++)
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
        outputText.text = "OUTPUT:  " + tree.outputName.ToUpper() + "   <color=" + WorkshopTheme.Hex(WorkshopTheme.Orange) + ">LV " + up.Level + "</color>";

        // --- stats (with deltas while a perk is inspected) ---
        StatBlock preview = pickerOpen
            ? (pickIndex >= 0 ? up.ComputeStats(-1, -1, true, InventoryBattery(pickIndex)) : null)
            : (inspectTier >= 0 ? up.ComputeStats(inspectTier, inspectOption) : null);
        statRows[0].Set("Robot Level", cur.Level.ToString(), "", true);
        for (int i = 0; i < Stats.All.Length; i++)
        {
            var st = Stats.All[i];
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
        if (diskState != null) diskState.text = diskOpen ? "EMPTY" : "LV " + tree.masterDiskUnlockLevel;
        if (diskLock != null) diskLock.SetActive(!diskOpen);
        if (diskIcon != null) diskIcon.color = diskOpen ? WorkshopTheme.White : WorkshopTheme.LockedGlyph;

        // --- level ---
        levelText.text = "FACTORY LEVEL  <color=" + WorkshopTheme.Hex(WorkshopTheme.Orange) + ">" + up.Level + "</color> / " + up.MaxLevel;
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
                detailBody.text = "Level up the factory to unlock Tier " + (up.Level) + " (+1 skill point).\nTap any perk to preview it.";
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

    // ------------------------------------------------------------------ battery picker

    BatteryItem InventoryBattery(int i)
    {
        var inv = PlayerInventory.Instance;
        return inv != null && i >= 0 && i < inv.batteries.Count ? inv.batteries[i] : null;
    }

    void OpenPicker()
    {
        if (up == null || batteryPanel == null) return;
        pickerOpen = true;
        pickIndex = -1;
        inspectTier = inspectOption = -1;
        batteryPanel.SetActive(true);
        Refresh();
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
        var b = InventoryBattery(pickIndex);
        if (b == null) return;
        pickIndex = -1;
        if (up.EquipBattery(b)) Toast("EQUIPPED  " + b.displayName.ToUpper());
    }

    void RemoveBattery()
    {
        var eq = up.equippedBattery;
        if (eq == null) return;
        pickIndex = -1;
        if (up.UnequipBattery()) Toast("BATTERY REMOVED");
        else Toast("BATTERY SLOTS FULL");
    }

    void RefreshPicker()
    {
        var inv = PlayerInventory.Instance;
        var list = inv != null ? inv.batteries : new List<BatteryItem>();
        if (pickIndex >= list.Count) pickIndex = -1;

        while (cards.Count < list.Count)
        {
            var c = Instantiate(batteryCardTemplate, batteryGrid);
            cards.Add(c);
        }
        for (int i = 0; i < cards.Count; i++)
        {
            bool on = i < list.Count;
            cards[i].gameObject.SetActive(on);
            if (!on) continue;
            int ii = i;
            cards[i].Setup(list[i], () => { pickIndex = ii; Refresh(); });
            cards[i].SetSelected(i == pickIndex);
        }
        batteryEmptyText.gameObject.SetActive(list.Count == 0);

        var eq = up.equippedBattery;
        float now = up.Current[StatType.BatteryRuntime];
        batteryEquippedText.text = eq != null
            ? "IN SLOT:  <color=" + WorkshopTheme.Hex(eq.Color) + ">" + eq.displayName.ToUpper() + "</color>   " + Stats.Clock(eq.runtimeSeconds)
            : "IN SLOT:  <color=" + WorkshopTheme.Hex(WorkshopTheme.Muted) + ">BUILT-IN SCRAP CELL</color>";

        var pick = InventoryBattery(pickIndex);
        if (pick != null)
        {
            float after = up.ComputeStats(-1, -1, true, pick)[StatType.BatteryRuntime];
            float d = after - now;
            string delta = Stats.FormatDelta(StatType.BatteryRuntime, d);
            batteryDetailText.text = "<color=" + WorkshopTheme.Hex(pick.Color) + ">" + pick.displayName.ToUpper() + "</color>\n" +
                "New bots: " + Stats.Clock(after) + " per charge  <color=" + WorkshopTheme.Hex(d >= 0 ? WorkshopTheme.Green : WorkshopTheme.Red) + ">" + delta + "</color>";
        }
        else
        {
            batteryDetailText.text = "Every bot this factory builds gets the battery's runtime.\n" +
                "Battery slots: " + list.Count + " / " + (inv != null ? inv.batterySlots : 0) + "   Tap a battery to compare.";
        }
        batteryEquipButton.interactable = pick != null;
        if (batteryEquipLabel != null) batteryEquipLabel.text = eq != null ? "SWAP" : "EQUIP";
        batteryRemoveButton.interactable = eq != null;
        var rl = batteryRemoveButton.GetComponentInChildren<TMP_Text>();
        if (rl != null) rl.alpha = eq != null ? 1f : 0.35f;
        batteryEquipButton.GetComponentInChildren<TMP_Text>().alpha = pick != null ? 1f : 0.45f;
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
