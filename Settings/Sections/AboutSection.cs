using System.Diagnostics;
using System.IO;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

public sealed class AboutSection(SettingsStore store, Updater updater) : SettingsSection<UpdateSettings>(store)
{
    public override string Title => "About";
    public override string Icon => "";

    public override IEnumerable<SettingItem> Build()
    {
        yield return new InfoItem { Label = "Version", Get = () => Updater.Current.ToString(3) };
        yield return Toggle("Update automatically",
            Updater.CanSelfUpdate
                ? "Install new versions from GitHub and restart the app"
                : "Only the downloaded single-file app updates itself",
            m => m.AutoUpdate, (m, v) => m.AutoUpdate = v);
        yield return new ActionItem
        {
            Label = "Check for updates",
            ButtonText = "Check",
            Run = () => _ = updater.CheckAsync(manual: true),
        };
        yield return new InfoItem { Label = "Settings file", Get = () => Store.FilePath };
        yield return new ActionItem
        {
            Label = "Settings folder",
            ButtonText = "Open",
            Run = () =>
            {
                var dir = Path.GetDirectoryName(Store.FilePath)!;
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            },
        };
    }
}
