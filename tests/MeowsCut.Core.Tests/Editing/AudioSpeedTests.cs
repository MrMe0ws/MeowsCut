using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.History;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Скорость куска звука: музыку подгоняют под монтаж, а не наоборот.
/// </summary>
public class AudioSpeedTests
{
    private static (EditHistory History, AudioTrackId TrackId, AudioClipId ClipId) Create()
    {
        var video = MediaSource.FromMedia(Fake.Info(30));
        var music = MediaSource.FromMedia(Fake.Info(20, path: @"C:\звук\музыка.m4a"));

        var clip = AudioClip.FromSource(music, TimeSpan.Zero);
        var track = AudioTrack.Empty("Музыка") with { Clips = [clip] };

        return (new EditHistory(Sequence.FromSource(video).WithTracks([track])), track.Id, clip.Id);
    }

    private static AudioClip ClipOf(EditHistory history) =>
        history.Current.AudioTracks[0].Clips[0];

    [Fact]
    public void Faster_audio_takes_less_room_on_the_board()
    {
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, speed: 2d));

        ClipOf(history).Duration.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Slower_audio_stretches()
    {
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, speed: 0.5));

        ClipOf(history).Duration.Should().Be(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void Speed_stays_inside_the_limits()
    {
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, speed: 500d));

        ClipOf(history).Speed.Should().Be(Clip.MaxSpeed);
    }

    [Fact]
    public void Speeding_up_shortens_a_fade_that_no_longer_fits()
    {
        // Затухание в 8 секунд длиннее куска, ускоренного до пяти: без пересчёта
        // оно съело бы кусок целиком и звук пропал бы вовсе.
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, fadeOut: TimeSpan.FromSeconds(8)));
        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, speed: 4d));

        var clip = ClipOf(history);

        clip.Duration.Should().Be(TimeSpan.FromSeconds(5));
        clip.FadeOut.Should().BeLessThanOrEqualTo(clip.Duration);
    }

    [Fact]
    public void Speed_after_gain_keeps_the_gain()
    {
        // Склеенная команда применяется к состоянию до предыдущей правки,
        // поэтому скорость обязана нести с собой и уже выставленную громкость.
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, gain: 0.4));
        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, speed: 1.5));

        var clip = ClipOf(history);

        clip.Gain.Should().BeApproximately(0.4, 0.0001);
        clip.Speed.Should().BeApproximately(1.5, 0.0001);
    }

    [Fact]
    public void Undo_brings_the_original_speed_back()
    {
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, speed: 3d));
        history.Undo();

        ClipOf(history).Speed.Should().Be(1d);
    }
}
