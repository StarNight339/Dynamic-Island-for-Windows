using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core;

namespace DynamicIsland.Views;

/// <summary>Fills a round icon badge with exactly one of: Fluent glyph, colour emoji, or an image.</summary>
internal static class BadgeFill
{
    /// <param name="icon">A Segoe Fluent Icons glyph (private-use char) or any text/emoji, shown in colour.</param>
    /// <param name="image">When set, replaces <paramref name="icon"/> with a rounded image (animated GIFs loop).</param>
    /// <param name="imageRadius">Corner radius of the app-icon style square used for images.</param>
    public static void Apply(Border badge, TextBlock glyph, Emoji.Wpf.TextBlock emoji, string icon, Brush accent,
        NotifyImage? image, double imageRadius)
    {
        if (image is not null)
        {
            // App-icon style: rounded square filled by the image, no accent circle behind it.
            glyph.Visibility = emoji.Visibility = Visibility.Collapsed;
            badge.CornerRadius = new CornerRadius(imageRadius);
            var brush = new ImageBrush(image.Still) { Stretch = Stretch.UniformToFill };
            if (image.IsAnimated) brush.BeginAnimation(ImageBrush.ImageSourceProperty, FrameAnimation(image));
            badge.Background = brush;
            return;
        }

        badge.CornerRadius = new CornerRadius(badge.Width / 2);
        badge.Background = accent;
        var isGlyph = IsFluentGlyph(icon);
        glyph.Visibility = isGlyph ? Visibility.Visible : Visibility.Collapsed;
        emoji.Visibility = isGlyph ? Visibility.Collapsed : Visibility.Visible;
        // Emoji / plain text: drawn in colour by Emoji.Wpf.
        if (isGlyph) glyph.Text = icon;
        else emoji.Text = icon;
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
