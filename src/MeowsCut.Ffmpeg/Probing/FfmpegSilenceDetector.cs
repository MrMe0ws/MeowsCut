using System.Globalization;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Arguments;
using MeowsCut.Ffmpeg.Execution;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Ffmpeg.Probing;

/// <summary>
/// Ищет паузы фильтром silencedetect.
/// </summary>
/// <remarks>
/// Фильтр ничего не пишет в файл — он рассказывает о найденном в stderr
/// строками «silence_start: 12.5» и «silence_end: 15.1 | silence_duration: 2.6».
/// Поэтому вывод идёт в пустоту (-f null), а разбирается поток ошибок.
///
/// Звук читается целиком, но без картинки (-vn): распаковывать кадры ради
/// тишины незачем, и на часовом ролике это разница между секундами и минутами.
/// </remarks>
public sealed class FfmpegSilenceDetector(
    IMediaToolsetProvider toolsetProvider,
    IProcessRunner processRunner,
    ILogger<FfmpegSilenceDetector> logger) : ISilenceDetector
{
    private const string SilenceStart = "silence_start:";
    private const string SilenceEnd = "silence_end:";

    public async Task<IReadOnlyList<TimeRange>> DetectAsync(
        string filePath,
        SilenceOptions options,
        CancellationToken cancellationToken)
    {
        var toolset = toolsetProvider.Current
            ?? throw new FfmpegNotFoundException("FFmpeg не найден.");

        var threshold = options.ThresholdDb.ToString("0.#", CultureInfo.InvariantCulture);
        var minimum = options.EffectiveMinDuration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

        var arguments = FfmpegArgumentBuilder.Create()
            .HideBanner()
            .NoStdin()

            // info, а не error: именно на этом уровне silencedetect рассказывает
            // о найденном, и с привычным error разбирать было бы нечего.
            .LogLevel("info")
            .Input(filePath)
            .NoVideo()
            .Option("-af", $"silencedetect=noise={threshold}dB:d={minimum}")
            .Format("null")
            .Output("NUL")
            .Build();

        var ranges = new List<TimeRange>();
        TimeSpan? started = null;

        var result = await processRunner.RunAsync(
            new ProcessRequest(toolset.FfmpegPath, arguments),
            null,
            line => Parse(line, ref started, ranges),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            logger.LogWarning(
                "Поиск пауз в {Path} завершился с кодом {Code}",
                filePath,
                result.ExitCode);

            throw new FfmpegExecutionException(
                "Не удалось прослушать файл в поисках пауз.",
                result.ExitCode,
                result.StdErrTail,
                string.Join(' ', arguments));
        }

        // Тишина до самого конца файла своего silence_end не получает:
        // ffmpeg просто заканчивает работу. Такой хвост нам не нужен —
        // вырезать его целиком пользователь не просил.
        return ranges;
    }

    private static void Parse(string line, ref TimeSpan? started, List<TimeRange> ranges)
    {
        var startIndex = line.IndexOf(SilenceStart, StringComparison.Ordinal);
        if (startIndex >= 0)
        {
            if (ReadSeconds(line, startIndex + SilenceStart.Length) is { } start)
            {
                started = start;
            }

            return;
        }

        var endIndex = line.IndexOf(SilenceEnd, StringComparison.Ordinal);
        if (endIndex < 0 || started is not { } from)
        {
            return;
        }

        if (ReadSeconds(line, endIndex + SilenceEnd.Length) is { } to && to > from)
        {
            ranges.Add(new TimeRange(from, to));
        }

        started = null;
    }

    /// <summary>
    /// Читает число секунд после метки.
    /// </summary>
    /// <remarks>
    /// Строка выглядит как «[silencedetect @ …] silence_end: 15.1 | silence_duration: 2.6»:
    /// за числом идёт либо конец строки, либо вертикальная черта со следующей парой.
    /// </remarks>
    private static TimeSpan? ReadSeconds(string line, int from)
    {
        var rest = line.AsSpan(from).TrimStart();

        var length = 0;
        while (length < rest.Length && (char.IsAsciiDigit(rest[length]) || rest[length] is '.' or '-' or '+'))
        {
            length++;
        }

        if (length == 0)
        {
            return null;
        }

        return double.TryParse(rest[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(Math.Max(0, seconds))
            : null;
    }
}
