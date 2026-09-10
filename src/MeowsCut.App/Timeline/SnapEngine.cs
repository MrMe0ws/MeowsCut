using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Timeline;

/// <summary>
/// Прилипание к значимым точкам таймлайна.
/// </summary>
/// <remarks>
/// Мышью невозможно попасть ровно в стык клипов, а именно туда пользователь целится
/// в девяти случаях из десяти. Порог задан в пикселях, а не в секундах: на разном
/// масштабе «рядом» означает разное количество времени.
/// </remarks>
public sealed class SnapEngine
{
    public const double DefaultThresholdPixels = 8d;

    public bool IsEnabled { get; set; } = true;

    public double ThresholdPixels { get; set; } = DefaultThresholdPixels;

    /// <summary>
    /// Притягивает время к ближайшей значимой точке: границам клипов, курсору и нулю.
    /// </summary>
    public TimeSpan Snap(
        TimeSpan time,
        Sequence sequence,
        TimelineMetrics metrics,
        TimeSpan? playhead = null)
    {
        if (!IsEnabled)
        {
            return time;
        }

        var thresholdSeconds = ThresholdPixels / Math.Max(metrics.PixelsPerSecond, TimelineMetrics.MinPixelsPerSecond);

        var best = time;
        var bestDistance = thresholdSeconds;

        foreach (var candidate in EnumerateSnapPoints(sequence, playhead))
        {
            var distance = Math.Abs((candidate - time).TotalSeconds);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <remarks>
    /// Границы перетаскиваемого клипа отдельно не исключаются: на дорожке без зазоров
    /// они всегда совпадают с границами соседа, который остаётся законной целью прилипания.
    /// </remarks>
    private static IEnumerable<TimeSpan> EnumerateSnapPoints(Sequence sequence, TimeSpan? playhead)
    {
        yield return TimeSpan.Zero;
        yield return sequence.Duration;

        if (playhead is { } cursor)
        {
            yield return cursor;
        }

        foreach (var placed in sequence.EnumeratePlaced())
        {
            yield return placed.Start;
            yield return placed.End;
        }
    }
}
