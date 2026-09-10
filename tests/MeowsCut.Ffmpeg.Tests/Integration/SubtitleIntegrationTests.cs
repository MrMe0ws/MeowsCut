using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Export;
using MeowsCut.Core.Jobs;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Planning;
using MeowsCut.Ffmpeg.Probing;
using MeowsCut.Ffmpeg.Rendering;
using MeowsCut.Ffmpeg.Toolset;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.Ffmpeg.Tests.Integration;

/// <summary>
/// Субтитры на настоящем ffmpeg.
/// </summary>
/// <remarks>
/// Экранирование пути в фильтре проверить иначе нельзя: строка выглядит правильной,
/// а ffmpeg отказывается её разбирать. Путь здесь нарочно с кириллицей и пробелом —
/// именно такие и будут у пользователя.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class SubtitleIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"),
        "субтитры и видео");

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public SubtitleIntegrationTests() => Directory.CreateDirectory(_workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Временные файлы — не повод падать в тесте.
        }
    }

    [FfmpegFact]
    public async Task Subtitles_are_burned_into_the_picture()
    {
        var project = await CreateProjectAsync();
        var subtitles = await CreateSubtitlesAsync("подписи к видео.srt");

        var output = Path.Combine(_workDirectory, "с субтитрами.mp4");
        var settings = Settings(output) with
        {
            Subtitles = new SubtitleSettings
            {
                FilePath = subtitles,
                Mode = SubtitleMode.Burn,
                ForceStyle = SubtitleFormats.DefaultStyle
            }
        };

        var result = await RunAsync(project, settings);

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);
        File.Exists(output).Should().BeTrue();
    }

    [FfmpegFact]
    public async Task Subtitles_can_go_as_a_separate_track()
    {
        var project = await CreateProjectAsync();
        var subtitles = await CreateSubtitlesAsync("дорожка.srt");

        var output = Path.Combine(_workDirectory, "с дорожкой.mp4");
        var settings = Settings(output) with
        {
            Subtitles = new SubtitleSettings { FilePath = subtitles, Mode = SubtitleMode.Embed }
        };

        var result = await RunAsync(project, settings);

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        var info = await ProbeAsync(output);
        info.SubtitleStreams.Should().NotBeEmpty("дорожка должна оказаться в файле");
    }

    private static ExportSettings Settings(string outputPath) =>
        ExportSettings.Default with { OutputPath = outputPath, PreferStreamCopy = false };

    private async Task<string> CreateSubtitlesAsync(string fileName)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await File.WriteAllTextAsync(path,
            """
            1
            00:00:00,500 --> 00:00:02,000
            Проверка субтитров

            2
            00:00:02,000 --> 00:00:03,500
            Вторая строка

            """,
            new System.Text.UTF8Encoding(false));

        return path;
    }

    private async Task<JobResult> RunAsync(Project project, ExportSettings settings)
    {
        var plan = new FfmpegExportPlanner(new AppPaths()).CreatePlan(
            new ExportRequest(project, settings),
            new MediaCapabilities(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        var engine = new FfmpegExportEngine(
            CreateToolsetProvider(),
            _runner,
            NullLogger<FfmpegExportEngine>.Instance);

        return await engine.ExecuteAsync(plan, new Progress<JobProgress>(), CancellationToken.None);
    }

    private async Task<Project> CreateProjectAsync()
    {
        var path = Path.Combine(_workDirectory, "исходник.mp4");

        var result = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!,
            [
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=4",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "32", "-pix_fmt", "yuv420p", path
            ]),
            null,
            null,
            CancellationToken.None);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.StdErrTail));

        return Project.FromMedia(await ProbeAsync(path));
    }

    private async Task<MediaInfo> ProbeAsync(string path) =>
        await new FfprobeMediaProbe(CreateToolsetProvider(), _runner, NullLogger<FfprobeMediaProbe>.Instance)
            .ProbeAsync(path, CancellationToken.None);

    private static IMediaToolsetProvider CreateToolsetProvider()
    {
        var provider = new MediaToolsetProvider();
        provider.Set(new MediaToolset(
            FfmpegTestEnvironment.FfmpegPath!,
            FfmpegTestEnvironment.FfprobePath!,
            "test",
            ToolsetSource.LocalAppData,
            MediaCapabilities.Empty));

        return provider;
    }
}
