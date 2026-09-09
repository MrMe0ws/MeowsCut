using System.Text.Json;
using FluentAssertions;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Probing;
using MeowsCut.Ffmpeg.Probing.Json;

namespace MeowsCut.Ffmpeg.Tests.Probing;

public class MediaInfoMapperTests
{
    private static MediaInfo Map(string json, long fileSize = 1024) =>
        MediaInfoMapper.Map(
            JsonSerializer.Deserialize<FfprobeResponse>(json)!,
            @"C:\видео\клип с пробелом.mp4",
            fileSize);

    [Fact]
    public void Maps_standard_mp4()
    {
        var info = Map(FfprobeFixtures.StandardMp4, 2_170_134);

        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(6.005), TimeSpan.FromMilliseconds(5));
        info.Container.FormatName.Should().Be("mov,mp4,m4a,3gp,3g2,mj2");
        info.HasVideo.Should().BeTrue();
        info.HasAudio.Should().BeTrue();

        var video = info.PrimaryVideo!;
        video.Size.Should().Be(new FrameSize(1280, 720));
        video.CodecName.Should().Be("h264");
        video.Profile.Should().Be("High");
        video.FrameRate.Value.Should().BeApproximately(30, 0.001);
        video.BitrateBps.Should().Be(2_757_984);
        video.IsVariableFrameRate.Should().BeFalse();

        var audio = info.PrimaryAudio!;
        audio.CodecName.Should().Be("aac");
        audio.Channels.Should().Be(1);
        audio.SampleRateHz.Should().Be(44_100);
    }

    [Fact]
    public void Display_size_accounts_for_rotation_side_data()
    {
        var info = Map(FfprobeFixtures.RotatedPhoneVideo);

        var video = info.PrimaryVideo!;
        video.RotationDegrees.Should().Be(270, "поворот -90 нормализуется в 270");
        video.Size.Should().Be(new FrameSize(1920, 1080));
        video.DisplaySize.Should().Be(new FrameSize(1080, 1920), "вертикальное видео не должно показываться на боку");
    }

    [Fact]
    public void Reads_rotation_from_legacy_tag()
    {
        var info = Map(FfprobeFixtures.RotatedByTag);

        info.PrimaryVideo!.RotationDegrees.Should().Be(270);
    }

    [Fact]
    public void Cover_art_is_not_treated_as_video()
    {
        var info = Map(FfprobeFixtures.Mp3WithCoverArt);

        info.HasVideo.Should().BeFalse("обложка альбома приходит как видеопоток, но монтировать её нельзя");
        info.HasAudio.Should().BeTrue();
    }

    [Fact]
    public void Detects_variable_frame_rate_and_falls_back_to_stream_duration()
    {
        var info = Map(FfprobeFixtures.VariableFrameRateWebm);

        info.Duration.Should().Be(TimeSpan.FromSeconds(42.5), "в format длительности нет, берём из потока");
        info.PrimaryVideo!.IsVariableFrameRate.Should().BeTrue();
        info.PrimaryVideo!.BitrateBps.Should().BeNull("битрейта потока нет — это не повод падать");
    }

    [Fact]
    public void Overall_bitrate_is_estimated_when_container_has_none()
    {
        var info = Map(FfprobeFixtures.VariableFrameRateWebm, 88_000_000);

        info.OverallBitrateBps.Should().NotBeNull();
        info.OverallBitrateBps!.Value.Should().BeInRange(16_400_000, 16_700_000);
    }

    [Fact]
    public void Detects_alpha_channel()
    {
        var info = Map(FfprobeFixtures.AlphaWebm);

        info.PrimaryVideo!.HasAlpha.Should().BeTrue("прозрачность нужна для стикеров Telegram");
    }
}
