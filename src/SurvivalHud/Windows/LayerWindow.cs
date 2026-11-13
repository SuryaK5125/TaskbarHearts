using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SurvivalHud.Core;
using SurvivalHud.Native;

namespace SurvivalHud.Windows;

/// <summary>
/// A borderless, transparent, always-on-top WPF window that never takes focus, has no
/// taskbar button and no Alt+Tab entry. It shows exactly one bitmap, whose pixel size always
/// equals the window's physical size, so nothing is ever rescaled or blurred.
/// </summary>
public class LayerWindow : Window
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.Fill,
        SnapsToDevicePixels = true,
        UseLayoutRounding = true,
    };

    private WriteableBitmap? _bitmap;
    private uint[]? _lastPixels;
    private int[] _scratch = Array.Empty<int>();
    private RECT _lastRect;
    private bool _shown;

    public IntPtr Handle { get; private set; }

    /// <summary>The window's current screen rectangle in physical pixels (empty while hidden).</summary>
    public RECT ScreenRect => _shown ? _lastRect : default;

    public bool Shown => _shown;

    protected LayerWindow(string title)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        Content = _image;

        Handle = new WindowInteropHelper(this).EnsureHandle();
        Win32.AddExStyle(Handle, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(Handle)!.AddHook(WndProc);
    }

    /// <summary>Shows the canvas at a physical screen position. Does nothing when neither pixels nor position changed.</summary>
    public void Present(Canvas32 canvas, int x, int y)
    {
        if (_bitmap == null || _bitmap.PixelWidth != canvas.Width || _bitmap.PixelHeight != canvas.Height)
        {
            _bitmap = new WriteableBitmap(canvas.Width, canvas.Height, 96, 96, PixelFormats.Pbgra32, null);
            _image.Source = _bitmap;
            _scratch = new int[canvas.Pixels.Length];
            _lastPixels = null;
        }

        if (_lastPixels == null || !canvas.Pixels.AsSpan().SequenceEqual(_lastPixels))
        {
            canvas.CopyPremultiplied(_scratch);
            _bitmap.WritePixels(new Int32Rect(0, 0, canvas.Width, canvas.Height), _scratch, canvas.Width * 4, 0);
            _lastPixels = (uint[])canvas.Pixels.Clone();
        }

        var rect = RECT.FromXYWH(x, y, canvas.Width, canvas.Height);
        bool moved = !rect.Equals(_lastRect);
        _lastRect = rect;
        if (moved) Place();
        if (!_shown)
        {
            _shown = true;
            Show();
            Place(); // WPF may nudge a window while showing it; pin it back to the exact pixels
        }
    }

    public void HideLayer()
    {
        if (!_shown) return;
        _shown = false;
        Hide();
    }

    /// <summary>Re-asserts the top-most position without activating (the taskbar is top-most too).</summary>
    public void BringToTop()
    {
        if (_shown)
            Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    private void Place() =>
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, _lastRect.Left, _lastRect.Top, _lastRect.Width, _lastRect.Height,
            Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);

    protected virtual IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_MOUSEACTIVATE && !AllowActivation)
        {
            handled = true;
            return new IntPtr(Win32.MA_NOACTIVATE);
        }
        if (msg == Win32.WM_DPICHANGED)
        {
            // Size and position are always set explicitly in physical pixels; ignore WPF's suggested rescale.
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected virtual bool AllowActivation => false;
}

/// <summary>The expanded HUD. Purely visual; hover is tracked by the shared controller, not by this window.</summary>
public sealed class HudWindow : LayerWindow
{
    public HudWindow() : base("Taskbar Hearts") { }
}

/// <summary>The compact three-icon row that sits on the taskbar.</summary>
public sealed class OverlayWindow : LayerWindow
{
    private bool _interactive, _dragging;
    private POINT _dragLast;

    public event Action? PointerMoved;
    public event Action? Clicked;
    public event Action? RightClicked;
    public event Action<int, int>? Dragged;       // dx, dy in physical pixels
    public event Action<int, bool>? KeyPressed;   // virtual key, shift held

    public OverlayWindow() : base("Taskbar Hearts icons") { }

    protected override bool AllowActivation => _interactive;

    /// <summary>Alignment mode: the window may take focus (for arrow keys) and can be dragged.</summary>
    public void SetInteractive(bool on)
    {
        if (_interactive == on) return;
        _interactive = on;
        _dragging = false;
        if (on)
        {
            Win32.AddExStyle(Handle, 0, Win32.WS_EX_NOACTIVATE);
            Win32.SetForegroundWindow(Handle);
        }
        else
        {
            Win32.ReleaseCapture();
            Win32.AddExStyle(Handle, Win32.WS_EX_NOACTIVATE);
        }
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEMOVE:
                if (_dragging && Win32.GetCursorPos(out POINT p))
                {
                    int dx = p.X - _dragLast.X, dy = p.Y - _dragLast.Y;
                    _dragLast = p;
                    if (dx != 0 || dy != 0) Dragged?.Invoke(dx, dy);
                }
                else PointerMoved?.Invoke();
                break;

            case Win32.WM_LBUTTONDOWN:
                if (_interactive && Win32.GetCursorPos(out _dragLast))
                {
                    _dragging = true;
                    Win32.SetCapture(hwnd);
                    handled = true;
                }
                break;

            case Win32.WM_LBUTTONUP:
                if (_dragging)
                {
                    _dragging = false;
                    Win32.ReleaseCapture();
                }
                else if (!_interactive) Clicked?.Invoke();
                handled = true;
                break;

            case Win32.WM_CAPTURECHANGED:
                _dragging = false;
                break;

            case Win32.WM_RBUTTONUP:
                RightClicked?.Invoke();
                handled = true;
                break;

            case Win32.WM_KEYDOWN:
                if (_interactive)
                {
                    KeyPressed?.Invoke(wParam.ToInt32(), Win32.GetKeyState(Win32.VK_SHIFT) < 0);
                    handled = true;
                }
                break;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }
}
