using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Export;
using MeowsCut.Core.Jobs;
using MeowsCut.Core.Media;
using MeowsCut.Core.Presets;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Planning;
using MeowsCut.Ffmpeg.Probing;
using MeowsCut.Ffmpeg.Rendering;
using MeowsCut.Ffmpeg.Toolset;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.Ffmpeg.Tests.Integration;

/// <summary>
/// Главная проверка восьмого этапа: из обычного ролика получается файл,
/// удовлетворяющий всем требованиям Telegram к видеостикеру.
/// </summary>
[Trait("Category", "RequiresFfmpeg")]
public sealed class StickerIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    private readonly IPresetProvider _presets =
        new JsonPresetProvider(new AppPaths(), NullLogger<JsonPresetProvider>.Instance);

    private readonly PresetApplier _applier = new(new PresetValidator());

    public StickerIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task Sticker_preset_turns_a_normal_clip_into_a_valid_sticker()
    {
        // Исходник специально «неудобный»: горизонтальный, длинный, со звуком.
        var project = await CreateProjectAsync("исходник стикера.mp4", seconds: 8, width: 640, height: 360);
        var preset = _presets.Find("telegram.video-sticker")!;

        var applied = _applier.Apply(preset, project, ExportSettings.Default);

        applied.Validation.IsSatisfied.Should().BeTrue("после применения пресета требования должны выполняться");

        var output = Path.Combine(_workDirectory, "стикер.webm");
        var settings = applied.Settings with { OutputPath = output };

        var result = await RunAsync(applied.Project, settings);
        result.IsSuccess.Should().BeTrue();

        var info = await ProbeAsync(output);

        info.PrimaryVideo!.Size.Should().Be(new FrameSize(512, 512), "стикер обязан быть квадратным");
        info.PrimaryVideo!.CodecName.Should().Be("vp9");
        info.Duration.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(3.2), "лимит — три секунды");
        info.HasAudio.Should().BeFalse("звук в стикерах не воспроизводится");

        new FileInfo(output).Length.Should().BeLessThan(256_000, "иначе Telegram файл не примет");
    }

    [FfmpegFact]
    public async Task Speeding_up_keeps_the_whole_story_inside_the_limit()
    {
        var project = await CreateProjectAsync("ускоренный стикер.mp4", seconds: 9, width: 480, height: 480);
        var preset = _presets.Find("telegram.video-sticker")!;

        var applied = _applier.Apply(preset, project, ExportSettings.Default, DurationFitMode.SpeedUp);

        // Содержание сохраняется целиком, просто идёт втрое быстрее.
        applied.Project.Sequence.Video.Clips[0].Speed.Should().BeApproximately(3d, 0.05);

        var output = Path.Combine(_workDirectory, "быстрый-стикер.webm");
        var result = await RunAsync(applied.Project, applied.Settings with { OutputPath = output });

        result.IsSuccess.Should().BeTrue();

        var info = await ProbeAsync(output);
        info.Duration.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(3.2));
        info.PrimaryVideo!.Size.Should().Be(new FrameSize(512, 512));
    }

    [FfmpegFact]
    public async Task Frame_zoom_reaches_the_result()
    {
        var project = await CreateProjectAsync("масштаб.mp4", seconds: 2, width: 640, height: 360);

        var zoomed = project.Sequence.WithTrack(
            project.Sequence.Video.Replace(
                project.Sequence.Video.Clips[0].WithTransform(new Core.Editing.Timeline.ClipTransform(1.6, 0.1, 0))));

        var preset = _presets.Find("telegram.video-sticker")!;
        var applied = _applier.Apply(preset, project.WithSequence(zoomed), ExportSettings.Default);

        var output = Path.Combine(_workDirectory, "с-масштабом.webm");
        var result = await RunAsync(applied.Project, applied.Settings with { OutputPath = output });

        result.IsSuccess.Should().BeTrue("масштаб кадра не должен ломать граф фильтров");
        (await ProbeAsync(output)).PrimaryVideo!.Size.Should().Be(new FrameSize(512, 512));
    }

    [FfmpegFact]
    public async Task Video_message_preset_produces_a_square_mp4_with_sound()
    {
        var project = await CreateProjectAsync("кружок.mp4", seconds: 4, width: 640, height: 360);
        var preset = _presets.Find("telegram.video-message")!;

        var applied = _applier.Apply(preset, project, ExportSettings.Default);
        var output = Path.Combine(_workDirectory, "кружок-готов.mp4");

        var result = await RunAsync(applied.Project, applied.Settings with { OutputPath = output });
        result.IsSuccess.Should().BeTrue();

        var info = await ProbeAsync(output);
        info.PrimaryVideo!.Size.Should().Be(new FrameSize(640, 640));
        info.PrimaryVideo!.CodecName.Should().Be("h264");
        info.HasAudio.Should().BeTrue();
        info.PrimaryAudio!.Channels.Should().Be(1);
    }

    private async Task<JobResult> RunAsync(Project project, ExportSettings settings)
    {
        var plan = new FfmpegExportPlanner(new AppPaths()).CreatePlan(
            new ExportRequest(project, settings),
            new MediaCapabilities(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264", "libvpx-vp9" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac", "libopus" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        var engine = new FfmpegExportEngine(
            CreateToolsetProvider(),
            _runner,
            NullLogger<FfmpegExportEngine>.Instance);

        return await engine.ExecuteAsync(plan, new Progress<JobProgress>(), CancellationToken.None);
    }

    private async Task<Project> CreateProjectAsync(string fileName, int seconds, int width, int height)
    {
        var path = Path.Combine(_workDirectory, fileName);

        var result = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!,
            [
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate=25:duration={seconds}",
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
