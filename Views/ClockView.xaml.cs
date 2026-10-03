using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DynamicIsland.Views;

public partial class ClockView : IslandView
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Minutes chosen from the quick-timer chips.</summary>
    public event Action<int>? TimerRequested;

    public override Size CompactSize => new(124, 32);
    public override Size ExpandedSize => new(380, 132);
    protected override FrameworkElement CompactPart => CompactRoot;
    protected override FrameworkElement ExpandedPart => ExpandedRoot;

    public ClockView()
    {
        InitializeComponent();
        _tick.Tick += (_, _) => Render();
        _tick.Start();
        Render();
        SetBattery(null, false);
    }

    /// <param name="percent">null hides the battery (desktop PCs).</param>
    public void SetBattery(int? percent, bool charging)
    {
        BatteryPanel.Visibility = percent is null ? Visibility.Collapsed : Visibility.Visible;
        if (percent is not { } p) return;
        BatteryText.Text = $"{p}%";
        BatteryIcon.Text = BatteryGlyph(p, charging);
        BatteryIcon.Foreground = (System.Windows.Media.Brush)FindResource(
            charging ? "AccentGreen" : p <= 20 ? "AccentRed" : "TextPrimary");
    }

    /// <summary>Segoe Fluent battery glyphs: E850..E859 (0-90%), E83F full; EA93.. charging variants.</summary>
    public static string BatteryGlyph(int percent, bool charging)
    {
        var step = Math.Clamp(percent / 10, 0, 10);
        if (charging) return ((char)(0xE85A + Math.Min(step, 9))).ToString();
        return step >= 10 ? "\uE83F" : ((char)(0xE850 + step)).ToString();
    }

    private void Render()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("HH:mm", CultureInfo.CurrentCulture);
        DateText.Text = now.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
    }

    public void SetTimerPresets(IEnumerable<int> minutes)
    {
        TimerChips.Children.Clear();
        foreach (var m in minutes.Take(5))
        {
            var chip = new Button { Style = (Style)FindResource("ChipButton"), Content = FormatMinutes(m) };
            chip.Click += (_, _) => TimerRequested?.Invoke(m);
            TimerChips.Children.Add(chip);
        }
    }
}
