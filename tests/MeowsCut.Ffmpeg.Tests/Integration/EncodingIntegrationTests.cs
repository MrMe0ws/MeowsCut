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
/// Параметры кодирования на настоящем ffmpeg: скорость, звук, два прохода.
/// Проверяется именно результат — то, что ffprobe видит в готовом файле.
/// </summary>
[Trait("Category", "RequiresFfmpeg")]
public sealed class EncodingIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public EncodingIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task Quadruple_speed_keeps_audio_in_sync_with_video()
    {
        var project = await CreateProjectAsync("скорость 4x.mp4", seconds: 8);
        var sequence = new SetClipSpeedCommand(project.Sequence.Video.Clips[0].Id, 4d).Apply(project.Sequence);

        var output = Path.Combine(_workDirectory, "быстро.mp4");
        var result = await RunAsync(project.WithSequence(sequence), Settings(output));

        result.IsSuccess.Should().BeTrue();

        var info = await ProbeAsync(output);
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(300));

        // Рассинхрон проявляется именно так: дорожки разъезжаются по длине.
        var video = info.PrimaryVideo!.Duration;
        var audio = info.PrimaryAudio!.Duration;
        Math.Abs((video - audio).TotalMilliseconds).Should().BeLessThan(250);
    }

    [FfmpegFact]
    public async Task Quarter_speed_stretches_the_result()
    {
        var project = await CreateProjectAsync("скорость 025x.mp4", seconds: 2);
        var sequence = new SetClipSpeedCommand(project.Sequence.Video.Clips[0].Id, 0.25).Apply(project.Sequence);

        var output = Path.Combine(_workDirectory, "медленно.mp4");
        var result = await RunAsync(project.WithSequence(sequence), Settings(output));

        result.IsSuccess.Should().BeTrue();

        var info = await ProbeAsync(output);
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(8), TimeSpan.FromMilliseconds(400));
        info.HasAudio.Should().BeTrue("замедление вчетверо раскладывается в две ступени atempo");
    }

    [FfmpegFact]
    public async Task Audio_settings_reach_the_result()
    {
        var project = await CreateProjectAsync("звук.mp4", seconds: 3);
        var output = Path.Combine(_workDirectory, "звук-настроен.mp4");

        var settings = Settings(output) with
        {
            Audio = AudioSettings.Default with
            {
                BitrateKbps = 96,
                SampleRateHz = 44_100,
                Channels = 1
            }
        };

        var result = await RunAsync(project, settings);
        result.IsSuccess.Should().BeTrue();

        var audio = (await ProbeAsync(output)).PrimaryAudio!;
        audio.SampleRateHz.Should().Be(44_100);
        audio.Channels.Should().Be(1);
        audio.CodecName.Should().Be("aac");
    }

    [FfmpegFact]
    public async Task Two_pass_encoding_produces_a_file_and_cleans_up_after_itself()
    {
        var project = await CreateProjectAsync("два прохода.mp4", seconds: 3);
        var output = Path.Combine(_workDirectory, "двухпроходный.mp4");

        var settings = Settings(output) with
        {
            Video = VideoSettings.Default with
            {
                Speed = EncodingSpeed.VeryFast,
                RateControl = new RateControl.ConstantBitrate(800),
                Advanced = new AdvancedVideoSettings { TwoPass = true }
            }
        };

        var plan = CreatePlan(project, settings);
        plan.Stages.Should().HaveCount(2);

        var stages = new List<string>();
        var progress = new Progress<JobProgress>(report => stages.Add(report.StageName));

        var result = await CreateEngine().ExecuteAsync(plan, progress, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        File.Exists(output).Should().BeTrue();

        foreach (var leftover in plan.CleanupPaths)
        {
            File.Exists(leftover).Should().BeFalse($"лог прохода {leftover} должен быть убран");
        }

        stages.Should().Contain(name => name.Contains("1 из 2", StringComparison.Ordinal));
    }

    [FfmpegFact]
    public async Task Target_size_lands_close_to_what_was_asked()
    {
        var project = await CreateProjectAsync("размер.mp4", seconds: 6, width: 640, height: 360);
        var output = Path.Combine(_workDirectory, "по-размеру.mp4");

        const long target = 900 * 1024;

        var settings = Settings(output) with
        {
            Video = VideoSettings.Default with
            {
                Speed = EncodingSpeed.VeryFast,
                RateControl = new RateControl.TargetSize(target)
            }
        };

        var result = await RunAsync(project, settings);
        result.IsSuccess.Should().BeTrue();

        var actual = new FileInfo(output).Length;

        actual.Should().BeLessThan(target, "смысл режима именно в том, чтобы уложиться");
        actual.Should().BeGreaterThan((long)(target * 0.5), "и при этом не тратить лимит впустую");
    }

    [FfmpegFact]
    public async Task Custom_resolution_with_cover_crops_to_a_square()
    {
        var project = await CreateProjectAsync("квадрат.mp4", seconds: 2, width: 640, height: 360);
        var output = Path.Combine(_workDirectory, "квадратный.mp4");

        var settings = Settings(output) with
        {
            Video = VideoSettings.Default with
            {
                Speed = EncodingSpeed.VeryFast,
                Resolution = new ResolutionSpec.Custom(512, 512, FitMode.Cover)
            }
        };

        var result = await RunAsync(project, settings);
        result.IsSuccess.Should().BeTrue();

        (await ProbeAsync(output)).PrimaryVideo!.Size.Should().Be(new FrameSize(512, 512));
    }

    private static ExportSettings Settings(string output) => ExportSettings.Default with
    {
        OutputPath = output,
        Video = VideoSettings.Default with { Speed = EncodingSpeed.VeryFast }
    };

    private static Core.Processing.ExportPlan CreatePlan(Project project, ExportSettings settings) =>
        new FfmpegExportPlanner(new AppPaths())
            .CreatePlan(new ExportRequest(project, settings), Capabilities());

    private static MediaCapabilities Capabilities() => new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264", "libvpx-vp9" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac", "libopus" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private async Task<JobResult> RunAsync(Project project, ExportSettings settings) =>
        await CreateEngine().ExecuteAsync(
            CreatePlan(project, settings),
            new Progress<JobProgress>(),
            CancellationToken.None);

    private FfmpegExportEngine CreateEngine() =>
        new(CreateToolsetProvider(), _runner, NullLogger<FfmpegExportEngine>.Instance);

    private async Task<Project> CreateProjectAsync(
        string fileName,
        int seconds,
        int width = 480,
        int height = 270,
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
