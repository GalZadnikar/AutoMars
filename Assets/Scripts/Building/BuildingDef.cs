using UnityEngine;

/// One entry of the build catalog. The prefab needs a GridFootprint (its size = tiles used).
[CreateAssetMenu(menuName = "Trash Game/Building", fileName = "Build_")]
public class BuildingDef : ScriptableObject
{
    public string displayName = "Building";
    [TextArea] public string description;
    public int cost = 50;
    [Tooltip("Seconds of construction after placing")]
    public float buildTime = 5f;
    public GameObject prefab;
    public Color color = new Color32(240, 160, 46, 255);
    [Tooltip("Letter(s) on the catalog card")]
    public string glyph = "B";

    public GridFootprint Footprint => prefab != null ? prefab.GetComponent<GridFootprint>() : null;
}
