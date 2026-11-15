namespace SurvivalHud.Core;

/// <summary>
/// Minecraft's little warning animations: at low health every heart bobs down by 0–1 pixel each
/// frame, and when hunger is running out the drumsticks jitter by −1–+1 pixel. The controller
/// calls <see cref="Update"/> on each animation frame; drawing code asks <see cref="For"/> for
/// per-icon offsets (in sprite pixels). Off (all null) unless the controller switches it on.
/// </summary>
public static class Jitter
{
    private static readonly Random Rng = new();
    private static StatSprites?[] _hearts = Array.Empty<StatSprites?>();
    private static StatSprites? _food;
    public static int[]? Hearts { get; private set; }
    public static int[]? Food { get; private set; }

    /// <summary>Game ticks in one regeneration wave: one per heart, then a pause (the game uses max health + 5 = 25).</summary>
    public const int RegenCycle = 25;

    /// <summary>
    /// <paramref name="regenStep"/> (0 … <see cref="RegenCycle"/> − 1, one per game tick) runs the
    /// Regeneration wave: heart number <c>regenStep</c> hops up 2 pixels, sweeping left to right,
    /// then the bar rests for the remaining ticks. Null = no wave.
    /// </summary>
    public static void Update(SpriteSet set, bool hearts, bool food, int? regenStep = null)
    {
        _hearts = new StatSprites?[] { set.Heart, set.HeartCharging };
        _food = set.Food;
        var h = hearts ? Enumerable.Range(0, FillMapping.IconsPerBar).Select(_ => Rng.Next(2)).ToArray() : null;
        if (regenStep is int step)
        {
            h ??= new int[FillMapping.IconsPerBar];
            if (step >= 0 && step < h.Length) h[step] -= 2;
        }
        Hearts = h;
        Food = food ? Enumerable.Range(0, FillMapping.IconsPerBar).Select(_ => Rng.Next(3) - 1).ToArray() : null;
    }

    public static void Stop() => Hearts = Food = null;

    /// <summary>Per-icon vertical offsets for this sprite set's hearts or hunger, or null.</summary>
    public static int[]? For(StatSprites s) =>
        Hearts != null && Array.IndexOf(_hearts, s) >= 0 ? Hearts : Food != null && ReferenceEquals(s, _food) ? Food : null;
}

/// <summary>Draws single icons and ten-icon bars with partial fills.</summary>
public static class BarRenderer
{
    /// <summary>Sprite cell size in sprite units; Minecraft HUD icons are 9×9 and overlap by one when laid out in a bar.</summary>
    public const int Cell = 9;
    public const int Pitch = 8;
    public const int BarWidth = Pitch * (FillMapping.IconsPerBar - 1) + Cell;

    /// <summary>
    /// Draws one icon: the empty container first, then the filled art clipped to the requested
    /// fraction, so the outline and empty background always survive. A null fraction (reading
    /// unavailable) draws just the container.
    /// </summary>
    public static void DrawIcon(Canvas32 canvas, StatSprites s, double? fraction, int x, int y, int size)
    {
        canvas.Blit(s.Empty, x, y, size, size);
        if (fraction is not double f) return;
        f = FillMapping.Clamp01(f);
        if (f <= 0) return;

        if (s.Half != null && Math.Abs(f - 0.5) < 0.001)
        {
            canvas.Blit(s.Half, x, y, size, size);
            return;
        }

        int cols = FillMapping.FillColumns(f, s.FillMax - s.FillMin + 1);
        if (s.FillFromRight)
            canvas.Blit(s.Full, x, y, size, size, s.FillMax + 1 - cols, s.FillMax + 1);
        else
            canvas.Blit(s.Full, x, y, size, size, 0, s.FillMin + cols);
    }

    /// <summary>Draws ten icons for a 0–100 % value; <paramref name="px"/> is physical pixels per sprite unit.</summary>
    public static void DrawBar(Canvas32 canvas, StatSprites s, double? percent, int x, int y, int px)
    {
        for (int i = 0; i < FillMapping.IconsPerBar; i++)
        {
            // Bars that drain from the left (hunger) put their first icon at the right end.
            int slot = s.FillFromRight ? FillMapping.IconsPerBar - 1 - i : i;
            double? frac = percent is double p ? FillMapping.IconFraction(p, i) : null;
            int dy = (Jitter.For(s)?[i] ?? 0) * px;
            DrawIcon(canvas, s, frac, x + slot * Pitch * px, y + dy, Cell * px);
        }
    }
}

/// <summary>What the three compact icons should show (fractions are 0..1, null = unavailable).</summary>
public readonly record struct CompactState(double? Heart, double? Food, double? Armor, bool Charging);

public static class CompactRenderer
{
    public static int ContentWidth(int px, int gap) => 3 * BarRenderer.Cell * px + 2 * gap;

    /// <summary>
    /// Renders the icon row centred in a width × height surface.
    /// <paramref name="rowColors"/> (one opaque colour per row) paints a background that hides
    /// whatever is underneath; without it the surface is transparent except for a 1/255-alpha
    /// plate behind the icons that keeps the whole row hoverable.
    /// </summary>
    public static Canvas32 Render(SpriteSet set, CompactState st, int width, int height, int px, int gap,
        uint[]? rowColors, bool alignmentBox)
    {
        var c = new Canvas32(width, height);
        int icon = BarRenderer.Cell * px;
        int contentW = ContentWidth(px, gap);
        int x0 = (width - contentW) / 2, y0 = (height - icon) / 2;

        if (alignmentBox)
        {
            c.Clear(0xC0202020);
        }
        else if (rowColors != null)
        {
            for (int y = 0; y < c.Height; y++)
                c.SetRect(0, y, c.Width, 1, rowColors[Math.Min(y, rowColors.Length - 1)] | 0xFF000000);
        }
        else
        {
            c.SetRect(x0 - px, y0 - px, contentW + 2 * px, icon + 2 * px, 0x01000000);
        }

        var heart = st.Charging ? set.HeartCharging : set.Heart;
        BarRenderer.DrawIcon(c, heart, st.Heart, x0, y0 + (Jitter.For(heart)?[0] ?? 0) * px, icon);
        BarRenderer.DrawIcon(c, set.Food, st.Food, x0 + icon + gap, y0 + (Jitter.For(set.Food)?[0] ?? 0) * px, icon);
        BarRenderer.DrawIcon(c, set.Armor, st.Armor, x0 + 2 * (icon + gap), y0, icon);

        if (alignmentBox)
        {
            const uint box = 0xFFFFD800;
            int t = Math.Max(1, px / 2);
            c.SetRect(0, 0, c.Width, t, box);
            c.SetRect(0, c.Height - t, c.Width, t, box);
            c.SetRect(0, 0, t, c.Height, box);
            c.SetRect(c.Width - t, 0, t, c.Height, box);
        }
        return c;
    }
}

/// <summary>
/// "Survival Bars" taskbar layout: ten armor icons (free RAM) above ten hearts (battery),
/// laid out exactly like Minecraft's own HUD rows (9-unit icons on an 8-unit pitch, one unit
/// between the rows). Values are 0–100 percentages; null draws empty containers.
/// </summary>
public static class SurvivalBarsRenderer
{
    public const int RowGap = 1;
    public const int HeightUnits = 2 * BarRenderer.Cell + RowGap;

    public static int ContentWidth(int px) => BarRenderer.BarWidth * px;
    public static int ContentHeight(int px, int rows = 2) => (rows * BarRenderer.Cell + (rows - 1) * RowGap) * px;

    /// <summary>Default rows, as in the game: armor on top, hearts underneath.</summary>
    public static Canvas32 Render(SpriteSet set, double? heartPercent, double? armorPercent, bool charging,
        int width, int height, int px, bool alignmentBox) =>
        RenderRows(set, new[] { (set.Armor, armorPercent), (charging ? set.HeartCharging : set.Heart, heartPercent) },
            width, height, px, alignmentBox);

    /// <summary>One or more ten-icon rows, top to bottom, centred in a width × height surface.</summary>
    public static Canvas32 RenderRows(SpriteSet set, IReadOnlyList<(StatSprites Sprites, double? Percent)> rows,
        int width, int height, int px, bool alignmentBox)
    {
        var c = new Canvas32(width, height);
        int contentW = ContentWidth(px), contentH = ContentHeight(px, rows.Count);
        int x0 = (width - contentW) / 2, y0 = (height - contentH) / 2;

        // Transparent except for a 1/255-alpha plate that keeps the bars hoverable/right-clickable.
        if (alignmentBox) c.Clear(0xC0202020);
        else c.SetRect(x0 - px, y0 - px, contentW + 2 * px, contentH + 2 * px, 0x01000000);

        for (int i = 0; i < rows.Count; i++)
            BarRenderer.DrawBar(c, rows[i].Sprites, rows[i].Percent, x0, y0 + i * (BarRenderer.Cell + RowGap) * px, px);

        if (alignmentBox)
        {
            const uint box = 0xFFFFD800;
            int t = Math.Max(1, px / 2);
            c.SetRect(0, 0, c.Width, t, box);
            c.SetRect(0, c.Height - t, c.Width, t, box);
            c.SetRect(0, 0, t, c.Height, box);
            c.SetRect(c.Width - t, 0, t, c.Height, box);
        }
        return c;
    }
}

/// <summary>Everything the expanded HUD displays. Null readings are shown as unavailable, never invented.</summary>
public readonly record struct HudState(
    int? BatteryPercent, bool HasBattery, bool OnAc, bool Charging, bool ShowCharging,
    double? CpuPercent, double? MemFreePercent, double MemUsedGb, double MemTotalGb,
    double? GpuPercent = null, GameData? Game = null, IReadOnlyList<HotbarItem>? Custom = null, BarMap? Map = null)
{
    /// <summary>Which reading each bar shows (the classic layout when not set).</summary>
    public BarMap Bars => Map ?? BarMap.Classic;
}

/// <summary>One item in the decorative custom hotbar. A null icon (item not in this texture source) leaves the slot empty.</summary>
public sealed record HotbarItem(Sprite? Icon, string Name, int Count = 1);

/// <summary>Readings shown in the game-style popup's hotbar, plus which slot the pointer is over (−1 = none).</summary>
public sealed record GameData(
    double? DownBytesPerSec, double? UpBytesPerSec, double? DiskFreeBytes, double? DiskTotalBytes,
    long UptimeSeconds, int? Processes, int? Threads,
    double? BatteryHealthPercent, double? BatteryFullWh, double? BatteryDesignWh, int HoverSlot = -1,
    IReadOnlyList<(string Name, double Free, double Total)>? OtherDrives = null,
    string? TopProcess = null, double? TopProcessPercent = null,
    int? VolumePercent = null, bool Muted = false);

public static class HudRenderer
{
    private const int Frame = 2;   // background rim + border, in units
    private const int Pad = 5;
    private const int TextGap = 2;
    private const int RowGap = 5;

    private readonly record struct Theme(uint Background, uint BorderTop, uint BorderBottom, uint Text, uint Shadow, uint Dim, bool Bevel);

    private static readonly Theme Dark = new(0xF0100010, 0x505000FF, 0x5028007F, 0xFFFFFFFF, 0xFF3F3F3F, 0xFFAAAAAA, false);
    private static readonly Theme Light = new(0xFFC6C6C6, 0xFFFFFFFF, 0xFF555555, 0xFF3F3F3F, 0, 0xFF6E6E6E, true);

    private enum Kind { Icons, Xp, TextOnly }

    private readonly record struct Row(string Label, string Value, StatSprites? Sprites, double? Percent, string? Sub, Kind Kind = Kind.Icons)
    {
        public string[] SubLines => Sub?.Split('\n') ?? Array.Empty<string>();
        public int BarHeight => Kind switch { Kind.Xp => XpHeight, Kind.TextOnly => 0, _ => BarRenderer.Cell };
    }

    private const int XpHeight = 5;

    /// <summary>"850 KBPS", "12.3 MBPS", "1.2 GBPS" from bytes per second (network speeds are quoted in bits).</summary>
    public static string FormatRate(double bytesPerSec)
    {
        double bits = Math.Max(0, bytesPerSec) * 8;
        if (bits >= 1e9) return $"{bits / 1e9:0.0} Gbps";
        if (bits >= 1e7) return $"{bits / 1e6:0} Mbps";
        if (bits >= 1e6) return $"{bits / 1e6:0.0} Mbps";
        return $"{bits / 1e3:0} kbps";
    }

    /// <summary>XP-bar fill for a download speed, on a log scale: 1 Mbps ≈ 10 %, 100 Mbps ≈ 67 %, 1 Gbps = full.</summary>
    public static double NetworkFill(double bytesPerSec)
    {
        double mbps = Math.Max(0, bytesPerSec) * 8 / 1e6;
        return FillMapping.Clamp01(Math.Log10(1 + mbps) / 3.0);
    }

    /// <summary>
    /// Draws the experience bar at the HUD's bar width (81 sprite units): the first 80 columns of
    /// the 182-wide game texture plus its rounded end cap, so the notches and outline stay genuine.
    /// </summary>
    public static void DrawXpBar(Canvas32 c, SpriteSet set, double? fraction, int x, int y, int px)
    {
        Sprite bg = set.XpBackground ?? FallbackSprites.XpBar(false);
        Sprite fg = set.XpProgress ?? FallbackSprites.XpBar(true);
        int k = Math.Max(1, bg.Height / XpHeight);          // resource packs may be 2×, 4×…
        int body = (BarRenderer.BarWidth - 1) * k;           // columns taken from the left
        int cap = Math.Max(k, bg.Width - 181 * k);           // the last source column(s): the end cap
        void Piece(Sprite s, int colTo)
        {
            int bodyTo = Math.Min(colTo, BarRenderer.BarWidth - 1);
            if (bodyTo > 0) c.Blit(s.Crop(0, 0, body, s.Height), x, y, (BarRenderer.BarWidth - 1) * px, XpHeight * px, 0, bodyTo * k);
            if (colTo >= BarRenderer.BarWidth) c.Blit(s.Crop(s.Width - cap, 0, cap, s.Height), x + (BarRenderer.BarWidth - 1) * px, y, px, XpHeight * px);
        }
        Piece(bg, BarRenderer.BarWidth);
        if (fraction is double f && f > 0)
            Piece(fg, Math.Max(1, (int)Math.Round(FillMapping.Clamp01(f) * BarRenderer.BarWidth)));
    }

    /// <summary>Heart fill: the battery level, or full on a PC without a battery (nothing to drain, like full health).</summary>
    public static int? HeartPercent(HudState s) => s.HasBattery ? s.BatteryPercent : 100;

    public static Canvas32 Render(SpriteSet set, HudState s, int px, bool light)
    {
        string batterySub =
            !s.HasBattery ? "Desktop PC" :
            s.Charging ? "Charging" :
            s.OnAc ? "Plugged in" : "";

        var map = s.Bars;
        string Pct(double? v) => v is double d ? $"{Math.Round(d):0}%" : "--";
        // Label, value and detail line for a reading shown on an icon bar.
        (string Label, string Value, string? Sub) Describe(Stat stat) => stat switch
        {
            Stat.Battery => ("Battery", !s.HasBattery ? "AC" : s.BatteryPercent is int b ? $"{b}%" : "N/A", batterySub.Length > 0 ? batterySub : null),
            Stat.Cpu => ("CPU", Pct(s.CpuPercent), null),
            Stat.Ram => ("RAM Free", Pct(s.MemFreePercent), s.MemTotalGb > 0 ? $"{s.MemUsedGb:0.0}/{s.MemTotalGb:0.0} GB" : null),
            _ => ("GPU", Pct(s.GpuPercent), null),
        };
        Row IconRow(Stat stat, StatSprites sprites) { var d = Describe(stat); return new Row(d.Label, d.Value, sprites, BarMap.Level(stat, s), d.Sub); }
        var hearts = map.Hearts == Stat.Battery && s.Charging && s.ShowCharging ? set.HeartCharging : set.Heart;
        var rows = new List<Row> { IconRow(map.Hearts, hearts), IconRow(map.Hunger, set.Food), IconRow(map.Armor, set.Armor) };
        // The experience bar fills up with use (RAM shows how much is used there, not how much is free).
        var xp = map.Xp == Stat.Ram ? ("RAM Used", Pct(BarMap.XpValue(Stat.Ram, s)), (string?)null) : Describe(map.Xp);
        rows.Add(new Row(xp.Item1, xp.Item2, null, BarMap.XpValue(map.Xp, s) / 100.0, null, Kind.Xp));

        int innerW = BarRenderer.BarWidth;
        foreach (var r in rows)
        {
            foreach (string line in r.SubLines) innerW = Math.Max(innerW, PixelFont.Measure(line));
            // Label and value share a line; never let them overlap.
            innerW = Math.Max(innerW, PixelFont.Measure(r.Label) + PixelFont.Advance + PixelFont.Measure(r.Value));
        }

        int innerH = 0;
        foreach (var r in rows)
            innerH += PixelFont.GlyphHeight + TextGap + r.BarHeight + r.SubLines.Length * (TextGap + PixelFont.GlyphHeight);
        innerH += RowGap * (rows.Count - 1);

        var th = light ? Light : Dark;
        var canvas = NewPanel(innerW, innerH, px, th);

        int x = (Frame + Pad) * px, y = (Frame + Pad) * px;
        foreach (var r in rows)
        {
            PixelFont.Draw(canvas, r.Label, x, y, px, th.Text, th.Shadow);
            PixelFont.Draw(canvas, r.Value, x + (innerW - PixelFont.Measure(r.Value)) * px, y, px, th.Text, th.Shadow);
            y += (PixelFont.GlyphHeight + TextGap) * px;
            int bx = x + (innerW - BarRenderer.BarWidth) / 2 * px;
            switch (r.Kind)
            {
                case Kind.TextOnly: break;
                case Kind.Xp: DrawXpBar(canvas, set, r.Percent, bx, y, px); break;
                default: BarRenderer.DrawBar(canvas, r.Sprites!, r.Percent, bx, y, px); break;
            }
            y += r.BarHeight * px;
            foreach (string line in r.SubLines)
            {
                y += TextGap * px;
                PixelFont.Draw(canvas, line, x, y, px, th.Dim, th.Shadow);
                y += PixelFont.GlyphHeight * px;
            }
            y += RowGap * px;
        }
        return canvas;
    }

    // ---- game-style popup ---------------------------------------------------------------

    private const int GameW = 182, GameH = 49, GameHotbarY = 27;

    /// <summary>Transparent strip (sprite units) above the game-style panel, reserved for tooltips so they never cover what they describe.</summary>
    public const int GameTipReserve = 46;

    /// <summary>
    /// The slot count drawn in a hotbar slot: the real value, shortened only when it is absurdly
    /// large (12,000 → "12K", 3,400,000 → "3.4M"). Small values keep two significant digits.
    /// </summary>
    public static string SlotCount(double value)
    {
        if (double.IsNaN(value) || value < 0) return "?";
        if (value >= 1e6) return $"{value / 1e6:0.#}M";
        if (value >= 1e4) return $"{value / 1e3:0}K";
        if (value >= 100) return $"{value:0}";
        if (value >= 10) return $"{value:0.#}";
        return $"{value:0.##}";   // 3.14, 0.72, 0.06 — never more than four characters
    }

    private static string Mbps(double bytesPerSec) => bytesPerSec * 8 / 1e6 is var m && m >= 100 ? $"{m:0} Mbps" : $"{m:0.##} Mbps";

    private static string Uptime(long seconds)
    {
        long d = seconds / 86400, h = seconds / 3600 % 24, m = seconds / 60 % 60;
        return d > 0 ? $"{d}d {h}h {m}m" : $"{h}h {m}m";
    }

    /// <summary>One hotbar slot: icon, the count drawn in the slot, and the tooltip (title, then detail lines).</summary>
    /// <summary>
    /// One hotbar slot: icon, optional stack count, optional bar (0..1; null = none) and the tooltip.
    /// <paramref name="BarColor"/> null = the game's durability colour (green when full → red when empty).
    /// </summary>
    private readonly record struct Slot(Sprite? Icon, string? Count, string Title, string[] Lines,
        double? Bar = null, uint? BarColor = null);

    /// <summary>The game's durability colour: hue from red (empty) to green (full).</summary>
    public static uint DurabilityColor(double fraction)
    {
        double h = FillMapping.Clamp01(fraction) / 3.0 * 6;   // 0..2 on the colour wheel (red → yellow → green)
        double x = 1 - Math.Abs(h % 2 - 1);
        (double r, double g) = h < 1 ? (1.0, x) : (x, 1.0);
        return 0xFF000000 | ((uint)Math.Round(r * 255) << 16) | ((uint)Math.Round(g * 255) << 8);
    }

    /// <summary>A whole-number stack count, or none below 1 (the exact value is in the tooltip).</summary>
    public static string? WholeCount(double value) => value >= 0.5 ? SlotCount(Math.Round(value)) : null;

    /// <summary>Speeds in Mbps: one decimal below 100 (0.3, 4.7, 12.4), whole above; nothing when idle.</summary>
    public static string? SpeedCount(double mbps) =>
        mbps < 0.05 ? null : mbps >= 99.95 ? SlotCount(Math.Round(mbps))
        : mbps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The decorative hotbar: the chosen items in order, name only on hover.</summary>
    private static Slot?[] CustomSlots(IReadOnlyList<HotbarItem> items)
    {
        var slots = new Slot?[9];
        for (int i = 0; i < Math.Min(9, items.Count); i++)
            if (items[i].Icon is { } icon)
                slots[i] = new(icon, items[i].Count > 1 ? items[i].Count.ToString() : null, items[i].Name, Array.Empty<string>());
        return slots;
    }

    /// <summary>Durability bar exactly where the game draws it: a 13×2 black strip near the bottom of the slot, filled 1 px high.</summary>
    private static void DrawItemBar(Canvas32 c, int slotX, int slotY, int px, double fraction, uint? color)
    {
        double f = FillMapping.Clamp01(fraction);
        int x = slotX + 2 * px, y = slotY + 13 * px;
        c.SetRect(x, y, 13 * px, 2 * px, 0xFF000000);
        int fill = f > 0 ? Math.Max(1, (int)Math.Round(f * 13)) : 0;
        c.SetRect(x, y, fill * px, px, color ?? DurabilityColor(f));
    }

    /// <summary>
    /// The nine hotbar slots, each item always in the same place (null = empty slot):
    /// 1 download, 2 upload, 3 free space on C:, 4 uptime, 5 battery health (laptops),
    /// 6 processes, 7 other drives, 8 top CPU process, 9 volume.
    /// </summary>
    private static Slot?[] GameSlots(SpriteSet set, GameData? g)
    {
        var gs = set.Game;
        var slots = new Slot?[9];
        // 1 download / 2 upload: Mbps as the stack count, one decimal below 100 (exact value on hover).
        slots[0] = new(gs?.Sugar ?? FallbackSprites.Sugar(), g?.DownBytesPerSec is double d ? SpeedCount(d * 8 / 1e6) : null,
            "Download Speed", new[] { g?.DownBytesPerSec is double d2 ? Mbps(d2) : "Measuring..." });
        slots[1] = new(gs?.FireworkRocket ?? FallbackSprites.FireworkRocket(), g?.UpBytesPerSec is double u ? SpeedCount(u * 8 / 1e6) : null,
            "Upload Speed", new[] { g?.UpBytesPerSec is double u2 ? Mbps(u2) : "Measuring..." });
        // 3 free space on C: as the pickaxe's durability.
        slots[2] = new(gs?.DiamondPickaxe ?? FallbackSprites.DiamondPickaxe(), null,
            "Free Space on C:", g?.DiskFreeBytes is double f2 && g.DiskTotalBytes is double t
                ? new[] { $"{f2 / 1e9:0.0} GB free", $"of {t / 1e9:0.0} GB ({f2 / t * 100:0}%)" } : new[] { "--" },
            g?.DiskFreeBytes is double f3 && g.DiskTotalBytes is double t3 && t3 > 0 ? f3 / t3 : null);
        // 4 uptime in whole hours.
        slots[3] = new(gs?.Clock ?? FallbackSprites.Clock(), g is null ? null : WholeCount(g.UptimeSeconds / 3600.0),
            "Uptime", new[] { g is null ? "--" : Uptime(g.UptimeSeconds), "Since the last full restart" });
        // 5 battery health as the elytra's durability (laptops only).
        if (g?.BatteryHealthPercent is double hp)
            slots[4] = new(gs?.Elytra ?? FallbackSprites.Elytra(), null, "Battery Health",
                new[] { $"{hp:0.#}% of design", $"{g.BatteryFullWh:0.0} / {g.BatteryDesignWh:0.0} Wh" }, hp / 100);
        // 6 processes as a count.
        slots[5] = new(gs?.Redstone ?? FallbackSprites.Redstone(), g?.Processes is int pc ? SlotCount(pc) : null, "Processes",
            new[] { g?.Processes is int p2 ? $"{p2} running" : "--", g?.Threads is int th ? $"{th} threads" : "" });
        // 7 other drives: the bundle's fullness bar shows how full they are together. Always shown.
        var drives = g?.OtherDrives;
        double? used = drives is { Count: > 0 } && drives.Sum(x => x.Total) is double tot && tot > 0
            ? 1 - drives.Sum(x => x.Free) / tot : null;
        slots[6] = new(gs?.Bundle ?? FallbackSprites.Bundle(), null,
            "Other Drives", drives is { Count: > 0 }
                ? drives.Take(4).Select(x => $"{x.Name} {x.Free / 1e9:0} of {x.Total / 1e9:0} GB free").ToArray()
                : new[] { "No other drives", "USB sticks and extra disks show here" },
            used, used is >= 0.999 ? 0xFFFF5555u : 0xFF6666FFu);
        // 8 top CPU process: durability drops as it burns more CPU.
        slots[7] = new(gs?.FlintAndSteel ?? FallbackSprites.FlintAndSteel(), null,
            "Top CPU Process", g?.TopProcess is string name
                ? new[] { name.Length > 22 ? name[..22] : name, $"{g.TopProcessPercent:0.#}% of total CPU" }
                : new[] { "Measuring..." },
            g?.TopProcessPercent is double tp ? 1 - tp / 100 : null);
        // 9 volume as a level bar.
        slots[8] = new(gs?.MusicDisc ?? FallbackSprites.MusicDisc(), null,
            "Volume", g?.VolumePercent is int v2 ? new[] { g.Muted ? $"Muted ({v2}%)" : $"{v2}%" } : new[] { "No speakers" },
            g?.VolumePercent is int v3 ? (g.Muted ? 0 : v3 / 100.0) : null);
        return slots;
    }

    /// <summary>Hover targets besides hotbar slots 0–8.</summary>
    public const int HitArmor = 100, HitHearts = 101, HitHunger = 102, HitXp = 103;

    /// <summary>
    /// What is under a point given in canvas pixels of the game-style popup: a hotbar slot (0–8),
    /// one of the bars (<see cref="HitArmor"/>…<see cref="HitXp"/>), or −1.
    /// </summary>
    public static int GameHitAt(int x, int y, int px)
    {
        y -= GameTipReserve * px;                       // the panel starts below the tooltip strip
        int o = (Frame + Pad) * px;
        if (x < o || y < o) return -1;
        int u = (x - o) / px, v = (y - o) / px;      // sprite units inside the HUD
        int bar = BarRenderer.BarWidth;
        if (v < 9 && u < bar) return HitArmor;
        if (v >= 10 && v < 19 && u < bar) return HitHearts;
        if (v >= 10 && v < 19 && u >= GameW - bar && u < GameW) return HitHunger;
        if (v >= 14 && v < 25 && u < GameW) return HitXp;            // level number and bar
        if (v >= GameHotbarY + 3 && v < GameHotbarY + 19)
        {
            int w = u - 3;
            if (w >= 0 && w % 20 < 16 && w / 20 < 9) return w / 20;
        }
        return -1;
    }

    /// <summary>
    /// The popup laid out like Minecraft's own HUD (in sprite units, 182 wide like the game):
    /// armor above the hearts on the left, hunger on the right, the experience bar across the
    /// bottom with GPU use as the green level number, and a hotbar of items carrying readings.
    /// Hovering a slot shows a game-style tooltip with the exact value.
    /// </summary>
    public static Canvas32 RenderGame(SpriteSet set, HudState s, int px, bool light)
    {
        const int W = GameW, H = GameH;
        const int ArmorY = 0, HeartsY = 10, XpY = 20, HotbarY = GameHotbarY;
        var th = light ? Light : Dark;
        var c = NewPanel(W, H, px, th);
        int ox = (Frame + Pad) * px, oy = (Frame + Pad) * px;
        int X(int u) => ox + u * px;
        int Y(int u) => oy + u * px;

        var map = s.Bars;
        BarRenderer.DrawBar(c, set.Armor, BarMap.Level(map.Armor, s), X(0), Y(ArmorY), px);
        BarRenderer.DrawBar(c, map.Hearts == Stat.Battery && s.Charging && s.ShowCharging ? set.HeartCharging : set.Heart,
            BarMap.Level(map.Hearts, s), X(0), Y(HeartsY), px);
        BarRenderer.DrawBar(c, set.Food, BarMap.Level(map.Hunger, s), X(W - BarRenderer.BarWidth), Y(HeartsY), px);

        // Experience bar at full game width: its reading (GPU use by default) as the fill and the level number.
        double? xpValue = BarMap.XpValue(map.Xp, s);
        Sprite bg = set.XpBackground ?? FallbackSprites.XpBar(false), fg = set.XpProgress ?? FallbackSprites.XpBar(true);
        int k = Math.Max(1, bg.Width / W);
        c.Blit(bg, X(0), Y(XpY), W * px, XpHeight * px);
        if (xpValue is double gp && gp > 0)
            c.Blit(fg, X(0), Y(XpY), W * px, XpHeight * px, 0, Math.Max(1, (int)Math.Round(FillMapping.Clamp01(gp / 100) * W)) * k);
        if (xpValue is double gp2)
        {
            string level = $"{Math.Round(gp2):0}";
            int lx = X((W - PixelFont.Measure(level)) / 2), ly = Y(XpY - 6);
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                PixelFont.Draw(c, level, lx + dx * px, ly + dy * px, px, 0xFF000000);
            PixelFont.Draw(c, level, lx, ly, px, 0xFF80FF20);
        }

        // Hotbar. Slot counts use a smaller pixel size so real numbers fit, like the game's small stack counts.
        Sprite bar = set.Game?.Hotbar ?? FallbackSprites.Hotbar();
        c.Blit(bar, X(0), Y(HotbarY), W * px, 22 * px);
        var slots = s.Custom is { } custom ? CustomSlots(custom) : GameSlots(set, s.Game);
        int cpx = Math.Max(1, px * 2 / 3);
        for (int i = 0; i < slots.Length; i++)
        {
            int sx = 3 + i * 20, sy = HotbarY + 3;
            if (slots[i] is not { } slot) continue;
            if (slot.Icon is { } icon) c.Blit(icon, X(sx), Y(sy), 16 * px, 16 * px);
            if (slot.Bar is double level) DrawItemBar(c, X(sx), Y(sy), px, level, slot.BarColor);
            if (slot.Count is { } count)
            {
                int w = PixelFont.Measure(count) * cpx;
                PixelFont.Draw(c, count, X(sx + 17) - w, Y(sy + 16) - PixelFont.GlyphHeight * cpx, cpx, 0xFFFFFFFF, 0xFF3F3F3F);
            }
        }

        // Tooltips get a transparent strip above the panel, so they can always sit above the
        // hovered item, like in the game, instead of covering it.
        int reserve = GameTipReserve * px;
        var full = new Canvas32(c.Width, c.Height + reserve);
        full.Blit(new Sprite(c.Width, c.Height, c.Pixels), 0, reserve, c.Width, c.Height);
        c = full;
        oy += reserve;

        // Tooltip for whatever is hovered, drawn like the game's item tooltip.
        int hit = s.Game?.HoverSlot ?? -1;
        if (hit >= 0 && hit < slots.Length && slots[hit] is { } hovered)
            DrawTooltip(c, hovered, X(3 + hit * 20), Y(HotbarY), Y(HotbarY + 22), px, th);
        else if (BarTooltip(s, hit) is { } tip)
        {
            var (ax, top, bottom) = hit switch
            {
                HitArmor => (0, ArmorY, ArmorY + 9),
                HitHearts => (0, HeartsY, HeartsY + 9),
                HitHunger => (W - BarRenderer.BarWidth, HeartsY, HeartsY + 9),
                _ => (W / 2 - 30, XpY - 6, XpY + XpHeight),
            };
            DrawTooltip(c, tip, X(ax), Y(top), Y(bottom), px, th);
        }
        return c;
    }

    /// <summary>Details for whichever reading the hovered bar shows.</summary>
    private static Slot? BarTooltip(HudState s, int hit) => hit switch
    {
        HitHearts => StatTooltip(s, s.Bars.Hearts),
        HitHunger => StatTooltip(s, s.Bars.Hunger),
        HitArmor => StatTooltip(s, s.Bars.Armor),
        HitXp => StatTooltip(s, s.Bars.Xp),
        _ => null,
    };

    private static Slot StatTooltip(HudState s, Stat stat) => stat switch
    {
        Stat.Battery => new Slot(null, null, "Battery", new[]
        {
            !s.HasBattery ? "No battery (desktop PC)" : s.BatteryPercent is int b ? $"{b}% charge" : "--",
            !s.HasBattery ? "Mains power, always full" : s.Charging ? "Charging" : s.OnAc ? "Plugged in" : "On battery",
        }),
        Stat.Ram => new Slot(null, null, "Memory", new[]
        {
            s.MemFreePercent is double m ? $"{m:0.#}% free" : "--",
            s.MemTotalGb > 0 ? $"{s.MemUsedGb:0.0} GB used of {s.MemTotalGb:0.0} GB" : "",
            s.MemTotalGb > 0 ? $"{s.MemTotalGb - s.MemUsedGb:0.0} GB available" : "",
        }),
        Stat.Cpu => new Slot(null, null, "CPU", new[]
        {
            s.CpuPercent is double c ? $"{c:0.#}% in use" : "Measuring...",
            s.CpuPercent is double c2 ? $"{100 - c2:0.#}% headroom" : "",
        }),
        _ => new Slot(null, null, "GPU", new[]
        {
            s.GpuPercent is double g ? $"{g:0.#}% in use" : "Measuring...",
            "Busiest GPU engine",
        }),
    };

    /// <summary>A Minecraft-style tooltip: dark translucent box, purple border, white title, grey detail lines.</summary>
    private static void DrawTooltip(Canvas32 c, Slot slot, int anchorX, int targetTop, int targetBottom, int px, Theme th)
    {
        var lines = slot.Lines.Where(l => l.Length > 0).ToArray();
        int innerW = PixelFont.Measure(slot.Title);
        foreach (string l in lines) innerW = Math.Max(innerW, PixelFont.Measure(l));
        int innerH = PixelFont.GlyphHeight + lines.Length * (PixelFont.GlyphHeight + 2) + (lines.Length > 0 ? 2 : 0);
        int w = (innerW + 6) * px, h = (innerH + 6) * px;
        // Always above the target (the canvas reserves room for it); below only as a last resort.
        int x = Math.Clamp(anchorX, px, Math.Max(px, c.Width - w - px));
        int y = targetTop - h - px >= 0 ? targetTop - h - px : Math.Min(targetBottom + px, Math.Max(0, c.Height - h));

        c.SetRect(x + px, y, w - 2 * px, h, 0xF0100010);
        c.SetRect(x, y + px, w, h - 2 * px, 0xF0100010);
        uint top = 0xFF5000FF, bottom = 0xFF28007F;
        c.SetRect(x + px, y + px, w - 2 * px, px, top);
        c.SetRect(x + px, y + h - 2 * px, w - 2 * px, px, bottom);
        for (int yy = y + px; yy < y + h - px; yy += px)
        {
            double t = (yy - y) / (double)h;
            uint col = Lerp(top, bottom, t);
            c.SetRect(x + px, yy, px, px, col);
            c.SetRect(x + w - 2 * px, yy, px, px, col);
        }

        int tx = x + 3 * px, ty = y + 3 * px;
        PixelFont.Draw(c, slot.Title, tx, ty, px, 0xFFFFFFFF, 0xFF3F3F3F);
        ty += (PixelFont.GlyphHeight + 4) * px;
        foreach (string l in lines)
        {
            PixelFont.Draw(c, l, tx, ty, px, 0xFFAAAAAA, 0xFF2A2A2A);
            ty += (PixelFont.GlyphHeight + 2) * px;
        }
    }

    /// <summary>A plain text panel in the same style (used for alignment-mode instructions).</summary>
    public static Canvas32 RenderMessage(string[] lines, int px, bool light)
    {
        int innerW = 0;
        foreach (string l in lines) innerW = Math.Max(innerW, PixelFont.Measure(l));
        int innerH = lines.Length * PixelFont.GlyphHeight + (lines.Length - 1) * 3;
        var th = light ? Light : Dark;
        var canvas = NewPanel(innerW, innerH, px, th);
        int y = (Frame + Pad) * px;
        for (int i = 0; i < lines.Length; i++)
        {
            int x = (Frame + Pad + (innerW - PixelFont.Measure(lines[i])) / 2) * px;
            PixelFont.Draw(canvas, lines[i], x, y, px, i == 0 ? th.Text : th.Dim, th.Shadow);
            y += (PixelFont.GlyphHeight + 3) * px;
        }
        return canvas;
    }

    /// <summary>Tooltip-style panel: square with single-pixel notched corners and a one-unit inner border.</summary>
    private static Canvas32 NewPanel(int innerW, int innerH, int px, Theme th)
    {
        int wu = innerW + 2 * (Frame + Pad), hu = innerH + 2 * (Frame + Pad);
        var c = new Canvas32(wu * px, hu * px);
        uint rim = th.Bevel ? 0xFF000000 : th.Background;

        // Rim (notched corners), then the body.
        c.SetRect(px, 0, (wu - 2) * px, hu * px, rim);
        c.SetRect(0, px, wu * px, (hu - 2) * px, rim);
        c.SetRect(px, px, (wu - 2) * px, (hu - 2) * px, th.Background);

        if (th.Bevel)
        {
            c.SetRect(px, px, (wu - 2) * px, px, th.BorderTop);
            c.SetRect(px, px, px, (hu - 2) * px, th.BorderTop);
            c.SetRect(px, (hu - 2) * px, (wu - 2) * px, px, th.BorderBottom);
            c.SetRect((wu - 2) * px, px, px, (hu - 2) * px, th.BorderBottom);
        }
        else
        {
            // Border fades from the top colour to the bottom colour, one sprite unit at a time.
            int span = Math.Max(1, hu - 3);
            for (int u = 1; u <= hu - 2; u++)
            {
                uint col = Lerp(th.BorderTop, th.BorderBottom, (u - 1) / (double)span);
                bool edge = u == 1 || u == hu - 2;
                if (edge) c.FillRect(px, u * px, (wu - 2) * px, px, col);
                else
                {
                    c.FillRect(px, u * px, px, px, col);
                    c.FillRect((wu - 2) * px, u * px, px, px, col);
                }
            }
        }
        return c;
    }

    private static uint Lerp(uint a, uint b, double t)
    {
        uint Ch(int s) => (uint)Math.Round(((a >> s) & 0xFF) + (((b >> s) & 0xFF) - (double)((a >> s) & 0xFF)) * t);
        return (Ch(24) << 24) | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }
}
