using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Projects;

/// <summary>Собирает проект обратно из черновика и заново прочитанных файлов.</summary>
/// <remarks>
/// Куски подрезаются под фактическую длительность источника: файл могли перезаписать
/// или сконвертировать, и кусок «с 10-й по 20-ю секунду» в укоротившемся файле
/// превратился бы в обрезку за концом. Совсем не влезшие куски выпадают —
/// про них сообщают отдельно, молча терять монтаж нельзя.
/// </remarks>
public static class ProjectDocumentReader
{
    public static (Project Project, IReadOnlyList<string> Warnings) Restore(
        ProjectDocument document,
        IReadOnlyDictionary<Guid, MediaInfo> media)
    {
        var warnings = new List<string>();
        var sources = new List<MediaSource>();
        var byId = new Dictionary<Guid, MediaSource>();

        foreach (var entry in document.Sources)
        {
            if (!media.TryGetValue(entry.Id, out var info))
            {
                continue;
            }

            var source = new MediaSource(new SourceId(entry.Id), info);
            sources.Add(source);
            byId[entry.Id] = source;
        }

        var clips = new List<Clip>();

        foreach (var entry in document.Clips)
        {
            if (!byId.TryGetValue(entry.SourceId, out var source))
            {
                continue;
            }

            if (TryRestore(entry, source) is { } clip)
            {
                clips.Add(clip);
            }
            else
            {
                warnings.Add($"Кусок файла «{source.DisplayName}» не поместился в изменившийся источник и пропущен");
            }
        }

        var tracks = new List<AudioTrack>();

        foreach (var entry in document.AudioTracks)
        {
            var audioClips = entry.Clips
                .Select(clip => byId.TryGetValue(clip.SourceId, out var source) ? TryRestore(clip, source) : null)
                .OfType<AudioClip>()
                .ToArray();

            tracks.Add(AudioTrack.Empty(entry.Title) with
            {
                Clips = audioClips,
                IsMuted = entry.Muted,
                Gain = entry.Gain
            });
        }

        var sequence = new Sequence(VideoTrack.Empty with { Clips = clips }, Format(document, clips, sources))
            .WithTracks(tracks)
            .WithTitles(document.Titles.Select(Restore).Where(title => !title.IsEmpty).ToArray());

        return (new Project(sources, sequence), warnings);
    }

    /// <summary>
    /// Надпись из черновика. Файлов она не касается, поэтому и потеряться,
    /// в отличие от клипа, не может — восстанавливается как есть.
    /// </summary>
    private static TitleClip Restore(ProjectTitleDocument entry) =>
        new(
            TitleId.New(),
            entry.Text,
            TimeSpan.FromMilliseconds(Math.Max(0, entry.StartMs)),
            TimeSpan.FromMilliseconds(Math.Max(TitleClip.MinDuration.TotalMilliseconds, entry.DurationMs)))
        {
            Scale = Math.Clamp(entry.Scale, TitleClip.MinScale, TitleClip.MaxScale),
            Anchor = Enum.IsDefined((TitleAnchor)entry.Anchor) ? (TitleAnchor)entry.Anchor : TitleAnchor.BottomCenter,
            Color = string.IsNullOrWhiteSpace(entry.Color) ? "#FFFFFF" : entry.Color,
            Backdrop = entry.Backdrop,
            Margin = Math.Clamp(entry.MarginShare, 0d, 0.4d)
        };

    private static Clip? TryRestore(ProjectClipDocument entry, MediaSource source)
    {
        var limit = source.IsImage ? MediaInfo.ImageSourceDuration : source.Duration;
        var range = Fit(entry.SourceStartMs, entry.SourceEndMs, limit, Clip.MinSourceDuration);

        if (range is not { } fitted)
        {
            return null;
        }

        return new Clip(
            ClipId.New(),
            source.Id,
            limit,
            source.HasAudio,
            fitted,
            Math.Clamp(entry.Speed, Clip.MinSpeed, Clip.MaxSpeed),
            new ClipAudio(entry.AudioEnabled, entry.Volume),
            new ClipTransform(entry.Zoom, entry.OffsetX, entry.OffsetY).WithRotation(entry.Rotation),
            entry.Label,
            TimeSpan.FromMilliseconds(Math.Max(0, entry.LeadingGapMs)),
            source.IsImage)
            .WithFades(
                TimeSpan.FromMilliseconds(Math.Max(0, entry.FadeInMs)),
                TimeSpan.FromMilliseconds(Math.Max(0, entry.FadeOutMs)));
    }

    private static AudioClip? TryRestore(ProjectAudioClipDocument entry, MediaSource source)
    {
        var range = Fit(entry.SourceStartMs, entry.SourceEndMs, source.Duration, AudioClip.MinDuration);

        if (range is not { } fitted)
        {
            return null;
        }

        return new AudioClip(
            AudioClipId.New(),
            source.Id,
            fitted,
            TimeSpan.FromMilliseconds(Math.Max(0, entry.TimelineStartMs)))
        {
            SourceDuration = source.Duration,
            Gain = entry.Gain,
            PitchSemitones = entry.PitchSemitones,
            Speed = Math.Clamp(entry.Speed, Clip.MinSpeed, Clip.MaxSpeed),
            FadeIn = TimeSpan.FromMilliseconds(Math.Max(0, entry.FadeInMs)),
            FadeOut = TimeSpan.FromMilliseconds(Math.Max(0, entry.FadeOutMs)),
            Title = entry.Title
        };
    }

    private static TimeRange? Fit(double startMs, double endMs, TimeSpan limit, TimeSpan minimum)
    {
        var start = TimeSpan.FromMilliseconds(Math.Max(0, startMs));
        var end = TimeSpan.FromMilliseconds(Math.Max(0, endMs));

        if (start > limit)
        {
            return null;
        }

        if (end > limit)
        {
            end = limit;
        }

        return end - start < minimum ? null : new TimeRange(start, end);
    }

    private static SequenceFormat Format(
        ProjectDocument document,
        IReadOnlyList<Clip> clips,
        IReadOnlyList<MediaSource> sources)
    {
        if (document.Format is { Width: > 0, Height: > 0, FrameRateNumerator: > 0 } saved)
        {
            return new SequenceFormat(
                new FrameSize(saved.Width, saved.Height),
                new Rational(saved.FrameRateNumerator, Math.Max(1, saved.FrameRateDenominator)))
            {
                IsCustom = saved.Custom,
                Fit = Enum.IsDefined((Export.FitMode)saved.Fit) ? (Export.FitMode)saved.Fit : Export.FitMode.Contain
            };
        }

        // Формата в файле нет — берём его у первого клипа, как при обычном открытии файла
        var first = clips.Count > 0
            ? sources.FirstOrDefault(source => source.Id == clips[0].SourceId)
            : null;

        return first is null ? SequenceFormat.Default : SequenceFormat.FromMedia(first.Info);
    }
}
