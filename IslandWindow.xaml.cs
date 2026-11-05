using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    private double _expandedRadius = 36;
    private double _scale = 1;
    private string _effect = ThemeEffects.Solid;
    private Color _acrylicTint = Colors.Black;

    private readonly ActivityManager _activities;
    private readonly SettingsStore _settings;
    private readonly ClockView _idleView;
    private readonly SpringAnimator _spring;
    private readonly DispatcherTimer _hoverDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _leaveDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _housekeeping = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _dragWatch = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private IslandView? _shown;
    private bool _hover;
    private const int HideHotkeyId = 0x4449; // "DI"
    private const uint VK_H = 0x48;

    private bool _fullscreen;
    private bool _hidden;
    private bool _fullyHidden; // the hide slide has finished; the island is out of sight

    /// <summary>0 = shown, 100 = tucked into the top edge. Under-damped, so showing bounces a little past its spot.</summary>
    private readonly Spring _slide = new(0, stiffness: 300, dampingRatio: 0.62);
    private bool _slideRunning;
    private TimeSpan _slideLast;
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

    public IslandWindow(ActivityManager activities, ClockView idleView, SettingsStore settings)
    {
        InitializeComponent();
        _activities = activities;
        _settings = settings;
        _idleView = idleView;

        var initial = idleView.CompactSize;
        _spring = new SpringAnimator(initial.Width, initial.Height, initial.Height / 2, ApplyFrame);

        _activities.CurrentChanged += Render;
        _hoverDelay.Tick += (_, _) => { _hoverDelay.Stop(); _hover = true; Render(); };
        _leaveDelay.Tick += (_, _) => { _leaveDelay.Stop(); _hover = false; HoverEnded?.Invoke(); Render(); };
        _housekeeping.Tick += (_, _) => Housekeeping();
        _dragWatch.Tick += (_, _) => CheckCursorAfterDrag();

        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(PositionWindow);
        _settings.Changed += id =>
        {
            if (id != GeneralSettings.SectionId) return;
            UpdateHotkey();
            UpdateVisibility();
        };
        Render();
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
        var current = _activities.Current;
        var view = current?.View ?? _idleView;
        var expanded = view.CanExpand && (_hover || current?.AutoExpand == true);

        var swapped = !ReferenceEquals(view, _shown);
        var layoutChanged = swapped || view.IsExpanded != expanded;
        if (swapped)
        {
            Host.Content = view;
            _shown = view;
        }
        view.SetExpanded(expanded);

        State = current is null && !expanded ? IslandState.Idle
            : expanded ? IslandState.Expanded
            : IslandState.Compact;

        var size = view.CurrentSize;
        Host.Width = size.Width;
        Host.Height = size.Height;
        _spring.AnimateTo(size.Width, size.Height, expanded ? _expandedRadius : size.Height / 2);

        if (layoutChanged) FadeInContent(scaleFrom: swapped ? 0.9 : 0.96);
        UpdateVisibility(); // an important notification breaks through a hide; the island hides again once it's gone
    }

    private void FadeInContent(double scaleFrom)
    {
        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var delay = TimeSpan.FromMilliseconds(60);

        Host.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease, BeginTime = delay, FillBehavior = FillBehavior.Stop });
        var scale = new DoubleAnimation(scaleFrom, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        HostScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        HostScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    /// <summary>Called every animation frame with the spring values.</summary>
    private void ApplyFrame(double width, double height, double radius)
    {
        radius = Math.Min(radius, Math.Min(width, height) / 2);
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
        if (_effect == ThemeEffects.Acrylic && !_fullyHidden) ClipBackdrop(width, height, radius);

        // Host sits centred above the pill at its final size; clip it to the pill's current shape.
        var hostWidth = double.IsNaN(Host.Width) ? width : Host.Width;
        var offsetX = (hostWidth - width) / 2;
        Host.Clip = new RectangleGeometry(new Rect(offsetX, 0, Math.Max(0, width), Math.Max(0, height)), radius, radius);
    }

    /// <summary>Theme: the capsule's background and effect overlays. Called by <see cref="ThemeManager"/>.</summary>
    public void ApplyLook(Brush background, string effect, bool texture, double textureOpacity, Color acrylicTint)
    {
        Pill.Background = background;
        PillTexture.Visibility = texture ? Visibility.Visible : Visibility.Collapsed;
        PillTexture.Opacity = textureOpacity;
        if (texture) PillTexture.Background ??= CreateGrain();
        PillSheen.Visibility = effect is ThemeEffects.Glass or ThemeEffects.Acrylic ? Visibility.Visible : Visibility.Collapsed;

        _effect = effect;
        _acrylicTint = acrylicTint;
        ApplyBackdrop();
        // Size the overlays now; otherwise they'd wait for the next spring frame, which won't come while idle.
        if (!double.IsNaN(Pill.Width) && !double.IsNaN(Pill.Height))
            ApplyFrame(Pill.Width, Pill.Height, Pill.CornerRadius.TopLeft);
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
        if (_effect == ThemeEffects.Acrylic && !_fullyHidden)
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
    /// Windows blurs the whole window rectangle, so cut the window to the capsule's current shape (device pixels).
    /// The capsule is centred horizontally, <see cref="IslandTop"/> below the top, all scaled by the theme size,
    /// then moved by the hide/show slide (squashed about its top centre and shifted up).
    /// </summary>
    private void ClipBackdrop(double width, double height, double radius)
    {
        if (_hwnd == IntPtr.Zero || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target) return;
        var toDevice = target.TransformToDevice;
        var sx = toDevice.M11 * _scale;
        var sy = toDevice.M22 * _scale;
        width *= IslandSquash.ScaleX;
        height *= IslandSquash.ScaleY;
        radius *= Math.Min(IslandSquash.ScaleX, IslandSquash.ScaleY);
        var left = (BaseWidth - width) / 2 * sx;
        var top = (IslandTop + IslandSlide.Y) * sy;
        NativeMethods.SetRoundWindowRegion(_hwnd,
            (int)Math.Floor(left), (int)Math.Floor(top),
            (int)Math.Ceiling(left + width * sx), (int)Math.Ceiling(top + height * sy),
            (int)Math.Round(radius * 2 * sx));
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

        // Out of the way while it slides off; once above the window it's transparent, so clicks go through.
        Island.IsHitTestVisible = !hide;
        if (!hide && _fullyHidden)
        {
            _fullyHidden = false;
            ApplyBackdrop(); // the blur comes back and follows the capsule down
        }

        _slide.Target = hide ? 100 : 0;
        if (animate)
        {
            StartSlide();
        }
        else
        {
            _slide.Jump(_slide.Target);
            ApplySlide();
        }

        if (hide)
        {
            // Nobody can reach a hidden island; let go of hover so the quick panel / shelf close.
            _hoverDelay.Stop();
            _leaveDelay.Stop();
            if (_hover)
            {
                _hover = false;
                HoverEnded?.Invoke();
            }
        }
        else
        {
            NativeMethods.BringToTop(_hwnd); // break through over a fullscreen app right away
        }
        if (changed) HiddenChanged?.Invoke(hide);
    }

    private void StartSlide()
    {
        if (_slideRunning) return;
        _slideRunning = true;
        _slideLast = TimeSpan.Zero;
        CompositionTarget.Rendering += OnSlideFrame;
    }

    private void OnSlideFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _slideLast) return;
        var dt = _slideLast == TimeSpan.Zero ? 1.0 / 60 : Math.Min((now - _slideLast).TotalSeconds, 1.0 / 20);
        _slideLast = now;

        _slide.Step(dt);
        ApplySlide();
        if (_slide.IsSettled)
        {
            CompositionTarget.Rendering -= OnSlideFrame;
            _slideRunning = false;
        }
    }

    /// <summary>
    /// Moves the island for the current slide value: up by its own height plus the gap above it, narrowing toward
    /// the top centre so it looks swallowed by the screen edge. The bounce on show (value below 0) stretches it a touch.
    /// </summary>
    private void ApplySlide()
    {
        var p = _slide.Value / 100;
        var height = double.IsNaN(Pill.Height) ? Pill.ActualHeight : Pill.Height;
        IslandSlide.Y = -p * (IslandTop + height + 12);
        IslandSquash.ScaleX = 1 - 0.25 * p;
        IslandSquash.ScaleY = 1 - 0.1 * p;

        if (_hidden && _slide.IsSettled && _slide.Value >= 100)
        {
            // Fully tucked away: drop the blur, which Windows draws and which the slide can't move off-screen by itself.
            _fullyHidden = true;
            NativeMethods.ClearAcrylic(_hwnd);
            NativeMethods.ClearWindowRegion(_hwnd);
        }
        else if (_effect == ThemeEffects.Acrylic && !_fullyHidden && !double.IsNaN(Pill.Width))
        {
            ClipBackdrop(Pill.Width, Pill.Height, Pill.CornerRadius.TopLeft);
        }
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

    private void Island_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        // Count as hovering right away so leaving the island reliably raises HoverEnded.
        _hoverDelay.Stop();
        _hover = true;
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
