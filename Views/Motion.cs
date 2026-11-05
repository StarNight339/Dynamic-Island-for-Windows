namespace DynamicIsland.Views;

public static class Motion
{
    /// <summary>
    /// Frame rate for animations that loop forever (spinners, busy bars, equalizer bars, theme colours). The island is
    /// a transparent layered window, so every frame is redrawn on the CPU; 30 fps looks the same for these slow loops
    /// and costs about half. One-off transitions (springs, fades) keep the full rate.
    /// </summary>
    public const int LoopFrameRate = 30;
}
