using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using DynamicIsland.Views;
using Windows.System.Power;

namespace DynamicIsland.Providers;

/// <summary>Charger plug-in flash, low-battery warnings, and the battery readout on the idle clock.</summary>
public sealed class BatteryProvider
{
    private const string Id = "battery";

    private readonly ActivityManager _activities;
    private readonly Notifier _notifier;
    private readonly ClockView _clock;
    private readonly Dispatcher _dispatcher;
    private readonly SettingsStore _settings;
    private readonly StatusView _view = new();
    private readonly Activity _activity;

    private bool _pluggedIn;
    private int _lastWarnedAt = 101;

    public BatteryProvider(ActivityManager activities, Notifier notifier, ClockView clock, Dispatcher dispatcher,
        SettingsStore settings)
    {
        _settings = settings;
        _activities = activities;
        _notifier = notifier;
        _clock = clock;
        _dispatcher = dispatcher;
        _activity = new Activity { Id = Id, View = _view, Priority = Priority.Battery };
    }

    public void Start()
    {
        if (PowerManager.BatteryStatus == BatteryStatus.NotPresent) return; // desktop PC

        _pluggedIn = IsPluggedIn();
        UpdateClock();
        PowerManager.PowerSupplyStatusChanged += (_, _) => _dispatcher.InvokeAsync(OnSupplyChanged);
        PowerManager.RemainingChargePercentChanged += (_, _) => _dispatcher.InvokeAsync(OnPercentChanged);
        PowerManager.BatteryStatusChanged += (_, _) => _dispatcher.InvokeAsync(UpdateClock);
    }

    private static bool IsPluggedIn() => PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;

    private void OnSupplyChanged()
    {
        var plugged = IsPluggedIn();
        UpdateClock();
        if (plugged == _pluggedIn) return;
        _pluggedIn = plugged;

        if (plugged) _lastWarnedAt = 101;
        var settings = _settings.Get<BatterySettings>();
        if (!settings.Enabled || !settings.ShowChargingFlash) return;

        var percent = PowerManager.RemainingChargePercent;
        if (plugged)
        {
            _view.Set(ClockView.BatteryGlyph(percent, true), "Charging", percent / 100.0, $"{percent}%",
                Notifier.Brush("AccentGreen"));
        }
        else
        {
            _view.Set(ClockView.BatteryGlyph(percent, false), "On battery", percent / 100.0, $"{percent}%",
                Notifier.Brush(percent <= 20 ? "AccentRed" : "TextPrimary"));
        }
        _activities.Post(_activity, TimeSpan.FromSeconds(3));
    }

    private void OnPercentChanged()
    {
        UpdateClock();
        var settings = _settings.Get<BatterySettings>();
        if (_pluggedIn || !settings.Enabled) return;

        var percent = PowerManager.RemainingChargePercent;
        foreach (var threshold in settings.LowThresholds.Order())
        {
            if (percent <= threshold && _lastWarnedAt > threshold)
            {
                _lastWarnedAt = threshold;
                _notifier.Show("Low Battery", $"{percent}% battery remaining", "\uE850", Notifier.Brush("AccentRed"), 6);
                break;
            }
        }
    }

    private void UpdateClock() => _clock.SetBattery(PowerManager.RemainingChargePercent, IsPluggedIn());
}
