using System.Collections.Generic;
using UnityEngine;

/// The tiles a building covers. Registers itself on the BuildGrid while enabled.
/// Placed buildings line up exactly with cells; older hand-placed ones claim every cell they overlap.
[DisallowMultipleComponent]
public class GridFootprint : MonoBehaviour
{
    [Tooltip("Cells along local X and local Z (at 0° rotation)")]
    public Vector2Int size = new Vector2Int(3, 3);
    [Tooltip("Local X/Z offset in metres from this transform to the footprint centre")]
    public Vector2 centerOffset;
    [Tooltip("Hand-placed in the scene: flatten the ground under it when the game starts (built ones are leveled when placed)")]
    public bool levelGround = true;

    readonly List<Vector2Int> cells = new List<Vector2Int>();
    public IReadOnlyList<Vector2Int> Cells => cells;

    public int QuarterTurns => Mathf.RoundToInt(transform.eulerAngles.y / 90f) & 3;

    void OnEnable()
    {
        Compute();
        BuildGrid.Register(this, cells);
    }

    void OnDisable() => BuildGrid.Unregister(this, cells);

    /// Footprint centre in world space.
    public Vector3 Center => transform.position + transform.rotation * new Vector3(centerOffset.x, 0f, centerOffset.y);

    void Compute()
    {
        cells.Clear();
        var dims = BuildGrid.Dims(size, QuarterTurns);
        Vector3 c = Center;
        float cs = BuildGrid.CellSize;
        float hx = dims.x * cs * 0.5f - 0.05f, hz = dims.y * cs * 0.5f - 0.05f;
        var a = BuildGrid.WorldToCell(new Vector3(c.x - hx, 0f, c.z - hz));
        var b = BuildGrid.WorldToCell(new Vector3(c.x + hx, 0f, c.z + hz));
        for (int x = a.x; x <= b.x; x++)
            for (int z = a.y; z <= b.y; z++)
                cells.Add(new Vector2Int(x, z));
    }

    void OnDrawGizmosSelected()
    {
        var dims = BuildGrid.Dims(size, QuarterTurns);
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(Center + Vector3.up * 0.1f, new Vector3(dims.x * BuildGrid.CellSize, 0.2f, dims.y * BuildGrid.CellSize));
    }
}
