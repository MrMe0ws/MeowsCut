using FluentAssertions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Звук живёт по своим правилам: куски стоят там, куда их положили, между ними
/// нормальны паузы, и удаление соседа никого не двигает. Эти тесты и стерегут
/// разницу с видеорядом, где всё наоборот.
/// </summary>
public class AudioTrackTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static AudioClip Clip(double start, double from = 0, double to = 5) =>
        new(AudioClipId.New(), SourceId.New(), new TimeRange(S(from), S(to)), S(start))
        {
            SourceDuration = S(60)
        };

    [Fact]
    public void A_clip_keeps_the_place_it_was_put()
    {
        var clip = Clip(start: 3, from: 0, to: 2);

        clip.TimelineStart.Should().Be(S(3));
        clip.TimelineEnd.Should().Be(S(5));
        clip.Duration.Should().Be(S(2));
    }

    [Fact]
    public void Gaps_between_clips_are_silence_not_an_error()
    {
        var track = AudioTrack.Empty("Звуки") with { Clips = [Clip(0, 0, 1), Clip(5, 0, 1)] };

        track.ClipAt(S(0.5)).Should().NotBeNull();
        track.ClipAt(S(3)).Should().BeNull("между кусками просто тишина");
        track.Duration.Should().Be(S(6));
    }

    [Fact]
    public void Removing_a_clip_does_not_move_the_others()
    {
        var first = Clip(0, 0, 2);
        var second = Clip(10, 0, 2);
        var track = AudioTrack.Empty("Звуки") with { Clips = [first, second] };

        var result = track.Remove(first.Id);

        result.Clips.Single().TimelineStart.Should().Be(S(10),
            "реплика привязана к кадру, а не к соседнему звуку");
    }

    [Fact]
    public void Trimming_the_left_edge_does_not_slide_the_sound()
    {
        var clip = Clip(start: 4, from: 1, to: 6);

        var trimmed = clip.TrimStart(S(1));

        trimmed.SourceRange.Start.Should().Be(S(2));
        trimmed.TimelineStart.Should().Be(S(5), "иначе звук уехал бы относительно картинки");
        trimmed.TimelineEnd.Should().Be(clip.TimelineEnd, "правый край стоит на месте");
    }

    [Fact]
    public void Trimming_stops_at_the_end_of_the_source()
    {
        var clip = new AudioClip(AudioClipId.New(), SourceId.New(), new TimeRange(S(0), S(5)), S(0))
        {
            SourceDuration = S(5)
        };

        clip.TrimEnd(S(10)).SourceRange.End.Should().Be(S(5), "тянуть дальше файла нечего");
    }

    [Fact]
    public void Speed_stretches_the_clip_on_the_timeline()
    {
        var clip = Clip(0, 0, 4) with { Speed = 2d };

        clip.Duration.Should().Be(S(2));

        // Обрезка приходит во времени таймлайна, а источник живёт в своём.
        clip.TrimEnd(S(-1)).SourceRange.End.Should().Be(S(2));
    }

    [Fact]
    public void Splitting_gives_two_halves_that_touch()
    {
        var clip = Clip(start: 2, from: 0, to: 6);

        var (left, right) = clip.SplitAt(S(2));

        left.Duration.Should().Be(S(2));
        right.TimelineStart.Should().Be(S(4));
        right.TimelineEnd.Should().Be(clip.TimelineEnd);
        right.Id.Should().NotBe(left.Id, "иначе выделение и удаление перепутают половины");
    }

    [Fact]
    public void Fades_never_exceed_the_clip()
    {
        var clip = Clip(0, 0, 2).WithFades(S(5), S(5));

        (clip.FadeIn + clip.FadeOut).Should().BeLessThanOrEqualTo(clip.Duration);
    }

    [Fact]
    public void A_muted_track_is_not_audible()
    {
        var track = AudioTrack.Empty("Музыка") with { Clips = [Clip(0)], IsMuted = true };

        track.IsAudible.Should().BeFalse();
        new Sequence(VideoTrack.Empty, SequenceFormat.Default) { AudioTracks = [track] }
            .HasAudioTracks.Should().BeFalse();
    }
}

/// <summary>Команды правки звука — всё идёт через историю, как и монтаж видео.</summary>
public class AudioCommandTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static MediaSource Source(double seconds, bool hasAudio = true) =>
        MediaSource.FromMedia(Fake.Info(seconds, withAudio: hasAudio));

    private static (Sequence Sequence, AudioTrackId TrackId) WithTrack(double clipStart = 0)
    {
        var source = Source(20);
        var video = Sequence.FromSource(source);

        var track = AudioTrack.Empty("Музыка") with
        {
            Clips = [AudioClip.FromSource(Source(10), S(clipStart))]
        };

        return (video.WithTracks([track]), track.Id);
    }

    [Fact]
    public void Adding_a_track_leaves_the_video_alone()
    {
        var sequence = Sequence.FromSource(Source(20));

        var result = new AddAudioTrackCommand(AudioTrack.Empty("Музыка")).Apply(sequence);

        result.AudioTracks.Should().HaveCount(1);
        result.Video.Should().BeSameAs(sequence.Video);
    }

    [Fact]
    public void Moving_a_clip_puts_it_where_asked()
    {
        var (sequence, trackId) = WithTrack();
        var clipId = sequence.RequireTrack(trackId).Clips[0].Id;

        var result = new MoveAudioClipCommand(trackId, clipId, S(4)).Apply(sequence);

        result.RequireTrack(trackId).Clips[0].TimelineStart.Should().Be(S(4));
    }

    [Fact]
    public void Dragging_a_clip_is_a_single_undo_step()
    {
        var (sequence, trackId) = WithTrack();
        var clipId = sequence.RequireTrack(trackId).Clips[0].Id;

        var first = new MoveAudioClipCommand(trackId, clipId, S(1));
        var second = new MoveAudioClipCommand(trackId, clipId, S(2));

        second.TryMergeWith(first, out var merged).Should().BeTrue();
        merged.Apply(sequence).RequireTrack(trackId).Clips[0].TimelineStart.Should().Be(S(2));
    }

    [Fact]
    public void Pitch_and_gain_are_clamped_to_something_usable()
    {
        var (sequence, trackId) = WithTrack();
        var clipId = sequence.RequireTrack(trackId).Clips[0].Id;

        var result = new SetAudioClipPropertiesCommand(trackId, clipId, gain: 99, pitchSemitones: 50)
            .Apply(sequence);

        var clip = result.RequireTrack(trackId).Clips[0];
        clip.Gain.Should().Be(AudioClip.MaxGain);
        clip.PitchSemitones.Should().Be(AudioClip.MaxPitchSemitones);
    }

    [Fact]
    public void Detaching_audio_moves_it_onto_a_track_and_mutes_the_clip()
    {
        var sequence = Sequence.FromSource(Source(20));
        var clipId = sequence.Video.Clips[0].Id;

        var result = new DetachClipAudioCommand(clipId).Apply(sequence);

        result.Video.Clips[0].Audio.Enabled.Should().BeFalse("иначе звук зазвучал бы дважды");
        result.AudioTracks.Should().HaveCount(1);
        result.AudioTracks[0].Clips[0].TimelineStart.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Detaching_carries_the_speed_over()
    {
        var sequence = Sequence.FromSource(Source(20));
        var sped = sequence.WithTrack(sequence.Video.Replace(sequence.Video.Clips[0] with { Speed = 2d }));

        var result = new DetachClipAudioCommand(sped.Video.Clips[0].Id).Apply(sped);

        result.AudioTracks[0].Clips[0].Speed.Should().Be(2d,
            "иначе отделённый звук немедленно уехал бы относительно картинки");
    }

    [Fact]
    public void Detaching_a_silent_clip_is_refused()
    {
        var sequence = Sequence.FromSource(Source(20, hasAudio: false));

        var act = () => new DetachClipAudioCommand(sequence.Video.Clips[0].Id).Apply(sequence);

        act.Should().Throw<EditOperationException>();
    }

    [Fact]
    public void Slicing_a_fragment_takes_the_sound_with_it()
    {
        var (sequence, trackId) = WithTrack(clipStart: 2);

        var slice = sequence.Slice(new TimeRange(S(3), S(6)));

        slice.AudioTracks.Should().HaveCount(1);
        slice.AudioTracks[0].Clips[0].TimelineStart.Should().Be(TimeSpan.Zero,
            "фрагмент начинается с нуля, звук обязан поехать вместе с ним");
        slice.AudioTracks[0].Clips[0].Duration.Should().Be(S(3));
        slice.RequireTrack(trackId).Should().NotBeNull("дорожка сохраняет свою личность");
    }
}
