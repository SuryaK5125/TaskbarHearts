using System.Runtime.InteropServices;
using SurvivalHud.Core;
using SurvivalHud.Native;

namespace SurvivalHud.Shell;

/// <summary>
/// The application's own notification-area icon (an original crafting-table pixel drawing).
/// It is always present while the app runs, so the app can be reached from the
/// "Show hidden icons" (^) flyout even when the three HUD icons are switched off.
/// </summary>
public sealed class AppTrayIcon : IDisposable
{
    public const int CallbackMessage = Win32.WM_APP + 2;
    private const int Id = 10;
    private const int NIF_SHOWTIP = 0x80;
    private const string Tip = "Taskbar Hearts";

    private static readonly Dictionary<char, uint> Palette = new()
    {
        ['K'] = 0xFF2B1D0E, ['T'] = 0xFFB8945F, ['t'] = 0xFFD2AE78,
        ['B'] = 0xFF7A5A30, ['D'] = 0xFF5A3F20, ['G'] = 0xFFB4B4B4, ['g'] = 0xFF6A6A6A,
    };

    private static readonly string[] CraftingTable =
    {
        "KKKKKKKKKKKKKKKK",
        "KtttTtttTtttTttK",
        "KtTTTtTTTtTTTtTK",
        "KTTTTTTTTTTTTTTK",
        "KKKKKKKKKKKKKKKK",
        "KBBBBBBDDBBBBBBK",
        "KBGGBBBDDBBBgBBK",
        "KBGgBBBDDBBgGgBK",
        "KBBgBBBDDBBBgBBK",
        "KBBgBBBDDBBBgBBK",
        "KBBgBBBDDBBBgBBK",
        "KBBBBBBDDBBBBBBK",
        "KBBDDBBDDBBDDBBK",
        "KBBDDBBDDBBDDBBK",
        "KBBBBBBDDBBBBBBK",
        "KKKKKKKKKKKKKKKK",
    };

    /// <summary>The 16×16 crafting-table art (also the main window's icon).</summary>
    public static Sprite Art() => Sprite.FromRows(CraftingTable, Palette);

    private readonly IntPtr _hwnd;
    private IntPtr _icon;
    private int _iconSize;
    private bool _added;

    public AppTrayIcon(IntPtr messageWindow) => _hwnd = messageWindow;

    /// <summary>Adds the icon if it is not there yet (first start, or after Explorer restarted).</summary>
    public void Ensure(uint dpi)
    {
        int size = TrayIcons.IconSize(dpi);
        if (_added && size == _iconSize) return;

        if (_icon == IntPtr.Zero || size != _iconSize)
        {
            IntPtr old = _icon;
            var sprite = Sprite.FromRows(CraftingTable, Palette);
            int edge = Math.Max(1, size / sprite.Width) * sprite.Width;   // whole-number scale keeps the pixels crisp
            var canvas = new Canvas32(size, size);
            canvas.Blit(sprite, (size - edge) / 2, (size - edge) / 2, edge, edge);
            _icon = TrayIcons.CreateIcon(canvas);
            _iconSize = size;
            if (_added)
            {
                var mod = NewData();
                mod.uFlags = Win32.NIF_ICON;
                mod.hIcon = _icon;
                Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref mod);
            }
            if (old != IntPtr.Zero) Win32.DestroyIcon(old);
            if (_added) return;
        }

        var data = NewData();
        data.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = Tip;
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data); // clear a leftover from a crashed run
        _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);
        if (_added)
        {
            data.uVersion = Win32.NOTIFYICON_VERSION_4;
            Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
        }
    }

    /// <summary>Explorer restarted and forgot the icon; the next <see cref="Ensure"/> adds it again.</summary>
    public void Invalidate() => _added = false;

    public bool Added => _added;

    public RECT? GetRect()
    {
        if (!_added) return null;
        var id = new Win32.NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<Win32.NOTIFYICONIDENTIFIER>(), hWnd = _hwnd, uID = Id };
        return Win32.Shell_NotifyIconGetRect(ref id, out RECT r) == 0 && !r.IsEmpty ? r : null;
    }

    private Win32.NOTIFYICONDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATA>(), hWnd = _hwnd, uID = Id, szTip = "", szInfo = "", szInfoTitle = "",
    };

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData();
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) Win32.DestroyIcon(_icon);
        _icon = IntPtr.Zero;
    }
}
