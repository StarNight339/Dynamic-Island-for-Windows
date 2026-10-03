namespace DynamicIsland.Core.Settings;

/// <summary>A page in the settings window (and the source of quick-panel toggles).</summary>
public interface ISettingsSection
{
    string Title { get; }

    /// <summary>Segoe Fluent glyph for the sidebar.</summary>
    string Icon { get; }

    /// <summary>Creates fresh items each call; callers own them for as long as they are shown.</summary>
    IEnumerable<SettingItem> Build();
}

/// <summary>Section backed by one <see cref="ISettingsModel"/>; the helpers save and notify on every change.</summary>
public abstract class SettingsSection<T>(SettingsStore store) : ISettingsSection where T : class, ISettingsModel, new()
{
    protected SettingsStore Store => store;
    protected T Model => store.Get<T>();

    public abstract string Title { get; }
    public abstract string Icon { get; }
    public abstract IEnumerable<SettingItem> Build();

    protected ToggleItem Toggle(string label, string? description, Func<T, bool> get, Action<T, bool> set,
        bool quick = false, string? icon = null) => new()
    {
        Label = label,
        Description = description,
        Quick = quick,
        Icon = icon ?? Icon,
        Get = () => get(Model),
        Set = v => { set(Model, v); store.Save<T>(); },
    };

    protected NumberItem Number(string label, string? description, Func<T, int> get, Action<T, int> set,
        int min, int max, int step = 1, string unit = "", bool textBox = false) => new()
    {
        Label = label,
        Description = description,
        Get = () => get(Model),
        Set = v => { set(Model, v); store.Save<T>(); },
        Min = min,
        Max = max,
        Step = step,
        Unit = unit,
        UseTextBox = textBox,
    };

    protected NumberListItem NumberList(string label, string? description, Func<T, List<int>> get,
        int min, int max, Func<int, string> format) => new()
    {
        Label = label,
        Description = description,
        Get = () => get(Model),
        Saved = store.Save<T>,
        Min = min,
        Max = max,
        Format = format,
    };
}

/// <summary>All settings pages, in sidebar order.</summary>
public sealed class SettingsRegistry(IReadOnlyList<ISettingsSection> sections)
{
    public IReadOnlyList<ISettingsSection> Sections => sections;

    public IEnumerable<ToggleItem> BuildQuickToggles() =>
        sections.SelectMany(s => s.Build()).OfType<ToggleItem>().Where(t => t.Quick);
}
