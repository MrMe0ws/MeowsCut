using System.Text.RegularExpressions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Ffmpeg.Execution;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Ffmpeg.Toolset;

/// <summary>
/// Поиск ffmpeg и ffprobe. Порядок описан в docs/03-FFMPEG-LAYER.md и намеренно
/// ставит настройки пользователя выше поставляемой сборки: если человек указал свою,
/// значит, она ему зачем-то нужна.
/// </summary>
public sealed partial class FfmpegToolsetLocator(
    AppPaths paths,
    IAppSettingsStore settingsStore,
    IProcessRunner processRunner,
    EncoderProbe encoderProbe,
    ILogger<FfmpegToolsetLocator> logger) : IMediaToolsetLocator
{
    private const string FfmpegExecutable = "ffmpeg.exe";
    private const string FfprobeExecutable = "ffprobe.exe";

    public async Task<ToolsetLocationResult> LocateAsync(CancellationToken cancellationToken)
    {
        foreach (var candidate in EnumerateCandidates())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(candidate.Directory) || !Directory.Exists(candidate.Directory))
            {
                continue;
            }

            var ffmpegPath = Path.Combine(candidate.Directory, FfmpegExecutable);
            var ffprobePath = Path.Combine(candidate.Directory, FfprobeExecutable);

            if (!File.Exists(ffmpegPath) || !File.Exists(ffprobePath))
            {
                continue;
            }

            var toolset = await TryCreateToolsetAsync(ffmpegPath, ffprobePath, candidate.Source, cancellationToken)
                .ConfigureAwait(false);

            if (toolset is not null)
            {
                logger.LogInformation(
                    "FFmpeg найден: {Path} (версия {Version}, источник {Source})",
                    toolset.FfmpegPath,
                    toolset.Version,
                    toolset.Source);

                return ToolsetLocationResult.Success(toolset);
            }
        }

        logger.LogWarning("FFmpeg не найден ни в одном из известных мест");
        return ToolsetLocationResult.Failure("Не удалось найти ffmpeg.exe и ffprobe.exe");
    }

    public async Task<ToolsetLocationResult> ValidateDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        var ffmpegPath = Path.Combine(directory, FfmpegExecutable);
        var ffprobePath = Path.Combine(directory, FfprobeExecutable);

        if (!File.Exists(ffmpegPath) || !File.Exists(ffprobePath))
        {
            return ToolsetLocationResult.Failure("В указанной папке нет ffmpeg.exe и ffprobe.exe");
        }

        var toolset = await TryCreateToolsetAsync(ffmpegPath, ffprobePath, ToolsetSource.UserSettings, cancellationToken)
            .ConfigureAwait(false);

        return toolset is not null
            ? ToolsetLocationResult.Success(toolset)
            : ToolsetLocationResult.Failure("Найденные файлы не удалось запустить как ffmpeg");
    }

    private IEnumerable<(string? Directory, ToolsetSource Source)> EnumerateCandidates()
    {
        yield return (settingsStore.Current.FfmpegDirectory, ToolsetSource.UserSettings);
        yield return (paths.BundledFfmpegDirectory, ToolsetSource.ApplicationFolder);
        yield return (paths.LocalFfmpegDirectory, ToolsetSource.LocalAppData);
        yield return (Environment.GetEnvironmentVariable("MEOWSCUT_FFMPEG_DIR"), ToolsetSource.EnvironmentVariable);

        foreach (var directory in EnumeratePathDirectories())
        {
            yield return (directory, ToolsetSource.SystemPath);
        }

        foreach (var directory in EnumerateWellKnownDirectories())
        {
            yield return (directory, ToolsetSource.WellKnownLocation);
        }
    }

    private static IEnumerable<string> EnumeratePathDirectories()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            yield break;
        }

        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return entry.Trim('"');
        }
    }

    private static IEnumerable<string> EnumerateWellKnownDirectories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links");
        yield return Path.Combine(userProfile, "scoop", "shims");
        yield return @"C:\ProgramData\chocolatey\bin";
        yield return @"C:\ffmpeg\bin";
    }

    private async Task<MediaToolset?> TryCreateToolsetAsync(
        string ffmpegPath,
        string ffprobePath,
        ToolsetSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            var version = await ReadVersionAsync(ffmpegPath, cancellationToken).ConfigureAwait(false);
            if (version is null)
            {
                return null;
            }

            var probeVersion = await ReadVersionAsync(ffprobePath, cancellationToken).ConfigureAwait(false);
            if (probeVersion is null)
            {
                return null;
            }

            var capabilities = await encoderProbe.ProbeAsync(ffmpegPath, cancellationToken).ConfigureAwait(false);

            return new MediaToolset(ffmpegPath, ffprobePath, version, source, capabilities);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Кандидат {Path} не подошёл", ffmpegPath);
            return null;
        }
    }

    private async Task<string?> ReadVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        var request = new ProcessRequest(executablePath, ["-hide_banner", "-version"]);
        var (result, output) = await processRunner.RunCapturingOutputAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return null;
        }

        var match = VersionRegex().Match(output);
        return match.Success ? match.Groups["version"].Value : null;
    }

    [GeneratedRegex(@"version\s+(?<version>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();
}
