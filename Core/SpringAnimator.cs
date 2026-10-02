using System.Windows.Media;

namespace DynamicIsland.Core;

/// <summary>Slightly under-damped spring, integrated with small fixed sub-steps for stability.</summary>
public sealed class Spring(double value, double stiffness = 320, double dampingRatio = 0.72)
{
    private readonly double _damping = 2 * dampingRatio * Math.Sqrt(stiffness);
    private double _velocity;

    public double Value { get; private set; } = value;
    public double Target { get; set; } = value;

    public bool IsSettled => Math.Abs(Value - Target) < 0.05 && Math.Abs(_velocity) < 0.05;

    public void Step(double dt)
    {
        const double sub = 1.0 / 480;
        for (var t = 0.0; t < dt; t += sub)
        {
            var h = Math.Min(sub, dt - t);
            var force = -stiffness * (Value - Target) - _damping * _velocity;
            _velocity += force * h;
            Value += _velocity * h;
        }
        if (IsSettled)
        {
            Value = Target;
            _velocity = 0;
        }
    }
}

/// <summary>
/// Drives width / height / corner-radius springs off the WPF render loop and reports each frame.
/// Hooks <see cref="CompositionTarget.Rendering"/> only while something is moving.
/// </summary>
public sealed class SpringAnimator
{
    private readonly Spring _w, _h, _r;
    private readonly Action<double, double, double> _apply;
    private TimeSpan _last;
    private bool _running;

    public SpringAnimator(double w, double h, double r, Action<double, double, double> apply)
    {
        _w = new Spring(w);
        _h = new Spring(h);
        _r = new Spring(r, dampingRatio: 0.9);
        _apply = apply;
        _apply(w, h, r);
    }

    public void AnimateTo(double w, double h, double r)
    {
        _w.Target = w;
        _h.Target = h;
        _r.Target = r;
        if (_running) return;
        _running = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        // First frame or a long stall: use one nominal frame instead of a huge jump.
        var dt = _last == TimeSpan.Zero ? 1.0 / 60 : Math.Min((now - _last).TotalSeconds, 1.0 / 20);
        if (now == _last) return;
        _last = now;

        _w.Step(dt);
        _h.Step(dt);
        _r.Step(dt);
        _apply(_w.Value, _h.Value, Math.Max(0, _r.Value));

        if (_w.IsSettled && _h.IsSettled && _r.IsSettled)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
    }
}
