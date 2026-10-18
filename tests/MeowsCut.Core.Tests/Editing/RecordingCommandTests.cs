using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.History;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;
using static MeowsCut.Core.Tests.Editing.Fake;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Дубль озвучки на доске: выравнивание по старту ролика и выбор дорожки.
/// </summary>
public class RecordingCommandTests
{
    private static readonly MediaSource Video = Source(30);

    private static MediaSource Take(double seconds) =>
        Source(seconds, path: $@"C:\записи\Озвучка {Guid.NewGuid():N}.wav");

    [Fact]
    public void Lead_in_is_trimmed_off_and_the_clip_starts_at_the_playhead()
    {
        // Микрофон включили за 0,4 с до того, как пошёл ролик
        var clip = RecordingTake.Create(Take(5), S(10), S(0.4))!;

        clip.TimelineStart.Should().Be(S(10));
        clip.SourceRange.Start.Should().Be(S(0.4), "лишнее начало файла не должно звучать");
        clip.SourceRange.End.Should().Be(S(5));
        clip.Duration.Should().Be(S(4.6));
    }

    [Fact]
    public void Trimmed_lead_in_can_be_pulled_back()
    {
        var clip = RecordingTake.Create(Take(5), S(10), S(0.4))!;

        var restored = clip.TrimStart(S(-0.4));

        restored.SourceRange.Start.Should().Be(TimeSpan.Zero, "обрезка, а не удаление: начало можно вернуть");
        restored.TimelineStart.Should().Be(S(9.6));
    }

    [Fact]
    public void Negative_lead_in_moves_the_clip_right()
    {
        var clip = RecordingTake.Create(Take(5), S(10), S(-0.2))!;

        clip.TimelineStart.Should().Be(S(10.2));
        clip.SourceRange.Start.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Nothing_left_after_lead_in_gives_no_clip()
    {
        RecordingTake.Create(Take(0.3), S(0), S(0.3)).Should().BeNull();
    }

    [Fact]
    public void First_take_creates_its_own_track()
    {
        var history = new EditHistory(Sequence.FromSource(Video));
        var clip = RecordingTake.Create(Take(5), S(2), S(0.1))!;

        history.Execute(new AddRecordingCommand(clip, preferredTrack: null, AudioTrack.Empty("Озвучка")));

        history.Current.AudioTracks.Should().ContainSingle()
            .Which.Clips.Should().ContainSingle(c => c.Id == clip.Id);
    }

    [Fact]
    public void Next_take_goes_to_the_same_track_when_there_is_room()
    {
        var history = new EditHistory(Sequence.FromSource(Video));
        var first = RecordingTake.Create(Take(5), S(0), S(0))!;
        var voice = AudioTrack.Empty("Озвучка");

        history.Execute(new AddRecordingCommand(first, null, voice));

        var second = RecordingTake.Create(Take(3), S(10), S(0))!;
        history.Execute(new AddRecordingCommand(second, voice.Id, AudioTrack.Empty("Озвучка 2")));

        history.Current.AudioTracks.Should().ContainSingle()
            .Which.Clips.Should().HaveCount(2);
    }

    [Fact]
    public void Overlapping_take_gets_a_new_track_instead_of_covering_the_old_one()
    {
        var history = new EditHistory(Sequence.FromSource(Video));
        var first = RecordingTake.Create(Take(5), S(0), S(0))!;
        var voice = AudioTrack.Empty("Озвучка");

        history.Execute(new AddRecordingCommand(first, null, voice));

        // Переписали тот же кусок
        var retake = RecordingTake.Create(Take(4), S(2), S(0))!;
        history.Execute(new AddRecordingCommand(retake, voice.Id, AudioTrack.Empty("Озвучка 2")));

        var tracks = history.Current.AudioTracks;
        tracks.Should().HaveCount(2);
        tracks[0].Clips.Should().ContainSingle(c => c.Id == first.Id, "прошлый дубль остаётся как был");
        tracks[1].Clips.Should().ContainSingle(c => c.Id == retake.Id);
    }

    [Fact]
    public void Undo_removes_the_take_together_with_its_new_track()
    {
        var history = new EditHistory(Sequence.FromSource(Video));
        var clip = RecordingTake.Create(Take(5), S(0), S(0))!;

        history.Execute(new AddRecordingCommand(clip, null, AudioTrack.Empty("Озвучка")));
        history.Undo();

        history.Current.AudioTracks.Should().BeEmpty();
    }

    [Fact]
    public void Touching_clips_leave_the_range_free()
    {
        var clip = RecordingTake.Create(Take(5), S(0), S(0))!;
        var track = AudioTrack.Empty("Озвучка").Add(clip);

        track.IsFree(new TimeRange(S(5), S(8))).Should().BeTrue("стык встык — не наложение");
        track.IsFree(new TimeRange(S(4.9), S(8))).Should().BeFalse();
    }
}
