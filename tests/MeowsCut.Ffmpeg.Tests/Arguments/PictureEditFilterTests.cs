using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Arguments;
using MeowsCut.Ffmpeg.Tests.Support;

namespace MeowsCut.Ffmpeg.Tests.Arguments;

/// <summary>
/// Затухание, поворот, выравнивание громкости и звук без картинки в графе фильтров.
/// </summary>
public class PictureEditFilterTests
{
    private static readonly FilterGraphBuilder Builder = new();

    private static FilterGraph Build(Sequence sequence, ExportSettings? settings = null)
    {
        var map = sequence.Video.Clips
            .Select(clip => clip.SourceId)
            .Concat(sequence.AudioTracks.SelectMany(track => track.Clips).Select(clip => clip.SourceId))
            .Distinct()
            .Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index);

        return Builder.Build(sequence, map, settings ?? ExportSettings.Default);
    }

    private static Sequence Single(Func<Clip, Clip> change, double seconds = 10)
    {
        var source = Fake.Source(seconds: 60);
        var clip = Clip.FromSource(source, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)));

        return new Sequence(new VideoTrack([change(clip)]), SequenceFormat.FromMedia(source.Info));
    }

    [Fact]
    public void Fade_in_starts_at_zero_of_the_clip()
    {
        // Отсчёт идёт от нуля, а не от точки входа в исходнике: цепочка стоит
        // после setpts=PTS-STARTPTS, и клип к этому месту начинается с нуля.
        var source = Fake.Source(seconds: 60);
        var clip = Clip
            .FromSource(source, new TimeRange(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)))
            .WithFades(fadeIn: TimeSpan.FromSeconds(1.5));

        var graph = Build(new Sequence(new VideoTrack([clip]), SequenceFormat.FromMedia(source.Info)));

        graph.Text.Should().Contain("fade=t=in:st=0:d=1.5");
    }

    [Fact]
    public void Fade_out_ends_exactly_at_the_end_of_the_clip()
    {
        var sequence = Single(clip => clip.WithFades(fadeOut: TimeSpan.FromSeconds(2)), seconds: 10);

        var graph = Build(sequence);

        graph.Text.Should().Contain("fade=t=out:st=8:d=2");
    }

    [Fact]
    public void Sped_up_clip_fades_by_timeline_time()
    {
        // Десять секунд источника на двойной скорости — это пять секунд ленты,
        // и затухание обязано начаться на третьей, а не на восьмой.
        var sequence = Single(clip => clip.WithSpeed(2d).WithFades(fadeOut: TimeSpan.FromSeconds(2)));

        var graph = Build(sequence);

        graph.Text.Should().Contain("fade=t=out:st=3:d=2");
    }

    [Fact]
    public void Clip_without_fades_has_no_fade_filter()
    {
        Build(Single(clip => clip)).Text.Should().NotContain("fade=");
    }

    [Theory]
    [InlineData(90, "transpose=1")]
    [InlineData(270, "transpose=2")]
    public void Quarter_turn_uses_transpose(int rotation, string expected)
    {
        var graph = Build(Single(clip => clip.WithTransform(ClipTransform.Identity.WithRotation(rotation))));

        graph.Text.Should().Contain(expected);
    }

    [Fact]
    public void Half_turn_flips_both_axes()
    {
        var graph = Build(Single(clip => clip.WithTransform(ClipTransform.Identity.WithRotation(180))));

        graph.Text.Should().Contain("hflip,vflip");
    }

    [Fact]
    public void Rotated_clip_is_scaled_back_to_the_sequence_frame()
    {
        // Поворот меняет ширину и высоту местами: без приведения к общему размеру
        // concat отказался бы склеивать такой кусок с соседями.
        var graph = Build(Single(clip => clip.WithTransform(ClipTransform.Identity.WithRotation(90))));

        graph.Text.Should().Contain("scale=1920:1080:force_original_aspect_ratio=decrease");
        graph.Text.Should().Contain("setsar=1");
    }

    [Fact]
    public void Rotation_comes_before_zoom()
    {
        // Масштаб пользователь выбирает, глядя на уже развёрнутый кадр.
        var graph = Build(Single(clip =>
            clip.WithTransform(ClipTransform.Identity.WithRotation(90).WithZoom(1.5))));

        var rotate = graph.Text.IndexOf("transpose=1", StringComparison.Ordinal);
        var zoom = graph.Text.IndexOf("scale=iw*1.5", StringComparison.Ordinal);

        rotate.Should().BeGreaterThan(-1);
        zoom.Should().BeGreaterThan(rotate);
    }

    [Fact]
    public void Loudness_normalization_is_the_last_step_of_the_audio_chain()
    {
        var settings = ExportSettings.Default with
        {
            Audio = AudioSettings.Default with { NormalizeLoudness = true, MasterVolume = 1.5 }
        };

        var graph = Build(Single(clip => clip), settings);

        graph.Text.Should().Contain("loudnorm=I=-16:TP=-1.5:LRA=11");
        graph.Text.IndexOf("volume=1.5", StringComparison.Ordinal)
            .Should().BeLessThan(graph.Text.IndexOf("loudnorm", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_checkbox_there_is_no_loudnorm()
    {
        Build(Single(clip => clip)).Text.Should().NotContain("loudnorm");
    }

    [Fact]
    public void Audio_only_export_builds_no_video_chains()
    {
        var settings = ExportSettings.Default with { Container = ContainerFormat.Mp3 };

        var graph = Build(Single(clip => clip), settings);

        graph.HasVideo.Should().BeFalse();
        graph.VideoLabel.Should().BeNull();
        graph.HasAudio.Should().BeTrue();
        graph.Text.Should().NotContain("[0:v]");
        graph.Text.Should().NotContain("concat=n=1:v=1");
    }

    [Fact]
    public void Audio_only_keeps_gaps_as_silence()
    {
        // Иначе куски звука сомкнутся вплотную и вытащенная дорожка
        // разойдётся с роликом, из которого её взяли.
        var source = Fake.Source(seconds: 60);
        var first = Clip.FromSource(source, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)));
        var second = Clip
            .FromSource(source, new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15)))
            .WithLeadingGap(TimeSpan.FromSeconds(3));

        var sequence = new Sequence(new VideoTrack([first, second]), SequenceFormat.FromMedia(source.Info));
        var graph = Build(sequence, ExportSettings.Default with { Container = ContainerFormat.M4a });

        graph.Text.Should().Contain("anullsrc");
        graph.Text.Should().Contain("atrim=duration=3");
        graph.Text.Should().Contain("concat=n=3:v=0:a=1[ac]");
    }

    [Fact]
    public void Audio_only_export_of_a_silent_sequence_is_refused()
    {
        var source = Fake.Source(seconds: 60, withAudio: false);
        var sequence = new Sequence(
            new VideoTrack([Clip.FromSource(source, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)))]),
            SequenceFormat.FromMedia(source.Info));

        var act = () => Build(sequence, ExportSettings.Default with { Container = ContainerFormat.Mp3 });

        act.Should().Throw<InvalidOperationException>();
    }
}
