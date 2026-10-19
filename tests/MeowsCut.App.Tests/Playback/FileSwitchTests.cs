using FluentAssertions;
using MeowsCut.App.Playback;
using MeowsCut.App.Tests.Support;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Tests.Playback;

/// <summary>
/// Переход на клип из другого файла посреди воспроизведения.
/// </summary>
/// <remarks>
/// Здесь жил повтор куска: проигрыватель получал «играть» до открытия файла,
/// отыгрывал кусок со звуком, а обработчик открытия отматывал его к началу клипа.
/// </remarks>
public class FileSwitchTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static (SequencePlaybackController Playback, FakeMediaPlayer Player) PlayToTheSecondFile()
    {
        var project = Project.FromMedia(Fake.Info(5, path: @"C:\видео\первый.mp4"));
        var (withSecond, second) = project.WithSource(Fake.Info(10, path: @"C:\видео\второй.mp4"));

        var clip = Clip.FromSource(second, new Core.Media.TimeRange(S(2), S(10)));
        var sequence = withSecond.Sequence.WithTrack(withSecond.Sequence.Video.Append(clip));

        var player = new FakeMediaPlayer { OpenImmediately = false };
        var playback = new SequencePlaybackController(player);
        playback.Attach(withSecond.WithSequence(sequence));
        player.CompleteOpen();

        playback.Play();
        player.ForcePosition(S(5));
        playback.Tick();

        return (playback, player);
    }

    [Fact]
    public void Next_file_is_not_started_before_it_is_open()
    {
        var (playback, player) = PlayToTheSecondFile();

        player.Source.Should().EndWith("второй.mp4");
        player.IsPlaying.Should().BeFalse("до открытия запуск отыграл бы кусок, который потом отмотают назад");
        playback.IsPlaying.Should().BeTrue("для пользователя ролик идёт — просто ждёт файл");
        playback.IsWaitingForPlayer.Should().BeTrue();
    }

    [Fact]
    public void Next_file_starts_from_the_clip_in_point_once_open()
    {
        var (playback, player) = PlayToTheSecondFile();

        player.CompleteOpen();

        player.IsPlaying.Should().BeTrue();
        player.Position.Should().Be(S(2), "клип начинается со второй секунды файла");
        playback.IsWaitingForPlayer.Should().BeFalse();
    }

    [Fact]
    public void Pause_while_waiting_cancels_the_deferred_start()
    {
        var (playback, player) = PlayToTheSecondFile();

        playback.Pause();
        player.CompleteOpen();

        player.IsPlaying.Should().BeFalse();
    }
}
