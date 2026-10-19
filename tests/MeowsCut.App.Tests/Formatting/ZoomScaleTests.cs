using FluentAssertions;
using MeowsCut.App.Formatting;

namespace MeowsCut.App.Tests.Formatting;

/// <summary>Ползунок масштаба от 10% до 5000%.</summary>
public class ZoomScaleTests
{
    [Fact]
    public void Covers_the_whole_range()
    {
        ZoomScale.FromSlider(ZoomScale.Max).Should().Be(5000);
        ZoomScale.FromSlider(ZoomScale.Min).Should().Be(10);
    }

    [Fact]
    public void Every_doubling_costs_the_same_distance()
    {
        (ZoomScale.ToSlider(400) - ZoomScale.ToSlider(200)).Should().BeApproximately(
            ZoomScale.ToSlider(2000) - ZoomScale.ToSlider(1000), 1e-9);
    }

    [Fact]
    public void Sticks_to_one_hundred_percent()
    {
        ZoomScale.FromSlider(0.03).Should().Be(100);
    }

    [Fact]
    public void Round_trips_whole_percents()
    {
        ZoomScale.FromSlider(ZoomScale.ToSlider(1350)).Should().Be(1350);
    }
}
