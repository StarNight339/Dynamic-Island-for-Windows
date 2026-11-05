namespace DynamicIsland.Core;

public static class IslandVisibility
{
    /// <summary>
    /// Whether the island should be hidden: the user hid it or a fullscreen app is in front, unless the activity
    /// on show is <see cref="Activity.Important"/>, which breaks through both.
    /// </summary>
    public static bool ShouldHide(bool userHidden, bool fullscreen, Activity? current) =>
        (userHidden || fullscreen) && current?.Important != true;
}
