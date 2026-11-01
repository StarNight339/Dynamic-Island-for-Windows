using System.IO;
using System.Text.Json;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core;

/// <summary>
/// Files dropped on the island, kept by path (nothing is copied), newest first, saved to shelf.json so the
/// shelf survives a restart. Pure logic with no UI so it can be tested on its own.
/// </summary>
public sealed class FileShelf
{
    private readonly string _path;
    private readonly List<string> _items = [];
    private int _maxItems = 30;

    public event Action? Changed;

    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "shelf.json");

    public FileShelf(string path)
    {
        _path = path;
        _items.AddRange(Load(path));
    }

    public IReadOnlyList<string> Items => _items;

    public int MaxItems
    {
        get => _maxItems;
        set
        {
            _maxItems = Math.Max(1, value);
            if (Trim()) Commit();
        }
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Adds existing paths to the front (keeping their order), moving ones already on the shelf. Returns how many were added.</summary>
    public int Add(IEnumerable<string> paths)
    {
        var fresh = paths
            .Where(p => !string.IsNullOrWhiteSpace(p) && Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (fresh.Count == 0) return 0;

        var set = new HashSet<string>(fresh, StringComparer.OrdinalIgnoreCase);
        _items.RemoveAll(set.Contains);
        _items.InsertRange(0, fresh);
        Trim();
        Commit();
        return fresh.Count;
    }

    public void Remove(string path)
    {
        if (_items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0) Commit();
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Commit();
    }

    /// <summary>Drops entries whose file or folder was moved or deleted. Returns true if anything changed.</summary>
    public bool Prune()
    {
        if (_items.RemoveAll(p => !Exists(p)) == 0) return false;
        Commit();
        return true;
    }

    private bool Trim()
    {
        if (_items.Count <= _maxItems) return false;
        _items.RemoveRange(_maxItems, _items.Count - _maxItems);
        return true;
    }

    private void Commit()
    {
        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The shelf stays in memory; the next change retries the write.
        }
    }

    private static List<string> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            // Unreadable file: keep a copy instead of silently overwriting it.
            try { File.Copy(path, path + ".bak", overwrite: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
