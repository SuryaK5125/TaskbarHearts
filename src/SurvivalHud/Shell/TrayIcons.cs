using System.Runtime.InteropServices;
using SurvivalHud.Core;
using SurvivalHud.Native;

namespace SurvivalHud.Shell;

/// <summary>
/// Fallback mode: three genuine notification-area icons created with Shell_NotifyIcon.
/// Windows (and the user) decide where these sit; the app only supplies the pictures.
/// </summary>
public sealed class TrayIcons : IDisposable
{
    public const int CallbackMessage = Win32.WM_APP + 1;
    private const int Count = 3;

    private readonly IntPtr _hwnd;
    private readonly IntPtr[] _icons = new IntPtr[Count];
    private readonly uint[]?[] _lastPixels = new uint[]?[Count];
    private readonly string[] _lastTips = new string[Count];
    private bool _added;

    public TrayIcons(IntPtr messageWindow) => _hwnd = messageWindow;

    public bool Active => _added;

    /// <summary>Icon edge length Windows wants for the notification area at this DPI.</summary>
    public static int IconSize(uint dpi) => Math.Max(16, Win32.GetSystemMetricsForDpi(Win32.SM_CXSMICON, dpi));

    /// <summary>
    /// Adds the icons (if needed) and refreshes any whose picture or tooltip changed.
    /// Index 0 = heart, 1 = hunger, 2 = armor. They are added in reverse order because the
    /// notification area usually places newer icons further left.
    /// </summary>
    public void Update(Canvas32[] images, string[] tips)
    {
        for (int n = 0; n < Count; n++)
        {
            int i = _added ? n : Count - 1 - n;
            bool pictureChanged = _lastPixels[i] == null || !images[i].Pixels.AsSpan().SequenceEqual(_lastPixels[i]);
            if (_added && !pictureChanged && tips[i] == _lastTips[i]) continue;

            IntPtr old = _icons[i];
            if (pictureChanged || old == IntPtr.Zero)
            {
                _icons[i] = CreateIcon(images[i]);
                _lastPixels[i] = (uint[])images[i].Pixels.Clone();
            }
            _lastTips[i] = tips[i];

            var data = NewData(i);
            data.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP;
            data.uCallbackMessage = CallbackMessage;
            data.hIcon = _icons[i];
            data.szTip = tips[i];
            if (_added)
            {
                Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
            }
            else
            {
                Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data); // clear any leftover from a crash
                Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);
                data.uVersion = Win32.NOTIFYICON_VERSION_4;
                Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
            }
            if (old != IntPtr.Zero && old != _icons[i]) Win32.DestroyIcon(old);
        }
        _added = true;
    }

    /// <summary>Called after Explorer restarts: the old icons are gone, so add them again on the next update.</summary>
    public void Invalidate() => _added = false;

    public void Remove()
    {
        for (int i = 0; i < Count; i++)
        {
            if (_added)
            {
                var data = NewData(i);
                Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
            }
            if (_icons[i] != IntPtr.Zero) Win32.DestroyIcon(_icons[i]);
            _icons[i] = IntPtr.Zero;
            _lastPixels[i] = null;
        }
        _added = false;
    }

    /// <summary>Screen rectangle of one icon, when it is currently visible in the notification area.</summary>
    public RECT? GetRect(int index)
    {
        if (!_added) return null;
        var id = new Win32.NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<Win32.NOTIFYICONIDENTIFIER>(), hWnd = _hwnd, uID = index + 1 };
        return Win32.Shell_NotifyIconGetRect(ref id, out RECT r) == 0 && !r.IsEmpty ? r : null;
    }

    private Win32.NOTIFYICONDATA NewData(int index) => new()
    {
        cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATA>(), hWnd = _hwnd, uID = index + 1, szTip = "", szInfo = "", szInfoTitle = "",
    };

    /// <summary>Builds a 32-bit alpha icon straight from canvas pixels.</summary>
    internal static IntPtr CreateIcon(Canvas32 c)
    {
        var bmi = new Win32.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(), biWidth = c.Width, biHeight = -c.Height, biPlanes = 1, biBitCount = 32,
        };
        IntPtr color = Win32.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero) return IntPtr.Zero;
        IntPtr mask = Win32.CreateBitmap(c.Width, c.Height, 1, 1, IntPtr.Zero);
        try
        {
            var px = new int[c.Pixels.Length];
            for (int i = 0; i < px.Length; i++) px[i] = unchecked((int)c.Pixels[i]);
            Marshal.Copy(px, 0, bits, px.Length);
            var info = new Win32.ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
            return Win32.CreateIconIndirect(ref info);
        }
        finally
        {
            Win32.DeleteObject(color);
            if (mask != IntPtr.Zero) Win32.DeleteObject(mask);
        }
    }

    public void Dispose() => Remove();
}

/// <summary>A native popup menu: light, correctly themed and it closes properly from a no-activate window.</summary>
public static class NativeMenu
{
    public readonly record struct Item(int Id, string Text, bool Checked = false, bool Enabled = true, Item[]? Sub = null)
    {
        public static readonly Item Separator = new(0, "");
        public static Item Submenu(string text, params Item[] items) => new(-1, text, Sub: items);
    }

    private static void Append(IntPtr menu, IReadOnlyList<Item> items)
    {
        foreach (var it in items)
        {
            if (it.Sub != null)
            {
                IntPtr sub = Win32.CreatePopupMenu();
                if (sub == IntPtr.Zero) continue;
                Append(sub, it.Sub);
                // The parent menu owns (and destroys) the submenu.
                Win32.AppendMenu(menu, Win32.MF_POPUP | (it.Enabled ? 0 : Win32.MF_GRAYED), (UIntPtr)(ulong)sub.ToInt64(), it.Text);
                continue;
            }
            if (it.Id == 0) { Win32.AppendMenu(menu, Win32.MF_SEPARATOR, UIntPtr.Zero, null); continue; }
            uint flags = Win32.MF_STRING | (it.Checked ? Win32.MF_CHECKED : 0) | (it.Enabled ? 0 : Win32.MF_GRAYED);
            Win32.AppendMenu(menu, flags, (UIntPtr)(uint)it.Id, it.Text);
        }
    }

    /// <summary>Shows the menu at the cursor and returns the chosen id (0 if dismissed).</summary>
    public static int Show(IntPtr owner, IReadOnlyList<Item> items)
    {
        IntPtr menu = Win32.CreatePopupMenu();
        if (menu == IntPtr.Zero) return 0;
        try
        {
            Append(menu, items);
            Win32.GetCursorPos(out POINT pt);
            // Required so the menu dismisses when the user clicks elsewhere.
            Win32.SetForegroundWindow(owner);
            int cmd = Win32.TrackPopupMenuEx(menu,
                Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON | Win32.TPM_BOTTOMALIGN | Win32.TPM_NONOTIFY,
                pt.X, pt.Y, owner, IntPtr.Zero);
            Win32.PostMessage(owner, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return cmd;
        }
        finally { Win32.DestroyMenu(menu); }
    }
}
