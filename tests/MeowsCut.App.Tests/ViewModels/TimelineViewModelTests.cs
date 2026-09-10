using FluentAssertions;
using MeowsCut.App.Tests.Support;
using MeowsCut.App.Timeline;
using MeowsCut.App.ViewModels;

namespace MeowsCut.App.Tests.ViewModels;

public class TimelineViewModelTests
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
    public void Attaching_a_project_shows_one_clip()
    {
        var timeline = Attached();

        timeline.Clips.Should().HaveCount(1);
        timeline.Duration.Should().Be(TimeSpan.FromSeconds(20));
        timeline.HasProject.Should().BeTrue();
    }

    [Fact]
    public void Clicking_an_empty_area_moves_the_playhead()
    {
        var timeline = Attached();

        timeline.PointerDown(X(30), 60);   // за пределами клипа
        timeline.PointerUp();

        timeline.Playhead.Should().Be(TimeSpan.FromSeconds(20), "плейхед не уходит за конец");
    }

    [Fact]
    public void Clicking_a_clip_selects_it()
    {
        var timeline = Attached();

        timeline.PointerDown(X(10), 60);
        timeline.PointerUp();

        timeline.SelectedClip.Should().NotBeNull();
        timeline.SelectedClip!.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void Razor_splits_the_clip_where_it_was_clicked()
    {
        var timeline = Attached();
        timeline.ActiveTool = TimelineTool.Razor;

        timeline.PointerDown(X(8), 60);
        timeline.PointerUp();

        timeline.Clips.Should().HaveCount(2);
        timeline.Clips[0].Clip.TimelineDuration.Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void Split_at_playhead_needs_room_on_both_sides()
    {
        var timeline = Attached();

        timeline.Playhead = TimeSpan.Zero;
        timeline.SplitAtPlayheadCommand.CanExecute(null).Should().BeFalse("резать по краю нечего");

        timeline.Playhead = TimeSpan.FromSeconds(10);
        timeline.SplitAtPlayheadCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Deleting_a_clip_pulls_the_rest_together()
    {
        var timeline = Attached();
        timeline.ActiveTool = TimelineTool.Razor;
        timeline.PointerDown(X(5), 60);
        timeline.PointerUp();

        timeline.ActiveTool = TimelineTool.Select;
        timeline.PointerDown(X(2), 60);
        timeline.PointerUp();
        timeline.DeleteSelectedCommand.Execute(null);

        timeline.Clips.Should().HaveCount(1);
        timeline.Duration.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Undo_returns_the_deleted_clip()
    {
        var timeline = Attached();
        timeline.ActiveTool = TimelineTool.Razor;
        timeline.PointerDown(X(5), 60);
        timeline.PointerUp();

        timeline.UndoCommand.Execute(null);

        timeline.Clips.Should().HaveCount(1);
        timeline.CanRedo.Should().BeTrue();

        timeline.RedoCommand.Execute(null);
        timeline.Clips.Should().HaveCount(2);
    }

    [Fact]
    public void Dragging_an_edge_is_a_single_undo_step()
    {
        var timeline = Attached();
        timeline.PointerDown(X(10), 60);   // выделяем клип
        timeline.PointerUp();

        // Тянем левый край: несколько десятков событий мыши.
        timeline.PointerDown(2, 60);
        for (var i = 1; i <= 20; i++)
        {
            timeline.PointerMove(2 + (i * 5), 60);
        }

        timeline.PointerUp();

        timeline.Clips[0].Clip.SourceRange.Start.Should().BeGreaterThan(TimeSpan.FromSeconds(1));

        timeline.UndoCommand.Execute(null);
        timeline.Clips[0].Clip.SourceRange.Start.Should().Be(TimeSpan.Zero, "одна отмена возвращает всё перетаскивание");
    }

    [Fact]
    public void Selection_survives_editing()
    {
        var timeline = Attached();
        timeline.PointerDown(X(10), 60);
        timeline.PointerUp();

        var selectedId = timeline.SelectedClip!.Id;

        timeline.PointerDown(2, 60);
        timeline.PointerMove(20, 60);
        timeline.PointerUp();

        timeline.SelectedClip.Should().NotBeNull();
        timeline.SelectedClip!.Id.Should().Be(selectedId);
    }

    [Fact]
    public void Hand_tool_pans_instead_of_editing()
    {
        var timeline = Attached();
        timeline.ActiveTool = TimelineTool.Hand;

        timeline.PointerDown(X(10), 60);
        timeline.PointerMove(X(10) - 100, 60);
        timeline.PointerUp();

        timeline.Metrics.Scroll.Should().Be(TimeSpan.FromSeconds(2));
        timeline.Clips.Should().HaveCount(1, "рука ничего не режет");
    }

    [Fact]
    public void Wheel_scrolls_and_ctrl_wheel_zooms()
    {
        var timeline = Attached();

        timeline.Wheel(-120, 500, zoom: false);
        timeline.Metrics.Scroll.Should().BeGreaterThan(TimeSpan.Zero);

        var before = timeline.Metrics.PixelsPerSecond;
        timeline.Wheel(120, 500, zoom: true);
        timeline.Metrics.PixelsPerSecond.Should().BeGreaterThan(before);
    }

    [Fact]
    public void Trim_to_playhead_moves_the_selected_edge()
    {
        var timeline = Attached();
        timeline.PointerDown(X(10), 60);
        timeline.PointerUp();

        timeline.Playhead = TimeSpan.FromSeconds(4);
        timeline.TrimStartToPlayheadCommand.Execute(null);

        timeline.Clips[0].Clip.SourceRange.Start.Should().Be(TimeSpan.FromSeconds(4));
        timeline.Duration.Should().Be(TimeSpan.FromSeconds(16));
    }

    [Fact]
    public void Sequence_changes_are_reported_outside()
    {
        var timeline = Attached();
        var notified = 0;
        timeline.SequenceChanged += (_, _) => notified++;

        timeline.ActiveTool = TimelineTool.Razor;
        timeline.PointerDown(X(5), 60);
        timeline.PointerUp();

        notified.Should().BeGreaterThan(0, "панель экспорта должна пересчитать сводку");
    }

    [Fact]
    public void Detaching_clears_the_board()
    {
        var timeline = Attached();

        timeline.Detach();

        timeline.Clips.Should().BeEmpty();
        timeline.HasProject.Should().BeFalse();
        timeline.SelectedClip.Should().BeNull();
    }

    [Fact]
    public void Editing_without_a_project_does_nothing()
    {
        var timeline = new TimelineViewModel();

        timeline.PointerDown(100, 60);
        timeline.PointerMove(150, 60);
        timeline.PointerUp();

        timeline.Clips.Should().BeEmpty();
        timeline.CanUndo.Should().BeFalse();
    }
}
