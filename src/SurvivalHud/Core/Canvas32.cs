namespace SurvivalHud.Core;

/// <summary>
/// A plain software drawing surface in physical pixels (straight 0xAARRGGBB).
/// Everything the app shows is composed here with integer maths so the result is
/// pixel-exact; WPF only ever displays the finished bitmap 1:1.
/// </summary>
public sealed class Canvas32
{
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }

    public Canvas32(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Pixels = new uint[Width * Height];
    }

    public void Clear(uint argb) => Array.Fill(Pixels, argb);

    /// <summary>Overwrites (no blending) a rectangle.</summary>
    public void SetRect(int x, int y, int w, int h, uint argb)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + w), y1 = Math.Min(Height, y + h);
        for (int j = y0; j < y1; j++)
            for (int i = x0; i < x1; i++)
                Pixels[j * Width + i] = argb;
    }

    /// <summary>Alpha-blends a rectangle over the existing content.</summary>
    public void FillRect(int x, int y, int w, int h, uint argb)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + w), y1 = Math.Min(Height, y + h);
        for (int j = y0; j < y1; j++)
            for (int i = x0; i < x1; i++)
                Pixels[j * Width + i] = Over(Pixels[j * Width + i], argb);
    }

    /// <summary>
    /// Nearest-neighbour blit of a sprite into the destination rectangle, limited to the
    /// source columns [srcColFrom, srcColTo). Used for partial fills: clipping happens on
    /// whole source pixels so the art is never smeared.
    /// </summary>
    public void Blit(Sprite s, int dx, int dy, int dw, int dh, int srcColFrom = 0, int srcColTo = int.MaxValue)
    {
        if (dw <= 0 || dh <= 0) return;
        srcColTo = Math.Min(srcColTo, s.Width);
        for (int j = 0; j < dh; j++)
        {
            int ty = dy + j;
            if (ty < 0 || ty >= Height) continue;
            int sy = j * s.Height / dh;
            for (int i = 0; i < dw; i++)
            {
                int tx = dx + i;
                if (tx < 0 || tx >= Width) continue;
                int sx = i * s.Width / dw;
                if (sx < srcColFrom || sx >= srcColTo) continue;
                uint c = s.Pixels[sy * s.Width + sx];
                if (c >> 24 == 0) continue;
                Pixels[ty * Width + tx] = Over(Pixels[ty * Width + tx], c);
            }
        }
    }

    /// <summary>Source-over compositing of straight-alpha colours.</summary>
    public static uint Over(uint dst, uint src)
    {
        uint sa = src >> 24;
        if (sa == 255) return src;
        if (sa == 0) return dst;
        uint da = dst >> 24;
        uint outA = sa + da * (255 - sa) / 255;
        if (outA == 0) return 0;
        uint Ch(int shift)
        {
            uint sc = (src >> shift) & 0xFF, dc = (dst >> shift) & 0xFF;
            return (sc * sa * 255 + dc * da * (255 - sa)) / (outA * 255);
        }
        return (outA << 24) | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }

    /// <summary>Copies the canvas as premultiplied BGRA (what layered windows expect).</summary>
    public void CopyPremultiplied(int[] target)
    {
        for (int i = 0; i < Pixels.Length; i++)
        {
            uint p = Pixels[i], a = p >> 24;
            if (a == 255) { target[i] = unchecked((int)p); continue; }
            if (a == 0) { target[i] = 0; continue; }
            uint r = ((p >> 16) & 0xFF) * a / 255, g = ((p >> 8) & 0xFF) * a / 255, b = (p & 0xFF) * a / 255;
            target[i] = unchecked((int)((a << 24) | (r << 16) | (g << 8) | b));
        }
    }
}
