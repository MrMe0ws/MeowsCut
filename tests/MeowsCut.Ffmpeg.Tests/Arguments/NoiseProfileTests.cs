using FluentAssertions;
using MeowsCut.Ffmpeg.Audio;

namespace MeowsCut.Ffmpeg.Tests.Arguments;

/// <summary>Где в дубле взять образец шума и каким порогом его давить.</summary>
public class NoiseProfileTests
{
    private static IReadOnlyList<LoudnessWindow> Windows(params double[] levels) =>
        levels.Select((level, i) => new LoudnessWindow(i * 0.5, (i + 1) * 0.5, level)).ToList();

    [Fact]
    public void Output_of_the_analysis_becomes_windows()
    {
        var lines = new[]
        {
            "[Parsed_ametadata_2 @ 0000] frame:0    pts:0       pts_time:0",
            "[Parsed_ametadata_2 @ 0000] lavfi.astats.Overall.RMS_level=-48.552251",
            "[Parsed_ametadata_2 @ 0000] frame:1    pts:24000   pts_time:0.5",
            "[Parsed_ametadata_2 @ 0000] lavfi.astats.Overall.RMS_level=-inf",
            "[Parsed_ametadata_2 @ 0000] frame:2    pts:48000   pts_time:1",
            "[Parsed_ametadata_2 @ 0000] lavfi.astats.Overall.RMS_level=-31.6"
        };

        var windows = NoiseProfile.Parse(lines);

        windows.Should().HaveCount(2, "последнее окно обрезано — его длина неизвестна");
        windows[0].Should().Be(new LoudnessWindow(0, 0.5, -48.552251));
        windows[1].RmsDb.Should().Be(double.NegativeInfinity);
    }

    [Fact]
    public void Learns_the_noise_from_the_quietest_half_second()
    {
        var filter = NoiseProfile.BuildFilter(Windows(-31, -47.6, -25, -30), strengthDb: 20)!;

        filter.Should().Contain("asendcmd=c='0.5 afftdn sn start; 1 afftdn sn stop'");
        filter.Should().Contain("afftdn=nr=20:nf=-38", "порог на 10 дБ выше уровня шума");
    }

    [Fact]
    public void Keeps_the_timing_of_the_voice()
    {
        var filter = NoiseProfile.BuildFilter(Windows(-50, -30), 20)!;

        filter.Should().StartWith("highpass=f=70,apad=pad_dur=0.025,");
        filter.Should().EndWith(",atrim=start=0.025,asetpts=PTS-STARTPTS", "задержка фильтра возвращается назад");
    }

    [Fact]
    public void Without_a_quiet_moment_it_does_not_learn_from_the_voice()
    {
        NoiseProfile.BuildFilter(Windows(-22, -18, -25), 20)!
            .Should().Contain("afftdn=nr=20:nf=-40:tn=1").And.NotContain("sn start");
    }

    [Fact]
    public void Digital_silence_throughout_has_nothing_to_remove()
    {
        NoiseProfile.BuildFilter(Windows(double.NegativeInfinity, -95, double.NegativeInfinity), 20).Should().BeNull();
    }

    [Fact]
    public void Zeros_while_the_microphone_wakes_up_are_skipped()
    {
        // Так начинался настоящий дубль: −90 дБ, пока микрофон включался, затем шум −43.
        var filter = NoiseProfile.BuildFilter(Windows(-90.7, -43, -24, -21), 20);

        filter.Should().NotBeNull("нули в начале не значат, что шума нет");
        filter.Should().Contain("0.5 afftdn sn start; 1 afftdn sn stop").And.Contain("nf=-33");
    }
}
