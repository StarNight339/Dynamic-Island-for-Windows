using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

public sealed class BatterySection(SettingsStore store) : SettingsSection<BatterySettings>(store)
{
    public override string Title => "Battery";
    public override string Icon => "\uE83F";

    public override IEnumerable<SettingItem> Build()
    {
        yield return Toggle("Battery alerts", "Charger and low-battery pop-ups (laptops only)",
            m => m.Enabled, (m, v) => m.Enabled = v, quick: true);
        yield return Toggle("Charger flash", "Show the level when you plug in or unplug",
            m => m.ShowChargingFlash, (m, v) => m.ShowChargingFlash = v);
        yield return NumberList("Low battery warnings", "Warn once as the charge drops below each level",
            m => m.LowThresholds, 1, 99, v => $"{v}%");
    }
}
