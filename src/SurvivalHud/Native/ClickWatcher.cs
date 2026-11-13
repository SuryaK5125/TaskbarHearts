using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace SurvivalHud.Native;

/// <summary>
/// Reports mouse-button presses anywhere on screen, so the HUD popup can close when the user
/// clicks outside it. It is only installed while the popup is open, never blocks or changes a
/// click (every event is passed straight on), and does its own work after the event has gone.
/// </summary>
public sealed class ClickWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207;

    private readonly Action<POINT> _onClick;
    private readonly HookProc _proc;
    private IntPtr _hook;

    public ClickWatcher(Action<POINT> onClick)
    {
        _onClick = onClick;
        _proc = Proc;   // kept in a field so the delegate outlives the native hook
    }

    public bool Active => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_hook == IntPtr.Zero) _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
        {
            var pt = Marshal.PtrToStructure<POINT>(lParam);   // MSLLHOOKSTRUCT starts with the point
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, () => _onClick(pt));
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose() => Stop();

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
