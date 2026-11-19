using UnityEngine;

/// Hue / saturation / brightness shift for a colour, so a whole palette can be re-tinted with three sliders.
public static class ColorShift
{
    /// hue in degrees (-180..180), saturation and brightness as multipliers (1 = unchanged).
    public static Color Apply(Color c, float hue, float saturation, float brightness)
    {
        if (Mathf.Approximately(hue, 0f) && Mathf.Approximately(saturation, 1f) && Mathf.Approximately(brightness, 1f)) return c;
        Color.RGBToHSV(c, out float h, out float s, out float v);
        h = Mathf.Repeat(h + hue / 360f, 1f);
        s = Mathf.Clamp01(s * saturation);
        v = Mathf.Max(0f, v * brightness);
        var o = Color.HSVToRGB(h, s, v, true);
        o.a = c.a;
        return o;
    }
}
