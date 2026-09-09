using MeowsCut.Core.Editing;
using MeowsCut.Core.Media;

namespace MeowsCut.Ffmpeg.Tests.Support;

/// <summary>Заготовки медиа для тестов планировщика и графа фильтров.</summary>
internal static class Fake
{
    public static MediaInfo Info(
        double seconds = 60,
        bool withAudio = true,
        int width = 1920,
        int height = 1080,
        double fps = 30,
        string videoCodec = "h264",
        string audioCodec = "aac",
        bool variableFrameRate = false,
        string path = @"C:\видео\клип с пробелом.mp4")
    {
        var duration = TimeSpan.FromSeconds(seconds);

        var video = new VideoStreamInfo(
            Index: 0,
            CodecName: videoCodec,
            CodecLongName: null,
            Profile: "High",
            PixelFormat: "yuv420p",
            Size: new FrameSize(width, height),
            FrameRate: variableFrameRate ? new Rational(1000, 1) : Rational.FromDouble(fps),
            AverageFrameRate: Rational.FromDouble(fps),
            SampleAspectRatio: new Rational(1, 1),
            BitrateBps: 4_000_000,
            RotationDegrees: 0,
            HasAlpha: false,
            Duration: duration);

        var audio = withAudio
            ? new[] { new AudioStreamInfo(1, audioCodec, null, 2, "stereo", 48_000, 192_000, duration, "und") }
            : [];

        return new MediaInfo(
            path,
            FileSizeBytes: 30_000_000,
            duration,
            new ContainerInfo("mov,mp4", "QuickTime / MOV", 4_200_000),
            [video],
            audio,
            []);
    }

    public static MediaSource Source(
        double seconds = 60,
        bool withAudio = true,
        int width = 1920,
        int height = 1080,
        double fps = 30,
        string path = @"C:\видео\клип с пробелом.mp4") =>
        MediaSource.FromMedia(Info(seconds, withAudio, width, height, fps, path: path));
}
