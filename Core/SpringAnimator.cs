using System.Windows.Media;

namespace DynamicIsland.Core;

/// <summary>Damped spring, integrated with small fixed sub-steps for stability.</summary>
public sealed class Spring
{
    private double _stiffness;
    private double _damping;

    public Spring(double value, double stiffness = 320, double dampingRatio = 0.72, double tolerance = 0.05)
    {
        Value = Target = value;
        Tolerance = tolerance;
        Configure(stiffness, dampingRatio);
    }

    public double Value { get; private set; }
    public double Target { get; set; }
    public double Velocity { get; private set; }

    /// <summary>How close (in the spring's own units, and per second for velocity) counts as settled.</summary>
    public double Tolerance { get; }

    public bool IsSettled => Math.Abs(Value - Target) < Tolerance && Math.Abs(Velocity) < Tolerance;

    public void Configure(double stiffness, double dampingRatio)
    {
        _stiffness = stiffness;
        _damping = 2 * dampingRatio * Math.Sqrt(stiffness);
    }

    /// <summary>Jumps to <paramref name="value"/> at rest, without animating.</summary>
    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    /// <summary>Starts from <paramref name="value"/> (keeping the target), e.g. to pop content in from a smaller scale.</summary>
    public void Reset(double value)
    {
        Value = value;
        Velocity = 0;
    }

    public void Step(double dt)
    {
        const double sub = 1.0 / 480;
        for (var t = 0.0; t < dt; t += sub)
        {
            var h = Math.Min(sub, dt - t);
            var force = -_stiffness * (Value - Target) - _damping * Velocity;
            Velocity += force * h;
            Value += Velocity * h;
        }
        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
        }
    }

    /// <summary>
    /// Maps the Motion settings to spring constants. Bounciness 0 is critically damped (no overshoot), 50 is the
    /// original island feel (damping ratio ≈ 0.72), 100 is very springy. Speed scales the natural frequency.
    /// </summary>
    public static (double Stiffness, double DampingRatio) FromMotion(int bounciness, int speed)
    {
        var b = Math.Clamp(bounciness, 0, 100) / 100.0;
        var s = Math.Clamp(speed, 50, 150) / 100.0;
        return (320 * s * s, 1.0 - 0.55 * b);
    }
}

/// <summary>
/// Steps a set of springs off the WPF render loop and calls back once per frame.
/// Hooks <see cref="CompositionTarget.Rendering"/> only while something is moving, so a settled island costs nothing.
/// </summary>
public sealed class SpringLoop(Action onFrame, params Spring[] springs)
{
    private TimeSpan _last;
    private bool _running;

    public bool IsRunning => _running;

    /// <summary>Call after changing a target: runs until every spring has settled.</summary>
    public void Kick()
    {
        if (_running) return;
        _running = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;
        // First frame or a long stall: use one nominal frame instead of a huge jump.
        var dt = _last == TimeSpan.Zero ? 1.0 / 60 : Math.Min((now - _last).TotalSeconds, 1.0 / 20);
        _last = now;

        var settled = true;
        foreach (var spring in springs)
        {
            spring.Step(dt);
            settled &= spring.IsSettled;
        }
        onFrame();

        if (settled)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
    }
}
