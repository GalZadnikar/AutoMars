using System;
using System.Collections.Generic;
using UnityEngine;

/// Upgrade tree of one production building (Deep Rock Galactic style).
/// Each level-up costs materials and unlocks the next tier; in every unlocked tier the player
/// equips exactly one perk and can swap it any time for free.
/// Tier 0 unlocks at level 2, tier 1 at level 3, ...
[CreateAssetMenu(menuName = "Trash Game/Building Upgrade Tree", fileName = "UpgradeTree")]
public class BuildingUpgradeTree : ScriptableObject
{
    [Header("Names shown in the Workshop")]
    public string displayName = "GATHERER FACTORY";
    public string category = "PRODUCTION BUILDING";
    public string outputName = "SCOUT BOT";
    [Tooltip("Text before the output name in the showcase")]
    public string outputPrefix = "OUTPUT:  ";
    [Tooltip("Level bar label, e.g. FACTORY LEVEL / PLAYER LEVEL")]
    public string levelLabel = "FACTORY LEVEL";
    [Tooltip("First row of the stats list")]
    public string levelRowLabel = "Robot Level";
    [Tooltip("Battery + Master Disk slots under the model (factories only)")]
    public bool hasSlots = true;
    [Tooltip("Stats listed in the Workshop. Empty = the factory stats")]
    public List<StatType> shownStats = new List<StatType>();

    [Header("What it builds")]
    [Tooltip("Only disks that fit this robot type can go into the Master Disk slot")]
    public RobotType robotType = RobotType.Gatherer;
    [Tooltip("Singular word on the factory popup / button: BOT, DRONE")]
    public string unitWord = "BOT";
    [Tooltip("Level-1 values that replace the ones read from the factory / bot prefab (e.g. the drone's weapon stats)")]
    public List<StatMod> baseStats = new List<StatMod>();

    [Header("Leveling")]
    [Tooltip("Materials to go from level N to N+1 (element 0 = level 1 -> 2). Count + 1 = max level.")]
    public List<int> levelCosts = new List<int> { 30, 60, 120, 200, 320 };

    [Tooltip("Applied once for every level above 1: the robots' tech-level growth.")]
    public List<StatMod> perLevel = new List<StatMod>();

    [Tooltip("Factory level at which the Master Disk slot unlocks (Module 6).")]
    public int masterDiskUnlockLevel = 4;

    [Header("Tiers (tier 0 unlocks at level 2)")]
    public List<UpgradeTier> tiers = new List<UpgradeTier>();

    public int MaxLevel => levelCosts.Count + 1;
    public int TierUnlockLevel(int tier) => tier + 2;
}

[Serializable]
public class UpgradeTier
{
    public List<Perk> options = new List<Perk>();
}

[Serializable]
public class Perk
{
    public string title = "Perk";
    [Tooltip("Optional one-liner shown under the stat changes")]
    [TextArea] public string flavor;
    [Tooltip("Short text drawn on the hex when there's no icon sprite")]
    public string glyph = "?";
    public Sprite icon;
    public List<StatMod> mods = new List<StatMod>();
}
