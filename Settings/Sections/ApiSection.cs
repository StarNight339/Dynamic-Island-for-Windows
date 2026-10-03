using DynamicIsland.Core;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

public sealed class ApiSection(SettingsStore store, Notifier notifier) : SettingsSection<ApiSettings>(store)
{
    private const string RestartNote = "Takes effect after restarting the app";

    public override string Title => "Local API";
    public override string Icon => "\uE968";

    public override IEnumerable<SettingItem> Build()
    {
        yield return Toggle("Local HTTP API", $"Let scripts and AI agents post to the island. {RestartNote}",
            m => m.Enabled, (m, v) => m.Enabled = v);
        yield return Number("Port", RestartNote, m => m.Port, (m, v) => m.Port = v, 1024, 65535, textBox: true);
        yield return new InfoItem { Label = "Address", Get = () => $"http://localhost:{Model.Port}/" };
        yield return new ActionItem
        {
            Label = "Test notification",
            Description = "Show a sample notification on the island",
            ButtonText = "Send",
            Run = () => notifier.Show("Hello from Settings", "Notifications look like this", "\uEA8F",
                Notifier.Brush("AccentClaude")),
        };
    }
}
