using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Timeline;

/// <summary>Инструмент доски монтажа.</summary>
/// <remarks>
/// Инструментов сознательно два. Отдельная «рука» была третьим режимом, ради которого
/// приходилось переключаться и возвращаться обратно; протяжка доски досталась средней
/// кнопке, пробелу и колесу — они доступны всегда, каким бы инструментом ни работали.
/// </remarks>
public enum TimelineTool
{
    /// <summary>Стрелка: выделение, перенос клипов и обрезка краёв.</summary>
    Select = 0,

    /// <summary>Ножницы: клик режет клип.</summary>
    Razor
}

/// <summary>Что находится под курсором.</summary>
public enum TimelineHitKind
{
    Empty = 0,
    Ruler,
    ClipBody,
    ClipStartEdge,
    ClipEndEdge
}

public readonly record struct TimelineHit(TimelineHitKind Kind, PlacedClip? Clip, TimeSpan Time)
{
    public static TimelineHit Nothing(TimeSpan time) => new(TimelineHitKind.Empty, null, time);

    public bool IsEdge => Kind is TimelineHitKind.ClipStartEdge or TimelineHitKind.ClipEndEdge;
}

/// <summary>
/// Определяет, по чему кликнул пользователь.
/// </summary>
/// <remarks>
/// Зоны краёв клипа фиксированы в пикселях: на мелком масштабе клип шириной в десяток
/// пикселей иначе состоял бы из одних только ручек, и его нельзя было бы выделить.
/// </remarks>
public sealed class TimelineHitTester
{
    public const double EdgeGripPixels = 7d;

    public double RulerHeight { get; set; } = 24d;

    public TimelineHit Test(double x, double y, Sequence sequence, TimelineMetrics metrics)
    {
        var time = metrics.XToTime(x);

        if (y < RulerHeight)
        {
            return new TimelineHit(TimelineHitKind.Ruler, null, time);
        }

        foreach (var placed in sequence.EnumeratePlaced())
        {
            var left = metrics.TimeToX(placed.Start);
            var right = metrics.TimeToX(placed.End);

            if (x < left || x > right)
            {
                continue;
            }

            // Ручки не должны занимать больше трети клипа, иначе по телу не попасть.
            var grip = Math.Min(EdgeGripPixels, (right - left) / 3d);

            if (x - left <= grip)
            {
                return new TimelineHit(TimelineHitKind.ClipStartEdge, placed, time);
            }

            if (right - x <= grip)
            {
                return new TimelineHit(TimelineHitKind.ClipEndEdge, placed, time);
            }

            return new TimelineHit(TimelineHitKind.ClipBody, placed, time);
        }

        return TimelineHit.Nothing(time);
    }

    /// <summary>Индекс, на который встанет перетаскиваемый клип при отпускании в точке x.</summary>
    public int ResolveDropIndex(double x, Sequence sequence, TimelineMetrics metrics, ClipId draggedClip)
    {
        var time = metrics.XToTime(x);
        var index = 0;

        foreach (var placed in sequence.EnumeratePlaced())
        {
            if (placed.Clip.Id == draggedClip)
            {
                continue;
            }

            // Клип встаёт перед соседом, если курсор левее его середины.
            var middle = placed.Start + TimeSpan.FromTicks(placed.Clip.TimelineDuration.Ticks / 2);
            if (time < middle)
            {
                break;
            }

            index++;
        }

        return Math.Clamp(index, 0, Math.Max(0, sequence.ClipCount - 1));
    }
}
