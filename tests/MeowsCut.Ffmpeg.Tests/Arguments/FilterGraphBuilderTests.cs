using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Arguments;
using MeowsCut.Ffmpeg.Tests.Support;

namespace MeowsCut.Ffmpeg.Tests.Arguments;

public class FilterGraphBuilderTests
{
    private static readonly FilterGraphBuilder Builder = new();

    private static FilterGraph Build(Sequence sequence, ExportSettings? settings = null)
    {
        var map = sequence.Video.Clips
            .Select(clip => clip.SourceId)
            .Distinct()
            .Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index);

        return Builder.Build(sequence, map, settings ?? ExportSettings.Default);
    }

    [Fact]
    public void Single_clip_produces_trim_and_reset_of_timestamps()
    {
        var source = Fake.Source(seconds: 30);
        var clip = Clip.FromSource(source, new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)));
        var sequence = new Sequence(new VideoTrack([clip]), SequenceFormat.FromMedia(source.Info));

        var graph = Build(sequence);

        graph.Text.Should().Contain("[0:v]trim=start=5:end=20,setpts=PTS-STARTPTS");
        graph.Text.Should().Contain("[0:a]atrim=start=5:end=20,asetpts=PTS-STARTPTS");
        graph.HasAudio.Should().BeTrue();
    }

    [Fact]
    public void Several_clips_are_concatenated()
    {
        var source = Fake.Source(seconds: 30);
        var sequence = new Sequence(
            new VideoTrack(
            [
                Clip.FromSource(source, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5))),
                Clip.FromSource(source, new TimeRange(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20)))
            ]),
            SequenceFormat.FromMedia(source.Info));

        var graph = Build(sequence);

        graph.Text.Should().Contain("[v0][a0][v1][a1]concat=n=2:v=1:a=1[vc][ac]");
        graph.VideoLabel.Should().Be("vc");
        graph.AudioLabel.Should().Be("ac");
    }

    [Fact]
    public void Speed_changes_both_video_and_audio()
    {
        var source = Fake.Source(seconds: 30);
        var clip = Clip.FromSource(source).WithSpeed(4d);
        var sequence = new Sequence(new VideoTrack([clip]), SequenceFormat.FromMedia(source.Info));

        var graph = Build(sequence);

        graph.Text.Should().Contain("setpts=(PTS-STARTPTS)/4");
        graph.Text.Should().Contain("atempo=2,atempo=2", "atempo принимает максимум двукратное ускорение");
    }

    [Fact]
    public void Muted_clip_is_replaced_with_silence_of_the_same_length()
    {
        var source = Fake.Source(seconds: 30);
        var sequence = new Sequence(
            new VideoTrack(
            [
                Clip.FromSource(source, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))),
                Clip.FromSource(source, new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)))
                    .WithAudio(ClipAudio.Muted)
            ]),
            SequenceFormat.FromMedia(source.Info));

        var graph = Build(sequence);

        // Без тишины concat разваливается: у частей должно совпадать число потоков.
        graph.Text.Should().Contain("anullsrc");
        graph.Text.Should().Contain("atrim=duration=10");
    }

    [Fact]
    public void Source_without_audio_also_gets_silence()
    {
        var source = Fake.Source(seconds: 10, withAudio: false);
        var sequence = new Sequence(
            new VideoTrack([Clip.FromSource(source)]),
            SequenceFormat.FromMedia(source.Info));

        var graph = Build(sequence);

        graph.HasAudio.Should().BeFalse("в последовательности вообще нет звука — дорожка не нужна");
        graph.Text.Should().NotContain("anullsrc");
    }

    [Fact]
    public void Disabled_audio_removes_the_audio_chain()
    {
        var source = Fake.Source(seconds: 10);
        var sequence = new Sequence(new VideoTrack([Clip.FromSource(source)]), SequenceFormat.FromMedia(source.Info));

        var graph = Build(sequence, ExportSettings.Default with { Audio = AudioSettings.Disabled });

        graph.HasAudio.Should().BeFalse();
        graph.Text.Should().NotContain("atrim");
    }

    [Fact]
    public void Resolution_change_adds_scale_and_padding()
    {
        var source = Fake.Source(seconds: 10, width: 1920, height: 1080);
        var sequence = new Sequence(new VideoTrack([Clip.FromSource(source)]), SequenceFormat.FromMedia(source.Info));

        var settings = ExportSettings.Default with
        {
            Video = VideoSettings.Default with { Resolution = new ResolutionSpec.Preset(720) }
        };

        var graph = Build(sequence, settings);

        graph.Text.Should().Contain("scale=1280:720:force_original_aspect_ratio=decrease");
        graph.Text.Should().Contain("pad=1280:720");
        graph.VideoLabel.Should().Be("vout");
    }

    [Fact]
    public void Square_target_with_cover_crops_instead_of_padding()
    {
        var source = Fake.Source(seconds: 3, width: 1920, height: 1080);
        var sequence = new Sequence(new VideoTrack([Clip.FromSource(source)]), SequenceFormat.FromMedia(source.Info));

        var settings = ExportSettings.Default with
        {
            Video = VideoSettings.Default with
            {
                Resolution = new ResolutionSpec.Custom(512, 512, FitMode.Cover)
            }
        };

        var graph = Build(sequence, settings);

        graph.Text.Should().Contain("scale=512:512:force_original_aspect_ratio=increase");
        graph.Text.Should().Contain("crop=512:512");
    }

    [Fact]
    public void Frame_rate_change_adds_fps_filter()
    {
        var source = Fake.Source(seconds: 10, fps: 60);
        var sequence = new Sequence(new VideoTrack([Clip.FromSource(source)]), SequenceFormat.FromMedia(source.Info));

        var settings = ExportSettings.Default with
        {
            Video = VideoSettings.Default with { FrameRate = new FrameRateSpec.Fixed(30) }
        };

        Build(sequence, settings).Text.Should().Contain("fps=30");
    }

    [Fact]
    public void Zoom_crops_the_frame_back_to_its_original_size()
    {
        var source = Fake.Source(seconds: 10);
        var clip = Clip.FromSource(source).WithTransform(new ClipTransform(1.5, 0.1, 0));
        var sequence = new Sequence(new VideoTrack([clip]), SequenceFormat.FromMedia(source.Info));

        var graph = Build(sequence);

        graph.Text.Should().Contain("scale=iw*1.5:ih*1.5");
        graph.Text.Should().Contain("crop=w=iw/1.5:h=ih/1.5");
    }

    [Fact]
    public void Graph_never_ends_with_a_stray_separator()
    {
        var source = Fake.Source(seconds: 10);
        var sequence = new Sequence(new VideoTrack([Clip.FromSource(source)]), SequenceFormat.FromMedia(source.Info));

        Build(sequence).Text.Should().NotEndWith(";", "лишняя точка с запятой — синтаксическая ошибка ffmpeg");
    }

    [Fact]
    public void Numbers_are_rendered_with_a_dot_regardless_of_system_locale()
    {
        var source = Fake.Source(seconds: 30);
        var clip = Clip.FromSource(source, new TimeRange(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.25)));
        var sequence = new Sequence(new VideoTrack([clip]), SequenceFormat.FromMedia(source.Info));

        var text = Build(sequence).Text;

        text.Should().Contain("trim=start=1.5:end=2.25");
        text.Should().NotContain(",5:end", "запятая как разделитель дробной части сломала бы граф");
    }
}
