using SurvivalHud.Core;

namespace SurvivalHud.Tests;

public static class Tests
{
    private static int _passed, _failed;

    private static void Check(bool condition, string what)
    {
        if (condition) { _passed++; return; }
        _failed++;
        Console.WriteLine("FAIL: " + what);
    }

    private static void Eq<T>(T actual, T expected, string what) =>
        Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{what}: expected {expected}, got {actual}");

    private static void Near(double actual, double expected, string what) =>
        Check(Math.Abs(actual - expected) < 1e-9, $"{what}: expected {expected}, got {actual}");

    public static int Main()
    {
        PercentageMapping();
        FillColumnRules();
        HungerAndArmor();
        IconRendering();
        BarRendering();
        HoverLogic();
        CpuCalculation();
        FontAndHud();
        SurvivalBars();
        HudExtras();

        Console.WriteLine($"{_passed} checks passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    // ---- percentage → icons -------------------------------------------------------------

    private static double[] Bar(double percent) =>
        Enumerable.Range(0, 10).Select(i => FillMapping.IconFraction(percent, i)).ToArray();

    private static void PercentageMapping()
    {
        Check(Bar(100).All(f => f == 1), "100% = ten full icons");
        Check(Bar(0).All(f => f == 0), "0% = ten empty icons");

        var b90 = Bar(90);
        Check(b90.Take(9).All(f => f == 1) && b90[9] == 0, "90% = nine full, tenth empty");

        var b85 = Bar(85);
        Check(b85.Take(8).All(f => f == 1), "85%: eight full");
        Near(b85[8], 0.5, "85%: ninth is half");
        Near(b85[9], 0, "85%: tenth empty");

        Near(Bar(83)[8], 0.3, "83%: ninth is 30%");
        Near(Bar(82)[8], 0.2, "82%: ninth is 20%");

        var b10 = Bar(10);
        Check(b10[0] == 1 && b10.Skip(1).All(f => f == 0), "10% = one full icon");

        Near(Bar(7)[0], 0.7, "7%: first icon 70%");
        Check(Bar(-5).All(f => f == 0) && Bar(140).All(f => f == 1), "out-of-range values are clamped");
        Check(Bar(double.NaN).All(f => f == 0), "NaN draws nothing");

        // Every whole percentage: the fractions always add back up to the value.
        for (int p = 0; p <= 100; p++)
            Near(Bar(p).Sum() * 10, p, $"fractions sum for {p}%");
    }

    private static void FillColumnRules()
    {
        Eq(FillMapping.FillColumns(0, 7), 0, "0 → no columns");
        Eq(FillMapping.FillColumns(1, 7), 7, "1 → all columns");
        Eq(FillMapping.FillColumns(0.5, 7), 4, "half of 7 → 4");
        Eq(FillMapping.FillColumns(0.3, 7), 2, "0.3 of 7 → 2");
        Eq(FillMapping.FillColumns(0.2, 7), 1, "0.2 of 7 → 1");
        Eq(FillMapping.FillColumns(0.01, 7), 1, "any non-zero value shows at least one column");
        Eq(FillMapping.FillColumns(0.99, 7), 6, "only a full value shows every column");
        Eq(FillMapping.FillColumns(0.5, 0), 0, "zero-width sprite");
        int last = 0;
        for (int i = 0; i <= 100; i++)
        {
            int c = FillMapping.FillColumns(i / 100.0, 7);
            Check(c >= last, $"columns never decrease as the value rises ({i}%)");
            last = c;
        }
    }

    private static void HungerAndArmor()
    {
        Near(FillMapping.HungerFromCpu(0), 100, "0% CPU = full hunger");
        Near(FillMapping.HungerFromCpu(35), 65, "35% CPU = 65% hunger");
        Near(FillMapping.HungerFromCpu(80), 20, "80% CPU = 20% hunger");
        Near(FillMapping.HungerFromCpu(100), 0, "100% CPU = empty hunger");
        Near(FillMapping.HungerFromCpu(130), 0, "CPU above 100 is clamped");

        const ulong gb = 1UL << 30;
        Near(FillMapping.ArmorFromMemory(16 * gb, 16 * gb), 100, "all RAM free = full armor");
        Near(FillMapping.ArmorFromMemory(4 * gb, 16 * gb), 25, "4 of 16 GB free = 25%");
        Near(FillMapping.ArmorFromMemory(0, 16 * gb), 0, "no RAM free = empty armor");
        Near(FillMapping.ArmorFromMemory(1, 0), 0, "unknown total never divides by zero");
    }

    // ---- sprite rendering ---------------------------------------------------------------

    private static Canvas32 Icon(StatSprites s, double? fraction, int px = 1)
    {
        var c = new Canvas32(9 * px, 9 * px);
        BarRenderer.DrawIcon(c, s, fraction, 0, 0, 9 * px);
        return c;
    }

    /// <summary>Which sprite columns contain at least one pixel taken from the full sprite.</summary>
    private static List<int> FilledColumns(StatSprites s, Canvas32 c, int px = 1)
    {
        var cols = new List<int>();
        for (int x = 0; x < 9; x++)
        {
            bool any = false;
            for (int y = 0; y < 9 && !any; y++)
                any = s.Full[x, y] >> 24 != 0 && c.Pixels[y * px * c.Width + x * px] == s.Full[x, y];
            if (any) cols.Add(x);
        }
        return cols;
    }

    private static void IconRendering()
    {
        var set = FallbackSprites.Create();
        foreach (var (name, s) in new[] { ("heart", set.Heart), ("food", set.Food), ("armor", set.Armor) })
        {
            Eq(s.Empty.Width, 9, name + " sprite is 9 wide");
            Eq(s.Empty.Height, 9, name + " sprite is 9 tall");

            // Every pixel of the full art must sit inside the container's interior, never on or outside its outline.
            bool inside = true;
            for (int i = 0; i < 81; i++)
                if (s.Full.Pixels[i] >> 24 != 0 && s.Empty.Pixels[i] != 0xFF3A3A3A) inside = false;
            Check(inside, name + ": full art stays inside the container outline");

            Check(Icon(s, 0).Pixels.SequenceEqual(s.Empty.Pixels), name + ": 0 draws only the container");
            Check(Icon(s, null).Pixels.SequenceEqual(s.Empty.Pixels), name + ": unavailable draws only the container");

            var full = Icon(s, 1);
            bool allFull = true, outlineKept = true;
            for (int i = 0; i < 81; i++)
            {
                if (s.Full.Pixels[i] >> 24 != 0 && full.Pixels[i] != s.Full.Pixels[i]) allFull = false;
                if (s.Empty.Pixels[i] == 0xFF101010 && full.Pixels[i] != 0xFF101010) outlineKept = false;
            }
            Check(allFull, name + ": 1 draws the whole full sprite");
            Check(outlineKept, name + ": outline survives a full fill");

            // Partial fills reveal whole columns from the correct side and grow monotonically.
            int width = s.FillMax - s.FillMin + 1, previous = 0;
            for (int p = 0; p <= 100; p += 5)
            {
                var cols = FilledColumns(s, Icon(s, p / 100.0));
                int expected = FillMapping.FillColumns(p / 100.0, width);
                Eq(cols.Count, expected, $"{name}: {p}% reveals {expected} columns");
                if (cols.Count > 0)
                {
                    if (s.FillFromRight) Eq(cols[^1], s.FillMax, $"{name}: {p}% is anchored to the right edge");
                    else Eq(cols[0], s.FillMin, $"{name}: {p}% is anchored to the left edge");
                    Check(cols[^1] - cols[0] + 1 == cols.Count, $"{name}: {p}% fill is contiguous");
                }
                Check(cols.Count >= previous, $"{name}: fill never shrinks as the value rises");
                previous = cols.Count;
            }

            // Scaling is exact nearest-neighbour: every block of px×px pixels is one flat colour.
            var big = Icon(s, 0.6, px: 3);
            var small = Icon(s, 0.6);
            bool exact = true;
            for (int y = 0; y < 27; y++)
                for (int x = 0; x < 27; x++)
                    if (big.Pixels[y * 27 + x] != small.Pixels[y / 3 * 9 + x / 3]) exact = false;
            Check(exact, name + ": 3× scaling is pixel-exact");
        }

        Check(!set.Heart.FillFromRight && set.Food.FillFromRight && !set.Armor.FillFromRight, "hearts/armor fill from the left, hunger keeps its right side");
        Check(!set.HeartCharging.Full.Pixels.SequenceEqual(set.Heart.Full.Pixels), "charging heart is recoloured");

        // A genuine half sprite is used at exactly 50 %, and its side is detected automatically.
        var pal = new Dictionary<char, uint> { ['K'] = 0xFF000000, ['R'] = 0xFFFF0000 };
        var empty = Sprite.FromRows(new[] { "KKKK", "K..K", "K..K", "KKKK" }, pal);
        var fullS = Sprite.FromRows(new[] { "....", ".RR.", ".RR.", "...." }, pal);
        var left = new StatSprites(empty, fullS, Sprite.FromRows(new[] { "....", ".R..", ".R..", "...." }, pal));
        var right = new StatSprites(empty, fullS, Sprite.FromRows(new[] { "....", "..R.", "..R.", "...." }, pal));
        Check(!left.FillFromRight, "left-hand half sprite → fills from the left");
        Check(right.FillFromRight, "right-hand half sprite → drains from the left");
        var c = new Canvas32(4, 4);
        BarRenderer.DrawIcon(c, right, 0.5, 0, 0, 4);
        Check(c.Pixels[1 * 4 + 2] == 0xFFFF0000 && c.Pixels[1 * 4 + 1] == 0, "exact half uses the genuine half sprite");
    }

    private static void BarRendering()
    {
        var set = FallbackSprites.Create();
        const int px = 2;

        int FullIcons(StatSprites s, double percent, out int partialSlot)
        {
            var c = new Canvas32(BarRenderer.BarWidth * px, 9 * px);
            BarRenderer.DrawBar(c, s, percent, 0, 0, px);
            int full = 0;
            partialSlot = -1;
            int width = s.FillMax - s.FillMin + 1;
            for (int slot = 0; slot < 10; slot++)
            {
                // Count the columns of this slot that show full-sprite pixels (ignoring the 1-unit overlap with neighbours).
                int cols = 0;
                for (int x = s.FillMin; x <= s.FillMax; x++)
                {
                    bool any = false;
                    for (int y = 0; y < 9 && !any; y++)
                        any = s.Full[x, y] >> 24 != 0 && c.Pixels[y * px * c.Width + (slot * BarRenderer.Pitch + x) * px] == s.Full[x, y];
                    if (any) cols++;
                }
                if (cols == width) full++;
                else if (cols > 0) partialSlot = slot;
            }
            return full;
        }

        Eq(FullIcons(set.Heart, 100, out _), 10, "heart bar at 100% shows ten full hearts");
        Eq(FullIcons(set.Heart, 0, out _), 0, "heart bar at 0% shows none");
        Eq(FullIcons(set.Heart, 83, out int hp), 8, "heart bar at 83% shows eight full hearts");
        Eq(hp, 8, "…and the ninth heart (slot 8) is partial");
        Eq(FullIcons(set.Armor, 64, out int ap), 6, "armor bar at 64% shows six full icons");
        Eq(ap, 6, "…and the seventh is partial");
        Eq(FullIcons(set.Food, 65, out int fp), 6, "hunger bar at 65% shows six full drumsticks");
        Eq(fp, 3, "…drained from the left, so the partial one is slot 3");

        var compact = CompactRenderer.Render(set, new CompactState(1, 0.5, null, false), 92, 72, 3, 3, null, false);
        Eq(compact.Width, 92, "compact surface keeps the requested width");
        Check(compact.Pixels[0] == 0, "compact surface is transparent outside the icons");
        Check(CompactRenderer.ContentWidth(3, 3) == 87, "three 27 px icons and two 3 px gaps are 87 px wide");
        var covered = CompactRenderer.Render(set, new CompactState(1, 1, 1, false), 92, 4, 1, 1, new uint[] { 0x00112233, 0x00445566 }, false);
        Check(covered.Pixels[0] == 0xFF112233 && covered.Pixels[92 * 3] == 0xFF445566, "cover background is opaque and repeats its last row");
    }

    // ---- hover state machine ------------------------------------------------------------

    private static void HoverLogic()
    {
        var h = new HoverController { OpenDelayMs = 200, CloseDelayMs = 400 };

        Check(!h.Update(false, 0), "starts closed");
        Check(!h.NeedsPolling, "idle when closed and outside");
        Check(!h.Update(true, 1000), "entering does not open immediately");
        Check(h.NeedsPolling, "polls while the open delay runs");
        Check(!h.Update(true, 1150), "still closed before the delay");
        Check(h.Update(true, 1200), "opens after the hover delay");

        // Moving between icons / into the HUD is just "inside" for the shared controller; short gaps are tolerated.
        Check(h.Update(false, 1300), "leaving does not close immediately");
        Check(h.Update(true, 1500), "re-entering cancels the close timer");
        Check(h.Update(false, 1600), "leaving again restarts the timer");
        Check(h.Update(false, 1999), "still open just before the close delay");
        Check(!h.Update(false, 2000), "closes after the close delay");
        Check(!h.NeedsPolling, "idle again after closing");

        // A quick pass over the icons never opens anything.
        h.Update(true, 3000);
        Check(!h.Update(false, 3100), "brief hover does not open");
        Check(!h.Update(false, 3400), "…and stays closed");
        h.Update(true, 4000);
        Check(!h.Update(true, 4199) && h.Update(true, 4200), "open delay restarts from the new entry");

        // Count how often the HUD would be (re)shown during a jittery session: exactly once.
        var j = new HoverController { OpenDelayMs = 200, CloseDelayMs = 400 };
        int shows = 0;
        bool visible = false;
        for (long t = 0; t <= 5000; t += 50)
        {
            bool inside = t % 500 < 350;           // drops out for 150 ms twice a second
            bool now = j.Update(inside, t);
            if (now && !visible) shows++;
            visible = now;
        }
        Eq(shows, 1, "jitter shorter than the close delay never re-opens the HUD");

        // Pinning.
        var p = new HoverController { OpenDelayMs = 200, CloseDelayMs = 400 };
        p.SetPinned(true, true);
        Check(p.Visible, "pinning shows the HUD at once");
        Check(p.Update(false, 10_000) && p.Update(false, 20_000), "a pinned HUD stays open with the pointer away");
        p.SetPinned(false, false);
        Check(!p.Visible, "unpinning with the pointer away closes it");
        p.SetPinned(true, true);
        p.SetPinned(false, true);
        Check(p.Visible, "unpinning under the pointer keeps it hover-open");
        Check(p.Update(false, 30_000) && !p.Update(false, 30_400), "…and it then closes with the normal delay");

        var z = new HoverController { OpenDelayMs = 0, CloseDelayMs = 0 };
        Check(z.Update(true, 0) && !z.Update(false, 1), "zero delays open and close immediately");
    }

    // ---- CPU ----------------------------------------------------------------------------

    private static void CpuCalculation()
    {
        var c = new CpuUsageCalculator(window: 4);
        // kernel time includes idle time, exactly as GetSystemTimes reports it.
        Check(c.Next(idle: 1000, kernel: 1500, user: 500) == null, "first sample yields no reading");
        Near(c.Next(1750, 2300, 700)!.Value, 25, "750 idle of 1000 elapsed = 25% busy");
        Near(c.Next(1750, 3100, 900)!.Value, 62.5, "moving average of 25% and 100%");
        Near(c.Raw!.Value, 100, "raw sample is unsmoothed");
        Near(c.Next(1750, 3100, 900)!.Value, 62.5, "a zero-length interval adds no sample");
        Near(c.Next(100, 200, 100)!.Value, 62.5, "counters going backwards are ignored");
        c.Next(200, 300, 100);   // 100 elapsed, 100 idle → 0 %
        Near(c.Raw!.Value, 0, "recovers after a counter reset");

        var w = new CpuUsageCalculator(window: 2);
        ulong idle = 0, kernel = 0;
        w.Next(idle, kernel, 0);
        foreach (int busy in new[] { 10, 20, 90 })
        {
            idle += (ulong)(100 - busy);
            kernel += 100;
            w.Next(idle, kernel, 0);
        }
        Near(w.Smoothed!.Value, 55, "window keeps only the newest samples");
    }

    // ---- font & HUD ---------------------------------------------------------------------

    private static void FontAndHud()
    {
        foreach (char ch in "BATTERY CPU RAM FREE 0123456789% N/A -- CHARGING PLUGGED IN NO GB . ALIGNMENT MODE DRAG THE BOX OR USE ARROW KEYS SHIFT + : 10 PX ENTER SAVE ESC CANCEL")
            Check(PixelFont.HasGlyph(ch), $"font has a glyph for '{ch}'");
        Eq(PixelFont.Measure("100%"), 23, "text measurement");
        foreach (char ch in "abcdefghijklmnopqrstuvwxyz,!()")
            Check(PixelFont.HasGlyph(ch), $"font has a lower-case/punctuation glyph for '{ch}'");

        var set = FallbackSprites.Create();
        var state = new HudState(83, true, false, false, true, 35, 64, 5.8, 15.7);
        var hud = HudRenderer.Render(set, state, 3, light: false);
        Check(hud.Width % 3 == 0 && hud.Height % 3 == 0, "HUD size is a whole number of sprite units");
        Check(hud.Width >= BarRenderer.BarWidth * 3, "HUD is wide enough for ten icons");
        Check(hud.Pixels[0] == 0, "HUD corner pixel is notched (transparent)");
        Check(hud.Pixels[hud.Width / 2] >> 24 != 0, "HUD top edge is drawn");

        // Every pixel sits on the 3×3 grid: each unit block is a single colour (no blur, no partial pixels).
        bool grid = true;
        for (int y = 0; y < hud.Height && grid; y++)
            for (int x = 0; x < hud.Width && grid; x++)
                grid = hud.Pixels[y * hud.Width + x] == hud.Pixels[y / 3 * 3 * hud.Width + x / 3 * 3];
        Check(grid, "HUD is drawn entirely on the sprite-pixel grid");

        var again = HudRenderer.Render(set, state, 3, light: false);
        Check(hud.Pixels.SequenceEqual(again.Pixels), "identical readings render identical pixels (no needless redraw)");
        var other = HudRenderer.Render(set, state with { BatteryPercent = 82 }, 3, light: false);
        Check(!hud.Pixels.SequenceEqual(other.Pixels), "a one-point change is visible in the HUD");

        var none = HudRenderer.Render(set, new HudState(null, false, true, false, true, null, null, 0, 0), 2, light: true);
        Check(none.Width > 0 && none.Height > 0, "HUD renders with every reading unavailable");
    }

    // ---- survival bars layout -----------------------------------------------------------

    private static void SurvivalBars()
    {
        var set = FallbackSprites.Create();
        const int px = 3;
        int w = SurvivalBarsRenderer.ContentWidth(px), h = SurvivalBarsRenderer.ContentHeight(px);
        Eq(w, 81 * px, "bars are ten 9-unit icons on an 8-unit pitch");
        Eq(h, 19 * px, "two rows plus a one-unit gap");

        // Each icon is compared against a single icon drawn with the expected fraction.
        Canvas32 Single(StatSprites s, double f)
        {
            var c = new Canvas32(BarRenderer.Cell * px, BarRenderer.Cell * px);
            BarRenderer.DrawIcon(c, s, f, 0, 0, BarRenderer.Cell * px);
            return c;
        }
        bool IconMatches(Canvas32 bars, int rowY, int index, StatSprites s, double f)
        {
            var one = Single(s, f);
            for (int y = 0; y < one.Height; y++)
                for (int x = 0; x < one.Width; x++)
                {
                    uint a = one.Pixels[y * one.Width + x];
                    if (a >> 24 == 0) continue;
                    // Neighbours overlap by one unit; only test the icon's own leftmost 8 units.
                    if (x >= BarRenderer.Pitch * px && index < 9) continue;
                    uint b = bars.Pixels[(rowY + y) * bars.Width + index * BarRenderer.Pitch * px + x];
                    if (a != b) return false;
                }
            return true;
        }

        foreach (var (bat, ram) in new[] { (100.0, 100.0), (83.0, 64.0), (50.0, 0.0), (0.0, 37.0) })
        {
            var c = SurvivalBarsRenderer.Render(set, bat, ram, false, w, h, px, false);
            bool ok = true;
            for (int i = 0; i < 10; i++)
            {
                ok &= IconMatches(c, 0, i, set.Armor, FillMapping.IconFraction(ram, i));   // armor on top, as in the game
                ok &= IconMatches(c, (BarRenderer.Cell + SurvivalBarsRenderer.RowGap) * px, i, set.Heart, FillMapping.IconFraction(bat, i));
            }
            Check(ok, $"survival bars {bat}% battery / {ram}% RAM draw the right fill per icon");
        }
        Near(FillMapping.IconFraction(83, 8), 0.3, "83% battery: ninth heart partly filled");
        Eq(SurvivalBarsRenderer.ContentHeight(px, 1), 9 * px, "hearts only: one row");
        var one = SurvivalBarsRenderer.RenderRows(set, new[] { (set.Heart, (double?)70) }, w, 9 * px, px, false);
        bool oneOk = true;
        for (int i = 0; i < 10; i++) oneOk &= IconMatches(one, 0, i, set.Heart, FillMapping.IconFraction(70, i));
        Check(oneOk, "hearts-only row draws the hearts");
        var hh = SurvivalBarsRenderer.RenderRows(set, new[] { (set.Heart, (double?)50), (set.Food, (double?)100) }, w, h, px, false);
        bool hhOk = true;
        for (int i = 0; i < 10; i++)
        {
            hhOk &= IconMatches(hh, 0, i, set.Heart, FillMapping.IconFraction(50, i));
            hhOk &= IconMatches(hh, (BarRenderer.Cell + SurvivalBarsRenderer.RowGap) * px, i, set.Food, 1);
        }
        Check(hhOk, "hearts + hunger: hearts on top, hunger below");
        Near(FillMapping.IconFraction(64, 6), 0.4, "64% RAM: seventh armor partly filled");
    }

    // ---- speed formatting ------------------------------------------

    private static void HudExtras()
    {
        Eq(HudRenderer.FormatRate(1.5e6), "12 Mbps", "1.5 MB/s = 12 Mbps");
        Eq(HudRenderer.FormatRate(9e4), "720 kbps", "90 KB/s = 720 kbps");
        Eq(HudRenderer.SlotCount(12.43), "12.4", "slot count keeps real value: 12.4");
        Eq(HudRenderer.SlotCount(0.72), "0.72", "slot count: 0.72");
        Eq(HudRenderer.SlotCount(3.5), "3.5", "slot count: 3.5");
        Eq(HudRenderer.SlotCount(402.3), "402", "slot count: 402");
        Eq(HudRenderer.SlotCount(0), "0", "slot count: zero");
        Eq(HudRenderer.SlotCount(0.057), "0.06", "slot count: small values keep two decimals");
        Check(new[] { 0.0, 0.013, 0.72, 9.99, 12.43, 402.3, 9999, 12000, 3.4e6 }.All(v => HudRenderer.SlotCount(v).Length <= 4), "slot counts fit a slot (four characters)");
        Eq(HudRenderer.SlotCount(12000), "12K", "absurdly large values are shortened");
        Eq(HudRenderer.SlotCount(3.4e6), "3.4M", "absurdly large values are shortened (M)");
        foreach (var (name, icon) in new[] { ("sugar", FallbackSprites.Sugar()), ("feather", FallbackSprites.Feather()), ("chest", FallbackSprites.Chest()),
                                             ("clock", FallbackSprites.Clock()), ("golden apple", FallbackSprites.GoldenApple()), ("redstone", FallbackSprites.Redstone()) })
            Check(icon.Width == 16 && icon.Height == 16 && !icon.IsEmpty, $"built-in {name} icon is a visible 16×16 sprite (no game needed)");
        var noDrives = new HudState(83, true, false, false, true, 20, 50, 8, 16, 10,
            new GameData(1e5, 1e4, 1e11, 5e11, 3600, 100, 1000, null, null, null, HudRenderer.GameHitAt((7 + 3 + 6 * 20 + 8) * 2, (7 + HudRenderer.GameTipReserve + 27 + 10) * 2, 2)));
        Check(HudRenderer.RenderGame(FallbackSprites.Create(), noDrives, 2, false).Width > 0, "hotbar renders with no other drives and the drives slot hovered");
        Eq(HudRenderer.GameHitAt((7 + 3 + 8 * 20 + 8) * 2, (7 + HudRenderer.GameTipReserve + 27 + 10) * 2, 2), 8, "hit test: ninth (volume) slot");
        var desktop = new HudState(null, false, true, false, true, 20, 50, 8, 16);
        Eq(HudRenderer.HeartPercent(desktop), 100, "desktop PC (no battery): hearts are full");
        Eq(HudRenderer.HeartPercent(desktop with { HasBattery = true, BatteryPercent = 42 }), 42, "laptop: hearts follow the battery");
        Eq(HudRenderer.GameHitAt(0, 0, 2), -1, "hit test: popup border");
        int O(int units) => (7 + units) * 2;   // frame + padding are 7 units; px = 2
        int OY(int units) => (7 + HudRenderer.GameTipReserve + units) * 2;   // rows sit below the tooltip strip
        Eq(HudRenderer.GameHitAt(O(5), OY(4), 2), HudRenderer.HitArmor, "hit test: armor row");
        Eq(HudRenderer.GameHitAt(O(5), OY(14), 2), HudRenderer.HitHearts, "hit test: hearts row");
        Eq(HudRenderer.GameHitAt(O(150), OY(14), 2), HudRenderer.HitHunger, "hit test: hunger row");
        Eq(HudRenderer.GameHitAt(O(90), OY(22), 2), HudRenderer.HitXp, "hit test: XP bar");
        Eq(HudRenderer.GameHitAt(O(3 + 2 * 20 + 8), OY(27 + 10), 2), 2, "hit test: third hotbar slot");
    }
}
