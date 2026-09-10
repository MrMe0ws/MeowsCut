using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Звук, который длиннее видеоряда, и правки сразу у нескольких клипов.
/// </summary>
public class AudioTailTests
{
    private static Sequence WithSound(double videoSeconds, double soundStart, double soundSeconds)
    {
        var source = MediaSource.FromMedia(Fake.Info(60));

        var video = new Sequence(
            new VideoTrack([Clip.FromSource(source, new Core.Media.TimeRange(TimeSpan.Zero, Fake.S(videoSeconds)))]),
            SequenceFormat.FromMedia(source.Info));

        var music = MediaSource.FromMedia(Fake.Info(soundSeconds, path: @"C:\звук\музыка.m4a"));

        var clip = AudioClip.FromSource(music, Fake.S(soundStart));
        return video.WithTracks([AudioTrack.Empty("Музыка") with { Clips = [clip] }]);
    }

    [Fact]
    public void Sound_beyond_the_last_frame_makes_the_film_longer()
    {
        var sequence = WithSound(videoSeconds: 5, soundStart: 0, soundSeconds: 9);

        sequence.Duration.Should().Be(Fake.S(9), "под музыку досматривают чёрный экран");
        sequence.VideoTail.Should().Be(Fake.S(4));
        sequence.HasVideoTail.Should().BeTrue();
    }

    [Fact]
    public void Sound_shorter_than_the_video_changes_nothing()
    {
        var sequence = WithSound(videoSeconds: 10, soundStart: 0, soundSeconds: 4);

        sequence.Duration.Should().Be(Fake.S(10));
        sequence.HasVideoTail.Should().BeFalse();
    }

    [Fact]
    public void A_shifted_sound_counts_from_where_it_ends()
    {
        // Кусок в четыре секунды, положенный на восьмой, кончается на двенадцатой.
        var sequence = WithSound(videoSeconds: 10, soundStart: 8, soundSeconds: 4);

        sequence.Duration.Should().Be(Fake.S(12));
        sequence.VideoTail.Should().Be(Fake.S(2));
    }

    [Fact]
    public void Nothing_plays_in_the_tail()
    {
        var sequence = WithSound(videoSeconds: 5, soundStart: 0, soundSeconds: 9);

        sequence.ClipAt(Fake.S(7)).Should().BeNull("в хвосте чёрный экран, а не последний кадр");
    }

    [Fact]
    public void Speed_applies_to_every_selected_clip()
    {
        var source = MediaSource.FromMedia(Fake.Info(60));
        var clip = Clip.FromSource(source, new Core.Media.TimeRange(TimeSpan.Zero, Fake.S(4)));

        var sequence = new Sequence(
            new VideoTrack([clip, clip with { Id = ClipId.New() }, clip with { Id = ClipId.New() }]),
            SequenceFormat.FromMedia(source.Info));

        var ids = sequence.Video.Clips.Take(2).Select(c => c.Id).ToArray();
        var changed = new SetClipSpeedCommand(ids, 2d).Apply(sequence);

        changed.Video.Clips[0].Speed.Should().Be(2d);
        changed.Video.Clips[1].Speed.Should().Be(2d);
        changed.Video.Clips[2].Speed.Should().Be(1d, "невыделенный клип трогать нельзя");
    }

    [Fact]
    public void Changing_several_clips_is_one_entry_in_the_history()
    {
        var source = MediaSource.FromMedia(Fake.Info(60));
        var clip = Clip.FromSource(source, new Core.Media.TimeRange(TimeSpan.Zero, Fake.S(4)));

        var sequence = new Sequence(
            new VideoTrack([clip, clip with { Id = ClipId.New() }]),
            SequenceFormat.FromMedia(source.Info));

        var history = new MeowsCut.Core.Editing.History.EditHistory(sequence);
        var ids = sequence.Video.Clips.Select(c => c.Id).ToArray();

        history.Execute(new SetClipSpeedCommand(ids, 2d));
        history.EndMergeGroup();
        history.Undo();

        history.Current.Video.Clips.Should().OnlyContain(c => Math.Abs(c.Speed - 1d) < 0.0001,
            "одно действие пользователя — одна отмена");
    }
}
