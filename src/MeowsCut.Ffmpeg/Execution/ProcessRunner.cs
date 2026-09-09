using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Ffmpeg.Execution;

/// <summary>
/// Единственное место в приложении, где запускается внешний процесс.
/// </summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    /// <summary>Сколько ждём корректного завершения после запроса отмены, прежде чем убивать.</summary>
    private static readonly TimeSpan GracefulShutdownTimeout = TimeSpan.FromMilliseconds(1500);

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        Action<string>? onStandardOutputLine,
        Action<string>? onStandardErrorLine,
        CancellationToken cancellationToken)
    {
        var stdErrTail = new StdErrRingBuffer();
        var stopwatch = Stopwatch.StartNew();

        using var process = CreateProcess(request);

        var stdOutComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdErrComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                stdOutComplete.TrySetResult();
                return;
            }

            onStandardOutputLine?.Invoke(args.Data);
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                stdErrComplete.TrySetResult();
                return;
            }

            stdErrTail.Add(args.Data);
            onStandardErrorLine?.Invoke(args.Data);
        };

        logger.LogDebug("Запуск: {CommandLine}", CommandLineFormatter.Format(request));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var cancellation = cancellationToken.Register(() => Terminate(process, request));

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdOutComplete.Task, stdErrComplete.Task).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = new ProcessResult(process.ExitCode, stdErrTail.Snapshot(), stopwatch.Elapsed);

        logger.LogDebug(
            "Процесс {FileName} завершён с кодом {ExitCode} за {Elapsed}",
            Path.GetFileName(request.FileName),
            result.ExitCode,
            result.Elapsed);

        return result;
    }

    public async Task<(ProcessResult Result, string StandardOutput)> RunCapturingOutputAsync(
        ProcessRequest request,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var result = await RunAsync(
                request,
                line => output.AppendLine(line),
                onStandardErrorLine: null,
                cancellationToken)
            .ConfigureAwait(false);

        return (result, output.ToString());
    }

    private static Process CreateProcess(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.RedirectStandardInput,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty
        };

        // Только ArgumentList: экранирование делает рантайм, конкатенация строк запрещена.
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    /// <summary>
    /// Отмена: сначала просим ffmpeg завершиться сам (он корректно закрывает файл),
    /// затем убиваем всё дерево процессов.
    /// </summary>
    private void Terminate(Process process, ProcessRequest request)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            if (request.RedirectStandardInput)
            {
                try
                {
                    process.StandardInput.Write('q');
                    process.StandardInput.Flush();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    logger.LogDebug(ex, "Не удалось отправить q процессу, будет принудительное завершение");
                }

                if (process.WaitForExit((int)GracefulShutdownTimeout.TotalMilliseconds))
                {
                    logger.LogDebug("Процесс завершился корректно после запроса отмены");
                    return;
                }
            }

            process.Kill(entireProcessTree: true);
            logger.LogDebug("Процесс и его дочерние процессы принудительно завершены");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            logger.LogDebug(ex, "Не удалось завершить процесс при отмене");
        }
    }
}
