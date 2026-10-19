using FluentAssertions;
using MeowsCut.App.Tests.Support;
using MeowsCut.App.Timeline;
using MeowsCut.App.ViewModels;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Media;

namespace MeowsCut.App.Tests.ViewModels;

/// <summary>
/// Логика интерфейса 2.0: масштаб доски ползунком, меню по правой кнопке,
/// панель медиа и подсветка готовых значений.
/// </summary>
public class Interface2Tests
{
    private const double PixelsPerSecond = 50;

    private static TimelineViewModel Attached(double seconds = 20)
    {
        var timeline = new TimelineViewModel();
        timeline.Attach(Fake.Project(seconds));
        timeline.Metrics.ViewportWidth = 1000;
        timeline.Metrics.PixelsPerSecond = PixelsPerSecond;
        timeline.Metrics.Scroll = TimeSpan.Zero;

        return timeline;
    }

    private static double X(double seconds) => seconds * PixelsPerSecond;

    [Fact]
    public void Zoom_slider_goes_both_ways()
    {
        // Ползунок на панели доски обязан и задавать масштаб, и показывать
        // тот, что выставили колесом: иначе он врёт после первого же Ctrl+колесо.
        var timeline = Attached();

        timeline.ZoomLevel = 50;

        timeline.ZoomLevel.Should().BeApproximately(50, 0.01);
        timeline.Metrics.PixelsPerSecond.Should().BeInRange(TimelineMetrics.MinPixelsPerSecond, TimelineMetrics.MaxPixelsPerSecond);

        timeline.ZoomLevel = TimelineViewModel.ZoomLevelMax;
        timeline.Metrics.PixelsPerSecond.Should().BeApproximately(TimelineMetrics.MaxPixelsPerSecond, 0.01);
    }

    [Fact]
    public void Right_click_on_a_clip_selects_it_instead_of_the_title()
    {
        // Меню правит выделение: если бы надпись осталась выделенной, панель
        // свойств показывала бы её, а чип из меню правил бы клип.
        var timeline = Attached();
        timeline.AddTitle("Вьетнам");
        timeline.SelectedTitle.Should().NotBeNull();

        var target = timeline.PrepareContext(X(10), 80);

        target.Kind.Should().Be(ContextTargetKind.Clip);
        timeline.SelectedTitle.Should().BeNull();
        timeline.SelectedClip.Should().NotBeNull();
        timeline.HasClipInspector.Should().BeTrue();
    }

    [Fact]
    public void Split_here_cuts_where_the_menu_was_opened()
    {
        var timeline = Attached();

        var target = timeline.PrepareContext(X(7), 60);
        timeline.SplitAt(target);

        timeline.Clips.Should().HaveCount(2);
        timeline.Clips[1].Start.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void Moving_playhead_does_not_redraw_the_whole_board()
    {
        // При воспроизведении курсор шагает 30 раз в секунду. Полная перерисовка
        // доски на каждый шаг забивала отрисовку, и видео шло по 4–5 кадров.
        var timeline = Attached();
        var redraws = 0;
        var moves = 0;
        timeline.VisualInvalidated += (_, _) => redraws++;
        timeline.PlayheadMoved += (_, _) => moves++;

        timeline.Playhead = TimeSpan.FromSeconds(3);
        timeline.Playhead = TimeSpan.FromSeconds(4);

        moves.Should().Be(2);
        redraws.Should().Be(0);
    }

    [Fact]
    public void Right_click_on_empty_space_opens_nothing()
    {
        var timeline = Attached();

        timeline.PrepareContext(X(40), 60).Kind.Should().Be(ContextTargetKind.None);
    }

    [Fact]
    public void Media_panel_splits_pictures_and_sound_and_marks_what_is_on_the_board()
    {
        var project = Fake.Project();
        var (withSound, _) = project.WithSource(SoundOnly());
        var sources = new SourcesViewModel();

        sources.Update(withSound);

        sources.VisualFiles.Should().ContainSingle(file => file.IsVideo && file.IsOnBoard);
        sources.AudioFiles.Should().ContainSingle(file => file.IsAudio && !file.IsOnBoard,
            "музыку в проект добавили, но на дорожку ещё не положили");

        sources.Filter = SourceFilter.Audio;

        sources.VisualFiles.Should().BeEmpty();
        sources.AudioFiles.Should().HaveCount(1);
    }

    [Fact]
    public void Chip_of_the_current_value_is_highlighted()
    {
        // Значение, набранное руками, совпало с готовым — чип загорается:
        // «2» в поле и «2×» на чипе — одно и то же.
        var chips = new[] { 0.5, 1, 2 }.Select(value => new ChoiceChip($"{value}×", value, _ => { })).ToArray();

        ChoiceChip.MarkActive(chips, 2.0000001, 0.001);

        chips.Single(chip => chip.IsActive).Value.Should().Be(2);
    }

    private static MediaInfo SoundOnly()
    {
        var duration = TimeSpan.FromSeconds(40);
        var audio = new AudioStreamInfo(0, "mp3", null, 2, "stereo", 44_100, 192_000, duration, "und");

        return new MediaInfo(
            @"C:\звук\музыка.mp3", 1_000_000, duration,
            new ContainerInfo("mp3", "MP3", 192_000),
            [], [audio], []);
    }
}
