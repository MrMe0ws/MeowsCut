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
/// Масштаб и наезд на настоящем ffmpeg: смотрим в сами кадры.
/// </summary>
/// <remarks>
/// Источник — чёрный кадр с белым квадратом 40×40 в середине. Чем крупнее кадр,
/// тем больше белого: по средней яркости видно, насколько он увеличен.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class MotionIntegrationTests : IDisposable
{
    private const int Width = 320;
    private const int Height = 180;

    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public MotionIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task Push_in_starts_at_full_frame_and_ends_zoomed()
    {
        var output = await ExportAsync(
            await ProjectWithAsync(new ClipTransform(3d, 0d, 0d) with { MotionSeconds = 1d }),
            "наезд.mp4");

        var first = await BrightnessAsync(output, 0d);
        var last = await BrightnessAsync(output, 1.6d);

        first.Should().BeLessThan(15, "в начале наезда кадр как есть: квадрат маленький");
        last.Should().BeInRange(45, 85, "к концу масштаб 300%: квадрат 120×120 из 320×180");
    }

    [FfmpegFact]
    public async Task Pull_out_goes_from_zoomed_back_to_full_frame()
    {
        var output = await ExportAsync(
            await ProjectWithAsync(new ClipTransform(3d, 0d, 0d) with { MotionSeconds = 1d, MotionOut = true }),
            "отъезд.mp4");

        (await BrightnessAsync(output, 0d)).Should().BeGreaterThan(45);
        (await BrightnessAsync(output, 1.6d)).Should().BeLessThan(15);
    }

    [FfmpegFact]
    public async Task Zoom_far_beyond_300_percent_exports()
    {
        // 2000%: раньше кадр сначала растягивался целиком — 6400 px на каждый
        // кадр. Теперь вырезается видимая часть, и белый квадрат во весь кадр.
        var output = await ExportAsync(await ProjectWithAsync(new ClipTransform(20d, 0d, 0d)), "2000.mp4");

        (await BrightnessAsync(output, 1d)).Should().BeGreaterThan(200);
        (await ProbeAsync(output)).PrimaryVideo!.DisplaySize.Should().Be(new FrameSize(Width, Height));
    }

    [FfmpegFact]
    public async Task Pull_out_below_full_size_keeps_the_frame_size()
    {
        var output = await ExportAsync(
            await ProjectWithAsync(new ClipTransform(0.5d, 0.1d, 0d) with { MotionSeconds = 1d }),
            "меньше.mp4");

        (await ProbeAsync(output)).PrimaryVideo!.DisplaySize.Should().Be(new FrameSize(Width, Height));
    }

    private async Task<Project> ProjectWithAsync(ClipTransform transform)
    {
        var project = Project.FromMedia(await ProbeAsync(await CreateVideoAsync("квадрат.mp4")));
        var clip = project.Sequence.Video.Clips[0];
        return project.WithSequence(project.Sequence.WithTrack(
            project.Sequence.Video.Replace(clip.WithTransform(transform))));
    }

    /// <summary>Средняя яркость кадра в момент <paramref name="seconds"/>, от 0 до 255.</summary>
    private async Task<double> BrightnessAsync(string video, double seconds)
    {
        var raw = Path.Combine(_workDirectory, Guid.NewGuid().ToString("N") + ".gray");
        await RunFfmpegAsync(
        [
            "-ss", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", video,
            "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "gray", raw
        ]);

        var bytes = await File.ReadAllBytesAsync(raw);
        return bytes.Average(value => (double)value);
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

    private async Task<string> CreateVideoAsync(string fileName)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i",
            $"color=c=black:size={Width}x{Height}:rate=25:duration=3,drawbox=x=140:y=70:w=40:h=40:color=white:t=fill",
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
