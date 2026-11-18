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

    void Awake() { Instance = this; }

    public void Add(DiskProgram disk)
    {
        if (disk == null) return;
        disks.Add(disk);
        OnChanged?.Invoke();
    }

    public void SetDisks(List<DiskProgram> newDisks)
    {
        disks = new List<DiskProgram>(newDisks);
        OnChanged?.Invoke();
    }
}
