namespace SurvivalHud.Core;

/// <summary>The sprites for one statistic: an empty container, the full icon and (optionally) a genuine half icon.</summary>
public sealed class StatSprites
{
    public Sprite Empty { get; }
    public Sprite Full { get; }
    public Sprite? Half { get; }

    /// <summary>True when the icon drains from the left, i.e. what remains is on the right (Minecraft's hunger bar).</summary>
    public bool FillFromRight { get; }

    /// <summary>Column range of the full sprite that actually carries pixels.</summary>
    public int FillMin { get; }
    public int FillMax { get; }

    public StatSprites(Sprite empty, Sprite full, Sprite? half, bool? fillFromRight = null)
    {
        Empty = empty;
        Full = full;
        Half = half;
        (FillMin, FillMax) = full.OpaqueColumns();
        FillFromRight = fillFromRight ?? DetectFillFromRight(empty, full, half);
    }

    public StatSprites WithFull(Sprite full, Sprite? half) => new(Empty, full, half, FillFromRight);

    /// <summary>
    /// Works out which side a genuine half sprite keeps by looking at where its "filled"
    /// pixels (those matching the full sprite but not the empty one) sit.
    /// </summary>
    private static bool DetectFillFromRight(Sprite empty, Sprite full, Sprite? half)
    {
        if (half == null || half.Width != full.Width || half.Height != full.Height) return false;
        bool sameSizeEmpty = empty.Width == full.Width && empty.Height == full.Height;
        long sumX = 0, n = 0;
        for (int y = 0; y < half.Height; y++)
            for (int x = 0; x < half.Width; x++)
            {
                uint h = half[x, y];
                if (h >> 24 == 0 || h != full[x, y]) continue;
                if (sameSizeEmpty && h == empty[x, y]) continue;
                sumX += x;
                n++;
            }
        if (n == 0) return false;
        var (min, max) = full.OpaqueColumns();
        return sumX / (double)n > (min + max) / 2.0;
    }
}

/// <summary>Extra art for the game-style popup: the hotbar (182×22), its selection frame and three item icons (16×16).</summary>
public sealed record GameSprites(Sprite? Hotbar, Sprite? Sugar, Sprite? Feather, Sprite? ChestMinecart,
    Sprite? Clock = null, Sprite? GoldenApple = null, Sprite? Redstone = null,
    Sprite? Bundle = null, Sprite? WritableBook = null, Sprite? MusicDisc = null);

public sealed class SpriteSet
{
    public StatSprites Heart { get; }
    public StatSprites Food { get; }
    public StatSprites Armor { get; }

    /// <summary>Gold-tinted heart used as the (optional) charging indicator.</summary>
    public StatSprites HeartCharging { get; }

    /// <summary>Minecraft's experience bar (182×5 in the game), used for the internet-speed row. Null = built-in art.</summary>
    public Sprite? XpBackground { get; init; }
    public Sprite? XpProgress { get; init; }

    /// <summary>Hotbar and item icons for the game-style popup. Null = built-in hotbar, no items.</summary>
    public GameSprites? Game { get; init; }

    /// <summary>Human-readable origin, e.g. "Minecraft 1.21.11" or "Built-in pixel art".</summary>
    public string Source { get; }

    public SpriteSet(StatSprites heart, StatSprites food, StatSprites armor, string source)
    {
        Heart = heart;
        Food = food;
        Armor = armor;
        Source = source;
        HeartCharging = heart.WithFull(heart.Full.Map(ToGold), heart.Half?.Map(ToGold));
    }

    /// <summary>Recolours the red heart to gold while keeping its shading; dark outline pixels are left alone.</summary>
    private static uint ToGold(uint p)
    {
        uint a = p >> 24, r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
        if (a == 0 || r < 60 || r <= g + 20) return p;
        uint ng = Math.Max(g, r * 200 / 255), nb = Math.Min(b, 70u) * 2 / 3;
        return (a << 24) | (r << 16) | (ng << 8) | nb;
    }
}
