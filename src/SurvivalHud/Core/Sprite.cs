namespace SurvivalHud.Core;

/// <summary>A small immutable pixel-art image. Pixels are straight (non-premultiplied) 0xAARRGGBB.</summary>
public sealed class Sprite
{
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }

    public Sprite(int width, int height, uint[] pixels)
    {
        if (width <= 0 || height <= 0 || pixels.Length != width * height)
            throw new ArgumentException("Invalid sprite dimensions.");
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public uint this[int x, int y] => Pixels[y * Width + x];

    /// <summary>Builds a sprite from a character matrix; characters missing from the palette are transparent.</summary>
    public static Sprite FromRows(string[] rows, IReadOnlyDictionary<char, uint> palette)
    {
        int h = rows.Length, w = rows[0].Length;
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
        {
            if (rows[y].Length != w) throw new ArgumentException($"Row {y} has the wrong width.");
            for (int x = 0; x < w; x++)
                px[y * w + x] = palette.TryGetValue(rows[y][x], out uint c) ? c : 0u;
        }
        return new Sprite(w, h, px);
    }

    public Sprite Crop(int x, int y, int w, int h)
    {
        var px = new uint[w * h];
        for (int j = 0; j < h; j++)
            Array.Copy(Pixels, (y + j) * Width + x, px, j * w, w);
        return new Sprite(w, h, px);
    }

    public Sprite Map(Func<uint, uint> f)
    {
        var px = new uint[Pixels.Length];
        for (int i = 0; i < px.Length; i++) px[i] = f(Pixels[i]);
        return new Sprite(Width, Height, px);
    }

    public bool IsEmpty => Array.TrueForAll(Pixels, p => p >> 24 == 0);

    /// <summary>First and last column that contain any visible pixel.</summary>
    public (int Min, int Max) OpaqueColumns()
    {
        int min = Width, max = -1;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (this[x, y] >> 24 != 0)
                {
                    if (x < min) min = x;
                    if (x > max) max = x;
                }
        return max < 0 ? (0, Width - 1) : (min, max);
    }
}
