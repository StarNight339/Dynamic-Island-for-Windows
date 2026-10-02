using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland.Views;

/// <summary>
/// Base for everything shown inside the island. Each view has a compact layout (the pill) and an
/// expanded layout (hover / notification); the window sizes the island from <see cref="CompactSize"/>
/// and <see cref="ExpandedSize"/>.
/// </summary>
public abstract class IslandView : UserControl
{
    public virtual Size CompactSize => new(300, 36);
    public virtual Size ExpandedSize => new(400, 150);

    /// <summary>False when hovering should not open an expanded layout.</summary>
    public virtual bool CanExpand => true;

    protected abstract FrameworkElement CompactPart { get; }
    protected abstract FrameworkElement ExpandedPart { get; }

    public bool IsExpanded { get; private set; }

    public void SetExpanded(bool expanded)
    {
        IsExpanded = expanded && CanExpand;
        CompactPart.Visibility = IsExpanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedPart.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    public Size CurrentSize => IsExpanded ? ExpandedSize : CompactSize;

    protected static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
