using DynamicIsland.Views;

namespace DynamicIsland.Core;

public enum IslandState
{
    Idle,
    Compact,
    Expanded,
}

/// <summary>Standard priorities. Higher wins the island.</summary>
public static class Priority
{
    public const int MediaPaused = 20;
    public const int Media = 40;
    public const int Timer = 50;
    public const int ClaudeWorking = 60;
    public const int Progress = 70;
    public const int Battery = 80;
    public const int Volume = 90;
    public const int Notification = 100;
    public const int QuickPanel = 110;
}

/// <summary>Something the island can show. Providers keep a reference and update <see cref="View"/> in place.</summary>
public sealed class Activity
{
    public required string Id { get; init; }
    public required IslandView View { get; init; }
    public int Priority { get; set; }

    /// <summary>Open in expanded form while it is current (notifications).</summary>
    public bool AutoExpand { get; init; }

    public DateTime PostedAt { get; internal set; }
    public DateTime? ExpiresAt { get; internal set; }
}
