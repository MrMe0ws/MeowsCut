using MeowsCut.Core.Jobs;
using MeowsCut.Core.Processing;

namespace MeowsCut.Core.Abstractions;

/// <summary>
/// Исполнитель плана экспорта. Интерфейсу приложения известен только он —
/// про процессы, аргументы и временные файлы знает реализация.
/// </summary>
public interface IExportEngine
{
    Task<JobResult> ExecuteAsync(
        ExportPlan plan,
        IProgress<JobProgress> progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Рабочая папка задачи. Удаляется при любом исходе, включая отмену и падение.
/// </summary>
public interface ITempWorkspace : IAsyncDisposable
{
    string Directory { get; }

    string GetFilePath(string fileName);
}

public interface ITempWorkspaceFactory
{
    ITempWorkspace Create();

    /// <summary>Убирает папки задач, оставшиеся после аварийного завершения.</summary>
    void CleanOrphans(TimeSpan olderThan);
}

/// <summary>Открытие файла и папки в системе — платформенная мелочь, которую не место знать ViewModel.</summary>
public interface IShellIntegration
{
    void OpenFile(string path);

    void RevealInExplorer(string path);
}
