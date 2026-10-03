using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Views;

public partial class QuickPanelView : IslandView
{
    private const double TileHeight = 46;

    private int _toggleRows;

    public event Action<int>? TimerRequested;
    public event Action? CancelTimerRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public override Size CompactSize => new(124, 32);
    public override Size ExpandedSize => new(420, 60 + (_toggleRows > 0 ? 8 + _toggleRows * (TileHeight + 6) : 0));
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public QuickPanelView() => InitializeComponent();

    /// <summary>Rebuilds the panel; call before showing so the island sizes itself to the content.</summary>
    public void Populate(IEnumerable<int> presets, bool timerRunning, IReadOnlyList<ToggleItem> toggles)
    {
        TimerChips.Children.Clear();
        foreach (var minutes in presets.Take(4))
        {
            var chip = new Button { Style = (Style)FindResource("ChipButton"), Content = FormatMinutes(minutes) };
            chip.Click += (_, _) => TimerRequested?.Invoke(minutes);
            TimerChips.Children.Add(chip);
        }
        CancelTimerButton.Visibility = timerRunning ? Visibility.Visible : Visibility.Collapsed;

        Toggles.Children.Clear();
        foreach (var item in toggles) Toggles.Children.Add(CreateTile(item));
        _toggleRows = (toggles.Count + 1) / 2;
    }

    private static Border CreateTile(ToggleItem item)
    {
        var icon = new TextBlock { Text = item.Icon, FontSize = 15, Margin = new Thickness(0, 0, 10, 0) };
        icon.SetResourceReference(StyleProperty, "Icon");
        var label = new TextBlock { Text = item.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var editor = item.CreateEditor();
        editor.Margin = new Thickness(8, 0, 0, 0);

        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(editor, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(editor);
        row.Children.Add(label);

        var tile = new Border
        {
            Height = TileHeight,
            Margin = new Thickness(3),
            Padding = new Thickness(12, 0, 10, 0),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = item.Description,
            Child = row,
        };
        // The whole tile toggles; the switch itself handles its own clicks.
        tile.MouseLeftButtonUp += (_, _) => item.Value = !item.Value;
        return tile;
    }

    private void CancelTimer_Click(object sender, RoutedEventArgs e) => CancelTimerRequested?.Invoke();
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}
