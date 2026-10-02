using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DynamicIsland.Views;

public partial class ClaudeView : IslandView
{
    public enum Phase { Working, Waiting, Done }

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _startedAt = DateTime.UtcNow;

    public Phase Current { get; private set; }

    public override Size ExpandedSize => new(400, 128);
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public ClaudeView()
    {
        InitializeComponent();
        _tick.Tick += (_, _) => RenderElapsed();
    }

    /// <summary>Resets the elapsed clock; call when a new prompt is submitted.</summary>
    public void Restart() => _startedAt = DateTime.UtcNow;

    public void Set(Phase phase, string project, string detail)
    {
        Current = phase;
        ProjectText.Text = project;
        DetailText.Text = detail;
        DetailText.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;

        var (title, label, brushKey) = phase switch
        {
            Phase.Working => ("Claude is working", "Working", "AccentClaude"),
            Phase.Waiting => ("Claude needs you", "Needs you", "AccentOrange"),
            _ => ("Claude finished", "Done", "AccentGreen"),
        };
        var brush = (Brush)FindResource(brushKey);
        TitleText.Text = title;
        CompactLabel.Text = label;
        Spark.Foreground = CompactSpark.Foreground = brush;
        Elapsed.Foreground = CompactElapsed.Foreground = brush;

        var spin = phase == Phase.Working
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever }
            : null;
        Spin.BeginAnimation(RotateTransform.AngleProperty, spin);
        CompactSpin.BeginAnimation(RotateTransform.AngleProperty, spin);

        if (phase == Phase.Working) _tick.Start(); else _tick.Stop();
        RenderElapsed();
    }

    private void RenderElapsed() =>
        Elapsed.Text = CompactElapsed.Text = FormatTime(DateTime.UtcNow - _startedAt);
}
