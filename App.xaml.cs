using System.Net;
using System.Windows;
using DynamicIsland.Core;
using DynamicIsland.Providers;
using DynamicIsland.Views;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DynamicIsland;

public partial class App : Application
{
    private const int ApiPort = 5179;
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DynamicIsland";

    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _tray;
    private VolumeProvider? _volume;
    private HttpApiProvider? _api;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\DynamicIsland.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        var activities = new ActivityManager();
        var notifier = new Notifier(activities);
        var clock = new ClockView();
        var window = new IslandWindow(activities, clock);

        var timer = new ClockTimerProvider(activities, notifier, clock);
        var claude = new ClaudeProvider(activities, notifier);
        window.TimerMenuRequested += minutes =>
        {
            if (minutes <= 0) timer.Cancel();
            else timer.Start(TimeSpan.FromMinutes(minutes));
        };
        window.ExitRequested += Quit;
        window.Show();

        CreateTray(timer);

        new BatteryProvider(activities, notifier, clock, Dispatcher).Start();
        try
        {
            _volume = new VolumeProvider(activities, Dispatcher);
            _volume.Start();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException)
        {
            _volume = null; // no audio endpoint (e.g. RDP without audio); the island just won't show volume
        }

        try
        {
            _api = new HttpApiProvider(ApiPort, Dispatcher, notifier, claude, timer);
            _api.Start();
        }
        catch (HttpListenerException ex)
        {
            _api = null;
            notifier.Show("API unavailable", $"Port {ApiPort}: {ex.Message}", "\uE7BA", Notifier.Brush("AccentRed"), 8);
        }

        try
        {
            await new MediaProvider(activities, Dispatcher).StartAsync();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Media session API unavailable; everything else still works.
        }
    }

    private void CreateTray(ClockTimerProvider timer)
    {
        var menu = new Forms.ContextMenuStrip();
        var startup = new Forms.ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled(), CheckOnClick = true };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked);
        menu.Items.Add($"API: http://localhost:{ApiPort}/").Enabled = false;
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Timer 5 min", null, (_, _) => timer.Start(TimeSpan.FromMinutes(5)));
        menu.Items.Add("Cancel timer", null, (_, _) => timer.Cancel());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(startup);
        menu.Items.Add("Exit", null, (_, _) => Quit());

        _tray = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Dynamic Island",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    /// <summary>A small black capsule, drawn at runtime so the repo needs no binary assets.</summary>
    private static Drawing.Icon CreateTrayIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = new Drawing.Drawing2D.GraphicsPath();
            path.AddArc(2, 10, 12, 12, 90, 180);
            path.AddArc(18, 10, 12, 12, 270, 180);
            path.CloseFigure();
            g.FillPath(Drawing.Brushes.Black, path);
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(200, 255, 255, 255), 1.5f);
            g.DrawPath(pen, path);
            g.FillEllipse(Drawing.Brushes.LimeGreen, 21, 13, 6, 6);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;
        if (enabled) key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private void Quit()
    {
        if (_tray is not null) _tray.Visible = false;
        _tray?.Dispose();
        _api?.Dispose();
        _volume?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null) _tray.Visible = false;
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
