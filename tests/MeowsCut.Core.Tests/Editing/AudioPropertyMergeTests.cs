using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.History;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Склейка соседних правок звука.
/// </summary>
/// <remarks>
/// Здесь стерегут ошибку, которая выглядела как «настройки звука вообще не
/// применяются»: склеенная команда применяется к состоянию до предыдущей правки,
/// и замена команды целиком стирала всё, чего новая не задаёт. Покрутив тональность
/// после громкости, пользователь видел, как громкость сама возвращается к 100%.
/// </remarks>
public class AudioPropertyMergeTests
{
    private static (EditHistory History, AudioTrackId TrackId, AudioClipId ClipId) Create()
    {
        var video = MediaSource.FromMedia(Fake.Info(30));
        var music = MediaSource.FromMedia(Fake.Info(20, path: @"C:\звук\музыка.m4a"));

        var clip = AudioClip.FromSource(music, TimeSpan.Zero);
        var track = AudioTrack.Empty("Музыка") with { Clips = [clip] };

        var sequence = Sequence.FromSource(video).WithTracks([track]);

        return (new EditHistory(sequence), track.Id, clip.Id);
    }

    private static AudioClip ClipOf(EditHistory history) =>
        history.Current.AudioTracks[0].Clips[0];

    [Fact]
    public void Pitch_after_gain_keeps_the_gain()
    {
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, gain: 0.4));
        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, pitchSemitones: 5));

        var clip = ClipOf(history);

        clip.Gain.Should().BeApproximately(0.4, 0.0001, "громкость не должна сама возвращаться к 100%");
        clip.PitchSemitones.Should().Be(5);
    }

    [Fact]
    public void Gain_after_pitch_keeps_the_pitch()
    {
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, pitchSemitones: -7));
        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, gain: 1.5));

        var clip = ClipOf(history);

        clip.PitchSemitones.Should().Be(-7);
        clip.Gain.Should().BeApproximately(1.5, 0.0001);
    }

    [Fact]
    public void Fades_survive_the_next_edit()
    {
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(
            trackId, clipId, fadeIn: TimeSpan.FromSeconds(2), fadeOut: TimeSpan.FromSeconds(1)));

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, gain: 0.8));

        var clip = ClipOf(history);

        clip.FadeIn.Should().Be(TimeSpan.FromSeconds(2));
        clip.FadeOut.Should().Be(TimeSpan.FromSeconds(1));
        clip.Gain.Should().BeApproximately(0.8, 0.0001);
    }

    [Fact]
    public void All_merged_edits_undo_as_one()
    {
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, gain: 0.4));
        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, pitchSemitones: 5));
        history.EndMergeGroup();

        history.Undo();

        var clip = ClipOf(history);

        clip.Gain.Should().BeApproximately(1d, 0.0001);
        clip.PitchSemitones.Should().Be(0);
    }

    [Fact]
    public void A_finished_edit_does_not_merge_into_the_next_one()
    {
        var (history, trackId, clipId) = Create();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, gain: 0.4));
        history.EndMergeGroup();

        history.Execute(new SetAudioClipPropertiesCommand(trackId, clipId, pitchSemitones: 5));
        history.EndMergeGroup();

        // Отменяется только тональность: громкость была отдельным действием.
        history.Undo();

        var clip = ClipOf(history);

        clip.PitchSemitones.Should().Be(0);
        clip.Gain.Should().BeApproximately(0.4, 0.0001);
    }

    [Fact]
    public void Track_volume_after_muting_keeps_the_silence()
    {
        var (history, trackId, _) = Create();

        history.Execute(new SetAudioTrackPropertiesCommand(trackId, muted: true));
        history.Execute(new SetAudioTrackPropertiesCommand(trackId, gain: 0.5));

        var track = history.Current.AudioTracks[0];

        track.IsMuted.Should().BeTrue();
        track.Gain.Should().BeApproximately(0.5, 0.0001);
    }
}
