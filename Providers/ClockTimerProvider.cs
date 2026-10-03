using System.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using DynamicIsland.Views;

namespace DynamicIsland.Providers;

/// <summary>Countdown timer. The clock itself lives in <see cref="ClockView"/> (the idle island).</summary>
public sealed class ClockTimerProvider
{
    private const string Id = "timer";

    private readonly ActivityManager _activities;
    private readonly Notifier _notifier;
    private readonly SettingsStore _settings;
    private readonly TimerView _view = new();
    private readonly Activity _activity;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime _endsAt;

    public bool IsRunning => _tick.IsEnabled;

    public ClockTimerProvider(ActivityManager activities, Notifier notifier, ClockView clock, SettingsStore settings)
    {
        _activities = activities;
        _notifier = notifier;
        _settings = settings;
        _activity = new Activity { Id = Id, View = _view, Priority = Priority.Timer };

        _tick.Tick += (_, _) => OnTick();
        _view.CancelRequested += Cancel;
        _view.AddMinuteRequested += () => _endsAt += TimeSpan.FromMinutes(1);
        clock.TimerRequested += minutes => Start(TimeSpan.FromMinutes(minutes));
        clock.SetTimerPresets(settings.Get<TimerSettings>().Presets);
        settings.Changed += id =>
        {
            if (id == TimerSettings.SectionId) clock.SetTimerPresets(settings.Get<TimerSettings>().Presets);
        };
    }

    public void Start(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;
        _endsAt = DateTime.UtcNow + duration;
        _view.SetRemaining(duration);
        _tick.Start();
        _activities.Post(_activity);
    }

    public void Cancel()
    {
        _tick.Stop();
        _activities.Remove(Id);
    }

    private void OnTick()
    {
        var remaining = _endsAt - DateTime.UtcNow;
        _view.SetRemaining(remaining);
        if (remaining > TimeSpan.Zero) return;

        Cancel();
        if (_settings.Get<TimerSettings>().PlaySound) SystemSounds.Asterisk.Play();
        _notifier.Show("Timer done", null, "\uEA8F", Notifier.Brush("AccentOrange"), 6);
    }
}
