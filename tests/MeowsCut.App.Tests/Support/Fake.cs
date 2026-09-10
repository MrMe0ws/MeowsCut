using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.App.Tests.Support;

/// <summary>Заготовки проекта для тестов доски монтажа.</summary>
internal static class Fake
{
    public static MediaInfo Info(double seconds = 20, bool withAudio = true, string path = @"C:\видео\клип.mp4")
    {
        var duration = TimeSpan.FromSeconds(seconds);

        var video = new VideoStreamInfo(
            0, "h264", null, "High", "yuv420p",
            new FrameSize(1920, 1080),
            new Rational(30, 1), new Rational(30, 1), new Rational(1, 1),
            4_000_000, 0, false, duration);

        var audio = withAudio
            ? new[] { new AudioStreamInfo(1, "aac", null, 2, "stereo", 48_000, 192_000, duration, "und") }
            : [];

        return new MediaInfo(
            path, 30_000_000, duration,
            new ContainerInfo("mov,mp4", "QuickTime / MOV", 4_200_000),
            [video], audio, []);
    }

    public static Project Project(double seconds = 20) => Core.Editing.Project.FromMedia(Info(seconds));

    public static Sequence Sequence(double seconds = 20) => Project(seconds).Sequence;
}
