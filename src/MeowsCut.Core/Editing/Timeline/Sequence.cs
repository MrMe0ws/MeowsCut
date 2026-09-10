using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Editing.Timeline;

/// <summary>
/// Формат последовательности: в нём считается предпросмотр и от него отталкивается
/// экспорт при выборе «как в исходнике». Задаётся первым добавленным клипом.
/// </summary>
public sealed record SequenceFormat(FrameSize Size, Rational FrameRate)
{
    public static readonly SequenceFormat Default = new(new FrameSize(1920, 1080), new Rational(30, 1));

    public static SequenceFormat FromMedia(MediaInfo info)
    {
        var video = info.PrimaryVideo;
        if (video is null)
        {
            return Default;
        }

        var frameRate = video.FrameRate.IsZero ? video.AverageFrameRate : video.FrameRate;

        return new SequenceFormat(
            video.DisplaySize.IsEmpty ? Default.Size : video.DisplaySize,
            frameRate.IsZero ? Default.FrameRate : frameRate);
    }
}

/// <summary>Клип вместе с его положением на таймлайне.</summary>
public readonly record struct PlacedClip(Clip Clip, int Index, TimeSpan Start)
{
    public TimeSpan End => Start + Clip.TimelineDuration;

    public TimeRange TimelineRange => new(Start, End);
}

/// <summary>
/// Последовательность — то, что видит плеер и что рендерит экспорт.
/// Иммутабельна: любое изменение создаёт новую и проходит через историю правок.
/// </summary>
public sealed record Sequence(VideoTrack Video, SequenceFormat Format)
{
    public static Sequence Empty { get; } = new(VideoTrack.Empty, SequenceFormat.Default);

    public static Sequence FromSource(MediaSource source) =>
        new(new VideoTrack([Clip.FromSource(source)]), SequenceFormat.FromMedia(source.Info));

    public TimeSpan Duration => Video.Duration;

    public bool IsEmpty => Video.IsEmpty;

    public int ClipCount => Video.Count;

    public bool HasAudio => Video.Clips.Any(clip => clip.HasAudio);

    /// <summary>Клипы вместе с их временем начала — основа отрисовки и планирования экспорта.</summary>
    public IEnumerable<PlacedClip> EnumeratePlaced()
    {
        var start = TimeSpan.Zero;
        for (var i = 0; i < Video.Count; i++)
        {
            var clip = Video.Clips[i];
            yield return new PlacedClip(clip, i, start);
            start += clip.TimelineDuration;
        }
    }

    public TimeSpan StartOf(ClipId id)
    {
        var index = Video.IndexOf(id);
        if (index < 0)
        {
            throw new EditOperationException($"Клип {id} не найден на дорожке.");
        }

        return Video.StartOf(index);
    }

    /// <summary>
    /// Что играет в этот момент таймлайна. Используется и плеером, и скраббингом:
    /// граница клипа принадлежит следующему клипу, конец последовательности — никому.
    /// </summary>
    public PlacedClip? ClipAt(TimeSpan timelineTime)
    {
        if (timelineTime < TimeSpan.Zero)
        {
            return null;
        }

        foreach (var placed in EnumeratePlaced())
        {
            if (timelineTime < placed.End)
            {
                return placed;
            }
        }

        return null;
    }

    /// <summary>Сопоставляет время таймлайна с временем внутри исходного файла.</summary>
    public (Clip Clip, TimeSpan SourceTime)? Resolve(TimeSpan timelineTime)
    {
        var placed = ClipAt(timelineTime);
        if (placed is null)
        {
            return null;
        }

        var offset = timelineTime - placed.Value.Start;
        return (placed.Value.Clip, placed.Value.Clip.ToSourceTime(offset));
    }

    public Sequence WithTrack(VideoTrack track) => this with { Video = track };

    /// <summary>
    /// Кусок последовательности за указанный отрезок времени.
    /// </summary>
    /// <remarks>
    /// Нужен там, где надо отрендерить не всё: предпросмотр фрагмента и ограничение
    /// длительности в пресетах (три секунды для стикера). Клипы на границах обрезаются,
    /// остальные выбрасываются, порядок сохраняется.
    /// </remarks>
    public Sequence Slice(TimeRange range)
    {
        var clamped = range.Clamp(Duration);
        if (clamped.Duration <= TimeSpan.Zero)
        {
            return WithTrack(VideoTrack.Empty);
        }

        var clips = new List<Clip>();

        foreach (var placed in EnumeratePlaced())
        {
            if (placed.End <= clamped.Start || placed.Start >= clamped.End)
            {
                continue;
            }

            var clip = placed.Clip;

            // Обрезаем края только у тех клипов, которые вылезают за отрезок.
            if (placed.Start < clamped.Start)
            {
                clip = clip.TrimStart(clamped.Start - placed.Start);
            }

            if (placed.End > clamped.End)
            {
                // Дельта считается от исходного конца клипа на таймлайне: обрезка
                // начала не сдвигает его точку выхода в исходнике.
                clip = clip.TrimEnd(clamped.End - placed.End);
            }

            if (clip.SourceRange.Duration >= Clip.MinSourceDuration)
            {
                clips.Add(clip with { Id = ClipId.New() });
            }
        }

        return WithTrack(new VideoTrack(clips));
    }

    /// <summary>
    /// Разрезает клип, накрывающий указанное время. Если время попадает на стык
    /// или за пределы последовательности, ничего не меняется — это не ошибка.
    /// </summary>
    public Sequence SplitAt(TimeSpan timelineTime)
    {
        var placed = ClipAt(timelineTime);
        if (placed is null)
        {
            return this;
        }

        var offset = timelineTime - placed.Value.Start;
        if (!placed.Value.Clip.CanSplitAt(offset))
        {
            return this;
        }

        var (left, right) = placed.Value.Clip.SplitAt(offset);
        return WithTrack(Video.ReplaceAt(placed.Value.Index, left, right));
    }
}
