namespace SurvivalHud.Core;

/// <summary>
/// Text in the popups. Uses the game's own font (font/ascii.png from the player's Minecraft
/// JAR, with its proportional widths) when <see cref="UseBitmap"/> was given one; otherwise an
/// original 5×7 bitmap font (upper and lower case, digits, a little punctuation) stored as
/// pixel matrices.
/// </summary>
public static class PixelFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    public const int Advance = 6;

    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['A'] = "01110 10001 10001 11111 10001 10001 10001",
        ['B'] = "11110 10001 10001 11110 10001 10001 11110",
        ['C'] = "01110 10001 10000 10000 10000 10001 01110",
        ['D'] = "11110 10001 10001 10001 10001 10001 11110",
        ['E'] = "11111 10000 10000 11110 10000 10000 11111",
        ['F'] = "11111 10000 10000 11110 10000 10000 10000",
        ['G'] = "01110 10001 10000 10111 10001 10001 01110",
        ['H'] = "10001 10001 10001 11111 10001 10001 10001",
        ['I'] = "01110 00100 00100 00100 00100 00100 01110",
        ['J'] = "00001 00001 00001 00001 10001 10001 01110",
        ['K'] = "10001 10010 10100 11000 10100 10010 10001",
        ['L'] = "10000 10000 10000 10000 10000 10000 11111",
        ['M'] = "10001 11011 10101 10101 10001 10001 10001",
        ['N'] = "10001 11001 10101 10011 10001 10001 10001",
        ['O'] = "01110 10001 10001 10001 10001 10001 01110",
        ['P'] = "11110 10001 10001 11110 10000 10000 10000",
        ['Q'] = "01110 10001 10001 10001 10101 10010 01101",
        ['R'] = "11110 10001 10001 11110 10100 10010 10001",
        ['S'] = "01111 10000 10000 01110 00001 00001 11110",
        ['T'] = "11111 00100 00100 00100 00100 00100 00100",
        ['U'] = "10001 10001 10001 10001 10001 10001 01110",
        ['V'] = "10001 10001 10001 10001 10001 01010 00100",
        ['W'] = "10001 10001 10001 10101 10101 11011 10001",
        ['X'] = "10001 10001 01010 00100 01010 10001 10001",
        ['Y'] = "10001 10001 01010 00100 00100 00100 00100",
        ['Z'] = "11111 00001 00010 00100 01000 10000 11111",
        ['0'] = "01110 10001 10011 10101 11001 10001 01110",
        ['1'] = "00100 01100 00100 00100 00100 00100 01110",
        ['2'] = "01110 10001 00001 00110 01000 10000 11111",
        ['3'] = "01110 10001 00001 00110 00001 10001 01110",
        ['4'] = "00010 00110 01010 10010 11111 00010 00010",
        ['5'] = "11111 10000 11110 00001 00001 10001 01110",
        ['6'] = "00110 01000 10000 11110 10001 10001 01110",
        ['7'] = "11111 00001 00010 00100 01000 01000 01000",
        ['8'] = "01110 10001 10001 01110 10001 10001 01110",
        ['9'] = "01110 10001 10001 01111 00001 00010 01100",
        ['%'] = "11001 11010 00010 00100 01000 01011 10011",
        ['.'] = "00000 00000 00000 00000 00000 01100 01100",
        ['/'] = "00001 00001 00010 00100 01000 10000 10000",
        ['-'] = "00000 00000 00000 11111 00000 00000 00000",
        ['+'] = "00000 00100 00100 11111 00100 00100 00000",
        [':'] = "00000 01100 01100 00000 01100 01100 00000",
        [' '] = "00000 00000 00000 00000 00000 00000 00000",
        // Lower case. Letters with a tail (g j p q y ,) have an eighth row that hangs below the
        // baseline, as in the game's font; layout still treats every glyph as 7 rows tall.
        ['a'] = "00000 00000 01110 00001 01111 10001 01111",
        ['b'] = "10000 10000 10110 11001 10001 10001 11110",
        ['c'] = "00000 00000 01110 10000 10000 10001 01110",
        ['d'] = "00001 00001 01101 10011 10001 10001 01111",
        ['e'] = "00000 00000 01110 10001 11111 10000 01110",
        ['f'] = "00110 01001 01000 11100 01000 01000 01000",
        ['g'] = "00000 00000 01111 10001 10001 01111 00001 11110",
        ['h'] = "10000 10000 10110 11001 10001 10001 10001",
        ['i'] = "00100 00000 01100 00100 00100 00100 01110",
        ['j'] = "00010 00000 00110 00010 00010 00010 10010 01100",
        ['k'] = "10000 10000 10010 10100 11000 10100 10010",
        ['l'] = "01100 00100 00100 00100 00100 00100 01110",
        ['m'] = "00000 00000 11010 10101 10101 10101 10101",
        ['n'] = "00000 00000 10110 11001 10001 10001 10001",
        ['o'] = "00000 00000 01110 10001 10001 10001 01110",
        ['p'] = "00000 00000 10110 11001 10001 11110 10000 10000",
        ['q'] = "00000 00000 01101 10011 10001 01111 00001 00001",
        ['r'] = "00000 00000 10110 11001 10000 10000 10000",
        ['s'] = "00000 00000 01111 10000 01110 00001 11110",
        ['t'] = "01000 01000 11100 01000 01000 01001 00110",
        ['u'] = "00000 00000 10001 10001 10001 10011 01101",
        ['v'] = "00000 00000 10001 10001 10001 01010 00100",
        ['w'] = "00000 00000 10001 10001 10101 10101 01010",
        ['x'] = "00000 00000 10001 01010 00100 01010 10001",
        ['y'] = "00000 00000 10001 10001 10001 01111 00001 11110",
        ['z'] = "00000 00000 11111 00010 00100 01000 11111",
        [','] = "00000 00000 00000 00000 00000 01100 00100 01000",
        ['!'] = "00100 00100 00100 00100 00100 00000 00100",
        ['('] = "00010 00100 01000 01000 01000 00100 00010",
        [')'] = "01000 00100 00010 00010 00010 00100 01000",
    };

    // ---- the game's font -----------------------------------------------------------------------

    /// <summary>Per ASCII character: advance in font units and an 8×8 pixel mask (rows 0–6 sit above the baseline, row 7 hangs below).</summary>
    private static (int Advance, bool[] Mask)?[]? _bitmap;

    /// <summary>
    /// Switches to the game's font. <paramref name="ascii"/> is Minecraft's font/ascii.png: a
    /// 16×16 grid of 8×8 cells laid out by character code (resource packs may scale it up by a
    /// whole factor). Each glyph is as wide as its rightmost lit column, plus one pixel of
    /// spacing, and a space is 4 wide, exactly as the game measures them. Null = built-in font.
    /// </summary>
    public static void UseBitmap(Sprite? ascii)
    {
        if (ascii is null || ascii.Width < 128 || ascii.Width % 128 != 0 || ascii.Height != ascii.Width) { _bitmap = null; return; }
        int k = ascii.Width / 128;
        var table = new (int, bool[])?[128];
        for (int code = 33; code < 127; code++)
        {
            int cx = code % 16 * 8, cy = code / 16 * 8, width = 0;
            var mask = new bool[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    bool lit = false;
                    for (int sy = 0; sy < k && !lit; sy++)
                        for (int sx = 0; sx < k && !lit; sx++)
                            lit = ascii[(cx + x) * k + sx, (cy + y) * k + sy] >> 24 > 0x40;
                    if (lit) { mask[y * 8 + x] = true; width = Math.Max(width, x + 1); }
                }
            if (width > 0) table[code] = (width + 1, mask);
        }
        table[' '] = (4, new bool[64]);
        _bitmap = table;
    }

    public static bool UsingGameFont => _bitmap != null;

    private static (int Advance, bool[] Mask)? GameGlyph(char c) => _bitmap is { } t && c < 128 ? t[c] : null;

    private static int AdvanceOf(char c) => GameGlyph(c)?.Advance ?? Advance;

    private static string? Glyph(char c) =>
        Glyphs.TryGetValue(c, out string? g) || Glyphs.TryGetValue(char.ToUpperInvariant(c), out g) ? g : null;

    /// <summary>Text width in font units (multiply by the pixel scale).</summary>
    public static int Measure(string text)
    {
        if (text.Length == 0) return 0;
        int w = 0;
        foreach (char c in text) w += AdvanceOf(c);
        return w - 1;
    }

    public static bool HasGlyph(char c) => GameGlyph(c) != null || Glyph(c) != null;

    /// <summary>Draws text with its top-left corner at (x, y); each font unit becomes px × px pixels.</summary>
    public static void Draw(Canvas32 canvas, string text, int x, int y, int px, uint color, uint shadow = 0)
    {
        if (shadow >> 24 != 0) DrawRun(canvas, text, x + px, y + px, px, shadow);
        DrawRun(canvas, text, x, y, px, color);
    }

    private static void DrawRun(Canvas32 canvas, string text, int x, int y, int px, uint color)
    {
        foreach (char raw in text)
        {
            if (GameGlyph(raw) is { } gg)
            {
                for (int row = 0; row < 8; row++)
                    for (int col = 0; col < 8; col++)
                        if (gg.Mask[row * 8 + col]) canvas.SetRect(x + col * px, y + row * px, px, px, color);
                x += gg.Advance * px;
                continue;
            }
            if (Glyph(raw) is { } g)
            {
                int rows = (g.Length + 1) / (GlyphWidth + 1);
                for (int row = 0; row < rows; row++)
                    for (int col = 0; col < GlyphWidth; col++)
                        if (g[row * (GlyphWidth + 1) + col] == '1')
                            canvas.SetRect(x + col * px, y + row * px, px, px, color);
            }
            x += Advance * px;
        }
    }
}
