using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SurvivalHud.Core;
using SurvivalHud.Native;
using SurvivalHud.Settings;
using SurvivalHud.Shell;

namespace SurvivalHud.Windows;

/// <summary>
/// The app's main window, opened by clicking the crafting-table icon in the notification area.
/// Compact and dark: a HUD on/off switch, live readings, the three taskbar layouts as rows with
/// real pixel previews, which bars to show, the popup style, Start with Windows, and links to
/// the advanced settings, diagnostics and Quit. It only exists while open.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly Brush Bg = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)));
    private static readonly Brush Card = Freeze(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x29)));
    private static readonly Brush RowHover = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2F)));
    private static readonly Brush Selected = Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x40)));       // Minecraft-style grey
    private static readonly Brush SelectedEdge = Freeze(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA6)));
    private static readonly Brush Line = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3E)));
    private static readonly Brush Text = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)));
    private static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA0)));
    private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0x5D, 0xB8, 0x3A)));         // grass green, used sparingly

    private readonly HudApp _app;
    private readonly Switch _hudSwitch, _startupSwitch, _gameSwitch;
    private readonly TextBlock _stats = new() { Foreground = Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly List<(string Layout, Border Row, Image Preview)> _layouts = new();
    private readonly ComboBox _rows;
    private readonly Border _transparentNote;
    private bool _syncing;

    public MainWindow(HudApp app)
    {
        _app = app;
        Title = "Taskbar Hearts";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;   // so its taskbar button minimises and restores it
        Background = Bg;
        Foreground = Text;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        UseLayoutRounding = true;
        ShowInTaskbar = true;
        Icon = ToBitmap(AppTrayIcon.Art(), 2);

        var root = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

        // ---- header: icon, name, HUD switch, live readings ----
        var header = new DockPanel { LastChildFill = true };
        _hudSwitch = new Switch(on => _app.SetHudVisible(on)) { ToolTip = "Show on taskbar" };
        DockPanel.SetDock(_hudSwitch, Dock.Right);
        header.Children.Add(_hudSwitch);
        var logo = PixelImage(ToBitmap(AppTrayIcon.Art(), 1), 24);
        logo.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(logo, Dock.Left);
        header.Children.Add(logo);
        header.Children.Add(new TextBlock { Text = "Taskbar Hearts", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(header);
        _stats.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(_stats);

        // ---- layouts: compact selectable rows ----
        root.Children.Add(Heading("Layout"));
        AddLayout(root, AppSettings.LayoutCompact, "Compact");
        AddLayout(root, AppSettings.LayoutBars, "Bars");
        AddLayout(root, AppSettings.LayoutBarsSlot, "Battery spot");

        // Which bars the two bar layouts show: one dropdown.
        _rows = DarkComboBox();
        foreach (var (value, text) in new[]
        {
            (AppSettings.RowsArmorHearts, "Hearts + Armor"), (AppSettings.RowsHearts, "Hearts only"),
            (AppSettings.RowsHeartsHunger, "Hearts + Hunger"), (AppSettings.RowsArmorHunger, "Armor + Hunger"),
        })
            _rows.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        _rows.Width = 160;
        _rows.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _rows.SelectedItem is ComboBoxItem { Tag: string v }) _app.SetBarsRows(v);
        };
        var barsRow = Row("Bars show", _rows);
        barsRow.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(barsRow);

        // Shown only when the taskbar looks see-through and the layout needs a solid patch.
        var noteAction = new TextBlock
        {
            Text = "Switch to Bars", Foreground = Accent, FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand, Margin = new Thickness(0, 4, 0, 0),
        };
        noteAction.MouseLeftButtonUp += (_, _) => _app.SetHudLayout(AppSettings.LayoutBars);
        var noteBody = new StackPanel();
        noteBody.Children.Add(new TextBlock
        {
            Text = "Your taskbar looks transparent, so this layout shows a solid block. Bars has no background.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        noteBody.Children.Add(noteAction);
        _transparentNote = new Border
        {
            Background = Card, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(10, 6, 10, 6), Child = noteBody, Visibility = Visibility.Collapsed,
        };
        root.Children.Add(_transparentNote);

        // ---- options ----
        root.Children.Add(Heading("Options"));
        _gameSwitch = new Switch(on => _app.SetPopupStyle(on ? AppSettings.PopupGame : AppSettings.PopupList));
        var gameRow = Row("Minecraft-style hover popup", _gameSwitch);
        gameRow.ToolTip = "Laid out like the game, with a hotbar of extra readings. Hover anything in it for details.";
        root.Children.Add(gameRow);
        _startupSwitch = new Switch(on => { StartupRegistration.Set(on); Sync(); });
        root.Children.Add(Row("Start with Windows", _startupSwitch));

        // ---- footer ----
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var quit = LinkButton("Quit", () => Application.Current.Shutdown());
        DockPanel.SetDock(quit, Dock.Right);
        footer.Children.Add(quit);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(LinkButton("Advanced settings", () => _app.ShowSettings(), HorizontalAlignment.Left));
        TextBlock? copy = null;
        copy = (TextBlock)LinkButton("Copy diagnostics", () =>
        {
            try { Clipboard.SetText(_app.BuildDiagnostics()); copy!.Text = "Copied"; }
            catch { copy!.Text = "Copy failed"; }
        }, HorizontalAlignment.Left);
        copy.Margin = new Thickness(14, 0, 0, 0);
        copy.ToolTip = "Copies a report for bug reports: Windows version, taskbar setup and what the app detected. No user name or folders.";
        left.Children.Add(copy);
        footer.Children.Add(left);
        root.Children.Add(footer);

        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !_rows.IsDropDownOpen) Close(); };   // Esc in the dropdown only closes the dropdown
        SourceInitialized += (_, _) => UseDarkTitleBar();
        Loaded += (_, _) => PlaceAboveTray();

        Sync();
        UpdateReadings();
        _app.StateChanged += Sync;
        _app.Ticked += UpdateReadings;
        Closed += (_, _) => { _app.StateChanged -= Sync; _app.Ticked -= UpdateReadings; };
    }

    // ---- state ------------------------------------------------------------------------------

    private void Sync()
    {
        _syncing = true;
        try
        {
            _hudSwitch.IsOn = _app.Settings.HudVisible;
            _startupSwitch.IsOn = StartupRegistration.IsEnabled;
            _gameSwitch.IsOn = _app.Settings.PopupStyle == AppSettings.PopupGame;
            foreach (ComboBoxItem item in _rows.Items)
                if ((string)item.Tag == _app.Settings.BarsRows) _rows.SelectedItem = item;
            _transparentNote.Visibility = _app.TaskbarLooksTransparent && _app.Settings.HudLayout != AppSettings.LayoutBars
                ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (layout, row, preview) in _layouts)
            {
                bool selected = _app.Settings.HudLayout == layout;
                row.Background = selected ? Selected : Brushes.Transparent;
                row.BorderBrush = selected ? SelectedEdge : Brushes.Transparent;
                preview.Source = Preview(layout);
            }
        }
        finally { _syncing = false; }
    }

    private void UpdateReadings()
    {
        var m = _app.CurrentMetrics;
        string battery = !m.HasBattery ? "AC power" : m.BatteryPercent is int b ? $"Battery {b}%" + (m.Charging ? " (charging)" : "") : "Battery —";
        string cpu = m.CpuPercent is double c ? $"CPU {Math.Round(c):0}%" : "CPU …";
        string ram = m.MemFreePercent is double r ? $"RAM {Math.Round(r):0}% free" : "RAM —";
        string net = _app.NetRate is { } n ? Rate(n.Down) + " down" : "";
        _stats.Text = string.Join("  ·  ", new[] { battery, cpu, ram, net }.Where(x => x.Length > 0));
    }

    private static string Rate(double bytesPerSec)
    {
        double bits = bytesPerSec * 8;
        return bits >= 1e9 ? $"{bits / 1e9:0.0} Gb/s" : bits >= 1e6 ? $"{bits / 1e6:0.#} Mb/s" : $"{bits / 1e3:0} kb/s";
    }

    // ---- layout rows ------------------------------------------------------------------------

    private void AddLayout(Panel root, string layout, string name)
    {
        var preview = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
        DockPanel.SetDock(preview, Dock.Right);
        var content = new DockPanel();
        content.Children.Add(preview);
        content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });

        var row = new Border
        {
            Height = 34, Padding = new Thickness(10, 0, 10, 0), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 0, 2), Child = content, Cursor = Cursors.Hand,
        };
        row.MouseEnter += (_, _) => { if (_app.Settings.HudLayout != layout) row.Background = RowHover; };
        row.MouseLeave += (_, _) => { if (_app.Settings.HudLayout != layout) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (!_app.Settings.HudVisible) _app.SetHudVisible(true);
            if (_app.Settings.Mode != AppSettings.ModeOverlay)
            {
                // The layouts are taskbar overlays; picking one leaves the tray-icon fallback mode.
                _app.Settings.Mode = AppSettings.ModeOverlay;
                _app.ApplySettings();
            }
            _app.SetHudLayout(layout);
        };
        root.Children.Add(row);
        _layouts.Add((layout, row, preview));
    }

    /// <summary>A real rendering of the layout with the current textures and readings, at 1× pixel size.</summary>
    private BitmapSource Preview(string layout)
    {
        var m = _app.CurrentMetrics;
        var set = _app.Sprites;
        const int px = 1;
        Canvas32 c;
        if (layout == AppSettings.LayoutCompact)
        {
            var st = new CompactState(m.BatteryPercent / 100.0,
                m.CpuPercent is double cpu ? FillMapping.HungerFromCpu(cpu) / 100.0 : null, m.MemFreePercent / 100.0, false);
            c = CompactRenderer.Render(set, st, CompactRenderer.ContentWidth(2, 2), BarRenderer.Cell * 2, 2, 2, null, false);
        }
        else
        {
            var rows = _app.BarRows();
            c = SurvivalBarsRenderer.RenderRows(set, rows,
                SurvivalBarsRenderer.ContentWidth(px), SurvivalBarsRenderer.ContentHeight(px, rows.Count), px, false);
        }
        return ToBitmap(c);
    }

    // ---- building blocks ----------------------------------------------------------------------

    private static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 12, Foreground = Dim, Margin = new Thickness(2, 14, 0, 4),
    };

    private static FrameworkElement Row(string label, FrameworkElement control)
    {
        var d = new DockPanel();
        DockPanel.SetDock(control, Dock.Right);
        d.Children.Add(control);
        d.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return new Border { Padding = new Thickness(2, 5, 0, 5), Background = Brushes.Transparent, Child = d };
    }

    private static FrameworkElement LinkButton(string text, Action click, HorizontalAlignment align = HorizontalAlignment.Right)
    {
        var t = new TextBlock { Text = text, Foreground = Dim, FontSize = 12, Cursor = Cursors.Hand, HorizontalAlignment = align, Padding = new Thickness(2) };
        t.MouseEnter += (_, _) => t.Foreground = Text;
        t.MouseLeave += (_, _) => t.Foreground = Dim;
        t.MouseLeftButtonUp += (_, _) => click();
        return t;
    }

    /// <summary>A plain dark dropdown (WPF's default one is light-only).</summary>
    private static ComboBox DarkComboBox()
    {
        const string xaml = """
            <ComboBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                      Foreground="#F2F2F2" FontSize="12" Height="26">
              <ComboBox.Template>
                <ControlTemplate TargetType="ComboBox">
                  <Grid>
                    <ToggleButton Focusable="False" ClickMode="Press"
                        IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                      <ToggleButton.Template>
                        <ControlTemplate TargetType="ToggleButton">
                          <Border x:Name="B" Background="#2A2A2D" BorderBrush="#4A4A50" BorderThickness="1" CornerRadius="2">
                            <Path HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,9,0"
                                  Data="M0,0 L4,4 L8,0" Stroke="#9A9AA0" StrokeThickness="1.4"/>
                          </Border>
                          <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="B" Property="Background" Value="#323236"/></Trigger>
                          </ControlTemplate.Triggers>
                        </ControlTemplate>
                      </ToggleButton.Template>
                    </ToggleButton>
                    <ContentPresenter IsHitTestVisible="False" Margin="9,0,26,0" VerticalAlignment="Center"
                        Content="{TemplateBinding SelectionBoxItem}"/>
                    <Popup IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True"
                           Focusable="False" PopupAnimation="None">
                      <Border Background="#2A2A2D" BorderBrush="#4A4A50" BorderThickness="1"
                              MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                        <StackPanel IsItemsHost="True" Margin="0,2"/>
                      </Border>
                    </Popup>
                  </Grid>
                </ControlTemplate>
              </ComboBox.Template>
              <ComboBox.ItemContainerStyle>
                <Style TargetType="ComboBoxItem">
                  <Setter Property="Foreground" Value="#F2F2F2"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="ComboBoxItem">
                        <Border x:Name="I" Padding="9,4" Background="Transparent"><ContentPresenter/></Border>
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="I" Property="Background" Value="#3C3C40"/></Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </ComboBox.ItemContainerStyle>
            </ComboBox>
            """;
        return (ComboBox)XamlReader.Parse(xaml);
    }

    private static Image PixelImage(BitmapSource src, double size)
    {
        var img = new Image { Source = src, Width = size, Height = size };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        return img;
    }

    private static BitmapSource ToBitmap(Sprite s, int scale)
    {
        var c = new Canvas32(s.Width * scale, s.Height * scale);
        c.Blit(s, 0, 0, c.Width, c.Height);
        return ToBitmap(c);
    }

    private static BitmapSource ToBitmap(Canvas32 c)
    {
        var px = new int[c.Pixels.Length];
        c.CopyPremultiplied(px);
        var b = BitmapSource.Create(c.Width, c.Height, 96, 96, PixelFormats.Pbgra32, null, px, c.Width * 4);
        b.Freeze();
        return b;
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    /// <summary>A small, quiet on/off toggle in Windows style: light grey when on, outlined when off.</summary>
    private sealed class Switch : Border
    {
        private static readonly Brush On = Freeze(new SolidColorBrush(Color.FromRgb(0xD6, 0xD6, 0xDA)));
        private static readonly Brush Knob = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)));
        private readonly Ellipse _knob = new() { Width = 10, Height = 10 };
        private bool _on;

        public Switch(Action<bool> changed)
        {
            Width = 34; Height = 18;
            CornerRadius = new CornerRadius(9);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            Child = _knob;
            MouseLeftButtonUp += (_, _) => { IsOn = !IsOn; changed(IsOn); };
            IsOn = false;
        }

        public bool IsOn
        {
            get => _on;
            set
            {
                _on = value;
                Background = value ? On : Brushes.Transparent;
                BorderBrush = value ? On : Dim;
                _knob.Fill = value ? Knob : Dim;
                _knob.HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                _knob.Margin = new Thickness(3, 0, 3, 0);
            }
        }
    }

    // ---- window chrome ----------------------------------------------------------------------

    private void UseDarkTitleBar()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
    }

    /// <summary>Opens just above the notification area, like Windows' own flyouts.</summary>
    private void PlaceAboveTray()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 12;
        Top = work.Bottom - ActualHeight - 12;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
