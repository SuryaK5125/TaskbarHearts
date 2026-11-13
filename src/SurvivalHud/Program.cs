using System.Windows;
using SurvivalHud.Settings;

namespace SurvivalHud;

public static class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    public static int Main(string[] args)
    {
        const string exitEventName = @"Local\MinecraftSurvivalHud.Exit";
        if (args.Contains("--exit"))
        {
            try { using var signal = EventWaitHandle.OpenExisting(exitEventName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }
        // One instance per user session. Launching it again (double-click, pinned taskbar icon,
        // Start menu) asks the running copy to show its window, then exits.
        const string showEventName = @"Local\MinecraftSurvivalHud.Show";
        using var mutex = new Mutex(initiallyOwned: true, @"Local\MinecraftSurvivalHud.SingleInstance", out bool first);
        if (!first)
        {
            if (!args.Contains(StartupRegistration.BackgroundArg))
                try
                {
                    AllowSetForegroundWindow(-1);   // let the running copy bring its window to the front
                    using var show = EventWaitHandle.OpenExisting(showEventName);
                    show.Set();
                }
                catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }

        // Every window only shows a finished bitmap 1:1, so GPU composition buys nothing; software
        // rendering skips loading the Direct3D pipeline (tens of MB of RAM) with identical pixels.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        StartupRegistration.MigrateFromOldName();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        HudApp? hud = null;
        using var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, exitEventName);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName);
        RegisteredWaitHandle? exitWait = null, showWait = null;

        app.DispatcherUnhandledException += (_, e) =>
        {
            // A failed redraw must never leave a stuck window on the taskbar: log it and carry on.
            Log.Write("Unhandled error: " + e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Write("Fatal error: " + e.ExceptionObject);
            try { hud?.Dispose(); } catch { /* best effort: remove tray icons before dying */ }
        };
        // Log-off / shutdown: remove the tray icons and windows cleanly.
        app.SessionEnding += (_, _) => hud?.Dispose();

        try
        {
            hud = new HudApp();
            hud.Start();
            exitWait = ThreadPool.RegisterWaitForSingleObject(exitSignal, (_, _) =>
                app.Dispatcher.BeginInvoke(new Action(() => app.Shutdown())), null, Timeout.Infinite, executeOnlyOnce: true);
            showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) =>
                app.Dispatcher.BeginInvoke(new Action(() => hud?.ShowMainWindow())), null, Timeout.Infinite, executeOnlyOnce: false);
            if (args.Contains("--settings")) hud.ShowSettings();
            // Opened by the user: show the window. Started with Windows: stay quietly in the tray.
            else if (!args.Contains(StartupRegistration.BackgroundArg)) app.Dispatcher.BeginInvoke(new Action(() => hud.ShowMainWindow()));
            return app.Run();
        }
        catch (Exception ex)
        {
            Log.Write("Startup failed: " + ex);
            MessageBox.Show("Taskbar Hearts could not start:\n\n" + ex.Message, "Taskbar Hearts",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        finally
        {
            exitWait?.Unregister(null);
            showWait?.Unregister(null);
            hud?.Dispose();
        }
    }
}
