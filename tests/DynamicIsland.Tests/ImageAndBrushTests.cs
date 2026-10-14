using System.IO;
using System.Windows.Media;
using DynamicIsland.Core;
using Xunit;

namespace DynamicIsland.Tests;

public class ImageAndBrushTests
{
    // 1x1 PNG.
    private const string Png =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    [Fact]
    public async Task Data_uri_png_loads_frozen()
    {
        var image = await ImageLoader.LoadAsync("data:image/png;base64," + Png);
        Assert.NotNull(image);
        Assert.False(image.IsAnimated);
        Assert.True(image.Still.IsFrozen); // has to be, it's handed from the API thread to the UI thread
    }

    [Fact]
    public async Task Local_file_loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"di-test-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(Png));
        try { Assert.NotNull(await ImageLoader.LoadAsync(path)); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\does\not\exist.png")]
    [InlineData("data:image/png;base64,!!!not base64!!!")]
    [InlineData("data:image/png;base64,aGVsbG8=")] // valid base64, not an image
    public async Task Bad_sources_return_null(string? source) => Assert.Null(await ImageLoader.LoadAsync(source));

    [Theory]
    [InlineData("#34C759", 0xFF, 0x34, 0xC7, 0x59)]
    [InlineData("#8034C759", 0x80, 0x34, 0xC7, 0x59)]
    [InlineData("Red", 0xFF, 0xFF, 0x00, 0x00)]
    public void ParseBrush_valid(string value, byte a, byte r, byte g, byte b) => Sta.Run(() =>
    {
        var brush = Assert.IsType<SolidColorBrush>(Notifier.ParseBrush(value, Brushes.Black));
        Assert.Equal(Color.FromArgb(a, r, g, b), brush.Color);
        Assert.True(brush.IsFrozen);
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#XYZ")]
    [InlineData("not a colour")]
    public void ParseBrush_invalid_uses_fallback(string? value) => Sta.Run(() =>
    {
        var fallback = Brushes.Teal;
        Assert.Same(fallback, Notifier.ParseBrush(value, fallback));
    });
}
