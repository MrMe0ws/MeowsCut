using MeowsCut.Core.Configuration;

namespace MeowsCut.Ffmpeg.Tests.Integration;

/// <summary>
/// Тест, которому нужен настоящий ffmpeg. На машине без него — пропускается,
/// а не падает: обычный прогон тестов не должен требовать установленных бинарников.
/// </summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (FfmpegTestEnvironment.FfmpegPath is null)
        {
            Skip = "FFmpeg не найден: интеграционный тест пропущен";
        }
    }
}

internal static class FfmpegTestEnvironment
{
    static FfmpegTestEnvironment()
    {
        var paths = new AppPaths();
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("MEOWSCUT_FFMPEG_DIR"),
            paths.LocalFfmpegDirectory,
            paths.BundledFfmpegDirectory
        };

        foreach (var directory in candidates)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var ffmpeg = Path.Combine(directory, "ffmpeg.exe");
            var ffprobe = Path.Combine(directory, "ffprobe.exe");

            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
            {
                FfmpegPath = ffmpeg;
                FfprobePath = ffprobe;
                return;
            }
        }
    }

    public static string? FfmpegPath { get; }

    public static string? FfprobePath { get; }
}
