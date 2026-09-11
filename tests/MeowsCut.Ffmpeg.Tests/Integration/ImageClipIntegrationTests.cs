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
using MeowsCut.Ffmpeg.Thumbnails;
using MeowsCut.Ffmpeg.Toolset;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.Ffmpeg.Tests.Integration;

/// <summary>
/// Фотографии в видеоряду на настоящем ffmpeg.
/// </summary>
/// <remarks>
/// Картинка отдаёт один кадр, и всё держится на -loop 1 с ограничением по времени:
/// без -t экспорт не заканчивается никогда, без -loop из неё нечего резать.
/// Ни то, ни другое строкой не проверяется.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class ImageClipIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public ImageClipIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task A_photo_is_recognised_and_gets_a_default_length()
    {
        var info = await ProbeAsync(await CreateImageAsync("фото.png", 800, 600));

        info.IsImage.Should().BeTrue();
        info.Duration.Should().Be(MediaInfo.ImageSourceDuration);

        var clip = Clip.FromSource(MediaSource.FromMedia(info));

        clip.SourceIsImage.Should().BeTrue();
        clip.TimelineDuration.Should().Be(MediaInfo.DefaultImageClipDuration);
    }

    [FfmpegFact]
    public async Task A_photo_alone_becomes_a_video_of_the_set_length()
    {
        var info = await ProbeAsync(await CreateImageAsync("одна.png", 640, 480));
        var project = Project.FromMedia(info);

        // Растягиваем фотографию до восьми секунд — так же, как тянут за край клипа.
        var stretched = project.Sequence.WithTrack(
            project.Sequence.Video.Replace(
                project.Sequence.Video.Clips[0].TrimEnd(TimeSpan.FromSeconds(3))));

        var output = Path.Combine(_workDirectory, "из фото.mp4");
        var result = await RunAsync(project.WithSequence(stretched), Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        (await ProbeAsync(output)).Duration
            .Should().BeCloseTo(TimeSpan.FromSeconds(8), TimeSpan.FromMilliseconds(600));
    }

    [FfmpegFact]
    public async Task A_photo_stands_between_video_clips()
    {
        var video = await CreateVideoAsync();
        var project = Project.FromMedia(await ProbeAsync(video));

        var photo = await ProbeAsync(await CreateImageAsync("вставка.jpg", 1200, 900));
        var (withPhoto, source) = project.WithSource(photo);

        // Фотография другого размера, чем ролик: без приведения к общему размеру
        // concat отказался бы сшивать части.
        var sequence = withPhoto.Sequence.SplitAt(TimeSpan.FromSeconds(2));
        sequence = sequence.WithTrack(sequence.Video.Insert(1, Clip.FromSource(source)));

        var output = Path.Combine(_workDirectory, "ролик с фото.mp4");
        var result = await RunAsync(withPhoto.WithSequence(sequence), Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        var info = await ProbeAsync(output);
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(9), TimeSpan.FromMilliseconds(700));
        info.HasAudio.Should().BeTrue("под фотографией обязана быть тишина, иначе concat разваливается");
    }

    [FfmpegFact]
    public async Task Fast_mode_does_not_try_to_copy_a_photo()
    {
        var info = await ProbeAsync(await CreateImageAsync("быстро.png", 320, 240));
        var project = Project.FromMedia(info);

        var settings = ExportSettings.Default with
        {
            OutputPath = Path.Combine(_workDirectory, "быстро.mp4"),
            PreferStreamCopy = true
        };

        var result = await RunAsync(project, settings);

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        (await ProbeAsync(settings.OutputPath!)).Duration
            .Should().BeCloseTo(MediaInfo.DefaultImageClipDuration, TimeSpan.FromMilliseconds(600));
    }

    [FfmpegFact]
    public async Task A_photo_gives_the_board_its_frame()
    {
        // Полоса кадров у фотографий оставалась пустой: таймлайн просит кадр на нуле,
        // а «-ss 0» перед «-i» выбрасывал единственный кадр изображения — ffmpeg
        // возвращал успех и не создавал файла, и так по кругу на каждой перерисовке.
        var photo = await CreateImageAsync("полоса.jpg", 640, 480);

        var service = new FfmpegThumbnailService(
            new AppPaths(_workDirectory, _workDirectory, _workDirectory),
            CreateToolsetProvider(),
            _runner,
            NullLogger<FfmpegThumbnailService>.Instance);

        var frame = await service.GetFrameAsync(photo, TimeSpan.Zero, 192, CancellationToken.None);

        frame.Should().NotBeNull("без кадра клип фотографии выглядит пустым прямоугольником");
        File.Exists(frame!).Should().BeTrue();
        new FileInfo(frame!).Length.Should().BeGreaterThan(0);
    }

    private static ExportSettings Settings(string outputPath) =>
        ExportSettings.Default with { OutputPath = outputPath, PreferStreamCopy = false };

    private async Task<string> CreateImageAsync(string fileName, int width, int height)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate=1:duration=1",
            "-frames:v", "1", path
        ]);

        return path;
    }

    private async Task<string> CreateVideoAsync()
    {
        var path = Path.Combine(_workDirectory, "ролик.mp4");

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=4",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=4",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "32", "-pix_fmt", "yuv420p",
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
