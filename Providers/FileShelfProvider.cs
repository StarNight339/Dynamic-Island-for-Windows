using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using DynamicIsland.Views;

namespace DynamicIsland.Providers;

/// <summary>
/// The file shelf: files dragged onto the island are kept (by path) and can be copied, have their path copied,
/// be opened, shown in Explorer, dragged back out or removed. Two activities share one view: a resting pill
/// while files are on the shelf, and an open panel (above notifications) during a drag or from the quick panel.
/// </summary>
public sealed class FileShelfProvider
{
    private const string OpenId = "shelf.open";
    private const string PillId = "shelf";

    private readonly ActivityManager _activities;
    private readonly IslandWindow _window;
    private readonly SettingsStore _settings;
    private readonly FileShelf _shelf;
    private readonly FileShelfView _view = new();
    private readonly Activity _open;
    private readonly Activity _pill;

    public FileShelfProvider(ActivityManager activities, IslandWindow window, SettingsStore settings, string? path = null)
    {
        _activities = activities;
        _window = window;
        _settings = settings;
        _shelf = new FileShelf(path ?? FileShelf.DefaultPath) { MaxItems = Settings.MaxItems };
        _open = new Activity { Id = OpenId, View = _view, Priority = Priority.ShelfOpen, AutoExpand = true };
        _pill = new Activity { Id = PillId, View = _view, Priority = Priority.Shelf };

        _shelf.Changed += Refresh;
        window.AcceptsFiles = Settings.Enabled;
        window.FileDragEntered += OnFileDragEntered;
        window.FilesDropped += OnFilesDropped;
        window.HoverEnded += Close;

        _view.CopyRequested += paths => Copy(paths, asPaths: false);
        _view.CopyPathRequested += paths => Copy(paths, asPaths: true);
        _view.OpenRequested += path => Run(path, FileActions.Open);
        _view.RevealRequested += path => Run(path, FileActions.Reveal);
        _view.RemoveRequested += _shelf.Remove;
        _view.ClearRequested += () =>
        {
            _shelf.Clear();
            _view.Flash("Shelf cleared");
        };
        _view.DragOutFinished += window.RecheckHover;

        settings.Changed += id =>
        {
            if (id != FileShelfSettings.SectionId) return;
            window.AcceptsFiles = Settings.Enabled;
            _shelf.MaxItems = Settings.MaxItems;
            if (!Settings.Enabled) Close();
            Refresh();
        };

        _shelf.Prune();
        Refresh();
    }

    public FileShelf Shelf => _shelf;

    private FileShelfSettings Settings => _settings.Get<FileShelfSettings>();

    /// <summary>Opens the shelf panel (quick panel button). It closes when the pointer leaves the island.</summary>
    public void Open()
    {
        if (!Settings.Enabled) return;
        _shelf.Prune();
        if (!_activities.Contains(OpenId)) _activities.Post(_open);
    }

    private void Close()
    {
        _view.ShowDropHint(false);
        _activities.Remove(OpenId);
    }

    private void Refresh()
    {
        _view.Populate(_shelf.Items);
        if (Settings.Enabled && Settings.ShowPill && _shelf.Items.Count > 0)
        {
            if (!_activities.Contains(PillId)) _activities.Post(_pill);
        }
        else
        {
            _activities.Remove(PillId);
        }
    }

    private void OnFileDragEntered()
    {
        if (!Settings.Enabled) return;
        _view.ShowDropHint(true);
        Open();
    }

    private void OnFilesDropped(string[] paths)
    {
        _view.ShowDropHint(false);
        if (!Settings.Enabled) return;
        Open();
        var added = _shelf.Add(paths);
        if (added == 0) _view.Flash("Nothing new to add", Notifier.Brush("TextSecondary"));
        else _view.Flash(added == 1 ? "Added 1 file" : $"Added {added} files");
    }

    private void Copy(string[] paths, bool asPaths)
    {
        var existing = paths.Where(FileShelf.Exists).ToArray();
        if (existing.Length < paths.Length) _shelf.Prune();
        if (existing.Length == 0)
        {
            Fail("File is gone");
            return;
        }

        var ok = asPaths ? FileActions.CopyPaths(existing) : FileActions.CopyFiles(existing);
        if (!ok)
        {
            Fail("Clipboard is busy, try again");
            return;
        }
        var what = asPaths
            ? existing.Length == 1 ? "path" : $"{existing.Length} paths"
            : existing.Length == 1 ? "file" : $"{existing.Length} files";
        _view.Flash($"Copied {what}");
    }

    private void Run(string path, Func<string, bool> action)
    {
        if (!FileShelf.Exists(path))
        {
            _shelf.Prune();
            Fail("File is gone");
            return;
        }
        if (!action(path)) Fail("Couldn't open it");
    }

    private void Fail(string message) => _view.Flash(message, Notifier.Brush("AccentRed"));
}
