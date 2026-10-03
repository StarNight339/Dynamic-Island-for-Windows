using System.IO;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using DynamicIsland.Views;

namespace DynamicIsland.Providers;

/// <summary>
/// Tracks Claude Code sessions from hook events. One persistent "working" activity per session, plus
/// transient notifications when Claude needs attention or finishes.
/// </summary>
public sealed class ClaudeProvider
{
    private readonly ActivityManager _activities;
    private readonly Notifier _notifier;
    private readonly SettingsStore _settings;
    private readonly Dictionary<string, (Activity Activity, ClaudeView View)> _sessions = new();

    public ClaudeProvider(ActivityManager activities, Notifier notifier, SettingsStore settings)
    {
        _activities = activities;
        _notifier = notifier;
        _settings = settings;
        settings.Changed += id =>
        {
            if (id == ClaudeSettings.SectionId && !settings.Get<ClaudeSettings>().Enabled) Clear();
        };
    }

    private void Clear()
    {
        foreach (var key in _sessions.Keys) _activities.Remove(key);
        _sessions.Clear();
    }

    /// <param name="hookEvent">
    /// A Claude Code hook name (UserPromptSubmit, PreToolUse, Notification, Stop, SessionEnd, ...) or a
    /// short alias: working / waiting / done / idle.
    /// </param>
    public void Handle(string hookEvent, string? sessionId, string? cwd, string? message, string? toolName, string? prompt,
        string? projectOverride = null)
    {
        if (!_settings.Get<ClaudeSettings>().Enabled) return;

        var key = "claude:" + (string.IsNullOrEmpty(sessionId) ? "default" : sessionId);
        var project = projectOverride ?? ProjectName(cwd);

        switch (hookEvent.ToLowerInvariant())
        {
            case "userpromptsubmit":
            case "working":
            {
                var s = GetOrCreate(key);
                s.View.Restart();
                s.View.Set(ClaudeView.Phase.Working, project, Truncate(prompt ?? message, 120));
                _activities.Post(s.Activity);
                break;
            }

            case "pretooluse":
            case "posttooluse":
            {
                var s = GetOrCreate(key);
                if (!_activities.Contains(key))
                {
                    s.View.Restart();
                    _activities.Post(s.Activity);
                }
                var detail = string.IsNullOrEmpty(toolName) ? message : $"Running {toolName}";
                s.View.Set(ClaudeView.Phase.Working, project, detail ?? "");
                break;
            }

            case "notification":
            case "permissionrequest":
            case "waiting":
            {
                // Only flip an in-progress session; the idle "waiting for input" ping after Stop shouldn't revive it.
                if (_sessions.TryGetValue(key, out var s) && _activities.Contains(key))
                    s.View.Set(ClaudeView.Phase.Waiting, project, message ?? "");
                _notifier.Show("Claude needs you", Join(message ?? "Waiting for your input", project), "\uE7BA",
                    Notifier.Brush("AccentClaude"), 8);
                break;
            }

            case "stop":
            case "done":
            {
                _activities.Remove(key);
                _notifier.Show("Claude finished", Join(message, project), "\uE73E", Notifier.Brush("AccentGreen"), 5);
                break;
            }

            case "sessionend":
            case "idle":
                _activities.Remove(key);
                _sessions.Remove(key);
                break;
        }
    }

    private (Activity Activity, ClaudeView View) GetOrCreate(string key)
    {
        if (_sessions.TryGetValue(key, out var s)) return s;
        var view = new ClaudeView();
        s = (new Activity { Id = key, View = view, Priority = Priority.ClaudeWorking }, view);
        _sessions[key] = s;
        return s;
    }

    private static string ProjectName(string? cwd) =>
        string.IsNullOrWhiteSpace(cwd) ? "Claude Code" : Path.GetFileName(cwd.TrimEnd('\\', '/'));

    private static string Join(string? a, string b) => string.IsNullOrWhiteSpace(a) ? b : $"{a} · {b}";

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.ReplaceLineEndings(" ").Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
