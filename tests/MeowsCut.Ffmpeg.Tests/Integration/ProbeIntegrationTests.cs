using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Probing;
using MeowsCut.Ffmpeg.Toolset;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.Ffmpeg.Tests.Integration;

/// <summary>
/// Проверка всей цепочки на настоящем ffmpeg: запуск процесса, чтение JSON, маппинг.
/// Имя файла специально с пробелом и кириллицей — это ежедневная реальность на этой машине.
/// </summary>
[Trait("Category", "RequiresFfmpeg")]
public sealed class ProbeIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    public ProbeIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task Probes_real_file_with_spaces_and_cyrillic_in_path()
    {
        var clipPath = await CreateClipAsync("тестовый клип 1.mp4", seconds: 3, width: 640, height: 360, fps: 25);

        var probe = CreateProbe();
        var info = await probe.ProbeAsync(clipPath, CancellationToken.None);

        info.FileName.Should().Be("тестовый клип 1.mp4");
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(200));
        info.PrimaryVideo!.Size.Should().Be(new FrameSize(640, 360));
        info.PrimaryVideo!.FrameRate.Value.Should().BeApproximately(25, 0.01);
        info.PrimaryVideo!.CodecName.Should().Be("h264");
        info.HasAudio.Should().BeTrue();
        info.FileSizeBytes.Should().BeGreaterThan(0);
    }

    [FfmpegFact]
    public async Task Reports_unsupported_media_for_audio_only_file()
    {
        var audioPath = Path.Combine(_workDirectory, "звук.m4a");
        await RunFfmpegAsync(
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-c:a", "aac", audioPath
        ]);

        var probe = CreateProbe();

        var act = async () => await probe.ProbeAsync(audioPath, CancellationToken.None);

        await act.Should().ThrowAsync<UnsupportedMediaException>();
    }

    [FfmpegFact]
    public async Task Reports_probe_failure_for_broken_file()
    {
        var brokenPath = Path.Combine(_workDirectory, "битый файл.mp4");
        await File.WriteAllTextAsync(brokenPath, "это не видео");

        var probe = CreateProbe();

        var act = async () => await probe.ProbeAsync(brokenPath, CancellationToken.None);

        await act.Should().ThrowAsync<MediaProbeException>();
    }

    [FfmpegFact]
    public async Task Encoder_probe_reports_expected_encoders()
    {
        var capabilities = await new EncoderProbe(new ProcessRunner(NullLogger<ProcessRunner>.Instance))
            .ProbeAsync(FfmpegTestEnvironment.FfmpegPath!, CancellationToken.None);

        capabilities.HasVideoEncoder("libx264").Should().BeTrue();
        capabilities.HasAudioEncoder("aac").Should().BeTrue();
    }

    private async Task<string> CreateClipAsync(string fileName, int seconds, int width, int height, int fps)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await RunFfmpegAsync(
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate={fps}:duration={seconds}",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "30", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "96k",
            "-shortest", path
        ]);

        return path;
    }

    private static async Task RunFfmpegAsync(IReadOnlyList<string> arguments)
    {
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var result = await runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!, arguments),
            onStandardOutputLine: null,
            onStandardErrorLine: null,
            CancellationToken.None);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.StdErrTail));
    }

    private static FfprobeMediaProbe CreateProbe()
    {
        var provider = new MediaToolsetProvider();
        provider.Set(new MediaToolset(
            FfmpegTestEnvironment.FfmpegPath!,
            FfmpegTestEnvironment.FfprobePath!,
            "test",
            ToolsetSource.LocalAppData,
            MediaCapabilities.Empty));

        return new FfprobeMediaProbe(
            provider,
            new ProcessRunner(NullLogger<ProcessRunner>.Instance),
            NullLogger<FfprobeMediaProbe>.Instance);
    }
}
