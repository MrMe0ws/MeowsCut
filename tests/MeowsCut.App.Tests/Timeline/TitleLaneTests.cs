using FluentAssertions;
using MeowsCut.App.Tests.Support;
using MeowsCut.App.Timeline;
using MeowsCut.App.ViewModels;

namespace MeowsCut.App.Tests.Timeline;

/// <summary>
/// Надписи на доске: доступность кнопки, выделение и перенос.
/// </summary>
/// <remarks>
/// Главное здесь — первая проверка. Условие команды считается один раз, при
/// создании модели, когда проекта ещё нет; если открытие файла об этом
/// не сообщает, кнопка остаётся серой навсегда. Ровно это уже случилось
/// с «Дорожкой», а потом повторилось с «Текстом» — значит, каждая такая
/// команда должна быть закрыта проверкой.
/// </remarks>
public class TitleLaneTests
{
    private const double PixelsPerSecond = 50;

    private static double X(double seconds) => seconds * PixelsPerSecond;

    /// <summary>Y внутри полосы надписей при высоте доски 300.</summary>
    private static double TitleY(TimelineViewModel timeline)
    {
        var lanes = new TimelineLayout().Build(
            timeline.ViewportHeight,
            [.. timeline.AudioTracks.Select(track => track.Id)],
            timeline.Sequence.HasTitles);

        var lane = lanes.First(item => item.Kind == LaneKind.Title);
        return lane.Top + (lane.Height / 2);
    }

    private static TimelineViewModel Attached()
    {
        var timeline = new TimelineViewModel();

        timeline.Attach(Fake.Project(20));
        timeline.Metrics.ViewportWidth = 1000;
        timeline.Metrics.PixelsPerSecond = PixelsPerSecond;
        timeline.Metrics.Scroll = TimeSpan.Zero;
        timeline.ViewportHeight = 300;
        timeline.SnapEnabled = false;

        return timeline;
    }

    [Fact]
    public void The_text_button_wakes_up_when_a_project_is_open()
    {
        var timeline = new TimelineViewModel();

        timeline.AddTitleAtPlayheadCommand.CanExecute(null).Should().BeFalse("проекта ещё нет");

        timeline.Attach(Fake.Project(10));

        timeline.AddTitleAtPlayheadCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void A_new_title_stands_at_the_playhead_and_is_selected()
    {
        var timeline = Attached();
        timeline.Playhead = TimeSpan.FromSeconds(4);

        timeline.AddTitle("Привет");

        timeline.Sequence.Titles.Should().ContainSingle()
            .Which.TimelineStart.Should().Be(TimeSpan.FromSeconds(4));

        // Надпись сразу выбрана: её текст правят следующим действием.
        timeline.SelectedTitle.Should().NotBeNull();
        timeline.HasTitleSelection.Should().BeTrue();
    }

    [Fact]
    public void Selecting_a_title_puts_away_the_clip_selection()
    {
        // Инспектор показывает что-то одно: иначе непонятно, к чему относится «размер».
        var timeline = Attached();
        timeline.AddTitle("Привет");

        timeline.HasClipInspector.Should().BeFalse();
        timeline.SelectedClip.Should().BeNull();
    }

    [Fact]
    public void A_title_is_dragged_along_the_board()
    {
        var timeline = Attached();
        timeline.Playhead = TimeSpan.FromSeconds(2);
        timeline.AddTitle("Привет");

        var y = TitleY(timeline);

        timeline.PointerDown(X(3), y);
        timeline.PointerMove(X(8), y);
        timeline.PointerUp();

        // Схватили за середину третьей секунды, то есть за секунду от начала:
        // надпись встаёт так, чтобы это место оказалось под курсором.
        timeline.Sequence.Titles[0].TimelineStart
            .Should().BeCloseTo(TimeSpan.FromSeconds(7), TimeSpan.FromMilliseconds(60));
    }

    [Fact]
    public void Delete_removes_the_selected_title_and_not_the_clip()
    {
        var timeline = Attached();
        timeline.AddTitle("Привет");

        timeline.DeleteSelectedCommand.Execute(null);

        timeline.Sequence.Titles.Should().BeEmpty();
        timeline.Sequence.Video.Clips.Should().ContainSingle("клип под надписью трогать нельзя");
    }

    [Fact]
    public void Undo_takes_the_title_back()
    {
        var timeline = Attached();
        timeline.AddTitle("Привет");

        timeline.UndoCommand.Execute(null);

        timeline.Sequence.Titles.Should().BeEmpty();
        timeline.SelectedTitle.Should().BeNull();
    }
}
