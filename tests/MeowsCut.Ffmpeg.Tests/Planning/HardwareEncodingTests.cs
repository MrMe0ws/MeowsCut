using FluentAssertions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Planning;
using MeowsCut.Ffmpeg.Tests.Support;

namespace MeowsCut.Ffmpeg.Tests.Planning;

/// <summary>
/// Кодирование видеокартой.
/// </summary>
/// <remarks>
/// Главное здесь — не то, что аппаратный энкодер включается, а то, что он молча
/// не включается там, где не справится: пользователь получит файл, который заказывал,
/// пусть и медленнее, и увидит предупреждение вместо загадки.
/// </remarks>
public class HardwareEncodingTests
{
    private static MediaCapabilities Capabilities(params string[] encoders)
    {
        var video = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "libx264", "libx265", "libvpx-vp9"
        };

        foreach (var encoder in encoders)
        {
            video.Add(encoder);
        }

        return new MediaCapabilities(
            video,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase))
        {
            // Аппаратным считается только то, что прошло пробное кодирование:
            // имя в -encoders означает лишь, с чем собран ffmpeg.
            WorkingHardwareEncoders = new HashSet<string>(encoders, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static Project Project() => Core.Editing.Project.FromMedia(Fake.Info(10));

    private static ExportSettings Settings(HardwareAcceleration hardware, VideoCodec codec = VideoCodec.H264) =>
        ExportSettings.Default with
        {
            OutputPath = @"C:\выход\готово.mp4",
            PreferStreamCopy = false,
            Video = VideoSettings.Default with
            {
                Codec = codec,
                Hardware = hardware,
                RateControl = new RateControl.ConstantQuality(23)
            }
        };

    private static ExportPlanText Plan(ExportSettings settings, MediaCapabilities capabilities)
    {
        var plan = new FfmpegExportPlanner(new AppPaths())
            .CreatePlan(new ExportRequest(Project(), settings), capabilities);

        return new ExportPlanText(
            string.Join(' ', plan.Stages[^1].Arguments),
            [.. plan.Warnings.Select(warning => warning.Kind)]);
    }

    private sealed record ExportPlanText(string Arguments, IReadOnlyList<PlanWarningKind> Warnings);

    [Fact]
    public void Nvidia_encoder_is_used_when_available()
    {
        var plan = Plan(Settings(HardwareAcceleration.Nvidia), Capabilities("h264_nvenc"));

        plan.Arguments.Should().Contain("h264_nvenc");
        plan.Arguments.Should().Contain("-cq 23", "CRF видеокарты не понимают");
        plan.Arguments.Should().NotContain("libx264");
        plan.Warnings.Should().NotContain(PlanWarningKind.HardwareUnavailable);
    }

    [Fact]
    public void Missing_encoder_falls_back_to_the_processor_with_a_warning()
    {
        var plan = Plan(Settings(HardwareAcceleration.Nvidia), Capabilities());

        plan.Arguments.Should().Contain("libx264");
        plan.Warnings.Should().Contain(PlanWarningKind.HardwareUnavailable);
    }

    [Fact]
    public void Target_size_wins_over_hardware()
    {
        // Подбор под размер делается двумя проходами, а журнал первого прохода
        // видеокарты не ведут: правильный файл важнее скорости.
        var settings = Settings(HardwareAcceleration.Nvidia) with
        {
            Video = VideoSettings.Default with
            {
                Codec = VideoCodec.H264,
                Hardware = HardwareAcceleration.Nvidia,
                RateControl = new RateControl.TargetSize(2_000_000)
            }
        };

        var plan = Plan(settings, Capabilities("h264_nvenc"));

        plan.Arguments.Should().Contain("libx264");
        plan.Warnings.Should().Contain(PlanWarningKind.HardwareUnavailable);
    }

    [Fact]
    public void Vp9_has_no_hardware_encoder_and_that_is_not_an_error()
    {
        var plan = Plan(
            Settings(HardwareAcceleration.Nvidia, VideoCodec.Vp9) with
            {
                Container = ContainerFormat.WebM,
                OutputPath = @"C:\выход\готово.webm"
            },
            Capabilities("h264_nvenc"));

        plan.Arguments.Should().Contain("libvpx-vp9");
        plan.Warnings.Should().Contain(PlanWarningKind.HardwareUnavailable);
    }

    [Fact]
    public void An_encoder_present_but_not_working_is_not_offered()
    {
        // Ровно тот случай, что нашёлся на машине разработчика: h264_nvenc есть
        // в сборке ffmpeg, но без драйвера NVIDIA падает с «Cannot load nvcuda.dll».
        var listedButBroken = new MediaCapabilities(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264", "h264_nvenc" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Ffmpeg.Arguments.HardwareEncoders.Available(VideoCodec.H264, listedButBroken)
            .Should().Equal(HardwareAcceleration.None);
    }

    [Fact]
    public void Intel_and_amd_have_their_own_quality_keys()
    {
        var intel = Plan(Settings(HardwareAcceleration.Intel), Capabilities("h264_qsv"));
        intel.Arguments.Should().Contain("-global_quality 23");

        var amd = Plan(Settings(HardwareAcceleration.Amd), Capabilities("h264_amf"));
        amd.Arguments.Should().Contain("-rc cqp");
    }
}
