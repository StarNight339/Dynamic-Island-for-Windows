using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using Xunit;

namespace DynamicIsland.Tests;

public sealed class ThemeTests
{
    [Theory]
    [InlineData("#FF5F6D", 0xFF, 0x5F, 0x6D)]
    [InlineData("ff5f6d", 0xFF, 0x5F, 0x6D)]
    [InlineData("  #000000 ", 0, 0, 0)]
    [InlineData("White", 0xFF, 0xFF, 0xFF)]
    public void Parses_colours(string text, byte r, byte g, byte b)
    {
        var c = ThemeManager.ParseColor(text, Colors.Magenta);
        Assert.Equal((r, g, b), (c.R, c.G, c.B));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a colour")]
    [InlineData("#12")]
    public void Bad_colours_fall_back(string? text) =>
        Assert.Equal(Colors.Magenta, ThemeManager.ParseColor(text, Colors.Magenta));

    [Fact]
    public void Hex_round_trips() =>
        Assert.Equal("#2EE6D6", ThemeManager.ToHex(ThemeManager.ParseColor("#2ee6d6", Colors.Black)));

    [Fact]
    public void Luminance_spans_black_to_white()
    {
        Assert.Equal(0, ThemeManager.Luminance(Colors.Black), 3);
        Assert.Equal(1, ThemeManager.Luminance(Colors.White), 3);
    }

    [Theory]
    [InlineData("#000000", false)] // default island: white text
    [InlineData("#1C1C1E", false)]
    [InlineData("#F2F2F7", true)]  // Light preset: dark text
    public void Text_colour_follows_the_background(string background, bool light) =>
        Assert.Equal(light, ThemeManager.IsLight([ThemeManager.ParseColor(background, Colors.Black)]));

    [Fact]
    public void Sunset_gradient_gets_dark_text() =>
        Assert.True(ThemeManager.IsLight([
            ThemeManager.ParseColor("#FF5F6D", Colors.Black), ThemeManager.ParseColor("#FFC371", Colors.Black)]));

    [Theory]
    [InlineData("Mesh")]
    [InlineData("Aurora")]
    public void Mesh_presets_on_dark_bases_keep_white_text(string preset)
    {
        var t = new ThemeSettings();
        Assert.True(ThemePresets.Apply(preset, t));
        var blobs = t.MeshColors.Select(c => ThemeManager.ParseColor(c, Colors.Black)).ToList();
        Assert.False(ThemeManager.IsLightMesh(ThemeManager.ParseColor(t.Background, Colors.Black), blobs));
    }

    [Fact]
    public void Light_mesh_gets_dark_text()
    {
        var blobs = new[] { Colors.White, Colors.LightYellow, Colors.LightPink, Colors.LightCyan };
        Assert.True(ThemeManager.IsLightMesh(Colors.White, blobs));
    }

    [Fact]
    public void Defaults_match_the_original_look()
    {
        var t = new ThemeSettings();
        Assert.Equal(("Dark", ThemeEffects.Solid, "#000000", 100), (t.Preset, t.Effect, t.Background, t.Opacity));
        Assert.Equal((100, 36, ""), (t.Scale, t.CornerRadius, t.Font));
        Assert.Equal("#D97757", t.Accent);
    }

    [Fact]
    public void Presets_set_the_look_but_keep_size_corners_and_font()
    {
        var t = new ThemeSettings { Scale = 120, CornerRadius = 20, Font = "Bahnschrift" };

        Assert.True(ThemePresets.Apply("Light", t));

        Assert.Equal(("Light", ThemeEffects.Solid, "#F2F2F7", 96), (t.Preset, t.Effect, t.Background, t.Opacity));
        Assert.Equal((120, 20, "Bahnschrift"), (t.Scale, t.CornerRadius, t.Font));
    }

    [Fact]
    public void Mesh_preset_sets_four_animated_colours()
    {
        var t = new ThemeSettings();
        ThemePresets.Apply("Mesh", t);
        Assert.Equal(ThemeEffects.Mesh, t.Effect);
        Assert.Equal(4, t.MeshColors.Count);
        Assert.True(t.AnimateColors);
    }

    [Fact]
    public void Switching_presets_clears_the_previous_extras()
    {
        var t = new ThemeSettings();
        ThemePresets.Apply("Moss", t);
        Assert.True(t.Texture);

        ThemePresets.Apply("Dark", t);
        Assert.False(t.Texture);
        Assert.Equal(new ThemeSettings().Accent, t.Accent);
    }

    [Fact]
    public void Unknown_preset_changes_nothing()
    {
        var t = new ThemeSettings();
        Assert.False(ThemePresets.Apply("Nope", t));
        Assert.Equal("Dark", t.Preset);
    }

    [Fact]
    public void Every_listed_preset_applies() =>
        Assert.All(ThemePresets.Names, name => Assert.True(ThemePresets.Apply(name, new ThemeSettings())));
}
