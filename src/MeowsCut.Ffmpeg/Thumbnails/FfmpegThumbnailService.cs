using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Ffmpeg.Arguments;
using MeowsCut.Ffmpeg.Execution;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Ffmpeg.Thumbnails;

/// <summary>
/// Извлекает кадры через ffmpeg и складывает их в кэш на диске.
/// </summary>
/// <remarks>
/// Таймлайн запрашивает десятки кадров при каждом изменении масштаба, поэтому
/// готовый кадр никогда не извлекается повторно, а одновременные запросы одного
/// и того же кадра схлопываются в одну задачу.
/// </remarks>
public sealed class FfmpegThumbnailService(
    AppPaths paths,
    IMediaToolsetProvider toolsetProvider,
    IProcessRunner processRunner,
    ILogger<FfmpegThumbnailService> logger) : IThumbnailService
{
    private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new();

    public Task<string?> GetFrameAsync(
        string sourcePath,
        TimeSpan position,
        int width,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath) || width <= 0)
        {
            return Task.FromResult<string?>(null);
        }

        var cachePath = BuildCachePath(sourcePath, position, width);

        if (File.Exists(cachePath))
        {
            return Task.FromResult<string?>(cachePath);
        }

        // Один и тот же кадр не извлекаем дважды параллельно.
        return _inFlight.GetOrAdd(cachePath, _ => ExtractAsync(sourcePath, position, width, cachePath, cancellationToken));
    }

    private async Task<string?> ExtractAsync(
        string sourcePath,
        TimeSpan position,
        int width,
        string cachePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var toolset = toolsetProvider.Current;
            if (toolset is null)
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            // Расширение обязано остаться .jpg: формат вывода ffmpeg определяет по нему,
            // и файл вида "кадр.jpg.tmp" он записать отказывается.
            var temporaryPath = cachePath + ".part.jpg";

            var arguments = FfmpegArgumentBuilder.Create()
                .HideBanner()
                .OverwriteOutput()
                .NoStdin()
                .LogLevel("error")
                // -ss до -i даёт быстрый поиск: для миниатюры точность до кадра не нужна.
                .InputSeek(position)
                .Input(sourcePath)
                .Option("-frames:v", "1")
                .Option("-vf", $"scale={width.ToString(CultureInfo.InvariantCulture)}:-2")
                .Option("-q:v", "5")
                .Output(temporaryPath)
                .Build();

            var result = await processRunner
                .RunAsync(new ProcessRequest(toolset.FfmpegPath, arguments), null, null, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success || !File.Exists(temporaryPath))
            {
                logger.LogDebug(
                    "Кадр {Position} из {Path} извлечь не удалось (код {ExitCode}): {Details}",
                    position,
                    sourcePath,
                    result.ExitCode,
                    string.Join(' ', result.StdErrTail));

                return null;
            }

            // Переименование не даёт таймлайну подхватить наполовину записанный файл.
            File.Move(temporaryPath, cachePath, overwrite: true);
            return cachePath;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Не удалось извлечь кадр {Position} из {Path}", position, sourcePath);
            return null;
        }
        finally
        {
            _inFlight.TryRemove(cachePath, out _);
        }
    }

    /// <summary>
    /// Ключ кэша учитывает не только путь, но и размер с датой изменения:
    /// перезаписанный файл не должен показывать старые кадры.
    /// </summary>
    private string BuildCachePath(string sourcePath, TimeSpan position, int width)
    {
        var info = new FileInfo(sourcePath);
        var key = $"{sourcePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        var milliseconds = (long)position.TotalMilliseconds;

        return Path.Combine(
            paths.ThumbnailsDirectory,
            hash,
            $"{milliseconds}_{width}.jpg");
    }

    public void TrimCache(long limitBytes)
    {
        try
        {
            if (!Directory.Exists(paths.ThumbnailsDirectory))
            {
                return;
            }

            var files = new DirectoryInfo(paths.ThumbnailsDirectory)
                .EnumerateFiles("*.jpg", SearchOption.AllDirectories)
                .OrderBy(file => file.LastAccessTimeUtc)
                .ToArray();

            var total = files.Sum(file => file.Length);

            foreach (var file in files)
            {
                if (total <= limitBytes)
                {
                    break;
                }

                total -= file.Length;

                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Не удалось удалить кадр из кэша {File}", file.FullName);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Не удалось привести кэш миниатюр к ограничению");
        }
    }
}
