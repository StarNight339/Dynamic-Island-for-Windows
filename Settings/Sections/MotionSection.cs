using DynamicIsland.Core.Settings;

namespace DynamicIsland.Settings.Sections;

/// <summary>How the island moves. Every change applies live.</summary>
public sealed class MotionSection(SettingsStore store) : SettingsSection<MotionSettings>(store)
{
    public override string Title => "Motion";
    public override string Icon => "";

    public override IEnumerable<SettingItem> Build()
    {
        yield return Number("Bounciness", "How much the island overshoots before it settles (0 = none)",
            m => m.Bounciness, (m, v) => m.Bounciness = v, 0, 100, 5);
        yield return Number("Speed", "How fast the island springs to its new shape", m => m.Speed,
            (m, v) => m.Speed = v, 50, 150, 10, "%");
        yield return Toggle("Squash & stretch", "The island stretches the way it's growing, like jelly",
            m => m.SquashStretch, (m, v) => m.SquashStretch = v);
        yield return Toggle("Morphing", "Old content blurs and melts into the new one when the activity changes",
            m => m.Morphing, (m, v) => m.Morphing = v);
        yield return Toggle("Split & merge", "A second activity (e.g. a timer while music plays) splits off into a bubble",
            m => m.SplitMerge, (m, v) => m.SplitMerge = v);
        yield return Toggle("Parallax & tilt", "The open island leans toward the pointer",
            m => m.ParallaxTilt, (m, v) => m.ParallaxTilt = v);
    }
}
