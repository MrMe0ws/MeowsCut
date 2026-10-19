namespace MeowsCut.Core.Editing.Timeline;

/// <summary>
/// Наезд и отъезд: масштаб и сдвиг кадра плавно идут от 100% к заданным или обратно.
/// </summary>
/// <remarks>
/// Одна формула на предпросмотр и экспорт — иначе в редакторе наезд шёл бы одним
/// темпом, а в файле другим. Масштаб меняется по степени, а не по прямой: с 100%
/// до 300% по прямой первые доли секунды кадр рос бы заметно быстрее последних.
/// Разгон и торможение — smoothstep, без рывка в начале и в конце.
/// </remarks>
public static class ClipMotion
{
    /// <summary>Самый долгий наезд, который можно задать.</summary>
    public const double MaxSeconds = 600d;

    /// <summary>Доля пройденного пути с плавным разгоном и торможением.</summary>
    public static double Ease(double progress)
    {
        var p = Math.Clamp(progress, 0d, 1d);
        return p * p * (3 - (2 * p));
    }

    /// <summary>
    /// Сколько наезда пройдено к моменту <paramref name="offset"/> от начала куска.
    /// Наезд длиннее куска ужимается до его длины, иначе он не доходил бы до конца.
    /// </summary>
    public static double Progress(TimeSpan offset, double motionSeconds, TimeSpan clipDuration)
    {
        var length = Math.Min(motionSeconds, clipDuration.TotalSeconds);
        return length <= 0 ? 1d : Math.Clamp(offset.TotalSeconds / length, 0d, 1d);
    }

    /// <summary>
    /// Наибольший сдвиг в долях кадра, при котором кадр не выходит за картинку.
    /// </summary>
    /// <remarks>
    /// При увеличении окно ходит внутри картинки на (Z−1)/2 своей ширины, при
    /// уменьшении картинка ходит внутри кадра на столько же. На 100% двигать нечего.
    /// </remarks>
    public static double OffsetLimit(double zoom) => Math.Abs(zoom - 1d) / 2d;
}
