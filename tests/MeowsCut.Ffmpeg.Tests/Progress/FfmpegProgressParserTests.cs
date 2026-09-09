using FluentAssertions;
using MeowsCut.Ffmpeg.Progress;

namespace MeowsCut.Ffmpeg.Tests.Progress;

public class FfmpegProgressParserTests
{
    private static ProgressSnapshot? FeedAll(FfmpegProgressParser parser, params string[] lines)
    {
        ProgressSnapshot? last = null;
        foreach (var line in lines)
        {
            last = parser.Feed(line) ?? last;
        }

        return last;
    }

    [Fact]
    public void Snapshot_appears_only_when_the_block_is_complete()
    {
        var parser = new FfmpegProgressParser();

        parser.Feed("frame=120").Should().BeNull();
        parser.Feed("out_time_us=4000000").Should().BeNull();

        var snapshot = parser.Feed("progress=continue");

        snapshot.Should().NotBeNull();
        snapshot!.Frame.Should().Be(120);
        snapshot.OutTime.Should().Be(TimeSpan.FromSeconds(4));
        snapshot.Completed.Should().BeFalse();
    }

    [Fact]
    public void Parses_a_full_block()
    {
        var parser = new FfmpegProgressParser();

        var snapshot = FeedAll(
            parser,
            "frame=180",
            "fps=59.80",
            "total_size=1048576",
            "out_time_us=6000000",
            "speed=2.01x",
            "progress=continue");

        snapshot.Should().NotBeNull();
        snapshot!.Fps.Should().BeApproximately(59.8, 0.01);
        snapshot.TotalSizeBytes.Should().Be(1_048_576);
        snapshot.SpeedFactor.Should().BeApproximately(2.01, 0.001);
        snapshot.OutTime.Should().Be(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void Final_block_is_marked_as_completed()
    {
        var parser = new FfmpegProgressParser();

        var snapshot = FeedAll(parser, "out_time_us=6000000", "progress=end");

        snapshot!.Completed.Should().BeTrue();
    }

    [Fact]
    public void Out_time_ms_is_actually_microseconds()
    {
        // Известная особенность ffmpeg: поле называется out_time_ms, а значение в микросекундах.
        var parser = new FfmpegProgressParser();

        var snapshot = FeedAll(parser, "out_time_ms=2500000", "progress=continue");

        snapshot!.OutTime.Should().Be(TimeSpan.FromSeconds(2.5));
    }

    [Fact]
    public void Unknown_speed_does_not_break_parsing()
    {
        var parser = new FfmpegProgressParser();

        var snapshot = FeedAll(parser, "speed=N/A", "out_time_us=1000000", "progress=continue");

        snapshot!.SpeedFactor.Should().BeNull();
        snapshot.OutTime.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Garbage_lines_are_ignored()
    {
        var parser = new FfmpegProgressParser();

        parser.Feed(string.Empty).Should().BeNull();
        parser.Feed("не пара ключ-значение").Should().BeNull();
        parser.Feed("=без ключа").Should().BeNull();
    }

    [Fact]
    public void Values_carry_over_between_blocks()
    {
        var parser = new FfmpegProgressParser();

        FeedAll(parser, "speed=1.50x", "out_time_us=1000000", "progress=continue");
        var second = FeedAll(parser, "out_time_us=2000000", "progress=continue");

        second!.SpeedFactor.Should().BeApproximately(1.5, 0.001, "ffmpeg повторяет не все поля в каждом блоке");
        second.OutTime.Should().Be(TimeSpan.FromSeconds(2));
    }
}
