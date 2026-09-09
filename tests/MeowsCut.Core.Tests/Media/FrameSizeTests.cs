using FluentAssertions;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Tests.Media;

public class FrameSizeTests
{
    [Fact]
    public void ScaledToHeight_keeps_aspect_ratio()
    {
        var result = new FrameSize(1920, 1080).ScaledToHeight(720);

        result.Should().Be(new FrameSize(1280, 720));
    }

    [Fact]
    public void ScaledToHeight_rounds_to_even_values()
    {
        // 1080/1920 вертикальное видео при высоте 500 даёт нечётную ширину — H.264 такое не примет.
        var result = new FrameSize(1080, 1920).ScaledToHeight(500);

        (result.Width % 2).Should().Be(0);
        (result.Height % 2).Should().Be(0);
    }

    [Fact]
    public void ScaledToFit_does_not_upscale()
    {
        var source = new FrameSize(640, 360);

        source.ScaledToFit(1920, 1080).Should().Be(source);
    }

    [Fact]
    public void ScaledToFit_shrinks_within_bounds()
    {
        var result = new FrameSize(1920, 1080).ScaledToFit(512, 512);

        result.Width.Should().BeLessThanOrEqualTo(512);
        result.Height.Should().BeLessThanOrEqualTo(512);
        result.AspectRatio.Should().BeApproximately(16d / 9d, 0.01);
    }

    [Fact]
    public void RoundedToEven_bumps_odd_sides_up()
    {
        new FrameSize(511, 383).RoundedToEven().Should().Be(new FrameSize(512, 384));
    }
}
