namespace SurvivalHud.Core;

/// <summary>Where the hover popup opens, kept inside the monitor. All values are screen pixels.</summary>
public static class PopupPlacement
{
    /// <summary>
    /// Centred on the icons, on the side of the taskbar that faces the screen: above a bottom
    /// taskbar, below a taskbar at the top (possible with taskbar tools). If it doesn't fit on
    /// that side it goes on the other one, and it is always kept within the monitor.
    /// </summary>
    /// <param name="anchorLeft">The icons the popup belongs to (left, top, right, bottom).</param>
    /// <param name="barTop">The taskbar's top and bottom edges, or null when it's hidden or the icons aren't on it.</param>
    public static (int X, int Y) Place(int anchorLeft, int anchorTop, int anchorRight, int anchorBottom,
        int? barTop, int? barBottom, int monLeft, int monTop, int monRight, int monBottom, int width, int height, int gap)
    {
        int top = barTop is int bt ? Math.Min(anchorTop, bt) : Math.Min(anchorTop, monBottom);
        int bottom = barBottom is int bb ? Math.Max(anchorBottom, bb) : anchorBottom;
        bool barAtTop = barTop is int t && barBottom is int b && (t + b) / 2 < (monTop + monBottom) / 2;

        int above = top - gap - height, below = bottom + gap;
        int y = barAtTop
            ? (below + height <= monBottom ? below : above)
            : (above >= monTop ? above : below);
        y = Math.Clamp(y, monTop, Math.Max(monTop, monBottom - height));

        int x = (anchorLeft + anchorRight) / 2 - width / 2;
        x = Math.Clamp(x, monLeft + gap, Math.Max(monLeft + gap, monRight - gap - width));
        return (x, y);
    }
}
