using FluentAssertions;
using MeowsCut.App.Timeline;

namespace MeowsCut.App.Tests.Timeline;

/// <summary>Кадры полосы берутся из точек сетки — после правок они уже в кэше.</summary>
public class ThumbnailGridTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Nearby_points_share_one_frame()
    {
        // Обрезка сдвинула середины ячеек на долю секунды — кадр тот же.
        var span = S(4);

        ThumbnailGrid.Snap(S(10.13), span).Should().Be(ThumbnailGrid.Snap(S(10.71), span));
    }

    [Fact]
    public void Step_grows_with_the_slot_and_stays_a_power_of_two()
    {
        ThumbnailGrid.Snap(S(10.7), S(0.1)).Should().Be(S(10.5));
        ThumbnailGrid.Snap(S(10.7), S(4)).Should().Be(S(10));
        ThumbnailGrid.Snap(S(10.7), S(30)).Should().Be(S(0));
    }

    [Fact]
    public void Coarser_zoom_reuses_points_of_the_finer_one()
    {
        // Шаги кратны: всё, что выбрано на крупном масштабе, есть и на мелком.
        var coarse = ThumbnailGrid.Snap(S(37.3), S(16));
        var fine = ThumbnailGrid.Snap(coarse, S(2));

        fine.Should().Be(coarse);
    }

    [Fact]
    public void Never_snaps_past_the_point()
    {
        ThumbnailGrid.Snap(S(3.9), S(8)).Should().BeLessThanOrEqualTo(S(3.9));
    }
}
