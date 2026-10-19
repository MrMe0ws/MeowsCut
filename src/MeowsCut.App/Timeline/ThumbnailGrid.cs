namespace MeowsCut.App.Timeline;

/// <summary>
/// Сетка точек, из которых берутся кадры для полосы клипа.
/// </summary>
/// <remarks>
/// Раньше кадр брался ровно из середины ячейки, с точностью до миллисекунды.
/// Любая правка — обрезка, сдвиг, масштаб — сдвигала середины всех ячеек, кэш
/// промахивался почти целиком, и после каждого движения мышью шла новая пачка
/// декодирования 4K. Теперь точка прилипает к сетке с шагом в степень двойки:
/// после правки большая часть кадров уже лежит в кэше, а при смене масштаба
/// шаги кратны друг другу и тоже совпадают.
/// </remarks>
public static class ThumbnailGrid
{
    private const double MinStepSeconds = 0.25;
    private const double MaxStepSeconds = 64d;

    /// <param name="sourceTime">Время в исходнике, которое приходится на середину ячейки.</param>
    /// <param name="slotSpan">Сколько времени исходника покрывает одна ячейка.</param>
    public static TimeSpan Snap(TimeSpan sourceTime, TimeSpan slotSpan)
    {
        var step = MinStepSeconds;
        var half = slotSpan.TotalSeconds / 2;

        while (step < half && step < MaxStepSeconds)
        {
            step *= 2;
        }

        return TimeSpan.FromSeconds(Math.Floor(sourceTime.TotalSeconds / step) * step);
    }
}
