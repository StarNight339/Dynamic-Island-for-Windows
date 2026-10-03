using DynamicIsland.Core.Settings;
using DynamicIsland.Views;

namespace DynamicIsland.Settings.Sections;

public sealed class TimerSection(SettingsStore store) : SettingsSection<TimerSettings>(store)
{
    public override string Title => "Timer";
    public override string Icon => "\uE916";

    public override IEnumerable<SettingItem> Build()
    {
        yield return NumberList("Quick timers", "Shown when you hover the clock and in the right-click panel",
            m => m.Presets, 1, 600, IslandView.FormatMinutes);
        yield return Toggle("Play a sound", "When a timer finishes", m => m.PlaySound, (m, v) => m.PlaySound = v);
    }
}
