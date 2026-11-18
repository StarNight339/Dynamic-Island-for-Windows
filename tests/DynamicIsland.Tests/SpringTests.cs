using DynamicIsland.Core;
using DynamicIsland.Core.Settings;
using Xunit;

namespace DynamicIsland.Tests;

public class SpringTests
{
    [Fact]
    public void Default_motion_matches_the_original_spring()
    {
        var defaults = new MotionSettings();
        var (stiffness, ratio) = Spring.FromMotion(defaults.Bounciness, defaults.Speed);
        Assert.Equal(320, stiffness, 3);
        Assert.Equal(0.72, ratio, 2);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(100, 0.45)]
    [InlineData(-20, 1.0)] // clamped
    [InlineData(500, 0.45)]
    public void Bounciness_maps_to_damping(int bounciness, double ratio) =>
        Assert.Equal(ratio, Spring.FromMotion(bounciness, 100).DampingRatio, 3);

    [Fact]
    public void Faster_is_stiffer() =>
        Assert.True(Spring.FromMotion(50, 150).Stiffness > Spring.FromMotion(50, 50).Stiffness);

    [Theory]
    [InlineData(0, 100)]
    [InlineData(50, 100)]
    [InlineData(100, 50)]
    [InlineData(100, 150)]
    public void Every_setting_settles_on_target(int bounciness, int speed)
    {
        var (stiffness, ratio) = Spring.FromMotion(bounciness, speed);
        var spring = new Spring(100, stiffness, ratio);
        spring.Target = 400;
        for (var i = 0; i < 60 * 10 && !spring.IsSettled; i++) spring.Step(1.0 / 60);
        Assert.True(spring.IsSettled);
        Assert.Equal(400, spring.Value);
        Assert.Equal(0, spring.Velocity);
    }

    [Fact]
    public void Critically_damped_never_overshoots()
    {
        var (stiffness, ratio) = Spring.FromMotion(0, 100);
        var spring = new Spring(0, stiffness, ratio) { Target = 100 };
        for (var i = 0; i < 600 && !spring.IsSettled; i++)
        {
            spring.Step(1.0 / 60);
            Assert.True(spring.Value <= 100.0001);
        }
    }

    [Fact]
    public void Bouncy_overshoots()
    {
        var (stiffness, ratio) = Spring.FromMotion(100, 100);
        var spring = new Spring(0, stiffness, ratio) { Target = 100 };
        var peak = 0.0;
        for (var i = 0; i < 600 && !spring.IsSettled; i++)
        {
            spring.Step(1.0 / 60);
            peak = Math.Max(peak, spring.Value);
        }
        Assert.True(peak > 110);
    }

    [Fact]
    public void Small_tolerance_springs_settle_precisely()
    {
        var spring = new Spring(0.88, tolerance: 0.0005) { Target = 1 };
        for (var i = 0; i < 600 && !spring.IsSettled; i++) spring.Step(1.0 / 60);
        Assert.True(spring.IsSettled);
        Assert.Equal(1, spring.Value);
    }
}
