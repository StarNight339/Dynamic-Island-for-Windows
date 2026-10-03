using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

public sealed class ClaudeSection(SettingsStore store) : SettingsSection<ClaudeSettings>(store)
{
    public override string Title => "Claude Code";
    public override string Icon => "\uE99A";

    public override IEnumerable<SettingItem> Build()
    {
        yield return Toggle("Claude status", "Working / needs you / finished, from Claude Code hooks",
            m => m.Enabled, (m, v) => m.Enabled = v, quick: true);
        yield return new InfoItem
        {
            Label = "Hook endpoint",
            Description = "Copy the hooks from hooks/claude-settings.json into ~/.claude/settings.json",
            Get = () => $"POST http://localhost:{Store.Get<ApiSettings>().Port}/claude",
        };
    }
}
