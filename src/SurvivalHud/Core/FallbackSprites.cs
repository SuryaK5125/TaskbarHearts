namespace SurvivalHud.Core;

/// <summary>
/// Original 9×9 pixel-art sprites drawn for this app (no Minecraft assets are bundled).
/// They are used whenever genuine textures are not available. Each statistic has an empty
/// container and a "full" interior that is composited on top of it.
/// </summary>
public static class FallbackSprites
{
    private const uint Outline = 0xFF101010;
    private const uint Hollow = 0xFF3A3A3A;

    private static readonly Dictionary<char, uint> Palette = new()
    {
        ['K'] = Outline,
        ['G'] = Hollow,
        // heart
        ['R'] = 0xFFE8201C,
        ['W'] = 0xFFFFC2C2,
        ['D'] = 0xFFA6120F,
        // drumstick
        ['B'] = 0xFFB4682C,
        ['L'] = 0xFFD98F4A,
        ['N'] = 0xFF7E4520,
        ['O'] = 0xFFF2EEDF,
        // armor
        ['H'] = 0xFFF4F4F6,
        ['S'] = 0xFFC2C2CA,
        ['M'] = 0xFF8C8C96,
    };

    private static readonly string[] HeartEmpty =
    {
        ".KKK.KKK.",
        "KGGGKGGGK",
        "KGGGGGGGK",
        "KGGGGGGGK",
        "KGGGGGGGK",
        ".KGGGGGK.",
        "..KGGGK..",
        "...KGK...",
        "....K....",
    };

    private static readonly string[] HeartFull =
    {
        ".........",
        ".RRR.RRR.",
        ".RWRRRRD.",
        ".RRRRRRD.",
        ".RRRRRDD.",
        "..RRRDD..",
        "...RDD...",
        "....D....",
        ".........",
    };

    private static readonly string[] FoodEmpty =
    {
        "....KKKK.",
        "...KGGGGK",
        "..KGGGGGK",
        "..KGGGGGK",
        "..KGGGGK.",
        ".KKGGGK..",
        "KGGKKK...",
        "KGK......",
        ".K.......",
    };

    private static readonly string[] FoodFull =
    {
        ".........",
        "....BLLB.",
        "...BLBBB.",
        "...BBBBN.",
        "...BBBN..",
        "...NNN...",
        ".OO......",
        ".O.......",
        ".........",
    };

    private static readonly string[] ArmorEmpty =
    {
        "KKK...KKK",
        "KGGK.KGGK",
        "KGGGKGGGK",
        "KGGGGGGGK",
        ".KGGGGGK.",
        ".KGGGGGK.",
        ".KGGGGGK.",
        "..KGGGK..",
        "...KKK...",
    };

    private static readonly string[] ArmorFull =
    {
        ".........",
        ".HH...HS.",
        ".HSS.SSM.",
        ".HSSSSSM.",
        "..HSSSM..",
        "..SSSSM..",
        "..SSSMM..",
        "...MMM...",
        ".........",
    };

    // ---- original 16×16 item icons for the hotbar (used when no Minecraft textures are available) ----

    private static readonly Dictionary<char, uint> ItemPalette = new()
    {
        ['K'] = 0xFF1A1A1A, // outline
        ['W'] = 0xFFFFFFFF, ['w'] = 0xFFDCDCE6, ['g'] = 0xFFA8A8B4,          // white / light grey / grey
        ['F'] = 0xFFF2F2F2, ['f'] = 0xFFC8C8D2, ['q'] = 0xFF8C8C96,          // feather
        ['B'] = 0xFF9A6A34, ['b'] = 0xFF6E4A22, ['n'] = 0xFFC48A48,          // wood
        ['L'] = 0xFF5A5A62, ['l'] = 0xFF8A8A94,                              // iron
        ['Y'] = 0xFFF2C437, ['y'] = 0xFFC8962A, ['o'] = 0xFF8A6418, ['H'] = 0xFFFFF0A0, // gold
        ['G'] = 0xFF4CA02C, ['e'] = 0xFF2E6A18,                              // leaf
        ['R'] = 0xFFE01818, ['r'] = 0xFF9A0C0C, ['P'] = 0xFFFF7070,          // redstone
        ['D'] = 0xFF3A6FB0,                                                  // clock face accent
        ['C'] = 0xFF5BE8D6, ['c'] = 0xFF2BA898,                              // diamond
        ['V'] = 0xFF9888B8, ['v'] = 0xFF625380,                              // elytra
    };

    private static Sprite Item(string[] rows) => Sprite.FromRows(rows, ItemPalette);

    /// <summary>A small heap of white granules (download speed).</summary>
    public static Sprite Sugar() => Item(new[]
    {
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        ".......W........",
        "......WwW.......",
        ".....WwWwW..W...",
        "....WwWWwWwWwW..",
        "...WWwWwWWwWWwW.",
        "..WwWWgWwWWgWwW.",
        "..wWgWWwWgWWwgWg",
        "...gwgwgwgwgwgg.",
        "................",
        "................",
    });



    /// <summary>A round gold pocket watch (uptime).</summary>
    public static Sprite Clock() => Item(new[]
    {
        "................",
        "......KKKK......",
        "....KKyYYyKK....",
        "...KyYHHHHYyK...",
        "..KyHwwwwwwHyK..",
        "..KYwwwwKwwwYK..",
        ".KyHwwwwKwwwHyK.",
        ".KYwwwwwKwwwwYK.",
        ".KYwwwwwKKKwwYK.",
        ".KyHwwwwwwwwHyK.",
        "..KYwwwwwwwwYK..",
        "..KyHwwDwwwHyK..",
        "...KyYHHHHYyK...",
        "....KKyYYyKK....",
        "......KKKK......",
        "................",
    });


    /// <summary>A pile of glowing red dust (processes).</summary>
    public static Sprite Redstone() => Item(new[]
    {
        "................",
        "................",
        "................",
        "................",
        "................",
        "........R.......",
        "......RPR...R...",
        ".....RRPRR.RPR..",
        "....RrRRPRRRRr..",
        "...RRPRrRRPRrRR.",
        "..RrRRRPRrRRRPRr",
        "..rRRrRRRRrRRRr.",
        "...rRrrRrRrrRr..",
        "................",
        "................",
        "................",
    });

    /// <summary>A leather pouch tied at the top (other drives).</summary>
    public static Sprite Bundle() => Item(new[]
    {
        "................",
        "................",
        "......KKKK......",
        ".....KnBBnK.....",
        "......KbbK......",
        ".....KbnnbK.....",
        "....KBBBBBBK....",
        "...KBBnBBBBBK...",
        "..KBBBBBBBBBBK..",
        "..KBnBBBBBBBbK..",
        "..KBBBBBBBBBbK..",
        "..KBBBBBBBBbbK..",
        "...KbBBBBBbbK...",
        "....KKKKKKKK....",
        "................",
        "................",
    });


    /// <summary>A black record with a coloured label (volume).</summary>
    public static Sprite MusicDisc() => Item(new[]
    {
        "................",
        ".....KKKKKK.....",
        "...KKLLLLLLKK...",
        "..KLLKKKKKKLLK..",
        "..KLKLLLLLLKLK..",
        ".KLKLLYYYYLLKLK.",
        ".KLKLYYYYYYLKLK.",
        ".KLKLYYKKYYLKLK.",
        ".KLKLYYKKYYLKLK.",
        ".KLKLYYYYYYLKLK.",
        ".KLKLLYYYYLLKLK.",
        "..KLKLLLLLLKLK..",
        "..KLLKKKKKKLLK..",
        "...KKLLLLLLKK...",
        ".....KKKKKK.....",
        "................",
    });

    /// <summary>A paper rocket on a stick (upload speed).</summary>
    public static Sprite FireworkRocket() => Item(new[]
    {
        "................",
        ".......KK.......",
        "......KggK......",
        ".....KgwwgK.....",
        ".....KRRRRK.....",
        ".....KWWWWK.....",
        ".....KRRRRK.....",
        ".....KWWWWK.....",
        ".....KRRRRK.....",
        ".....KWWWWK.....",
        ".....KKKKKK.....",
        ".......bn.......",
        ".......bn.......",
        ".......bn.......",
        ".......KK.......",
        "................",
    });

    /// <summary>A diamond pickaxe (free space on C:).</summary>
    public static Sprite DiamondPickaxe() => Item(new[]
    {
        "................",
        "....KKKKKK......",
        "...KCCCCCCK.....",
        "..KCcKKKKCCK....",
        "..KcK..KbnKCK...",
        "..KK..KbnK.KcK..",
        ".....KbnK...KcK.",
        "....KbnK....KK..",
        "...KbnK.........",
        "..KbnK..........",
        ".KbnK...........",
        ".KnK............",
        "..K.............",
        "................",
        "................",
        "................",
    });

    /// <summary>A pair of elytra wings (battery health).</summary>
    public static Sprite Elytra() => Item(new[]
    {
        "................",
        "..KKKK....KKKK..",
        ".KVVVVK..KVVVVK.",
        ".KVvVVK..KVVvVK.",
        ".KVvVVK..KVVvVK.",
        ".KVVvVK..KVvVVK.",
        "..KVvVK..KVvVK..",
        "..KVVvK..KvVVK..",
        "..KVVVK..KVVVK..",
        "...KVvK..KvVK...",
        "...KVVK..KVVK...",
        "....KVK..KVK....",
        ".....KK..KK.....",
        "................",
        "................",
        "................",
    });

    /// <summary>A steel striker and a flint (top CPU process).</summary>
    public static Sprite FlintAndSteel() => Item(new[]
    {
        "................",
        "........KKKK....",
        ".......KwwwwK...",
        "......KwKKKKwK..",
        "......KwK..KwK..",
        "......KwK..KwK..",
        ".......KKKKwK...",
        "...........KK...",
        "..KKKK..........",
        ".KLLLLK.........",
        "KLgLLLLK........",
        "KLLLgLLK........",
        ".KLLLLK.........",
        "..KKKK..........",
        "................",
        "................",
    });

    /// <summary>A plain 182×22 bar of nine slots, used when no Minecraft hotbar texture is available.</summary>
    public static Sprite Hotbar()
    {
        const int w = 182, h = 22;
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool outer = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                int sx = (x - 1) % 20;
                bool divider = x > 0 && x < w - 1 && (sx == 0 || sx == 19);
                bool rim = y == 1 || y == h - 2;
                px[y * w + x] = outer ? 0xFF000000u : divider || rim ? 0xFF555555u : 0xC0303030u;
            }
        return new Sprite(w, h, px);
    }

    /// <summary>A plain 182×5 bar in the style of an experience bar: dark trough, green fill, darker outline.</summary>
    public static Sprite XpBar(bool progress)
    {
        const int w = 182, h = 5;
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = y == 0 || y == h - 1 || x == 0 || x == w - 1;
                bool tick = !progress && x % 20 == 0 && x > 0 && x < w - 1;
                px[y * w + x] = progress
                    ? (edge ? 0xFF1F5F0Fu : y == 1 ? 0xFFB0FF60u : 0xFF80D020u)
                    : (edge || tick ? 0xFF000000u : 0xFF3A3A3Au);
            }
        return new Sprite(w, h, px);
    }

    public static SpriteSet Create()
    {
        Sprite S(string[] rows) => Sprite.FromRows(rows, Palette);
        return new SpriteSet(
            new StatSprites(S(HeartEmpty), S(HeartFull), null, fillFromRight: false),
            new StatSprites(S(FoodEmpty), S(FoodFull), null, fillFromRight: true),
            new StatSprites(S(ArmorEmpty), S(ArmorFull), null, fillFromRight: false),
            "Built-in pixel art");
    }
}
