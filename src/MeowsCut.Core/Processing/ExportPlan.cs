using MeowsCut.Core.Export;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Processing;

public enum StageKind
{
    /// <summary>Копирование потоков без перекодирования.</summary>
    Remux = 0,

    /// <summary>Один проход перекодирования.</summary>
    Transcode,

    /// <summary>Первый проход при кодировании под целевой размер.</summary>
    PassOne,

    /// <summary>Второй проход.</summary>
    PassTwo
}

/// <summary>
/// Одна стадия работы: готовые аргументы ffmpeg плюс всё, что нужно для прогресса.
/// </summary>
public sealed record ExportStage(
    StageKind Kind,
    string DisplayName,
    IReadOnlyList<string> Arguments,
    double Weight,
    TimeSpan ExpectedDuration,
    string? OutputFile);

/// <summary>
/// План экспорта — промежуточное представление между намерением пользователя
/// и запуском процессов. Строится чистой логикой и потому проверяется тестами
/// без единого запуска ffmpeg.
/// </summary>
public sealed record ExportPlan(
    IReadOnlyList<ExportStage> Stages,
    TimeSpan ExpectedOutputDuration,
    ExportSummary Summary,
    IReadOnlyList<PlanWarning> Warnings,
    string OutputPath)
{
    /// <summary>
    /// Служебные файлы, которые нужно убрать после работы: логи двухпроходного
    /// кодирования и прочий мусор, который ffmpeg оставляет рядом.
    /// </summary>
    public IReadOnlyList<string> CleanupPaths { get; init; } = [];

    public bool IsStreamCopy => Stages.All(stage => stage.Kind == StageKind.Remux);

    public string StageDisplayName(int index) =>
        index >= 0 && index < Stages.Count ? Stages[index].DisplayName : string.Empty;
}

/// <summary>
/// Превращает запрос в план. Реализация живёт в слое ffmpeg, но интерфейс —
/// в ядре: интерфейс приложения знает только его.
/// </summary>
public interface IExportPlanner
{
    ExportPlan CreatePlan(ExportRequest request, MediaCapabilities capabilities);
}
