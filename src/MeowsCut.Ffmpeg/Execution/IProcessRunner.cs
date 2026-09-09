namespace MeowsCut.Ffmpeg.Execution;

/// <summary>
/// Запуск внешних процессов. Отдельный интерфейс нужен, чтобы всё, что строит команды,
/// тестировалось без реального ffmpeg.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Запускает процесс, построчно отдавая stdout и stderr. Отмена завершает процесс
    /// корректно (q в stdin), а через grace period убивает всё дерево.
    /// </summary>
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        Action<string>? onStandardOutputLine,
        Action<string>? onStandardErrorLine,
        CancellationToken cancellationToken);

    /// <summary>
    /// Запускает процесс и собирает stdout целиком — для ffprobe, который отдаёт JSON.
    /// </summary>
    Task<(ProcessResult Result, string StandardOutput)> RunCapturingOutputAsync(
        ProcessRequest request,
        CancellationToken cancellationToken);
}
