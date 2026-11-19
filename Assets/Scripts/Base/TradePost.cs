using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// Trade Post (Red Planet model): walk into its ring and tap the floating TRADE button → popup with two tabs,
/// BATTERIES and DISKS, where materials buy items for the player's inventory. Stock is unlimited.
/// Built from the base popup (BUILD tab) like the other buildings; prefab Assets/Prefabs/Buildings/TradePost.prefab
/// (Tools > Trash Game > 14. Trade Post). Prices are set on the prefab (0 = default price).
public class TradePost : MonoBehaviour
{
    public static readonly List<TradePost> All = new List<TradePost>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); }

    [Serializable]
    public class BatteryOffer
    {
        public BatteryItem item;
        [Tooltip("0 = default price for its rarity")]
        public int price;
        public int Price => price > 0 ? price : DefaultPrice(item);
    }

    [Serializable]
    public class DiskOffer
    {
        public DiskProgram disk;
        public int price = 50;
        public int Price => Mathf.Max(0, price);
    }

    [Tooltip("Stand this close to get the TRADE button")]
    public float radius = 3.6f;

    [Header("Stock (unlimited)")]
    public List<BatteryOffer> batteries = new List<BatteryOffer>();
    public List<DiskOffer> disks = new List<DiskOffer>();

    [Header("Visuals")]
    public TMP_Text label;
    public GroundRing ring;
    public string idleText = "Trade Post";
    [Tooltip("Turns slowly (the planet)")]
    public Transform spinner;
    public float spinSpeed = 10f;
    public float bobAmount = 0.12f;

    [Header("Popup icons (set by the builder)")]
    public Sprite batteryIcon;
    public Sprite diskIcon;

    Vector3 spinnerBase;

    public static int DefaultPrice(BatteryItem b)
    {
        if (b == null) return 0;
        switch (b.rarity)
        {
            case Rarity.Common: return 25;
            case Rarity.Uncommon: return 50;
            case Rarity.Rare: return 90;
            case Rarity.Epic: return 150;
            default: return 240;
        }
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Awake()
    {
        if (spinner != null) spinnerBase = spinner.localPosition;
        if (label != null) label.text = idleText;
    }

    void Update()
    {
        if (spinner == null) return;
        spinner.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        spinner.localPosition = spinnerBase + Vector3.up * Mathf.Sin(Time.time * 0.9f) * bobAmount;
    }

    public bool InRange(Vector3 worldPos)
    {
        Vector3 d = worldPos - transform.position; d.y = 0f;
        return d.sqrMagnitude <= radius * radius;
    }

    public void SetHighlight(bool on) { if (ring != null) ring.SetHighlight(on); }

    /// The trade post whose ring the position is in (nearest if several), or null.
    public static TradePost InRangeOf(Vector3 pos)
    {
        TradePost best = null;
        float bd = float.MaxValue;
        foreach (var t in All)
        {
            if (t == null || !t.InRange(pos)) continue;
            float d = (t.transform.position - pos).sqrMagnitude;
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    // ------------------------------------------------------------------ buying

    static bool Pay(int price)
    {
        if (BuildMode.FreeBuild) return true;
        return Economy.Instance != null && Economy.Instance.TrySpend(price);
    }

    public static bool CanAfford(int price) =>
        BuildMode.FreeBuild || (Economy.Instance != null && Economy.Instance.materials >= price);

    /// Why a battery can't be bought right now ("" = it can).
    public static string BatteryBlock(BatteryOffer o)
    {
        var inv = PlayerInventory.Instance;
        if (o == null || o.item == null || inv == null) return "UNAVAILABLE";
        if (!inv.batteries.Contains(o.item) && !inv.HasBatterySpace) return "NO FREE SLOT";
        if (!CanAfford(o.Price)) return "NOT ENOUGH";
        return "";
    }

    public static string DiskBlock(DiskOffer o)
    {
        if (o == null || o.disk == null || PlayerInventory.Instance == null) return "UNAVAILABLE";
        if (!CanAfford(o.Price)) return "NOT ENOUGH";
        return "";
    }

    public bool BuyBattery(BatteryOffer o)
    {
        string block = BatteryBlock(o);
        if (block == "NO FREE SLOT") { GameToast.Show("NO FREE BATTERY SLOT  (" + PlayerInventory.Instance.batterySlots + " KINDS MAX)", WorkshopTheme.Red); return false; }
        if (block != "") { GameToast.Show("NEED " + o.Price + " MATERIALS", WorkshopTheme.Red); return false; }
        if (!Pay(o.Price)) { GameToast.Show("NEED " + o.Price + " MATERIALS", WorkshopTheme.Red); return false; }
        PlayerInventory.Instance.AddBattery(o.item);
        GameToast.Show("BOUGHT " + o.item.displayName.ToUpper(), o.item.Color);
        return true;
    }

    public bool BuyDisk(DiskOffer o)
    {
        if (DiskBlock(o) != "" || !Pay(o.Price)) { GameToast.Show("NEED " + (o != null ? o.Price : 0) + " MATERIALS", WorkshopTheme.Red); return false; }
        PlayerInventory.Instance.Add(o.disk);
        GameToast.Show("BOUGHT DISK: " + o.disk.displayName.ToUpper(), o.disk.color);
        return true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.45f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
