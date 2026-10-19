using FluentAssertions;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Ffmpeg.Arguments;

namespace MeowsCut.Ffmpeg.Tests.Arguments;

/// <summary>Кадрирование куска в строке фильтра.</summary>
public class TransformFilterTests
{
    private static List<string> Build(ClipTransform transform)
    {
        var chain = new List<string>();
        TransformFilter.Append(chain, transform, TimeSpan.FromSeconds(5), "30");
        return chain;
    }

    [Fact]
    public void Still_zoom_cuts_first_and_stretches_after()
    {
        // Наоборот 2000% от 4000 px потребовали бы кадр в 80 000 px.
        var chain = Build(new ClipTransform(20d, 0d, 0d));

        chain[0].Should().StartWith("crop=w=iw/20:h=ih/20");
        chain[1].Should().Be("scale=iw*20:ih*20");
    }

    [Fact]
    public void Offset_past_the_edge_is_pulled_back()
    {
        Build(new ClipTransform(3d, 5d, 0d))[0].Should().Contain("+ow*1:", "при 300% дальше 100% ширины окна некуда");
    }

    [Fact]
    public void Motion_counts_frames_at_an_even_rate()
    {
        var chain = Build(new ClipTransform(3d, 0d, 0d) with { MotionSeconds = 2 });

        chain[0].Should().Be("fps=30");
        chain[1].Should().StartWith("perspective=").And.Contain("in/60").And.Contain("eval=frame");
    }

    [Fact]
    public void Motion_longer_than_the_clip_is_squeezed_into_it()
    {
        Build(new ClipTransform(3d, 0d, 0d) with { MotionSeconds = 60 })[1].Should().Contain("in/150");
    }

    [Fact]
    public void Pull_out_reverses_the_path()
    {
        Build(new ClipTransform(3d, 0d, 0d) with { MotionSeconds = 1, MotionOut = true })[1]
            .Should().Contain("st(0,1-ld(0))");
    }

    [Fact]
    public void Shrinking_motion_draws_black_margins_instead_of_smearing()
    {
        var chain = Build(new ClipTransform(0.5d, 0d, 0d) with { MotionSeconds = 1 });

        chain[1].Should().StartWith("pad=").And.Contain("color=black");
        chain[2].Should().Contain("st(1,2*pow(0.5,ld(0)))");
    }
}
