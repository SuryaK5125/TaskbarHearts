using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using SurvivalHud.Core;
using SurvivalHud.Native;
using SurvivalHud.Settings;

namespace SurvivalHud.Windows;

/// <summary>
/// The taskbar icons, drawn in a per-pixel-alpha layered window that is a <b>child of the
/// taskbar window</b>. While Start, Search or Quick Settings are open, Windows lifts the
/// taskbar into a system z-band that no ordinary top-most window can reach; as a child the
/// overlay simply travels with it and never falls behind.
///
/// The previous top-most <see cref="OverlayWindow"/> is still used for alignment mode (it can
/// take keyboard focus) and as a fallback whenever the child window cannot be created.
/// Nothing inside Explorer is modified: this is an ordinary window owned by this process.
/// </summary>
public sealed class TaskbarOverlay : IDisposable
{
    private const int WS_CHILD = 0x40000000, WS_CLIPSIBLINGS = 0x04000000;
    private const long WS_EX_LAYERED = 0x80000;

    private readonly OverlayWindow _popup = new();
    private NativeChildWindow? _child;
    private IntPtr _parent;
    private bool _interactive, _childShown, _disposed;
    private RECT _lastRect;
    private Canvas32? _lastCanvas;
    private bool _pixelsDirty;
    private DateTime _retryAfter;

    public event Action? PointerMoved;
    public event Action? Clicked;
    public event Action? RightClicked;
    public event Action<int, int>? Dragged;
    public event Action<int, bool>? KeyPressed;

    /// <summary>
    /// Screen area that stands in for Windows' Quick Settings button (network + volume + battery).
    /// A left-click there opens Quick Settings with its own shortcut (Win + A) instead of
    /// toggling the HUD pin. Right-clicks always open this app's menu.
    /// </summary>
    public RECT? QuickSettingsArea { get; set; }

    private bool InQuickSettings() => QuickSettingsArea is { } a && Win32.GetCursorPos(out POINT p) && a.Contains(p);

    private static void OpenQuickSettings()
    {
        const ushort VK_LWIN = 0x5B, VK_A = 0x41;
        const uint KEYUP = 0x2;
        INPUT Key(ushort vk, uint flags) => new() { type = 1, ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } };
        var inputs = new[] { Key(VK_LWIN, 0), Key(VK_A, 0), Key(VK_A, KEYUP), Key(VK_LWIN, KEYUP) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    public TaskbarOverlay()
    {
        _popup.PointerMoved += () => PointerMoved?.Invoke();
        _popup.Clicked += () => Clicked?.Invoke();
        _popup.RightClicked += () => RightClicked?.Invoke();
        _popup.Dragged += (dx, dy) => Dragged?.Invoke(dx, dy);
        _popup.KeyPressed += (k, s) => KeyPressed?.Invoke(k, s);
    }

    public Dispatcher Dispatcher => _popup.Dispatcher;

    private bool UsingChild => ChildAlive && !_interactive;
    private bool ChildAlive => _child is { IsDisposed: false } c && Win32.IsWindow(c.Handle) && Win32.IsWindow(_parent);

    public bool Shown => UsingChild ? _childShown : _popup.Shown;

    /// <summary>True when the icons are drawn as a child of the taskbar (false = top-most fallback window).</summary>
    public bool AttachedToTaskbar => UsingChild;
    public RECT ScreenRect => UsingChild ? (_childShown ? _lastRect : default) : _popup.ScreenRect;

    /// <summary>Makes the overlay a child of <paramref name="taskbar"/> (recreated after an Explorer restart or a display change).</summary>
    public void Attach(IntPtr taskbar)
    {
        if (_disposed) return;
        if (taskbar == _parent && ChildAlive) return;
        DestroyChild();
        _parent = taskbar;
        if (taskbar == IntPtr.Zero || !Win32.IsWindow(taskbar) || DateTime.UtcNow < _retryAfter) return;
        try
        {
            _child = new NativeChildWindow(taskbar, "Taskbar Hearts icons",
                WS_CHILD | WS_CLIPSIBLINGS, WS_EX_LAYERED | Win32.WS_EX_NOACTIVATE, ChildProc);
            _colorKey = false;
            _childShown = false;
            _pixelsDirty = true;
            if (!_interactive) _popup.HideLayer();
        }
        catch (Exception ex)
        {
            Log.Write("Could not attach the overlay to the taskbar, using a top-most window: " + ex.Message);
            _child = null;
            _retryAfter = DateTime.UtcNow.AddSeconds(5);
        }
    }

    public void Present(Canvas32 canvas, int x, int y)
    {
        if (!UsingChild)
        {
            _popup.Present(canvas, x, y);
            return;
        }

        var rect = RECT.FromXYWH(x, y, canvas.Width, canvas.Height);
        bool sizeChanged = rect.Width != _lastRect.Width || rect.Height != _lastRect.Height;
        bool moved = !rect.Equals(_lastRect);
        bool pixelsChanged = _pixelsDirty || _lastCanvas == null || sizeChanged
            || !canvas.Pixels.AsSpan().SequenceEqual(_lastCanvas.Pixels);
        _lastRect = rect;
        IntPtr h = _child!.Handle;

        if (moved || !_childShown)
        {
            var pt = new POINT { X = x, Y = y };
            ScreenToClient(_parent, ref pt);
            Win32.SetWindowPos(h, IntPtr.Zero /* HWND_TOP */, pt.X, pt.Y, rect.Width, rect.Height,
                Win32.SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        if (pixelsChanged)
        {
            if (!Upload(h, canvas)) { FallBack(canvas, x, y); return; }
            _lastCanvas = canvas;
            _pixelsDirty = false;
        }
        _childShown = true;
    }

    public void HideLayer()
    {
        _popup.HideLayer();
        if (ChildAlive && _childShown) Win32.SetWindowPos(_child!.Handle, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | SWP_HIDEWINDOW | SWP_NOZORDER);
        _childShown = false;
    }

    /// <summary>Keeps the overlay above the taskbar's own content (child) or above other top-most windows (fallback).</summary>
    public void BringToTop()
    {
        if (UsingChild)
        {
            if (_childShown) Win32.SetWindowPos(_child!.Handle, IntPtr.Zero, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }
        else _popup.BringToTop();
    }

    /// <summary>Alignment mode uses the focusable top-most window; the child returns afterwards.</summary>
    public void SetInteractive(bool on)
    {
        if (_interactive == on) return;
        _interactive = on;
        if (on)
        {
            if (ChildAlive && _childShown && _lastCanvas != null)
            {
                var (c, r) = (_lastCanvas, _lastRect);
                HideChildOnly();
                _popup.Present(c, r.Left, r.Top);
            }
            _popup.SetInteractive(true);
        }
        else
        {
            _popup.SetInteractive(false);
            if (ChildAlive)
            {
                _popup.HideLayer();
                _pixelsDirty = true;
                _childShown = false; // shown again by the next Present
            }
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyChild();
        _popup.Close();
    }

    // ---- internals ------------------------------------------------------------------------

    private void FallBack(Canvas32 canvas, int x, int y)
    {
        Log.Write("Layered child window could not be updated; using a top-most window instead.");
        DestroyChild();
        _retryAfter = DateTime.UtcNow.AddSeconds(5);
        _popup.Present(canvas, x, y);
    }

    private void HideChildOnly()
    {
        if (ChildAlive) Win32.SetWindowPos(_child!.Handle, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | SWP_HIDEWINDOW | SWP_NOZORDER);
        _childShown = false;
    }

    private void DestroyChild()
    {
        try { if (_child is { IsDisposed: false }) _child.Dispose(); }
        catch (Exception ex) { Log.Write("Overlay child cleanup: " + ex.Message); }
        _child = null;
        _childShown = false;
        _lastCanvas = null;
        _lastRect = default;
    }

    // Colour-key transparency: a cross-process child window cannot use per-pixel alpha
    // (UpdateLayeredWindow is rejected), so one unused colour is made see-through instead.
    // Minecraft HUD sprites are hard-edged pixel art, so nothing is lost.
    private const uint KeyColorRef = 0x00FE00FF;   // COLORREF (0x00BBGGRR) for RGB(255, 0, 254)
    private const int KeyPixel = 0x00FF00FE;       // the same colour as a 0x00RRGGBB DIB pixel
    private int[] _flat = Array.Empty<int>();
    private int _flatW, _flatH;

    private bool _colorKey;

    private bool Upload(IntPtr hwnd, Canvas32 canvas)
    {
        if (!_colorKey)
        {
            if (UploadAlpha(hwnd, canvas)) return true;
            // Per-pixel alpha refused: fall back to colour-key transparency on the same window.
            Log.Write($"Per-pixel alpha unavailable on the taskbar child ({Marshal.GetLastWin32Error()}); using colour-key transparency.");
            _colorKey = true;
            if (!SetLayeredWindowAttributes(hwnd, KeyColorRef, 0, 1 /* LWA_COLORKEY */)) return false;
        }
        if (_flat.Length != canvas.Pixels.Length) _flat = new int[canvas.Pixels.Length];
        _flatW = canvas.Width;
        _flatH = canvas.Height;
        for (int i = 0; i < _flat.Length; i++)
        {
            uint p = canvas.Pixels[i];
            int rgb = (int)(p & 0xFFFFFF);
            _flat[i] = p >> 24 < 128 || rgb == KeyPixel ? KeyPixel : rgb;
        }
        InvalidateRect(hwnd, IntPtr.Zero, false);
        UpdateWindow(hwnd);
        return true;
    }

    private static bool UploadAlpha(IntPtr hwnd, Canvas32 canvas)
    {
        IntPtr screen = Win32.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return false;
        IntPtr mem = Win32.CreateCompatibleDC(screen), dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            var bmi = new Win32.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(), biWidth = canvas.Width, biHeight = -canvas.Height, biPlanes = 1, biBitCount = 32,
            };
            dib = Win32.CreateDIBSection(screen, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return false;
            var px = new int[canvas.Pixels.Length];
            canvas.CopyPremultiplied(px);
            Marshal.Copy(px, 0, bits, px.Length);
            old = Win32.SelectObject(mem, dib);
            var size = new SIZE { cx = canvas.Width, cy = canvas.Height };
            var src = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            return UpdateLayeredWindow(hwnd, screen, IntPtr.Zero, ref size, mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            if (old != IntPtr.Zero) Win32.SelectObject(mem, old);
            if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
            Win32.DeleteDC(mem);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, IntPtr pptDst, ref SIZE size, IntPtr src, ref POINT pptSrc,
        uint key, ref BLENDFUNCTION blend, uint flags);

    private void Paint(IntPtr hwnd)
    {
        var ps = new byte[128];
        IntPtr dc = BeginPaint(hwnd, ps);
        try
        {
            if (dc == IntPtr.Zero || _flatW == 0) return;
            var bmi = new Win32.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(), biWidth = _flatW, biHeight = -_flatH, biPlanes = 1, biBitCount = 32,
            };
            SetDIBitsToDevice(dc, 0, 0, (uint)_flatW, (uint)_flatH, 0, 0, 0, (uint)_flatH, _flat, ref bmi, 0);
        }
        finally { EndPaint(hwnd, ps); }
    }

    private IntPtr ChildProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(Win32.MA_NOACTIVATE);
            case Win32.WM_MOUSEMOVE:
                PointerMoved?.Invoke();
                break;
            case Win32.WM_LBUTTONUP:
                if (InQuickSettings()) OpenQuickSettings();
                else Clicked?.Invoke();
                handled = true;
                break;
            case Win32.WM_RBUTTONUP:
                RightClicked?.Invoke();
                handled = true;
                break;
            case Win32.WM_DPICHANGED:
                handled = true;
                break;
            case WM_PAINT:
                Paint(hwnd);
                handled = true;
                break;
            case WM_ERASEBKGND:
                handled = true;
                return new IntPtr(1);
            case WM_NCDESTROY:
                // The taskbar was destroyed (Explorer restart) and took this window with it.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { if (_child?.Handle == hwnd) { _child = null; _childShown = false; } });
                break;
        }
        return IntPtr.Zero;
    }

    private const int WM_NCDESTROY = 0x0082, WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014;
    private const uint SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80, SWP_NOZORDER = 0x4;

    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, byte[] ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, byte[] ps);
    [DllImport("gdi32.dll")] private static extern int SetDIBitsToDevice(IntPtr dc, int x, int y, uint w, uint h, int sx, int sy,
        uint startScan, uint lines, int[] bits, ref Win32.BITMAPINFOHEADER bmi, uint usage);
}

/// <summary>
/// A bare Win32 window with its own window procedure. Used instead of WPF's HwndSource for the
/// taskbar child because HwndSource strips WS_EX_LAYERED, which a taskbar child needs to be visible.
/// </summary>
internal sealed class NativeChildWindow : IDisposable
{
    public delegate IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);
    private delegate IntPtr WndProcFn(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const string ClassName = "SurvivalHudTaskbarChild";
    private static WndProcFn? _classProc;          // kept alive for the lifetime of the process
    private static readonly Dictionary<IntPtr, NativeChildWindow> Live = new();

    private readonly Hook _hook;
    public IntPtr Handle { get; private set; }
    public bool IsDisposed => Handle == IntPtr.Zero;

    public NativeChildWindow(IntPtr parent, string title, int style, long exStyle, Hook hook)
    {
        _hook = hook;
        IntPtr module = GetModuleHandle(null);
        if (_classProc == null)
        {
            _classProc = StaticProc;
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_classProc),
                hInstance = module, lpszClassName = ClassName,
            };
            if (RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410 /* already registered */)
                throw new System.ComponentModel.Win32Exception();
        }
        Handle = CreateWindowEx((uint)exStyle, ClassName, title, (uint)style, 0, 0, 1, 1, parent, IntPtr.Zero, module, IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        Live[Handle] = this;
    }

    private static IntPtr StaticProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (Live.TryGetValue(hwnd, out var w))
        {
            bool handled = false;
            IntPtr r = w._hook(hwnd, (int)msg, wParam, lParam, ref handled);
            if (msg == 0x0082 /* WM_NCDESTROY */) { Live.Remove(hwnd); w.Handle = IntPtr.Zero; }
            if (handled) return r;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero && IsWindow(Handle)) DestroyWindow(Handle);
        Live.Remove(Handle);
        Handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
