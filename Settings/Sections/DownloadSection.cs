using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

public sealed class DownloadSection(SettingsStore store, string folder) : SettingsSection<DownloadSettings>(store)
{
    public override string Title => "Downloads";
    public override string Icon => "";

    public override IEnumerable<SettingItem> Build()
    {
        yield return Toggle("Download progress", "Show browser downloads (Chrome, Edge, Firefox...) while they run",
            m => m.Enabled, (m, v) => m.Enabled = v, quick: true);
        yield return Toggle("Add finished downloads to the shelf", "Put each finished download on the file shelf",
            m => m.AddToShelf, (m, v) => m.AddToShelf = v);
        yield return new InfoItem { Label = "Watched folder", Get = () => folder };
    }
}
