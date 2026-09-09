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

public class FfmpegExportPlannerTests
{
    private static readonly MediaCapabilities Capabilities = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "libx264", "libx265", "libvpx-vp9", "libsvtav1" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac", "libopus", "libmp3lame" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static readonly FfmpegExportPlanner Planner = new();

    private static string OutputPath(string name = "результат.mp4") =>
        Path.Combine(Path.GetTempPath(), "meowscut-tests", Guid.NewGuid().ToString("N"), name);

    private static ExportPlan Plan(Project project, ExportSettings settings) =>
        Planner.CreatePlan(new ExportRequest(project, settings), Capabilities);

    private static ExportSettings Settings(string? output = null) =>
        ExportSettings.Default with { OutputPath = output ?? OutputPath() };

    [Fact]
    public void Unchanged_project_with_fast_mode_is_copied_without_re_encoding()
    {
        var project = Project.FromMedia(Fake.Info());
        var settings = Settings() with { PreferStreamCopy = true };

        var plan = Plan(project, settings);

        plan.IsStreamCopy.Should().BeTrue();
        plan.Stages.Single().Arguments.Should().ContainInOrder("-c", "copy");
        plan.Summary.IsStreamCopy.Should().BeTrue();
    }

    [Fact]
    public void Fast_mode_is_refused_when_edits_require_re_encoding()
    {
        var project = Project.FromMedia(Fake.Info());
        var sped = new SetClipSpeedCommand(project.Sequence.Video.Clips[0].Id, 2d).Apply(project.Sequence);

        var plan = Plan(project.WithSequence(sped), Settings() with { PreferStreamCopy = true });

        plan.IsStreamCopy.Should().BeFalse("скорость нельзя изменить копированием потока");
        plan.Stages.Single().Kind.Should().Be(StageKind.Transcode);
    }

    [Fact]
    public void Several_clips_always_go_through_the_filter_graph()
    {
        var project = Project.FromMedia(Fake.Info());
        var cut = new RemoveRangeCommand(
                new TimeRangeSelection(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8)))
            .Apply(project.Sequence);

        var plan = Plan(project.WithSequence(cut), Settings() with { PreferStreamCopy = true });

        plan.IsStreamCopy.Should().BeFalse();
        plan.Stages.Single().Arguments.Should().Contain("-filter_complex");
    }

    [Fact]
    public void Codec_incompatible_with_the_container_blocks_copying()
    {
        // H.264 в WebM не положить, значит копирование невозможно.
        var project = Project.FromMedia(Fake.Info(videoCodec: "h264"));
        var settings = Settings("результат.webm") with
        {
            Container = ContainerFormat.WebM,
            PreferStreamCopy = true
        };

        var plan = Plan(project, settings);

        plan.IsStreamCopy.Should().BeFalse();
        plan.Stages.Single().Arguments.Should().ContainInOrder("-c:v", "libvpx-vp9");
    }

    [Fact]
    public void Trimmed_copy_warns_about_keyframes()
    {
        var project = Project.FromMedia(Fake.Info(seconds: 60));
        var trimmed = project.Sequence.WithTrack(
            project.Sequence.Video.Replace(
                project.Sequence.Video.Clips[0].TrimStart(TimeSpan.FromSeconds(5))));

        var plan = Plan(project.WithSequence(trimmed), Settings() with { PreferStreamCopy = true });

        plan.IsStreamCopy.Should().BeTrue();
        plan.Warnings.Should().Contain(w => w.Kind == PlanWarningKind.StreamCopyKeyframeSnap);
        plan.Stages.Single().Arguments.Should().ContainInOrder("-ss", "5");
    }

    [Fact]
    public void Expected_duration_accounts_for_cuts_and_speed()
    {
        var project = Project.FromMedia(Fake.Info(seconds: 60));
        var sequence = new SetClipSpeedCommand(project.Sequence.Video.Clips[0].Id, 2d).Apply(project.Sequence);

        var plan = Plan(project.WithSequence(sequence), Settings());

        // Прогресс считается от длительности результата, а не исходника.
        plan.ExpectedOutputDuration.Should().Be(TimeSpan.FromSeconds(30));
        plan.Summary.Duration.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Mp4_gets_faststart_and_pixel_format()
    {
        var plan = Plan(Project.FromMedia(Fake.Info()), Settings());

        plan.Stages.Single().Arguments.Should().ContainInOrder("-movflags", "+faststart");
        plan.Stages.Single().Arguments.Should().ContainInOrder("-pix_fmt", "yuv420p");
    }

    [Fact]
    public void H265_in_mov_gets_the_compatibility_tag()
    {
        var settings = Settings("результат.mov") with
        {
            Container = ContainerFormat.Mov,
            Video = VideoSettings.Default with { Codec = VideoCodec.H265 }
        };

        var plan = Plan(Project.FromMedia(Fake.Info()), settings);

        plan.Stages.Single().Arguments.Should().ContainInOrder("-tag:v", "hvc1");
    }

    [Fact]
    public void Vp9_constant_quality_needs_zero_bitrate()
    {
        var settings = Settings("результат.webm") with
        {
            Container = ContainerFormat.WebM,
            Video = VideoSettings.Default with
            {
                Codec = VideoCodec.Vp9,
                RateControl = new RateControl.ConstantQuality(31)
            }
        };

        var plan = Plan(Project.FromMedia(Fake.Info()), settings);

        plan.Stages.Single().Arguments.Should().ContainInOrder("-crf", "31");
        plan.Stages.Single().Arguments.Should().ContainInOrder("-b:v", "0");
    }

    [Fact]
    public void Constant_bitrate_sets_limits_too()
    {
        var settings = Settings() with
        {
            Video = VideoSettings.Default with { RateControl = new RateControl.ConstantBitrate(4000) }
        };

        var arguments = Plan(Project.FromMedia(Fake.Info()), settings).Stages.Single().Arguments;

        arguments.Should().ContainInOrder("-b:v", "4000k");
        arguments.Should().Contain("-maxrate");
        arguments.Should().Contain("-bufsize");
    }

    [Fact]
    public void Upscale_and_frame_rate_increase_are_reported()
    {
        var settings = Settings() with
        {
            Video = VideoSettings.Default with
            {
                Resolution = new ResolutionSpec.Preset(2160),
                FrameRate = new FrameRateSpec.Fixed(60)
            }
        };

        var plan = Plan(Project.FromMedia(Fake.Info(width: 1280, height: 720, fps: 30)), settings);

        plan.Warnings.Should().Contain(w => w.Kind == PlanWarningKind.Upscale);
        plan.Warnings.Should().Contain(w => w.Kind == PlanWarningKind.FrameRateIncrease);
    }

    [Fact]
    public void Dropping_audio_is_reported_and_passed_to_ffmpeg()
    {
        var settings = Settings() with { Audio = AudioSettings.Disabled };

        var plan = Plan(Project.FromMedia(Fake.Info()), settings);

        plan.Warnings.Should().Contain(w => w.Kind == PlanWarningKind.AudioDropped);
        plan.Stages.Single().Arguments.Should().Contain("-an");
        plan.Summary.AudioLabel.Should().Be("без звука");
    }

    [Fact]
    public void Summary_reports_target_resolution_and_estimated_size()
    {
        var settings = Settings() with
        {
            Video = VideoSettings.Default with { Resolution = new ResolutionSpec.Preset(720) }
        };

        var plan = Plan(Project.FromMedia(Fake.Info(seconds: 60, width: 1920, height: 1080)), settings);

        plan.Summary.Resolution.Should().Be(new FrameSize(1280, 720));
        plan.Summary.Fps.Should().BeApproximately(30, 0.01);
        plan.Summary.EstimatedSizeBytes.Should().NotBeNull();
        plan.Summary.EstimatedSizeBytes!.Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Output_extension_follows_the_container()
    {
        var settings = Settings("результат.mp4") with { Container = ContainerFormat.Mkv };

        var plan = Plan(Project.FromMedia(Fake.Info()), settings);

        Path.GetExtension(plan.OutputPath).Should().Be(".mkv");
    }

    [Fact]
    public void Existing_file_is_not_overwritten_silently()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meowscut-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "результат.mp4");
        File.WriteAllText(path, "уже есть");

        try
        {
            var plan = Plan(Project.FromMedia(Fake.Info()), Settings(path));

            plan.OutputPath.Should().NotBe(path);
            plan.OutputPath.Should().EndWith("результат (2).mp4");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Result_is_written_to_a_temporary_file_next_to_the_target()
    {
        var plan = Plan(Project.FromMedia(Fake.Info()), Settings());

        plan.Stages.Single().OutputFile.Should().Be(plan.OutputPath + ".meowscut.part");
    }

    [Fact]
    public void Missing_encoder_falls_back_to_an_available_one()
    {
        var capabilities = new MediaCapabilities(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264", "libaom-av1" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var settings = Settings() with
        {
            Video = VideoSettings.Default with { Codec = VideoCodec.Av1 }
        };

        var plan = Planner.CreatePlan(new ExportRequest(Project.FromMedia(Fake.Info()), settings), capabilities);

        plan.Stages.Single().Arguments.Should().ContainInOrder("-c:v", "libaom-av1");
    }

    [Fact]
    public void Empty_timeline_cannot_be_exported()
    {
        var project = Project.Empty;

        var act = () => Plan(project, Settings());

        act.Should().Throw<Core.Diagnostics.EditOperationException>();
    }
}
