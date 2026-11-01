using System.Windows;

namespace DynamicIsland.Views;

public partial class TimerView : IslandView
{
    public event Action? CancelRequested;
    public event Action? AddMinuteRequested;

    public override Size ExpandedSize => new(380, 108);
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public TimerView() => InitializeComponent();

    /// <param name="total">The full length (including added minutes); the bar shows how much of it is left.</param>
    public void SetRemaining(TimeSpan remaining, TimeSpan total)
    {
        var rounded = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, remaining.TotalSeconds)));
        Remaining.Text = CompactRemaining.Text = FormatTime(rounded);
        Bar.Value = CompactBar.Value = total > TimeSpan.Zero ? Math.Clamp(remaining / total, 0, 1) : 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void AddMinute_Click(object sender, RoutedEventArgs e) => AddMinuteRequested?.Invoke();
}
