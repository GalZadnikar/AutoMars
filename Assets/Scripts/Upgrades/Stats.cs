using System;
using UnityEngine;

/// Stats a production building can change, on itself (build time, cost) or on the robots it builds.
/// Add a new stat: add it here, give it a label/format below, read it where it's used.
public enum StatType
{
    MoveSpeed,
    CarryCapacity,
    CollectRate,
    UnloadRate,
    BuildTime,
    BotCost,
}

public enum ModOp { AddFlat, AddPercent }

/// One stat change, e.g. "+25% Move Speed" or "+2 Carry Capacity".
[Serializable]
public struct StatMod
{
    public StatType stat;
    public ModOp op;
    public float value;

    public StatMod(StatType stat, ModOp op, float value)
    {
        this.stat = stat;
        this.op = op;
        this.value = value;
    }

    /// True when this change makes things better for the player (green), false when it's a drawback (red).
    public bool IsGood => Stats.LowerIsBetter(stat) ? value < 0f : value > 0f;

    public string Describe()
    {
        string sign = value >= 0f ? "+" : "-";
        float a = Mathf.Abs(value);
        string num = op == ModOp.AddPercent ? a.ToString("0.#") + "%" : a.ToString("0.#");
        return sign + num + " " + Stats.Label(stat);
    }
}

public static class Stats
{
    public static readonly StatType[] All = (StatType[])Enum.GetValues(typeof(StatType));

    public static string Label(StatType s) => s switch
    {
        StatType.MoveSpeed => "Move Speed",
        StatType.CarryCapacity => "Carry Capacity",
        StatType.CollectRate => "Collect Rate",
        StatType.UnloadRate => "Unload Rate",
        StatType.BuildTime => "Build Time",
        StatType.BotCost => "Bot Cost",
        _ => s.ToString(),
    };

    public static bool LowerIsBetter(StatType s) => s == StatType.BuildTime || s == StatType.BotCost;

    public static bool IsInteger(StatType s) => s == StatType.CarryCapacity || s == StatType.BotCost;

    public static string Format(StatType s, float v) => s switch
    {
        StatType.MoveSpeed => v.ToString("0.0") + " m/s",
        StatType.CarryCapacity => Mathf.RoundToInt(v).ToString(),
        StatType.CollectRate => v.ToString("0.0") + "/s",
        StatType.UnloadRate => v.ToString("0.0") + "/s",
        StatType.BuildTime => v.ToString("0.#") + "s",
        StatType.BotCost => Mathf.RoundToInt(v).ToString(),
        _ => v.ToString("0.##"),
    };

    /// "+0.9" / "-3" style difference, or "" when there's no visible change.
    public static string FormatDelta(StatType s, float delta)
    {
        if (IsInteger(s))
        {
            int d = Mathf.RoundToInt(delta);
            return d == 0 ? "" : (d > 0 ? "+" : "-") + Mathf.Abs(d);
        }
        if (Mathf.Abs(delta) < 0.05f) return "";
        return (delta > 0 ? "+" : "-") + Mathf.Abs(delta).ToString(s == StatType.BuildTime ? "0.#" : "0.0");
    }

    public static bool DeltaIsGood(StatType s, float delta) => LowerIsBetter(s) ? delta < 0f : delta > 0f;
}

/// A full set of stat values (plus the robot tech level they belong to).
public class StatBlock
{
    readonly float[] values = new float[Stats.All.Length];

    public int Level = 1;

    public float this[StatType s]
    {
        get => values[(int)s];
        set => values[(int)s] = value;
    }

    public int Int(StatType s) => Mathf.Max(0, Mathf.RoundToInt(this[s]));
}
