using UnityEngine;

/// BIOME PROTOTYPE - what one kind of ground looks like.
/// Create with: right-click in the Project window > Create > Biome Prototype > Biome.
/// Each triangle of the ground gets a random tone between Colour A and Colour B (low-poly patchwork).
[CreateAssetMenu(menuName = "Biome Prototype/Biome", fileName = "Biome")]
public class BiomeDef : ScriptableObject
{
    public Color groundA = new Color(0.8f, 0.52f, 0.33f);
    public Color groundB = new Color(0.76f, 0.48f, 0.3f);
    [Tooltip("Colour of the small rocks scattered on this ground")]
    public Color rock = new Color(0.55f, 0.34f, 0.21f);

    [Header("Colour Shift (applied on top of the colours above)")]
    [Range(-180f, 180f)] public float hueShift = 0f;
    [Range(0f, 2f)] public float saturation = 1f;
    [Range(0f, 2f)] public float brightness = 1f;

    public Color GroundA => ColorShift.Apply(groundA, hueShift, saturation, brightness);
    public Color GroundB => ColorShift.Apply(groundB, hueShift, saturation, brightness);
    public Color Rock => ColorShift.Apply(rock, hueShift, saturation, brightness);

#if UNITY_EDITOR
    // changing colours in the inspector updates the ground right away
    void OnValidate() => BiomeZone.NotifyChanged();
#endif
}
