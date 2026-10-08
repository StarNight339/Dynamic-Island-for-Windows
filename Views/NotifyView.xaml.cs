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

        if (image is not null)
        {
            // App-icon style: rounded square filled by the image, no accent circle behind it.
            IconText.Visibility = CompactIcon.Visibility = Visibility.Collapsed;
            Badge.CornerRadius = new CornerRadius(12);
            CompactBadge.CornerRadius = new CornerRadius(6);
            var brush = new ImageBrush(image.Still) { Stretch = Stretch.UniformToFill };
            if (image.IsAnimated) brush.BeginAnimation(ImageBrush.ImageSourceProperty, FrameAnimation(image));
            Badge.Background = CompactBadge.Background = brush;
            return;
        }

        Badge.Background = CompactBadge.Background = accent;
        if (IsFluentGlyph(icon))
        {
            IconText.Text = CompactIcon.Text = icon;
            return;
        }

        // Emoji / plain text: drawn in colour by Emoji.Wpf.
        IconText.Visibility = CompactIcon.Visibility = Visibility.Collapsed;
        EmojiIcon.Text = CompactEmoji.Text = icon;
        EmojiIcon.Visibility = CompactEmoji.Visibility = Visibility.Visible;
    }

    /// <summary>Steps the brush through the GIF frames with their own delays, looping forever.</summary>
    private static ObjectAnimationUsingKeyFrames FrameAnimation(NotifyImage image)
    {
        var anim = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        var at = TimeSpan.Zero;
        for (var i = 0; i < image.Frames!.Length; i++)
        {
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(image.Frames[i], KeyTime.FromTimeSpan(at)));
            at += image.Delays![i];
        }
        anim.Duration = at;
        anim.Freeze(); // frozen timelines aren't cloned when the clock starts
        return anim;
    }

    /// <summary>Fluent/MDL2 icons live in the Unicode private-use area.</summary>
    private static bool IsFluentGlyph(string s) => s.Length == 1 && s[0] is >= '' and <= '';
}
