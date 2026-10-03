using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace DynamicIsland.Core.Settings;

/// <summary>A settings section stored under its own key in settings.json.</summary>
public interface ISettingsModel
{
    static abstract string SectionId { get; }
}

/// <summary>
/// settings.json as one JSON object keyed by section id. Each section is deserialized lazily into a single
/// shared instance; keys nobody asked for (older/newer versions) are kept as-is. Must be used on the UI thread.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly JsonObject _root;
    private readonly Dictionary<string, object> _sections = new();
    private readonly DispatcherTimer _saveDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };

    /// <summary>Raised with the section id after a section was changed through <see cref="Save{T}"/>.</summary>
    public event Action<string>? Changed;

    public string FilePath => _path;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "settings.json");

    public SettingsStore(string path)
    {
        _path = path;
        _root = Load(path);
        _saveDelay.Tick += (_, _) => Flush();
    }

    public T Get<T>() where T : class, ISettingsModel, new()
    {
        if (_sections.TryGetValue(T.SectionId, out var existing)) return (T)existing;

        T value;
        try { value = _root[T.SectionId]?.Deserialize<T>(Json) ?? new T(); }
        catch (JsonException) { value = new T(); }
        _sections[T.SectionId] = value;
        return value;
    }

    /// <summary>Notifies listeners and schedules a write. Call after mutating the instance from <see cref="Get{T}"/>.</summary>
    public void Save<T>() where T : class, ISettingsModel, new()
    {
        _root[T.SectionId] = JsonSerializer.SerializeToNode(Get<T>(), Json);
        Changed?.Invoke(T.SectionId);
        _saveDelay.Stop();
        _saveDelay.Start();
    }

    /// <summary>Writes pending changes now (also called on exit).</summary>
    public void Flush()
    {
        if (!_saveDelay.IsEnabled) return;
        _saveDelay.Stop();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, _root.ToJsonString(Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings stay in memory; the next change retries the write.
        }
    }

    private static JsonObject Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new JsonObject();
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject root) return root;
        }
        catch (JsonException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new JsonObject();
        }

        // Unreadable file: keep a copy for the user instead of silently overwriting it.
        try { File.Copy(path, path + ".bak", overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new JsonObject();
    }
}
