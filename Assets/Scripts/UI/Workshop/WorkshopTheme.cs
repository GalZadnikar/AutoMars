using UnityEngine;

/// Colours of the Workshop (DRG-inspired: warm dark browns, orange + yellow accents).
/// Change them here and re-run Tools > Trash Game > 3. Build Workshop.
public static class WorkshopTheme
{
    public static readonly Color Bg = new Color32(35, 30, 23, 255);
    public static readonly Color Panel = new Color32(47, 40, 31, 255);
    public static readonly Color PanelDark = new Color32(26, 22, 17, 255);
    public static readonly Color Rule = new Color32(240, 160, 46, 255);
    public static readonly Color Orange = new Color32(240, 160, 46, 255);
    public static readonly Color Yellow = new Color32(255, 200, 61, 255);
    public static readonly Color White = new Color32(242, 237, 228, 255);
    public static readonly Color Muted = new Color32(163, 152, 133, 255);
    public static readonly Color LockedFill = new Color32(60, 53, 43, 255);
    public static readonly Color LockedGlyph = new Color32(118, 107, 90, 255);
    public static readonly Color DarkText = new Color32(34, 27, 19, 255);
    public static readonly Color Green = new Color32(139, 224, 122, 255);
    public static readonly Color Red = new Color32(240, 106, 85, 255);
    public static readonly Color Cyan = new Color32(111, 211, 238, 255);
    public static readonly Color ButtonGreen = new Color32(92, 186, 88, 255);
    public static readonly Color ButtonOff = new Color32(78, 70, 58, 255);

    public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
}
