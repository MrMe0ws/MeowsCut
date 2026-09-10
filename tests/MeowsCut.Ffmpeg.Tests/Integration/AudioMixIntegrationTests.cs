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
/// Наложение звука на настоящем ffmpeg.
/// </summary>
/// <remarks>
/// Граф микшера можно проверить строкой, но строка не расскажет, примет ли его ffmpeg:
/// имена меток, поддержка adelay=all, наличие rubberband — всё это выясняется только
/// запуском. Поэтому здесь настоящие файлы и настоящий экспорт.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class AudioMixIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public AudioMixIntegrationTests() => Directory.CreateDirectory(_workDirectory);

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
    public async Task Second_audio_track_is_mixed_into_the_result()
    {
        var project = await CreateProjectAsync(seconds: 6);
        var music = await AddAudioSourceAsync(project, "музыка.m4a", seconds: 4, frequency: 220);

        // Кладём музыку со сдвигом: проверяется, что adelay доезжает до ffmpeg.
        var clip = AudioClip.FromSource(music.Source, TimeSpan.FromSeconds(1)).WithGain(0.5);
        var track = AudioTrack.Empty("Музыка") with { Clips = [clip] };

        var withMusic = music.Project.WithSequence(
            music.Project.Sequence.WithTracks([track]));

        var output = Path.Combine(_workDirectory, "с музыкой.mp4");
        var result = await RunAsync(withMusic, Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        var info = await ProbeAsync(output);
        info.HasAudio.Should().BeTrue();
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(500),
            "микс обрезается по длине картинки, а не растягивает её");
    }

    [FfmpegFact]
    public async Task Pitch_shift_keeps_the_length()
    {
        var project = await CreateProjectAsync(seconds: 5);
        var voice = await AddAudioSourceAsync(project, "голос.m4a", seconds: 5, frequency: 330);

        var clip = AudioClip.FromSource(voice.Source, TimeSpan.Zero).WithPitch(5);
        var track = AudioTrack.Empty("Голос") with { Clips = [clip] };

        var withVoice = voice.Project.WithSequence(voice.Project.Sequence.WithTracks([track]));

        var output = Path.Combine(_workDirectory, "тональность.mp4");
        var result = await RunAsync(withVoice, Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        var info = await ProbeAsync(output);
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500),
            "сдвиг тональности не имеет права менять длительность");
    }

    [FfmpegFact]
    public async Task Muted_video_with_added_music_still_has_sound()
    {
        var project = await CreateProjectAsync(seconds: 4);

        // Родной звук выключен — ровно тот случай, ради которого звук и подставляют.
        var silenced = project.Sequence.WithTrack(
            project.Sequence.Video.Replace(project.Sequence.Video.Clips[0] with { Audio = ClipAudio.Muted }));

        var music = await AddAudioSourceAsync(project.WithSequence(silenced), "замена.m4a", seconds: 4, frequency: 660);

        var track = AudioTrack.Empty("Замена") with
        {
            Clips = [AudioClip.FromSource(music.Source, TimeSpan.Zero)]
        };

        var replaced = music.Project.WithSequence(music.Project.Sequence.WithTracks([track]));

        var output = Path.Combine(_workDirectory, "замена звука.mp4");
        var result = await RunAsync(replaced, Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);
        (await ProbeAsync(output)).HasAudio.Should().BeTrue();
    }

    private static ExportSettings Settings(string outputPath) =>
        ExportSettings.Default with { OutputPath = outputPath, PreferStreamCopy = false };

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

    private async Task<Project> CreateProjectAsync(int seconds)
    {
        var path = Path.Combine(_workDirectory, "видео.mp4");

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", $"testsrc2=size=320x240:rate=25:duration={seconds}",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "32", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "64k", "-shortest", path
        ]);

        return Project.FromMedia(await ProbeAsync(path));
    }

    private async Task<(Project Project, MediaSource Source)> AddAudioSourceAsync(
        Project project,
        string fileName,
        int seconds,
        int frequency)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", $"sine=frequency={frequency}:duration={seconds}",
            "-c:a", "aac", "-b:a", "96k", path
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
