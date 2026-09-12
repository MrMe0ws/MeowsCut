using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;
using MeowsCut.Core.Processing;
using MeowsCut.Ffmpeg.Planning;
using MeowsCut.Ffmpeg.Tests.Support;

namespace MeowsCut.Ffmpeg.Tests.Planning;

/// <summary>
/// Выбор стратегии для звукового файла и для правок картинки.
/// </summary>
/// <remarks>
/// Главное, что здесь стерегут: быстрые стратегии не должны молча съедать
/// затухание, поворот и выравнивание громкости. Скопированный поток отдал бы
/// файл без них, и пользователь увидел бы не то, что собрал на доске.
/// </remarks>
public class AudioOnlyAndPictureEditPlanningTests
{
    private static readonly MediaCapabilities Capabilities = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264", "libx265" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac", "libmp3lame", "pcm_s16le" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static readonly FfmpegExportPlanner Planner = new(new Core.Configuration.AppPaths());

    private static string OutputPath(string name) =>
        Path.Combine(Path.GetTempPath(), "meowscut-tests", Guid.NewGuid().ToString("N"), name);

    private static ExportPlan Plan(Project project, ExportSettings settings) =>
        Planner.CreatePlan(new ExportRequest(project, settings), Capabilities);

    private static ExportSettings FastSettings(string name = "результат.mp4") =>
        ExportSettings.Default with { OutputPath = OutputPath(name), PreferStreamCopy = true };

    private static Project WithFirstClip(Project project, Func<Clip, Clip> change) =>
        project.WithSequence(
            project.Sequence.WithTrack(
                project.Sequence.Video.Replace(change(project.Sequence.Video.Clips[0]))));

    [Fact]
    public void Fade_forbids_stream_copy()
    {
        var project = WithFirstClip(
            Project.FromMedia(Fake.Info()),
            clip => clip.WithFades(fadeOut: TimeSpan.FromSeconds(2)));

        var plan = Plan(project, FastSettings());

        plan.IsStreamCopy.Should().BeFalse("затухание существует только в перерисованном кадре");
        plan.Stages.Single().Kind.Should().Be(StageKind.Transcode);
    }

    [Fact]
    public void Rotation_forbids_stream_copy()
    {
        var project = WithFirstClip(
            Project.FromMedia(Fake.Info()),
            clip => clip.WithTransform(ClipTransform.Identity.WithRotation(90)));

        Plan(project, FastSettings()).IsStreamCopy.Should().BeFalse();
    }

    [Fact]
    public void Loudness_normalization_forbids_stream_copy()
    {
        var settings = FastSettings() with
        {
            Audio = AudioSettings.Default with { NormalizeLoudness = true }
        };

        Plan(Project.FromMedia(Fake.Info()), settings).IsStreamCopy.Should().BeFalse();
    }

    [Fact]
    public void Fade_forbids_the_fast_concat_of_several_clips()
    {
        var project = Project.FromMedia(Fake.Info());
        var split = new SplitClipCommand(TimeSpan.FromSeconds(10)).Apply(project.Sequence);

        var faded = split.WithTrack(split.Video.Replace(
            split.Video.Clips[1].WithFades(fadeIn: TimeSpan.FromSeconds(1))));

        var plan = Plan(project.WithSequence(faded), FastSettings());

        plan.Stages.Should().ContainSingle().Which.Kind.Should().Be(StageKind.Transcode);
    }

    [Fact]
    public void Audio_only_export_drops_the_picture()
    {
        var settings = ExportSettings.Default with
        {
            Container = ContainerFormat.Mp3,
            OutputPath = OutputPath("дорожка.mp3")
        };

        var plan = Plan(Project.FromMedia(Fake.Info()), settings);

        var arguments = plan.Stages.Single().Arguments;

        arguments.Should().Contain("-vn");
        arguments.Should().Contain("-c:a");
        arguments.Should().Contain("libmp3lame");
        arguments.Should().NotContain("-c:v");
        plan.IsStreamCopy.Should().BeFalse();
    }

    [Fact]
    public void Audio_only_export_keeps_the_asked_extension()
    {
        var settings = ExportSettings.Default with
        {
            Container = ContainerFormat.M4a,

            // Путь с расширением от видео: контейнер обязан его исправить,
            // иначе проигрыватель получит m4a под именем .mp4.
            OutputPath = OutputPath("дорожка.mp4")
        };

        Plan(Project.FromMedia(Fake.Info()), settings).OutputPath.Should().EndWith(".m4a");
    }

    [Fact]
    public void Wav_export_uses_uncompressed_audio()
    {
        var settings = ExportSettings.Default with
        {
            Container = ContainerFormat.Wav,
            OutputPath = OutputPath("дорожка.wav")
        };

        Plan(Project.FromMedia(Fake.Info()), settings).Stages.Single()
            .Arguments.Should().Contain("pcm_s16le");
    }

    [Fact]
    public void Audio_only_summary_promises_no_picture()
    {
        var settings = ExportSettings.Default with
        {
            Container = ContainerFormat.Mp3,
            OutputPath = OutputPath("дорожка.mp3")
        };

        var summary = Plan(Project.FromMedia(Fake.Info()), settings).Summary;

        summary.Resolution.IsEmpty.Should().BeTrue();
        summary.EstimatedSizeBytes.Should().NotBeNull();
    }

    [Fact]
    public void Audio_stays_on_even_if_it_was_switched_off()
    {
        // Иначе кнопка экспорта делала бы пустой файл: в звуковом контейнере
        // выключенный звук не оставляет вообще ничего.
        var settings = (ExportSettings.Default with
        {
            Container = ContainerFormat.Mp3,
            Audio = AudioSettings.Disabled,
            OutputPath = OutputPath("дорожка.mp3")
        }).Normalized();

        settings.Audio.Enabled.Should().BeTrue();
    }
}
