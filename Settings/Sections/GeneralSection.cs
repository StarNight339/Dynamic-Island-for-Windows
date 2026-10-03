using DynamicIsland.Core;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

public sealed class GeneralSection(SettingsStore store) : SettingsSection<GeneralSettings>(store)
{
    public override string Title => "General";
    public override string Icon => "\uE713";

    public override IEnumerable<SettingItem> Build()
    {
        yield return new ToggleItem
        {
            Label = "Start with Windows",
            Description = "Open Dynamic Island when you sign in",
            Get = () => Startup.IsEnabled,
            Set = v => Startup.IsEnabled = v,
        };
        yield return Toggle("Hide in fullscreen", "Get out of the way of games, videos and presentations",
            m => m.HideInFullscreen, (m, v) => m.HideInFullscreen = v);
    }
}
