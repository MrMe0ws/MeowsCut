using FluentAssertions;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;
using static MeowsCut.Core.Tests.Editing.Fake;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Вырезка куска последовательности: предпросмотр фрагмента и ограничение
/// длительности в пресетах опираются на неё.
/// </summary>
public class SequenceSliceTests
{
    private static Sequence Single(double seconds = 20) => Sequence.FromSource(Source(seconds));

    [Fact]
    public void Slice_takes_the_requested_piece()
    {
        var slice = Single().Slice(new TimeRange(S(5), S(9)));

        slice.Duration.Should().Be(S(4));
        slice.Video.Clips[0].SourceRange.Should().Be(new TimeRange(S(5), S(9)));
    }

    [Fact]
    public void Slice_beyond_the_end_is_clamped()
    {
        var slice = Single(20).Slice(new TimeRange(S(18), S(30)));

        slice.Duration.Should().Be(S(2));
    }

    [Fact]
    public void Slice_outside_the_sequence_is_empty()
    {
        Single(20).Slice(new TimeRange(S(25), S(30))).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Slice_keeps_whole_clips_that_fit_inside()
    {
        var sequence = new SplitClipCommand(S(5)).Apply(Single(20));
        sequence = new SplitClipCommand(S(10)).Apply(sequence);

        var slice = sequence.Slice(new TimeRange(S(3), S(12)));

        slice.ClipCount.Should().Be(3, "два края обрезаются, средний клип входит целиком");
        slice.Duration.Should().Be(S(9));
    }

    [Fact]
    public void Slice_accounts_for_clip_speed()
    {
        var sequence = Single(20);
        var sped = new SetClipSpeedCommand(sequence.Video.Clips[0].Id, 2d).Apply(sequence);

        // Клип занимает 10 секунд таймлайна; берём с 2-й по 6-ю.
        var slice = sped.Slice(new TimeRange(S(2), S(6)));

        slice.Duration.Should().Be(S(4));
        slice.Video.Clips[0].SourceRange.Should().Be(new TimeRange(S(4), S(12)));
        slice.Video.Clips[0].Speed.Should().Be(2d);
    }

    [Fact]
    public void Slice_gives_clips_new_identity()
    {
        var sequence = Single(20);
        var slice = sequence.Slice(new TimeRange(S(1), S(5)));

        slice.Video.Clips[0].Id.Should().NotBe(sequence.Video.Clips[0].Id,
            "иначе выделение на доске «перепрыгнет» на клип из предпросмотра");
    }

    [Fact]
    public void Slice_drops_pieces_that_are_too_short_to_matter()
    {
        var sequence = new SplitClipCommand(S(5)).Apply(Single(20));

        // Берём отрезок, от первого клипа в который попадает миллисекунда.
        var slice = sequence.Slice(new TimeRange(S(4.999), S(9)));

        slice.Video.Clips.Should().OnlyContain(clip => clip.SourceRange.Duration >= Clip.MinSourceDuration);
    }

    [Fact]
    public void Empty_range_gives_an_empty_sequence()
    {
        Single().Slice(new TimeRange(S(5), S(5))).IsEmpty.Should().BeTrue();
    }
}
