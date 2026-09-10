using FluentAssertions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;
using MeowsCut.Core.Processing;
using MeowsCut.Ffmpeg.Planning;
using MeowsCut.Ffmpeg.Tests.Support;

namespace MeowsCut.Ffmpeg.Tests.Planning;

/// <summary>
/// Два прохода и целевой размер: и то и другое превращает экспорт в план из двух стадий.
/// </summary>
public class TwoPassPlanningTests
{
    private static readonly MediaCapabilities Capabilities = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264", "libvpx-vp9" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac", "libopus" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static readonly FfmpegExportPlanner Planner = new(new AppPaths());

    private static ExportSettings Settings() => ExportSettings.Default with
    {
        OutputPath = Path.Combine(Path.GetTempPath(), "meowscut-tests", Guid.NewGuid().ToString("N"), "результат.mp4")
    };

    private static ExportPlan Plan(ExportSettings settings, double seconds = 60) =>
        Planner.CreatePlan(new ExportRequest(Project.FromMedia(Fake.Info(seconds)), settings), Capabilities);

    [Fact]
    public void Two_pass_produces_analysis_and_encoding_stages()
    {
        var settings = Settings() with
        {
            Video = VideoSettings.Default with
            {
                RateControl = new RateControl.ConstantBitrate(4000),
                Advanced = new AdvancedVideoSettings { TwoPass = true }
            }
        };

        var plan = Plan(settings);

        plan.Stages.Should().HaveCount(2);
        plan.Stages[0].Kind.Should().Be(StageKind.PassOne);
        plan.Stages[1].Kind.Should().Be(StageKind.PassTwo);
        plan.Stages[0].Arguments.Should().ContainInOrder("-pass", "1");
        plan.Stages[1].Arguments.Should().ContainInOrder("-pass", "2");
    }

    [Fact]
    public void First_pass_writes_nothing_and_skips_audio()
    {
        var settings = Settings() with
        {
            Video = VideoSettings.Default with
            {
                RateControl = new RateControl.ConstantBitrate(4000),
                Advanced = new AdvancedVideoSettings { TwoPass = true }
            }
        };

        var first = Plan(settings).Stages[0];

        first.Arguments.Should().ContainInOrder("-f", "null");
        first.Arguments.Should().Contain("-an", "звук в первом проходе только тратит время");
        first.OutputFile.Should().BeNull();
    }

    [Fact]
    public void Second_pass_writes_the_result_and_keeps_audio()
    {
        var settings = Settings() with
        {
            Video = VideoSettings.Default with
            {
                RateControl = new RateControl.ConstantBitrate(4000),
                Advanced = new AdvancedVideoSettings { TwoPass = true }
            }
        };

        var plan = Plan(settings);
        var second = plan.Stages[1];

        second.OutputFile.Should().Be(plan.OutputPath + ".meowscut.part");
        second.Arguments.Should().ContainInOrder("-c:a", "aac");
        second.Arguments.Should().ContainInOrder("-movflags", "+faststart");
    }

    [Fact]
    public void Both_passes_share_one_statistics_file()
    {
        var settings = Settings() with
        {
            Video = VideoSettings.Default with
            {
                RateControl = new RateControl.ConstantBitrate(4000),
                Advanced = new AdvancedVideoSettings { TwoPass = true }
            }
        };

        var plan = Plan(settings);

        var firstLog = plan.Stages[0].Arguments[plan.Stages[0].Arguments.ToList().IndexOf("-passlogfile") + 1];
        var secondLog = plan.Stages[1].Arguments[plan.Stages[1].Arguments.ToList().IndexOf("-passlogfile") + 1];

        firstLog.Should().Be(secondLog, "второй проход читает статистику первого");
        plan.CleanupPaths.Should().Contain(path => path.StartsWith(firstLog, StringComparison.Ordinal));
    }

    [Fact]
    public void Stage_weights_reflect_that_the_first_pass_is_faster()
    {
        var settings = Settings() with
        {
            Video = VideoSettings.Default with
            {
                RateControl = new RateControl.ConstantBitrate(4000),
                Advanced = new AdvancedVideoSettings { TwoPass = true }
            }
        };

        var plan = Plan(settings);

        plan.Stages[0].Weight.Should().BeLessThan(plan.Stages[1].Weight);
        (plan.Stages[0].Weight + plan.Stages[1].Weight).Should().BeApproximately(1d, 0.001);
    }

    [Fact]
    public void Target_size_turns_into_a_bitrate_and_two_passes()
    {
        // 10 МБ на 60 секунд — примерно 1330 кбит/с на всё, минус звук.
        var settings = Settings() with
        {
            Video = VideoSettings.Default with { RateControl = new RateControl.TargetSize(10 * 1024 * 1024) }
        };

        var plan = Plan(settings, seconds: 60);

        plan.Stages.Should().HaveCount(2);

        var arguments = plan.Stages[1].Arguments;
        var bitrateIndex = arguments.ToList().IndexOf("-b:v");
        bitrateIndex.Should().BeGreaterThan(-1);

        var value = arguments[bitrateIndex + 1];
        value.Should().EndWith("k");

        var kbps = int.Parse(value.TrimEnd('k'));
        kbps.Should().BeInRange(1000, 1300, "из общего битрейта вычитается звук и берётся запас");
    }

    [Fact]
    public void Single_pass_stays_single_when_nothing_asks_for_two()
    {
        Plan(Settings()).Stages.Should().HaveCount(1);
    }

    [Fact]
    public void Stream_copy_never_gets_a_second_pass()
    {
        var settings = Settings() with
        {
            PreferStreamCopy = true,
            Video = VideoSettings.Default with { Advanced = new AdvancedVideoSettings { TwoPass = true } }
        };

        var plan = Plan(settings);

        plan.Stages.Should().HaveCount(1);
        plan.IsStreamCopy.Should().BeTrue("копировать поток дважды бессмысленно");
    }

    [Theory]
    [InlineData(256_000, 3, 0)]
    [InlineData(10_485_760, 60, 128)]
    public void Bitrate_for_target_size_leaves_room_for_the_container(long bytes, int seconds, int audioKbps)
    {
        var kbps = RateControlPolicy.BitrateForTargetSizeKbps(bytes, TimeSpan.FromSeconds(seconds), audioKbps);

        var predictedBytes = (kbps + audioKbps) * 1000d / 8 * seconds;

        predictedBytes.Should().BeLessThan(bytes, "иначе файл стабильно вылезает за лимит");
    }
}
