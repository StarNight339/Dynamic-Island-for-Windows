using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Views;
using NAudio.CoreAudioApi;

namespace DynamicIsland.Providers;

/// <summary>Shows a volume bar whenever the default output device's master volume / mute changes.</summary>
public sealed class VolumeProvider : IDisposable
{
    private const string Id = "volume";

    private readonly ActivityManager _activities;
    private readonly Dispatcher _dispatcher;
    private readonly StatusView _view = new();
    private readonly Activity _activity;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DispatcherTimer _devicePoll = new() { Interval = TimeSpan.FromSeconds(2) };

    private MMDevice? _device;
    private DateTime _attachedAt;

    public VolumeProvider(ActivityManager activities, Dispatcher dispatcher)
    {
        _activities = activities;
        _dispatcher = dispatcher;
        _activity = new Activity { Id = Id, View = _view, Priority = Priority.Volume };
    }

    public void Start()
    {
        Attach();
        // Cheaper than an IMMNotificationClient and good enough: follow default-device switches (headphones etc.).
        _devicePoll.Tick += (_, _) =>
        {
            if (!_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var d)) return;
            var changed = d.ID != _device?.ID;
            d.Dispose();
            if (changed) Attach();
        };
        _devicePoll.Start();
    }

    private void Attach()
    {
        if (_device is not null)
        {
            _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
            _device.Dispose();
            _device = null;
        }
        if (!_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device)) return;

        _device = device;
        _attachedAt = DateTime.UtcNow;
        _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
    }

    private void OnVolume(AudioVolumeNotificationData data)
    {
        // Attaching can echo the current state back; that isn't a user change.
        if (DateTime.UtcNow - _attachedAt < TimeSpan.FromMilliseconds(500)) return;
        _dispatcher.InvokeAsync(() => Show(data.MasterVolume, data.Muted));
    }

    private void Show(float level, bool muted)
    {
        var percent = (int)Math.Round(level * 100);
        var glyph = muted || percent == 0 ? "\uE74F"
            : percent < 34 ? "\uE993"
            : percent < 67 ? "\uE994"
            : "\uE995";
        _view.Set(glyph, muted ? "Muted" : "Volume", muted ? 0 : level, muted ? "" : percent.ToString(),
            Notifier.Brush(muted ? "TextSecondary" : "TextPrimary"));
        _activities.Post(_activity, TimeSpan.FromSeconds(1.6));
    }

    public void Dispose()
    {
        _devicePoll.Stop();
        if (_device is not null) _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
        _device?.Dispose();
        _enumerator.Dispose();
    }
}
