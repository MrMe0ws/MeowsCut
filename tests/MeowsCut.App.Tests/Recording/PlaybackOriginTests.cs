using System.Diagnostics;
using FluentAssertions;
using MeowsCut.App.Recording;

namespace MeowsCut.App.Tests.Recording;

/// <summary>
/// Когда на самом деле пошёл ролик: от этого зависит, совпадёт ли голос с губами.
/// </summary>
public class PlaybackOriginTests
{
    private static readonly TimeSpan Start = TimeSpan.FromSeconds(10);

    private static long Ticks(double milliseconds) => (long)(milliseconds / 1000d * Stopwatch.Frequency);

    private static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void Without_movement_the_request_time_is_used()
    {
        var origin = new PlaybackOrigin(Start, requestedAt: 1_000);

        origin.Observe(Start, 5_000);
        origin.Observe(Start, 9_000);

        origin.Timestamp.Should().Be(1_000, "ролик стоит — например, под курсором фотография без проигрывателя");
    }

    [Fact]
    public void Player_start_delay_is_measured_not_assumed()
    {
        var requested = Ticks(1_000);
        var actualStart = requested + Ticks(180); // проигрыватель раскачивался 180 мс
        var origin = new PlaybackOrigin(Start, requested);

        for (var step = 1; step <= PlaybackOrigin.SampleCount; step++)
        {
            var elapsed = step * 33d;
            origin.Observe(Start + Ms(elapsed), actualStart + Ticks(elapsed));
        }

        origin.IsSettled.Should().BeTrue();
        (origin.Timestamp - actualStart).Should().BeInRange(-Ticks(1), Ticks(1));
    }

    [Fact]
    public void Single_jittery_tick_does_not_move_the_estimate()
    {
        var actualStart = Ticks(2_000);
        var origin = new PlaybackOrigin(Start, Ticks(1_900));

        for (var step = 1; step <= 9; step++)
        {
            var elapsed = step * 33d;
            // Пятый шаг пришёл на 120 мс позже — таймер интерфейса задержался
            var late = step == 5 ? 120d : 0d;
            origin.Observe(Start + Ms(elapsed), actualStart + Ticks(elapsed + late));
        }

        (origin.Timestamp - actualStart).Should().BeInRange(-Ticks(1), Ticks(1), "берётся медиана, а не среднее");
    }

    [Fact]
    public void Backward_jump_is_ignored()
    {
        var actualStart = Ticks(2_000);
        var origin = new PlaybackOrigin(Start, Ticks(1_900));

        origin.Observe(Start + Ms(33), actualStart + Ticks(33));
        origin.Observe(Start + Ms(10), actualStart + Ticks(66)); // откат позиции на стыке клипов
        origin.Observe(Start + Ms(99), actualStart + Ticks(99));

        (origin.Timestamp - actualStart).Should().BeInRange(-Ticks(1), Ticks(1));
    }
}
