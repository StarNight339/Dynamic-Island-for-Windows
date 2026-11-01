using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

/// <summary>
/// The island's look. Every change applies live (the island is the preview). Picking a preset fills in the look;
/// changing anything by hand afterwards turns the preset into "Custom".
/// </summary>
public sealed class ThemeSection(SettingsStore store) : SettingsSection<ThemeSettings>(store)
{
    private static readonly string[] Fonts = ["", "Segoe UI Variable Text", "Segoe UI", "Leelawadee UI", "Bahnschrift", "Cascadia Code"];

    public override string Title => "Theme";
    public override string Icon => "";

    public override IEnumerable<SettingItem> Build()
    {
        yield return Choice("Preset", "A ready-made look. Size, corners and font are kept",
            [.. ThemePresets.Names, ThemePresets.Custom], m => m.Preset, (m, v) =>
            {
                if (v != ThemePresets.Custom) ThemePresets.Apply(v, m);
                else m.Preset = v;
            });
        yield return Choice("Effect",
            "Gradient and Mesh blend colours; Glass adds a sheen; Acrylic blurs what's behind the island (experimental)",
            ThemeEffects.All, m => m.Effect, Custom<string>((m, v) => m.Effect = v));

        yield return Color("Background colour", "The island's colour (the base colour for Gradient and Mesh)",
            m => m.Background, Custom<string>((m, v) => m.Background = v));
        yield return Number("Opacity", "Lower lets the desktop show through", m => m.Opacity,
            Custom<int>((m, v) => m.Opacity = v), 40, 100, 5, "%");

        yield return Color("Gradient end colour", "Used by the Gradient effect", m => m.GradientTo,
            Custom<string>((m, v) => m.GradientTo = v));
        yield return Number("Gradient angle", "Used by the Gradient effect", m => m.GradientAngle,
            Custom<int>((m, v) => m.GradientAngle = v), 0, 360, 15, "°");

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            yield return Color($"Mesh colour {index + 1}", index == 0 ? "The four colours blended by the Mesh effect" : null,
                m => index < m.MeshColors.Count ? m.MeshColors[index] : "#000000",
                Custom<string>((m, v) =>
                {
                    while (m.MeshColors.Count <= index) m.MeshColors.Add("#000000");
                    m.MeshColors[index] = v;
                }));
        }

        yield return Toggle("Animated colours", "Slowly moves the Gradient and Mesh colours",
            m => m.AnimateColors, Custom<bool>((m, v) => m.AnimateColors = v));
        yield return Number("Animation speed", "Seconds per cycle (lower is faster)", m => m.AnimationSeconds,
            Custom<int>((m, v) => m.AnimationSeconds = v), 4, 60, 2, "s");

        yield return Toggle("Mossy texture", "A fine grain over the background", m => m.Texture,
            Custom<bool>((m, v) => m.Texture = v));
        yield return Number("Texture strength", null, m => m.TextureStrength,
            Custom<int>((m, v) => m.TextureStrength = v), 1, 10);

        yield return Toggle("Use Windows accent colour", "Replaces the main accent below", m => m.UseWindowsAccent,
            Custom<bool>((m, v) => m.UseWindowsAccent = v));
        yield return Color("Accent", "Main accent: Claude, progress bars, downloads", m => m.Accent,
            Custom<string>((m, v) => m.Accent = v));
        yield return Color("Green", "Done, charging, copied", m => m.Green, Custom<string>((m, v) => m.Green = v));
        yield return Color("Orange", "Timer, warnings", m => m.Orange, Custom<string>((m, v) => m.Orange = v));
        yield return Color("Red", "Errors, low battery", m => m.Red, Custom<string>((m, v) => m.Red = v));

        // Size, corners and font aren't part of a preset, so they don't turn it into "Custom".
        yield return Number("Size", "Scales the whole island", m => m.Scale, (m, v) => m.Scale = v, 80, 130, 5, "%");
        yield return Number("Corner roundness", "Corner radius of the expanded island", m => m.CornerRadius,
            (m, v) => m.CornerRadius = v, 12, 48, 2);
        yield return Choice("Font", "Or type the name of any installed font and press Enter", Fonts, m => m.Font,
            (m, v) => m.Font = v, allowCustom: true, format: v => v.Length == 0 ? "Default" : v);

        yield return new ActionItem
        {
            Label = "Reset theme",
            Description = "Back to the original black island",
            ButtonText = "Reset",
            Run = () =>
            {
                var fresh = new ThemeSettings();
                var m = Model;
                foreach (var p in typeof(ThemeSettings).GetProperties().Where(p => p.CanWrite))
                    p.SetValue(m, p.GetValue(fresh));
                Store.Save<ThemeSettings>();
            },
        };
    }

    /// <summary>Wraps a setter so a hand edit marks the preset as "Custom".</summary>
    private static Action<ThemeSettings, TValue> Custom<TValue>(Action<ThemeSettings, TValue> set) => (m, v) =>
    {
        set(m, v);
        m.Preset = ThemePresets.Custom;
    };
}
