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
    private const double ExpandedRadius = 36;

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
    private bool _hiddenForFullscreen;
    private IntPtr _hwnd;

    /// <summary>Right-click on the island.</summary>
    public event Action? QuickPanelRequested;

    /// <summary>The pointer left the island (after the leave delay).</summary>
    public event Action? HoverEnded;

    /// <summary>Files from another app are being dragged over the island.</summary>
    public event Action? FileDragEntered;

    /// <summary>Files from another app were dropped on the island.</summary>
    public event Action<string[]>? FilesDropped;

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
        Render();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeOverlay(_hwnd);
        PositionWindow();
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
        _spring.AnimateTo(size.Width, size.Height, expanded ? ExpandedRadius : size.Height / 2);

        if (layoutChanged) FadeInContent(scaleFrom: swapped ? 0.9 : 0.96);
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

        // Host sits centred above the pill at its final size; clip it to the pill's current shape.
        var hostWidth = double.IsNaN(Host.Width) ? width : Host.Width;
        var offsetX = (hostWidth - width) / 2;
        Host.Clip = new RectangleGeometry(new Rect(offsetX, 0, Math.Max(0, width), Math.Max(0, height)), radius, radius);
    }

    private void Housekeeping()
    {
        if (_hwnd == IntPtr.Zero) return;

        var fullscreen = _settings.Get<GeneralSettings>().HideInFullscreen && NativeMethods.IsForegroundFullscreen(_hwnd);
        if (fullscreen != _hiddenForFullscreen)
        {
            _hiddenForFullscreen = fullscreen;
            // Opacity 0 keeps the window alive but makes every pixel click-through.
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(fullscreen ? 0 : 1, TimeSpan.FromMilliseconds(200)));
        }
        if (!fullscreen) NativeMethods.BringToTop(_hwnd);
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
