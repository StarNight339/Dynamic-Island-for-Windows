using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DynamicIsland.Views;

public partial class MediaView : IslandView
{
    private readonly List<ScaleTransform> _bars = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private TimeSpan _position, _duration;
    private DateTime _positionAt;
    private bool _playing;

    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;

    // Browsers often report no timeline; drop that row instead of leaving a gap.
    public override Size ExpandedSize => new(400, _duration > TimeSpan.Zero ? 180 : 150);
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public MediaView()
    {
        InitializeComponent();
        BuildBars(CompactBars, 3, 14);
        BuildBars(ExpandedBars, 4, 20);
        _tick.Tick += (_, _) => RenderTimeline();
    }

    public void Update(string title, string artist, ImageSource? art, bool playing)
    {
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Unknown" : title;
        ArtistText.Text = artist;
        CompactArt.ImageSource = art;
        ExpandedArt.ImageSource = art;
        SetPlaying(playing);
    }

    public void SetPlaying(bool playing)
    {
        if (playing != _playing)
        {
            // Freeze the extrapolated position at the moment playback state flips.
            _position = CurrentPosition();
            _positionAt = DateTime.UtcNow;
        }
        _playing = playing;
        PlayPauseButton.Content = playing ? "\uE769" : "\uE768";
        AnimateBars(playing);
        if (playing) _tick.Start(); else _tick.Stop();
        RenderTimeline();
    }

    public void UpdateTimeline(TimeSpan position, TimeSpan duration, DateTime updatedAtUtc)
    {
        _position = position;
        _duration = duration;
        _positionAt = updatedAtUtc;
        RenderTimeline();
    }

    private TimeSpan CurrentPosition()
    {
        var p = _playing ? _position + (DateTime.UtcNow - _positionAt) : _position;
        return _duration > TimeSpan.Zero && p > _duration ? _duration : p;
    }

    private void RenderTimeline()
    {
        TimelineRow.Visibility = _duration > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
        if (_duration <= TimeSpan.Zero) return;
        var p = CurrentPosition();
        Progress.Value = p.TotalSeconds / _duration.TotalSeconds;
        PositionText.Text = FormatTime(p);
        DurationText.Text = FormatTime(_duration);
    }

    private void BuildBars(Panel host, int count, double height)
    {
        for (var i = 0; i < count; i++)
        {
            var scale = new ScaleTransform(1, 0.3);
            host.Children.Add(new Rectangle
            {
                Width = 3,
                Height = height,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Fill = (Brush)FindResource("AccentGreen"),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = scale,
            });
            _bars.Add(scale);
        }
    }

    private void AnimateBars(bool on)
    {
        for (var i = 0; i < _bars.Count; i++)
        {
            if (!on)
            {
                _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _bars[i].ScaleY = 0.3;
                continue;
            }
            var anim = new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(320 + (i * 137) % 260))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                BeginTime = TimeSpan.FromMilliseconds(i * 90),
            };
            _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke();
    private void Next_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke();
    private void Prev_Click(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke();
}
