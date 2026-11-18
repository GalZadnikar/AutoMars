using System;
using System.Collections.Generic;
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
    public bool HasBatterySpace => batteries.Count < batterySlots;

    public bool AddBattery(BatteryItem b)
    {
        if (b == null || !HasBatterySpace) return false;
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
