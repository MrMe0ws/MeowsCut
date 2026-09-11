using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;
using MeowsCut.Core.Projects;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Probing;
using MeowsCut.Ffmpeg.Toolset;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.Ffmpeg.Tests.Integration;

/// <summary>
/// Черновик проекта на настоящих файлах: сохранили, открыли, монтаж на месте.
/// </summary>
/// <remarks>
/// Здесь нужен ffprobe: при открытии черновик перечитывает файлы заново, и именно
/// это отличает его от простого дампа объектов в JSON.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class ProjectStoreIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public ProjectStoreIntegrationTests() => Directory.CreateDirectory(_workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [FfmpegFact]
    public async Task A_saved_draft_opens_with_the_same_cut()
    {
        var video = await CreateVideoAsync("ролик.mp4", 6);
        var project = Project.FromMedia(await ProbeAsync(video));

        // Режем и ускоряем — черновик обязан помнить именно это
        var clip = project.Sequence.Video.Clips[0]
            .TrimEnd(TimeSpan.FromSeconds(-2))
            .WithSpeed(2d);

        var edited = project.WithSequence(
            project.Sequence.WithTrack(project.Sequence.Video.Replace(clip)));

        var store = Store();
        var path = Path.Combine(_workDirectory, "черновик.meows");

        await store.SaveAsync(edited, path, CancellationToken.None);
        File.Exists(path).Should().BeTrue();

        var result = await store.LoadAsync(path, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
        result.Project.Sources.Should().ContainSingle();

        var restored = result.Project.Sequence.Video.Clips.Should().ContainSingle().Subject;
        restored.Speed.Should().BeApproximately(2d, 0.0001);
        restored.SourceRange.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(100));
    }

    [FfmpegFact]
    public async Task A_draft_whose_file_is_gone_says_so()
    {
        var video = await CreateVideoAsync("пропажа.mp4", 3);
        var project = Project.FromMedia(await ProbeAsync(video));

        var store = Store();
        var path = Path.Combine(_workDirectory, "потеря.meows");

        await store.SaveAsync(project, path, CancellationToken.None);
        File.Delete(video);

        var result = await store.LoadAsync(path, CancellationToken.None);

        result.Warnings.Should().ContainSingle().Which.Should().Contain("не найден");
        result.Project.Sequence.Video.Clips.Should().BeEmpty();
    }

    [FfmpegFact]
    public async Task Audio_tracks_come_back_from_the_draft()
    {
        var video = await CreateVideoAsync("видео.mp4", 5);
        var music = await CreateAudioAsync("музыка.m4a", 4);

        var project = Project.FromMedia(await ProbeAsync(video));
        var (withMusic, source) = project.WithSource(await ProbeAsync(music));

        var track = AudioTrack.Empty("Музыка") with
        {
            Gain = 0.6,
            Clips = [AudioClip.FromSource(source, TimeSpan.FromSeconds(1)) with { Speed = 1.5, PitchSemitones = -2 }]
        };

        var edited = withMusic.WithSequence(withMusic.Sequence.WithTracks([track]));

        var store = Store();
        var path = Path.Combine(_workDirectory, "со звуком.meows");

        await store.SaveAsync(edited, path, CancellationToken.None);
        var result = await store.LoadAsync(path, CancellationToken.None);

        var restored = result.Project.Sequence.AudioTracks.Should().ContainSingle().Subject;
        restored.Title.Should().Be("Музыка");
        restored.Gain.Should().BeApproximately(0.6, 0.0001);

        var clip = restored.Clips.Should().ContainSingle().Subject;
        clip.Speed.Should().BeApproximately(1.5, 0.0001);
        clip.PitchSemitones.Should().Be(-2);
        clip.TimelineStart.Should().Be(TimeSpan.FromSeconds(1));
    }

    private JsonProjectStore Store() =>
        new(new FfprobeMediaProbe(CreateToolsetProvider(), _runner, NullLogger<FfprobeMediaProbe>.Instance),
            NullLogger<JsonProjectStore>.Instance);

    private async Task<string> CreateVideoAsync(string fileName, int seconds)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", $"testsrc2=size=320x240:rate=25:duration={seconds}",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "32", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "64k", "-shortest", path
        ]);

        return path;
    }

    private async Task<string> CreateAudioAsync(string fileName, int seconds)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await RunFfmpegAsync(["-f", "lavfi", "-i", $"sine=frequency=330:duration={seconds}", "-c:a", "aac", path]);

        return path;
    }

    private async Task RunFfmpegAsync(string[] arguments)
    {
        string[] full = ["-y", "-hide_banner", "-loglevel", "error", .. arguments];

        var result = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!, full), null, null, CancellationToken.None);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.StdErrTail));
    }

    private async Task<MediaInfo> ProbeAsync(string path) =>
        await new FfprobeMediaProbe(CreateToolsetProvider(), _runner, NullLogger<FfprobeMediaProbe>.Instance)
            .ProbeAsync(path, CancellationToken.None);

    private static Core.Abstractions.IMediaToolsetProvider CreateToolsetProvider()
    {
        var provider = new MediaToolsetProvider();
        provider.Set(new Core.Abstractions.MediaToolset(
            FfmpegTestEnvironment.FfmpegPath!,
            FfmpegTestEnvironment.FfprobePath!,
            "test",
            Core.Abstractions.ToolsetSource.LocalAppData,
            MediaCapabilities.Empty));

        return provider;
    }
}
