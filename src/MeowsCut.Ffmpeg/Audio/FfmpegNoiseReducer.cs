using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Ffmpeg.Arguments;
using MeowsCut.Ffmpeg.Execution;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Ffmpeg.Audio;

/// <summary>
/// Шумоподавление записи фильтром afftdn, обученным на самой записи.
/// </summary>
/// <remarks>
/// Два прохода: первый слушает запись и ищет самую тихую полсекунды, второй
/// по её профилю убирает шум (<see cref="NoiseProfile"/>). На дубль в минуту
/// оба вместе — доля секунды. Результат пишется в .part и переименовывается
/// только после успеха (правило 10).
/// </remarks>
public sealed class FfmpegNoiseReducer(
    IMediaToolsetProvider toolsetProvider,
    IProcessRunner processRunner,
    ILogger<FfmpegNoiseReducer> logger) : INoiseReducer
{
    public async Task<bool> ReduceAsync(
        string inputPath,
        string outputPath,
        int strengthDb,
        CancellationToken cancellationToken)
    {
        var toolset = toolsetProvider.Current
            ?? throw new FfmpegNotFoundException("FFmpeg не найден.");

        var windows = await MeasureAsync(toolset.FfmpegPath, inputPath, cancellationToken).ConfigureAwait(false);
        var filter = NoiseProfile.BuildFilter(windows, strengthDb);

        if (filter is null)
        {
            logger.LogInformation("В записи {Path} нечего убирать: она тихая до нуля", inputPath);
            return false;
        }

        var partPath = outputPath + ".part";

        var arguments = FfmpegArgumentBuilder.Create()
            .HideBanner()
            .OverwriteOutput()
            .NoStdin()
            .LogLevel("error")
            .Input(inputPath)
            .NoVideo()
            .Option("-af", filter)
            .AudioCodec("pcm_s16le")
            .Format("wav")
            .Output(partPath)
            .Build();

        var result = await processRunner
            .RunAsync(new ProcessRequest(toolset.FfmpegPath, arguments), null, null, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success || !File.Exists(partPath))
        {
            TryDelete(partPath);
            throw new FfmpegExecutionException(
                "Не удалось убрать шум из записи.",
                result.ExitCode,
                result.StdErrTail,
                string.Join(' ', arguments));
        }

        File.Move(partPath, outputPath, overwrite: true);
        logger.LogInformation("Шум убран: {Path}, фильтр {Filter}", outputPath, filter);
        return true;
    }

    private async Task<IReadOnlyList<LoudnessWindow>> MeasureAsync(
        string ffmpegPath,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var arguments = FfmpegArgumentBuilder.Create()
            .HideBanner()
            .NoStdin()

            // info, а не error: на этом уровне ametadata печатает громкость окон.
            .LogLevel("info")
            .Input(inputPath)
            .NoVideo()
            .Option("-af", NoiseProfile.AnalysisFilter)
            .Format("null")
            .Output("NUL")
            .Build();

        var lines = new List<string>();

        var result = await processRunner.RunAsync(
            new ProcessRequest(ffmpegPath, arguments),
            null,
            line =>
            {
                lock (lines)
                {
                    lines.Add(line);
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            throw new FfmpegExecutionException(
                "Не удалось прослушать запись перед шумоподавлением.",
                result.ExitCode,
                result.StdErrTail,
                string.Join(' ', arguments));
        }

        lock (lines)
        {
            return NoiseProfile.Parse(lines);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Не удалось удалить {Path}", path);
        }
    }
}
