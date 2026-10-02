using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Views;

public partial class StatusView : IslandView
{
    public override bool CanExpand => false;
    public override Size CompactSize => new(320, 36);
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public StatusView() => InitializeComponent();

    /// <param name="level">0..1, or null to hide the bar.</param>
    public void Set(string glyph, string label, double? level, string value, Brush accent)
    {
        IconText.Text = glyph;
        IconText.Foreground = accent;
        LabelText.Text = label;
        LevelBar.Visibility = level is null ? Visibility.Collapsed : Visibility.Visible;
        LevelBar.Value = level ?? 0;
        LevelBar.Foreground = accent;
        ValueText.Text = value;
        ValueText.Foreground = accent;
    }
}
