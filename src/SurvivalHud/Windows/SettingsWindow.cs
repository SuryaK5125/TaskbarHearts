using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using SurvivalHud.Settings;
using SurvivalHud.Taskbar;
using SurvivalHud.Textures;

namespace SurvivalHud.Windows;

/// <summary>The small preferences window. Every change is applied and saved immediately.</summary>
public sealed class SettingsWindow : Window
{
    private readonly HudApp _app;
    private readonly ComboBox _textures = new(), _display = new(), _mode = new();
    private readonly TextBlock _textureStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Opacity = 0.75 };
    private readonly CheckBox _startup = new() { Content = "Start automatically with Windows" };
    private readonly CheckBox _pinEnabled = new() { Content = "Clicking an icon pins the HUD open" };
    private readonly Button _align = new() { Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
    private bool _loading;
    private FrameworkElement _iconSize = null!, _spacing = null!, _lightPopup = null!, _cover = null!;
    private Button _reset = null!;

    /// <summary>Greys out a control that has no effect right now and says why on hover.</summary>
    private static void Applies(FrameworkElement e, bool applies, string whyNot)
    {
        e.IsEnabled = applies;
        e.Opacity = applies ? 1 : 0.5;
        e.ToolTip = applies ? null : whyNot;
        ToolTipService.SetShowOnDisabled(e, true);
    }

    private AppSettings S => _app.Settings;

    private sealed record Choice(string Label, string Value)
    {
        public override string ToString() => Label;
    }

    public SettingsWindow(HudApp app)
    {
        _app = app;
        Title = "Taskbar Hearts – Settings";
        SizeToContent = SizeToContent.Height;
        Width = 440;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var root = new StackPanel { Margin = new Thickness(14) };
        _loading = true;

        // ---- Appearance ----
        var appearance = Section(root, "Appearance");
        _iconSize = Slider(appearance, "Taskbar icon size", 1, 4, 0.5, () => S.CompactScale, v => S.CompactScale = v, v => $"{v:0.#}×");
        _spacing = Slider(appearance, "Spacing (Compact)", 0, 12, 1, () => S.IconSpacing, v => S.IconSpacing = v, v => $"{v:0} px");
        Slider(appearance, "Popup size", 1, 5, 0.5, () => S.HudScale, v => S.HudScale = v, v => $"{v:0.#}×");

        appearance.Children.Add(new TextBlock { Text = "Minecraft texture source", Margin = new Thickness(0, 8, 0, 3) });
        appearance.Children.Add(_textures);
        var pick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        pick.Children.Add(MakeButton("Choose JAR or resource pack…", ChooseJar));
        pick.Children.Add(MakeButton("Choose folder…", ChooseFolder));
        appearance.Children.Add(pick);
        appearance.Children.Add(_textureStatus);
        _textures.SelectionChanged += (_, _) =>
        {
            if (_loading || _textures.SelectedItem is not Choice c) return;
            S.TextureSource = c.Value;
            _app.ApplySettings(reloadTextures: true);
        };

        Check(appearance, "Show a gold heart while charging", () => S.ChargingIndicator, v => S.ChargingIndicator = v);
        Check(appearance, "Popup follows the Windows light/dark theme", () => S.PopupFollowsTheme, v => S.PopupFollowsTheme = v);
        _lightPopup = Check(appearance, "Light popup background", () => S.LightPopup, v => S.LightPopup = v);

        // ---- Behaviour ----
        var behaviour = Section(root, "Behaviour");
        behaviour.Children.Add(_startup);
        _startup.Click += (_, _) => { StartupRegistration.Set(_startup.IsChecked == true); Sync(); };
        Slider(behaviour, "Hover delay", 0, 1000, 50, () => S.HoverDelayMs, v => S.HoverDelayMs = (int)v, v => $"{v:0} ms");
        Slider(behaviour, "Popup closing delay", 0, 2000, 50, () => S.CloseDelayMs, v => S.CloseDelayMs = (int)v, v => $"{v:0} ms");
        _pinEnabled.Margin = new Thickness(0, 6, 0, 0);
        _pinEnabled.Click += (_, _) => { S.PinEnabled = _pinEnabled.IsChecked == true; _app.ApplySettings(); };
        behaviour.Children.Add(_pinEnabled);

        // ---- Taskbar ----
        var taskbar = Section(root, "Taskbar");
        taskbar.Children.Add(new TextBlock { Text = "Placement", Margin = new Thickness(0, 0, 0, 3) });
        _mode.Items.Add(new Choice("Overlay on the taskbar (covers the battery area)", AppSettings.ModeOverlay));
        _mode.Items.Add(new Choice("Three notification-area (tray) icons", AppSettings.ModeTray));
        _mode.SelectionChanged += (_, _) =>
        {
            if (_loading || _mode.SelectedItem is not Choice c) return;
            S.Mode = c.Value;
            _app.ApplySettings();
        };
        taskbar.Children.Add(_mode);
        _cover = Check(taskbar, "Hide the native battery icon behind a matching background (Compact)", () => S.CoverNativeBattery, v => S.CoverNativeBattery = v);

        taskbar.Children.Add(new TextBlock { Text = "Display", Margin = new Thickness(0, 8, 0, 3) });
        taskbar.Children.Add(_display);
        _display.SelectionChanged += (_, _) =>
        {
            if (_loading || _display.SelectedItem is not Choice c) return;
            S.TargetDisplay = c.Value;
            _app.ApplySettings();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        _align.Click += (_, _) => _app.SetAlignment(!_app.Aligning, save: true);
        buttons.Children.Add(_align);
        _reset = MakeButton("Reset position", () => _app.ResetPosition());
        buttons.Children.Add(_reset);
        taskbar.Children.Add(buttons);
        taskbar.Children.Add(new TextBlock
        {
            Text = "Alignment mode: drag the yellow box or use the arrow keys (Shift = 10 px), then press Enter to save or Esc to cancel.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 0),
        });

        Content = root;
        Sync();
        _app.StateChanged += Sync;
        Closed += (_, _) => _app.StateChanged -= Sync;
    }

    /// <summary>Refreshes the parts of the window that can change from outside (menu, hotkeys, texture loading).</summary>
    private void Sync()
    {
        _loading = true;
        try
        {
            _startup.IsChecked = StartupRegistration.IsEnabled;
            _pinEnabled.IsChecked = S.PinEnabled;
            _align.Content = _app.Aligning ? "Finish alignment" : "Enter alignment mode";
            bool overlay = S.Mode == AppSettings.ModeOverlay, compact = overlay && S.HudLayout == AppSettings.LayoutCompact;
            bool spot = overlay && S.HudLayout == AppSettings.LayoutBarsSlot;
            _align.IsEnabled = overlay && !spot;
            _align.ToolTip = spot ? "The battery-spot layout positions itself on the network/volume/battery button." : null;
            ToolTipService.SetShowOnDisabled(_align, true);
            Applies(_reset, overlay && !spot, "The battery-spot layout positions itself; there is no offset to reset.");
            Applies(_spacing, compact, "Only used by the Compact layout (heart, hunger, armor).");
            Applies(_cover, compact, "Only used by the Compact layout.");
            Applies(_lightPopup, !S.PopupFollowsTheme, "The popup follows the Windows theme. Untick that option to choose here.");
            Applies(_iconSize, overlay, "Tray-icon mode uses Windows' own icon size.");
            _iconSize.ToolTip = spot ? "In the battery spot the bars are capped to fit the network/volume/battery button." : _iconSize.ToolTip;
            _mode.SelectedIndex = S.Mode == AppSettings.ModeTray ? 1 : 0;

            _textures.Items.Clear();
            _textures.Items.Add(new Choice("Automatic – newest installed Minecraft Java Edition", AppSettings.TexturesAuto));
            _textures.Items.Add(new Choice("Built-in pixel art (no Minecraft files)", AppSettings.TexturesBuiltIn));
            foreach (var v in MinecraftTextureLoader.FindInstalledVersions())
                _textures.Items.Add(new Choice("Minecraft " + v.Name, v.JarPath));
            Select(_textures, S.TextureSource, missing => new Choice("Custom: " + missing, missing));
            _textureStatus.Text = "In use: " + _app.Sprites.Source + (_app.TextureNote != null ? "\n" + _app.TextureNote : "");

            _display.Items.Clear();
            _display.Items.Add(new Choice("Primary display", ""));
            int n = 1;
            foreach (var d in TaskbarLocator.GetDisplays())
                _display.Items.Add(new Choice($"Display {n++} – {d.Bounds.Width}×{d.Bounds.Height}{(d.IsPrimary ? " (primary)" : "")}", d.DeviceName));
            Select(_display, S.TargetDisplay, missing => new Choice("Disconnected: " + missing, missing));
        }
        finally { _loading = false; }
    }

    private static void Select(ComboBox box, string value, Func<string, Choice> forMissing)
    {
        foreach (Choice c in box.Items)
            if (string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = c; return; }
        var extra = forMissing(value);
        box.Items.Add(extra);
        box.SelectedItem = extra;
    }

    private void ChooseJar()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a Minecraft Java Edition JAR or a resource pack",
            Filter = "Minecraft JAR or resource pack (*.jar;*.zip)|*.jar;*.zip|All files (*.*)|*.*",
        };
        if (Directory.Exists(MinecraftTextureLoader.VersionsDirectory)) dlg.InitialDirectory = MinecraftTextureLoader.VersionsDirectory;
        if (dlg.ShowDialog(this) == true) UseCustomSource(dlg.FileName);
    }

    private void ChooseFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder that contains the HUD sprite PNGs" };
        if (dlg.ShowDialog(this) == true) UseCustomSource(dlg.FolderName);
    }

    private void UseCustomSource(string path)
    {
        if (MinecraftTextureLoader.TryLoad(path, path, out string? error) == null)
        {
            MessageBox.Show(this, error ?? "No Minecraft HUD sprites were found there.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        S.TextureSource = path;
        _app.ApplySettings(reloadTextures: true);
    }

    // ---- small layout helpers -----------------------------------------------------------

    private static StackPanel Section(Panel parent, string title)
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        parent.Children.Add(new GroupBox
        {
            Header = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold },
            Content = panel, Margin = new Thickness(0, 0, 0, 10),
        });
        return panel;
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private CheckBox Check(Panel parent, string text, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Content = text, IsChecked = get(), Margin = new Thickness(0, 6, 0, 0) };
        box.Click += (_, _) => { set(box.IsChecked == true); _app.ApplySettings(); };
        parent.Children.Add(box);
        return box;
    }

    private Grid Slider(Panel parent, string label, double min, double max, double step,
        Func<double> get, Action<double> set, Func<double, string> format)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var value = new TextBlock { Text = format(get()), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = get(), TickFrequency = step, IsSnapToTickEnabled = true,
            SmallChange = step, LargeChange = step, TickPlacement = TickPlacement.None, VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, e) =>
        {
            value.Text = format(e.NewValue);
            if (_loading) return;
            set(e.NewValue);
            _app.ApplySettings();
        };
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(value, 2);
        grid.Children.Add(text);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        parent.Children.Add(grid);
        return grid;
    }
}
