using System;
using UnityEngine;

/// Holds the player's currencies. Later: upgrades/shop call TrySpend().
public class Economy : MonoBehaviour
{
    public static Economy Instance { get; private set; }

    public int materials;
    public event Action<int> OnMaterialsChanged;

    void Awake() { Instance = this; }

    public void AddMaterials(int amount)
    {
        materials += amount;
        OnMaterialsChanged?.Invoke(materials);
    }

    public bool TrySpend(int amount)
    {
        if (materials < amount) return false;
        materials -= amount;
        OnMaterialsChanged?.Invoke(materials);
        return true;
    }
}
