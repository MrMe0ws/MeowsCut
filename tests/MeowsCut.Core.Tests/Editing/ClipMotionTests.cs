using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;
using MeowsCut.Core.Projects;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>Наезд: откуда кадр едет, куда и как быстро.</summary>
public class ClipMotionTests
{
    private static readonly TimeSpan Clip = TimeSpan.FromSeconds(10);

    private static ClipTransform PushIn(double seconds = 4) =>
        new ClipTransform(3d, 0.4d, -0.2d) with { MotionSeconds = seconds };

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Push_in_starts_at_full_frame()
    {
        var start = PushIn().At(TimeSpan.Zero, Clip);

        start.Zoom.Should().BeApproximately(1d, 1e-9);
        start.OffsetX.Should().Be(0);
    }

    [Fact]
    public void Push_in_arrives_and_stays()
    {
        PushIn().At(S(4), Clip).Zoom.Should().BeApproximately(3d, 1e-9);
        PushIn().At(S(9), Clip).Zoom.Should().BeApproximately(3d, 1e-9, "после наезда кадр стоит");
        PushIn().At(S(9), Clip).OffsetX.Should().BeApproximately(0.4d, 1e-9);
    }

    [Fact]
    public void Zoom_grows_by_ratio_not_by_step()
    {
        // Середина пути по времени — середина и по ощущению: √3, а не 2.
        PushIn().At(S(2), Clip).Zoom.Should().BeApproximately(Math.Sqrt(3d), 1e-9);
    }

    [Fact]
    public void Starts_and_ends_softly()
    {
        ClipMotion.Ease(0.05).Should().BeLessThan(0.05, "без рывка в начале");
        ClipMotion.Ease(0.95).Should().BeGreaterThan(0.95, "и без удара в конце");
    }

    [Fact]
    public void Pull_out_goes_the_other_way()
    {
        var pull = PushIn() with { MotionOut = true };

        pull.At(TimeSpan.Zero, Clip).Zoom.Should().BeApproximately(3d, 1e-9);
        pull.At(S(4), Clip).Zoom.Should().BeApproximately(1d, 1e-9);
    }

    [Fact]
    public void Motion_longer_than_the_clip_still_arrives()
    {
        PushIn(seconds: 60).At(Clip, Clip).Zoom.Should().BeApproximately(3d, 1e-9);
    }

    [Fact]
    public void Offset_never_shows_past_the_edge()
    {
        // При 300% окно ходит на (3−1)/2 = 100% своей ширины, не дальше.
        var far = new ClipTransform(3d, 2.5d, -4d).Clamped();

        far.OffsetX.Should().Be(1d);
        far.OffsetY.Should().Be(-1d);
        new ClipTransform(1d, 0.3d, 0d).Clamped().OffsetX.Should().Be(0, "на 100% двигать нечего");
    }

    [Fact]
    public void Motion_to_full_frame_is_no_motion()
    {
        (ClipTransform.Identity with { MotionSeconds = 3 }).HasMotion.Should().BeFalse();
    }

    [Fact]
    public void Zoom_is_no_longer_capped_at_ten()
    {
        ClipTransform.Identity.WithZoom(20d).Zoom.Should().Be(20d);
    }

    [Fact]
    public void Motion_survives_the_draft()
    {
        var project = Project.FromMedia(Fake.Info(10));
        var clip = project.Sequence.Video.Clips[0];
        var moved = project.WithSequence(project.Sequence.WithTrack(
            project.Sequence.Video.Replace(clip.WithTransform(PushIn() with { MotionOut = true }))));
        var media = moved.Sources.ToDictionary(source => source.Id.Value, source => source.Info);

        var (restored, _) = ProjectDocumentReader.Restore(ProjectDocumentWriter.Create(moved), media);

        var transform = restored.Sequence.Video.Clips[0].Transform;
        transform.MotionSeconds.Should().Be(4);
        transform.MotionOut.Should().BeTrue();
    }

    [Fact]
    public void Sound_fades_in_and_out_by_volume()
    {
        var sound = new AudioClip(
            AudioClipId.New(), new SourceId(Guid.NewGuid()), new TimeRange(TimeSpan.Zero, S(10)), S(5))
            .WithFades(S(2), S(4));

        sound.FadeGainAt(S(5)).Should().Be(0, "на первом сэмпле — тишина");
        sound.FadeGainAt(S(6)).Should().BeApproximately(0.5, 1e-9);
        sound.FadeGainAt(S(9)).Should().Be(1);
        sound.FadeGainAt(S(13)).Should().BeApproximately(0.5, 1e-9, "за 2 с до конца при затухании 4 с");
    }
}
