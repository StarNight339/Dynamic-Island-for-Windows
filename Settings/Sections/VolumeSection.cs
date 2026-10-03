using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

public sealed class VolumeSection(SettingsStore store) : SettingsSection<VolumeSettings>(store)
{
    public override string Title => "Volume";
    public override string Icon => "\uE995";

    public override IEnumerable<SettingItem> Build()
    {
        yield return Toggle("Volume indicator", "Show the level when the volume or mute changes",
            m => m.Enabled, (m, v) => m.Enabled = v, quick: true);
    }
}
