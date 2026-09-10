using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
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
/// Быстрая склейка на настоящем ffmpeg: куски вырезаются копированием потоков
/// и соединяются демультиплексором concat.
/// </summary>
[Trait("Category", "RequiresFfmpeg")]
public sealed class ConcatIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public ConcatIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task Fast_join_produces_a_playable_file_of_the_right_length()
    {
        // Из десяти секунд вырезаем середину: остаётся примерно восемь.
        var project = await CreateProjectAsync("склейка.mp4", seconds: 10);
        var sequence = new RemoveRangeCommand(
                new TimeRangeSelection(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)))
            .Apply(project.Sequence);

        var output = Path.Combine(_workDirectory, "склеено.mp4");
        var plan = CreatePlan(project.WithSequence(sequence), ExportSettings.Default with
        {
            OutputPath = output,
            PreferStreamCopy = true
        });

        plan.Stages.Should().HaveCount(3);

        var result = await CreateEngine().ExecuteAsync(plan, new Progress<JobProgress>(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        File.Exists(output).Should().BeTrue();

        var info = await ProbeAsync(output);

        // Границы прилипают к ключевым кадрам, поэтому допуск здесь больше обычного.
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1.5));
        info.HasAudio.Should().BeTrue();
        info.PrimaryVideo!.CodecName.Should().Be("h264", "перекодирования быть не должно");
    }

    [FfmpegFact]
    public async Task Fast_join_cleans_up_its_pieces()
    {
        var project = await CreateProjectAsync("уборка.mp4", seconds: 6);
        var sequence = new RemoveRangeCommand(
                new TimeRangeSelection(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)))
            .Apply(project.Sequence);

        var output = Path.Combine(_workDirectory, "убрано.mp4");
        var plan = CreatePlan(project.WithSequence(sequence), ExportSettings.Default with
        {
            OutputPath = output,
            PreferStreamCopy = true
        });

        await CreateEngine().ExecuteAsync(plan, new Progress<JobProgress>(), CancellationToken.None);

        foreach (var leftover in plan.CleanupPaths)
        {
            File.Exists(leftover).Should().BeFalse($"файл {leftover} должен быть убран");
            Directory.Exists(leftover).Should().BeFalse($"папка {leftover} должна быть убрана");
        }
    }

    [FfmpegFact]
    public async Task Fast_join_reports_progress_through_all_stages()
    {
        var project = await CreateProjectAsync("прогресс склейки.mp4", seconds: 6);
        var sequence = new RemoveRangeCommand(
                new TimeRangeSelection(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)))
            .Apply(project.Sequence);

        var output = Path.Combine(_workDirectory, "прогресс.mp4");
        var plan = CreatePlan(project.WithSequence(sequence), ExportSettings.Default with
        {
            OutputPath = output,
            PreferStreamCopy = true
        });

        var reports = new List<JobProgress>();
        await CreateEngine().ExecuteAsync(plan, new Progress<JobProgress>(reports.Add), CancellationToken.None);

        reports.Should().NotBeEmpty();
        reports[^1].Percent.Should().Be(100);
        reports.Select(r => r.StageIndex).Distinct().Should().HaveCountGreaterThan(1,
            "прогресс должен идти по всем стадиям, а не замирать на первой");
    }

    private static Core.Processing.ExportPlan CreatePlan(Project project, ExportSettings settings) =>
        new FfmpegExportPlanner(new AppPaths()).CreatePlan(
            new ExportRequest(project, settings),
            new MediaCapabilities(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

    private FfmpegExportEngine CreateEngine() =>
        new(CreateToolsetProvider(), _runner, NullLogger<FfmpegExportEngine>.Instance);

    private async Task<Project> CreateProjectAsync(string fileName, int seconds)
    {
        var path = Path.Combine(_workDirectory, fileName);

        var result = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!,
            [
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", $"testsrc2=size=480x270:rate=25:duration={seconds}",
                "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "30", "-pix_fmt", "yuv420p",
                "-g", "25", "-c:a", "aac", "-b:a", "96k", "-shortest", path
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
