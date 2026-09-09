using FluentAssertions;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Tests.Media;

public class TimeRangeTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Duration_is_difference_of_bounds()
    {
        new TimeRange(S(5), S(20)).Duration.Should().Be(S(15));
    }

    [Fact]
    public void Inverted_range_is_empty_rather_than_negative()
    {
        var range = new TimeRange(S(20), S(5));

        range.Duration.Should().Be(TimeSpan.Zero);
        range.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Clamp_trims_range_to_media_duration()
    {
        var range = new TimeRange(S(-3), S(120)).Clamp(S(60));

        range.Start.Should().Be(TimeSpan.Zero);
        range.End.Should().Be(S(60));
    }

    [Fact]
    public void Clamp_keeps_start_before_end_when_start_is_beyond_media()
    {
        var range = new TimeRange(S(90), S(120)).Clamp(S(60));

        range.Start.Should().Be(S(60));
        range.End.Should().Be(S(60));
        range.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Overlaps_is_false_for_touching_ranges()
    {
        // Полуинтервал: [0,5) и [5,8) стыкуются, но не пересекаются.
        new TimeRange(S(0), S(5)).Overlaps(new TimeRange(S(5), S(8))).Should().BeFalse();
    }

    [Fact]
    public void Contains_excludes_upper_bound()
    {
        var range = new TimeRange(S(0), S(5));

        range.Contains(S(0)).Should().BeTrue();
        range.Contains(S(4.999)).Should().BeTrue();
        range.Contains(S(5)).Should().BeFalse();
    }
}
