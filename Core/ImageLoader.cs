using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Core;

/// <summary>A notification image: a still, or an animated GIF (<see cref="Frames"/> + per-frame <see cref="Delays"/>).</summary>
public sealed record NotifyImage(ImageSource Still, ImageSource[]? Frames = null, TimeSpan[]? Delays = null)
{
    public bool IsAnimated => Frames is { Length: > 1 };
}

/// <summary>
/// Loads notification images from an http(s) URL, a local file path, or a data: URI.
/// Returns frozen images (safe to hand across threads), or null if it can't be loaded.
/// </summary>
public static class ImageLoader
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private const int DecodeSize = 128; // badge is 44 DIPs; enough for 200% scaling
    private const int MaxFrames = 300;
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        // Some hosts (e.g. Wikimedia) reject requests without a User-Agent with 403.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0");
        return http;
    }

    public static async Task<NotifyImage?> LoadAsync(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        try
        {
            var bytes = await ReadBytesAsync(source.Trim());
            return bytes is null ? null : DecodeGif(bytes) ?? new NotifyImage(Decode(bytes));
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
        bmp.DecodePixelWidth = DecodeSize;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Composites a multi-frame GIF into full frames (GIF frames are partial patches with a disposal mode).
    /// Works on raw pixel arrays so it can run off the UI thread. Returns null for non-GIF / single-frame images.
    /// </summary>
    private static NotifyImage? DecodeGif(byte[] bytes)
    {
        if (bytes.Length < 6 || bytes[0] != 'G' || bytes[1] != 'I' || bytes[2] != 'F') return null;

        var decoder = new GifBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count < 2) return null;

        int width = Query<ushort?>(decoder.Metadata, "/logscrdesc/Width") ?? decoder.Frames[0].PixelWidth;
        int height = Query<ushort?>(decoder.Metadata, "/logscrdesc/Height") ?? decoder.Frames[0].PixelHeight;
        var canvas = new int[width * height];
        int[]? restore = null;

        var count = Math.Min(decoder.Frames.Count, MaxFrames);
        var frames = new ImageSource[count];
        var delays = new TimeSpan[count];
        (int X, int Y, int W, int H, byte Disposal) prev = default;

        for (var i = 0; i < count; i++)
        {
            // Undo the previous frame per its disposal mode: 2 = clear to transparent, 3 = restore what was under it.
            if (prev.Disposal == 2) FillRect(canvas, width, height, prev.X, prev.Y, prev.W, prev.H);
            else if (prev.Disposal == 3 && restore is not null) Array.Copy(restore, canvas, canvas.Length);

            var frame = decoder.Frames[i];
            var meta = frame.Metadata as BitmapMetadata;
            int x = Query<ushort?>(meta, "/imgdesc/Left") ?? 0;
            int y = Query<ushort?>(meta, "/imgdesc/Top") ?? 0;
            var disposal = Query<byte?>(meta, "/grctlext/Disposal") ?? (byte)0;
            var delay = Query<ushort?>(meta, "/grctlext/Delay") ?? 0;
            delays[i] = TimeSpan.FromMilliseconds(delay < 2 ? 100 : delay * 10); // browsers treat 0-1 as 100ms

            if (disposal == 3) restore = (int[])canvas.Clone();

            var src = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            var pixels = new int[src.PixelWidth * src.PixelHeight];
            src.CopyPixels(pixels, src.PixelWidth * 4, 0);
            for (var row = 0; row < src.PixelHeight; row++)
            {
                var cy = y + row;
                if (cy >= height) break;
                for (var col = 0; col < src.PixelWidth; col++)
                {
                    var cx = x + col;
                    if (cx >= width) break;
                    var p = pixels[row * src.PixelWidth + col];
                    if ((uint)p >> 24 != 0) canvas[cy * width + cx] = p; // GIF transparency is on/off
                }
            }

            frames[i] = Scale(BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, canvas, width * 4));
            prev = (x, y, frame.PixelWidth, frame.PixelHeight, disposal);
        }

        return new NotifyImage(frames[0], frames, delays);
    }

    private static ImageSource Scale(BitmapSource bmp)
    {
        var factor = (double)DecodeSize / Math.Max(bmp.PixelWidth, bmp.PixelHeight);
        BitmapSource scaled = factor < 1 ? new TransformedBitmap(bmp, new ScaleTransform(factor, factor)) : bmp;
        // Bake into a plain pixel bitmap with no source chain: animations clone their key frames on the UI thread,
        // and a clone that walks back to a bitmap created on this (background) thread throws a cross-thread error.
        var stride = scaled.PixelWidth * 4;
        var pixels = new byte[stride * scaled.PixelHeight];
        scaled.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(scaled.PixelWidth, scaled.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static void FillRect(int[] canvas, int width, int height, int x, int y, int w, int h)
    {
        for (var row = y; row < Math.Min(y + h, height); row++)
            Array.Clear(canvas, row * width + x, Math.Max(0, Math.Min(w, width - x)));
    }

    private static T? Query<T>(BitmapMetadata? meta, string query)
    {
        try
        {
            return meta?.GetQuery(query) is T value ? value : default;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return default;
        }
    }
}
