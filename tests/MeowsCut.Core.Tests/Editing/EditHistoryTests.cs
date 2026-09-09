using FluentAssertions;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.History;
using MeowsCut.Core.Editing.Timeline;
using static MeowsCut.Core.Tests.Editing.Fake;

namespace MeowsCut.Core.Tests.Editing;

public class EditHistoryTests
{
    private static EditHistory History(double seconds = 20) =>
        new(Sequence.FromSource(Source(seconds)));

    [Fact]
    public void Undo_returns_the_previous_state()
    {
        var history = History();
        var before = history.Current;

        history.Execute(new SplitClipCommand(S(5)));
        history.Current.ClipCount.Should().Be(2);

        history.Undo();

        history.Current.Should().Be(before);
        history.Current.ClipCount.Should().Be(1);
    }

    [Fact]
    public void Redo_returns_the_undone_state()
    {
        var history = History();
        history.Execute(new SplitClipCommand(S(5)));
        var after = history.Current;

        history.Undo();
        history.Redo();

        history.Current.Should().Be(after);
    }

    [Fact]
    public void New_command_clears_the_redo_stack()
    {
        var history = History();
        history.Execute(new SplitClipCommand(S(5)));
        history.Undo();

        history.CanRedo.Should().BeTrue();

        history.Execute(new SplitClipCommand(S(10)));

        history.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Undo_and_redo_are_no_ops_on_empty_stacks()
    {
        var history = History();
        var initial = history.Current;

        history.Undo();
        history.Redo();

        history.Current.Should().Be(initial);
        history.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Drag_series_collapses_into_a_single_history_entry()
    {
        var history = History();
        var id = history.Current.Video.Clips[0].Id;
        var before = history.Current;

        // Десять шагов перетаскивания края по 0.5 с.
        for (var i = 0; i < 10; i++)
        {
            history.Execute(new TrimClipEdgeCommand(id, ClipEdge.Start, S(0.5)));
        }

        history.Current.Video.Clips[0].SourceRange.Start.Should().Be(S(5), "дельты сложились");
        history.UndoTitles.Should().HaveCount(1, "перетаскивание — одна правка");

        history.Undo();
        history.Current.Should().Be(before, "один Ctrl+Z отменяет всё перетаскивание");
    }

    [Fact]
    public void Ending_a_drag_starts_a_new_history_entry()
    {
        var history = History();
        var id = history.Current.Video.Clips[0].Id;

        history.Execute(new TrimClipEdgeCommand(id, ClipEdge.Start, S(1)));
        history.EndMergeGroup();
        history.Execute(new TrimClipEdgeCommand(id, ClipEdge.Start, S(1)));

        history.UndoTitles.Should().HaveCount(2);

        history.Undo();
        history.Current.Video.Clips[0].SourceRange.Start.Should().Be(S(1));
    }

    [Fact]
    public void Different_commands_never_merge()
    {
        var history = History();
        history.Execute(new SplitClipCommand(S(5)));
        history.Execute(new SplitClipCommand(S(10)));

        history.UndoTitles.Should().HaveCount(2);
    }

    [Fact]
    public void History_depth_is_limited()
    {
        var history = new EditHistory(Sequence.FromSource(Source(60)), depth: 3);
        var id = history.Current.Video.Clips[0].Id;

        for (var i = 0; i < 5; i++)
        {
            history.Execute(new SetClipSpeedCommand(id, 1 + i * 0.1));
            history.EndMergeGroup();
        }

        history.UndoTitles.Should().HaveCount(3);
    }

    [Fact]
    public void Changed_event_reports_what_happened()
    {
        var history = History();
        var kinds = new List<SequenceChangeKind>();
        history.Changed += (_, args) => kinds.Add(args.Kind);

        history.Execute(new SplitClipCommand(S(5)));
        history.Undo();
        history.Redo();
        history.Reset(Sequence.Empty);

        kinds.Should().Equal(
            SequenceChangeKind.Executed,
            SequenceChangeKind.Undone,
            SequenceChangeKind.Redone,
            SequenceChangeKind.Reset);
    }

    [Fact]
    public void Reset_clears_both_stacks()
    {
        var history = History();
        history.Execute(new SplitClipCommand(S(5)));
        history.Undo();

        history.Reset(Sequence.FromSource(Source(10)));

        history.CanUndo.Should().BeFalse();
        history.CanRedo.Should().BeFalse();
        history.Current.Duration.Should().Be(S(10));
    }

    [Fact]
    public void Failed_command_leaves_history_untouched()
    {
        var history = History();
        var before = history.Current;

        var act = () => history.Execute(new SplitClipCommand(S(500)));

        act.Should().Throw<Core.Diagnostics.EditOperationException>();
        history.Current.Should().Be(before);
        history.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Undo_after_a_drag_does_not_merge_the_next_command()
    {
        var history = History();
        var id = history.Current.Video.Clips[0].Id;

        history.Execute(new TrimClipEdgeCommand(id, ClipEdge.Start, S(1)));
        history.Undo();
        history.Execute(new TrimClipEdgeCommand(id, ClipEdge.Start, S(2)));

        history.UndoTitles.Should().HaveCount(1);
        history.Current.Video.Clips[0].SourceRange.Start.Should().Be(S(2));
    }
}
