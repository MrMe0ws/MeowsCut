using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;
using MeowsCut.Core.Projects;
using MeowsCut.Core.Tests.Editing;

namespace MeowsCut.Core.Tests.Projects;

/// <summary>
/// Черновик проекта: что записали — то и открылось.
/// </summary>
public class ProjectDocumentTests
{
    private static (Project Project, Dictionary<Guid, MediaInfo> Media) Build()
    {
        var videoInfo = Fake.Info(30);
        var musicInfo = Fake.Info(20, path: @"C:\звук\музыка.m4a");

        var project = Project.FromMedia(videoInfo);
        var (withMusic, music) = project.WithSource(musicInfo);

        var clip = withMusic.Sequence.Video.Clips[0]
            .WithSpeed(1.5)
            .WithAudio(new ClipAudio(true, 0.4));

        var track = AudioTrack.Empty("Музыка") with
        {
            Gain = 0.8,
            Clips = [AudioClip.FromSource(music, Fake.S(2)) with { Gain = 0.5, PitchSemitones = 3, Speed = 2d }]
        };

        var sequence = withMusic.Sequence
            .WithTrack(withMusic.Sequence.Video.Replace(clip))
            .WithTracks([track]);

        var media = withMusic.Sources.ToDictionary(source => source.Id.Value, source => source.Info);

        return (withMusic.WithSequence(sequence), media);
    }

    private static Project RoundTrip(Project project, Dictionary<Guid, MediaInfo> media)
    {
        var (restored, _) = ProjectDocumentReader.Restore(ProjectDocumentWriter.Create(project), media);
        return restored;
    }

    [Fact]
    public void The_cut_survives_saving_and_opening()
    {
        var (project, media) = Build();

        var restored = RoundTrip(project, media);

        restored.Sources.Should().HaveCount(2);
        restored.Sequence.Video.Clips.Should().HaveCount(1);

        var clip = restored.Sequence.Video.Clips[0];
        clip.Speed.Should().BeApproximately(1.5, 0.0001);
        clip.Audio.Volume.Should().BeApproximately(0.4, 0.0001);
        clip.SourceRange.Should().Be(project.Sequence.Video.Clips[0].SourceRange);
    }

    [Fact]
    public void Audio_tracks_keep_their_settings()
    {
        var (project, media) = Build();

        var track = RoundTrip(project, media).Sequence.AudioTracks.Should().ContainSingle().Subject;

        track.Title.Should().Be("Музыка");
        track.Gain.Should().BeApproximately(0.8, 0.0001);

        var clip = track.Clips.Should().ContainSingle().Subject;
        clip.Gain.Should().BeApproximately(0.5, 0.0001);
        clip.PitchSemitones.Should().Be(3);
        clip.Speed.Should().BeApproximately(2d, 0.0001);
        clip.TimelineStart.Should().Be(Fake.S(2));
    }

    [Fact]
    public void Format_of_the_sequence_is_kept()
    {
        var (project, media) = Build();

        RoundTrip(project, media).Sequence.Format.Should().Be(project.Sequence.Format);
    }

    [Fact]
    public void A_missing_file_takes_its_clips_with_it_but_keeps_the_rest()
    {
        // Исходник могли удалить или перенести: остальной монтаж от этого пропадать не должен.
        var (project, media) = Build();
        var videoId = project.Sequence.Video.Clips[0].SourceId.Value;

        media.Remove(videoId);

        var (restored, _) = ProjectDocumentReader.Restore(ProjectDocumentWriter.Create(project), media);

        restored.Sequence.Video.Clips.Should().BeEmpty();
        restored.Sequence.AudioTracks.Should().ContainSingle();
    }

    [Fact]
    public void A_shortened_source_trims_the_clip_instead_of_breaking_it()
    {
        // Файл перезаписали более коротким: кусок «с 0 по 30» обязан подрезаться,
        // иначе проект открылся бы с обрезкой за концом файла.
        var (project, media) = Build();
        var clip = project.Sequence.Video.Clips[0];

        media[clip.SourceId.Value] = Fake.Info(10);

        var (restored, warnings) = ProjectDocumentReader.Restore(ProjectDocumentWriter.Create(project), media);

        restored.Sequence.Video.Clips[0].SourceRange.End.Should().Be(Fake.S(10));
        warnings.Should().BeEmpty("кусок подрезан, а не потерян");
    }

    [Fact]
    public void A_clip_that_no_longer_fits_is_reported()
    {
        var (project, media) = Build();
        var clip = project.Sequence.Video.Clips[0];

        var moved = clip with { SourceRange = new TimeRange(Fake.S(20), Fake.S(30)) };
        var sequence = project.Sequence.WithTrack(project.Sequence.Video.Replace(moved));

        // Источник стал короче начала куска — спасать нечего
        media[clip.SourceId.Value] = Fake.Info(5);

        var (restored, warnings) = ProjectDocumentReader.Restore(
            ProjectDocumentWriter.Create(project.WithSequence(sequence)), media);

        restored.Sequence.Video.Clips.Should().BeEmpty();
        warnings.Should().ContainSingle().Which.Should().Contain("не поместился");
    }
}
