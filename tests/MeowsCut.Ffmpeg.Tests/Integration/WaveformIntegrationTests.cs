using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Thumbnails;
using MeowsCut.Ffmpeg.Toolset;
using MeowsCut.Core.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.Ffmpeg.Tests.Integration;

/// <summary>
/// Картинка звуковой волны на настоящем ffmpeg.
/// </summary>
/// <remarks>
/// Фильтр showwavespic собирается в -filter_complex, и ошибку в его записи
/// строкой не поймать: ffmpeg просто ничего не выведет, а доска молча останется
/// без волны. Поэтому здесь настоящий файл и проверка результата на диске.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class WaveformIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public WaveformIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task A_waveform_is_rendered_for_an_audio_file()
    {
        var source = await CreateAudioAsync("музыка и тишина.m4a");
        var service = CreateService();

        var path = await service.GetWaveformAsync(source, CancellationToken.None);

        path.Should().NotBeNull();
        File.Exists(path!).Should().BeTrue();
        new FileInfo(path!).Length.Should().BeGreaterThan(0);
    }

    [FfmpegFact]
    public async Task The_second_request_reuses_the_cached_file()
    {
        var source = await CreateAudioAsync("повтор.m4a");
        var service = CreateService();

        var first = await service.GetWaveformAsync(source, CancellationToken.None);
        var written = File.GetLastWriteTimeUtc(first!);

        var second = await service.GetWaveformAsync(source, CancellationToken.None);

        second.Should().Be(first);
        File.GetLastWriteTimeUtc(second!).Should().Be(written, "проход ffmpeg по файлу не должен повторяться");
    }

    [FfmpegFact]
    public async Task A_file_without_sound_gives_no_waveform()
    {
        var path = Path.Combine(_workDirectory, "без звука.mp4");

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=2",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "35", "-pix_fmt", "yuv420p", path
        ]);

        // Не исключение и не пустой файл: доска обязана просто нарисовать кусок без волны.
        (await CreateService().GetWaveformAsync(path, CancellationToken.None)).Should().BeNull();
    }

    private FfmpegWaveformService CreateService() =>
        new(new AppPaths(_workDirectory, _workDirectory, _workDirectory),
            CreateToolsetProvider(),
            _runner,
            NullLogger<FfmpegWaveformService>.Instance);

    private async Task<string> CreateAudioAsync(string fileName)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
            "-c:a", "aac", "-b:a", "96k", path
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
