using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// Disks the player is holding.
public class PlayerInventory : MonoBehaviour
{
    public static PlayerInventory Instance { get; private set; }

    public List<DiskProgram> disks = new List<DiskProgram>();
    public event Action OnChanged;

    public int DiskCount => disks.Count;

    [Header("Batteries (dedicated slots)")]
    public int batterySlots = 6;
    public List<BatteryItem> batteries = new List<BatteryItem>();
    /// Identical batteries share one slot (shown as "x2"), so slots count distinct batteries.
    public int BatterySlotsUsed => batteries.Distinct().Count();
    public bool HasBatterySpace => BatterySlotsUsed < batterySlots;

    public int CountOf(BatteryItem b) => batteries.Count(x => x == b);
    public int CountOf(DiskProgram d) => disks.Count(x => x == d);

    /// Batteries grouped by kind, in order of first pickup: one entry per slot.
    public List<(BatteryItem item, int count)> BatteryStacks()
    {
        var list = new List<(BatteryItem, int)>();
        foreach (var b in batteries.Distinct()) list.Add((b, CountOf(b)));
        return list;
    }

    /// Disks grouped by program, in order of first pickup.
    public List<(DiskProgram item, int count)> DiskStacks()
    {
        var list = new List<(DiskProgram, int)>();
        foreach (var d in disks.Distinct()) list.Add((d, CountOf(d)));
        return list;
    }

    public bool AddBattery(BatteryItem b)
    {
        if (b == null) return false;
        if (!batteries.Contains(b) && !HasBatterySpace) return false;   // stacking onto an owned kind needs no free slot
        batteries.Add(b);
        OnChanged?.Invoke();
        return true;
    }

    public bool RemoveBattery(BatteryItem b)
    {
        if (!batteries.Remove(b)) return false;
        OnChanged?.Invoke();
        return true;
    }

    void Awake() { Instance = this; }

    public void Add(DiskProgram disk)
    {
        if (disk == null) return;
        disks.Add(disk);
        OnChanged?.Invoke();
    }

    public bool RemoveDisk(DiskProgram disk)
    {
        if (!disks.Remove(disk)) return false;
        OnChanged?.Invoke();
        return true;
    }

    public void SetDisks(List<DiskProgram> newDisks)
    {
        disks = new List<DiskProgram>(newDisks);
        OnChanged?.Invoke();
    }
}
