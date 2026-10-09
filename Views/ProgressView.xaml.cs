using System.Windows;
using System.Windows.Media;
using DynamicIsland.Core;

namespace DynamicIsland.Views;

/// <summary>A long-running task pushed over the API. Updated in place as progress arrives.</summary>
public partial class ProgressView : IslandView
{
    private bool _hasBody;
    private Brush _accent = Brushes.White;

    public override Size ExpandedSize => new(400, _hasBody ? 104 : 86);
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public ProgressView() => InitializeComponent();

    public void SetContent(string title, string? body, string icon, Brush accent, NotifyImage? image)
    {
        _hasBody = !string.IsNullOrWhiteSpace(body);
        TitleText.Text = CompactTitle.Text = title;
        BodyText.Text = body ?? "";
        BodyText.Visibility = _hasBody ? Visibility.Visible : Visibility.Collapsed;

        _accent = accent;
        Bar.Foreground = CompactBar.Foreground = CompactPercent.Foreground = accent;
        BadgeFill.Apply(Badge, IconText, EmojiIcon, icon, accent, image, 12);
        BadgeFill.Apply(CompactBadge, CompactIcon, CompactEmoji, icon, accent, image, 6);
    }

    /// <param name="fraction">0..1</param>
    public void SetProgress(double fraction)
    {
        Bar.Value = CompactBar.Value = fraction;
        Percent.Text = CompactPercent.Text = $"{Math.Floor(fraction * 100):0}%";
    }

    /// <summary>Full green bar; the provider lets the activity expire shortly after.</summary>
    public void SetDone()
    {
        SetProgress(1);
        Bar.Foreground = CompactBar.Foreground = CompactPercent.Foreground = Notifier.Brush("AccentGreen");
    }

    /// <summary>Back to the sender's colour, e.g. when a finished id is reused for a new run.</summary>
    public void ClearDone() => Bar.Foreground = CompactBar.Foreground = CompactPercent.Foreground = _accent;
}
