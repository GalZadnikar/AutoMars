using UnityEngine;

public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

/// A lootable battery. Equipped into a production building, it gives every robot built there its runtime.
/// Assets live in Resources/Batteries so code (debug menu, future loot tables) can find them.
[CreateAssetMenu(menuName = "Trash Game/Battery", fileName = "Battery")]
public class BatteryItem : ScriptableObject
{
    public Rarity rarity;
    public string displayName = "Battery";
    [Tooltip("Seconds of active work on a full charge")]
    public float runtimeSeconds = 120f;

    public Color Color => RarityColor(rarity);

    public static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Common => new Color32(215, 215, 215, 255),
        Rarity.Uncommon => new Color32(110, 225, 100, 255),
        Rarity.Rare => new Color32(80, 175, 255, 255),
        Rarity.Epic => new Color32(195, 120, 255, 255),
        _ => new Color32(255, 165, 40, 255),
    };

    public static BatteryItem[] LoadAll() => Resources.LoadAll<BatteryItem>("Batteries");
}
