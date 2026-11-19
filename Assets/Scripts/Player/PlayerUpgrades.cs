using UnityEngine;

/// The player's upgrade tree, the same system as the robot factory (BuildingUpgrades + a BuildingUpgradeTree asset):
/// LEVEL UP costs materials, grows the stats a little (tree.perLevel) and unlocks the next tier of perks
/// (pick one per tier, swap any time for free). Opened from the base popup's PLAYER tab in the Workshop screen.
/// Every level also lengthens the pipe: pipe level = player level (PipeTether.rangePerLevel; the H2O pump at pumpAtLevel).
/// The level-1 values below are the source of truth: they overwrite PlayerCarrier / JoystickPlayerController on start.
[RequireComponent(typeof(BuildingUpgrades))]
public class PlayerUpgrades : MonoBehaviour, IUpgradeBase
{
    public static PlayerUpgrades Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [Header("Level 1 stats (before level growth and perks)")]
    public int capacity = 5;
    [Tooltip("Items picked up per second")]
    public float collectRate = 3f;
    [Tooltip("Items dropped at the base per second")]
    public float unloadRate = 6f;
    [Tooltip("Metres per second")]
    public float moveSpeed = 4.2f;

    [Header("Workshop showcase")]
    [Tooltip("Model shown in the Workshop (the astronaut prefab)")]
    public GameObject previewPrefab;

    BuildingUpgrades upgrades;
    PlayerCarrier carrier;
    JoystickPlayerController mover;

    public BuildingUpgrades Upgrades => upgrades != null ? upgrades : (upgrades = GetComponent<BuildingUpgrades>());
    public StatBlock Current => Upgrades.Current;
    public int Level => Upgrades.Level;

    void Awake()
    {
        Instance = this;
        carrier = GetComponent<PlayerCarrier>();
        mover = GetComponent<JoystickPlayerController>();
        Upgrades.OnChanged += Apply;
    }

    void Start() => Apply();   // after PipeTether.Awake

    void OnDestroy()
    {
        if (upgrades != null) upgrades.OnChanged -= Apply;
        if (Instance == this) Instance = null;
    }

    public void FillBase(StatBlock s)
    {
        s[StatType.CarryCapacity] = capacity;
        s[StatType.CollectRate] = collectRate;
        s[StatType.UnloadRate] = unloadRate;
        s[StatType.MoveSpeed] = moveSpeed;
        var pt = PipeTether.Instance;
        s[StatType.PipeRange] = pt != null ? pt.RangeAt(s.Level) : 12f;
    }

    /// Pushes the current stats into the player + the pipe.
    public void Apply()
    {
        var s = Current;
        if (carrier != null)
        {
            carrier.capacity = s.Int(StatType.CarryCapacity);
            carrier.pickupInterval = 1f / Mathf.Max(0.1f, s[StatType.CollectRate]);
            carrier.depositInterval = 1f / Mathf.Max(0.1f, s[StatType.UnloadRate]);
        }
        if (mover != null) mover.moveSpeed = s[StatType.MoveSpeed];

        var pt = PipeTether.Instance;
        if (pt != null)
        {
            float baseRange = pt.RangeAt(Level);
            pt.bonusRange = baseRange >= Stats.NoLimit * 0.5f ? 0f : s[StatType.PipeRange] - baseRange;   // perks
            if (pt.level != Level) pt.SetLevel(Level);   // also swaps the pipe for the H2O pump at its level
            else pt.SetLevel(pt.level);                  // redraw the range circle
        }
    }

    public static PlayerUpgrades Ensure()
    {
        if (Instance != null) return Instance;
        var carrier = FindFirstObjectByType<PlayerCarrier>();
        if (carrier == null) return null;
        var u = carrier.GetComponent<PlayerUpgrades>();
        return u != null ? u : carrier.gameObject.AddComponent<PlayerUpgrades>();
    }
}
