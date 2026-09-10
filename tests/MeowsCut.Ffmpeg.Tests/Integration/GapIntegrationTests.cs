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
/// Пустое место между клипами на настоящем ffmpeg.
/// </summary>
/// <remarks>
/// Чёрная вставка рождается прямо в графе фильтров, и concat принимает её только
/// при точном совпадении размера, формата пикселя и SAR. Проверить это строкой
/// нельзя: граф выглядит правильным, а ffmpeg отказывается его собирать.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class GapIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public GapIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task A_gap_becomes_black_video_of_its_length()
    {
        var project = await CreateSplitProjectAsync();

        // Отодвигаем вторую половину на две секунды: между кусками пустота.
        var sequence = project.Sequence;
        var moved = sequence.WithTrack(
            sequence.Video.MoveInTime(sequence.Video.Clips[1].Id, TimeSpan.FromSeconds(5)));

        var output = Path.Combine(_workDirectory, "с зазором.mp4");
        var result = await RunAsync(project.WithSequence(moved), Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        var info = await ProbeAsync(output);
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(8), TimeSpan.FromMilliseconds(600),
            "три секунды первого куска, две пустоты и три второго");
    }

    [FfmpegFact]
    public async Task A_gap_keeps_the_sound_going()
    {
        var project = await CreateSplitProjectAsync();

        var sequence = project.Sequence;
        var moved = sequence.WithTrack(
            sequence.Video.MoveInTime(sequence.Video.Clips[1].Id, TimeSpan.FromSeconds(4)));

        var output = Path.Combine(_workDirectory, "зазор со звуком.mp4");
        var result = await RunAsync(project.WithSequence(moved), Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        // В зазоре тишина, но дорожка обязана остаться непрерывной: иначе concat
        // получил бы разное число потоков у частей и развалился.
        (await ProbeAsync(output)).HasAudio.Should().BeTrue();
    }

    [FfmpegFact]
    public async Task Fast_mode_does_not_swallow_the_gap()
    {
        var project = await CreateSplitProjectAsync();

        var sequence = project.Sequence;
        var moved = sequence.WithTrack(
            sequence.Video.MoveInTime(sequence.Video.Clips[1].Id, TimeSpan.FromSeconds(5)));

        // Быстрая склейка о пустоте не знает: планировщик обязан отказаться от неё сам.
        var settings = ExportSettings.Default with
        {
            OutputPath = Path.Combine(_workDirectory, "быстро.mp4"),
            PreferStreamCopy = true
        };

        var result = await RunAsync(project.WithSequence(moved), settings);

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        (await ProbeAsync(settings.OutputPath!)).Duration
            .Should().BeCloseTo(TimeSpan.FromSeconds(8), TimeSpan.FromMilliseconds(600));
    }

    private static ExportSettings Settings(string outputPath) =>
        ExportSettings.Default with { OutputPath = outputPath, PreferStreamCopy = false };

    /// <summary>Шестисекундное видео, разрезанное пополам: два куска по три секунды.</summary>
    private async Task<Project> CreateSplitProjectAsync()
    {
        var path = Path.Combine(_workDirectory, "видео.mp4");

        string[] arguments =
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=6",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=6",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "32", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "64k", "-shortest", path
        ];

        var made = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!, arguments),
            null,
            null,
            CancellationToken.None);

        made.Success.Should().BeTrue(string.Join(Environment.NewLine, made.StdErrTail));

        var project = Project.FromMedia(await ProbeAsync(path));

        return project.WithSequence(project.Sequence.SplitAt(TimeSpan.FromSeconds(3)));
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
