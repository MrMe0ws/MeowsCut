using FluentAssertions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;
using MeowsCut.Core.Processing;
using MeowsCut.Ffmpeg.Planning;
using MeowsCut.Ffmpeg.Temp;
using MeowsCut.Ffmpeg.Tests.Support;

namespace MeowsCut.Ffmpeg.Tests.Planning;

/// <summary>
/// Быстрая склейка нескольких кусков без перекодирования.
/// </summary>
public class ConcatPlanningTests
{
    private static readonly MediaCapabilities Capabilities = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264", "libvpx-vp9" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac", "libopus" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static readonly FfmpegExportPlanner Planner = new(new AppPaths());

    private static ExportSettings Settings(bool fast = true, string name = "результат.mp4") =>
        ExportSettings.Default with
        {
            PreferStreamCopy = fast,
            OutputPath = Path.Combine(Path.GetTempPath(), "meowscut-tests", Guid.NewGuid().ToString("N"), name)
        };

    /// <summary>Проект, из которого вырезан кусок: два клипа одного файла.</summary>
    private static Project CutProject()
    {
        var project = Project.FromMedia(Fake.Info(seconds: 60));
        var sequence = new RemoveRangeCommand(
                new TimeRangeSelection(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)))
            .Apply(project.Sequence);

        return project.WithSequence(sequence);
    }

    private static ExportPlan Plan(Project project, ExportSettings settings) =>
        Planner.CreatePlan(new ExportRequest(project, settings), Capabilities);

    [Fact]
    public void Fast_mode_cuts_pieces_and_joins_them_without_re_encoding()
    {
        var plan = Plan(CutProject(), Settings());

        plan.Stages.Should().HaveCount(3, "два куска плюс сборка");
        plan.Stages[0].Kind.Should().Be(StageKind.SegmentExtract);
        plan.Stages[1].Kind.Should().Be(StageKind.SegmentExtract);
        plan.Stages[2].Kind.Should().Be(StageKind.Concat);

        foreach (var stage in plan.Stages)
        {
            stage.Arguments.Should().ContainInOrder("-c", "copy");
        }
    }

    [Fact]
    public void Each_piece_is_cut_by_its_own_range()
    {
        var plan = Plan(CutProject(), Settings());

        // Первый кусок: с нуля на двадцать секунд, второй: с тридцатой на тридцать.
        plan.Stages[0].Arguments.Should().ContainInOrder("-t", "20");
        plan.Stages[1].Arguments.Should().ContainInOrder("-ss", "30");
        plan.Stages[1].Arguments.Should().ContainInOrder("-t", "30");
    }

    [Fact]
    public void Assembly_stage_knows_what_to_join()
    {
        var plan = Plan(CutProject(), Settings());
        var concat = plan.Stages[^1];

        concat.Concat.Should().NotBeNull();
        concat.Concat!.Files.Should().HaveCount(2);
        concat.Arguments.Should().ContainInOrder("-f", "concat");
        concat.Arguments.Should().ContainInOrder("-safe", "0");
        concat.Arguments.Should().Contain(concat.Concat.ListFile);
    }

    [Fact]
    public void Temporary_pieces_are_scheduled_for_removal()
    {
        var plan = Plan(CutProject(), Settings());

        plan.CleanupPaths.Should().HaveCountGreaterThanOrEqualTo(4, "куски, список и папка");
        plan.CleanupPaths.Should().Contain(plan.Stages[^1].Concat!.ListFile);
    }

    [Fact]
    public void Fast_join_warns_about_keyframes()
    {
        var plan = Plan(CutProject(), Settings());

        plan.Warnings.Should().Contain(w => w.Kind == PlanWarningKind.StreamCopyKeyframeSnap);
    }

    [Fact]
    public void Without_fast_mode_everything_goes_through_the_filter_graph()
    {
        var plan = Plan(CutProject(), Settings(fast: false));

        plan.Stages.Should().HaveCount(1);
        plan.Stages[0].Arguments.Should().Contain("-filter_complex");
    }

    [Fact]
    public void Speed_change_disables_fast_join()
    {
        var project = CutProject();
        var sequence = new SetClipSpeedCommand(project.Sequence.Video.Clips[0].Id, 2d).Apply(project.Sequence);

        var plan = Plan(project.WithSequence(sequence), Settings());

        plan.Stages.Should().HaveCount(1, "скорость требует пересчёта кадров");
    }

    [Fact]
    public void Resolution_change_disables_fast_join()
    {
        var settings = Settings() with
        {
            Video = VideoSettings.Default with { Resolution = new ResolutionSpec.Preset(720) }
        };

        Plan(CutProject(), settings).Stages.Should().HaveCount(1);
    }

    [Fact]
    public void Muted_clip_disables_fast_join()
    {
        var project = CutProject();
        var sequence = new SetClipAudioCommand(project.Sequence.Video.Clips[0].Id, ClipAudio.Muted)
            .Apply(project.Sequence);

        Plan(project.WithSequence(sequence), Settings()).Stages.Should().HaveCount(1,
            "выключить звук у одного куска копированием потока нельзя");
    }

    [Fact]
    public void Pieces_from_different_files_disable_fast_join()
    {
        var project = CutProject();
        var (withSecond, second) = project.WithSource(Fake.Info(seconds: 10, path: @"C:\видео\другой.mp4"));
        var sequence = new AppendClipCommand(Clip.FromSource(second)).Apply(withSecond.Sequence);

        var plan = Plan(withSecond.WithSequence(sequence), Settings());

        plan.Stages.Should().HaveCount(1,
            "у разных файлов параметры потоков не совпадают, склейка дала бы битый результат");
    }

    [Fact]
    public void Container_that_cannot_hold_the_source_codec_disables_fast_join()
    {
        var settings = Settings(name: "результат.webm") with { Container = ContainerFormat.WebM };

        Plan(CutProject(), settings).Stages.Should().HaveCount(1, "H.264 в WebM не положить даже копированием");
    }
}

/// <summary>
/// Список для concat: формат придирчив к кавычкам и кодировке.
/// </summary>
public class ConcatListWriterTests
{
    [Fact]
    public void Each_file_becomes_its_own_line()
    {
        var text = ConcatListWriter.Build([@"C:\видео\seg-000.mp4", @"C:\видео\seg-001.mp4"]);

        text.Should().Be("file 'C:\\видео\\seg-000.mp4'\nfile 'C:\\видео\\seg-001.mp4'\n");
    }

    [Fact]
    public void Apostrophe_in_a_path_is_escaped()
    {
        var text = ConcatListWriter.Build([@"C:\видео\Пете'с клип.mp4"]);

        text.Should().Contain(@"'\''", "иначе кавычка обрывает строку и ffmpeg берёт не тот файл");
    }

    [Fact]
    public void File_is_written_without_byte_order_mark()
    {
        var path = Path.Combine(Path.GetTempPath(), "meowscut-tests", Guid.NewGuid().ToString("N"), "concat.txt");

        try
        {
            ConcatListWriter.Write(path, [@"C:\видео\клип с пробелом.mp4"]);

            var bytes = File.ReadAllBytes(path);
            bytes.Take(3).Should().NotEqual([0xEF, 0xBB, 0xBF], "с BOM ffmpeg не распознаёт первую строку");

            File.ReadAllText(path).Should().Contain("клип с пробелом.mp4");
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
