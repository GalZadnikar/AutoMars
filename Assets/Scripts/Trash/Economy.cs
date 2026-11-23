using System;
using UnityEngine;

public enum ResourceKind { Coins, Gems, Sand, Stone }

/// Holds everything the player owns in numbers: the two core currencies (coins, gems), the collected
/// materials (sand, stone) and XP. `materials` is the sand counter: it is the old single 'materials'
/// value, so buildings and upgrade trees that pay with AddMaterials / TrySpend(int) keep working.
public class Economy : MonoBehaviour
{
    public static Economy Instance { get; private set; }

    [Header("Core currencies")]
    public int coins;
    public int gems;

    [Header("Materials")]
    [Tooltip("Sand (the old 'materials' counter)")]
    public int materials;
    public int stone;

    [Header("Player")]
    [Tooltip("Earned by completing tasks (later); will pay for player upgrades")]
    public int xp;

    public event Action<int> OnMaterialsChanged;
    /// Any counter changed.
    public event Action OnChanged;

    void Awake() { Instance = this; }

    public int Get(ResourceKind r)
    {
        switch (r)
        {
            case ResourceKind.Coins: return coins;
            case ResourceKind.Gems: return gems;
            case ResourceKind.Sand: return materials;
            default: return stone;
        }
    }

    void Set(ResourceKind r, int v)
    {
        switch (r)
        {
            case ResourceKind.Coins: coins = v; break;
            case ResourceKind.Gems: gems = v; break;
            case ResourceKind.Sand: materials = v; break;
            default: stone = v; break;
        }
        if (r == ResourceKind.Sand) OnMaterialsChanged?.Invoke(materials);
        OnChanged?.Invoke();
    }

    public void Add(ResourceKind r, int amount) => Set(r, Mathf.Max(0, Get(r) + amount));

    public bool TrySpend(ResourceKind r, int amount)
    {
        if (Get(r) < amount) return false;
        Set(r, Get(r) - amount);
        return true;
    }

    public void AddXp(int amount)
    {
        xp = Mathf.Max(0, xp + amount);
        OnChanged?.Invoke();
    }

    // the old single-material API (sand)
    public void AddMaterials(int amount) => Add(ResourceKind.Sand, amount);
    public bool TrySpend(int amount) => TrySpend(ResourceKind.Sand, amount);

    public static string Name(ResourceKind r) => r.ToString().ToUpperInvariant();
}
