using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Editing.Timeline;

/// <summary>
/// Единица монтажа: кусок источника со своими точками входа и выхода, скоростью,
/// звуком и трансформацией кадра.
/// </summary>
/// <remarks>
/// Клип намеренно самодостаточен: он знает длительность своего источника и наличие
/// в нём звука. Это позволяет проверять корректность обрезки, не таская за собой
/// список источников, и делает всю модель таймлайна чистой и тестируемой.
/// </remarks>
public sealed record Clip(
    ClipId Id,
    SourceId SourceId,
    TimeSpan SourceDuration,
    bool SourceHasAudio,
    TimeRange SourceRange,
    double Speed,
    ClipAudio Audio,
    ClipTransform Transform,
    string? Label = null,
    TimeSpan LeadingGap = default,
    bool SourceIsImage = false)
{
    /// <summary>Ниже этого предела клип теряет смысл: примерно один кадр.</summary>
    public static readonly TimeSpan MinSourceDuration = TimeSpan.FromMilliseconds(20);

    public const double MinSpeed = 0.1;
    public const double MaxSpeed = 16d;

    public static Clip FromSource(MediaSource source) =>
        FromSource(source, new TimeRange(
            TimeSpan.Zero,

            // Фотография не «длится»: её отрезок задаём сами, а растянуть его
            // пользователь сможет за край клипа, как у любого другого куска.
            source.IsImage ? Media.MediaInfo.DefaultImageClipDuration : source.Duration));

    public static Clip FromSource(MediaSource source, TimeRange range)
    {
        var clamped = range.Clamp(source.Duration);
        if (clamped.Duration < MinSourceDuration)
        {
            throw new EditOperationException(
                $"Слишком короткий фрагмент: {clamped.Duration.TotalMilliseconds:0} мс.");
        }

        return new Clip(
            ClipId.New(),
            source.Id,
            source.Duration,
            source.HasAudio,
            clamped,
            Speed: 1d,
            ClipAudio.Default,
            ClipTransform.Identity,
            SourceIsImage: source.IsImage);
    }

    /// <summary>Сколько места клип занимает на таймлайне с учётом скорости.</summary>
    public TimeSpan TimelineDuration => TimeSpan.FromTicks((long)(SourceRange.Duration.Ticks / Speed));

    /// <summary>
    /// Место, которое клип занимает вместе со своим отступом от предыдущего.
    /// </summary>
    /// <remarks>
    /// Отступ хранится у клипа, а не позиция: положение по-прежнему вычисляется из
    /// порядка, и вставка клипа в середину не требует пересчитывать все остальные.
    /// В зазоре при экспорте рисуется чёрный кадр с тишиной.
    /// </remarks>
    public TimeSpan TotalTimelineSpan => LeadingGap + TimelineDuration;

    public bool HasLeadingGap => LeadingGap > TimeSpan.Zero;

    /// <summary>Ставит отступ перед клипом. Отрицательный отступ невозможен: клипы не накладываются.</summary>
    public Clip WithLeadingGap(TimeSpan gap) =>
        this with { LeadingGap = gap < TimeSpan.Zero ? TimeSpan.Zero : gap };

    public bool HasAudio => SourceHasAudio && Audio.Enabled;

    public bool IsSpeedChanged => Math.Abs(Speed - 1d) > 0.0001;

    /// <summary>Занимает ли клип весь источник целиком — признак, что можно обойтись remux.</summary>
    public bool IsFullSource =>
        SourceRange.Start <= TimeSpan.Zero &&
        SourceRange.End >= SourceDuration - TimeSpan.FromMilliseconds(1);

    /// <summary>Переводит время внутри клипа во время внутри исходного файла.</summary>
    public TimeSpan ToSourceTime(TimeSpan offsetInClip)
    {
        var clamped = offsetInClip < TimeSpan.Zero ? TimeSpan.Zero : offsetInClip;
        if (clamped > TimelineDuration)
        {
            clamped = TimelineDuration;
        }

        return SourceRange.Start + TimeSpan.FromTicks((long)(clamped.Ticks * Speed));
    }

    public Clip WithSpeed(double speed)
    {
        var clamped = Math.Clamp(speed, MinSpeed, MaxSpeed);
        return this with { Speed = clamped };
    }

    public Clip WithAudio(ClipAudio audio) => this with { Audio = audio };

    public Clip WithTransform(ClipTransform transform) => this with { Transform = transform };

    /// <summary>
    /// Сдвигает точку входа на delta времени таймлайна: положительное значение
    /// укорачивает клип слева, отрицательное возвращает отрезанное, если источник позволяет.
    /// </summary>
    public Clip TrimStart(TimeSpan timelineDelta)
    {
        var sourceDelta = ToSourceDelta(timelineDelta);
        var newStart = SourceRange.Start + sourceDelta;

        var lowerBound = TimeSpan.Zero;
        var upperBound = SourceRange.End - MinSourceDuration;
        newStart = Clamp(newStart, lowerBound, upperBound);

        return this with { SourceRange = new TimeRange(newStart, SourceRange.End) };
    }

    /// <summary>
    /// Сдвигает точку выхода на delta времени таймлайна: положительное значение
    /// удлиняет клип справа в пределах источника.
    /// </summary>
    public Clip TrimEnd(TimeSpan timelineDelta)
    {
        var sourceDelta = ToSourceDelta(timelineDelta);
        var newEnd = SourceRange.End + sourceDelta;

        var lowerBound = SourceRange.Start + MinSourceDuration;
        var upperBound = SourceDuration;
        newEnd = Clamp(newEnd, lowerBound, upperBound);

        return this with { SourceRange = new TimeRange(SourceRange.Start, newEnd) };
    }

    /// <summary>
    /// Разрез ножницами в точке внутри клипа. Обе половины сохраняют скорость,
    /// звук и трансформацию, но получают новые идентификаторы.
    /// </summary>
    public (Clip Left, Clip Right) SplitAt(TimeSpan offsetInClip)
    {
        if (!CanSplitAt(offsetInClip))
        {
            throw new EditOperationException(
                "Разрез слишком близко к краю клипа: каждая половина должна остаться заметной.");
        }

        var splitSourceTime = ToSourceTime(offsetInClip);

        var left = this with
        {
            Id = ClipId.New(),
            SourceRange = new TimeRange(SourceRange.Start, splitSourceTime)
        };

        var right = this with
        {
            Id = ClipId.New(),
            SourceRange = new TimeRange(splitSourceTime, SourceRange.End),

            // Отступ достаётся левой половине: он был перед клипом, а не внутри него.
            LeadingGap = TimeSpan.Zero
        };

        return (left, right);
    }

    public bool CanSplitAt(TimeSpan offsetInClip)
    {
        if (offsetInClip <= TimeSpan.Zero || offsetInClip >= TimelineDuration)
        {
            return false;
        }

        var splitSourceTime = ToSourceTime(offsetInClip);

        return splitSourceTime - SourceRange.Start >= MinSourceDuration &&
               SourceRange.End - splitSourceTime >= MinSourceDuration;
    }

    private TimeSpan ToSourceDelta(TimeSpan timelineDelta) =>
        TimeSpan.FromTicks((long)(timelineDelta.Ticks * Speed));

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (max < min)
        {
            return min;
        }

        return value < min ? min : value > max ? max : value;
    }
}
