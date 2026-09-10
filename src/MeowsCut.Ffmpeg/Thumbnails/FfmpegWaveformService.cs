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
/// Рисует волну звука фильтром showwavespic и складывает её в кэш на диске.
/// </summary>
/// <remarks>
/// Ширина фиксирована и заведомо больше экранной: доска растягивает готовую
/// картинку под масштаб, и пересчитывать волну при каждом зуме не приходится.
/// Каналы сводятся в один — на полосе высотой в сорок пикселей стерео
/// превращается в две неразличимые полоски.
/// </remarks>
public sealed class FfmpegWaveformService(
    AppPaths paths,
    IMediaToolsetProvider toolsetProvider,
    IProcessRunner processRunner,
    ILogger<FfmpegWaveformService> logger) : IWaveformService
{
    /// <summary>Разрешение картинки волны. Хватает, чтобы разглядеть удар на любом масштабе.</summary>
    private const int Width = 2400;
    private const int Height = 120;

    private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new();

    public Task<string?> GetWaveformAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            return Task.FromResult<string?>(null);
        }

        var cachePath = BuildCachePath(sourcePath);

        if (File.Exists(cachePath))
        {
            return Task.FromResult<string?>(cachePath);
        }

        // Один и тот же файл не обсчитываем дважды параллельно: доска просит волну
        // на каждой перерисовке, а проход по длинному треку идёт секунды.
        return _inFlight.GetOrAdd(cachePath, _ => RenderAsync(sourcePath, cachePath, cancellationToken));
    }

    private async Task<string?> RenderAsync(
        string sourcePath,
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

            // Расширение обязано остаться .png: формат вывода ffmpeg определяет по нему.
            var temporaryPath = cachePath + ".part.png";

            var size = $"{Width.ToString(CultureInfo.InvariantCulture)}x{Height.ToString(CultureInfo.InvariantCulture)}";

            var arguments = FfmpegArgumentBuilder.Create()
                .HideBanner()
                .OverwriteOutput()
                .NoStdin()
                .LogLevel("error")
                .Input(sourcePath)
                // scale=sqrt, а не линейная: музыка сводится с запасом по громкости,
                // и в линейном масштабе типичная дорожка выглядит ниткой по центру.
                // Корень поднимает тихое, не срезая громкое, — форма читается.
                .Option(
                    "-filter_complex",
                    $"aformat=channel_layouts=mono,showwavespic=s={size}:colors=#8FE3A8:scale=sqrt")
                .Option("-frames:v", "1")
                .Output(temporaryPath)
                .Build();

            var result = await processRunner
                .RunAsync(new ProcessRequest(toolset.FfmpegPath, arguments), null, null, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success || !File.Exists(temporaryPath))
            {
                logger.LogDebug(
                    "Волну для {Path} построить не удалось (код {ExitCode}): {Details}",
                    sourcePath,
                    result.ExitCode,
                    string.Join(' ', result.StdErrTail));

                return null;
            }

            // Переименование не даёт доске подхватить наполовину записанный файл.
            File.Move(temporaryPath, cachePath, overwrite: true);
            return cachePath;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Не удалось построить волну для {Path}", sourcePath);
            return null;
        }
        finally
        {
            _inFlight.TryRemove(cachePath, out _);
        }
    }

    /// <summary>
    /// Ключ кэша учитывает размер и дату изменения: перезаписанный файл
    /// не должен показывать волну от прежнего звука.
    /// </summary>
    private string BuildCachePath(string sourcePath)
    {
        var info = new FileInfo(sourcePath);

        // Вид волны входит в ключ: поменяв размер или шкалу, мы обязаны перерисовать,
        // а не подсунуть картинку, нарисованную по прежним правилам.
        var key = $"{sourcePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{Width}x{Height}|sqrt";

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16];

        return Path.Combine(paths.WaveformsDirectory, $"{hash}.png");
    }
}
