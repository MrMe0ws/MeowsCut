using System.Globalization;

namespace MeowsCut.Ffmpeg.Arguments;

/// <summary>
/// Раскладка скорости в фильтры ffmpeg.
/// </summary>
/// <remarks>
/// Видео замедляется и ускоряется одним setpts, а вот atempo принимает только
/// множители от 0.5 до 2.0. Поэтому 4× — это atempo=2,atempo=2, а 0.25× —
/// atempo=0.5,atempo=0.5. Без раскладки ffmpeg просто откажется строить граф.
/// </remarks>
public static class SpeedFilter
{
    private const double MinTempo = 0.5;
    private const double MaxTempo = 2.0;

    public static string VideoSetPts(double speed) =>
        "setpts=" + FormatFactor(1d / speed) + "*PTS";

    /// <summary>Множители atempo, произведение которых даёт нужную скорость.</summary>
    public static IReadOnlyList<double> DecomposeTempo(double speed)
    {
        if (speed <= 0)
        {
            return [1d];
        }

        if (Math.Abs(speed - 1d) < 0.0001)
        {
            return [];
        }

        var factors = new List<double>();
        var remaining = speed;

        while (remaining > MaxTempo)
        {
            factors.Add(MaxTempo);
            remaining /= MaxTempo;
        }

        while (remaining < MinTempo)
        {
            factors.Add(MinTempo);
            remaining /= MinTempo;
        }

        if (Math.Abs(remaining - 1d) > 0.0001)
        {
            factors.Add(remaining);
        }

        return factors;
    }

    public static IReadOnlyList<string> AudioTempoFilters(double speed) =>
        DecomposeTempo(speed)
            .Select(factor => "atempo=" + FormatFactor(factor))
            .ToArray();

    public static string FormatFactor(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
