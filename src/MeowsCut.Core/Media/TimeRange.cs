namespace MeowsCut.Core.Media;

/// <summary>
/// Полуинтервал времени [Start, End) внутри медиафайла.
/// </summary>
public readonly record struct TimeRange(TimeSpan Start, TimeSpan End)
{
    public static readonly TimeRange Empty = new(TimeSpan.Zero, TimeSpan.Zero);

    public static TimeRange FromDuration(TimeSpan start, TimeSpan duration) => new(start, start + duration);

    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    public bool IsEmpty => End <= Start;

    public bool Contains(TimeSpan time) => time >= Start && time < End;

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    /// <summary>
    /// Ограничивает диапазон длительностью источника и не даёт End уйти раньше Start.
    /// </summary>
    public TimeRange Clamp(TimeSpan mediaDuration)
    {
        var start = Start < TimeSpan.Zero ? TimeSpan.Zero : Start;
        if (start > mediaDuration)
        {
            start = mediaDuration;
        }

        var end = End > mediaDuration ? mediaDuration : End;
        if (end < start)
        {
            end = start;
        }

        return new TimeRange(start, end);
    }

    public override string ToString() => $"{Start:hh\\:mm\\:ss\\.fff} → {End:hh\\:mm\\:ss\\.fff}";
}
