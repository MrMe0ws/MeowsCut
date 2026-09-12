using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Arguments;
using MeowsCut.Ffmpeg.Tests.Support;

namespace MeowsCut.Ffmpeg.Tests.Arguments;

/// <summary>
/// Надписи: положение, размер и то, как текст доезжает до ffmpeg.
/// </summary>
/// <remarks>
/// Текст едет файлом, а не строкой: апостроф в слове «дом'ой» закрывает
/// кавычку значения, остаток фильтра уезжает внутрь текста, и граф рассыпается
/// с сообщением «No option name near…». Поэтому здесь проверяется не
/// экранирование текста, а то, что он вообще не попадает в строку.
/// </remarks>
public class TitleFilterTests
{
    private const string TextFile = @"C:\temp\meows\titles\ab12.txt";

    private static string Build(TitleClip title) =>
        TitleFilter.Build([title], new Dictionary<TitleId, string> { [title.Id] = TextFile }).Single();

    private static TitleClip Title(string text = "Привет") =>
        TitleClip.Create(text, TimeSpan.FromSeconds(2));

    [Fact]
    public void Titles_reach_the_whole_graph()
    {
        // Проверка сквозная: фильтр может быть правильным, а в граф не попасть —
        // ровно это и случилось, когда сборка не заявляла drawtext.
        var source = Fake.Source(seconds: 30);
        var clip = Clip.FromSource(source, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)));
        var title = TitleClip.Create("Привет", TimeSpan.FromSeconds(1));

        var sequence = new Sequence(new VideoTrack([clip]), SequenceFormat.FromMedia(source.Info))
            .WithTitles([title]);

        var builder = new FilterGraphBuilder
        {
            TitleTextFiles = new Dictionary<TitleId, string> { [title.Id] = TextFile }
        };

        var graph = builder.Build(
            sequence,
            new Dictionary<SourceId, int> { [source.Id] = 0 },
            Core.Export.ExportSettings.Default);

        graph.Text.Should().Contain("drawtext=");
        graph.VideoLabel.Should().Be("vout");
    }

    [Fact]
    public void Text_goes_by_file_and_never_into_the_filter_string()
    {
        var filter = Build(Title("Итого: 100% дом'ой"));

        filter.Should().StartWith("drawtext=");
        filter.Should().NotContain("Итого", "текст в строке фильтра — источник всех бед");
        filter.Should().Contain(@"textfile='C\:/temp/meows/titles/ab12.txt'");
    }

    [Fact]
    public void Expansion_is_switched_off()
    {
        // Иначе процент и %{…} в тексте drawtext считает своими подстановками
        // и падает на «Stray %».
        Build(Title("100% готово")).Should().Contain("expansion=none");
    }

    [Fact]
    public void Own_stretch_of_time_is_set()
    {
        Build(Title()).Should().Contain("enable='between(t,2,5)'");
    }

    [Fact]
    public void Colour_is_given_in_the_form_ffmpeg_understands()
    {
        // Решётку ffmpeg не понимает вовсе.
        Build(Title() with { Color = "#FFCC00" }).Should().Contain("fontcolor='0xFFCC00'");
    }

    [Fact]
    public void Size_follows_the_frame_height()
    {
        // Доля высоты, а не пиксели: одна надпись в 1080p и в вертикальном 4K
        // обязана выглядеть одинаково.
        Build(Title() with { Scale = 0.08 }).Should().Contain("fontsize=h*0.08");
    }

    [Theory]
    [InlineData(TitleAnchor.BottomCenter, "(w-tw)/2", "h-th-h*0.05")]
    [InlineData(TitleAnchor.TopLeft, "h*0.05", "h*0.05")]
    [InlineData(TitleAnchor.MiddleCenter, "(w-tw)/2", "(h-th)/2")]
    [InlineData(TitleAnchor.BottomRight, "w-tw-h*0.05", "h-th-h*0.05")]
    public void Nine_places_map_to_coordinates(TitleAnchor anchor, string x, string y)
    {
        var filter = Build(Title() with { Anchor = anchor });

        filter.Should().Contain($"x='{x}'");
        filter.Should().Contain($"y='{y}'");
    }

    [Fact]
    public void Backdrop_can_be_switched_off()
    {
        Build(Title()).Should().Contain("box=1");
        Build(Title() with { Backdrop = false }).Should().NotContain("box=1");
    }

    [Fact]
    public void Title_without_a_file_is_skipped()
    {
        // Файл записать не удалось — лучше отдать ролик без титра, чем уронить
        // весь экспорт на висящем фильтре.
        TitleFilter.Build([Title()], new Dictionary<TitleId, string>()).Should().BeEmpty();
    }

    [Fact]
    public void Empty_titles_are_skipped()
    {
        var blank = Title("   ");
        var visible = Title("видно");

        var files = new Dictionary<TitleId, string> { [blank.Id] = TextFile, [visible.Id] = TextFile };

        TitleFilter.Build([blank, visible], files).Should().ContainSingle();
    }

    [Fact]
    public void Font_path_is_escaped_the_same_way()
    {
        TitleFilter.Build(
                [Title()],
                new Dictionary<TitleId, string> { [Title().Id] = TextFile },
                @"C:\Windows\Fonts\arial.ttf")
            .Should().BeEmpty("надпись без своего файла в граф не попадает");

        var title = Title();

        TitleFilter.Build(
                [title],
                new Dictionary<TitleId, string> { [title.Id] = TextFile },
                @"C:\Windows\Fonts\arial.ttf")
            .Single()
            .Should().Contain(@"fontfile='C\:/Windows/Fonts/arial.ttf'");
    }
}
