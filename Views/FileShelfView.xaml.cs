using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Views;

/// <summary>
/// The file shelf. Raises events for every action; <see cref="Providers.FileShelfProvider"/> does the work.
/// Tiles can be double-clicked to open, or dragged out into other apps.
/// </summary>
public partial class FileShelfView : IslandView
{
    private const double TileWidth = 88;

    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IReadOnlyList<string> _items = [];
    private bool _dropHint;
    private Point? _pressAt;

    public event Action<string[]>? CopyRequested;
    public event Action<string[]>? CopyPathRequested;
    public event Action<string>? OpenRequested;
    public event Action<string>? RevealRequested;
    public event Action<string>? RemoveRequested;
    public event Action? ClearRequested;

    /// <summary>A drag out of the shelf ended (dropped somewhere or cancelled).</summary>
    public event Action? DragOutFinished;

    public override Size CompactSize => new(300, 36);
    public override Size ExpandedSize => new(520, 196);
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public FileShelfView()
    {
        InitializeComponent();
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Text = CountLabel(_items.Count);
            StatusText.Foreground = (Brush)FindResource("TextSecondary");
        };
        Populate([]);
    }

    /// <summary>Rebuilds the tiles from the shelf's paths (newest first).</summary>
    public void Populate(IReadOnlyList<string> items)
    {
        _items = items;
        foreach (var stale in _icons.Keys.Where(k => !items.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            _icons.Remove(stale);

        Tiles.Children.Clear();
        foreach (var path in items) Tiles.Children.Add(CreateTile(path));

        CompactCount.Text = CountLabel(items.Count);
        CompactName.Text = items.Count > 0 ? DisplayName(items[0]) : "";
        if (!_statusTimer.IsEnabled) StatusText.Text = CountLabel(items.Count);
        HeaderActions.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDropZone();
    }

    /// <summary>Shows the dashed drop zone over the tiles while files are being dragged in.</summary>
    public void ShowDropHint(bool on)
    {
        _dropHint = on;
        UpdateDropZone();
    }

    /// <summary>Briefly replaces the file count in the header with a message ("Copied", "Added 3 files").</summary>
    public void Flash(string message, Brush? brush = null)
    {
        StatusText.Text = message;
        StatusText.Foreground = brush ?? (Brush)FindResource("AccentGreen");
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void UpdateDropZone()
    {
        var show = _dropHint || _items.Count == 0;
        DropZone.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DropZone.Background = new SolidColorBrush(Color.FromArgb((byte)(_items.Count == 0 && !_dropHint ? 0x14 : 0xD8), 0x1C, 0x1C, 0x1E));
        DropZoneText.Text = _dropHint ? "Drop to add to the shelf" : "Drop files here";
    }

    private static string CountLabel(int count) => count == 1 ? "1 file" : $"{count} files";

    private static string DisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name; // drive roots have no file name
    }

    private FrameworkElement CreateTile(string path)
    {
        if (!_icons.TryGetValue(path, out var icon)) _icons[path] = icon = FileActions.GetIcon(path);

        FrameworkElement picture = icon is null
            ? Glyph(Directory.Exists(path) ? "" : "", 30)
            : new Image { Source = icon, Stretch = Stretch.Uniform, MaxWidth = 52, MaxHeight = 48 };
        picture.HorizontalAlignment = HorizontalAlignment.Center;
        picture.VerticalAlignment = VerticalAlignment.Center;
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);

        var name = new TextBlock
        {
            Text = DisplayName(path),
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 30,
            Margin = new Thickness(4, 4, 4, 0),
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0,
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                ActionButton("", "Copy file", () => CopyRequested?.Invoke([path])),
                ActionButton("", "Copy path", () => CopyPathRequested?.Invoke([path])),
                ActionButton("", "Show in folder", () => RevealRequested?.Invoke(path)),
                ActionButton("", "Remove from shelf", () => RemoveRequested?.Invoke(path)),
            },
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(name, 1);
        Grid.SetRow(actions, 2);
        layout.Children.Add(picture);
        layout.Children.Add(name);
        layout.Children.Add(actions);

        var idle = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        var hot = new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF));
        var tile = new Border
        {
            Width = TileWidth,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(2, 8, 2, 6),
            CornerRadius = new CornerRadius(14),
            Background = idle,
            Cursor = Cursors.Hand,
            ToolTip = path,
            Child = layout,
        };
        ToolTipService.SetInitialShowDelay(tile, 700);

        tile.MouseEnter += (_, _) =>
        {
            tile.Background = hot;
            actions.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        };
        tile.MouseLeave += (_, _) =>
        {
            tile.Background = idle;
            actions.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            _pressAt = null;
        };
        // Buttons mark their own presses handled, so these only see presses on the tile itself.
        tile.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                _pressAt = null;
                OpenRequested?.Invoke(path);
                return;
            }
            _pressAt = e.GetPosition(tile);
        };
        tile.MouseLeftButtonUp += (_, _) => _pressAt = null;
        tile.MouseMove += (_, e) =>
        {
            if (_pressAt is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(tile) - start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _pressAt = null;
            if (!FileShelf.Exists(path))
            {
                Flash("File is gone", (Brush)FindResource("AccentRed"));
                return;
            }
            DragDrop.DoDragDrop(tile, FileActions.CreateDragData([path]), DragDropEffects.Copy | DragDropEffects.Link);
            tile.Background = idle;
            actions.Opacity = 0;
            DragOutFinished?.Invoke();
        };
        return tile;
    }

    private static TextBlock Glyph(string glyph, double size)
    {
        var text = new TextBlock { Text = glyph, FontSize = size };
        text.SetResourceReference(StyleProperty, "Icon");
        return text;
    }

    private static Button ActionButton(string glyph, string tip, Action run)
    {
        var button = new Button { Content = glyph, ToolTip = tip, Width = 20, Height = 20, FontSize = 11 };
        button.SetResourceReference(StyleProperty, "IslandButton");
        button.Click += (_, e) =>
        {
            e.Handled = true;
            run();
        };
        return button;
    }

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(_items.ToArray());
    private void CopyAllPaths_Click(object sender, RoutedEventArgs e) => CopyPathRequested?.Invoke(_items.ToArray());
    private void Clear_Click(object sender, RoutedEventArgs e) => ClearRequested?.Invoke();
}
