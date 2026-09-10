using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
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
/// Сквозная проверка экспорта на настоящем ffmpeg: план, аргументы, запуск,
/// временный файл, переименование результата, прогресс и отмена.
/// </summary>
[Trait("Category", "RequiresFfmpeg")]
public sealed class ExportIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public ExportIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task Exports_unchanged_video_by_copying_streams()
    {
        var project = await CreateProjectAsync("исходник 1.mp4", seconds: 4);
        var output = Path.Combine(_workDirectory, "копия.mp4");

        var settings = ExportSettings.Default with { OutputPath = output, PreferStreamCopy = true };
        var plan = CreatePlan(project, settings);

        plan.IsStreamCopy.Should().BeTrue();

        var result = await RunAsync(plan);

        result.IsSuccess.Should().BeTrue();
        File.Exists(output).Should().BeTrue();
        File.Exists(output + ".meowscut.part").Should().BeFalse("временный файл должен быть переименован");

        var info = await ProbeAsync(output);
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(400));
    }

    [FfmpegFact]
    public async Task Exports_a_cut_out_fragment_with_correct_duration()
    {
        // Оставить 0–2, вырезать 2–4, оставить 4–6: в результате должно быть 4 секунды.
        var project = await CreateProjectAsync("исходник 2.mp4", seconds: 6);
        var sequence = new RemoveRangeCommand(
                new TimeRangeSelection(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)))
            .Apply(project.Sequence);

        var output = Path.Combine(_workDirectory, "вырезано.mp4");
        var plan = CreatePlan(
            project.WithSequence(sequence),
            ExportSettings.Default with { OutputPath = output });

        plan.IsStreamCopy.Should().BeFalse();
        plan.ExpectedOutputDuration.Should().Be(TimeSpan.FromSeconds(4));

        var result = await RunAsync(plan);
        result.IsSuccess.Should().BeTrue();

        var info = await ProbeAsync(output);
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(400));
        info.HasAudio.Should().BeTrue();
    }

    [FfmpegFact]
    public async Task Speed_change_shortens_the_result_and_keeps_audio()
    {
        var project = await CreateProjectAsync("исходник 3.mp4", seconds: 4);
        var sequence = new SetClipSpeedCommand(project.Sequence.Video.Clips[0].Id, 2d).Apply(project.Sequence);

        var output = Path.Combine(_workDirectory, "ускорено.mp4");
        var plan = CreatePlan(project.WithSequence(sequence), ExportSettings.Default with { OutputPath = output });

        var result = await RunAsync(plan);
        result.IsSuccess.Should().BeTrue();

        var info = await ProbeAsync(output);
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(400));
        info.HasAudio.Should().BeTrue("ускорение не должно терять звук");
    }

    [FfmpegFact]
    public async Task Resolution_and_frame_rate_are_applied()
    {
        var project = await CreateProjectAsync("исходник 4.mp4", seconds: 3, width: 1280, height: 720, fps: 30);
        var output = Path.Combine(_workDirectory, "уменьшено.mp4");

        var settings = ExportSettings.Default with
        {
            OutputPath = output,
            Video = VideoSettings.Default with
            {
                Resolution = new ResolutionSpec.Preset(360),
                FrameRate = new FrameRateSpec.Fixed(24)
            }
        };

        var result = await RunAsync(CreatePlan(project, settings));
        result.IsSuccess.Should().BeTrue();

        var info = await ProbeAsync(output);
        info.PrimaryVideo!.Size.Should().Be(new FrameSize(640, 360));
        info.PrimaryVideo!.FrameRate.Value.Should().BeApproximately(24, 0.1);
    }

    [FfmpegFact]
    public async Task Export_to_webm_uses_vp9_and_opus()
    {
        var project = await CreateProjectAsync("исходник 5.mp4", seconds: 2);
        var output = Path.Combine(_workDirectory, "результат.webm");

        var settings = (ExportSettings.Default with
        {
            OutputPath = output,
            Container = ContainerFormat.WebM,
            Video = VideoSettings.Default with { Speed = EncodingSpeed.VeryFast }
        }).Normalized();

        var result = await RunAsync(CreatePlan(project, settings));
        result.IsSuccess.Should().BeTrue();

        var info = await ProbeAsync(output);
        info.PrimaryVideo!.CodecName.Should().Be("vp9");
        info.PrimaryAudio!.CodecName.Should().Be("opus");
    }

    [FfmpegFact]
    public async Task Progress_reaches_the_end_and_reports_stage_name()
    {
        var project = await CreateProjectAsync("исходник 6.mp4", seconds: 3);
        var output = Path.Combine(_workDirectory, "прогресс.mp4");

        var reports = new List<JobProgress>();
        var progress = new Progress<JobProgress>(reports.Add);

        var result = await RunAsync(CreatePlan(project, ExportSettings.Default with { OutputPath = output }), progress);

        result.IsSuccess.Should().BeTrue();
        reports.Should().NotBeEmpty();
        reports[^1].Percent.Should().Be(100);
        reports.Should().Contain(report => report.StageName.Length > 0);
    }

    [FfmpegFact]
    public async Task Cancellation_kills_ffmpeg_and_leaves_no_files_behind()
    {
        var project = await CreateProjectAsync("исходник 7.mp4", seconds: 20, width: 1280, height: 720);
        var output = Path.Combine(_workDirectory, "отменено.mp4");

        var settings = ExportSettings.Default with
        {
            OutputPath = output,
            Video = VideoSettings.Default with { Speed = EncodingSpeed.Slow }
        };

        using var cancellation = new CancellationTokenSource();
        var engine = CreateEngine();
        var plan = CreatePlan(project, settings);

        var progress = new Progress<JobProgress>(_ => cancellation.Cancel());

        var act = async () => await engine.ExecuteAsync(plan, progress, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        File.Exists(output).Should().BeFalse("незавершённый результат не должен оставаться");
        File.Exists(output + ".meowscut.part").Should().BeFalse("временный файл должен быть удалён");
    }

    [FfmpegFact]
    public async Task Broken_arguments_produce_a_readable_error()
    {
        var project = await CreateProjectAsync("исходник 8.mp4", seconds: 2);
        var output = Path.Combine(_workDirectory, "ошибка.mp4");

        var settings = ExportSettings.Default with
        {
            OutputPath = output,
            Video = VideoSettings.Default with
            {
                Advanced = new AdvancedVideoSettings { RawOptions = new Dictionary<string, string> { ["-crf"] = "-99" } }
            }
        };

        var engine = CreateEngine();
        var plan = CreatePlan(project, settings);

        var act = async () => await engine.ExecuteAsync(plan, new Progress<JobProgress>(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<Core.Diagnostics.FfmpegExecutionException>();
        exception.Which.StdErrTail.Should().NotBeEmpty("для диагностики нужен хвост вывода ffmpeg");
        exception.Which.CommandLine.Should().Contain("ffmpeg");
    }

    private static Core.Processing.ExportPlan CreatePlan(Project project, ExportSettings settings) =>
        new FfmpegExportPlanner(new Core.Configuration.AppPaths())
            .CreatePlan(new ExportRequest(project, settings), Capabilities());

    private static MediaCapabilities Capabilities() => new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264", "libvpx-vp9", "libx265", "libsvtav1" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac", "libopus" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private async Task<JobResult> RunAsync(Core.Processing.ExportPlan plan, IProgress<JobProgress>? progress = null) =>
        await CreateEngine().ExecuteAsync(
            plan,
            progress ?? new Progress<JobProgress>(),
            CancellationToken.None);

    private FfmpegExportEngine CreateEngine() =>
        new(CreateToolsetProvider(), _runner, NullLogger<FfmpegExportEngine>.Instance);

    private async Task<Project> CreateProjectAsync(
        string fileName,
        int seconds,
        int width = 640,
        int height = 360,
        int fps = 25)
    {
        var path = Path.Combine(_workDirectory, fileName);

        var result = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!,
            [
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate={fps}:duration={seconds}",
                "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "30", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "96k", "-shortest", path
            ]),
            null,
            null,
            CancellationToken.None);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.StdErrTail));

        return Project.FromMedia(await ProbeAsync(path));
    }

    private async Task<MediaInfo> ProbeAsync(string path)
    {
        var probe = new FfprobeMediaProbe(CreateToolsetProvider(), _runner, NullLogger<FfprobeMediaProbe>.Instance);
        return await probe.ProbeAsync(path, CancellationToken.None);
    }

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
