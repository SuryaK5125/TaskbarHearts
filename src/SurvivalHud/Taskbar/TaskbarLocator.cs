using System.Runtime.InteropServices;
using System.Windows.Automation;
using SurvivalHud.Native;
using SurvivalHud.Settings;

namespace SurvivalHud.Taskbar;

/// <summary>Geometry of the taskbar the overlay is anchored to, in physical pixels.</summary>
public sealed record TaskbarInfo(IntPtr Hwnd, RECT Rect, RECT Monitor, RECT WorkArea, uint Dpi, bool IsPrimary, bool Hidden)
{
    public double Scale => Dpi / 96.0;
}

public sealed record DisplayInfo(string DeviceName, RECT Bounds, bool IsPrimary);

/// <summary>Finds taskbar windows by class name and reads their live geometry. Nothing is hard-coded or cached.</summary>
public static class TaskbarLocator
{
    private const string PrimaryClass = "Shell_TrayWnd", SecondaryClass = "Shell_SecondaryTrayWnd";

    public static List<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (mon, _, _, _) =>
        {
            if (Win32.TryGetMonitorInfo(mon, out var mi))
                list.Add(new DisplayInfo(mi.szDevice, mi.rcMonitor, (mi.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Returns the taskbar on the requested display, or the primary taskbar when that display has none.</summary>
    public static TaskbarInfo? Find(string targetDisplay)
    {
        IntPtr primary = Win32.FindWindow(PrimaryClass, null);
        if (!string.IsNullOrEmpty(targetDisplay))
        {
            if (Describe(primary, true) is { } p && DeviceOf(p.Hwnd) == targetDisplay) return p;
            IntPtr h = IntPtr.Zero;
            while ((h = Win32.FindWindowEx(IntPtr.Zero, h, SecondaryClass, null)) != IntPtr.Zero)
                if (DeviceOf(h) == targetDisplay && Describe(h, false) is { } s) return s;
        }
        return Describe(primary, true);
    }

    private static string DeviceOf(IntPtr hwnd) =>
        Win32.TryGetMonitorInfo(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), out var mi) ? mi.szDevice : "";

    private static TaskbarInfo? Describe(IntPtr hwnd, bool primary)
    {
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd) || !Win32.GetWindowRect(hwnd, out RECT r) || r.IsEmpty) return null;
        if (!Win32.TryGetMonitorInfo(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), out var mi)) return null;

        // An auto-hidden taskbar slides almost entirely off its monitor; treat it as hidden
        // when less than half of it is on screen (or the window itself is not visible).
        var m = mi.rcMonitor;
        int visW = Math.Min(r.Right, m.Right) - Math.Max(r.Left, m.Left);
        int visH = Math.Min(r.Bottom, m.Bottom) - Math.Max(r.Top, m.Top);
        bool hidden = !Win32.IsWindowVisible(hwnd) || visW < r.Width / 2 || visH < r.Height / 2;

        uint dpi = Win32.GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        return new TaskbarInfo(hwnd, r, m, mi.rcWork, dpi, primary, hidden);
    }

    /// <summary>
    /// True when the foreground window covers the whole monitor (a game, a video, a
    /// presentation) and is not part of the desktop shell.
    /// </summary>
    public static bool IsFullscreenAppActive(RECT monitor)
    {
        IntPtr fg = Win32.GetForegroundWindow();
        if (fg == IntPtr.Zero || !Win32.GetWindowRect(fg, out RECT r) || !r.Contains(monitor)) return false;
        string cls = Win32.ClassNameOf(fg);
        return cls is not ("Progman" or "WorkerW" or PrimaryClass or SecondaryClass
            or "XamlExplorerHostIslandWindow" or "Windows.UI.Core.CoreWindow" or "");
    }
}

/// <summary>
/// Locates the native battery button inside the taskbar through UI Automation, the
/// documented accessibility API. This only reads its on-screen rectangle; the taskbar is not
/// modified. The lookup can take a while, so it is always run off the UI thread.
/// </summary>
public static class BatterySlotFinder
{
    private static AutomationElement? _cached;
    private static IntPtr _cachedFor;

    public static RECT? Find(IntPtr taskbar)
    {
        try
        {
            if (_cached != null && _cachedFor == taskbar && ToRect(_cached) is { } r) return r;

            _cached = null;
            _cachedFor = taskbar;
            var root = AutomationElement.FromHandle(taskbar);
            var found = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"));

            var items = new List<(AutomationElement El, RECT Rect, string Class, string Name)>();
            foreach (AutomationElement e in found)
            {
                if (ToRect(e) is not { } rect) continue;
                var cur = e.Current;
                items.Add((e, rect, cur.ClassName ?? "", cur.Name ?? ""));
            }
            items.Sort((a, b) => a.Rect.Left.CompareTo(b.Rect.Left));

            // The battery sits directly after the volume button and before the clock. Class
            // names are locale-independent; the English name is only a secondary hint.
            int volume = items.FindIndex(i => i.Class == "SystemTray.OmniButtonCenter");
            int clock = items.FindLastIndex(i => i.Class == "SystemTray.OmniButton");
            int pick = -1;
            if (volume >= 0 && volume + 1 < items.Count && (clock < 0 || volume + 1 < clock)) pick = volume + 1;
            if (pick < 0) pick = items.FindIndex(i => i.Name.Contains("Battery", StringComparison.OrdinalIgnoreCase));
            if (pick < 0) return null;

            _cached = items[pick].El;
            return items[pick].Rect;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or TimeoutException)
        {
            _cached = null;
            return null;
        }
        catch (Exception ex)
        {
            _cached = null;
            Log.Write("Battery slot lookup failed: " + ex.Message);
            return null;
        }
    }

    private static RECT? ToRect(AutomationElement e)
    {
        var b = e.Current.BoundingRectangle;
        if (b.IsEmpty || double.IsInfinity(b.Width) || b.Width < 4 || b.Height < 4) return null;
        return RECT.FromXYWH((int)Math.Round(b.X), (int)Math.Round(b.Y), (int)Math.Round(b.Width), (int)Math.Round(b.Height));
    }
}

/// <summary>
/// Finds the empty stretch of taskbar between the app buttons and the notification area,
/// used by the wider Survival Bars layout so it never covers a functional control.
/// Read-only UI Automation, run off the UI thread.
/// </summary>
/// <summary>
/// Taskbar layout facts used by the Survival Bars layouts (screen X coordinates, or relative to
/// the taskbar's left edge once stored). <see cref="ItemLefts"/> are the left edges of the
/// notification-area buttons, sorted.
/// </summary>
public sealed record TrayLayout(int TrayLeft, int? ButtonsRight, int? QuickSettingsLeft, int? ClockLeft, int[] ItemLefts)
{
    public TrayLayout Offset(int dx) => new(TrayLeft + dx, ButtonsRight + dx, QuickSettingsLeft + dx, ClockLeft + dx,
        Array.ConvertAll(ItemLefts, l => l + dx));

    public bool Equals(TrayLayout? o) => o is not null && TrayLeft == o.TrayLeft && ButtonsRight == o.ButtonsRight
        && QuickSettingsLeft == o.QuickSettingsLeft && ClockLeft == o.ClockLeft && ItemLefts.AsSpan().SequenceEqual(o.ItemLefts);

    public override int GetHashCode() => HashCode.Combine(TrayLeft, ButtonsRight, QuickSettingsLeft, ClockLeft, ItemLefts.Length);
}

public static class TaskbarFreeSpaceFinder
{
    /// <summary>Screen X of the notification area's left edge, and of the right edge of the rightmost
    /// left-side control (app buttons, Start, Search, widgets...), or null when unknown.</summary>
    public static TrayLayout? Find(IntPtr taskbar)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbar);
            var all = root.FindAll(TreeScope.Descendants, new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)));
            if (!Win32.GetWindowRect(taskbar, out RECT tb)) return null;

            int trayLeft = int.MaxValue, buttonsRight = int.MinValue;
            var tray = new List<(int L, int R, string Cls)>();
            foreach (AutomationElement e in all)
            {
                var cur = e.Current;
                var b = cur.BoundingRectangle;
                if (b.IsEmpty || double.IsInfinity(b.Width) || b.Width < 4 || b.Height < 4) continue;
                int l = (int)Math.Round(b.X), r = (int)Math.Round(b.X + b.Width);
                string cls = cur.ClassName ?? "";
                if (cls.StartsWith("SystemTray.", StringComparison.Ordinal))
                {
                    if (cls != "SystemTray.ShowDesktopButton") trayLeft = Math.Min(trayLeft, l);
                    tray.Add((l, r, cls));
                }
                else if (r <= tb.Right && l >= tb.Left)
                    buttonsRight = Math.Max(buttonsRight, r);
            }
            if (trayLeft == int.MaxValue) return null;

            // Quick Settings group (network + volume + battery): volume is the "OmniButtonCenter";
            // the network button directly before it belongs to the same group.
            tray.Sort((a, b) => a.L.CompareTo(b.L));
            int? qsLeft = null;
            int vol = tray.FindIndex(t => t.Cls == "SystemTray.OmniButtonCenter");
            if (vol >= 0)
            {
                qsLeft = tray[vol].L;
                if (vol > 0 && tray[vol - 1].Cls == "SystemTray.AccentButton" && tray[vol - 1].R >= tray[vol].L - 8) qsLeft = tray[vol - 1].L;
            }
            int clock = tray.FindLastIndex(t => t.Cls == "SystemTray.OmniButton");
            int? clockLeft = clock >= 0 ? tray[clock].L : null;
            var lefts = tray.Where(t => t.Cls != "SystemTray.OmniButton").Select(t => t.L).Distinct().ToArray();
            return new TrayLayout(trayLeft, buttonsRight == int.MinValue || buttonsRight > trayLeft ? null : buttonsRight,
                qsLeft, clockLeft, lefts);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or TimeoutException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Write("Taskbar free-space lookup failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>Cheap fallback: the left edge of the classic TrayNotifyWnd child window.</summary>
    public static int? TrayNotifyLeft(IntPtr taskbar)
    {
        IntPtr h = Win32.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        return h != IntPtr.Zero && Win32.GetWindowRect(h, out RECT r) && !r.IsEmpty ? r.Left : null;
    }
}

/// <summary>
/// Reads the taskbar's real on-screen colours beside the overlay so the overlay's opaque
/// background blends in (dark or light theme, accent colour, translucency).
/// </summary>
public static class ScreenSampler
{
    /// <summary>
    /// Returns one background colour per row of <paramref name="area"/>: the median of the
    /// pixels immediately left and right of it (within <paramref name="bounds"/>). Using the
    /// median ignores the thin glyphs of neighbouring icons and the clock.
    /// </summary>
    /// <summary>
    /// How uneven the taskbar behind the last sample was: the average spread of the middle half
    /// of pixel brightness per row (0 = a solid colour). Icon glyphs beside the area are a small
    /// minority and fall outside the middle half; wallpaper showing through a transparent taskbar
    /// (TranslucentTB and similar) does not.
    /// </summary>
    public static int LastSpread { get; private set; }

    public static uint[]? SampleRows(RECT area, RECT bounds, int margin)
    {
        int x0 = Math.Max(bounds.Left, area.Left - margin), x1 = Math.Min(bounds.Right, area.Right + margin);
        int w = x1 - x0, h = area.Height;
        if (w <= area.Width || h <= 0) return null;

        IntPtr screen = Win32.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        IntPtr mem = Win32.CreateCompatibleDC(screen), dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            var bmi = new Win32.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
            };
            dib = Win32.CreateDIBSection(screen, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;
            old = Win32.SelectObject(mem, dib);
            if (!Win32.BitBlt(mem, 0, 0, w, h, screen, x0, area.Top, Win32.SRCCOPY)) return null;

            var px = new int[w * h];
            Marshal.Copy(bits, px, 0, px.Length);

            var rows = new uint[h];
            var line = new List<uint>(w);
            long spread = 0;
            for (int y = 0; y < h; y++)
            {
                line.Clear();
                for (int x = 0; x < w; x++)
                {
                    int sx = x0 + x;
                    if (sx >= area.Left && sx < area.Right) continue;
                    line.Add((uint)px[y * w + x] | 0xFF000000);
                }
                if (line.Count == 0) return null;
                line.Sort((a, b) => Luma(a).CompareTo(Luma(b)));
                rows[y] = line[line.Count / 2];
                spread += Luma(line[line.Count * 3 / 4]) - Luma(line[line.Count / 4]);
            }
            LastSpread = (int)(spread / Math.Max(1, h));
            return rows;
        }
        finally
        {
            if (old != IntPtr.Zero) Win32.SelectObject(mem, old);
            if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
            Win32.DeleteDC(mem);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static int Luma(uint c) => (int)(((c >> 16) & 0xFF) * 3 + ((c >> 8) & 0xFF) * 6 + (c & 0xFF));
}
