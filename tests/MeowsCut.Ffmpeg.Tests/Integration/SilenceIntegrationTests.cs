using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Probing;
using MeowsCut.Ffmpeg.Toolset;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.Ffmpeg.Tests.Integration;

/// <summary>
/// Поиск пауз на настоящем ffmpeg.
/// </summary>
/// <remarks>
/// silencedetect рассказывает о найденном строками в stderr, а не файлом
/// и не кодом возврата. Разбор этих строк проверить иначе нельзя: формат
/// вывода задаёт сам ffmpeg, и подделанная строка в тесте доказывала бы
/// только то, что мы умеем читать свою же выдумку.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class SilenceIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public SilenceIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task Pause_between_two_tones_is_found()
    {
        // Две секунды тона, три тишины, две тона: пауза ровно одна и известна заранее.
        var path = await CreateAsync("тон 2с, тишина 3с, тон 2с");

        var ranges = await Detect(SilenceOptions.Default);

        ranges.Should().ContainSingle();

        var pause = ranges[0];
        pause.Start.Should().BeCloseTo(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(300));
        pause.End.Should().BeCloseTo(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300));

        File.Exists(path).Should().BeTrue();
    }

    [FfmpegFact]
    public async Task Pause_shorter_than_asked_is_not_reported()
    {
        await CreateAsync("та же пауза");

        var ranges = await Detect(SilenceOptions.Default with { MinDuration = TimeSpan.FromSeconds(5) });

        ranges.Should().BeEmpty("трёхсекундная пауза короче запрошенных пяти секунд");
    }

    private async Task<IReadOnlyList<TimeRange>> Detect(SilenceOptions options)
    {
        var detector = new FfmpegSilenceDetector(
            CreateToolsetProvider(),
            _runner,
            NullLogger<FfmpegSilenceDetector>.Instance);

        return await detector.DetectAsync(
            Path.Combine(_workDirectory, "речь.m4a"),
            options,
            CancellationToken.None);
    }

    /// <summary>Звук из трёх частей: тон, тишина, тон.</summary>
    private async Task<string> CreateAsync(string _)
    {
        var path = Path.Combine(_workDirectory, "речь.m4a");

        string[] arguments =
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-f", "lavfi", "-i", "anullsrc=channel_layout=mono:sample_rate=44100:duration=3",
            "-f", "lavfi", "-i", "sine=frequency=660:duration=2",
            "-filter_complex", "[0:a][1:a][2:a]concat=n=3:v=0:a=1[out]",
            "-map", "[out]",
            "-c:a", "aac", "-b:a", "64k",
            path
        ];

        var made = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!, arguments),
            null,
            null,
            CancellationToken.None);

        made.Success.Should().BeTrue(string.Join(Environment.NewLine, made.StdErrTail));

        return path;
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
