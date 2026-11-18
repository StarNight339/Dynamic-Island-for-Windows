using System.IO;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Settings;
using Xunit;

namespace DynamicIsland.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DynamicIslandTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Missing_file_gives_defaults() => Sta.Run(() =>
    {
        var store = new SettingsStore(FilePath);
        Assert.Equal(5179, store.Get<ApiSettings>().Port);
        Assert.Equal(new[] { 1, 5, 10, 25 }, store.Get<TimerSettings>().Presets);
        var motion = store.Get<MotionSettings>();
        Assert.True(motion.Morphing && motion.SquashStretch && motion.SplitMerge && motion.ParallaxTilt);
        Assert.Equal(50, motion.Bounciness);
        Assert.Equal(100, motion.Speed);
    });

    [Fact]
    public void Motion_settings_round_trip() => Sta.Run(() =>
    {
        var store = new SettingsStore(FilePath);
        var motion = store.Get<MotionSettings>();
        motion.SplitMerge = false;
        motion.Bounciness = 80;
        store.Save<MotionSettings>();
        store.Flush();

        var reloaded = new SettingsStore(FilePath).Get<MotionSettings>();
        Assert.False(reloaded.SplitMerge);
        Assert.Equal(80, reloaded.Bounciness);
        Assert.True(reloaded.Morphing);
    });

    [Fact]
    public void Get_returns_the_same_instance() => Sta.Run(() =>
    {
        var store = new SettingsStore(FilePath);
        Assert.Same(store.Get<ApiSettings>(), store.Get<ApiSettings>());
    });

    [Fact]
    public void Save_raises_Changed_and_round_trips() => Sta.Run(() =>
    {
        var store = new SettingsStore(FilePath);
        string? changed = null;
        store.Changed += id => changed = id;

        store.Get<ApiSettings>().Port = 6000;
        store.Save<ApiSettings>();
        store.Flush();

        Assert.Equal(ApiSettings.SectionId, changed);
        Assert.Equal(6000, new SettingsStore(FilePath).Get<ApiSettings>().Port);
    });

    [Fact]
    public void Unknown_sections_are_kept() => Sta.Run(() =>
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "fromNewerVersion": { "x": 1 } }""");

        var store = new SettingsStore(FilePath);
        store.Get<MediaSettings>().Enabled = false;
        store.Save<MediaSettings>();
        store.Flush();

        var root = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        Assert.Equal(1, (int)root["fromNewerVersion"]!["x"]!);
        Assert.False((bool)root["media"]!["enabled"]!);
    });

    [Fact]
    public void Corrupt_file_is_backed_up_and_defaults_used() => Sta.Run(() =>
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        var store = new SettingsStore(FilePath);

        Assert.True(store.Get<ClaudeSettings>().Enabled);
        Assert.Equal("{ not json", File.ReadAllText(FilePath + ".bak"));
    });

    [Fact]
    public void Wrongly_typed_section_falls_back_to_defaults() => Sta.Run(() =>
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "api": { "port": "not a number" } }""");
        Assert.Equal(5179, new SettingsStore(FilePath).Get<ApiSettings>().Port);
    });
}
