using FluentAssertions;
using MeowsCut.App.Timeline;

namespace MeowsCut.App.Tests.Timeline;

public class TimelineMetricsTests
{
    private static TimelineMetrics Metrics(double pixelsPerSecond = 40, double viewport = 1000) =>
        new() { PixelsPerSecond = pixelsPerSecond, ViewportWidth = viewport };

    [Fact]
    public void Time_and_position_convert_both_ways()
    {
        var metrics = Metrics();

        metrics.TimeToX(TimeSpan.FromSeconds(5)).Should().Be(200);
        metrics.XToTime(200).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Scroll_shifts_the_visible_window()
    {
        var metrics = Metrics();
        metrics.Scroll = TimeSpan.FromSeconds(10);

        metrics.TimeToX(TimeSpan.FromSeconds(10)).Should().Be(0);
        metrics.TimeToX(TimeSpan.FromSeconds(12)).Should().Be(80);
    }

    [Fact]
    public void Negative_scroll_is_not_allowed()
    {
        var metrics = Metrics();
        metrics.Scroll = TimeSpan.FromSeconds(-5);

        metrics.Scroll.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Zoom_keeps_the_point_under_the_cursor_in_place()
    {
        var metrics = Metrics();
        metrics.Scroll = TimeSpan.FromSeconds(3);

        var timeUnderCursor = metrics.XToTime(400);
        metrics.ZoomAt(2d, 400);

        // Иначе содержимое уезжает из-под мыши и попасть по нужному кадру невозможно.
        metrics.XToTime(400).TotalSeconds.Should().BeApproximately(timeUnderCursor.TotalSeconds, 0.001);
    }

    [Fact]
    public void Zoom_is_limited_on_both_ends()
    {
        var metrics = Metrics();

        for (var i = 0; i < 40; i++)
        {
            metrics.ZoomAt(2d, 0);
        }

        metrics.PixelsPerSecond.Should().Be(TimelineMetrics.MaxPixelsPerSecond);

        for (var i = 0; i < 60; i++)
        {
            metrics.ZoomAt(0.5, 0);
        }

        metrics.PixelsPerSecond.Should().Be(TimelineMetrics.MinPixelsPerSecond);
    }

    [Fact]
    public void Zoom_to_fit_puts_the_whole_sequence_in_view()
    {
        var metrics = Metrics(viewport: 1000);

        metrics.ZoomToFit(TimeSpan.FromSeconds(20));

        metrics.Scroll.Should().Be(TimeSpan.Zero);
        metrics.TimeToX(TimeSpan.FromSeconds(20)).Should().BeLessThanOrEqualTo(1000);
        metrics.TimeToX(TimeSpan.FromSeconds(20)).Should().BeGreaterThan(900, "иначе останется много пустого места");
    }

    [Fact]
    public void Zoom_to_fit_ignores_an_empty_sequence()
    {
        var metrics = Metrics();
        var before = metrics.PixelsPerSecond;

        metrics.ZoomToFit(TimeSpan.Zero);

        metrics.PixelsPerSecond.Should().Be(before);
    }

    [Fact]
    public void Ensure_visible_scrolls_to_a_point_beyond_the_right_edge()
    {
        var metrics = Metrics(pixelsPerSecond: 100, viewport: 500);

        metrics.EnsureVisible(TimeSpan.FromSeconds(30));

        var x = metrics.TimeToX(TimeSpan.FromSeconds(30));
        x.Should().BeInRange(0, 500);
    }

    [Fact]
    public void Ensure_visible_does_nothing_for_a_point_already_in_view()
    {
        var metrics = Metrics(pixelsPerSecond: 100, viewport: 500);
        metrics.Scroll = TimeSpan.FromSeconds(1);

        metrics.EnsureVisible(TimeSpan.FromSeconds(3));

        metrics.Scroll.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(40)]
    [InlineData(200)]
    [InlineData(600)]
    public void Ruler_labels_never_crowd_each_other(double pixelsPerSecond)
    {
        var metrics = Metrics(pixelsPerSecond);

        var step = metrics.RulerStep();

        (step.TotalSeconds * pixelsPerSecond).Should().BeGreaterThanOrEqualTo(
            70, "подписи времени иначе сливаются в кашу");
    }
}
