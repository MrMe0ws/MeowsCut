using FluentAssertions;
using MeowsCut.App.Playback;

namespace MeowsCut.App.Tests.Playback;

/// <summary>Наложенная музыка ждёт, пока пойдёт картинка, и замирает вместе с ней.</summary>
public class PlaybackMotionTests
{
    private TimeSpan _now;

    private static TimeSpan Ms(double value) => TimeSpan.FromMilliseconds(value);

    private PlaybackMotion Create() => new(() => _now);

    [Fact]
    public void Music_waits_while_the_video_spins_up()
    {
        var motion = Create();
        motion.Restart(Ms(0));

        for (var i = 0; i < 20; i++)
        {
            _now += Ms(33);
            motion.Observe(Ms(0), playing: true, waitingForPlayer: false).Should().BeFalse("4K ещё раскачивается — позиция стоит");
        }

        _now += Ms(33);
        motion.Observe(Ms(30), playing: true, waitingForPlayer: false).Should().BeTrue("картинка пошла — звук вместе с ней");
    }

    [Fact]
    public void One_tick_without_movement_is_not_a_stall()
    {
        var motion = Create();
        motion.Restart(Ms(0));

        _now += Ms(33);
        motion.Observe(Ms(33), true, false);

        // Проигрыватель обновляет позицию кадрами — два шага с одним значением бывают.
        _now += Ms(33);
        motion.Observe(Ms(33), true, false).Should().BeTrue();
    }

    [Fact]
    public void Frozen_picture_stops_the_music()
    {
        var motion = Create();
        motion.Restart(Ms(0));
        _now += Ms(33);
        motion.Observe(Ms(33), true, false);

        _now += PlaybackMotion.StallThreshold + Ms(1);

        motion.Observe(Ms(33), true, false).Should().BeFalse();
    }

    [Fact]
    public void Opening_the_next_file_holds_the_music_even_if_the_clock_moves()
    {
        var motion = Create();
        motion.Restart(Ms(0));

        _now += Ms(33);
        motion.Observe(Ms(33), true, waitingForPlayer: true).Should().BeFalse();
    }

    [Fact]
    public void Seeking_back_waits_for_movement_again()
    {
        var motion = Create();
        motion.Restart(Ms(0));
        _now += Ms(33);
        motion.Observe(Ms(5000), true, false);

        _now += Ms(33);
        motion.Observe(Ms(1000), true, false).Should().BeFalse("позиция ушла назад — это перемотка, а не ход ролика");
    }
}
