using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Arguments;
using MeowsCut.Ffmpeg.Tests.Support;

namespace MeowsCut.Ffmpeg.Tests.Arguments;

/// <summary>
/// Вертикальный формат: как горизонтальный кусок ложится в кадр 9:16.
/// </summary>
public class SequenceFormatFilterTests
{
    private static readonly FilterGraphBuilder Builder = new();

    private static FilterGraph Build(Sequence sequence)
    {
        var map = sequence.Video.Clips
            .Select(clip => clip.SourceId)
            .Distinct()
            .Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index);

        return Builder.Build(sequence, map, ExportSettings.Default);
    }

    private static Sequence Horizontal()
    {
        var source = Fake.Source(seconds: 30);
        var clip = Clip.FromSource(source, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)));

        return new Sequence(new VideoTrack([clip]), SequenceFormat.FromMedia(source.Info));
    }

    [Fact]
    public void Format_from_the_file_adds_no_scaling()
    {
        // Куски одного файла и так одного размера: лишние фильтры только
        // отняли бы время у каждого кадра.
        Build(Horizontal()).Text.Should().NotContain("scale=");
    }

    [Fact]
    public void Vertical_format_fits_the_horizontal_clip_with_black_bars()
    {
        var sequence = Horizontal();
        var vertical = sequence with { Format = sequence.Format.WithAspect(9d / 16d) };

        var graph = Build(vertical).Text;

        graph.Should().Contain("scale=1080:1920:force_original_aspect_ratio=decrease");
        graph.Should().Contain("pad=1080:1920");
    }

    [Fact]
    public void Filling_the_frame_crops_instead_of_padding()
    {
        var sequence = Horizontal();
        var vertical = sequence with
        {
            Format = sequence.Format.WithAspect(9d / 16d) with { Fit = FitMode.Cover }
        };

        var graph = Build(vertical).Text;

        graph.Should().Contain("scale=1080:1920:force_original_aspect_ratio=increase");
        graph.Should().Contain("crop=1080:1920");
        graph.Should().NotContain("pad=1080:1920");
    }

    [Fact]
    public void Per_clip_framing_still_works_inside_the_vertical_frame()
    {
        // Масштаб куска применяется до приведения к общему кадру: пользователь
        // выбирает, какая часть картинки попадёт в вертикальный формат.
        var sequence = Horizontal();
        var clip = sequence.Video.Clips[0].WithTransform(ClipTransform.Identity.WithZoom(1.5));

        var vertical = sequence.WithTrack(sequence.Video.Replace(clip)) with
        {
            Format = sequence.Format.WithAspect(9d / 16d)
        };

        var graph = Build(vertical).Text;

        graph.IndexOf("scale=iw*1.5", StringComparison.Ordinal)
            .Should().BeLessThan(graph.IndexOf("scale=1080:1920", StringComparison.Ordinal));
    }
}
