using System.Diagnostics;
using System.IO;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

public sealed class AboutSection(SettingsStore store) : ISettingsSection
{
    public string Title => "About";
    public string Icon => "\uE946";

    public IEnumerable<SettingItem> Build()
    {
        yield return new InfoItem
        {
            Label = "Version",
            Get = () => typeof(AboutSection).Assembly.GetName().Version?.ToString(3) ?? "dev",
        };
        yield return new InfoItem { Label = "Settings file", Get = () => store.FilePath };
        yield return new ActionItem
        {
            Label = "Settings folder",
            ButtonText = "Open",
            Run = () =>
            {
                var dir = Path.GetDirectoryName(store.FilePath)!;
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            },
        };
    }
}
