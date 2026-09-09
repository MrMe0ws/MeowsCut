using FluentAssertions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;
using static MeowsCut.Core.Tests.Editing.Fake;

namespace MeowsCut.Core.Tests.Editing;

public class ClipTests
{
    [Fact]
    public void Clip_from_source_covers_whole_file()
    {
        var clip = Clip.FromSource(Source(seconds: 42));

        clip.SourceRange.Should().Be(new TimeRange(TimeSpan.Zero, S(42)));
        clip.TimelineDuration.Should().Be(S(42));
        clip.Speed.Should().Be(1d);
        clip.IsFullSource.Should().BeTrue();
    }

    [Fact]
    public void Timeline_duration_shrinks_with_speed()
    {
        var clip = Clip.FromSource(Source(seconds: 60)).WithSpeed(2d);

        clip.TimelineDuration.Should().Be(S(30));
    }

    [Fact]
    public void Timeline_duration_grows_when_slowed_down()
    {
        var clip = Clip.FromSource(Source(seconds: 10)).WithSpeed(0.5);

        clip.TimelineDuration.Should().Be(S(20));
    }

    [Fact]
    public void Speed_is_clamped_to_supported_range()
    {
        var clip = Clip.FromSource(Source());

        clip.WithSpeed(100).Speed.Should().Be(Clip.MaxSpeed);
        clip.WithSpeed(0.001).Speed.Should().Be(Clip.MinSpeed);
    }

    [Fact]
    public void Source_time_accounts_for_in_point_and_speed()
    {
        var clip = Clip.FromSource(Source(seconds: 60), new TimeRange(S(10), S(40))).WithSpeed(2d);

        // На таймлайне клип длится 15 с; 5 с внутри него — это 10 с исходника от точки входа.
        clip.ToSourceTime(S(5)).Should().Be(S(20));
    }

    [Fact]
    public void Split_divides_source_range_at_the_right_point()
    {
        var clip = Clip.FromSource(Source(seconds: 20));

        var (left, right) = clip.SplitAt(S(5));

        left.SourceRange.Should().Be(new TimeRange(TimeSpan.Zero, S(5)));
        right.SourceRange.Should().Be(new TimeRange(S(5), S(20)));
        (left.TimelineDuration + right.TimelineDuration).Should().Be(clip.TimelineDuration);
    }

    [Fact]
    public void Split_of_sped_up_clip_maps_offset_through_speed()
    {
        var clip = Clip.FromSource(Source(seconds: 20)).WithSpeed(2d);

        var (left, right) = clip.SplitAt(S(5));

        // 5 с таймлайна при скорости 2× — это 10 с исходника.
        left.SourceRange.End.Should().Be(S(10));
        right.SourceRange.Start.Should().Be(S(10));
    }

    [Fact]
    public void Split_gives_both_halves_new_identity()
    {
        var clip = Clip.FromSource(Source(seconds: 20));

        var (left, right) = clip.SplitAt(S(5));

        left.Id.Should().NotBe(clip.Id);
        right.Id.Should().NotBe(clip.Id);
        left.Id.Should().NotBe(right.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(-5)]
    [InlineData(25)]
    public void Split_outside_clip_is_rejected(double offsetSeconds)
    {
        var clip = Clip.FromSource(Source(seconds: 20));

        var act = () => clip.SplitAt(S(offsetSeconds));

        act.Should().Throw<EditOperationException>();
    }

    [Fact]
    public void Trim_start_moves_in_point_forward()
    {
        var clip = Clip.FromSource(Source(seconds: 20)).TrimStart(S(5));

        clip.SourceRange.Should().Be(new TimeRange(S(5), S(20)));
        clip.TimelineDuration.Should().Be(S(15));
    }

    [Fact]
    public void Trim_start_cannot_pass_the_out_point()
    {
        var clip = Clip.FromSource(Source(seconds: 20)).TrimStart(S(100));

        clip.SourceRange.Duration.Should().BeGreaterThanOrEqualTo(Clip.MinSourceDuration);
        clip.SourceRange.End.Should().Be(S(20));
    }

    [Fact]
    public void Trim_start_backwards_stops_at_the_beginning_of_source()
    {
        var clip = Clip.FromSource(Source(seconds: 20), new TimeRange(S(5), S(15)));

        var widened = clip.TrimStart(S(-100));

        widened.SourceRange.Start.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Trim_end_cannot_pass_the_end_of_source()
    {
        var clip = Clip.FromSource(Source(seconds: 20)).TrimEnd(S(100));

        clip.SourceRange.End.Should().Be(S(20), "за пределами файла кадров нет");
    }

    [Fact]
    public void Trim_delta_is_measured_in_timeline_time()
    {
        // При скорости 2× сдвиг края на 5 с таймлайна — это 10 с исходника.
        var clip = Clip.FromSource(Source(seconds: 60)).WithSpeed(2d).TrimStart(S(5));

        clip.SourceRange.Start.Should().Be(S(10));
    }

    [Fact]
    public void Muted_clip_reports_no_audio()
    {
        var clip = Clip.FromSource(Source()).WithAudio(ClipAudio.Muted);

        clip.HasAudio.Should().BeFalse();
    }

    [Fact]
    public void Clip_from_source_without_audio_never_has_audio()
    {
        var clip = Clip.FromSource(Source(withAudio: false));

        clip.Audio.Enabled.Should().BeTrue("настройка клипа не зависит от источника");
        clip.HasAudio.Should().BeFalse("но звука в источнике нет");
    }

    [Fact]
    public void Too_short_fragment_is_rejected()
    {
        var act = () => Clip.FromSource(Source(seconds: 20), new TimeRange(S(1), S(1.001)));

        act.Should().Throw<EditOperationException>();
    }
}
