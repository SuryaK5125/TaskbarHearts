using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SurvivalHud.Core;
using SurvivalHud.Shell;
using SurvivalHud.Textures;

namespace SurvivalHud.Windows;

/// <summary>
/// Picks the decorative items for the custom hotbar. Click or drag an item from the grid to put
/// it in the next free slot (or drop it on a slot to insert it there, shifting the others
/// right). Drag a slot along the hotbar to move it; drag it back to the grid or right-click it
/// to take it out. Every change applies to the popup straight away.
/// </summary>
public sealed class HotbarEditorWindow : Window
{
    private const int S = 2;              // the hotbar is drawn at twice its game size
    private const int Max = 9;
    private const string ItemTag = "item:", SlotTag = "slot:";

    private static readonly Brush Bg = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)));
    private static readonly Brush Card = Freeze(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x29)));
    private static readonly Brush Hover = Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x40)));
    private static readonly Brush Edge = Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x50)));
    private static readonly Brush Text = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)));
    private static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA0)));

    private readonly HudApp _app;
    private readonly string? _source;
    private readonly string[] _items;   // nine slots, "" = empty
    private readonly Dictionary<string, BitmapSource> _icons = new();
    private readonly Canvas _bar = new() { Width = 182 * S, Height = 22 * S, Background = Brushes.Transparent, AllowDrop = true };
    private readonly Image[] _slotImages = new Image[Max];
    private readonly TextBlock[] _slotCounts = new TextBlock[Max], _slotShadows = new TextBlock[Max];
    private readonly Rectangle _marker = new()
    {
        Width = 24 * S, Height = 24 * S, Stroke = Brushes.White, StrokeThickness = S, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
    };
    private readonly WrapPanel _grid = new() { Margin = new Thickness(4) };
    private readonly TextBox _search;
    private readonly TextBlock _status = new() { Foreground = Dim, FontSize = 12, Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly List<(string Id, string Name, FrameworkElement Cell)> _cells = new();

    private Point _down;
    private string? _pressId;
    private int _pressSlot = -1;
    private bool _closed;

    public HotbarEditorWindow(HudApp app)
    {
        _app = app;
        _source = app.Sprites.ItemSource;
        _items = HotbarLayout.FromList(app.Settings.HotbarItems);

        Title = "Custom Hotbar";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        Background = Bg;
        Foreground = Text;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        UseLayoutRounding = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = MainWindow.ToBitmap(AppTrayIcon.Art(), 2);

        var root = new StackPanel { Margin = new Thickness(16, 12, 16, 12), AllowDrop = true };
        root.Drop += (_, e) => { if (Data(e) is { } d && d.StartsWith(SlotTag)) Remove(int.Parse(d[SlotTag.Length..])); };

        root.Children.Add(Heading("Your Hotbar"));
        var hotbar = _app.Sprites.Game?.Hotbar ?? FallbackSprites.Hotbar();
        var barImage = MainWindow.PixelImage(MainWindow.ToBitmap(hotbar, 1), 0);
        barImage.Width = 182 * S;
        barImage.Height = 22 * S;
        _bar.Children.Add(barImage);
        for (int i = 0; i < Max; i++)
        {
            var img = new Image { Width = 16 * S, Height = 16 * S, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(img, (3 + i * 20) * S);
            Canvas.SetTop(img, 3 * S);
            _bar.Children.Add(img);
            _slotImages[i] = img;
            // Stack count in the slot's bottom-right corner, with the game's dark drop shadow.
            for (int k = 1; k >= 0; k--)
            {
                var t = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.Bold, FontSize = 14, IsHitTestVisible = false,
                    Foreground = k == 1 ? new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x3F)) : Brushes.White,
                    Width = 18 * S, TextAlignment = TextAlignment.Right,
                };
                Canvas.SetLeft(t, (1 + i * 20) * S + k * 2);
                Canvas.SetTop(t, 10 * S + k * 2);
                _bar.Children.Add(t);
                if (k == 1) _slotShadows[i] = t; else _slotCounts[i] = t;
            }
        }
        Canvas.SetTop(_marker, -S);
        _bar.Children.Add(_marker);
        _bar.Cursor = Cursors.Hand;
        _bar.MouseLeftButtonDown += (_, e) =>
        {
            int slot = SlotAt(e.GetPosition(_bar).X);
            _pressSlot = _items[slot].Length > 0 ? slot : -1;
            _down = e.GetPosition(this);
        };
        _bar.MouseLeftButtonUp += (_, _) => _pressSlot = -1;
        _bar.MouseMove += (_, e) =>
        {
            if (_pressSlot < 0 || e.LeftButton != MouseButtonState.Pressed || !Moved(e)) return;
            int from = _pressSlot;
            _pressSlot = -1;
            DragDrop.DoDragDrop(_bar, SlotTag + from, DragDropEffects.Move);
            _marker.Visibility = Visibility.Collapsed;
        };
        _bar.MouseRightButtonUp += (_, e) => Remove(SlotAt(e.GetPosition(_bar).X));
        _bar.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            int slot = SlotAt(e.GetPosition(_bar).X);
            if (_items[slot].Length == 0) return;
            if (HotbarLayout.ToggleStack(_items, slot)) Commit();
            else _status.Text = $"{ItemLibrary.Pretty(HotbarLayout.Parse(_items[slot]).Id)} doesn't stack in the game.";
        };
        _bar.DragOver += (_, e) =>
        {
            int? target = Target(e);
            e.Effects = target is null ? DragDropEffects.None : Data(e)!.StartsWith(SlotTag) ? DragDropEffects.Move : DragDropEffects.Copy;
            if (target is int t) { Canvas.SetLeft(_marker, (t * 20 - 1) * S); _marker.Visibility = Visibility.Visible; }
            e.Handled = true;
        };
        _bar.DragLeave += (_, _) => _marker.Visibility = Visibility.Collapsed;
        _bar.Drop += (_, e) =>
        {
            _marker.Visibility = Visibility.Collapsed;
            if (Target(e) is not int t || Data(e) is not { } d) return;
            e.Handled = true;
            if (d.StartsWith(SlotTag)) HotbarLayout.Move(_items, int.Parse(d[SlotTag.Length..]), t);
            else HotbarLayout.Place(_items, t, d[ItemTag.Length..]);
            Commit();
        };
        root.Children.Add(new Border { Child = _bar, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) });
        root.Children.Add(_status);

        root.Children.Add(Heading("Items"));
        _search = new TextBox
        {
            Background = Card, Foreground = Text, CaretBrush = Text, BorderBrush = Edge, BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4), FontSize = 13,
        };
        var searchHint = new TextBlock { Text = "Search items", Foreground = Dim, IsHitTestVisible = false, Margin = new Thickness(9, 5, 0, 0) };
        _search.TextChanged += (_, _) =>
        {
            searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Filter();
        };
        var searchBox = new Grid();
        searchBox.Children.Add(_search);
        searchBox.Children.Add(searchHint);
        root.Children.Add(searchBox);

        _grid.Children.Add(new TextBlock { Text = "Loading items...", Foreground = Dim, Margin = new Thickness(6) });
        var scroll = new ScrollViewer
        {
            Content = _grid, Height = 300, Margin = new Thickness(0, 6, 0, 0), Background = Card,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        root.Children.Add(scroll);

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var done = MainWindow.LinkButton("Done", Close);
        DockPanel.SetDock(done, Dock.Right);
        footer.Children.Add(done);
        footer.Children.Add(MainWindow.LinkButton("Clear hotbar", () => { Array.Fill(_items, ""); Commit(); }, HorizontalAlignment.Left));
        root.Children.Add(footer);

        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => _closed = true;

        foreach (var (id, sprite) in ItemLibrary.Load(_source, _items.Select(e => HotbarLayout.Parse(e).Id))) _icons[id] = MainWindow.ToBitmap(sprite, 1);
        RefreshBar();
        LoadGridAsync();
    }

    // ---- hotbar --------------------------------------------------------------------------------

    private static int SlotAt(double x) => Math.Clamp((int)((x / S - 1) / 20), 0, Max - 1);

    /// <summary>Where a drop would land, or null when it can't go anywhere.</summary>
    private int? Target(DragEventArgs e)
    {
        if (Data(e) is null) return null;
        return SlotAt(e.GetPosition(_bar).X);
    }

    private void Add(string id)
    {
        int free = HotbarLayout.FirstFree(_items);
        if (free < 0) { _status.Text = "The hotbar is full. Drop an item on a slot to swap it in."; return; }
        _items[free] = id;
        Commit();
    }

    private void Remove(int slot)
    {
        if (slot < 0 || slot >= Max || _items[slot].Length == 0) return;
        _items[slot] = "";   // the others stay where they are
        Commit();
    }

    private void Commit()
    {
        RefreshBar();
        _app.SetHotbarItems(HotbarLayout.ToList(_items));
    }

    private void RefreshBar()
    {
        for (int i = 0; i < Max; i++)
        {
            var (id, count) = HotbarLayout.Parse(_items[i]);
            _slotImages[i].Source = id.Length > 0 && _icons.TryGetValue(id, out var b) ? b : null;
            _slotCounts[i].Text = _slotShadows[i].Text = id.Length > 0 && count > 1 ? count.ToString() : "";
        }
        int used = _items.Count(i => i.Length > 0);
        _bar.ToolTip = used == 0 ? "Empty" : string.Join(", ", _items.Where(i => i.Length > 0).Select(i => ItemLibrary.Pretty(HotbarLayout.Parse(i).Id)));
        _status.Text = used == 0
            ? "Click an item below to add it (middle-click for a full stack), or drag it to any slot."
            : $"{used} of {Max} slots used. Drag to move, middle-click for a full stack, right-click to remove.";
    }

    // ---- item grid -----------------------------------------------------------------------------

    private void LoadGridAsync()
    {
        string? source = _source;
        Task.Run(() =>
        {
            var ids = ItemLibrary.List(source);
            var sprites = ItemLibrary.Load(source, ids);
            return ids.Where(sprites.ContainsKey).Select(id => (id, ItemLibrary.Pretty(id), MainWindow.ToBitmap(sprites[id], 1))).ToList();
        }).ContinueWith(t =>
        {
            if (_closed) return;
            _grid.Children.Clear();
            if (t.Status != TaskStatus.RanToCompletion || t.Result.Count == 0)
            {
                _grid.Children.Add(new TextBlock { Text = "No items found.", Foreground = Dim, Margin = new Thickness(6) });
                return;
            }
            foreach (var (id, name, bmp) in t.Result)
            {
                _icons.TryAdd(id, bmp);
                var cell = Cell(id, name, bmp);
                _cells.Add((id, name, cell));
                _grid.Children.Add(cell);
            }
            if (_source == null)
                _grid.Children.Add(new TextBlock
                {
                    Text = "Install Minecraft Java Edition to choose from every item in the game.",
                    Foreground = Dim, FontSize = 12, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap, Width = 340,
                });
            RefreshBar();
            Filter();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private FrameworkElement Cell(string id, string name, BitmapSource bmp)
    {
        var cell = new Border
        {
            Width = 40, Height = 40, Background = Brushes.Transparent, CornerRadius = new CornerRadius(2),
            Child = MainWindow.PixelImage(bmp, 32), ToolTip = name, Cursor = Cursors.Hand,
        };
        cell.MouseEnter += (_, _) => cell.Background = Hover;
        cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
        cell.MouseLeftButtonDown += (_, e) => { _pressId = id; _down = e.GetPosition(this); };
        cell.MouseLeftButtonUp += (_, _) => { if (_pressId == id) Add(id); _pressId = null; };
        cell.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) Add(HotbarLayout.Format(id, ItemNames.MaxStack(id))); };
        cell.MouseMove += (_, e) =>
        {
            if (_pressId != id || e.LeftButton != MouseButtonState.Pressed || !Moved(e)) return;
            _pressId = null;
            DragDrop.DoDragDrop(cell, ItemTag + id, DragDropEffects.Copy);
            _marker.Visibility = Visibility.Collapsed;
        };
        return cell;
    }

    private void Filter()
    {
        string q = _search.Text.Trim();
        foreach (var (id, name, cell) in _cells)
            cell.Visibility = q.Length == 0 || name.Contains(q, StringComparison.OrdinalIgnoreCase) || id.Contains(q.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- helpers -------------------------------------------------------------------------------

    private bool Moved(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return Math.Abs(p.X - _down.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(p.Y - _down.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private static string? Data(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.StringFormat) && e.Data.GetData(DataFormats.StringFormat) is string s
        && (s.StartsWith(ItemTag) || s.StartsWith(SlotTag)) ? s : null;

    private static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 12, Foreground = Dim, Margin = new Thickness(2, 10, 0, 4),
    };

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
