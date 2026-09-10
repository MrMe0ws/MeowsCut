using MeowsCut.Core.Media;

namespace MeowsCut.Core.Editing.Timeline;

/// <summary>
/// Идентификатор звукового клипа. Отдельный от <see cref="ClipId"/>: клипы живут
/// на разных дорожках, и перепутать их при удалении было бы дорого.
/// </summary>
public readonly record struct AudioClipId(Guid Value)
{
    public static AudioClipId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N")[..8];
}

/// <summary>
/// Кусок звука на аудиодорожке.
/// </summary>
/// <remarks>
/// В отличие от видеоклипа, положение хранится явно: звук кладут в конкретное
/// место — реплику под кадр, удар под монтажную склейку. Видеоклип же знает лишь
/// свой отступ от предыдущего, потому что его место задаёт в первую очередь порядок.
/// </remarks>
public sealed record AudioClip(
    AudioClipId Id,
    SourceId SourceId,
    TimeRange SourceRange,
    TimeSpan TimelineStart)
{
    /// <summary>Короче этого кусок звука не имеет смысла — это щелчок.</summary>
    public static readonly TimeSpan MinDuration = TimeSpan.FromMilliseconds(20);

    public const double MinGain = 0d;
    public const double MaxGain = 4d;

    /// <summary>Предел сдвига тональности в полутонах: дальше речь перестаёт быть речью.</summary>
    public const int MaxPitchSemitones = 12;

    /// <summary>Длительность источника, нужна для ограничения обрезки справа.</summary>
    public TimeSpan SourceDuration { get; init; } = TimeSpan.Zero;

    /// <summary>Громкость куска. 1 — как в источнике.</summary>
    public double Gain { get; init; } = 1d;

    /// <summary>Сдвиг тональности в полутонах, без изменения длительности.</summary>
    public int PitchSemitones { get; init; }

    /// <summary>
    /// Скорость воспроизведения. Нужна, когда звук отделяют от ускоренного клипа:
    /// без неё отделённая дорожка немедленно уехала бы относительно картинки.
    /// </summary>
    public double Speed { get; init; } = 1d;

    public TimeSpan FadeIn { get; init; }

    public TimeSpan FadeOut { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>Длительность на таймлайне: кусок источника, растянутый скоростью.</summary>
    public TimeSpan Duration => Speed > 0 ? SourceRange.Duration / Speed : SourceRange.Duration;

    public bool IsSpeedChanged => Math.Abs(Speed - 1d) > 0.0001;

    public TimeSpan TimelineEnd => TimelineStart + Duration;

    public TimeRange TimelineRange => new(TimelineStart, TimelineEnd);

    public bool IsPitchShifted => PitchSemitones != 0;

    public bool IsGainChanged => Math.Abs(Gain - 1d) > 0.0001;

    public static AudioClip FromSource(MediaSource source, TimeSpan timelineStart) =>
        new(AudioClipId.New(), source.Id, new TimeRange(TimeSpan.Zero, source.Duration), timelineStart)
        {
            SourceDuration = source.Duration,
            Title = Path.GetFileNameWithoutExtension(source.FilePath)
        };

    /// <summary>
    /// Время внутри исходного файла, соответствующее точке таймлайна.
    /// Нужно предпросмотру: он крутит сам файл и должен попасть в нужное место.
    /// </summary>
    public TimeSpan SourceTimeAt(TimeSpan timelinePosition)
    {
        var offset = timelinePosition - TimelineStart;

        if (offset < TimeSpan.Zero)
        {
            offset = TimeSpan.Zero;
        }
        else if (offset > Duration)
        {
            offset = Duration;
        }

        return SourceRange.Start + ToSource(offset);
    }

    public AudioClip MoveTo(TimeSpan start) =>
        this with { TimelineStart = start < TimeSpan.Zero ? TimeSpan.Zero : start };

    public AudioClip WithGain(double gain) =>
        this with { Gain = Math.Clamp(gain, MinGain, MaxGain) };

    public AudioClip WithPitch(int semitones) =>
        this with { PitchSemitones = Math.Clamp(semitones, -MaxPitchSemitones, MaxPitchSemitones) };

    public AudioClip WithFades(TimeSpan fadeIn, TimeSpan fadeOut)
    {
        var limit = Duration;
        var head = Clamp(fadeIn, limit);
        var tail = Clamp(fadeOut, limit - head);

        return this with { FadeIn = head, FadeOut = tail };
    }

    /// <summary>
    /// Двигает левый край. Кусок остаётся на месте на таймлайне только в том смысле,
    /// что съеденное начало не сдвигает звук: точка входа в источнике и позиция
    /// уезжают вместе, иначе звук поплыл бы относительно картинки.
    /// </summary>
    public AudioClip TrimStart(TimeSpan delta)
    {
        var start = SourceRange.Start + ToSource(delta);

        if (start < TimeSpan.Zero)
        {
            delta -= ToTimeline(start);
            start = TimeSpan.Zero;
        }

        if (ToTimeline(SourceRange.End - start) < MinDuration)
        {
            return this;
        }

        return this with
        {
            SourceRange = SourceRange with { Start = start },
            TimelineStart = TimelineStart + delta
        };
    }

    public AudioClip TrimEnd(TimeSpan delta)
    {
        var end = SourceRange.End + ToSource(delta);
        var limit = SourceDuration > TimeSpan.Zero ? SourceDuration : SourceRange.End;

        if (end > limit)
        {
            end = limit;
        }

        return ToTimeline(end - SourceRange.Start) < MinDuration
            ? this
            : this with { SourceRange = SourceRange with { End = end } };
    }

    public bool CanSplitAt(TimeSpan offsetFromStart) =>
        offsetFromStart >= MinDuration && Duration - offsetFromStart >= MinDuration;

    private TimeSpan ToSource(TimeSpan timelineDelta) =>
        IsSpeedChanged ? timelineDelta * Speed : timelineDelta;

    private TimeSpan ToTimeline(TimeSpan sourceDelta) =>
        IsSpeedChanged && Speed > 0 ? sourceDelta / Speed : sourceDelta;

    /// <summary>Разрез: затухания достаются краям, к которым они относились.</summary>
    public (AudioClip Left, AudioClip Right) SplitAt(TimeSpan offsetFromStart)
    {
        var cut = SourceRange.Start + ToSource(offsetFromStart);

        var left = this with
        {
            SourceRange = SourceRange with { End = cut },
            FadeOut = TimeSpan.Zero
        };

        var right = this with
        {
            Id = AudioClipId.New(),
            SourceRange = SourceRange with { Start = cut },
            TimelineStart = TimelineStart + offsetFromStart,
            FadeIn = TimeSpan.Zero
        };

        return (left, right);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan limit)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value > limit ? (limit > TimeSpan.Zero ? limit : TimeSpan.Zero) : value;
    }
}
