namespace DynamicIsland.Core.Settings;

// Defaults match the app's behaviour before settings existed.

public sealed class GeneralSettings : ISettingsModel
{
    public static string SectionId => "general";
    public bool HideInFullscreen { get; set; } = true;
}

public sealed class TimerSettings : ISettingsModel
{
    public static string SectionId => "timer";
    public List<int> Presets { get; set; } = [1, 5, 10, 25];
    public bool PlaySound { get; set; } = true;
}

public sealed class BatterySettings : ISettingsModel
{
    public static string SectionId => "battery";
    public bool Enabled { get; set; } = true;
    public bool ShowChargingFlash { get; set; } = true;
    public List<int> LowThresholds { get; set; } = [5, 10, 20];
}

public sealed class MediaSettings : ISettingsModel
{
    public static string SectionId => "media";
    public bool Enabled { get; set; } = true;
    public int PausedLingerSeconds { get; set; } = 90;
}

public sealed class VolumeSettings : ISettingsModel
{
    public static string SectionId => "volume";
    public bool Enabled { get; set; } = true;
}

public sealed class ClaudeSettings : ISettingsModel
{
    public static string SectionId => "claude";
    public bool Enabled { get; set; } = true;
}

public sealed class ApiSettings : ISettingsModel
{
    public static string SectionId => "api";
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 5179;
}
