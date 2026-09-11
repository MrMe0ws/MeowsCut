using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Projects;

/// <summary>Перекладывает проект в то, что записывается в файл черновика.</summary>
public static class ProjectDocumentWriter
{
    public static ProjectDocument Create(Project project) => new()
    {
        Sources = project.Sources
            .Select(source => new ProjectSourceDocument { Id = source.Id.Value, Path = source.FilePath })
            .ToArray(),

        Format = new ProjectFormatDocument
        {
            Width = project.Sequence.Format.Size.Width,
            Height = project.Sequence.Format.Size.Height,
            FrameRateNumerator = project.Sequence.Format.FrameRate.Numerator,
            FrameRateDenominator = project.Sequence.Format.FrameRate.Denominator
        },

        Clips = project.Sequence.Video.Clips.Select(Write).ToArray(),
        AudioTracks = project.Sequence.AudioTracks.Select(Write).ToArray()
    };

    private static ProjectClipDocument Write(Clip clip) => new()
    {
        SourceId = clip.SourceId.Value,
        SourceStartMs = clip.SourceRange.Start.TotalMilliseconds,
        SourceEndMs = clip.SourceRange.End.TotalMilliseconds,
        LeadingGapMs = clip.LeadingGap.TotalMilliseconds,
        Speed = clip.Speed,
        AudioEnabled = clip.Audio.Enabled,
        Volume = clip.Audio.Volume,
        Zoom = clip.Transform.Zoom,
        OffsetX = clip.Transform.OffsetX,
        OffsetY = clip.Transform.OffsetY,
        Label = clip.Label
    };

    private static ProjectAudioTrackDocument Write(AudioTrack track) => new()
    {
        Title = track.Title,
        Muted = track.IsMuted,
        Gain = track.Gain,
        Clips = track.Clips.Select(Write).ToArray()
    };

    private static ProjectAudioClipDocument Write(AudioClip clip) => new()
    {
        SourceId = clip.SourceId.Value,
        SourceStartMs = clip.SourceRange.Start.TotalMilliseconds,
        SourceEndMs = clip.SourceRange.End.TotalMilliseconds,
        TimelineStartMs = clip.TimelineStart.TotalMilliseconds,
        Gain = clip.Gain,
        PitchSemitones = clip.PitchSemitones,
        Speed = clip.Speed,
        FadeInMs = clip.FadeIn.TotalMilliseconds,
        FadeOutMs = clip.FadeOut.TotalMilliseconds,
        Title = clip.Title
    };
}
