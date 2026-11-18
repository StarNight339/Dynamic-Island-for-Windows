using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;
using DynamicIsland.Views;
using Xunit;

namespace DynamicIsland.Tests;

public class ActivityManagerTests
{
    private sealed class TestView : IslandView
    {
        private readonly Border _compact = new(), _expanded = new();
        protected override FrameworkElement CompactPart => _compact;
        protected override FrameworkElement ExpandedPart => _expanded;
    }

    private static Activity Make(string id, int priority) => new() { Id = id, View = new TestView(), Priority = priority };

    [Fact]
    public void Starts_empty() => Sta.Run(() => Assert.Null(new ActivityManager().Current));

    [Fact]
    public void Higher_priority_wins() => Sta.Run(() =>
    {
        var m = new ActivityManager();
        var media = Make("media", Priority.Media);
        var notify = Make("notify", Priority.Notification);
        m.Post(media);
        m.Post(notify);
        Assert.Same(notify, m.Current);

        m.Post(media); // re-posting a lower priority doesn't take over
        Assert.Same(notify, m.Current);
    });

    [Fact]
    public void Newest_wins_among_equal_priority() => Sta.Run(() =>
    {
        var m = new ActivityManager();
        var a = Make("a", Priority.Progress);
        var b = Make("b", Priority.Progress);
        m.Post(a);
        Thread.Sleep(5); // PostedAt has to differ
        m.Post(b);
        Assert.Same(b, m.Current);
    });

    [Fact]
    public void Remove_falls_back_to_next() => Sta.Run(() =>
    {
        var m = new ActivityManager();
        var media = Make("media", Priority.Media);
        m.Post(media);
        m.Post(Make("volume", Priority.Volume));
        m.Remove("volume");
        Assert.Same(media, m.Current);
        m.Remove("media");
        Assert.Null(m.Current);
    });

    [Fact]
    public void CurrentChanged_fires_only_on_change() => Sta.Run(() =>
    {
        var m = new ActivityManager();
        var count = 0;
        m.CurrentChanged += () => count++;
        var media = Make("media", Priority.Media);
        m.Post(media);
        m.Post(Make("paused", Priority.MediaPaused)); // lower, doesn't change Current
        Assert.Equal(1, count);
        m.Remove("missing");
        Assert.Equal(1, count);
    });

    [Fact]
    public void Recompute_after_priority_change() => Sta.Run(() =>
    {
        var m = new ActivityManager();
        var media = Make("media", Priority.Media);
        var timer = Make("timer", Priority.Timer);
        m.Post(media);
        m.Post(timer);
        Assert.Same(timer, m.Current);

        media.Priority = Priority.Notification;
        m.Recompute();
        Assert.Same(media, m.Current);
    });

    [Theory]
    [InlineData(5, "5 min")]
    [InlineData(60, "1 h")]
    [InlineData(90, "1 h 30")]
    [InlineData(120, "2 h")]
    public void FormatMinutes(int minutes, string expected) => Assert.Equal(expected, IslandView.FormatMinutes(minutes));

    [Fact]
    public void Secondary_is_the_runner_up() => Sta.Run(() =>
    {
        var m = new ActivityManager();
        var media = Make("media", Priority.Media);
        var timer = Make("timer", Priority.Timer);
        m.Post(media);
        Assert.Null(m.Secondary);
        m.Post(timer);
        Assert.Same(timer, m.Current);
        Assert.Same(media, m.Secondary);
        m.Remove("timer");
        Assert.Same(media, m.Current);
        Assert.Null(m.Secondary);
    });

    [Fact]
    public void Secondary_skips_expiring_activities_and_the_current_view() => Sta.Run(() =>
    {
        var m = new ActivityManager();
        var media = Make("media", Priority.Media);
        m.Post(Make("volume", Priority.Volume), TimeSpan.FromSeconds(2));
        m.Post(Make("notify", Priority.Notification), TimeSpan.FromSeconds(5));
        Assert.Null(m.Secondary); // the volume pop-up is transient

        var shelfView = new TestView();
        m.Post(new Activity { Id = "shelf-open", View = shelfView, Priority = Priority.ShelfOpen });
        m.Post(new Activity { Id = "shelf", View = shelfView, Priority = Priority.Shelf });
        m.Post(media);
        Assert.Same(media, m.Secondary); // not the shelf pill: it's the same view as the open shelf
    });

    [Fact]
    public void SecondaryChanged_fires_only_on_change() => Sta.Run(() =>
    {
        var m = new ActivityManager();
        var count = 0;
        m.SecondaryChanged += () => count++;
        m.Post(Make("timer", Priority.Timer));
        Assert.Equal(0, count);
        m.Post(Make("media", Priority.Media));
        Assert.Equal(1, count);
        m.Post(Make("claude", Priority.ClaudeWorking)); // claude takes over; timer becomes the runner-up
        Assert.Equal(2, count);
        m.Remove("missing");
        Assert.Equal(2, count);
    });
}
