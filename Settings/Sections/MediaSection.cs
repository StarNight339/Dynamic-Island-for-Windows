using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

public sealed class MediaSection(SettingsStore store) : SettingsSection<MediaSettings>(store)
{
    public override string Title => "Media";
    public override string Icon => "\uE8D6";

    public override IEnumerable<SettingItem> Build()
    {
        yield return Toggle("Now playing", "Spotify, browsers and other apps using Windows media controls",
            m => m.Enabled, (m, v) => m.Enabled = v, quick: true);
        yield return Number("Keep after pausing", "How long a paused track stays on the island",
            m => m.PausedLingerSeconds, (m, v) => m.PausedLingerSeconds = v, 0, 600, 15, "s");
    }
}
