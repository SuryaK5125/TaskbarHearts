namespace SurvivalHud.Core;

/// <summary>A PC reading one of the bars can show.</summary>
public enum Stat { Battery, Cpu, Ram, Gpu }

/// <summary>
/// Which reading each bar shows. By default hearts are the battery, hunger the CPU, armor free
/// RAM and the XP bar the GPU, but any bar can show any reading.
/// Icon bars always read "full = good", like in the game: battery charge, CPU/GPU headroom
/// (hunger drains as the CPU works harder) and free RAM. The XP bar fills up with use instead:
/// CPU/GPU/RAM in use, or battery charge.
/// </summary>
public sealed record BarMap(Stat Hearts = Stat.Battery, Stat Hunger = Stat.Cpu, Stat Armor = Stat.Ram, Stat Xp = Stat.Gpu)
{
    public static BarMap Classic { get; } = new();

    public static readonly Stat[] All = { Stat.Battery, Stat.Cpu, Stat.Ram, Stat.Gpu };

    /// <summary>The value stored in settings.</summary>
    public static string Key(Stat s) => s switch { Stat.Battery => "battery", Stat.Cpu => "cpu", Stat.Ram => "ram", _ => "gpu" };

    public static Stat Parse(string? key, Stat fallback) => key?.Trim().ToLowerInvariant() switch
    {
        "battery" => Stat.Battery, "cpu" => Stat.Cpu, "ram" => Stat.Ram, "gpu" => Stat.Gpu, _ => fallback,
    };

    /// <summary>Name shown in the settings dropdowns.</summary>
    public static string Name(Stat s) => s switch { Stat.Battery => "Battery", Stat.Cpu => "CPU", Stat.Ram => "RAM", _ => "GPU" };

    /// <summary>Fill (0–100) of an icon bar showing <paramref name="stat"/>: full is good. A PC without a battery counts as full.</summary>
    public static double? Level(Stat stat, HudState s) => stat switch
    {
        Stat.Battery => s.HasBattery ? s.BatteryPercent : 100,
        Stat.Cpu => s.CpuPercent is double c ? FillMapping.HungerFromCpu(c) : null,
        Stat.Ram => s.MemFreePercent,
        _ => s.GpuPercent is double g ? FillMapping.HungerFromCpu(g) : null,
    };

    /// <summary>XP bar fill and level number (0–100): how much is in use, or the battery charge.</summary>
    public static double? XpValue(Stat stat, HudState s) => stat switch
    {
        Stat.Battery => s.HasBattery ? s.BatteryPercent : 100,
        Stat.Cpu => s.CpuPercent,
        Stat.Ram => s.MemFreePercent is double f ? 100 - f : null,
        _ => s.GpuPercent,
    };
}
