using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Views;

public partial class NotifyView : IslandView
{
    private bool _hasBody;

    public override Size ExpandedSize => new(400, _hasBody ? 100 : 76);
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public NotifyView() => InitializeComponent();

    /// <param name="icon">A Segoe Fluent Icons glyph (private-use char) or any text/emoji, shown in colour.</param>
    /// <param name="image">When set, replaces <paramref name="icon"/> with a rounded image.</param>
    public NotifyView(string title, string? body, string icon, Brush accent, ImageSource? image = null) : this()
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
            Badge.Background = CompactBadge.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
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

    /// <summary>Fluent/MDL2 icons live in the Unicode private-use area.</summary>
    private static bool IsFluentGlyph(string s) => s.Length == 1 && s[0] is >= '\uE000' and <= '\uF8FF';
}
