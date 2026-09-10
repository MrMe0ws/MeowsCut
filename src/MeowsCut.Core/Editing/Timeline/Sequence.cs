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

    /// <summary>
    /// Отдельные звуковые дорожки поверх звука видеоряда.
    /// </summary>
    /// <remarks>
    /// Свойство, а не позиционный параметр: последовательность создаётся в десятке мест,
    /// и звук там ни при чём — пусть по умолчанию его просто нет.
    /// </remarks>
    public IReadOnlyList<AudioTrack> AudioTracks { get; init; } = [];

    /// <summary>Длительность видеоряда. Звук за его пределами при экспорте отсекается.</summary>
    public TimeSpan Duration => Video.Duration;

    public bool IsEmpty => Video.IsEmpty;

    public int ClipCount => Video.Count;

    /// <summary>
    /// Есть ли на видеоряде пустые места. Быстрые стратегии экспорта их не умеют:
    /// копирование потоков отдало бы склейку без чёрных вставок, будто зазора и не было.
    /// </summary>
    public bool HasGaps => Video.HasGaps;

    /// <summary>
    /// Есть ли на видеоряде фотографии. Быстрые стратегии их не умеют: у картинки
    /// нет потока, который можно скопировать, — кадры для неё надо создать.
    /// </summary>
    public bool HasImages
    {
        get
        {
            foreach (var clip in Video.Clips)
            {
                if (clip.SourceIsImage)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Есть ли звук у самого видеоряда (без отдельных дорожек).</summary>
    public bool HasAudio => Video.Clips.Any(clip => clip.HasAudio);

    /// <summary>Звучащие отдельные дорожки — только их имеет смысл подавать в микшер.</summary>
    public IEnumerable<AudioTrack> AudibleTracks => AudioTracks.Where(track => track.IsAudible);

    public bool HasAudioTracks => AudibleTracks.Any();

    /// <summary>Есть ли звук вообще — из видеоряда или с отдельной дорожки.</summary>
    public bool HasAnyAudio => HasAudio || HasAudioTracks;

    public AudioTrack? FindTrack(AudioTrackId id) =>
        AudioTracks.FirstOrDefault(track => track.Id == id);

    public AudioTrack RequireTrack(AudioTrackId id) =>
        FindTrack(id) ?? throw new EditOperationException($"Аудиодорожка {id} не найдена.");

    public Sequence WithTracks(IReadOnlyList<AudioTrack> tracks) => this with { AudioTracks = tracks };

    /// <summary>Заменяет одну дорожку, остальные оставляет как есть.</summary>
    public Sequence WithTrack(AudioTrack track)
    {
        var tracks = AudioTracks.ToArray();
        var index = Array.FindIndex(tracks, item => item.Id == track.Id);

        if (index < 0)
        {
            throw new EditOperationException($"Аудиодорожка {track.Id} не найдена.");
        }

        tracks[index] = track;
        return this with { AudioTracks = tracks };
    }

    /// <summary>Клипы вместе с их временем начала — основа отрисовки и планирования экспорта.</summary>
    public IEnumerable<PlacedClip> EnumeratePlaced()
    {
        var start = TimeSpan.Zero;
        for (var i = 0; i < Video.Count; i++)
        {
            var clip = Video.Clips[i];

            // Отступ идёт перед клипом: пустое место принадлежит зазору, а не кадру.
            start += clip.LeadingGap;
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
            // Попали в зазор перед клипом: там пусто, и это не ошибка. Клипы идут
            // по возрастанию, поэтому дальше искать нечего.
            if (timelineTime < placed.Start)
            {
                return null;
            }

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

        // Курсор — конец последнего попавшего в отрезок клипа уже в новых координатах.
        // Через него пересчитываются отступы: зазор между клипами обязан пережить
        // нарезку, иначе проверка фрагмента показывала бы не тот монтаж.
        var cursor = TimeSpan.Zero;

        foreach (var placed in EnumeratePlaced())
        {
            if (placed.End <= clamped.Start || placed.Start >= clamped.End)
            {
                continue;
            }

            var clip = placed.Clip;
            var start = placed.Start;

            // Обрезаем края только у тех клипов, которые вылезают за отрезок.
            if (placed.Start < clamped.Start)
            {
                clip = clip.TrimStart(clamped.Start - placed.Start);
                start = clamped.Start;
            }

            if (placed.End > clamped.End)
            {
                // Дельта считается от исходного конца клипа на таймлайне: обрезка
                // начала не сдвигает его точку выхода в исходнике.
                clip = clip.TrimEnd(clamped.End - placed.End);
            }

            if (clip.SourceRange.Duration < Clip.MinSourceDuration)
            {
                continue;
            }

            var gap = start - clamped.Start - cursor;

            clips.Add(clip with { Id = ClipId.New(), LeadingGap = gap < TimeSpan.Zero ? TimeSpan.Zero : gap });
            cursor = start - clamped.Start + clip.TimelineDuration;
        }

        return WithTrack(new VideoTrack(clips)) with { AudioTracks = SliceAudio(clamped) };
    }

    /// <summary>
    /// Звук того же отрезка, сдвинутый к нулю. Без этого проверка фрагмента и лимит
    /// длительности в пресетах отдавали бы звук, не совпадающий с картинкой.
    /// </summary>
    private IReadOnlyList<AudioTrack> SliceAudio(TimeRange range)
    {
        var tracks = new List<AudioTrack>();

        foreach (var track in AudioTracks)
        {
            var clips = new List<AudioClip>();

            foreach (var clip in track.Clips)
            {
                if (clip.TimelineEnd <= range.Start || clip.TimelineStart >= range.End)
                {
                    continue;
                }

                var trimmed = clip;

                if (clip.TimelineStart < range.Start)
                {
                    trimmed = trimmed.TrimStart(range.Start - clip.TimelineStart);
                }

                if (clip.TimelineEnd > range.End)
                {
                    trimmed = trimmed.TrimEnd(range.End - clip.TimelineEnd);
                }

                if (trimmed.Duration < AudioClip.MinDuration)
                {
                    continue;
                }

                clips.Add(trimmed with
                {
                    Id = AudioClipId.New(),
                    TimelineStart = trimmed.TimelineStart - range.Start
                });
            }

            if (clips.Count > 0)
            {
                tracks.Add(track with { Clips = clips });
            }
        }

        return tracks;
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
