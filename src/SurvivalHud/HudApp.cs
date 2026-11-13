using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SurvivalHud.Core;
using SurvivalHud.Monitoring;
using SurvivalHud.Native;
using SurvivalHud.Settings;
using SurvivalHud.Shell;
using SurvivalHud.Taskbar;
using SurvivalHud.Textures;
using SurvivalHud.Windows;

namespace SurvivalHud;

/// <summary>
/// The application controller. It owns the two layer windows, the tray fallback, the timers
/// and the shared hover state, and is the only place where those pieces talk to each other.
///
/// Timers: a 1 s tick samples the system and re-checks taskbar geometry; a 50 ms poll runs
/// only while the pointer is on the taskbar/icons or a hover delay is pending, and stops
/// itself otherwise, so the app is idle between ticks.
/// </summary>
public sealed class HudApp : IDisposable
{
    private const int MenuSettings = 1, MenuAlign = 2, MenuPin = 3, MenuStartup = 4, MenuExit = 5;
    private const int AppToggleHud = 11, AppFullHud = 12, AppSettingsCmd = 13, AppStartup = 14, AppExit = 15;
    private const int AppLayoutCompact = 16, AppLayoutBars = 17, AppLayoutBarsSlot = 18, AppOpen = 19;
    private const int SlotRefreshTicks = 5;

    public AppSettings Settings { get; }
    public SpriteSet Sprites { get; private set; }
    public string? TextureNote { get; private set; }
    public bool Aligning { get; private set; }
    public event Action? StateChanged;

    /// <summary>Raised once a second after new readings were taken (the main window listens while open).</summary>
    public event Action? Ticked;
    public Metrics CurrentMetrics => _metrics;
    public (double Down, double Up)? NetRate => _netRate;

    private readonly SystemMonitor _monitor = new();
    private readonly NetworkMonitor _net = new();
    private (double Down, double Up)? _netRate;
    private readonly GpuMonitor _gpu = new();
    private double? _gpuPercent;
    private (double Free, double Total)? _drive;
    private (int Processes, int Threads)? _procs;
    private readonly TopProcessMonitor _top = new();
    private (string Name, double Percent)? _topProcess;
    private (int Percent, bool Muted)? _volume;
    private List<(string Name, double Free, double Total)>? _drives;
    private (double Percent, double FullWh, double DesignWh)? _health;
    private bool _healthRead;
    private int _hoverSlot = -1;

    private bool GamePopup => Settings.PopupStyle == AppSettings.PopupGame;
    private readonly HoverController _hover = new();
    private readonly TaskbarOverlay _overlay;
    private readonly HudWindow _hud;
    private readonly HwndSource _host;
    private readonly TrayIcons _tray;
    private readonly AppTrayIcon _appIcon;
    private bool _menuOpen;
    private readonly DispatcherTimer _tick, _poll;
    private readonly int _taskbarCreatedMsg;
    private readonly Win32.WinEventProc _foregroundProc;
    private IntPtr _foregroundHook;

    private Metrics _metrics;
    private TaskbarInfo? _taskbar;
    private RECT? _slotRelative;      // native battery slot, relative to the taskbar's top-left corner
    private IntPtr _slotTaskbar;
    private int _slotMisses;
    private bool _slotBusy;
    private TrayLayout? _freeRelative; // free taskbar space for Survival Bars, relative to the taskbar's left edge
    private IntPtr _freeTaskbar;
    private bool _freeBusy;
    private RECT _anchor;             // last known rectangle of the compact icons
    private int _trayHot = -1;
    private long _tickCount, _pollCount;
    private SettingsWindow? _settingsWindow;
    private MainWindow? _mainWindow;
    private bool _disposed;

    public HudApp()
    {
        Settings = AppSettings.Load();
        Sprites = MinecraftTextureLoader.Resolve(Settings.TextureSource, out string? note);
        TextureNote = note;

        _hover.OpenDelayMs = Settings.HoverDelayMs;
        _hover.CloseDelayMs = Settings.CloseDelayMs;
        if (Settings.PinEnabled && Settings.Pinned) _hover.SetPinned(true, false);

        _overlay = new TaskbarOverlay();
        _overlay.PointerMoved += EnsurePolling;
        _overlay.Clicked += TogglePin;
        _overlay.RightClicked += ShowMenu;
        _overlay.Dragged += (dx, dy) => NudgeAlignment(dx, dy);
        _overlay.KeyPressed += OnAlignmentKey;
        _hud = new HudWindow();

        // Hidden top-level window: receives broadcasts (Explorer restart, power, display) and tray callbacks.
        _host = new HwndSource(new HwndSourceParameters("SurvivalHudHost")
        {
            Width = 0, Height = 0, PositionX = -32000, PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = (int)(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE),
        });
        _host.AddHook(HostProc);
        _taskbarCreatedMsg = Win32.RegisterWindowMessage("TaskbarCreated");
        _tray = new TrayIcons(_host.Handle);
        _appIcon = new AppTrayIcon(_host.Handle);

        _foregroundProc = (_, _, _, _, _, _, _) => OnForegroundChanged();
        _foregroundHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);

        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Tick();
        _poll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
        _poll.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        _tick.Start();
        Tick();
    }

    // ---- periodic work ------------------------------------------------------------------

    private void Tick()
    {
        if (_disposed) return;
        _metrics = _monitor.Sample();
        // GPU, network and hotbar readings are only taken while the popup or the main window is
        // open, and always on a worker thread: the overlay is a child of the taskbar, so any stall
        // on this thread would also stall the taskbar.
        SampleInBackground(full: _tickCount % 10 == 0);
        if (_tickCount == 15 || _tickCount % 600 == 0 || _trimSoon) TrimMemory();
        Ticked?.Invoke();
        if (_tickCount++ % SlotRefreshTicks == 0) RefreshSlotAsync();
        Refresh();

        if (Win32.GetCursorPos(out POINT pt) && _taskbar is { Hidden: false } tb && tb.Rect.Contains(pt)) EnsurePolling();
    }

    /// <summary>Re-reads geometry and redraws whatever changed. Cheap enough to call from any event.</summary>
    private void Refresh()
    {
        if (_disposed) return;
        var previous = _taskbar;
        _taskbar = TaskbarLocator.Find(Settings.TargetDisplay);
        if (_taskbar?.Hwnd != previous?.Hwnd) { _slotRelative = null; _slotMisses = 0; _freeRelative = null; }

        bool fullscreen = _taskbar != null && !Aligning && TaskbarLocator.IsFullscreenAppActive(_taskbar.Monitor);

        // The app's own tray icon is always present (re-added here after an Explorer restart).
        _appIcon.Ensure(_taskbar?.Dpi ?? Win32.GetDpiForSystem());

        if (!Settings.HudVisible)
        {
            // HUD icons switched off from the tray icon: nothing on the taskbar at all.
            _overlay.HideLayer();
            if (_tray.Active) _tray.Remove();
            _anchor = _appIcon.GetRect()
                ?? (_taskbar is { } t ? new RECT(t.Rect.Right - 8, t.Rect.Top, t.Rect.Right, t.Rect.Bottom) : default);
        }
        else if (Settings.Mode == AppSettings.ModeTray || VerticalTaskbar)
        {
            _overlay.HideLayer();
            UpdateTray();
        }
        else
        {
            if (_tray.Active) _tray.Remove();
            if (_taskbar == null || _taskbar.Hidden || fullscreen) _overlay.HideLayer();
            else
            {
                _overlay.Attach(_taskbar.Hwnd);
                RenderOverlay(_taskbar, OverlayRect(_taskbar));
                if (!_menuOpen) _overlay.BringToTop();
            }
        }

        UpdateHud(fullscreen);
    }

    private bool _trimSoon;

    private bool? _windowsLight;

    /// <summary>Light popup: follows Windows' "apps" light/dark mode unless the user fixed it in Settings.</summary>
    private bool LightPopup => Settings.PopupFollowsTheme ? _windowsLight ??= ReadWindowsLight() : Settings.LightPopup;

    private static bool ReadWindowsLight()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0) is int v && v == 1;
        }
        catch { return false; }
    }


    /// <summary>Sprites and fill for each row, top to bottom.</summary>
    public IReadOnlyList<(StatSprites Sprites, double? Percent)> BarRows()
    {
        var heart = (Settings.ChargingIndicator && _metrics.Charging ? Sprites.HeartCharging : Sprites.Heart, (double?)HeartLevel);
        var armor = (Sprites.Armor, _metrics.MemFreePercent);
        var hunger = (Sprites.Food, _metrics.CpuPercent is double cpu ? FillMapping.HungerFromCpu(cpu) : (double?)null);
        return Settings.BarsRows switch
        {
            AppSettings.RowsHearts => new[] { heart },
            AppSettings.RowsHeartsHunger => new[] { heart, hunger },
            AppSettings.RowsArmorHunger => new[] { armor, hunger },
            _ => new[] { armor, heart },
        };
    }

    /// <summary>Changes which bars the Survival Bars layouts show, immediately.</summary>
    public void SetBarsRows(string rows)
    {
        Settings.BarsRows = rows;
        Settings.Save();
        Refresh();
        StateChanged?.Invoke();
    }

    /// <summary>Hearts: battery level, or full on a PC without a battery.</summary>
    private int? HeartLevel => _metrics.HasBattery ? _metrics.BatteryPercent : 100;

    /// <summary>
    /// A taskbar on the left or right edge (possible with taskbar mods). The overlay layouts are
    /// built for a horizontal taskbar, so the three notification-area icons are used instead;
    /// the saved layout is untouched and comes back when the taskbar is horizontal again.
    /// </summary>
    private bool VerticalTaskbar => _taskbar is { } t && t.Rect.Height > t.Rect.Width;

    // ---- transparent taskbar detection -------------------------------------------------------

    private int _busyScore;

    /// <summary>
    /// True when the taskbar behind the icons looks see-through (wallpaper showing, e.g. TranslucentTB).
    /// Compact and battery-spot layouts must cover native icons with a solid patch, which then
    /// shows as a block; Survival Bars has no background and is unaffected.
    /// </summary>
    public bool TaskbarLooksTransparent { get; private set; }

    /// <summary>Samples the taskbar colours beside an area and updates the transparency verdict (with hysteresis).</summary>
    private uint[]? SampleBackground(RECT area, TaskbarInfo tb)
    {
        uint[]? rows = ScreenSampler.SampleRows(area, tb.Rect, (int)Math.Round(40 * tb.Scale));
        if (rows == null || !TaskbarIdle(tb)) return rows;
        const int Threshold = 150;   // luma units (r*3 + g*6 + b): about 15 grey levels between quartiles
        // Needs ~10 s of consistently uneven samples to flag, ~3 s of even ones to clear.
        _busyScore = Math.Clamp(_busyScore + (ScreenSampler.LastSpread > Threshold ? 1 : -3), 0, 12);
        bool transparent = TaskbarLooksTransparent ? _busyScore > 0 : _busyScore >= 10;
        if (transparent != TaskbarLooksTransparent)
        {
            TaskbarLooksTransparent = transparent;
            StateChanged?.Invoke();
        }
        return rows;
    }

    /// <summary>
    /// True when nothing is drawn over or lit up on the taskbar: the pointer is elsewhere and no
    /// shell flyout (hidden icons, Start, Quick Settings…) is in front. Only then does the
    /// background sample say anything about the taskbar's own transparency.
    /// </summary>
    private static bool TaskbarIdle(TaskbarInfo tb)
    {
        if (Win32.GetCursorPos(out POINT pt) && pt.Y >= tb.Rect.Top - 600 && pt.X >= tb.Rect.Right - 900) return false;
        string fg = Win32.ClassNameOf(Win32.GetForegroundWindow());
        return fg is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow"
            or "TopLevelWindowForOverflowXamlIsland" or "NotifyIconOverflowWindow" or "Xaml_WindowedPopupClass" or "XamlExplorerHostIslandWindow");
    }

    // ---- diagnostics -------------------------------------------------------------------------

    /// <summary>
    /// A plain-text report for bug reports: Windows build, scaling, taskbar geometry and settings,
    /// what was detected, known taskbar mods and this app's settings. Contains no user name and
    /// no folder paths, so it can be pasted publicly.
    /// </summary>
    public string BuildDiagnostics()
    {
        var sb = new System.Text.StringBuilder();
        void L(string k, object? v) => sb.Append(k).Append(": ").Append(v?.ToString() ?? "-").Append('\n');
        string? Reg(string key, string name)
        {
            try { return Microsoft.Win32.Registry.GetValue(key, name, null)?.ToString(); } catch { return null; }
        }

        sb.Append("Taskbar Hearts diagnostics\n==========================\n");
        L("App version", typeof(HudApp).Assembly.GetName().Version);
        string cv = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        string product = Reg(cv, "ProductName") ?? "Windows";
        // Windows 11 still reports "Windows 10" in ProductName; the build number tells them apart.
        if (int.TryParse(Reg(cv, "CurrentBuild"), out int build) && build >= 22000) product = product.Replace("Windows 10", "Windows 11");
        L("Windows", $"{product} {Reg(cv, "DisplayVersion")} build {Reg(cv, "CurrentBuild")}.{Reg(cv, "UBR")}");
        string adv = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        string per = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        L("Taskbar alignment", Reg(adv, "TaskbarAl") == "0" ? "left" : "centered");
        L("Taskbar button size (raw IconSizePreference / TaskbarSi)", $"{Reg(adv, "IconSizePreference") ?? "-"} / {Reg(adv, "TaskbarSi") ?? "-"}");
        L("Taskbar theme", Reg(per, "SystemUsesLightTheme") == "1" ? "light" : "dark");
        L("Transparency effects", Reg(per, "EnableTransparency") == "0" ? "off" : "on");
        L("Monitors", TaskbarLocator.GetDisplays().Count);

        if (_taskbar is { } tb)
        {
            string edge = tb.Rect.Width >= tb.Rect.Height
                ? (tb.Rect.Top <= tb.Monitor.Top ? "top" : "bottom")
                : (tb.Rect.Left <= tb.Monitor.Left ? "left" : "right");
            L("Taskbar", $"{tb.Rect.Width}x{tb.Rect.Height} px at {edge}, {(tb.IsPrimary ? "primary" : "secondary")} display");
            L("Scaling", $"{tb.Dpi} dpi ({tb.Scale * 100:0}%)");
            L("Taskbar hidden (auto-hide)", tb.Hidden);
            L("Battery button found", Slot(tb) is { } sl ? $"yes, {sl.Width}x{sl.Height} at x={sl.Left - tb.Rect.Left}" : "no");
            if (_freeRelative is { } fr)
                L("Tray / app buttons / Quick Settings x", $"{fr.TrayLeft} / {fr.ButtonsRight?.ToString() ?? "-"} / {fr.QuickSettingsLeft?.ToString() ?? "-"}");
            else L("Tray layout", "not read (only read in the Survival Bars layouts)");
        }
        else L("Taskbar", "not found");

        L("Attached to taskbar", _overlay.AttachedToTaskbar);
        L("Overlay shown", _overlay.Shown);
        L("Background spread", $"{ScreenSampler.LastSpread} (transparent taskbar: {TaskbarLooksTransparent})");

        var mods = new List<string>();
        foreach (var (proc, name) in new[] { ("TranslucentTB", "TranslucentTB"), ("windhawk", "Windhawk"), ("StartAllBackCfg", "StartAllBack"),
                                             ("ExplorerPatcher", "ExplorerPatcher"), ("RoundedTB", "RoundedTB"), ("TaskbarX", "TaskbarX") })
            try { if (System.Diagnostics.Process.GetProcessesByName(proc).Length > 0) mods.Add(name + " (running)"); } catch { }
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var (dir, name) in new[] { ("StartAllBack", "StartAllBack"), ("ExplorerPatcher", "ExplorerPatcher") })
            if (Directory.Exists(Path.Combine(pf, dir)) && !mods.Exists(m => m.StartsWith(name))) mods.Add(name + " (installed)");
        L("Taskbar mods", mods.Count > 0 ? string.Join(", ", mods) : "none detected");

        L("Layout", $"{Settings.HudLayout} ({Settings.Mode}), popup {Settings.PopupStyle}, HUD visible {Settings.HudVisible}");
        L("Scales", $"icons {Settings.CompactScale}, popup {Settings.HudScale}, spacing {Settings.IconSpacing}");
        L("Offsets", $"slot {Settings.SlotOffsetX},{Settings.SlotOffsetY} edge {Settings.EdgeOffsetX},{Settings.EdgeOffsetY} bars {Settings.BarsOffsetX},{Settings.BarsOffsetY}");
        L("Textures", Sprites.Source + (Settings.TextureSource is AppSettings.TexturesAuto or AppSettings.TexturesBuiltIn ? $" ({Settings.TextureSource})" : " (custom path)"));
        L("Battery", _metrics.HasBattery ? $"{_metrics.BatteryPercent}% {(_metrics.Charging ? "charging" : _metrics.OnAc ? "on AC" : "on battery")}" : "none");

        sb.Append("\nRecent log:\n");
        try
        {
            string log = Path.Combine(AppSettings.Directory, "log.txt");
            if (File.Exists(log))
                foreach (string line in File.ReadLines(log).TakeLast(8)) sb.Append("  ").Append(Anonymise(line)).Append('\n');
            else sb.Append("  (empty)\n");
        }
        catch { sb.Append("  (unreadable)\n"); }
        return Anonymise(sb.ToString());
    }

    /// <summary>Removes the user name and profile folder from text.</summary>
    private static string Anonymise(string text)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        if (user.Length > 2) text = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(user), "<user>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text;
    }

    private Canvas32 RenderPopup(SpriteSet set, HudState st, int px, bool light) =>
        GamePopup ? HudRenderer.RenderGame(set, st, px, light) : HudRenderer.Render(set, st, px, light);

    private bool _sampling;

    /// <summary>
    /// Takes the slower readings (GPU counters ~250 ms on first use, network adapters ~40 ms,
    /// battery driver, drive) on a worker thread, then applies them on the UI thread. One run at
    /// a time; the popup shows the new values on the next redraw.
    /// </summary>
    private void SampleInBackground(bool full)
    {
        if (_sampling || _disposed) return;
        bool popup = _hud.Shown, game = GamePopup && popup, window = _mainWindow != null;
        bool wantNet = window || game, wantDrive = game && (full || _drive == null), wantHealth = game && !_healthRead && _metrics.HasBattery;
        if (!popup && !wantNet && !_gpuOpen) return;
        _sampling = true;
        Task.Run(() =>
        {
            double? gpu = null;
            if (popup) { gpu = _gpu.Sample(); }
            else _gpu.Dispose();                       // popup closed: release the GPU counter query
            var net = wantNet ? _net.Sample() : null;
            var procs = game ? SystemExtras.ProcessCounts() : null;
            var top = game ? _top.Sample() : null;
            var volume = game ? VolumeReader.Read() : null;
            var drives = game && (full || _drives == null) ? OtherDrives.Read() : null;
            var drive = wantDrive ? SystemExtras.SystemDrive() : null;
            var health = wantHealth ? SystemExtras.BatteryHealth() : null;
            return (popup, gpu, wantNet, net, procs, drive, wantHealth, health, top, volume, drives, game);
        }).ContinueWith(t =>
        {
            _sampling = false;
            if (_disposed || t.Status != TaskStatus.RanToCompletion) return;
            var r = t.Result;
            _gpuOpen = r.popup;
            _gpuPercent = r.popup ? r.gpu ?? _gpuPercent : null;
            _netRate = r.wantNet ? r.net ?? _netRate : null;
            _procs = r.procs ?? _procs;
            if (r.game) { _topProcess = r.top ?? _topProcess; _volume = r.volume; }
            _drives = r.drives ?? _drives;
            _drive = r.drive ?? _drive;
            if (r.wantHealth) { _health = r.health; _healthRead = true; }
            if (_hud.Shown) UpdateHud(fullscreen: false);
            Ticked?.Invoke();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private bool _gpuOpen;

    /// <summary>Switches the hover popup between the labelled list and the Minecraft-HUD layout.</summary>
    public void SetPopupStyle(string style)
    {
        Settings.PopupStyle = style == AppSettings.PopupGame ? AppSettings.PopupGame : AppSettings.PopupList;
        Settings.Save();
        UpdateHud(fullscreen: false);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Gives idle memory back to Windows: a compacting collection, then the working set is
    /// trimmed so only pages the app actually touches come back. Skipped while the popup,
    /// alignment mode or the settings window are in use so nothing visible ever stutters.
    /// </summary>
    private void TrimMemory()
    {
        if (_hud.Shown || Aligning || _settingsWindow != null || _mainWindow != null || _menuOpen) { _trimSoon = true; return; }
        _trimSoon = false;
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        Win32.SetProcessWorkingSetSize(Win32.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }

    private void Poll()
    {
        if (_disposed || !Win32.GetCursorPos(out POINT pt)) return;
        _pollCount++;
        bool onTaskbar = _taskbar is { Hidden: false } tb && tb.Rect.Contains(pt);

        if (!Aligning)
        {
            bool was = _hover.Visible;
            bool now = _hover.Update(InHotArea(pt), Environment.TickCount64);
            if (was != now) { _hoverSlot = -1; UpdateHud(fullscreen: false); }
            else if (now && GamePopup && _hud.Shown)
            {
                // Pointer over a bar or hotbar slot: show its tooltip.
                RECT r = _hud.ScreenRect;
                double scale = _taskbar?.Scale ?? Win32.GetDpiForSystem() / 96.0;
                int hpx = Math.Max(1, (int)Math.Round(Settings.HudScale * scale));
                int slot = r.Contains(pt) ? HudRenderer.GameHitAt(pt.X - r.Left, pt.Y - r.Top, hpx) : -1;
                if (slot != _hoverSlot) { _hoverSlot = slot; UpdateHud(fullscreen: false); }
            }
        }

        // While the pointer is on the taskbar its hover highlights change the colours beside
        // the overlay, so re-sample the background a few times per second.
        if (onTaskbar && _overlay.Shown && !Aligning && (Settings.CoverNativeBattery || SlotBars) && _pollCount % 3 == 0)
            RenderOverlay(_taskbar!, _overlay.ScreenRect);

        if (!_hover.NeedsPolling && !onTaskbar && !Aligning) _poll.Stop();
    }

    private void EnsurePolling()
    {
        if (!_poll.IsEnabled && !_disposed) _poll.Start();
    }

    private void OnForegroundChanged()
    {
        if (_disposed) return;
        // The taskbar jumps above other top-most windows when it is activated; follow it,
        // and hide while a full-screen application is in front.
        _overlay.Dispatcher.BeginInvoke(DispatcherPriority.Background, Refresh);
    }

    // ---- compact overlay ----------------------------------------------------------------

    private (int Px, int Gap, int PadX) CompactMetrics(double scale) =>
        (Math.Max(1, (int)Math.Round(Settings.CompactScale * scale)),
         Math.Max(0, (int)Math.Round(Settings.IconSpacing * scale)),
         (int)Math.Round(4 * scale));

    private RECT? Slot(TaskbarInfo tb)
    {
        if (!tb.IsPrimary || _slotRelative is not { } rel || _slotTaskbar != tb.Hwnd) return null;
        return new RECT(tb.Rect.Left + rel.Left, tb.Rect.Top + rel.Top, tb.Rect.Left + rel.Right, tb.Rect.Top + rel.Bottom);
    }

    /// <summary>Where the overlay belongs: on the native battery slot when it is known, otherwise at a saved offset from the taskbar edge.</summary>
    private RECT OverlayRect(TaskbarInfo tb)
    {
        if (SlotBars && SlotGeometry(tb) is { } sg) return sg.Overlay;
        if (BarsLayout) return BarsGeometry(tb, withOffset: true).Rect;
        double scale = tb.Scale;
        var (px, gap, padX) = CompactMetrics(scale);
        int content = CompactRenderer.ContentWidth(px, gap) + 2 * padX;
        int x, y, w, h;
        if (Slot(tb) is { } slot)
        {
            w = Math.Max(slot.Width, content);
            h = slot.Height;
            x = (slot.Left + slot.Right) / 2 - w / 2 + (int)Math.Round(Settings.SlotOffsetX * scale);
            y = slot.Top + (int)Math.Round(Settings.SlotOffsetY * scale);
        }
        else
        {
            w = content;
            h = tb.Rect.Height;
            x = tb.Rect.Right + (int)Math.Round(Settings.EdgeOffsetX * scale) - w;
            y = tb.Rect.Top + (int)Math.Round(Settings.EdgeOffsetY * scale);
        }
        return ClampToTaskbar(RECT.FromXYWH(x, y, w, h), tb);
    }

    private static RECT ClampToTaskbar(RECT r, TaskbarInfo tb)
    {
        int x = Math.Clamp(r.Left, tb.Rect.Left, Math.Max(tb.Rect.Left, tb.Rect.Right - r.Width));
        int y = Math.Clamp(r.Top, tb.Rect.Top - r.Height / 2, tb.Rect.Bottom - r.Height / 2);
        return RECT.FromXYWH(x, y, r.Width, r.Height);
    }

    private CompactState CurrentCompact() => new(
        HeartLevel / 100.0,
        _metrics.CpuPercent is double cpu ? FillMapping.HungerFromCpu(cpu) / 100.0 : null,
        _metrics.MemFreePercent / 100.0,
        Settings.ChargingIndicator && _metrics.Charging);

    private void RenderOverlay(TaskbarInfo tb, RECT rect)
    {
        _overlay.QuickSettingsArea = null;
        _barsHot = null;
        if (SlotBars && !Aligning && SlotGeometry(tb) is { } sg && RenderSlotBars(tb, sg)) return;
        if (BarsLayout)
        {
            int bpx = BarsGeometry(tb, withOffset: true).Px;
            var bars = SurvivalBarsRenderer.RenderRows(Sprites, BarRows(), rect.Width, rect.Height, bpx, Aligning);
            _overlay.Present(bars, rect.Left, rect.Top);
            _anchor = rect;
            return;
        }
        var (px, gap, _) = CompactMetrics(tb.Scale);
        uint[]? rows = !Aligning && Settings.CoverNativeBattery
            ? SampleBackground(rect, tb)
            : null;
        var canvas = CompactRenderer.Render(Sprites, CurrentCompact(),rect.Width, rect.Height, px, gap, rows, Aligning);
        _overlay.Present(canvas, rect.Left, rect.Top);
        _anchor = rect;
    }

    // ---- survival bars overlay ----------------------------------------------------------

    /// <summary>True when the two-row "Survival Bars" layout is the active taskbar overlay.</summary>
    private bool BarsLayout => Settings.HudLayout is AppSettings.LayoutBars or AppSettings.LayoutBarsSlot && Settings.Mode == AppSettings.ModeOverlay;

    /// <summary>Survival Bars drawn over the Quick Settings button (network + volume + battery); clicks still open Quick Settings.</summary>
    private bool SlotBars => BarsLayout && Settings.HudLayout == AppSettings.LayoutBarsSlot;

    /// <summary>
    /// Survival Bars placement: right-aligned just left of the notification area, in the empty
    /// taskbar space, vertically centred. Sprite size follows the compact icons' scale and only
    /// steps down (never below 2 physical px per sprite pixel) when the taskbar is too short or
    /// the free space too narrow.
    /// </summary>
    private (RECT Rect, int Px) BarsGeometry(TaskbarInfo tb, bool withOffset)
    {
        double scale = tb.Scale;
        int px = Math.Max(1, (int)Math.Round(Settings.CompactScale * scale));
        int minPx = Math.Min(px, 2);
        int padX = (int)Math.Round(4 * scale), margin = (int)Math.Round(6 * scale);

        int? trayLeft = null, buttonsRight = null;
        if (_freeRelative is { } fr && _freeTaskbar == tb.Hwnd)
        {
            trayLeft = tb.Rect.Left + fr.TrayLeft;
            if (fr.ButtonsRight is int br) buttonsRight = tb.Rect.Left + br;
        }
        trayLeft ??= TaskbarFreeSpaceFinder.TrayNotifyLeft(tb.Hwnd);
        if (trayLeft is not int tl || tl <= tb.Rect.Left || tl > tb.Rect.Right)
            tl = Slot(tb) is { } s ? s.Left - (int)Math.Round(220 * scale) : tb.Rect.Right - (int)Math.Round(360 * scale);

        int right = tl - margin;
        int available = buttonsRight is int b ? right - (b + margin) : int.MaxValue;
        while (px > minPx && (SurvivalBarsRenderer.ContentHeight(px, BarRows().Count) + 2 * px > tb.Rect.Height
                              || SurvivalBarsRenderer.ContentWidth(px) + 2 * padX > available))
            px--;

        int w = SurvivalBarsRenderer.ContentWidth(px) + 2 * padX, h = tb.Rect.Height;
        int x = right - w, y = tb.Rect.Top;
        if (withOffset)
        {
            x += (int)Math.Round(Settings.BarsOffsetX * scale);
            y += (int)Math.Round(Settings.BarsOffsetY * scale);
        }
        return (ClampToTaskbar(RECT.FromXYWH(x, y, w, h), tb), px);
    }

    private RECT? _barsHot;   // part of the overlay that is the bars (hover target), when not the whole overlay

    private readonly record struct SlotLayout(RECT Overlay, int Px);

    /// <summary>
    /// Battery-spot placement: the bars always cover the Quick Settings button (network + volume +
    /// battery) and, when they need more room, whole tray buttons to its left, never part of one.
    /// The covered stretch runs from the left edge of the first covered button to the clock, and
    /// the bars are centred in it, so the gaps to the neighbouring icon and to the clock match
    /// the taskbar's normal icon spacing. Sprites keep the Survival Bars size and only step down
    /// if nothing fits. Null when the button or the clock cannot be located.
    /// </summary>
    private SlotLayout? SlotGeometry(TaskbarInfo tb)
    {
        if (_freeRelative is not { QuickSettingsLeft: int qsRel, ClockLeft: int clockRel } fr || _freeTaskbar != tb.Hwnd) return null;
        int origin = tb.Rect.Left, qsLeft = origin + qsRel, clockLeft = origin + clockRel;
        double scale = tb.Scale;
        for (int px = Math.Max(2, (int)Math.Round(Settings.CompactScale * scale)); px >= 2; px--)
        {
            if (SurvivalBarsRenderer.ContentHeight(px, BarRows().Count) + 2 * px > tb.Rect.Height) continue;
            int need = SurvivalBarsRenderer.ContentWidth(px);
            // Rightmost button edge at or left of the Quick Settings button that leaves enough room.
            int? start = null;
            foreach (int rel in fr.ItemLefts)
            {
                int l = origin + rel;
                if (l <= qsLeft && l >= origin + fr.TrayLeft && clockLeft - l >= need && (start is not int s0 || l > s0)) start = l;
            }
            if (start is int left) return new SlotLayout(new RECT(left, tb.Rect.Top, clockLeft, tb.Rect.Bottom), px);
        }
        return null;
    }

    private bool RenderSlotBars(TaskbarInfo tb, SlotLayout g)
    {
        int w = g.Overlay.Width, h = g.Overlay.Height;
        var canvas = new Canvas32(w, h);
        // Opaque taskbar-coloured background (sampled beside the button) hides the native icons.
        uint[]? rows = SampleBackground(g.Overlay, tb);
        for (int y = 0; y < h; y++)
            canvas.SetRect(0, y, w, 1, rows != null ? rows[Math.Min(y, rows.Length - 1)] | 0xFF000000 : 0xFF202020);

        var bars = SurvivalBarsRenderer.RenderRows(Sprites, BarRows(), w, h, g.Px, false);
        canvas.Blit(new Sprite(bars.Width, bars.Height, bars.Pixels), 0, 0, w, h);

        _overlay.Present(canvas, g.Overlay.Left, g.Overlay.Top);
        _overlay.QuickSettingsArea = g.Overlay;   // left-click still opens Quick Settings
        _anchor = g.Overlay;
        return true;
    }

    /// <summary>Re-reads where the notification area and the app buttons are (Survival Bars only).</summary>
    private void RefreshFreeSpaceAsync()
    {
        if (_freeBusy || !BarsLayout || _taskbar is not { Hidden: false } tb) return;
        _freeBusy = true;
        IntPtr hwnd = tb.Hwnd;
        Task.Run(() =>
        {
            TrayLayout? rel = null;
            if (Win32.GetWindowRect(hwnd, out RECT before) && TaskbarFreeSpaceFinder.Find(hwnd) is { } abs
                && Win32.GetWindowRect(hwnd, out RECT after) && before.Equals(after))
                rel = abs.Offset(-before.Left);
            return rel;
        }).ContinueWith(t =>
        {
            _freeBusy = false;
            if (_disposed || Aligning || t.Status != TaskStatus.RanToCompletion || t.Result is not { } rel) return;
            if (rel.Equals(_freeRelative) && _freeTaskbar == hwnd) return;
            _freeRelative = rel;
            _freeTaskbar = hwnd;
            Refresh();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Switches between the compact three-icon layout and the two-row Survival Bars, immediately.</summary>
    public void SetHudLayout(string layout)
    {
        if (layout is not (AppSettings.LayoutBars or AppSettings.LayoutBarsSlot)) layout = AppSettings.LayoutCompact;
        if (Settings.HudLayout == layout) return;
        if (Aligning) SetAlignment(false, save: true);
        Settings.HudLayout = layout;
        _hover.Reset();
        _anchor = default;
        Settings.Save();
        Refresh();
        RefreshSlotAsync();
        StateChanged?.Invoke();
    }

    // ---- tray fallback ------------------------------------------------------------------

    private void UpdateTray()
    {
        uint dpi = _taskbar?.Dpi ?? Win32.GetDpiForSystem();
        int size = TrayIcons.IconSize(dpi);
        int px = Math.Max(1, size / BarRenderer.Cell);
        int at = (size - BarRenderer.Cell * px) / 2;
        var st = CurrentCompact();

        Canvas32 Icon(StatSprites s, double? f)
        {
            var c = new Canvas32(size, size);
            BarRenderer.DrawIcon(c, s, f, at, at, BarRenderer.Cell * px);
            return c;
        }

        string battery = _metrics.BatteryPercent is int b
            ? $"Battery {b}%" + (_metrics.Charging ? " (charging)" : "")
            : _metrics.HasBattery ? "Battery: not available" : "Desktop PC: no battery (hearts stay full)";
        string cpu = _metrics.CpuPercent is double c ? $"CPU {Math.Round(c):0}%" : "CPU: measuring";
        string ram = _metrics.MemFreePercent is double m ? $"RAM free {Math.Round(m):0}%" : "RAM: not available";

        _tray.Update(
            new[] { Icon(st.Charging ? Sprites.HeartCharging : Sprites.Heart, st.Heart), Icon(Sprites.Food, st.Food), Icon(Sprites.Armor, st.Armor) },
            new[] { battery, cpu, ram });

        if (_trayHot >= 0 && _tray.GetRect(_trayHot) is { } r) _anchor = r;
    }

    // ---- expanded HUD -------------------------------------------------------------------

    private bool InHotArea(POINT pt)
    {
        bool inIcons = false;
        if (Settings.Mode == AppSettings.ModeTray || VerticalTaskbar)
        {
            for (int i = 0; i < 3 && !inIcons; i++)
                if (_tray.GetRect(i) is { } r && r.Contains(pt)) { inIcons = true; _trayHot = i; _anchor = r; }
        }
        else inIcons = _overlay.Shown && (_barsHot is { } hot ? hot.Contains(pt) : _overlay.ScreenRect.Contains(pt));
        if (inIcons || !_hud.Shown) return inIcons;

        RECT hud = _hud.ScreenRect;
        if (hud.Contains(pt)) return true;
        // The strip between the HUD and the icons counts too, so crossing it never starts the close timer.
        var bridge = new RECT(Math.Min(hud.Left, _anchor.Left), Math.Min(hud.Bottom, _anchor.Bottom),
            Math.Max(hud.Right, _anchor.Right), Math.Max(hud.Top, _anchor.Top));
        return bridge.Contains(pt);
    }

    private void UpdateHud(bool fullscreen)
    {
        // A context menu must never end up underneath the (top-most) HUD, so the HUD steps aside while one is open.
        bool want = (_hover.Visible || Aligning) && !fullscreen && !_anchor.IsEmpty && !_menuOpen;
        if (!want)
        {
            if (_hud.Shown)
            {
                _trimSoon = true;   // popup just closed: release what drawing it used
                _gpuPercent = null;
            }
            _hud.HideLayer();
            return;
        }

        if (!_hud.Shown && !Aligning)
        {
            // Opening: start the readings now (on the worker) instead of waiting for the next tick.
            _healthRead = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => SampleInBackground(full: true));
        }
        double scale = _taskbar?.Scale ?? Win32.GetDpiForSystem() / 96.0;
        int px = Math.Max(1, (int)Math.Round(Settings.HudScale * scale));
        Canvas32 canvas = Aligning
            ? HudRenderer.RenderMessage(new[] { "Alignment Mode", "Drag the box or use the arrow keys", "Shift + arrow: 10 px", "Enter: save   Esc: cancel" }, px, LightPopup)
            : RenderPopup(Sprites, new HudState(
                _metrics.BatteryPercent, _metrics.HasBattery, _metrics.OnAc, _metrics.Charging, Settings.ChargingIndicator,
                _metrics.CpuPercent, _metrics.MemFreePercent,
                (_metrics.MemTotalBytes - _metrics.MemAvailableBytes) / 1073741824.0, _metrics.MemTotalBytes / 1073741824.0,
                _gpuPercent, GamePopup ? new GameData(_netRate?.Down, _netRate?.Up, _drive?.Free, _drive?.Total,
                    Environment.TickCount64 / 1000, _procs?.Processes, _procs?.Threads,
                    _health?.Percent, _health?.FullWh, _health?.DesignWh, _hoverSlot,
                    _drives, _topProcess?.Name, _topProcess?.Percent, _volume?.Percent, _volume?.Muted ?? false) : null),
                px, LightPopup);

        // Directly above the icons, centred on them, kept inside the monitor.
        RECT a = _anchor;
        var mon = Win32.TryGetMonitorInfo(Win32.MonitorFromRect(ref a, Win32.MONITOR_DEFAULTTONEAREST), out var mi)
            ? mi.rcMonitor : new RECT(a.Left - canvas.Width, a.Top - 2 * canvas.Height, a.Right + canvas.Width, a.Bottom);
        int gap = (int)Math.Round(6 * scale);
        int top = _taskbar is { Hidden: false } tb && tb.Rect.Intersects(a) ? Math.Min(a.Top, tb.Rect.Top) : Math.Min(a.Top, mon.Bottom);
        int y = top - gap - canvas.Height;
        if (y < mon.Top) y = Math.Min(a.Bottom + gap, mon.Bottom - canvas.Height);
        int x = (a.Left + a.Right) / 2 - canvas.Width / 2;
        x = Math.Clamp(x, mon.Left + gap, Math.Max(mon.Left + gap, mon.Right - gap - canvas.Width));

        _hud.Present(canvas, x, y);
        _hud.BringToTop();
    }

    // ---- battery slot detection ---------------------------------------------------------

    private void RefreshSlotAsync()
    {
        RefreshFreeSpaceAsync();
        if (_slotBusy || _taskbar is not { IsPrimary: true, Hidden: false } tb) return;
        _slotBusy = true;
        IntPtr hwnd = tb.Hwnd;
        Task.Run(() =>
        {
            RECT? rel = null;
            if (Win32.GetWindowRect(hwnd, out RECT before) && BatterySlotFinder.Find(hwnd) is { } abs
                && Win32.GetWindowRect(hwnd, out RECT after) && before.Equals(after) && before.Intersects(abs))
                rel = new RECT(abs.Left - before.Left, abs.Top - before.Top, abs.Right - before.Left, abs.Bottom - before.Top);
            return rel;
        }).ContinueWith(t =>
        {
            _slotBusy = false;
            if (_disposed || Aligning) return;
            RECT? rel = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
            if (rel == null && ++_slotMisses < 3 && _slotTaskbar == hwnd) return; // ride out brief lookup failures
            if (rel != null) _slotMisses = 0;
            bool changed = _slotTaskbar != hwnd || rel.HasValue != _slotRelative.HasValue
                || (rel.HasValue && !rel.Value.Equals(_slotRelative!.Value));
            _slotRelative = rel;
            _slotTaskbar = hwnd;
            if (changed) Refresh();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ---- user actions -------------------------------------------------------------------

    private void TogglePin()
    {
        if (Aligning || !Settings.PinEnabled) return;
        SetPinned(!_hover.Pinned);
    }

    public void SetPinned(bool pinned)
    {
        Win32.GetCursorPos(out POINT pt);
        _hover.SetPinned(pinned, InHotArea(pt));
        Settings.Pinned = pinned;
        Settings.Save();
        UpdateHud(fullscreen: false);
        EnsurePolling();
        StateChanged?.Invoke();
    }

    private int RunMenu(NativeMenu.Item[] items)
    {
        if (_menuOpen) return 0;
        _menuOpen = true;
        _hud.HideLayer();
        try { return NativeMenu.Show(_host.Handle, items); }
        finally
        {
            _menuOpen = false;
            Refresh();
        }
    }

    /// <summary>Shows or hides the three HUD icons (left-click on the app's tray icon).</summary>
    public void SetHudVisible(bool visible)
    {
        if (Settings.HudVisible == visible) return;
        if (!visible && Aligning) SetAlignment(false, save: true);
        Settings.HudVisible = visible;
        _hover.Reset();
        _trayHot = -1;
        if (visible) _anchor = default;
        Settings.Save();
        Refresh();
        RefreshSlotAsync();
        StateChanged?.Invoke();
    }

    /// <summary>Right-click menu of the app's tray icon.</summary>
    private void ShowAppMenu()
    {
        int cmd = RunMenu(new[]
        {
            new NativeMenu.Item(AppOpen, "Open Taskbar Hearts"),
            NativeMenu.Item.Separator,
            new NativeMenu.Item(AppToggleHud, Settings.HudVisible ? "Hide HUD" : "Show HUD"),
            new NativeMenu.Item(AppFullHud, "Open Full HUD", _hover.Pinned),
            NativeMenu.Item.Submenu("HUD Layout",
                new NativeMenu.Item(AppLayoutCompact, "Compact (Heart + Hunger + Armor)", Settings.HudLayout == AppSettings.LayoutCompact),
                new NativeMenu.Item(AppLayoutBars, "Survival Bars", Settings.HudLayout == AppSettings.LayoutBars,
                    Settings.Mode == AppSettings.ModeOverlay),
                new NativeMenu.Item(AppLayoutBarsSlot, "Survival Bars in battery spot (covers network, volume, ENG)", Settings.HudLayout == AppSettings.LayoutBarsSlot,
                    Settings.Mode == AppSettings.ModeOverlay)),
            new NativeMenu.Item(AppSettingsCmd, "Settings"),
            new NativeMenu.Item(AppStartup, "Start with Windows", StartupRegistration.IsEnabled),
            NativeMenu.Item.Separator,
            new NativeMenu.Item(AppExit, "Exit"),
        });
        switch (cmd)
        {
            case AppOpen: if (_mainWindow == null) ToggleMainWindow(); else _mainWindow.Activate(); break;
            case AppToggleHud: SetHudVisible(!Settings.HudVisible); break;
            case AppFullHud: SetPinned(!_hover.Pinned); break;
            case AppSettingsCmd: ShowSettings(); break;
            case AppLayoutCompact: SetHudLayout(AppSettings.LayoutCompact); break;
            case AppLayoutBars: SetHudLayout(AppSettings.LayoutBars); break;
            case AppLayoutBarsSlot: SetHudLayout(AppSettings.LayoutBarsSlot); break;
            case AppStartup: StartupRegistration.Set(!StartupRegistration.IsEnabled); StateChanged?.Invoke(); break;
            case AppExit: Application.Current.Shutdown(); break;
        }
    }

    private void ShowMenu()
    {
        bool overlayMode = Settings.Mode == AppSettings.ModeOverlay;
        int cmd = RunMenu(new[]
        {
            new NativeMenu.Item(MenuSettings, "Settings"),
            new NativeMenu.Item(MenuAlign, "Alignment Mode", Aligning, overlayMode),
            new NativeMenu.Item(MenuPin, "Pin HUD", _hover.Pinned, Settings.PinEnabled),
            new NativeMenu.Item(MenuStartup, "Start with Windows", StartupRegistration.IsEnabled),
            NativeMenu.Item.Separator,
            new NativeMenu.Item(MenuExit, "Exit"),
        });
        switch (cmd)
        {
            case MenuSettings: ShowSettings(); break;
            case MenuAlign: SetAlignment(!Aligning, save: true); break;
            case MenuPin: SetPinned(!_hover.Pinned); break;
            case MenuStartup: StartupRegistration.Set(!StartupRegistration.IsEnabled); StateChanged?.Invoke(); break;
            case MenuExit: Application.Current.Shutdown(); break;
        }
    }

    /// <summary>Opens the main window (or brings it forward); clicking the tray icon again closes it.</summary>
    public void ToggleMainWindow()
    {
        if (_mainWindow != null) { _mainWindow.Close(); return; }
        _mainWindow = new MainWindow(this);
        _mainWindow.Closed += (_, _) => { _mainWindow = null; _trimSoon = true; };
        _mainWindow.Show();
        // Raise it above other windows even when Windows withholds focus, without keeping it top-most.
        _mainWindow.Topmost = true;
        _mainWindow.Activate();
        _mainWindow.Topmost = false;
    }

    public void ShowSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => { _settingsWindow = null; _trimSoon = true; };
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    /// <summary>Applies edited settings immediately and saves them.</summary>
    public void ApplySettings(bool reloadTextures = false)
    {
        Settings.Sanitize();
        _hover.OpenDelayMs = Settings.HoverDelayMs;
        _hover.CloseDelayMs = Settings.CloseDelayMs;
        if (!Settings.PinEnabled && _hover.Pinned) { _hover.SetPinned(false, false); Settings.Pinned = false; }
        if (Settings.Mode == AppSettings.ModeTray && Aligning) SetAlignment(false, save: true);
        if (reloadTextures)
        {
            Sprites = MinecraftTextureLoader.Resolve(Settings.TextureSource, out string? note);
            TextureNote = note;
        }
        Settings.Save();
        _trayHot = -1;
        if (Settings.Mode == AppSettings.ModeTray) _anchor = default;
        Refresh();
        RefreshSlotAsync();
        StateChanged?.Invoke();
    }

    // ---- alignment mode -----------------------------------------------------------------

    private (double, double, double, double) _alignBackup;
    private (double, double) _alignBarsBackup;

    public void SetAlignment(bool on, bool save)
    {
        if (on == Aligning) return;
        if (on)
        {
            if (!Settings.HudVisible) SetHudVisible(true);
            if (Settings.Mode != AppSettings.ModeOverlay || _taskbar == null || _taskbar.Hidden || VerticalTaskbar) return;
            _alignBackup = (Settings.SlotOffsetX, Settings.SlotOffsetY, Settings.EdgeOffsetX, Settings.EdgeOffsetY);
            _alignBarsBackup = (Settings.BarsOffsetX, Settings.BarsOffsetY);
            Aligning = true;
            _hover.Reset();
            Refresh();
            _overlay.SetInteractive(true);
            EnsurePolling();
        }
        else
        {
            Aligning = false;
            _overlay.SetInteractive(false);
            if (save) Settings.Save();
            else
            {
                (Settings.SlotOffsetX, Settings.SlotOffsetY, Settings.EdgeOffsetX, Settings.EdgeOffsetY) = _alignBackup;
                (Settings.BarsOffsetX, Settings.BarsOffsetY) = _alignBarsBackup;
            }
            Refresh();
        }
        StateChanged?.Invoke();
    }

    private void NudgeAlignment(int dx, int dy)
    {
        if (!Aligning || _taskbar is not { } tb) return;
        RECT cur = OverlayRect(tb);
        RECT next = ClampToTaskbar(RECT.FromXYWH(cur.Left + dx, cur.Top + dy, cur.Width, cur.Height), tb);
        double scale = tb.Scale;
        if (BarsLayout)
        {
            RECT home = BarsGeometry(tb, withOffset: false).Rect;
            Settings.BarsOffsetX = (next.Left - home.Left) / scale;
            Settings.BarsOffsetY = (next.Top - home.Top) / scale;
        }
        else if (Slot(tb) is { } slot)
        {
            Settings.SlotOffsetX = (next.Left - ((slot.Left + slot.Right) / 2 - next.Width / 2)) / scale;
            Settings.SlotOffsetY = (next.Top - slot.Top) / scale;
        }
        else
        {
            Settings.EdgeOffsetX = (next.Right - tb.Rect.Right) / scale;
            Settings.EdgeOffsetY = (next.Top - tb.Rect.Top) / scale;
        }
        Refresh();
    }

    private void OnAlignmentKey(int vk, bool shift)
    {
        int step = shift ? 10 : 1;
        switch (vk)
        {
            case Win32.VK_LEFT: NudgeAlignment(-step, 0); break;
            case Win32.VK_RIGHT: NudgeAlignment(step, 0); break;
            case Win32.VK_UP: NudgeAlignment(0, -step); break;
            case Win32.VK_DOWN: NudgeAlignment(0, step); break;
            case Win32.VK_RETURN: SetAlignment(false, save: true); break;
            case Win32.VK_ESCAPE: SetAlignment(false, save: false); break;
        }
    }

    public void ResetPosition()
    {
        var d = new AppSettings();
        (Settings.SlotOffsetX, Settings.SlotOffsetY, Settings.EdgeOffsetX, Settings.EdgeOffsetY) =
            (d.SlotOffsetX, d.SlotOffsetY, d.EdgeOffsetX, d.EdgeOffsetY);
        (Settings.BarsOffsetX, Settings.BarsOffsetY) = (d.BarsOffsetX, d.BarsOffsetY);
        Settings.Save();
        Refresh();
    }

    // ---- window messages ----------------------------------------------------------------

    private IntPtr HostProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed) return IntPtr.Zero;

        if (msg == _taskbarCreatedMsg)
        {
            // Explorer restarted: its taskbar window and our tray icons are gone. Start over.
            _slotRelative = null;
            _slotMisses = 0;
            _freeRelative = null;
            _tray.Invalidate();
            _appIcon.Invalidate();
            Refresh();
            _tickCount = 0; // look the battery slot up again on the next tick, once the new taskbar has settled
        }
        else if (msg == AppTrayIcon.CallbackMessage)
        {
            // Version-4 notifications: the event is in the low word of lParam.
            const int NIN_KEYSELECT = 0x401;
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case Win32.NIN_SELECT:
                case NIN_KEYSELECT:
                    ToggleMainWindow();
                    break;
                case Win32.WM_CONTEXTMENU:
                    ShowAppMenu();
                    break;
            }
            handled = true;
        }
        else if (msg == TrayIcons.CallbackMessage)
        {
            int evt = (int)(lParam.ToInt64() & 0xFFFF), id = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
            switch (evt)
            {
                case Win32.WM_MOUSEMOVE:
                case Win32.NIN_POPUPOPEN:
                    if (id is >= 1 and <= 3 && _tray.GetRect(id - 1) is { } r) { _trayHot = id - 1; _anchor = r; }
                    EnsurePolling();
                    break;
                case Win32.WM_LBUTTONUP:
                    TogglePin();
                    break;
                case Win32.WM_CONTEXTMENU:
                case Win32.WM_RBUTTONUP:
                    ShowMenu();
                    break;
            }
            handled = true;
        }
        else if (msg is Win32.WM_POWERBROADCAST or Win32.WM_DISPLAYCHANGE or Win32.WM_SETTINGCHANGE)
        {
            if (msg == Win32.WM_SETTINGCHANGE) _windowsLight = null;   // theme may have changed: re-read it
            // Battery/AC change, resolution or DPI change, taskbar or theme setting change.
            _overlay.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (_disposed) return;
                _metrics = _monitor.Sample();
                Refresh();
            });
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tick.Stop();
        _poll.Stop();
        if (_foregroundHook != IntPtr.Zero) Win32.UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
        _tray.Dispose();
        _appIcon.Dispose();
        _settingsWindow?.Close();
        _mainWindow?.Close();
        _gpu.Dispose();
        _overlay.Close();
        _hud.Close();
        _host.Dispose();
        GC.KeepAlive(_foregroundProc);
    }
}
