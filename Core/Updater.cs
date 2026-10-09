using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core;

/// <summary>
/// Self-update from GitHub Releases. Only the published single-file exe updates itself: it downloads the
/// matching asset (small or standalone) next to itself, swaps it in and restarts. A running exe can't be
/// overwritten but can be renamed, so the old one is moved to "*.old" and deleted on the next start.
/// </summary>
public sealed class Updater(Notifier notifier, SettingsStore settings, Action quit)
{
    private const string LatestUrl = "https://api.github.com/repos/StarNight339/Dynamic-Island-for-Windows/releases/latest";
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>Passed to the restarted exe so it can say it was updated.</summary>
    public const string UpdatedArg = "--updated";

    /// <summary>Passed with the old process id; the new one waits for it to exit before taking the single-instance lock.</summary>
    public const string WaitArg = "--wait-pid";

#if STANDALONE
    private const string AssetSuffix = "-standalone.exe";
#else
    private const string AssetSuffix = "-win-x64.exe";
#endif

    private static readonly HttpClient Http = CreateHttp();
    private readonly DispatcherTimer _timer = new() { Interval = FirstCheck };
    private bool _busy;

    public static Version Current => Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0));

    /// <summary>True for the published single-file exe; dev builds (exe + dll) are left alone.</summary>
    public static bool CanSelfUpdate =>
        Environment.ProcessPath is { } exe
        && !File.Exists(Path.Combine(AppContext.BaseDirectory, Path.GetFileNameWithoutExtension(exe) + ".dll"));

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; // the standalone exe is ~80 MB
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/" + Current.ToString(3)); // GitHub API requires one
        return http;
    }

    /// <summary>Checks shortly after launch, then every few hours, while "Update automatically" is on.</summary>
    public void Start()
    {
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = Interval;
            if (settings.Get<UpdateSettings>().AutoUpdate) await CheckAsync(manual: false);
        };
        _timer.Start();
    }

    /// <summary>Removes the exe left behind by the previous update. Call after the old process has exited.</summary>
    public static void CleanUp()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try { File.Delete(exe + ".old"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // try again next start
    }

    /// <summary>Waits for the process that restarted us (an update) so its single-instance lock is gone.</summary>
    public static void WaitForPrevious(string[] args)
    {
        var i = Array.IndexOf(args, WaitArg);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid)) return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException) { } // already gone
    }

    /// <param name="manual">From the settings button: report "up to date" and errors instead of staying quiet.</param>
    public async Task CheckAsync(bool manual)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var release = await GetLatestAsync();
            if (release is null || release.Version <= Current)
            {
                if (manual) Notify("You're up to date", $"Version {Current.ToString(3)}", "", "AccentGreen");
                return;
            }

            var tag = release.Version.ToString(3);
            if (!CanSelfUpdate || release.AssetUrl is null)
            {
                if (manual)
                {
                    Notify($"Version {tag} is available", "Opening the download page", "", "AccentClaude");
                    Process.Start(new ProcessStartInfo(release.PageUrl) { UseShellExecute = true });
                }
                return;
            }

            Notify($"Updating to {tag}", "Downloading…", "", "AccentClaude", 30);
            await InstallAsync(release);
            Restart();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or UnauthorizedAccessException or JsonException)
        {
            if (manual || ex is IOException or UnauthorizedAccessException)
                Notify("Update failed", ex.Message, "", "AccentRed", 8);
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task<Release?> GetLatestAsync()
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestUrl));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null; // e.g. the old "pre-release" tag

        string? assetUrl = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!(asset.GetProperty("name").GetString() ?? "").EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            assetUrl = asset.GetProperty("browser_download_url").GetString();
            size = asset.GetProperty("size").GetInt64();
            break;
        }
        return new Release(Normalize(version), root.GetProperty("html_url").GetString() ?? "", assetUrl, size);
    }

    private static async Task InstallAsync(Release release)
    {
        var exe = Environment.ProcessPath!;
        var downloaded = exe + ".new";
        var old = exe + ".old";

        using (var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(downloaded);
            await response.Content.CopyToAsync(file);
        }
        if (new FileInfo(downloaded).Length != release.Size)
        {
            File.Delete(downloaded);
            throw new IOException("The download was incomplete.");
        }

        File.Move(exe, old, overwrite: true);
        try
        {
            File.Move(downloaded, exe);
        }
        catch
        {
            File.Move(old, exe); // put the running version back so the next launch still works
            throw;
        }
    }

    private void Restart()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add(UpdatedArg);
        start.ArgumentList.Add(WaitArg);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(start);
        quit();
    }

    private void Notify(string title, string body, string icon, string accent, double seconds = 5) =>
        notifier.Show(title, body, icon, Notifier.Brush(accent), seconds);

    /// <summary>"0.2" and "0.2.0.0" compare equal.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private sealed record Release(Version Version, string PageUrl, string? AssetUrl, long Size);
}
