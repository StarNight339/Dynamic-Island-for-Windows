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

public sealed class FileShelfSettings : ISettingsModel
{
    public static string SectionId => "fileShelf";
    public bool Enabled { get; set; } = true;
    public bool ShowPill { get; set; } = true;
    public int MaxItems { get; set; } = 30;
}

public sealed class DownloadSettings : ISettingsModel
{
    public static string SectionId => "downloads";
    public bool Enabled { get; set; } = true;
    public bool AddToShelf { get; set; }
}

public sealed class ApiSettings : ISettingsModel
{
    public static string SectionId => "api";
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 5179;
}

public sealed class UpdateSettings : ISettingsModel
{
    public static string SectionId => "update";
    public bool AutoUpdate { get; set; } = true;
}

/// <summary>How the island looks. Applied live by <see cref="ThemeManager"/>; presets live in <see cref="ThemePresets"/>.</summary>
public sealed class ThemeSettings : ISettingsModel
{
    public static string SectionId => "theme";

    /// <summary>Name of the preset last chosen, or "Custom" once something was changed by hand.</summary>
    public string Preset { get; set; } = "Dark";

    /// <summary>Solid, Gradient, Mesh, Glass or Acrylic.</summary>
    public string Effect { get; set; } = ThemeEffects.Solid;

    public string Background { get; set; } = "#000000";

    /// <summary>Background opacity in percent (40–100).</summary>
    public int Opacity { get; set; } = 100;

    public string GradientTo { get; set; } = "#1B2A6B";
    public int GradientAngle { get; set; } = 135;

    /// <summary>The four blob colours of the mesh gradient (on top of <see cref="Background"/>).</summary>
    public List<string> MeshColors { get; set; } = ["#FF6B9D", "#C44BFF", "#4B7BFF", "#2EE6D6"];

    /// <summary>"Mossy": a fine grain over the background.</summary>
    public bool Texture { get; set; }
    public int TextureStrength { get; set; } = 4;

    /// <summary>Slowly moves gradient / mesh colours.</summary>
    public bool AnimateColors { get; set; }
    public int AnimationSeconds { get; set; } = 12;

    public bool UseWindowsAccent { get; set; }
    public string Accent { get; set; } = "#D97757";
    public string Green { get; set; } = "#34C759";
    public string Orange { get; set; } = "#FF9F0A";
    public string Red { get; set; } = "#FF453A";

    /// <summary>Island size in percent (80–130).</summary>
    public int Scale { get; set; } = 100;

    /// <summary>Corner radius of the expanded island.</summary>
    public int CornerRadius { get; set; } = 36;

    /// <summary>Empty = the default UI font.</summary>
    public string Font { get; set; } = "";
}

public static class ThemeEffects
{
    public const string Solid = "Solid";
    public const string Gradient = "Gradient";
    public const string Mesh = "Mesh";
    public const string Glass = "Glass";
    public const string Acrylic = "Acrylic";

    public static readonly string[] All = [Solid, Gradient, Mesh, Glass, Acrylic];
}

/// <summary>Ready-made looks. Applying one overwrites the look fields but keeps size, corners and font.</summary>
public static class ThemePresets
{
    public const string Custom = "Custom";

    public static readonly string[] Names = ["Dark", "Light", "Midnight", "Glass", "Acrylic", "Sunset", "Aurora", "Mesh", "Moss"];

    /// <summary>Returns false (and changes nothing) for an unknown name.</summary>
    public static bool Apply(string name, ThemeSettings t)
    {
        var defaults = new ThemeSettings();
        void Look(string effect, string background, int opacity = 100)
        {
            t.Effect = effect;
            t.Background = background;
            t.Opacity = opacity;
            t.Texture = false;
            t.AnimateColors = false;
            t.Accent = defaults.Accent;
            t.Green = defaults.Green;
        }

        switch (name)
        {
            case "Dark":
                Look(ThemeEffects.Solid, "#000000");
                break;
            case "Light":
                Look(ThemeEffects.Solid, "#F2F2F7", 96);
                break;
            case "Midnight":
                Look(ThemeEffects.Gradient, "#0B1026");
                t.GradientTo = "#1B2A6B";
                t.GradientAngle = 135;
                break;
            case "Glass":
                Look(ThemeEffects.Glass, "#1C1C1E", 70);
                break;
            case "Acrylic":
                Look(ThemeEffects.Acrylic, "#000000", 55);
                break;
            case "Sunset":
                Look(ThemeEffects.Gradient, "#FF5F6D");
                t.GradientTo = "#FFC371";
                t.GradientAngle = 90;
                break;
            case "Aurora":
                Look(ThemeEffects.Mesh, "#06121F");
                t.MeshColors = ["#00C9A7", "#2C73D2", "#845EC2", "#00E5FF"];
                t.AnimateColors = true;
                break;
            case "Mesh":
                Look(ThemeEffects.Mesh, "#0E0E1A");
                t.MeshColors = ["#FF6B9D", "#C44BFF", "#4B7BFF", "#2EE6D6"];
                t.AnimateColors = true;
                break;
            case "Moss":
                Look(ThemeEffects.Solid, "#1E2B1E");
                t.Texture = true;
                t.TextureStrength = 5;
                t.Accent = "#8FD694";
                t.Green = "#6FCF7F";
                break;
            default:
                return false;
        }
        t.Preset = name;
        return true;
    }
}
