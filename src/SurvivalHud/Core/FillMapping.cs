namespace SurvivalHud.Core;

/// <summary>Pure maths that turn a 0–100 % reading into sprite fill amounts.</summary>
public static class FillMapping
{
    public const int IconsPerBar = 10;

    public static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);

    /// <summary>
    /// Fill fraction (0..1) of icon <paramref name="index"/> (0 = first icon to fill) for a bar
    /// showing <paramref name="percent"/>. Each icon is worth 10 percentage points and the
    /// value is never rounded to 10 % steps: 83 % → eight full icons and 0.3 of the ninth.
    /// </summary>
    public static double IconFraction(double percent, int index)
    {
        double per = 100.0 / IconsPerBar;
        // Round away binary noise such as (83 - 80) / 10 = 0.29999999999999999.
        return Math.Round(Clamp01((percent - index * per) / per), 9);
    }

    /// <summary>
    /// Number of whole source-pixel columns to reveal for a fraction of a sprite whose fillable
    /// interior is <paramref name="width"/> columns wide. Sprites are tiny, so neighbouring
    /// percentages can share a column count; two rules keep the picture honest anyway:
    /// anything above zero shows at least one column, and only a truly full value shows all.
    /// </summary>
    public static int FillColumns(double fraction, int width)
    {
        fraction = Clamp01(fraction);
        if (width <= 0 || fraction <= 0) return 0;
        if (fraction >= 1) return width;
        int cols = (int)Math.Round(fraction * width, MidpointRounding.AwayFromZero);
        return width == 1 ? 1 : Math.Clamp(cols, 1, width - 1);
    }

    /// <summary>Hunger remaining: the inverse of CPU load.</summary>
    public static double HungerFromCpu(double cpuUsagePercent) => Math.Clamp(100.0 - cpuUsagePercent, 0, 100);

    /// <summary>Armor remaining: available physical memory as a percentage of the total.</summary>
    public static double ArmorFromMemory(ulong availableBytes, ulong totalBytes) =>
        totalBytes == 0 ? 0 : Math.Clamp(availableBytes * 100.0 / totalBytes, 0, 100);
}
