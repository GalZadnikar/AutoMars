using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public enum ToolKind { Shovel, Pickaxe }

/// The levels of the player's tools (shovel for sand, pickaxe for rocks). Upgrades are bought at the base
/// with collected materials (sand / stone). The tables below are the source of truth: the current level's
/// numbers overwrite PlayerShovel / PlayerMelee at start and after every upgrade.
/// Level 1 = the values the tools had before this system, so nothing changes until the first upgrade.
public class ToolProgress : MonoBehaviour
{
    public static ToolProgress Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [Serializable]
    public class ShovelLevel
    {
        [Tooltip("Sand taken per dig")] public float bite = 1f;
        [Tooltip("How far the dig reaches (m)")] public float reach = 1.5f;
        [Tooltip("Dig animation speed")] public float digSpeed = 1f;
        [Tooltip("Cost to buy this level")] public int sandCost, stoneCost;
    }

    [Serializable]
    public class PickaxeLevel
    {
        public float damage = 3f;
        [Tooltip("Swing animation speed")] public float swingSpeed = 1f;
        [Tooltip("Reach to the edge of the target (m)")] public float reach = 1f;
        [Tooltip("Cost to buy this level")] public int sandCost, stoneCost;
    }

    public struct Stat
    {
        public string label, text;
        public Stat(string label, float value, string format) { this.label = label; text = value.ToString(format, CultureInfo.InvariantCulture); }
    }

    [Min(1)] public int shovelLevel = 1;
    [Min(1)] public int pickaxeLevel = 1;

    public List<ShovelLevel> shovelLevels = new List<ShovelLevel>
    {
        new ShovelLevel { bite = 1.0f, reach = 1.5f, digSpeed = 1.00f },
        new ShovelLevel { bite = 1.3f, reach = 1.7f, digSpeed = 1.10f, sandCost = 15 },
        new ShovelLevel { bite = 1.6f, reach = 1.9f, digSpeed = 1.20f, sandCost = 35 },
        new ShovelLevel { bite = 2.0f, reach = 2.1f, digSpeed = 1.30f, sandCost = 70, stoneCost = 10 },
        new ShovelLevel { bite = 2.5f, reach = 2.3f, digSpeed = 1.45f, sandCost = 120, stoneCost = 25 },
    };

    public List<PickaxeLevel> pickaxeLevels = new List<PickaxeLevel>
    {
        new PickaxeLevel { damage = 3f, swingSpeed = 1.0f, reach = 1.0f },
        new PickaxeLevel { damage = 4f, swingSpeed = 1.1f, reach = 1.0f, sandCost = 10, stoneCost = 6 },
        new PickaxeLevel { damage = 6f, swingSpeed = 1.2f, reach = 1.1f, sandCost = 25, stoneCost = 15 },
        new PickaxeLevel { damage = 8f, swingSpeed = 1.3f, reach = 1.2f, sandCost = 50, stoneCost = 30 },
        new PickaxeLevel { damage = 12f, swingSpeed = 1.4f, reach = 1.3f, sandCost = 90, stoneCost = 50 },
    };

    [Header("UI")]
    public Sprite shovelIcon;
    public Sprite pickaxeIcon;

    public event Action OnChanged;

    PlayerShovel shovel;
    PlayerMelee melee;

    void Awake()
    {
        Instance = this;
        shovel = GetComponent<PlayerShovel>();
        melee = GetComponent<PlayerMelee>();
    }

    void Start() => Apply();

    void OnDestroy() { if (Instance == this) Instance = null; }

    public int Level(ToolKind k) => k == ToolKind.Shovel ? shovelLevel : pickaxeLevel;
    public int MaxLevel(ToolKind k) => k == ToolKind.Shovel ? shovelLevels.Count : pickaxeLevels.Count;
    public bool IsMax(ToolKind k) => Level(k) >= MaxLevel(k);
    public Sprite Icon(ToolKind k) => k == ToolKind.Shovel ? shovelIcon : pickaxeIcon;
    public static string Name(ToolKind k) => k == ToolKind.Shovel ? "SHOVEL" : "PICKAXE";
    public static string Description(ToolKind k) => k == ToolKind.Shovel
        ? "Digs sand. Walk into sand and he digs his way through. Unload at the base."
        : "Breaks rocks for stone. Stand next to a rock and he starts swinging.";

    /// Cost of the next level (0 / 0 at max level).
    public void NextCost(ToolKind k, out int sand, out int stone)
    {
        sand = stone = 0;
        if (IsMax(k)) return;
        int next = Level(k);   // index of the next level
        if (k == ToolKind.Shovel) { sand = shovelLevels[next].sandCost; stone = shovelLevels[next].stoneCost; }
        else { sand = pickaxeLevels[next].sandCost; stone = pickaxeLevels[next].stoneCost; }
    }

    public bool CanAfford(ToolKind k)
    {
        if (IsMax(k)) return false;
        if (BuildMode.FreeBuild) return true;
        var e = Economy.Instance;
        if (e == null) return false;
        NextCost(k, out int sand, out int stone);
        return e.Get(ResourceKind.Sand) >= sand && e.Get(ResourceKind.Stone) >= stone;
    }

    public bool TryUpgrade(ToolKind k)
    {
        if (!CanAfford(k)) return false;
        if (!BuildMode.FreeBuild)
        {
            NextCost(k, out int sand, out int stone);
            var e = Economy.Instance;
            e.TrySpend(ResourceKind.Sand, sand);
            e.TrySpend(ResourceKind.Stone, stone);
        }
        if (k == ToolKind.Shovel) shovelLevel++; else pickaxeLevel++;
        Apply();
        return true;
    }

    /// Debug / save: set a level directly.
    public void SetLevel(ToolKind k, int level)
    {
        level = Mathf.Clamp(level, 1, MaxLevel(k));
        if (k == ToolKind.Shovel) shovelLevel = level; else pickaxeLevel = level;
        Apply();
    }

    /// The numbers shown in the UI for a tool at a level (1-based).
    public List<Stat> Stats(ToolKind k, int level)
    {
        var list = new List<Stat>();
        level = Mathf.Clamp(level, 1, MaxLevel(k));
        if (k == ToolKind.Shovel)
        {
            var l = shovelLevels[level - 1];
            list.Add(new Stat("SAND PER DIG", l.bite, "0.0"));
            list.Add(new Stat("REACH", l.reach, "0.0"));
            list.Add(new Stat("DIG SPEED", l.digSpeed, "0.00"));
        }
        else
        {
            var l = pickaxeLevels[level - 1];
            list.Add(new Stat("DAMAGE", l.damage, "0"));
            list.Add(new Stat("SWING SPEED", l.swingSpeed, "0.00"));
            list.Add(new Stat("REACH", l.reach, "0.0"));
        }
        return list;
    }

    /// Pushes the current levels into the tools.
    public void Apply()
    {
        shovelLevel = Mathf.Clamp(shovelLevel, 1, Mathf.Max(1, shovelLevels.Count));
        pickaxeLevel = Mathf.Clamp(pickaxeLevel, 1, Mathf.Max(1, pickaxeLevels.Count));
        if (shovel != null && shovelLevels.Count > 0)
        {
            var l = shovelLevels[shovelLevel - 1];
            shovel.bite = l.bite;
            shovel.reach = l.reach;
            shovel.attackSpeed = l.digSpeed;
        }
        if (melee != null && pickaxeLevels.Count > 0)
        {
            var l = pickaxeLevels[pickaxeLevel - 1];
            melee.damage = l.damage;
            melee.attackSpeed = l.swingSpeed;
            melee.range = l.reach;
        }
        OnChanged?.Invoke();
    }
}
