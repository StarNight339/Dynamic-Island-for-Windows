using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Views;

namespace DynamicIsland.Providers;

/// <summary>
/// Progress bars pushed over the API, one activity per id. Each update edits the same view in place;
/// at 100% it turns green and lingers briefly. A sender that goes quiet is cleaned up by <see cref="StaleAfter"/>.
/// </summary>
public sealed class ProgressProvider(ActivityManager activities)
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LingerWhenDone = TimeSpan.FromSeconds(3);

    private sealed class Entry(Activity activity, ProgressView view)
    {
        public Activity Activity { get; } = activity;
        public ProgressView View { get; } = view;
        public string Title = "Progress";
        public string? Body;
        public string Icon = ""; // Download
        public Brush Accent = Notifier.Brush("AccentClaude");
        public NotifyImage? Image;
        public double Fraction;
    }

    private readonly Dictionary<string, Entry> _tasks = new();

    /// <summary>Creates or updates the bar for <paramref name="id"/>. Null arguments keep the previous value.</summary>
    /// <param name="percent">0..100</param>
    public void Update(string id, string? title, string? body, string? icon, string? color, NotifyImage? image, double? percent)
    {
        var t = GetOrCreate("progress:" + id);
        if (title is not null) t.Title = title;
        if (body is not null) t.Body = body;
        if (icon is not null) t.Icon = icon;
        if (color is not null) t.Accent = Notifier.ParseBrush(color, t.Accent);
        if (image is not null) t.Image = image;
        if (percent is { } p) t.Fraction = Math.Clamp(p / 100, 0, 1);

        t.View.SetContent(t.Title, t.Body, t.Icon, t.Accent, t.Image);
        if (t.Fraction >= 1)
        {
            t.View.SetDone();
            activities.Post(t.Activity, LingerWhenDone);
        }
        else
        {
            t.View.ClearDone();
            t.View.SetProgress(t.Fraction);
            activities.Post(t.Activity, StaleAfter);
        }
    }

    public void Dismiss(string id)
    {
        var key = "progress:" + id;
        activities.Remove(key);
        _tasks.Remove(key);
    }

    private Entry GetOrCreate(string key)
    {
        // An entry whose activity already expired starts over fresh rather than inheriting old fields.
        if (_tasks.TryGetValue(key, out var t) && activities.Contains(key)) return t;
        var view = new ProgressView();
        t = new Entry(new Activity { Id = key, View = view, Priority = Priority.Progress }, view);
        _tasks[key] = t;
        return t;
    }
}
