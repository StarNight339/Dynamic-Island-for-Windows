using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Core;

/// <summary>
/// Loads notification images from an http(s) URL, a local file path, or a data: URI.
/// Returns a frozen image (safe to hand across threads), or null if it can't be loaded.
/// </summary>
public static class ImageLoader
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<ImageSource?> LoadAsync(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        try
        {
            var bytes = await ReadBytesAsync(source.Trim());
            return bytes is null ? null : Decode(bytes);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException
                                       or FormatException or NotSupportedException or UnauthorizedAccessException
                                       or ArgumentException or InvalidOperationException)
        {
            return null; // fall back to the icon rather than failing the whole notification
        }
    }

    private static async Task<byte[]?> ReadBytesAsync(string source)
    {
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = source.IndexOf(',');
            return comma < 0 ? null : Convert.FromBase64String(source[(comma + 1)..]);
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes) return null;
            var data = await response.Content.ReadAsByteArrayAsync();
            return data.Length > MaxBytes ? null : data;
        }

        var path = uri?.IsFile == true ? uri.LocalPath : Environment.ExpandEnvironmentVariables(source);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxBytes) return null;
        return await File.ReadAllBytesAsync(path);
    }

    private static ImageSource Decode(byte[] bytes)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 128; // badge is 44 DIPs; enough for 200% scaling
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
