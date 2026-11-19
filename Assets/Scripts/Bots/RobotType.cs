using System;
using UnityEngine;

/// Which kind of robot a disk works on / a factory builds. Flags, so a disk can fit several ("Any").
[Flags]
public enum RobotType
{
    None = 0,
    Gatherer = 1,
    Drone = 2,
    Any = Gatherer | Drone,
}

public static class RobotTypes
{
    /// "GATHERER" / "DRONE" / "ANY ROBOT": shown in toasts.
    public static string Name(RobotType t)
    {
        if (t == RobotType.Gatherer) return "GATHERER";
        if (t == RobotType.Drone) return "DRONE";
        return "ANY ROBOT";
    }

    /// Short text on the disk icon's compatibility badge (swap for sprites later).
    public static string Glyph(RobotType t)
    {
        if (t == RobotType.Gatherer) return "G";
        if (t == RobotType.Drone) return "D";
        return "G+D";
    }

    public static Color Color(RobotType t)
    {
        if (t == RobotType.Gatherer) return new Color(0.35f, 0.82f, 0.42f);
        if (t == RobotType.Drone) return new Color(1f, 0.42f, 0.30f);
        return new Color(0.62f, 0.66f, 0.72f);
    }
}
