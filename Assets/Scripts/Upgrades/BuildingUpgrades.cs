using System;
using System.Collections.Generic;
using UnityEngine;

/// Level + equipped perks of one production building. Sits next to BotFactory.
/// Base values come from where they already live (factory inspector + the bot prefab),
/// the tree adds per-level growth and perks on top.
public class BuildingUpgrades : MonoBehaviour
{
    public BuildingUpgradeTree tree;

    [Tooltip("Battery in the factory's slot: every bot built here gets its runtime. Empty = the bot prefab's built-in cell.")]
    public BatteryItem equippedBattery;

    [Tooltip("Master Disk (unlocks at tree.masterDiskUnlockLevel): every new bot comes out running this program.")]
    public DiskProgram masterDisk;

    [SerializeField] int level = 1;
    [Tooltip("Equipped option per tier, -1 = nothing equipped yet")]
    [SerializeField] List<int> picks = new List<int>();

    /// Fired after a level-up or a perk change.
    public event Action OnChanged;

    BotFactory factory;
    StatBlock current;

    public int Level => level;
    public int MaxLevel => tree != null ? tree.MaxLevel : 1;
    public bool IsMaxLevel => level >= MaxLevel;
    public int NextLevelCost => IsMaxLevel || tree == null ? 0 : tree.levelCosts[level - 1];
    public bool CanAffordLevelUp => !IsMaxLevel && Economy.Instance != null && Economy.Instance.materials >= NextLevelCost;
    public int TierCount => tree != null ? tree.tiers.Count : 0;
    public bool MasterDiskUnlocked => tree != null && level >= tree.masterDiskUnlockLevel;

    /// Stats with the current level and equipped perks. Cached; refreshed on change.
    public StatBlock Current => current ??= ComputeStats();

    BotFactory Factory => factory != null ? factory : (factory = GetComponent<BotFactory>());

    void Awake() => EnsurePicks();

    void OnValidate()
    {
        EnsurePicks();
        current = null;
    }

    void EnsurePicks()
    {
        picks ??= new List<int>();
        while (picks.Count < TierCount) picks.Add(-1);
    }

    public bool IsTierUnlocked(int tier) => tree != null && level >= tree.TierUnlockLevel(tier);

    public int GetPick(int tier)
    {
        EnsurePicks();
        return tier >= 0 && tier < picks.Count ? picks[tier] : -1;
    }

    /// Unlocked tiers that don't have a perk equipped yet ("skill points" to spend).
    public int UnspentPoints
    {
        get
        {
            int n = 0;
            for (int t = 0; t < TierCount; t++)
                if (IsTierUnlocked(t) && GetPick(t) < 0) n++;
            return n;
        }
    }

    public bool TryLevelUp()
    {
        if (IsMaxLevel || Economy.Instance == null) return false;
        if (!Economy.Instance.TrySpend(NextLevelCost)) return false;
        level++;
        Changed();
        return true;
    }

    /// Equip option `option` in tier `tier`. Swapping is free once the tier is unlocked.
    public bool Equip(int tier, int option)
    {
        if (tree == null || tier < 0 || tier >= TierCount || !IsTierUnlocked(tier)) return false;
        if (option < 0 || option >= tree.tiers[tier].options.Count) return false;
        EnsurePicks();
        if (picks[tier] == option) return false;
        picks[tier] = option;
        Changed();
        return true;
    }

    /// Debug: jump to a level for free.
    public void DevSetLevel(int newLevel)
    {
        level = Mathf.Clamp(newLevel, 1, MaxLevel);
        Changed();
    }

    /// Debug: back to level 1 with nothing equipped.
    public void DevReset()
    {
        level = 1;
        for (int i = 0; i < picks.Count; i++) picks[i] = -1;
        Changed();
    }

    /// Moves a battery from the player's inventory into the slot (the old one goes back).
    public bool EquipBattery(BatteryItem b)
    {
        var inv = PlayerInventory.Instance;
        if (b == null || inv == null || !inv.RemoveBattery(b)) return false;
        var old = equippedBattery;
        equippedBattery = b;
        if (old != null) inv.AddBattery(old);
        Changed();
        return true;
    }

    public bool UnequipBattery()
    {
        var inv = PlayerInventory.Instance;
        if (equippedBattery == null || inv == null || !inv.AddBattery(equippedBattery)) return false;
        equippedBattery = null;
        Changed();
        return true;
    }

    /// Moves a disk from the player's inventory into the Master Disk slot (the old one goes back).
    public bool EquipMasterDisk(DiskProgram disk)
    {
        var inv = PlayerInventory.Instance;
        if (disk == null || inv == null || !MasterDiskUnlocked || !inv.RemoveDisk(disk)) return false;
        var old = masterDisk;
        masterDisk = disk;
        if (old != null) inv.Add(old);
        Changed();
        return true;
    }

    public bool UnequipMasterDisk()
    {
        var inv = PlayerInventory.Instance;
        if (masterDisk == null || inv == null) return false;
        inv.Add(masterDisk);
        masterDisk = null;
        Changed();
        return true;
    }

    /// The program new bots get, or null (slot empty / still locked).
    public DiskProgram AutoProgram => MasterDiskUnlocked ? masterDisk : null;

    void Changed()
    {
        current = null;
        OnChanged?.Invoke();
    }

    /// Stats for the current setup. Pass previewTier/previewOption to see what a different pick
    /// in one tier would give (used by the Workshop to show green/red deltas).
    public StatBlock ComputeStats(int previewTier = -1, int previewOption = -1, bool previewBattery = false, BatteryItem battery = null)
    {
        var s = new StatBlock { Level = level };

        // --- base values (where they're tuned today) ---
        var f = Factory;
        var bot = f != null && f.botPrefab != null ? f.botPrefab.GetComponent<ScoutBot>() : null;
        s[StatType.MoveSpeed] = bot != null ? bot.moveSpeed : 3.5f;
        s[StatType.CarryCapacity] = bot != null ? bot.capacity : 5;
        s[StatType.CollectRate] = 1f / Mathf.Max(0.01f, bot != null ? bot.pickupInterval : 0.5f);
        s[StatType.UnloadRate] = 1f / Mathf.Max(0.01f, bot != null ? bot.depositInterval : 0.2f);
        s[StatType.BuildTime] = f != null ? f.buildTime : 30f;
        s[StatType.BotCost] = f != null ? f.costPerBot : 10;
        var cell = previewBattery ? battery : equippedBattery;
        s[StatType.BatteryRuntime] = cell != null ? cell.runtimeSeconds : (bot != null ? bot.batteryRuntime : 60f);

        if (tree == null) return s;

        int n = Stats.All.Length;
        var flat = new float[n];
        var pct = new float[n];

        void Add(List<StatMod> mods, int times = 1)
        {
            foreach (var m in mods)
            {
                if (m.op == ModOp.AddFlat) flat[(int)m.stat] += m.value * times;
                else pct[(int)m.stat] += m.value * times;
            }
        }

        Add(tree.perLevel, level - 1);

        for (int t = 0; t < TierCount; t++)
        {
            int p = t == previewTier ? previewOption : (IsTierUnlocked(t) ? GetPick(t) : -1);
            if (p >= 0 && p < tree.tiers[t].options.Count) Add(tree.tiers[t].options[p].mods);
        }

        foreach (var st in Stats.All)
        {
            int i = (int)st;
            float v = (s[st] + flat[i]) * Mathf.Max(0.05f, 1f + pct[i] / 100f);
            if (Stats.IsInteger(st)) v = Mathf.Max(1, Mathf.RoundToInt(v));
            s[st] = v;
        }
        return s;
    }
}
