using System.Diagnostics;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Jobs;
using MeowsCut.Core.Processing;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Progress;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Ffmpeg.Rendering;

/// <summary>
/// Выполняет план экспорта: запускает стадии, считает прогресс, подчищает за собой.
/// </summary>
public sealed class FfmpegExportEngine(
    IMediaToolsetProvider toolsetProvider,
    IProcessRunner processRunner,
    ILogger<FfmpegExportEngine> logger) : IExportEngine
{
    /// <summary>Чаще десяти раз в секунду интерфейс перерисовывать незачем.</summary>
    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(100);

    public async Task<JobResult> ExecuteAsync(
        ExportPlan plan,
        IProgress<JobProgress> progress,
        CancellationToken cancellationToken)
    {
        var toolset = toolsetProvider.Require();
        var stopwatch = Stopwatch.StartNew();
        var partFiles = plan.Stages
            .Select(stage => stage.OutputFile)
            .Where(file => !string.IsNullOrEmpty(file))
            .ToArray();

        try
        {
            EnsureOutputDirectory(plan.OutputPath);

            for (var i = 0; i < plan.Stages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RunStageAsync(toolset.FfmpegPath, plan, i, progress, stopwatch, cancellationToken)
                    .ConfigureAwait(false);
            }

            var lastStage = plan.Stages[^1];
            var producedFile = lastStage.OutputFile ?? plan.OutputPath;

            PublishResult(producedFile, plan.OutputPath);

            progress.Report(new JobProgress(
                100,
                stopwatch.Elapsed,
                TimeSpan.Zero,
                "Готово",
                null,
                plan.Stages.Count - 1,
                plan.Stages.Count));

            logger.LogInformation("Экспорт завершён: {Path} за {Elapsed}", plan.OutputPath, stopwatch.Elapsed);
            return JobResult.Success(plan.OutputPath);
        }
        catch (OperationCanceledException)
        {
            CleanupPartials(partFiles);
            throw;
        }
        catch (Exception)
        {
            CleanupPartials(partFiles);
            throw;
        }
        finally
        {
            // Логи двухпроходного кодирования нужны только между стадиями.
            CleanupPartials(plan.CleanupPaths);
        }
    }

    private async Task RunStageAsync(
        string ffmpegPath,
        ExportPlan plan,
        int stageIndex,
        IProgress<JobProgress> progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var stage = plan.Stages[stageIndex];
        var parser = new FfmpegProgressParser();
        var eta = new EtaEstimator(plan.ExpectedOutputDuration);
        var lastReport = TimeSpan.Zero;

        var stageName = plan.Stages.Count > 1
            ? $"{stage.DisplayName} ({stageIndex + 1} из {plan.Stages.Count})"
            : stage.DisplayName;

        var request = new ProcessRequest(
            ffmpegPath,
            stage.Arguments,
            WorkingDirectory: null,
            RedirectStandardInput: true);

        var result = await processRunner.RunAsync(
                request,
                line =>
                {
                    var snapshot = parser.Feed(line);
                    if (snapshot is null)
                    {
                        return;
                    }

                    var elapsed = stopwatch.Elapsed;
                    if (elapsed - lastReport < ProgressThrottle && !snapshot.Completed)
                    {
                        return;
                    }

                    lastReport = elapsed;
                    progress.Report(BuildProgress(plan, stageIndex, stageName, snapshot, eta, elapsed));
                },
                onStandardErrorLine: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            var commandLine = CommandLineFormatter.Format(request);
            logger.LogError(
                "ffmpeg завершился с кодом {ExitCode}. Команда: {CommandLine}. Хвост вывода: {StdErr}",
                result.ExitCode,
                commandLine,
                string.Join(Environment.NewLine, result.StdErrTail));

            throw new FfmpegExecutionException(
                DescribeFailure(result.StdErrTail),
                result.ExitCode,
                result.StdErrTail,
                commandLine);
        }
    }

    private static JobProgress BuildProgress(
        ExportPlan plan,
        int stageIndex,
        string stageName,
        ProgressSnapshot snapshot,
        EtaEstimator eta,
        TimeSpan elapsed)
    {
        var processed = snapshot.OutTime ?? TimeSpan.Zero;

        // Процент считается от ожидаемой длительности результата, а не исходника:
        // при вырезании и ускорении это разные величины, и иначе прогресс врёт.
        var stageFraction = plan.ExpectedOutputDuration > TimeSpan.Zero
            ? Math.Clamp(processed.TotalSeconds / plan.ExpectedOutputDuration.TotalSeconds, 0d, 1d)
            : 0d;

        var completedWeight = plan.Stages.Take(stageIndex).Sum(stage => stage.Weight);
        var totalWeight = plan.Stages.Sum(stage => stage.Weight);
        var percent = totalWeight > 0
            ? (completedWeight + (plan.Stages[stageIndex].Weight * stageFraction)) / totalWeight * 100d
            : 0d;

        var detail = snapshot.SpeedFactor is { } speed
            ? $"{speed:0.0}× · {processed:mm\\:ss}"
            : null;

        return new JobProgress(
            Math.Clamp(percent, 0d, 100d),
            elapsed,
            eta.Update(processed, snapshot.SpeedFactor, elapsed),
            stageName,
            detail,
            stageIndex,
            plan.Stages.Count);
    }

    /// <summary>
    /// Результат пишется в файл .part рядом с целью и переименовывается только после успеха:
    /// пользователь не должен получить недописанный файл, выглядящий как готовый.
    /// </summary>
    private void PublishResult(string producedFile, string outputPath)
    {
        if (string.Equals(producedFile, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!File.Exists(producedFile))
        {
            throw new OutputWriteException($"ffmpeg не создал файл результата: {producedFile}");
        }

        try
        {
            File.Move(producedFile, outputPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new OutputWriteException(
                "Не удалось сохранить файл: возможно, он открыт в другой программе.", ex);
        }
    }

    private static void EnsureOutputDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private void CleanupPartials(IEnumerable<string?> files)
    {
        foreach (var file in files)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Не удалось удалить незавершённый файл {File}", file);
            }
        }
    }

    /// <summary>
    /// Переводит частые причины отказа ffmpeg на человеческий язык. Остальное
    /// остаётся в подробностях ошибки и в логе.
    /// </summary>
    private static string DescribeFailure(IReadOnlyList<string> stdErr)
    {
        var text = string.Join(' ', stdErr);

        if (text.Contains("No space left", StringComparison.OrdinalIgnoreCase))
        {
            return "На диске не хватает места для результата.";
        }

        if (text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return "Нет прав на запись в выбранную папку.";
        }

        if (text.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase))
        {
            return "Выбранный кодек отсутствует в этой сборке FFmpeg.";
        }

        if (text.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("moov atom not found", StringComparison.OrdinalIgnoreCase))
        {
            return "Исходный файл повреждён или обрезан.";
        }

        return "FFmpeg не смог обработать видео.";
    }
}
