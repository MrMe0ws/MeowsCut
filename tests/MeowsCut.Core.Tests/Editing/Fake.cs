using MeowsCut.Core.Editing;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Заготовки данных для тестов таймлайна: настоящий ffprobe здесь не нужен.
/// </summary>
internal static class Fake
{
    public static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    public static MediaInfo Info(
        double seconds = 60,
        bool withAudio = true,
        int width = 1920,
        int height = 1080,
        double fps = 30,
        string path = @"C:\видео\клип с пробелом.mp4")
    {
        var duration = S(seconds);

        var video = new VideoStreamInfo(
            Index: 0,
            CodecName: "h264",
            CodecLongName: null,
            Profile: "High",
            PixelFormat: "yuv420p",
            Size: new FrameSize(width, height),
            FrameRate: Rational.FromDouble(fps),
            AverageFrameRate: Rational.FromDouble(fps),
            SampleAspectRatio: new Rational(1, 1),
            BitrateBps: 4_000_000,
            RotationDegrees: 0,
            HasAlpha: false,
            Duration: duration);

        var audio = withAudio
            ? new[]
            {
                new AudioStreamInfo(1, "aac", null, 2, "stereo", 48_000, 192_000, duration, "und")
            }
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
        MediaSource.FromMedia(Info(seconds, withAudio, width, height, fps, path));
}
