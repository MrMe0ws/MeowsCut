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
/// Сдвиг тональности проверяется измерением, а не на слух.
/// </summary>
/// <remarks>
/// «Вроде не слышно разницы» — не диагноз: в предпросмотре тональность не применяется
/// вовсе, и проверить её можно только на готовом файле. Здесь берётся чистый тон
/// в 200 Гц, поднимается на октаву и измеряется, где после этого энергия: около
/// 400 Гц или всё ещё около 200. Заодно это ловит сборку ffmpeg без rubberband,
/// где сдвиг собирается из asetrate и atempo.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class PitchIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public PitchIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task An_octave_up_moves_the_energy_to_the_doubled_frequency()
    {
        var output = await ExportWithPitchAsync(semitones: 12, fileName: "октава вверх.mp4");

        var low = await BandVolumeAsync(output, 200);
        var high = await BandVolumeAsync(output, 400);

        high.Should().BeGreaterThan(low + 6,
            "тон в 200 Гц, поднятый на октаву, обязан звучать около 400 Гц");
    }

    [FfmpegFact]
    public async Task An_octave_down_moves_the_energy_to_the_halved_frequency()
    {
        var output = await ExportWithPitchAsync(semitones: -12, fileName: "октава вниз.mp4");

        var low = await BandVolumeAsync(output, 100);
        var high = await BandVolumeAsync(output, 200);

        low.Should().BeGreaterThan(high + 6);
    }

    [FfmpegFact]
    public async Task Without_a_shift_the_tone_stays_where_it_was()
    {
        var output = await ExportWithPitchAsync(semitones: 0, fileName: "без сдвига.mp4");

        var original = await BandVolumeAsync(output, 200);
        var octaveUp = await BandVolumeAsync(output, 400);

        original.Should().BeGreaterThan(octaveUp + 6);
    }

    /// <summary>Экспортирует ролик с одной звуковой дорожкой заданной тональности.</summary>
    private async Task<string> ExportWithPitchAsync(int semitones, string fileName)
    {
        var project = await CreateProjectAsync();
        var tone = await AddToneAsync(project, "тон.m4a", frequency: 200);

        var clip = AudioClip.FromSource(tone.Source, TimeSpan.Zero).WithPitch(semitones);
        var track = AudioTrack.Empty("Тон") with { Clips = [clip] };

        // Родной звук ролика выключен: иначе его синус мешал бы измерению.
        var sequence = tone.Project.Sequence;
        sequence = sequence
            .WithTrack(sequence.Video.Replace(sequence.Video.Clips[0] with { Audio = ClipAudio.Muted }))
            .WithTracks([track]);

        var output = Path.Combine(_workDirectory, fileName);

        var result = await RunAsync(
            tone.Project.WithSequence(sequence),
            ExportSettings.Default with { OutputPath = output, PreferStreamCopy = false });

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        return output;
    }

    /// <summary>
    /// Средняя громкость в узкой полосе вокруг частоты, в децибелах.
    /// Всё, кроме полосы, вырезается фильтром, и остаётся сравнить, где громче.
    /// </summary>
    private async Task<double> BandVolumeAsync(string path, int frequency)
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!,
            [
                "-hide_banner", "-nostdin", "-i", path,
                "-af", $"bandpass=f={frequency}:width_type=h:w=40,volumedetect",
                "-f", "null", "-"
            ]),
            null,
            null,
            CancellationToken.None);

        foreach (var line in result.StdErrTail)
        {
            var marker = line.IndexOf("mean_volume:", StringComparison.Ordinal);
            if (marker < 0)
            {
                continue;
            }

            var value = line[(marker + "mean_volume:".Length)..].Replace("dB", string.Empty).Trim();

            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var decibels))
            {
                return decibels;
            }
        }

        throw new InvalidOperationException(
            "ffmpeg не отдал mean_volume: " + string.Join(Environment.NewLine, result.StdErrTail));
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

    private async Task<Project> CreateProjectAsync()
    {
        var path = Path.Combine(_workDirectory, "видео.mp4");

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=4",
            "-f", "lavfi", "-i", "sine=frequency=1500:duration=4",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "32", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "64k", "-shortest", path
        ]);

        return Project.FromMedia(await ProbeAsync(path));
    }

    private async Task<(Project Project, MediaSource Source)> AddToneAsync(
        Project project,
        string fileName,
        int frequency)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", $"sine=frequency={frequency}:duration=4",
            "-c:a", "aac", "-b:a", "128k", path
        ]);

        return project.WithSource(await ProbeAsync(path));
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
