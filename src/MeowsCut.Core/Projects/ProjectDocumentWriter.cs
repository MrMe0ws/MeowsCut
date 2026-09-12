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
            FrameRateDenominator = project.Sequence.Format.FrameRate.Denominator,
            Custom = project.Sequence.Format.IsCustom,
            Fit = (int)project.Sequence.Format.Fit
        },

        Clips = project.Sequence.Video.Clips.Select(Write).ToArray(),
        AudioTracks = project.Sequence.AudioTracks.Select(Write).ToArray(),
        Titles = project.Sequence.Titles.Select(Write).ToArray()
    };

    private static ProjectTitleDocument Write(TitleClip title) => new()
    {
        Text = title.Text,
        StartMs = title.TimelineStart.TotalMilliseconds,
        DurationMs = title.Duration.TotalMilliseconds,
        Scale = title.Scale,
        Anchor = (int)title.Anchor,
        Color = title.Color,
        Backdrop = title.Backdrop,
        MarginShare = title.Margin
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
        Rotation = clip.Transform.Rotation,
        FadeInMs = clip.FadeIn.TotalMilliseconds,
        FadeOutMs = clip.FadeOut.TotalMilliseconds,
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
