using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Пустые места на видеодорожке.
/// </summary>
/// <remarks>
/// Зазор — единственное место, где положение клипа перестаёт следовать из одного
/// лишь порядка, поэтому здесь стерегут три ошибки: длительность, не заметившую
/// пустоту; поиск клипа, вернувший соседа вместо тишины; и сдвиг, уехавший всей
/// лентой вместо одного куска.
/// </remarks>
public class VideoTrackGapTests
{
    private static Sequence TwoClips()
    {
        var source = MediaSource.FromMedia(Fake.Info(60));
        var clip = Clip.FromSource(source, new MeowsCut.Core.Media.TimeRange(TimeSpan.Zero, Fake.S(5)));

        return new Sequence(
            new VideoTrack([clip, clip with { Id = ClipId.New() }]),
            SequenceFormat.FromMedia(source.Info));
    }

    [Fact]
    public void A_gap_makes_the_sequence_longer()
    {
        var sequence = TwoClips();
        var second = sequence.Video.Clips[1];

        var moved = sequence.WithTrack(sequence.Video.MoveInTime(second.Id, Fake.S(8)));

        moved.Duration.Should().Be(Fake.S(13), "три секунды пустоты плюс два клипа по пять");
        moved.HasGaps.Should().BeTrue();
    }

    [Fact]
    public void Nothing_plays_inside_a_gap()
    {
        var sequence = TwoClips();
        var second = sequence.Video.Clips[1];
        var moved = sequence.WithTrack(sequence.Video.MoveInTime(second.Id, Fake.S(8)));

        moved.ClipAt(Fake.S(6)).Should().BeNull("в зазоре чёрный кадр, а не соседний клип");
        moved.ClipAt(Fake.S(9)).Should().NotBeNull();
    }

    private static Sequence ThreeClips()
    {
        var source = MediaSource.FromMedia(Fake.Info(60));
        var clip = Clip.FromSource(source, new MeowsCut.Core.Media.TimeRange(TimeSpan.Zero, Fake.S(5)));

        return new Sequence(
            new VideoTrack([clip, clip with { Id = ClipId.New() }, clip with { Id = ClipId.New() }]),
            SequenceFormat.FromMedia(source.Info));
    }

    [Fact]
    public void A_clip_first_eats_the_free_space_next_to_it()
    {
        var sequence = ThreeClips();

        // Отодвигаем третий клип, освобождая четыре секунды, и двигаем в них средний.
        var withRoom = sequence.WithTrack(sequence.Video.MoveInTime(sequence.Video.Clips[2].Id, Fake.S(14)));
        var moved = withRoom.WithTrack(withRoom.Video.MoveInTime(withRoom.Video.Clips[1].Id, Fake.S(7)));

        moved.Video.StartOf(1).Should().Be(Fake.S(7));
        moved.Video.StartOf(2).Should().Be(Fake.S(14), "свободного места хватило, соседа трогать незачем");
    }

    [Fact]
    public void Without_free_space_the_neighbours_are_pushed_along()
    {
        var sequence = ThreeClips();

        // Справа занято вплотную: третий клип обязан уехать вперёд, а не пропасть.
        var moved = sequence.WithTrack(sequence.Video.MoveInTime(sequence.Video.Clips[1].Id, Fake.S(7)));

        moved.Video.StartOf(1).Should().Be(Fake.S(7));
        moved.Video.StartOf(2).Should().Be(Fake.S(12));
    }

    [Fact]
    public void A_clip_cannot_overlap_its_neighbours()
    {
        var sequence = TwoClips();
        var second = sequence.Video.Clips[1];

        // Просим встать на первую секунду — там занято первым клипом.
        var moved = sequence.WithTrack(sequence.Video.MoveInTime(second.Id, Fake.S(1)));

        moved.Video.StartOf(1).Should().Be(Fake.S(5));
    }

    [Fact]
    public void Reordering_drops_the_gap_of_the_moved_clip()
    {
        var sequence = TwoClips();
        var second = sequence.Video.Clips[1];

        var withGap = sequence.WithTrack(sequence.Video.MoveInTime(second.Id, Fake.S(9)));
        var reordered = new MoveClipCommand(second.Id, 0).Apply(withGap);

        reordered.Video.Clips[0].Id.Should().Be(second.Id);
        reordered.Video.StartOf(0).Should().Be(TimeSpan.Zero, "перед первым клипом пустоте взяться неоткуда");
    }

    [Fact]
    public void Splitting_keeps_the_gap_on_the_left_half()
    {
        var sequence = TwoClips();
        var second = sequence.Video.Clips[1];
        var withGap = sequence.WithTrack(sequence.Video.MoveInTime(second.Id, Fake.S(8)));

        var split = withGap.SplitAt(Fake.S(10));

        // Половинки лежат встык, а зазор перед ними остаётся прежним.
        split.Video.StartOf(1).Should().Be(Fake.S(8));
        split.Video.StartOf(2).Should().Be(Fake.S(10));
        split.Duration.Should().Be(withGap.Duration);
    }

    [Fact]
    public void A_slice_keeps_the_gap_inside_it()
    {
        var sequence = TwoClips();
        var second = sequence.Video.Clips[1];
        var withGap = sequence.WithTrack(sequence.Video.MoveInTime(second.Id, Fake.S(8)));

        var slice = withGap.Slice(new MeowsCut.Core.Media.TimeRange(Fake.S(2), Fake.S(11)));

        // Три секунды первого клипа, три секунды пустоты, три секунды второго.
        slice.Video.StartOf(0).Should().Be(TimeSpan.Zero);
        slice.Video.StartOf(1).Should().Be(Fake.S(6));
        slice.Duration.Should().Be(Fake.S(9));
    }
}
