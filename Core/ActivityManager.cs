using System.Windows.Threading;

namespace DynamicIsland.Core;

/// <summary>
/// Priority queue of activities. Transient ones (notification, volume) carry an expiry and pre-empt
/// long-running ones (media, timer); once they expire the island falls back to whatever is next.
/// All members must be called on the UI thread.
/// </summary>
public sealed class ActivityManager
{
    private readonly Dictionary<string, Activity> _items = new();
    private readonly DispatcherTimer _expiryTimer;

    public Activity? Current { get; private set; }

    /// <summary>Raised when <see cref="Current"/> changes (including to null).</summary>
    public event Action? CurrentChanged;

    public ActivityManager()
    {
        _expiryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _expiryTimer.Tick += (_, _) => PurgeExpired();
        _expiryTimer.Start();
    }

    /// <summary>Adds or replaces an activity. Re-posting the same instance just refreshes its expiry.</summary>
    public void Post(Activity activity, TimeSpan? duration = null)
    {
        activity.PostedAt = DateTime.UtcNow;
        activity.ExpiresAt = duration is { } d ? DateTime.UtcNow + d : null;
        _items[activity.Id] = activity;
        Recompute();
    }

    public void Remove(string id)
    {
        if (_items.Remove(id))
            Recompute();
    }

    public bool Contains(string id) => _items.ContainsKey(id);

    /// <summary>Re-evaluates which activity wins. Call after changing an activity's priority.</summary>
    public void Recompute()
    {
        var best = _items.Values
            .OrderByDescending(a => a.Priority)
            .ThenByDescending(a => a.PostedAt)
            .FirstOrDefault();

        if (!ReferenceEquals(best, Current))
        {
            Current = best;
            CurrentChanged?.Invoke();
        }
    }

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        var expired = _items.Values.Where(a => a.ExpiresAt <= now).Select(a => a.Id).ToList();
        if (expired.Count == 0) return;
        foreach (var id in expired) _items.Remove(id);
        Recompute();
    }
}
