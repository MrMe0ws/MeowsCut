namespace MeowsCut.Ffmpeg.Progress;

/// <summary>
/// Оценка оставшегося времени.
/// </summary>
/// <remarks>
/// Мгновенная скорость ffmpeg скачет, и оценка «в лоб» прыгает от пяти секунд
/// до трёх минут и обратно. Поэтому скорость сглаживается, первые секунды
/// оценка не показывается вовсе, а вверх она может расти только плавно —
/// прыгающая цифра выглядит как сломанная.
/// </remarks>
public sealed class EtaEstimator(TimeSpan expectedDuration)
{
    private const double SmoothingFactor = 0.25;
    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(2);

    private double? _smoothedSpeed;
    private TimeSpan? _lastEstimate;

    public TimeSpan? Update(TimeSpan processed, double? speedFactor, TimeSpan elapsed)
    {
        if (speedFactor is > 0)
        {
            _smoothedSpeed = _smoothedSpeed is null
                ? speedFactor
                : (_smoothedSpeed * (1 - SmoothingFactor)) + (speedFactor * SmoothingFactor);
        }

        if (elapsed < WarmUp || _smoothedSpeed is not > 0 || expectedDuration <= TimeSpan.Zero)
        {
            return null;
        }

        var remainingContent = expectedDuration - processed;
        if (remainingContent <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var estimate = TimeSpan.FromSeconds(remainingContent.TotalSeconds / _smoothedSpeed.Value);

        // Даём оценке свободно уменьшаться, но расти — только понемногу.
        if (_lastEstimate is { } previous && estimate > previous)
        {
            var maxGrowth = previous + TimeSpan.FromSeconds(1);
            estimate = estimate > maxGrowth ? maxGrowth : estimate;
        }

        _lastEstimate = estimate;
        return estimate;
    }
}
