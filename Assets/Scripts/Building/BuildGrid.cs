using System;
using System.Collections.Generic;
using UnityEngine;

/// Tile grid for base construction. Cells are CellSize metres; a cell is either free or owned by one GridFootprint.
/// Also answers "can I build here?" (bounds, buildings, fog, trash piles, loot on the ground).
public static class BuildGrid
{
    public static float CellSize = 2f;
    public static Vector2 Origin = Vector2.zero;
    /// Buildable area (world x/z). The level walls sit at ±39.5.
    public static Vector2 Min = new Vector2(-37f, -37f);
    public static Vector2 Max = new Vector2(37f, 37f);

    public enum CellState { Free, Occupied, OutOfBounds, Fog, Trash, Item }

    static readonly Dictionary<Vector2Int, GridFootprint> occupied = new Dictionary<Vector2Int, GridFootprint>();
    static readonly List<Vector3> items = new List<Vector3>();

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        occupied.Clear();
        items.Clear();
        Changed = null;
    }

    // ------------------------------------------------------------------ coordinates

    public static Vector2Int WorldToCell(Vector3 p) =>
        new Vector2Int(Mathf.FloorToInt((p.x - Origin.x) / CellSize), Mathf.FloorToInt((p.z - Origin.y) / CellSize));

    public static Vector3 CellCenter(Vector2Int c) =>
        new Vector3(Origin.x + (c.x + 0.5f) * CellSize, 0f, Origin.y + (c.y + 0.5f) * CellSize);

    /// Footprint size after rotating by quarter turns (90° swaps X and Z).
    public static Vector2Int Dims(Vector2Int size, int quarterTurns) =>
        (quarterTurns & 1) == 1 ? new Vector2Int(size.y, size.x) : size;

    /// Bottom-left cell of a footprint whose centre lands as close as possible to p.
    public static Vector2Int SnapBottomLeft(Vector3 p, Vector2Int dims) =>
        new Vector2Int(Mathf.RoundToInt((p.x - Origin.x) / CellSize - dims.x * 0.5f),
                       Mathf.RoundToInt((p.z - Origin.y) / CellSize - dims.y * 0.5f));

    public static Vector3 FootprintCenter(Vector2Int bl, Vector2Int dims) =>
        new Vector3(Origin.x + (bl.x + dims.x * 0.5f) * CellSize, 0f, Origin.y + (bl.y + dims.y * 0.5f) * CellSize);

    // ------------------------------------------------------------------ occupancy

    public static bool IsOccupied(Vector2Int c) => occupied.ContainsKey(c);
    public static GridFootprint OccupantOf(Vector2Int c) => occupied.TryGetValue(c, out var f) ? f : null;

    internal static void Register(GridFootprint f, List<Vector2Int> cells)
    {
        foreach (var c in cells) occupied[c] = f;
        Changed?.Invoke();
    }

    internal static void Unregister(GridFootprint f, List<Vector2Int> cells)
    {
        foreach (var c in cells)
            if (occupied.TryGetValue(c, out var o) && o == f) occupied.Remove(c);
        Changed?.Invoke();
    }

    public static bool InBounds(Vector2Int c)
    {
        var w = CellCenter(c);
        return w.x >= Min.x && w.x <= Max.x && w.z >= Min.y && w.z <= Max.y;
    }

    /// Call when entering build mode: remembers where loot lies so we don't build on top of it.
    public static void RefreshItems()
    {
        items.Clear();
        foreach (var d in UnityEngine.Object.FindObjectsByType<DiskPickup>(FindObjectsSortMode.None)) items.Add(d.transform.position);
        foreach (var b in UnityEngine.Object.FindObjectsByType<BatteryPickup>(FindObjectsSortMode.None)) items.Add(b.transform.position);
    }

    /// Full check of one cell (used for the footprint being placed).
    public static CellState Check(Vector2Int c)
    {
        if (!InBounds(c)) return CellState.OutOfBounds;
        if (occupied.ContainsKey(c)) return CellState.Occupied;
        var w = CellCenter(c);
        var fog = FogOfWar.Instance;
        if (fog != null && fog.isActiveAndEnabled && !fog.IsExplored(w)) return CellState.Fog;   // anywhere already explored (not the black fog)

        float half = CellSize * 0.5f;
        foreach (var p in TrashPile.All)
            if (p != null && !p.IsEmpty && DistToCell(p.transform.position, w, half) < p.pickupRadius * 0.8f) return CellState.Trash;
        foreach (var it in items)
            if (DistToCell(it, w, half) < 0.9f) return CellState.Item;
        return CellState.Free;
    }

    static float DistToCell(Vector3 p, Vector3 cellCenter, float half)
    {
        float dx = Mathf.Max(Mathf.Abs(p.x - cellCenter.x) - half, 0f);
        float dz = Mathf.Max(Mathf.Abs(p.z - cellCenter.z) - half, 0f);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public static string Describe(CellState s) => s switch
    {
        CellState.Occupied => "Blocked by a building",
        CellState.OutOfBounds => "Outside the base area",
        CellState.Fog => "Can't build in the dark fog: explore there first",
        CellState.Trash => "Too close to a trash pile",
        CellState.Item => "Pick up the item there first",
        _ => "",
    };
}
