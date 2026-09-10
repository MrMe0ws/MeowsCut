using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Execution;

namespace MeowsCut.Ffmpeg.Toolset;

/// <summary>
/// Определяет, что умеет конкретная сборка ffmpeg. Сборки различаются: где-то нет
/// libsvtav1, где-то libvpx. Интерфейс не должен предлагать то, чего нет.
/// </summary>
public sealed class EncoderProbe(IProcessRunner processRunner)
{
    public async Task<MediaCapabilities> ProbeAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        var (encodersResult, encodersOutput) = await processRunner
            .RunCapturingOutputAsync(new ProcessRequest(ffmpegPath, ["-hide_banner", "-encoders"]), cancellationToken)
            .ConfigureAwait(false);

        if (!encodersResult.Success)
        {
            return MediaCapabilities.Empty;
        }

        var video = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var audio = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ParseEncoders(encodersOutput, video, audio);

        var (accelResult, accelOutput) = await processRunner
            .RunCapturingOutputAsync(new ProcessRequest(ffmpegPath, ["-hide_banner", "-hwaccels"]), cancellationToken)
            .ConfigureAwait(false);

        var accelerators = accelResult.Success
            ? ParseHardwareAccelerators(accelOutput)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var (filtersResult, filtersOutput) = await processRunner
            .RunCapturingOutputAsync(new ProcessRequest(ffmpegPath, ["-hide_banner", "-filters"]), cancellationToken)
            .ConfigureAwait(false);

        var filters = filtersResult.Success
            ? ParseFilters(filtersOutput)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new MediaCapabilities(video, audio, accelerators) { Filters = filters };
    }

    /// <summary>
    /// Формат строк ffmpeg -filters: " T.. rubberband  A-&gt;A  Apply time-stretching...".
    /// Имя идёт третьим полем после флагов.
    /// </summary>
    private static HashSet<string> ParseFilters(string output)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 6 || !line.StartsWith(' '))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            result.Add(parts[1]);
        }

        return result;
    }

    /// <summary>
    /// Формат строк ffmpeg -encoders: " V....D libx264   H.264 ...".
    /// Первый символ флагов — тип потока, дальше имя энкодера.
    /// </summary>
    private static void ParseEncoders(string output, ISet<string> video, ISet<string> audio)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 9 || line.StartsWith(" ---", StringComparison.Ordinal))
            {
                continue;
            }

            var flags = line[1..7];
            if (flags.Length < 6 || line[7] != ' ')
            {
                continue;
            }

            var rest = line[8..].TrimStart();
            var space = rest.IndexOf(' ');
            var name = space > 0 ? rest[..space] : rest;

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            switch (flags[0])
            {
                case 'V':
                    video.Add(name);
                    break;
                case 'A':
                    audio.Add(name);
                    break;
            }
        }
    }

    private static HashSet<string> ParseHardwareAccelerators(string output)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t');
            if (string.IsNullOrEmpty(line) ||
                line.StartsWith("Hardware acceleration", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(line);
        }

        return result;
    }
}
