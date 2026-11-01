using System.IO;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using DynamicIsland.Views;

namespace DynamicIsland.Providers;

/// <summary>
/// Shows browser downloads in progress by watching the Downloads folder for the browsers' temporary files
/// (Chrome/Edge ".crdownload", Firefox ".part", ...). Browsers don't expose the total size, so the bar is
/// indeterminate with the size so far and the speed. When the temporary file becomes the real file the bar turns
/// green ("Downloaded") and, if enabled, the file goes onto the file shelf.
/// </summary>
public sealed class DownloadProvider : IDisposable
{
    private static readonly string[] TempExtensions = [".crdownload", ".part", ".partial", ".opdownload", ".download"];
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LingerWhenDone = TimeSpan.FromSeconds(4);

    private sealed class Entry(string path, Activity activity, ProgressView view)
    {
        public string Path = path;
        public Activity Activity { get; } = activity;
        public ProgressView View { get; } = view;
        public long Size;
        public double Speed; // bytes/s, smoothed
        public DateTime SampledAt = DateTime.UtcNow;
        public DateTime GrewAt = DateTime.UtcNow;
    }

    private readonly ActivityManager _activities;
    private readonly Dispatcher _dispatcher;
    private readonly SettingsStore _settings;
    private readonly FileShelf _shelf;
    private readonly Dictionary<string, Entry> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _poll = new() { Interval = PollInterval };
    private FileSystemWatcher? _watcher;
    private int _nextId;

    public DownloadProvider(ActivityManager activities, Dispatcher dispatcher, SettingsStore settings, FileShelf shelf)
    {
        _activities = activities;
        _dispatcher = dispatcher;
        _settings = settings;
        _shelf = shelf;
        Folder = NativeMethods.GetDownloadsFolder();
        _poll.Tick += (_, _) => Poll();
        settings.Changed += id =>
        {
            if (id != DownloadSettings.SectionId) return;
            if (settings.Get<DownloadSettings>().Enabled) Start();
            else Stop();
        };
    }

    public string Folder { get; }

    public void Start()
    {
        if (_watcher is not null || !_settings.Get<DownloadSettings>().Enabled || !Directory.Exists(Folder)) return;
        try
        {
            _watcher = new FileSystemWatcher(Folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            };
            _watcher.Created += (_, e) => OnUi(() => Seen(e.FullPath));
            _watcher.Changed += (_, e) => OnUi(() => Seen(e.FullPath));
            _watcher.Renamed += (_, e) => OnUi(() => Renamed(e.OldFullPath, e.FullPath));
            _watcher.Deleted += (_, e) => OnUi(() => Deleted(e.FullPath));
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _watcher?.Dispose();
            _watcher = null; // no Downloads folder we can watch; the island just won't show downloads
        }
    }

    private void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _poll.Stop();
        foreach (var entry in _active.Values) _activities.Remove(entry.Activity.Id);
        _active.Clear();
    }

    public void Dispose() => Stop();

    /// <summary>True for a browser's in-progress download file.</summary>
    public static bool IsTempDownload(string path) =>
        TempExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// "report.pdf.crdownload" → "report.pdf". Null while Chrome hasn't named the file yet ("Unconfirmed 123.crdownload").
    /// </summary>
    public static string? DisplayName(string tempPath)
    {
        var name = Path.GetFileNameWithoutExtension(tempPath);
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase)) return null;
        return name;
    }

    /// <summary>"12.3 MB", "850 KB".</summary>
    public static string FormatSize(double bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{Math.Max(0, bytes) / 1024.0:0} KB",
    };

    private void OnUi(Action action) => _dispatcher.InvokeAsync(action);

    private void Seen(string path)
    {
        if (!IsTempDownload(path) || _active.ContainsKey(path) || !File.Exists(path)) return;

        var view = new ProgressView();
        var entry = new Entry(path, new Activity { Id = $"download:{++_nextId}", View = view, Priority = Priority.Progress }, view);
        _active[path] = entry;
        Render(entry);
        _activities.Post(entry.Activity, StaleAfter);
        _poll.Start();
    }

    private void Renamed(string oldPath, string newPath)
    {
        if (!_active.Remove(oldPath, out var entry))
        {
            Seen(newPath); // e.g. a file renamed *to* a temp name
            return;
        }
        if (IsTempDownload(newPath))
        {
            // Chrome: "Unconfirmed 123.crdownload" → "report.pdf.crdownload" once the name is known.
            entry.Path = newPath;
            _active[newPath] = entry;
            Render(entry);
            return;
        }
        Complete(entry, newPath);
    }

    private void Deleted(string path)
    {
        if (!_active.Remove(path, out var entry)) return;
        // Firefox moves "x.zip.part" over the placeholder "x.zip"; a deleted temp file with nothing left was cancelled.
        var final = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        if (File.Exists(final) && new FileInfo(final).Length > 0) Complete(entry, final);
        else _activities.Remove(entry.Activity.Id);
        if (_active.Count == 0) _poll.Stop();
    }

    private void Complete(Entry entry, string finalPath)
    {
        _active.Remove(entry.Path);
        if (_active.Count == 0) _poll.Stop();

        var size = File.Exists(finalPath) ? new FileInfo(finalPath).Length : entry.Size;
        entry.View.SetContent(Path.GetFileName(finalPath), $"Downloaded · {FormatSize(size)}", "",
            Notifier.Brush("AccentGreen"), null);
        entry.View.SetDone();
        _activities.Post(entry.Activity, LingerWhenDone);

        if (_settings.Get<DownloadSettings>().AddToShelf && _settings.Get<FileShelfSettings>().Enabled)
            _shelf.Add([finalPath]);
    }

    private void Poll()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in _active.Values.ToList())
        {
            long size;
            try { size = new FileInfo(entry.Path).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            var seconds = (now - entry.SampledAt).TotalSeconds;
            if (seconds > 0)
            {
                var instant = Math.Max(0, size - entry.Size) / seconds;
                entry.Speed = entry.Speed <= 0 ? instant : entry.Speed * 0.7 + instant * 0.3;
            }
            if (size > entry.Size) entry.GrewAt = now;
            entry.Size = size;
            entry.SampledAt = now;

            if (now - entry.GrewAt > StaleAfter)
            {
                // Paused or abandoned: the browser left the temp file behind.
                _active.Remove(entry.Path);
                _activities.Remove(entry.Activity.Id);
                continue;
            }
            Render(entry);
            _activities.Extend(entry.Activity.Id, StaleAfter);
        }
        if (_active.Count == 0) _poll.Stop();
    }

    private void Render(Entry entry)
    {
        entry.View.SetContent(DisplayName(entry.Path) ?? "Downloading…", "Downloading", "",
            Notifier.Brush("AccentClaude"), null);
        var size = FormatSize(entry.Size);
        entry.View.SetIndeterminate(entry.Speed > 0 ? $"{size} · {FormatSize(entry.Speed)}/s" : size, size);
    }
}
