using System.Windows;

namespace DynamicIsland.Views;

public partial class TimerView : IslandView
{
    public event Action? CancelRequested;
    public event Action? AddMinuteRequested;

    public override Size ExpandedSize => new(380, 96);
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public TimerView() => InitializeComponent();

    public void SetRemaining(TimeSpan remaining)
    {
        var rounded = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, remaining.TotalSeconds)));
        Remaining.Text = CompactRemaining.Text = FormatTime(rounded);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void AddMinute_Click(object sender, RoutedEventArgs e) => AddMinuteRequested?.Invoke();
}
