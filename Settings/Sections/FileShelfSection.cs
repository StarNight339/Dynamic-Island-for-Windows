using DynamicIsland.Core;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

public sealed class FileShelfSection(SettingsStore store, FileShelf shelf) : SettingsSection<FileShelfSettings>(store)
{
    public override string Title => "File shelf";
    public override string Icon => "";

    public override IEnumerable<SettingItem> Build()
    {
        yield return Toggle("File shelf", "Drop files on the island to keep them handy; copy, copy path or drag them out later",
            m => m.Enabled, (m, v) => m.Enabled = v, quick: true);
        yield return Toggle("Show shelf pill", "Show a small pill with the file count while files are on the shelf",
            m => m.ShowPill, (m, v) => m.ShowPill = v);
        yield return Number("Max items", "The oldest files drop off the shelf past this many",
            m => m.MaxItems, (m, v) => m.MaxItems = v, 5, 100, 5);
        yield return new ActionItem
        {
            Label = "Clear shelf",
            Description = "Removes everything from the shelf. The files themselves stay where they are",
            ButtonText = "Clear",
            Run = shelf.Clear,
        };
    }
}
