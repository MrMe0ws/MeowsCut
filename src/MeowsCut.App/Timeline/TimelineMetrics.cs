namespace MeowsCut.App.Timeline;

/// <summary>
/// Пересчёт времени в пиксели и обратно.
/// </summary>
/// <remarks>
/// Вынесено из контрола в отдельный тип по одной причине: это единственное место,
/// где легко ошибиться на масштабе и прокрутке, и его нужно уметь проверять тестами
/// без окна и мыши.
/// </remarks>
public sealed class TimelineMetrics
{
    public const double MinPixelsPerSecond = 2d;
    public const double MaxPixelsPerSecond = 600d;

    private double _pixelsPerSecond = 40d;
    private double _scrollSeconds;

    /// <summary>Сколько пикселей занимает секунда таймлайна.</summary>
    public double PixelsPerSecond
    {
        get => _pixelsPerSecond;
        set => _pixelsPerSecond = Math.Clamp(value, MinPixelsPerSecond, MaxPixelsPerSecond);
    }

    /// <summary>Время в левом краю видимой области.</summary>
    public TimeSpan Scroll
    {
        get => TimeSpan.FromSeconds(_scrollSeconds);
        set => _scrollSeconds = Math.Max(0d, value.TotalSeconds);
    }

    public double ViewportWidth { get; set; } = 800d;

    public TimeSpan VisibleDuration =>
        TimeSpan.FromSeconds(ViewportWidth / Math.Max(PixelsPerSecond, MinPixelsPerSecond));

    public double TimeToX(TimeSpan time) => (time.TotalSeconds - _scrollSeconds) * PixelsPerSecond;

    public TimeSpan XToTime(double x) =>
        TimeSpan.FromSeconds(Math.Max(0d, (x / PixelsPerSecond) + _scrollSeconds));

    public double DurationToWidth(TimeSpan duration) => duration.TotalSeconds * PixelsPerSecond;

    public TimeSpan WidthToDuration(double width) => TimeSpan.FromSeconds(width / PixelsPerSecond);

    /// <summary>
    /// Масштабирование с фиксацией точки под курсором: иначе при зуме содержимое
    /// уезжает из-под мыши и попасть по нужному кадру невозможно.
    /// </summary>
    public void ZoomAt(double factor, double anchorX)
    {
        var anchorTime = XToTime(anchorX);
        PixelsPerSecond *= factor;
        Scroll = TimeSpan.FromSeconds(anchorTime.TotalSeconds - (anchorX / PixelsPerSecond));
    }

    /// <summary>Подбирает масштаб так, чтобы вся последовательность поместилась в окно.</summary>
    public void ZoomToFit(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || ViewportWidth <= 0)
        {
            return;
        }

        PixelsPerSecond = (ViewportWidth - 24d) / duration.TotalSeconds;
        Scroll = TimeSpan.Zero;
    }

    /// <summary>Подкручивает прокрутку так, чтобы указанное время осталось видимым.</summary>
    public void EnsureVisible(TimeSpan time, double edgePadding = 40d)
    {
        var x = TimeToX(time);

        if (x < edgePadding)
        {
            Scroll = TimeSpan.FromSeconds(time.TotalSeconds - (edgePadding / PixelsPerSecond));
        }
        else if (x > ViewportWidth - edgePadding)
        {
            Scroll = TimeSpan.FromSeconds(time.TotalSeconds - ((ViewportWidth - edgePadding) / PixelsPerSecond));
        }
    }

    /// <summary>
    /// Шаг разметки времени: подбирается так, чтобы подписи не слипались
    /// ни на секундном масштабе, ни на часовом.
    /// </summary>
    public TimeSpan RulerStep()
    {
        double[] candidates =
        [
            0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30,
            60, 120, 300, 600, 900, 1800, 3600
        ];

        const double minimumLabelSpacing = 70d;

        foreach (var candidate in candidates)
        {
            if (candidate * PixelsPerSecond >= minimumLabelSpacing)
            {
                return TimeSpan.FromSeconds(candidate);
            }
        }

        return TimeSpan.FromSeconds(candidates[^1]);
    }
}
