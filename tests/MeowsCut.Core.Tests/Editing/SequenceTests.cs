using FluentAssertions;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;
using static MeowsCut.Core.Tests.Editing.Fake;

namespace MeowsCut.Core.Tests.Editing;

public class SequenceTests
{
    private static Sequence ThreeClips()
    {
        var source = Source(seconds: 60);

        var track = new VideoTrack(
        [
            Clip.FromSource(source, new TimeRange(TimeSpan.Zero, S(10))),
            Clip.FromSource(source, new TimeRange(S(20), S(25))),
            Clip.FromSource(source, new TimeRange(S(40), S(60)))
        ]);

        return new Sequence(track, SequenceFormat.FromMedia(source.Info));
    }

    [Fact]
    public void Duration_is_sum_of_clip_durations()
    {
        ThreeClips().Duration.Should().Be(S(35));
    }

    [Fact]
    public void Format_comes_from_the_source()
    {
        var sequence = Sequence.FromSource(Source(width: 1280, height: 720, fps: 25));

        sequence.Format.Size.Should().Be(new FrameSize(1280, 720));
        sequence.Format.FrameRate.Value.Should().BeApproximately(25, 0.001);
    }

    [Fact]
    public void Rotated_source_defines_format_by_display_size()
    {
        var info = Info(width: 1920, height: 1080) with { };
        var rotated = info with
        {
            VideoStreams = [info.PrimaryVideo! with { RotationDegrees = 90 }]
        };

        var format = SequenceFormat.FromMedia(rotated);

        format.Size.Should().Be(new FrameSize(1080, 1920), "вертикальное видео и в проекте вертикальное");
    }

    [Fact]
    public void Placed_clips_lie_end_to_end_without_gaps()
    {
        var placed = ThreeClips().EnumeratePlaced().ToArray();

        placed[0].Start.Should().Be(TimeSpan.Zero);
        placed[1].Start.Should().Be(S(10));
        placed[2].Start.Should().Be(S(15));
        placed[2].End.Should().Be(S(35));
    }

    [Fact]
    public void Resolve_maps_timeline_time_to_source_time()
    {
        var sequence = ThreeClips();

        // 12 с таймлайна — это второй клип, 2 с от его начала, то есть 22 с исходника.
        var resolved = sequence.Resolve(S(12));

        resolved.Should().NotBeNull();
        resolved!.Value.SourceTime.Should().Be(S(22));
    }

    [Fact]
    public void Resolve_respects_clip_speed()
    {
        var source = Source(seconds: 60);
        var clip = Clip.FromSource(source, new TimeRange(S(10), S(30))).WithSpeed(2d);
        var sequence = new Sequence(new VideoTrack([clip]), SequenceFormat.FromMedia(source.Info));

        // Клип занимает 10 с таймлайна; 4 с внутри него — 8 с исходника после точки входа.
        sequence.Resolve(S(4))!.Value.SourceTime.Should().Be(S(18));
    }

    [Fact]
    public void Clip_boundary_belongs_to_the_next_clip()
    {
        var sequence = ThreeClips();

        sequence.ClipAt(S(10))!.Value.Index.Should().Be(1);
        sequence.ClipAt(S(9.99))!.Value.Index.Should().Be(0);
    }

    [Fact]
    public void Beyond_the_end_nothing_is_playing()
    {
        var sequence = ThreeClips();

        sequence.ClipAt(sequence.Duration).Should().BeNull();
        sequence.Resolve(S(-1)).Should().BeNull();
    }

    [Fact]
    public void Split_at_time_divides_the_covering_clip()
    {
        var sequence = ThreeClips().SplitAt(S(5));

        sequence.ClipCount.Should().Be(4);
        sequence.Duration.Should().Be(S(35), "разрез не меняет общую длительность");
    }

    [Fact]
    public void Split_on_a_boundary_changes_nothing()
    {
        var sequence = ThreeClips();

        sequence.SplitAt(S(10)).ClipCount.Should().Be(3);
        sequence.SplitAt(sequence.Duration).ClipCount.Should().Be(3);
    }

    [Fact]
    public void Sequence_reports_audio_only_when_some_clip_sounds()
    {
        var sequence = Sequence.FromSource(Source());
        sequence.HasAudio.Should().BeTrue();

        var muted = sequence.WithTrack(
            new VideoTrack(sequence.Video.Clips.Select(c => c.WithAudio(ClipAudio.Muted)).ToArray()));

        muted.HasAudio.Should().BeFalse();
    }
}
