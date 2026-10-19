namespace MeowsCut.App.Timeline;

/// <summary>
/// Кусок волны файла, который лежит на доске: от <see cref="From"/> до <see cref="To"/>
/// в долях длительности файла.
/// </summary>
public readonly record struct WaveformSlice(WaveformPeaks Peaks, double From, double To)
{
    /// <summary>Громкость на доле <paramref name="start"/>–<paramref name="end"/> самого куска.</summary>
    public double Max(double start, double end)
    {
        var span = To - From;
        return Peaks.Max(From + (start * span), From + (end * span));
    }
}
