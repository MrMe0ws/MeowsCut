using FluentAssertions;
using MeowsCut.App.Timeline;
using MeowsCut.App.Tests.Support;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Tests.Timeline;

public class SnapEngineTests
{
    private static readonly TimelineMetrics Metrics = new() { PixelsPerSecond = 50, ViewportWidth = 1000 };

    /// <summary>Клип 0–20 с, разрезанный на 0–5 и 5–20.</summary>
    private static Sequence TwoClips() =>
        new SplitClipCommand(TimeSpan.FromSeconds(5)).Apply(Fake.Sequence(20));

    [Fact]
    public void Snaps_to_a_nearby_clip_boundary()
    {
        var snap = new SnapEngine();

        // 5.1 с — это 5 пикселей от стыка при таком масштабе, порог 8.
        var result = snap.Snap(TimeSpan.FromSeconds(5.1), TwoClips(), Metrics);

        result.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Leaves_distant_points_alone()
    {
        var snap = new SnapEngine();

        var result = snap.Snap(TimeSpan.FromSeconds(7), TwoClips(), Metrics);

        result.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void Threshold_is_measured_in_pixels_not_seconds()
    {
        var snap = new SnapEngine();
        var zoomedOut = new TimelineMetrics { PixelsPerSecond = 5, ViewportWidth = 1000 };

        // На мелком масштабе те же 8 пикселей — это уже больше секунды.
        snap.Snap(TimeSpan.FromSeconds(6), TwoClips(), zoomedOut).Should().Be(TimeSpan.FromSeconds(5));
        snap.Snap(TimeSpan.FromSeconds(6), TwoClips(), Metrics).Should().Be(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void Disabled_snapping_changes_nothing()
    {
        var snap = new SnapEngine { IsEnabled = false };

        snap.Snap(TimeSpan.FromSeconds(5.05), TwoClips(), Metrics).Should().Be(TimeSpan.FromSeconds(5.05));
    }

    [Fact]
    public void Snaps_to_the_playhead()
    {
        var snap = new SnapEngine();

        var result = snap.Snap(TimeSpan.FromSeconds(12.05), TwoClips(), Metrics, TimeSpan.FromSeconds(12));

        result.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void Snaps_to_the_start_and_the_end_of_the_sequence()
    {
        var snap = new SnapEngine();
        var sequence = TwoClips();

        snap.Snap(TimeSpan.FromSeconds(0.05), sequence, Metrics).Should().Be(TimeSpan.Zero);
        snap.Snap(TimeSpan.FromSeconds(19.95), sequence, Metrics).Should().Be(TimeSpan.FromSeconds(20));
    }
}

public class TimelineHitTesterTests
{
    private static readonly TimelineMetrics Metrics = new() { PixelsPerSecond = 50, ViewportWidth = 1000 };

    private static Sequence TwoClips() =>
        new SplitClipCommand(TimeSpan.FromSeconds(5)).Apply(Fake.Sequence(20));

    [Fact]
    public void Clicking_the_ruler_is_recognised()
    {
        var hit = new TimelineHitTester().Test(100, 5, TwoClips(), Metrics);

        hit.Kind.Should().Be(TimelineHitKind.Ruler);
    }

    [Fact]
    public void Clicking_the_middle_of_a_clip_selects_its_body()
    {
        var hit = new TimelineHitTester().Test(125, 60, TwoClips(), Metrics);

        hit.Kind.Should().Be(TimelineHitKind.ClipBody);
        hit.Clip!.Value.Index.Should().Be(0, "125 пикселей — это 2.5 с, первый клип");
    }

    [Fact]
    public void Clip_edges_are_grab_handles()
    {
        var tester = new TimelineHitTester();
        var sequence = TwoClips();

        // Стык клипов на 250 пикселях: слева конец первого, справа начало второго.
        tester.Test(247, 60, sequence, Metrics).Kind.Should().Be(TimelineHitKind.ClipEndEdge);
        tester.Test(253, 60, sequence, Metrics).Kind.Should().Be(TimelineHitKind.ClipStartEdge);
    }

    [Fact]
    public void Handles_never_eat_more_than_a_third_of_a_short_clip()
    {
        var tiny = new TimelineMetrics { PixelsPerSecond = 2, ViewportWidth = 1000 };
        var tester = new TimelineHitTester();

        // Клип шириной 10 пикселей: середина обязана оставаться телом клипа.
        var hit = tester.Test(5, 60, Fake.Sequence(5), tiny);

        hit.Kind.Should().Be(TimelineHitKind.ClipBody);
    }

    [Fact]
    public void Clicking_past_the_last_clip_hits_nothing()
    {
        // 1200 пикселей при 50 px/с — это 24 секунды, за концом двадцатисекундной дорожки.
        var hit = new TimelineHitTester().Test(1200, 60, TwoClips(), Metrics);

        hit.Kind.Should().Be(TimelineHitKind.Empty);
        hit.Clip.Should().BeNull();
    }

}
