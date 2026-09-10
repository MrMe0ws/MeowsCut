using System.Text.Json;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Probing.Json;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Ffmpeg.Probing;

/// <summary>
/// Чтение характеристик файла через ffprobe.
/// </summary>
public sealed class FfprobeMediaProbe(
    IMediaToolsetProvider toolsetProvider,
    IProcessRunner processRunner,
    ILogger<FfprobeMediaProbe> logger) : IMediaProbe
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new MediaProbeException($"Файл не найден: {filePath}", filePath);
        }

        var toolset = toolsetProvider.Require();

        var request = new ProcessRequest(
            toolset.FfprobePath,
            [
                "-hide_banner",
                "-loglevel", "error",
                "-print_format", "json",
                "-show_format",
                "-show_streams",
                filePath
            ]);

        var (result, output) = await processRunner
            .RunCapturingOutputAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            var details = string.Join(Environment.NewLine, result.StdErrTail);
            logger.LogWarning("ffprobe завершился с кодом {ExitCode} для {Path}: {Details}",
                result.ExitCode, filePath, details);

            throw new MediaProbeException(
                "Не удалось прочитать файл: он повреждён или это не медиафайл.",
                filePath);
        }

        FfprobeResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<FfprobeResponse>(output, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new MediaProbeException("Ответ ffprobe не удалось разобрать.", filePath, ex);
        }

        if (response is null)
        {
            throw new MediaProbeException("ffprobe вернул пустой ответ.", filePath);
        }

        var fileSize = new FileInfo(filePath).Length;
        var mediaInfo = MediaInfoMapper.Map(response, filePath, fileSize);

        // Файл без видео — это музыка или запись голоса для аудиодорожки.
        // Отказываем только тогда, когда в файле нет вообще ничего звучащего или видимого.
        if (!mediaInfo.HasVideo && !mediaInfo.HasAudio)
        {
            throw new UnsupportedMediaException(
                "В файле нет ни видео, ни звука — с ним нечего делать.",
                filePath);
        }

        if (mediaInfo.Duration <= TimeSpan.Zero)
        {
            throw new UnsupportedMediaException(
                "Не удалось определить длительность файла.",
                filePath);
        }

        logger.LogInformation(
            "Файл прочитан: {Name}, {Duration}, {Size}, {Codec}",
            mediaInfo.FileName,
            mediaInfo.Duration,
            mediaInfo.PrimaryVideo?.Size,
            mediaInfo.PrimaryVideo?.CodecName);

        return mediaInfo;
    }
}
