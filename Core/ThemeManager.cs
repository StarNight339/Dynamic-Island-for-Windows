using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core;

/// <summary>
/// Applies <see cref="ThemeSettings"/> to the island, live: the capsule's background (solid / gradient / mesh /
/// glass / acrylic), text colours that stay readable on it, accents, font, size and corner radius.
/// Text and surface brushes go into the island window's resources only, so the settings window keeps its own look.
/// </summary>
public sealed class ThemeManager
{
    /// <summary>Above this average luminance the island switches to dark text.</summary>
    internal const double LightBackgroundThreshold = 0.3;

    private static readonly Color DarkText = Color.FromRgb(0x1C, 0x1C, 0x1E);

    private readonly IslandWindow _window;
    private readonly SettingsStore _settings;
    private readonly List<AnimationClock> _clocks = [];

    public ThemeManager(IslandWindow window, SettingsStore settings)
    {
        _window = window;
        _settings = settings;
        settings.Changed += id =>
        {
            if (id == ThemeSettings.SectionId) Apply();
        };
        window.HiddenChanged += hidden =>
        {
            // Nobody sees the colours while the island is hidden; don't spend frames on them.
            foreach (var clock in _clocks)
            {
                if (hidden) clock.Controller?.Pause();
                else clock.Controller?.Resume();
            }
        };
        Apply();
    }

    public void Apply()
    {
        var t = _settings.Get<ThemeSettings>();
        StopAnimations();

        var background = ParseColor(t.Background, Colors.Black);
        var alpha = (byte)Math.Round(Math.Clamp(t.Opacity, 40, 100) / 100.0 * 255);
        var mesh = (t.MeshColors.Count > 0 ? t.MeshColors : new ThemeSettings().MeshColors)
            .Select(c => ParseColor(c, Colors.Transparent)).ToList();
        var gradientTo = ParseColor(t.GradientTo, background);

        var lightBackground = t.Effect switch
        {
            ThemeEffects.Gradient => IsLight([background, gradientTo]),
            ThemeEffects.Mesh => IsLightMesh(background, mesh),
            _ => IsLight([background]),
        };
        ApplyTextColors(lightBackground);
        ApplyAccents(t);
        ApplyFont(t.Font);

        var effect = ThemeEffects.All.Contains(t.Effect) ? t.Effect : ThemeEffects.Solid;
        var pill = effect switch
        {
            ThemeEffects.Gradient => CreateGradient(WithAlpha(background, alpha), WithAlpha(gradientTo, alpha), t),
            ThemeEffects.Mesh => CreateMesh(WithAlpha(background, alpha), mesh, alpha, t),
            // The blur shows through, so keep the capsule light; it must stay above 0 alpha or it stops taking the mouse.
            ThemeEffects.Acrylic => Freeze(new SolidColorBrush(WithAlpha(background, (byte)Math.Max(2, alpha / 4)))),
            _ => Freeze(new SolidColorBrush(WithAlpha(background, alpha))),
        };

        _window.ApplyLook(pill, effect, t.Texture, Math.Clamp(t.TextureStrength, 1, 10) / 10.0, WithAlpha(background, alpha));
        _window.ApplyShape(Math.Clamp(t.Scale, 80, 130) / 100.0, Math.Clamp(t.CornerRadius, 12, 48));
    }

    // ---- pure helpers (unit-tested) ----

    /// <summary>"#RRGGBB", "RRGGBB", "#AARRGGBB" or a colour name; <paramref name="fallback"/> when it can't be read.</summary>
    public static Color ParseColor(string? text, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var s = text.Trim();
        if (!s.StartsWith('#') && (s.Length is 6 or 8) && s.All(Uri.IsHexDigit)) s = "#" + s;
        try
        {
            return (Color)ColorConverter.ConvertFromString(s);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    public static string ToHex(Color c) => c.A == 255
        ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
        : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    public static bool IsLight(IReadOnlyCollection<Color> colors) =>
        colors.Count > 0 && colors.Average(Luminance) > LightBackgroundThreshold;

    /// <summary>Mesh blobs fade to transparent, so the base colour shows through about half the surface.</summary>
    public static bool IsLightMesh(Color background, IReadOnlyCollection<Color> blobs) =>
        0.5 * Luminance(background) + 0.5 * (blobs.Count > 0 ? blobs.Average(Luminance) : Luminance(background))
        > LightBackgroundThreshold;

    // ---- resources ----

    private void ApplyTextColors(bool lightBackground)
    {
        var ink = lightBackground ? DarkText : Colors.White;
        var r = _window.Resources;
        r["TextPrimary"] = SolidBrush(ink, 0xFF);
        r["TextSecondary"] = SolidBrush(ink, lightBackground ? (byte)0xA6 : (byte)0x99);
        r["Track"] = SolidBrush(ink, lightBackground ? (byte)0x26 : (byte)0x33);
        r["SurfaceSubtle"] = SolidBrush(ink, 0x14);
        r["SurfaceTile"] = SolidBrush(ink, lightBackground ? (byte)0x14 : (byte)0x1C);
        r["SurfaceHover"] = SolidBrush(ink, lightBackground ? (byte)0x24 : (byte)0x2C);
        r["SurfaceBase"] = SolidBrush(lightBackground ? Color.FromRgb(0xF2, 0xF2, 0xF7) : Color.FromRgb(0x1C, 0x1C, 0x1E), 0xFF);
    }

    private static void ApplyAccents(ThemeSettings t)
    {
        var defaults = new ThemeSettings();
        var accent = t.UseWindowsAccent ? SystemParameters.WindowGlassColor : ParseColor(t.Accent, ParseColor(defaults.Accent, Colors.Orange));
        var app = Application.Current.Resources;
        app["AccentClaude"] = SolidBrush(accent, 0xFF);
        app["AccentGreen"] = SolidBrush(ParseColor(t.Green, ParseColor(defaults.Green, Colors.Green)), 0xFF);
        app["AccentOrange"] = SolidBrush(ParseColor(t.Orange, ParseColor(defaults.Orange, Colors.Orange)), 0xFF);
        app["AccentRed"] = SolidBrush(ParseColor(t.Red, ParseColor(defaults.Red, Colors.Red)), 0xFF);
    }

    private void ApplyFont(string font)
    {
        if (string.IsNullOrWhiteSpace(font))
        {
            _window.Resources.Remove("UiFont"); // back to the app default
            return;
        }
        // Keep the defaults as fallbacks so a missing font (or Thai text) still renders.
        _window.Resources["UiFont"] = new FontFamily($"{font.Trim()}, Segoe UI Variable Text, Segoe UI, Leelawadee UI");
    }

    // ---- brushes ----

    private Brush CreateGradient(Color from, Color to, ThemeSettings t)
    {
        var angle = t.GradientAngle * Math.PI / 180;
        var dx = Math.Cos(angle) / 2;
        var dy = Math.Sin(angle) / 2;
        var brush = new LinearGradientBrush(from, to, new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
        if (!t.AnimateColors) return Freeze(brush);

        // Spin the gradient slowly around the middle of the capsule.
        var rotate = new RotateTransform(0, 0.5, 0.5);
        brush.RelativeTransform = rotate;
        Animate(rotate, RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, Seconds(t.AnimationSeconds) * 2) { RepeatBehavior = RepeatBehavior.Forever });
        return brush;
    }

    /// <summary>
    /// A mesh gradient: the base colour with four soft radial blobs on top that blend where they overlap.
    /// When animated, each blob drifts on its own path at its own speed, so the pattern never repeats exactly.
    /// </summary>
    private Brush CreateMesh(Color background, IReadOnlyList<Color> blobs, byte alpha, ThemeSettings t)
    {
        Point[] homes = [new(0.15, 0.2), new(0.85, 0.25), new(0.3, 0.9), new(0.8, 0.85)];
        double[] periods = [1.0, 1.3, 1.7, 2.1];
        var unit = new RectangleGeometry(new Rect(0, 0, 1, 1));

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(background), null, unit));
        for (var i = 0; i < homes.Length; i++)
        {
            var color = blobs[i % blobs.Count];
            var a = (byte)(color.A * alpha / 255);
            var blob = new RadialGradientBrush
            {
                Center = homes[i],
                GradientOrigin = homes[i],
                RadiusX = 0.75,
                RadiusY = 0.9,
                GradientStops =
                {
                    new GradientStop(WithAlpha(color, a), 0),
                    new GradientStop(WithAlpha(color, (byte)(a * 0.45)), 0.45),
                    new GradientStop(WithAlpha(color, 0), 1),
                },
            };
            group.Children.Add(new GeometryDrawing(blob, null, unit));

            if (!t.AnimateColors) continue;
            var path = DriftPath(homes[i], i, Seconds(t.AnimationSeconds) * periods[i]);
            Animate(blob, RadialGradientBrush.CenterProperty, path);
            Animate(blob, RadialGradientBrush.GradientOriginProperty, path.Clone());
        }

        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
        return t.AnimateColors ? brush : Freeze(brush);
    }

    /// <summary>A slow loop around <paramref name="home"/> through a few waypoints, eased so it never jerks.</summary>
    private static PointAnimationUsingKeyFrames DriftPath(Point home, int seed, TimeSpan period)
    {
        var random = new Random(seed * 7919 + 17);
        var animation = new PointAnimationUsingKeyFrames { Duration = period, RepeatBehavior = RepeatBehavior.Forever };
        const int steps = 4;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        for (var s = 1; s <= steps; s++)
        {
            var target = s == steps
                ? home
                : new Point(Math.Clamp(home.X + (random.NextDouble() - 0.5) * 0.7, -0.1, 1.1),
                            Math.Clamp(home.Y + (random.NextDouble() - 0.5) * 0.8, -0.1, 1.1));
            animation.KeyFrames.Add(new EasingPointKeyFrame(target, KeyTime.FromPercent(s / (double)steps), ease));
        }
        return animation;
    }

    private void Animate(Animatable target, DependencyProperty property, AnimationTimeline animation)
    {
        var clock = animation.CreateClock();
        target.ApplyAnimationClock(property, clock);
        _clocks.Add(clock);
    }

    private void StopAnimations()
    {
        foreach (var clock in _clocks) clock.Controller?.Stop();
        _clocks.Clear();
    }

    private static TimeSpan Seconds(int seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, 3, 120));

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static SolidColorBrush SolidBrush(Color c, byte alpha) => Freeze(new SolidColorBrush(WithAlpha(c, alpha)));

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
