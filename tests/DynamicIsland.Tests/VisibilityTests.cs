using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using DynamicIsland.Providers;
using DynamicIsland.Views;
using Xunit;

namespace DynamicIsland.Tests;

public sealed class VisibilityTests : IDisposable
{
    private sealed class TestView : IslandView
    {
        private readonly Border _compact = new(), _expanded = new();
        protected override FrameworkElement CompactPart => _compact;
        protected override FrameworkElement ExpandedPart => _expanded;
    }

    private static Activity Make(bool important) =>
        new() { Id = "notify", View = new TestView(), Priority = Priority.Notification, Important = important };

    [Fact]
    public void Visible_when_nothing_hides_it() => Sta.Run(() =>
    {
        Assert.False(IslandVisibility.ShouldHide(false, false, null));
        Assert.False(IslandVisibility.ShouldHide(false, false, Make(important: false)));
    });

    [Fact]
    public void User_hide_and_fullscreen_hide() => Sta.Run(() =>
    {
        Assert.True(IslandVisibility.ShouldHide(true, false, null));
        Assert.True(IslandVisibility.ShouldHide(false, true, null));
        Assert.True(IslandVisibility.ShouldHide(true, true, Make(important: false)));
    });

    [Fact]
    public void Important_activity_breaks_through_both() => Sta.Run(() =>
    {
        Assert.False(IslandVisibility.ShouldHide(true, false, Make(important: true)));
        Assert.False(IslandVisibility.ShouldHide(false, true, Make(important: true)));
        Assert.False(IslandVisibility.ShouldHide(true, true, Make(important: true)));
    });

    [Theory]
    [InlineData("""{"title":"x"}""", false)]
    [InlineData("""{"priority":"normal"}""", false)]
    [InlineData("""{"priority":"low"}""", false)]
    [InlineData("""{"priority":1}""", false)]
    [InlineData("""{"important":false}""", false)]
    [InlineData("""{"priority":"high"}""", true)]
    [InlineData("""{"priority":"URGENT"}""", true)]
    [InlineData("""{"priority":"critical"}""", true)]
    [InlineData("""{"priority":2}""", true)]
    [InlineData("""{"priority":"5"}""", true)]
    [InlineData("""{"important":true}""", true)]
    public void Api_priority_marks_notifications_important(string json, bool expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, HttpApiProvider.IsImportant(doc.RootElement));
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DynamicIslandTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Hidden_defaults_off_and_is_remembered() => Sta.Run(() =>
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        Assert.False(store.Get<GeneralSettings>().Hidden);
        Assert.True(store.Get<GeneralSettings>().HideHotkey);

        store.Get<GeneralSettings>().Hidden = true;
        store.Save<GeneralSettings>();
        store.Flush();

        Assert.True(new SettingsStore(path).Get<GeneralSettings>().Hidden);
    });
}
