using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using DynamicIsland.Views;
using Microsoft.Win32;

namespace DynamicIsland;

public partial class IslandWindow : Window
{
    private const double BaseWidth = 560;
    private const double BaseHeight = 240;
    private const double IslandTop = 6;

    /// <summary>Space between the capsule and the split-off bubble.</summary>
    private const double BubbleGap = 8;

    /// <summary>The gooey neck between capsule and bubble snaps once they are this far apart.</summary>
    private const double BridgeReach = 14;

    private double _expandedRadius = 36;
    private double _scale = 1;
    private string _effect = ThemeEffects.Solid;
    private Color _acrylicTint = Colors.Black;

    private readonly ActivityManager _activities;
    private readonly SettingsStore _settings;
    private readonly ClockView _idleView;
    private readonly DispatcherTimer _hoverDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _leaveDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _housekeeping = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _dragWatch = new() { Interval = TimeSpan.FromMilliseconds(250) };

    // Every moving part of the island is a spring on one render-loop hook, which only runs while something moves.
    private readonly Spring _width, _height, _radius; // the capsule
    private readonly Spring _pop;                      // content scale when it changes
    private readonly Spring _split;                    // 0 = bubble merged into the capsule, 1 = split off
    private readonly Spring _tiltX, _tiltY;            // pointer over the open island, -1..1
    private readonly SpringLoop _loop;

    private IslandView? _shown;
    private IslandView? _bubbleView;
    private double _bubbleSize = 36;
    private int _morphId;
    private bool _hover;

    /// <summary>The pointer opened the island from the bubble, so it shows the runner-up activity.</summary>
    private bool _bubbleHover;

    private const int HideHotkeyId = 0x4449; // "DI"
    private const uint VK_H = 0x48;

    private bool _fullscreen;
    private bool _hidden;
    private bool _hotkeyRegistered;
    private IntPtr _hwnd;

    /// <summary>Right-click on the island.</summary>
    public event Action? QuickPanelRequested;

    /// <summary>The pointer left the island (after the leave delay).</summary>
    public event Action? HoverEnded;

    /// <summary>Files from another app are being dragged over the island.</summary>
    public event Action? FileDragEntered;

    /// <summary>Files from another app were dropped on the island.</summary>
    public event Action<string[]>? FilesDropped;

    /// <summary>The island was hidden (true) or shown again (false), by the user or for a fullscreen app.</summary>
    public event Action<bool>? HiddenChanged;

    /// <summary>Whether the user hid the island (tray, quick panel, hotkey, API). Important notifications still show.</summary>
    public bool UserHidden
    {
        get => _settings.Get<GeneralSettings>().Hidden;
        set
        {
            var general = _settings.Get<GeneralSettings>();
            if (general.Hidden == value) return;
            general.Hidden = value;
            _settings.Save<GeneralSettings>(); // Changed → UpdateVisibility
        }
    }

    public void ToggleHidden() => UserHidden = !UserHidden;

    /// <summary>Whether the island takes dropped files (the file shelf is on).</summary>
    public bool AcceptsFiles { get; set; }

    public IslandState State { get; private set; } = IslandState.Idle;

    private MotionSettings Motion => _settings.Get<MotionSettings>();

    public IslandWindow(ActivityManager activities, ClockView idleView, SettingsStore settings)
    {
        InitializeComponent();
        _activities = activities;
        _settings = settings;
        _idleView = idleView;

        var initial = idleView.CompactSize;
        _width = new Spring(initial.Width);
        _height = new Spring(initial.Height);
        _radius = new Spring(initial.Height / 2);
        _pop = new Spring(1, tolerance: 0.0005);
        _split = new Spring(0, tolerance: 0.002);
        _tiltX = new Spring(0, tolerance: 0.002);
        _tiltY = new Spring(0, tolerance: 0.002);
        _loop = new SpringLoop(OnFrame, _width, _height, _radius, _pop, _split, _tiltX, _tiltY);
        ApplyMotionSettings();

        // The theme overlays follow the capsule's parallax.
        PillTexture.RenderTransform = PillSheen.RenderTransform = PillParallax;

        _activities.CurrentChanged += Render;
        _activities.SecondaryChanged += Render;
        _hoverDelay.Tick += (_, _) =>
        {
            _hoverDelay.Stop();
            _hover = true;
            _bubbleHover = Bubble.IsVisible && Bubble.IsMouseOver;
            Render();
        };
        _leaveDelay.Tick += (_, _) =>
        {
            _leaveDelay.Stop();
            _hover = false;
            _bubbleHover = false;
            HoverEnded?.Invoke();
            Render();
        };
        _housekeeping.Tick += (_, _) => Housekeeping();
        _dragWatch.Tick += (_, _) => CheckCursorAfterDrag();

        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(PositionWindow);
        _settings.Changed += id =>
        {
            if (id == MotionSettings.SectionId)
            {
                ApplyMotionSettings();
                Render();
                return;
            }
            if (id != GeneralSettings.SectionId) return;
            UpdateHotkey();
            UpdateVisibility();
        };
        Render();
        OnFrame();
    }

    /// <summary>Spring constants from the Motion settings (bounciness / speed).</summary>
    private void ApplyMotionSettings()
    {
        var (stiffness, ratio) = Spring.FromMotion(Motion.Bounciness, Motion.Speed);
        _width.Configure(stiffness, ratio);
        _height.Configure(stiffness, ratio);
        _radius.Configure(stiffness, Math.Min(1, ratio + 0.18)); // corners settle calmer than the size
        _pop.Configure(stiffness * 1.3, ratio);
        _split.Configure(stiffness * 0.8, ratio); // a touch slower, so the pull-apart reads as gooey
        _tiltX.Configure(140, 0.9); // smoothing the pointer, not bouncing
        _tiltY.Configure(140, 0.9);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hotkeyRegistered) NativeMethods.UnregisterHotKey(_hwnd, HideHotkeyId);
        base.OnClosed(e);
    }

    private void UpdateHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;
        var want = _settings.Get<GeneralSettings>().HideHotkey;
        if (want == _hotkeyRegistered) return;
        if (want)
        {
            // Fails if another app already owns Ctrl+Alt+H; the tray / quick panel / API still work.
            _hotkeyRegistered = NativeMethods.RegisterHotKey(_hwnd, HideHotkeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, VK_H);
        }
        else
        {
            NativeMethods.UnregisterHotKey(_hwnd, HideHotkeyId);
            _hotkeyRegistered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HideHotkeyId)
        {
            ToggleHidden();
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeOverlay(_hwnd);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        UpdateHotkey();
        ApplyBackdrop();
        PositionWindow();
        UpdateVisibility(animate: false); // start hidden straight away if the user left it hidden
        _housekeeping.Start();
    }

    private void PositionWindow()
    {
        var area = SystemParameters.WorkArea; // primary monitor, DIPs; respects a top-docked taskbar
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top;
    }

    /// <summary>Picks the view and target size for the current activity + hover state, then springs there.</summary>
    private void Render()
    {
        var motion = Motion;
        var current = _activities.Current;
        var secondary = motion.SplitMerge && _activities.Secondary?.View.Bubble is not null ? _activities.Secondary : null;
        if (secondary is null) _bubbleHover = false;

        var shownActivity = _bubbleHover ? secondary : current;
        var view = shownActivity?.View ?? _idleView;
        var expanded = view.CanExpand && (_hover || shownActivity?.AutoExpand == true);

        var swapped = !ReferenceEquals(view, _shown);
        var layoutChanged = swapped || view.IsExpanded != expanded;
        MorphSnapshot? ghost = null;
        if (swapped)
        {
            if (motion.Morphing && _shown is not null) ghost = Snapshot();
            HostInner.Content = view;
            _shown = view;
        }
        view.SetExpanded(expanded);

        State = shownActivity is null && !expanded ? IslandState.Idle
            : expanded ? IslandState.Expanded
            : IslandState.Compact;

        var size = view.CurrentSize;
        Host.Width = size.Width;
        Host.Height = size.Height;
        _width.Target = size.Width;
        _height.Target = size.Height;
        _radius.Target = expanded ? _expandedRadius : size.Height / 2;

        // Split & merge: the runner-up splits off while the island is a compact pill, and merges back otherwise.
        SetBubble(expanded ? null : secondary, view.CompactSize.Height);

        if (!_hover || !motion.ParallaxTilt)
        {
            _tiltX.Target = 0;
            _tiltY.Target = 0;
        }

        if (layoutChanged) FadeInContent(scaleFrom: swapped ? 0.88 : 0.95, ghost);
        _loop.Kick();
        UpdateVisibility(); // an important notification breaks through a hide; the island hides again once it's gone
    }

    private void FadeInContent(double scaleFrom, MorphSnapshot? ghost)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        HostInner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = ease,
            BeginTime = TimeSpan.FromMilliseconds(ghost is null ? 60 : 40),
            FillBehavior = FillBehavior.Stop,
        });

        // Spring physics: the content pops in and overshoots a little with the bounciness setting.
        _pop.Reset(scaleFrom);
        _pop.Target = 1;

        if (ghost is { } g) Morph(g);
    }

    // ---- Morphing ----------------------------------------------------------------------------------------------

    private readonly record struct MorphSnapshot(ImageSource Image, double Width, double Height);

    /// <summary>A still of the outgoing content, so it can melt into the new one.</summary>
    private MorphSnapshot? Snapshot()
    {
        var width = Host.ActualWidth;
        var height = Host.ActualHeight;
        if (_hidden || width < 1 || height < 1 || PresentationSource.FromVisual(Host) is null) return null;

        var dpi = VisualTreeHelper.GetDpi(this);
        var sx = dpi.DpiScaleX * _scale;
        var sy = dpi.DpiScaleY * _scale;
        // Draw through a VisualBrush: RenderTargetBitmap would otherwise include Host's offset in the window.
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(Host)
            {
                Viewbox = new Rect(0, 0, width, height),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
            }, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * sx), (int)Math.Ceiling(height * sy),
            96 * sx, 96 * sy, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return new MorphSnapshot(bitmap, width, height);
    }

    /// <summary>The old content blurs, shrinks and fades while the new one comes into focus.</summary>
    private void Morph(MorphSnapshot ghost)
    {
        var id = ++_morphId;
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        Ghost.Source = ghost.Image;
        Ghost.Width = ghost.Width;
        Ghost.Height = ghost.Height;
        Ghost.Visibility = Visibility.Visible;
        var oldBlur = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
        Ghost.Effect = oldBlur;

        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) =>
        {
            if (id != _morphId) return; // a newer morph took over the ghost
            Ghost.Visibility = Visibility.Collapsed;
            Ghost.Source = null;
            Ghost.Effect = null;
            Ghost.Opacity = 0;
        };
        Ghost.BeginAnimation(OpacityProperty, fade);
        oldBlur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, 10, duration) { EasingFunction = ease });
        var shrink = new DoubleAnimation(1, 0.92, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        GhostScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        GhostScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);

        var newBlur = new BlurEffect { Radius = 10, RenderingBias = RenderingBias.Performance };
        HostInner.Effect = newBlur;
        var focus = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = ease,
            BeginTime = TimeSpan.FromMilliseconds(40),
        };
        // An effect makes WPF render the content through a shader every frame; drop it as soon as it's sharp.
        focus.Completed += (_, _) =>
        {
            if (ReferenceEquals(HostInner.Effect, newBlur)) HostInner.Effect = null;
        };
        newBlur.BeginAnimation(BlurEffect.RadiusProperty, focus);
    }

    // ---- Split & merge -----------------------------------------------------------------------------------------

    /// <summary>Splits the bubble off for <paramref name="activity"/>, or merges it back into the capsule (null).</summary>
    private void SetBubble(Activity? activity, double size)
    {
        var view = activity?.View;
        if (!ReferenceEquals(view, _bubbleView))
        {
            if (_bubbleView is not null) _bubbleView.BubbleChanged -= FillBubble;
            _bubbleView = view;
            if (view is not null)
            {
                view.BubbleChanged += FillBubble;
                FillBubble(); // a merging bubble keeps showing its last content until it's gone
            }
        }
        if (view is not null && Math.Abs(size - _bubbleSize) > 0.5 && _split.Value < 0.01)
        {
            _bubbleSize = size;
            Bubble.Width = Bubble.Height = size;
            Bubble.CornerRadius = new CornerRadius(size / 2);
        }
        _split.Target = view is null ? 0 : 1;
    }

    private void FillBubble()
    {
        if (_bubbleView?.Bubble is not { } info) return;
        if (info.Image is not null)
        {
            BubbleArt.Fill = new ImageBrush(info.Image) { Stretch = Stretch.UniformToFill };
            BubbleArt.Visibility = Visibility.Visible;
            BubbleIcon.Visibility = Visibility.Collapsed;
            return;
        }
        BubbleArt.Visibility = Visibility.Collapsed;
        BubbleIcon.Visibility = Visibility.Visible;
        BubbleIcon.Text = info.Icon;
        if (BadgeFill.IsFluentGlyph(info.Icon))
        {
            BubbleIcon.SetResourceReference(StyleProperty, "Icon");
            BubbleIcon.ClearValue(System.Windows.Controls.TextBlock.FontFamilyProperty);
        }
        else
        {
            BubbleIcon.ClearValue(StyleProperty);
            BubbleIcon.FontFamily = new FontFamily("Segoe UI Symbol");
        }
        BubbleIcon.Foreground = info.Accent;
    }

    /// <summary>Places the bubble and the neck for the current split amount; returns the capsule's sideways shift.</summary>
    private double LayoutBubble(double pillWidth)
    {
        var split = _split.Value; // may overshoot past 1 with bounciness
        var d = _bubbleSize;
        var shift = -(BubbleGap + d) / 2 * split; // keep capsule + bubble centred as a pair
        MainShift.X = shift;

        if (split < 0.01)
        {
            Bubble.Visibility = Bridge.Visibility = Visibility.Collapsed;
            return shift;
        }
        Bubble.Visibility = Visibility.Visible;

        // Merged, the bubble hides under the capsule's right end; split, it sits a gap to its right.
        var pillRight = shift + pillWidth / 2 * MainScale.ScaleX;
        var centre = pillRight - d / 2 + (BubbleGap + d) * split;
        var scale = Math.Clamp(0.5 + 0.5 * split, 0.5, 1.1);
        BubbleShift.X = centre;
        BubbleScale.ScaleX = BubbleScale.ScaleY = scale;
        Bubble.Opacity = Bridge.Opacity = Math.Clamp(split * 3, 0, 1);

        DrawBridge(pillRight, centre - d / 2 * scale, d);
        return shift;
    }

    /// <summary>
    /// The gooey neck: a pinched band from inside the capsule's end to inside the bubble, whose waist thins as the
    /// gap grows and snaps at <see cref="BridgeReach"/>. Coordinates are relative to the island's centre line.
    /// </summary>
    private void DrawBridge(double pillRight, double bubbleLeft, double d)
    {
        var gap = bubbleLeft - pillRight;
        var half = d / 2 * 0.7; // where the neck joins capsule and bubble
        var waist = half * Math.Pow(1 - Math.Clamp(gap / BridgeReach, 0, 1), 1.5);
        if (gap >= BridgeReach || waist < 0.6)
        {
            Bridge.Visibility = Visibility.Collapsed;
            return;
        }

        var cy = d / 2;
        var left = pillRight - 6;
        var right = bubbleLeft + 6;
        var mid = (left + right) / 2;
        var control = 2 * waist - half; // puts the curve's midpoint exactly at the waist
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(left, cy - half), true, true);
            g.QuadraticBezierTo(new Point(mid, cy - control), new Point(right, cy - half), true, true);
            g.LineTo(new Point(right, cy + half), true, true);
            g.QuadraticBezierTo(new Point(mid, cy + control), new Point(left, cy + half), true, true);
        }
        geometry.Freeze();
        Bridge.Data = geometry;
        Bridge.Visibility = Visibility.Visible;
    }

    // ---- Every frame -------------------------------------------------------------------------------------------

    /// <summary>Called every animation frame with the spring values.</summary>
    private void OnFrame()
    {
        var width = Math.Max(0, _width.Value);
        var height = Math.Max(0, _height.Value);
        var radius = Math.Min(Math.Max(0, _radius.Value), Math.Min(width, height) / 2);
        Pill.Width = width;
        Pill.Height = height;
        Pill.CornerRadius = new CornerRadius(radius);
        foreach (var overlay in (ReadOnlySpan<System.Windows.Controls.Border>)[PillTexture, PillSheen])
        {
            if (overlay.Visibility != Visibility.Visible) continue;
            overlay.Width = width;
            overlay.Height = height;
            overlay.CornerRadius = new CornerRadius(radius);
        }

        // Squash & stretch: the capsule stretches along the way it's growing, by how fast it grows (strain rate).
        var (sx, sy) = Motion.SquashStretch ? Squash(width, height) : (1.0, 1.0);
        MainScale.ScaleX = sx;
        MainScale.ScaleY = sy;

        // Parallax & tilt: the capsule moves a little, the content more and skewed, the sheen follows the light.
        var tx = _tiltX.Value;
        var ty = _tiltY.Value;
        PillParallax.X = tx * 1.5;
        PillParallax.Y = ty * 1;
        HostParallax.X = tx * 4;
        HostParallax.Y = ty * 2.5;
        if (!double.IsNaN(Host.Width) && !double.IsNaN(Host.Height))
        {
            HostSkew.CenterX = Host.Width / 2;
            HostSkew.CenterY = Host.Height / 2;
        }
        HostSkew.AngleY = tx * 1.2;
        HostSkew.AngleX = -ty * 0.8;
        SheenBrush.StartPoint = new Point(0.5 - 0.5 * tx, -0.25 * ty);
        SheenBrush.EndPoint = new Point(0.5 + 0.5 * tx, 1 - 0.25 * ty);

        HostScale.ScaleX = HostScale.ScaleY = _pop.Value;

        // Content sits centred above the capsule at its final size; clip it (and the morph ghost) to the capsule.
        Host.Clip = CapsuleClip(double.IsNaN(Host.Width) ? width : Host.Width, width, height, radius);
        if (Ghost.Visibility == Visibility.Visible) Ghost.Clip = CapsuleClip(Ghost.Width, width, height, radius);

        LayoutBubble(width);
        if (_effect == ThemeEffects.Acrylic && !_hidden) ClipBackdrop();
    }

    private (double X, double Y) Squash(double width, double height)
    {
        if (width < 1 || height < 1) return (1, 1);
        var strain = Math.Clamp((_width.Velocity / width - _height.Velocity / height) * 0.008, -0.045, 0.045);
        return (1 + strain * 0.5, 1 - strain);
    }

    private Geometry CapsuleClip(double layerWidth, double width, double height, double radius)
    {
        var offsetX = (layerWidth - width) / 2 + PillParallax.X;
        return new RectangleGeometry(new Rect(offsetX, PillParallax.Y, width, height), radius, radius);
    }

    /// <summary>Theme: the capsule's background and effect overlays. Called by <see cref="ThemeManager"/>.</summary>
    public void ApplyLook(Brush background, string effect, bool texture, double textureOpacity, Color acrylicTint)
    {
        Pill.Background = background;
        Bubble.Background = background;
        Bridge.Fill = background;
        PillTexture.Visibility = texture ? Visibility.Visible : Visibility.Collapsed;
        PillTexture.Opacity = textureOpacity;
        if (texture) PillTexture.Background ??= CreateGrain();
        PillSheen.Visibility = effect is ThemeEffects.Glass or ThemeEffects.Acrylic ? Visibility.Visible : Visibility.Collapsed;

        _effect = effect;
        _acrylicTint = acrylicTint;
        ApplyBackdrop();
        // Size the overlays now; otherwise they'd wait for the next spring frame, which won't come while idle.
        OnFrame();
    }

    /// <summary>Theme: island size (the whole window scales) and the expanded corner radius.</summary>
    public void ApplyShape(double scale, double expandedRadius)
    {
        var changed = Math.Abs(scale - _scale) > 0.001 || Math.Abs(expandedRadius - _expandedRadius) > 0.01;
        _scale = scale;
        _expandedRadius = expandedRadius;
        Root.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
        Width = BaseWidth * scale;
        Height = BaseHeight * scale;
        PositionWindow();
        if (changed) Render();
    }

    /// <summary>Real blur behind the capsule (Acrylic), or none.</summary>
    private void ApplyBackdrop()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_effect == ThemeEffects.Acrylic && !_hidden)
        {
            NativeMethods.SetAcrylic(_hwnd, _acrylicTint.A, _acrylicTint.R, _acrylicTint.G, _acrylicTint.B);
        }
        else
        {
            NativeMethods.ClearAcrylic(_hwnd);
            NativeMethods.ClearWindowRegion(_hwnd);
        }
    }

    /// <summary>
    /// Windows blurs the whole window rectangle, so cut the window to the capsule's (and bubble's) current shape in
    /// device pixels. The island is centred horizontally, <see cref="IslandTop"/> below the top, all scaled by the
    /// theme size.
    /// </summary>
    private void ClipBackdrop()
    {
        if (_hwnd == IntPtr.Zero || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target) return;
        var toDevice = target.TransformToDevice;
        var sx = toDevice.M11 * _scale;
        var sy = toDevice.M22 * _scale;

        var width = Pill.Width * MainScale.ScaleX;
        var height = Pill.Height * MainScale.ScaleY;
        var radius = Pill.CornerRadius.TopLeft * Math.Min(MainScale.ScaleX, MainScale.ScaleY);
        var left = BaseWidth / 2 + MainShift.X + PillParallax.X - width / 2;
        var top = IslandTop + PillParallax.Y;
        Span<(int, int, int, int, int)> rects = stackalloc (int, int, int, int, int)[2];
        rects[0] = DeviceRect(left, top, width, height, radius);
        var count = 1;

        if (Bubble.Visibility == Visibility.Visible)
        {
            var d = _bubbleSize * BubbleScale.ScaleX;
            rects[1] = DeviceRect(BaseWidth / 2 + BubbleShift.X - d / 2, IslandTop + (_bubbleSize - d) / 2, d, d, d / 2);
            count = 2;
        }
        NativeMethods.SetRoundWindowRegion(_hwnd, rects[..count]);

        (int, int, int, int, int) DeviceRect(double l, double t, double w, double h, double r) =>
            ((int)Math.Floor(l * sx), (int)Math.Floor(t * sy),
             (int)Math.Ceiling((l + w) * sx), (int)Math.Ceiling((t + h) * sy), (int)Math.Round(r * 2 * sx));
    }

    /// <summary>A tile of fine random grain for the "mossy" texture.</summary>
    private static ImageBrush CreateGrain()
    {
        const int size = 128;
        var pixels = new byte[size * size * 4];
        var random = new Random(42);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var v = (byte)random.Next(256);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = v; // grey, BGRA
            pixels[i + 3] = (byte)random.Next(10, 70);
        }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    private void Housekeeping()
    {
        if (_hwnd == IntPtr.Zero) return;

        _fullscreen = _settings.Get<GeneralSettings>().HideInFullscreen && NativeMethods.IsForegroundFullscreen(_hwnd);
        UpdateVisibility();
        if (!_hidden) NativeMethods.BringToTop(_hwnd);
    }

    /// <summary>Hides or shows the island when the user's choice, the fullscreen state or the current activity changes.</summary>
    private void UpdateVisibility(bool animate = true)
    {
        if (_hwnd == IntPtr.Zero) return;
        var hide = IslandVisibility.ShouldHide(UserHidden, _fullscreen, _activities.Current);
        if (hide == _hidden && animate) return;
        var changed = hide != _hidden;
        _hidden = hide;

        // The blur is drawn by Windows, not WPF, so fading Root out wouldn't hide it.
        if (_effect == ThemeEffects.Acrylic)
        {
            if (hide)
            {
                NativeMethods.ClearAcrylic(_hwnd);
                NativeMethods.ClearWindowRegion(_hwnd);
            }
            else
            {
                ApplyBackdrop();
                ClipBackdrop();
            }
        }
        // Opacity 0 keeps the window alive but makes every pixel click-through.
        if (animate)
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(hide ? 0 : 1, TimeSpan.FromMilliseconds(200)));
        else
        {
            Root.BeginAnimation(OpacityProperty, null);
            Root.Opacity = hide ? 0 : 1;
        }

        if (hide)
        {
            // Nobody can reach a hidden island; let go of hover so the quick panel / shelf close.
            _hoverDelay.Stop();
            _leaveDelay.Stop();
            if (_hover)
            {
                _hover = false;
                _bubbleHover = false;
                HoverEnded?.Invoke();
            }
        }
        else
        {
            NativeMethods.BringToTop(_hwnd); // break through over a fullscreen app right away
        }
        if (changed) HiddenChanged?.Invoke(hide);
    }

    private void Island_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _leaveDelay.Stop();
        if (!_hover) _hoverDelay.Start();
    }

    private void Island_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hoverDelay.Stop();
        if (_hover) _leaveDelay.Start();
    }

    /// <summary>Parallax & tilt: lean the open island toward the pointer.</summary>
    private void Island_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_hover || _hidden || !Motion.ParallaxTilt || Pill.ActualWidth < 1 || Pill.ActualHeight < 1) return;
        var p = e.GetPosition(Pill);
        _tiltX.Target = Math.Clamp(p.X / Pill.ActualWidth * 2 - 1, -1, 1);
        _tiltY.Target = Math.Clamp(p.Y / Pill.ActualHeight * 2 - 1, -1, 1);
        _loop.Kick();
    }

    private void Island_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        // Count as hovering right away so leaving the island reliably raises HoverEnded.
        _hoverDelay.Stop();
        _hover = true;
        _bubbleHover = false;
        QuickPanelRequested?.Invoke();
    }

    /// <summary>
    /// Re-reads where the cursor is and closes the island if it's gone. Needed after dragging a file out of the
    /// island: the OLE drag loop swallows the MouseLeave, so the island would otherwise stay open.
    /// </summary>
    public void RecheckHover()
    {
        _dragWatch.Stop();
        _dragWatch.Start();
        CheckCursorAfterDrag();
    }

    /// <summary>
    /// Until WPF sees the mouse again (MouseEnter), watch the cursor ourselves: it may leave over transparent pixels,
    /// which send no messages, and then MouseLeave never comes.
    /// </summary>
    private void CheckCursorAfterDrag()
    {
        if (!_hover || Island.IsMouseOver)
        {
            _dragWatch.Stop();
            return;
        }
        if (!NativeMethods.GetCursorPos(out var x, out var y) || PresentationSource.FromVisual(Pill) is null) return;
        var p = Pill.PointFromScreen(new Point(x, y));
        var inside = p.X >= 0 && p.Y >= 0 && p.X <= Pill.ActualWidth && p.Y <= Pill.ActualHeight;
        if (inside) return;

        _dragWatch.Stop();
        _hoverDelay.Stop();
        _leaveDelay.Start();
    }

    private bool IsExternalFileDrag(DragEventArgs e) =>
        AcceptsFiles && e.Data.GetDataPresent(DataFormats.FileDrop) && !e.Data.GetDataPresent(FileActions.ShelfDragFormat);

    private void Island_DragEnter(object sender, DragEventArgs e)
    {
        // DragLeave/DragEnter also fire when moving between the island's own children, so the leave delay smooths that out.
        _leaveDelay.Stop();
        if (!IsExternalFileDrag(e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;

        // Open straight away so the island grows into a big drop zone.
        _hoverDelay.Stop();
        _hover = true;
        _bubbleHover = false;
        FileDragEntered?.Invoke();
        Render();
    }

    private void Island_DragOver(object sender, DragEventArgs e)
    {
        _leaveDelay.Stop();
        e.Effects = IsExternalFileDrag(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Island_DragLeave(object sender, DragEventArgs e)
    {
        if (_hover) _leaveDelay.Start();
    }

    private void Island_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!IsExternalFileDrag(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        e.Effects = DragDropEffects.Copy;
        _leaveDelay.Stop();
        FilesDropped?.Invoke(files);
        RecheckHover();
    }
}
