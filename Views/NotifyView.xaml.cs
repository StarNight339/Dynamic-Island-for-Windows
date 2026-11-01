using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core;

namespace DynamicIsland.Views;

public partial class NotifyView : IslandView
{
    private bool _hasBody;

    public override Size ExpandedSize => new(400, _hasBody ? 100 : 76);
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public NotifyView() => InitializeComponent();

    /// <param name="icon">A Segoe Fluent Icons glyph (private-use char) or any text/emoji, shown in colour.</param>
    /// <param name="image">When set, replaces <paramref name="icon"/> with a rounded image (animated GIFs loop).</param>
    public NotifyView(string title, string? body, string icon, Brush accent, NotifyImage? image = null) : this()
    {
        _hasBody = !string.IsNullOrWhiteSpace(body);
        TitleText.Text = CompactTitle.Text = title;
        BodyText.Text = body ?? "";
        BodyText.Visibility = _hasBody ? Visibility.Visible : Visibility.Collapsed;

        BadgeFill.Apply(Badge, IconText, EmojiIcon, icon, accent, image, 12);
        BadgeFill.Apply(CompactBadge, CompactIcon, CompactEmoji, icon, accent, image, 6);
        Countdown.Background = accent;
    }

    /// <summary>Starts the bar along the bottom that empties over <paramref name="duration"/>, when the notification closes.</summary>
    public void StartCountdown(TimeSpan duration)
    {
        Countdown.Visibility = Visibility.Visible;
        CountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, duration));
    }
}
