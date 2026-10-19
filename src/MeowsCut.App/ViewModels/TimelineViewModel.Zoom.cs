using MeowsCut.App.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Масштаб доски ползунком на её панели.
/// </summary>
/// <remarks>
/// Ползунок идёт по логарифму: от всей ленты на экране до отдельных кадров
/// масштаб меняется в сотни раз, и на линейной шкале весь полезный ход
/// сжимался бы в первые несколько пикселей.
/// </remarks>
public sealed partial class TimelineViewModel
{
    public const double ZoomLevelMax = 100d;

    /// <summary>Положение ползунка масштаба, от 0 (вся лента) до <see cref="ZoomLevelMax"/>.</summary>
    public double ZoomLevel
    {
        get
        {
            var span = Math.Log(TimelineMetrics.MaxPixelsPerSecond / TimelineMetrics.MinPixelsPerSecond);
            return Math.Log(Metrics.PixelsPerSecond / TimelineMetrics.MinPixelsPerSecond) / span * ZoomLevelMax;
        }
        set
        {
            var span = Math.Log(TimelineMetrics.MaxPixelsPerSecond / TimelineMetrics.MinPixelsPerSecond);
            var target = TimelineMetrics.MinPixelsPerSecond * Math.Exp(Math.Clamp(value, 0d, ZoomLevelMax) / ZoomLevelMax * span);

            if (Math.Abs(target - Metrics.PixelsPerSecond) < 0.001)
            {
                return;
            }

            // Масштаб растёт от плейхеда, если он на экране: смотрят обычно туда,
            // где стоит курсор времени, а не в середину окна.
            var anchor = Metrics.TimeToX(Playhead);
            if (anchor < 0 || anchor > Metrics.ViewportWidth)
            {
                anchor = Metrics.ViewportWidth / 2;
            }

            Metrics.ZoomAt(target / Metrics.PixelsPerSecond, anchor);
            RequestRedraw();
        }
    }
}
