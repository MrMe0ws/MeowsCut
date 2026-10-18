using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
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
/// Ролики разного размера встык на настоящем ffmpeg.
/// </summary>
/// <remarks>
/// Строкой графа здесь не обойтись: разъехавшийся размер ломает concat только
/// при настоящем запуске. Без приведения к кадру проекта этот тест падает.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class MixedSizeIntegrationTests : IDisposable
{
    private const int Width = 320;
    private const int Height = 180;

    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public MixedSizeIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task Clips_of_different_size_are_joined_without_gaps()
    {
        // Ролик другого размера вплотную к первому: concat склеивает только
        // одинаковые кадры, и без приведения к кадру проекта экспорт падал.
        var project = Project.FromMedia(await ProbeAsync(await CreateVideoAsync("широкий.mp4", Width, Height)));
        var (withSecond, source) = project.WithSource(await ProbeAsync(await CreateVideoAsync("квадрат.mp4", 240, 240)));
        var sequence = withSecond.Sequence.WithTrack(withSecond.Sequence.Video.Append(Clip.FromSource(source)));

        var output = await ExportAsync(withSecond.WithSequence(sequence), "разные.mp4");

        var info = await ProbeAsync(output);
        info.PrimaryVideo!.DisplaySize.Should().Be(new FrameSize(Width, Height));
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(600));
    }

    private async Task<string> ExportAsync(Project project, string fileName)
    {
        var output = Path.Combine(_workDirectory, fileName);
        var settings = ExportSettings.Default with { OutputPath = output, PreferStreamCopy = false };

        var plan = new FfmpegExportPlanner(new AppPaths()).CreatePlan(
            new ExportRequest(project, settings),
            new MediaCapabilities(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        var engine = new FfmpegExportEngine(CreateToolsetProvider(), _runner, NullLogger<FfmpegExportEngine>.Instance);
        var result = await engine.ExecuteAsync(plan, new Progress<JobProgress>(), CancellationToken.None);

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);
        return output;
    }

    private async Task<string> CreateVideoAsync(string fileName, int width, int height)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", $"color=c=0xC0C0C0:size={width}x{height}:rate=25:duration=3",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
            "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "64k", "-shortest", path
        ]);

        return path;
    }

    private async Task RunFfmpegAsync(string[] arguments)
    {
        string[] full = ["-y", "-hide_banner", "-loglevel", "error", .. arguments];

        var result = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!, full),
            null,
            null,
            CancellationToken.None);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.StdErrTail));
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
