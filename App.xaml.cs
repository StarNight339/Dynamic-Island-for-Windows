using System.Net;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using DynamicIsland.Providers;
using DynamicIsland.Settings.Sections;
using DynamicIsland.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DynamicIsland;

public partial class App : Application
{
    private const string QuickPanelId = "quick";

    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _tray;
    private Drawing.Icon? _icon;
    private VolumeProvider? _volume;
    private HttpApiProvider? _api;
    private SettingsStore? _settings;
    private SettingsRegistry? _registry;
    private SettingsWindow? _settingsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\DynamicIsland.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        var settings = _settings = new SettingsStore(SettingsStore.DefaultPath);
        var activities = new ActivityManager();
        var notifier = new Notifier(activities);
        var clock = new ClockView();
        var window = new IslandWindow(activities, clock, settings);

        var timer = new ClockTimerProvider(activities, notifier, clock, settings);
        var claude = new ClaudeProvider(activities, notifier, settings);

        // Sidebar order. A new system adds its section here (see README "Adding a settings page").
        _registry = new SettingsRegistry([
            new GeneralSection(settings),
            new TimerSection(settings),
            new BatterySection(settings),
            new MediaSection(settings),
            new VolumeSection(settings),
            new ClaudeSection(settings),
            new ApiSection(settings, notifier),
            new AboutSection(settings),
        ]);

        SetUpQuickPanel(window, activities, timer);
        window.Show();

        CreateTray();

        new BatteryProvider(activities, notifier, clock, Dispatcher, settings).Start();
        try
        {
            _volume = new VolumeProvider(activities, Dispatcher, settings);
            _volume.Start();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException)
        {
            _volume = null; // no audio endpoint (e.g. RDP without audio); the island just won't show volume
        }

        var api = settings.Get<ApiSettings>();
        if (api.Enabled)
        {
            try
            {
                _api = new HttpApiProvider(api.Port, Dispatcher, notifier, claude, timer);
                _api.Start();
            }
            catch (HttpListenerException ex)
            {
                _api = null;
                notifier.Show("API unavailable", $"Port {api.Port}: {ex.Message}", "\uE7BA", Notifier.Brush("AccentRed"), 8);
            }
        }

        try
        {
            await new MediaProvider(activities, Dispatcher, settings).StartAsync();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Media session API unavailable; everything else still works.
        }
    }

    /// <summary>Right-click turns the island into a panel of quick timers and toggles; leaving it closes it.</summary>
    private void SetUpQuickPanel(IslandWindow window, ActivityManager activities, ClockTimerProvider timer)
    {
        var view = new QuickPanelView();
        var activity = new Activity { Id = QuickPanelId, View = view, Priority = Priority.QuickPanel, AutoExpand = true };
        void Close() => activities.Remove(QuickPanelId);

        window.QuickPanelRequested += () =>
        {
            if (activities.Contains(QuickPanelId))
            {
                Close();
                return;
            }
            view.Populate(_settings!.Get<TimerSettings>().Presets, timer.IsRunning, _registry!.BuildQuickToggles().ToList());
            activities.Post(activity);
        };
        window.HoverEnded += Close;

        view.TimerRequested += minutes =>
        {
            Close();
            timer.Start(TimeSpan.FromMinutes(minutes));
        };
        view.CancelTimerRequested += () =>
        {
            Close();
            timer.Cancel();
        };
        view.SettingsRequested += () =>
        {
            Close();
            ShowSettings();
        };
        view.ExitRequested += Quit;
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_registry!, _settings!) { Icon = ToImageSource(_icon!) };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    private void CreateTray()
    {
        _icon = CreateTrayIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());

        _tray = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Dynamic Island",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowSettings();
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

    private static ImageSource ToImageSource(Drawing.Icon icon) =>
        Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

    private void Quit()
    {
        _settings?.Flush();
        if (_tray is not null) _tray.Visible = false;
        _tray?.Dispose();
        _api?.Dispose();
        _volume?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settings?.Flush();
        if (_tray is not null) _tray.Visible = false;
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
