using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Views;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using GsmtcManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GsmtcSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace DynamicIsland.Providers;

/// <summary>
/// Now-playing info from the system media transport controls (Spotify, browsers, Media Player, ...).
/// Shown while playing; after pausing it lingers for a while at low priority so it can be resumed.
/// </summary>
public sealed class MediaProvider
{
    private const string Id = "media";
    private static readonly TimeSpan PausedLinger = TimeSpan.FromSeconds(90);

    private readonly ActivityManager _activities;
    private readonly Dispatcher _dispatcher;
    private readonly MediaView _view = new();
    private readonly Activity _activity;

    private GsmtcManager? _manager;
    private GsmtcSession? _session;
    private bool _lastPlaying;
    private string? _artKey;
    private ImageSource? _art;

    private readonly TypedEventHandler<GsmtcSession, MediaPropertiesChangedEventArgs> _onProps;
    private readonly TypedEventHandler<GsmtcSession, PlaybackInfoChangedEventArgs> _onPlayback;
    private readonly TypedEventHandler<GsmtcSession, TimelinePropertiesChangedEventArgs> _onTimeline;

    public MediaProvider(ActivityManager activities, Dispatcher dispatcher)
    {
        _activities = activities;
        _dispatcher = dispatcher;
        _activity = new Activity { Id = Id, View = _view, Priority = Priority.Media };

        _onProps = (_, _) => QueueRefresh();
        _onPlayback = (_, _) => QueueRefresh();
        _onTimeline = (_, _) => QueueRefresh();

        _view.PlayPauseRequested += () => Try(s => s.TryTogglePlayPauseAsync());
        _view.NextRequested += () => Try(s => s.TrySkipNextAsync());
        _view.PreviousRequested += () => Try(s => s.TrySkipPreviousAsync());
    }

    public async Task StartAsync()
    {
        _manager = await GsmtcManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => _dispatcher.InvokeAsync(AttachCurrentSession);
        AttachCurrentSession();
    }

    private void AttachCurrentSession()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= _onProps;
            _session.PlaybackInfoChanged -= _onPlayback;
            _session.TimelinePropertiesChanged -= _onTimeline;
        }

        _session = _manager?.GetCurrentSession();
        if (_session is null)
        {
            _lastPlaying = false;
            _activities.Remove(Id);
            return;
        }

        _session.MediaPropertiesChanged += _onProps;
        _session.PlaybackInfoChanged += _onPlayback;
        _session.TimelinePropertiesChanged += _onTimeline;
        _ = RefreshAsync();
    }

    private void QueueRefresh() => _dispatcher.InvokeAsync(() => _ = RefreshAsync());

    private async Task RefreshAsync()
    {
        var session = _session;
        if (session is null) return;

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var playing = session.GetPlaybackInfo()?.PlaybackStatus ==
                          GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var timeline = session.GetTimelineProperties();

            var key = $"{props.Title}\n{props.Artist}";
            if (key != _artKey)
            {
                _artKey = key;
                _art = await LoadArtAsync(props.Thumbnail);
            }
            if (!ReferenceEquals(session, _session)) return; // session changed while awaiting

            _view.Update(props.Title, props.Artist, _art, playing);
            if (timeline is not null)
            {
                _view.UpdateTimeline(timeline.Position - timeline.StartTime,
                    timeline.EndTime - timeline.StartTime, timeline.LastUpdatedTime.UtcDateTime);
            }

            var shown = _activities.Contains(Id);
            if (playing && (!shown || !_lastPlaying))
            {
                _activity.Priority = Priority.Media;
                _activities.Post(_activity);
            }
            else if (!playing && shown && _lastPlaying)
            {
                _activity.Priority = Priority.MediaPaused;
                _activities.Post(_activity, PausedLinger);
            }
            _lastPlaying = playing;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Session went away mid-query; the next CurrentSessionChanged will sort it out.
        }
    }

    private static async Task<ImageSource?> LoadArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var ras = await thumbnail.OpenReadAsync();
            using var source = ras.AsStreamForRead();
            var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            buffer.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 128;
            bmp.StreamSource = buffer;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null; // Some players hand out thumbnails that cannot be decoded; just show no art.
        }
    }

    private async void Try(Func<GsmtcSession, IAsyncOperation<bool>> action)
    {
        if (_session is null) return;
        try { await action(_session); }
        catch (System.Runtime.InteropServices.COMException) { }
    }
}
