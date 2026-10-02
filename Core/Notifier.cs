using System.Windows;
using System.Windows.Media;
using DynamicIsland.Views;

namespace DynamicIsland.Core;

/// <summary>Shows a transient, auto-expanding notification. A newer one replaces the older.</summary>
public sealed class Notifier(ActivityManager activities)
{
    public void Show(string title, string? body, string icon, Brush accent, double seconds = 5, ImageSource? image = null)
    {
        var view = new NotifyView(title, body, icon, accent, image);
        activities.Post(new Activity
        {
            Id = "notify",
            View = view,
            Priority = Priority.Notification,
            AutoExpand = true,
        }, TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60)));
    }

    public static Brush Brush(string resourceKey) => (Brush)Application.Current.FindResource(resourceKey);

    /// <summary>Parses "#RRGGBB" / "#AARRGGBB" / named colors, falling back to <paramref name="fallback"/>.</summary>
    public static Brush ParseBrush(string? value, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            b.Freeze();
            return b;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}
