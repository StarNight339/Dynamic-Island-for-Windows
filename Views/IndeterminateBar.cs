using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.Views;

/// <summary>
/// A slim bar with a segment sliding across it, for work with no known end (Claude working, a download whose
/// size the browser doesn't tell us). Matches the <c>SlimBar</c> style; animates only while visible.
/// </summary>
public sealed class IndeterminateBar : Grid
{
    private const double SegmentFraction = 0.3;

    private readonly Border _segment;
    private readonly TranslateTransform _offset = new();

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(IndeterminateBar),
        new PropertyMetadata(Brushes.White, (d, e) => ((IndeterminateBar)d)._segment.Background = (Brush)e.NewValue));

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IndeterminateBar()
    {
        Height = 5;
        ClipToBounds = true;
        var track = new Border { CornerRadius = new CornerRadius(2.5) };
        track.SetResourceReference(Border.BackgroundProperty, "Track");
        _segment = new Border
        {
            CornerRadius = new CornerRadius(2.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Foreground,
            RenderTransform = _offset,
        };
        Children.Add(track);
        Children.Add(_segment);

        SizeChanged += (_, _) => Restart();
        IsVisibleChanged += (_, _) => Restart();
    }

    private void Restart()
    {
        if (!IsVisible || ActualWidth <= 0)
        {
            _offset.BeginAnimation(TranslateTransform.XProperty, null);
            return;
        }
        var width = ActualWidth * SegmentFraction;
        _segment.Width = width;
        var sweep = new DoubleAnimation(-width, ActualWidth, TimeSpan.FromSeconds(1.2))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(sweep, Motion.LoopFrameRate);
        _offset.BeginAnimation(TranslateTransform.XProperty, sweep);
    }
}
