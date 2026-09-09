using MeowsCut.Core.Editing;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Export;

/// <summary>Запрос на экспорт: что рендерим и с какими параметрами.</summary>
public sealed record ExportRequest(Project Project, ExportSettings Settings)
{
    public string OutputPath => Settings.OutputPath;
}

public enum PlanWarningKind
{
    Upscale = 0,
    FrameRateIncrease,
    StreamCopyKeyframeSnap,
    VariableFrameRateSource,
    AudioDropped,
    SizeEstimateUnavailable
}

/// <summary>Предупреждение о последствиях выбранных настроек — показывается до запуска.</summary>
public sealed record PlanWarning(PlanWarningKind Kind, string Message);

/// <summary>
/// Сводка, которую пользователь видит перед экспортом. Считает её планировщик,
/// а не интерфейс: иначе показанное и запущенное однажды разойдутся.
/// </summary>
public sealed record ExportSummary(
    FrameSize Resolution,
    double Fps,
    string VideoCodecLabel,
    string VideoBitrateLabel,
    string AudioLabel,
    TimeSpan Duration,
    int ClipCount,
    long? EstimatedSizeBytes,
    bool IsStreamCopy);

/// <summary>
/// Оценка размера результата.
/// </summary>
public static class OutputSizeEstimator
{
    /// <summary>Накладные расходы контейнера — примерно два процента.</summary>
    private const double ContainerOverhead = 1.02;

    public static long? Estimate(long? videoBitrateBps, long? audioBitrateBps, TimeSpan duration)
    {
        if (videoBitrateBps is null || duration <= TimeSpan.Zero)
        {
            return null;
        }

        var totalBits = (videoBitrateBps.Value + (audioBitrateBps ?? 0)) * duration.TotalSeconds;
        return (long)(totalBits / 8 * ContainerOverhead);
    }
}
