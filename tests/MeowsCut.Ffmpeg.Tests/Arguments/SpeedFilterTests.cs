using FluentAssertions;
using MeowsCut.Ffmpeg.Arguments;

namespace MeowsCut.Ffmpeg.Tests.Arguments;

public class SpeedFilterTests
{
    [Theory]
    [InlineData(2.0, new[] { 2.0 })]
    [InlineData(4.0, new[] { 2.0, 2.0 })]
    [InlineData(0.5, new[] { 0.5 })]
    [InlineData(0.25, new[] { 0.5, 0.5 })]
    public void Decomposes_speed_into_supported_tempo_factors(double speed, double[] expected)
    {
        SpeedFilter.DecomposeTempo(speed).Should().Equal(expected);
    }

    [Fact]
    public void Normal_speed_needs_no_tempo_filters()
    {
        SpeedFilter.DecomposeTempo(1d).Should().BeEmpty();
        SpeedFilter.AudioTempoFilters(1d).Should().BeEmpty();
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(0.3)]
    [InlineData(1.25)]
    [InlineData(8.0)]
    public void Factors_multiply_back_to_the_requested_speed(double speed)
    {
        var product = SpeedFilter.DecomposeTempo(speed).Aggregate(1d, (acc, factor) => acc * factor);

        product.Should().BeApproximately(speed, 0.0001);
    }

    [Fact]
    public void Every_factor_is_within_the_atempo_limits()
    {
        foreach (var speed in new[] { 0.1, 0.25, 0.5, 0.75, 1.25, 1.5, 2.0, 4.0, 16.0 })
        {
            foreach (var factor in SpeedFilter.DecomposeTempo(speed))
            {
                factor.Should().BeInRange(0.5, 2.0, $"atempo не принимает другие значения (скорость {speed})");
            }
        }
    }

    [Fact]
    public void Audio_filters_are_rendered_with_invariant_numbers()
    {
        var filters = SpeedFilter.AudioTempoFilters(0.25);

        filters.Should().Equal("atempo=0.5", "atempo=0.5");
    }

    [Fact]
    public void Video_setpts_uses_inverse_of_speed()
    {
        SpeedFilter.VideoSetPts(2d).Should().Be("setpts=0.5*PTS");
        SpeedFilter.VideoSetPts(0.5).Should().Be("setpts=2*PTS");
    }
}
