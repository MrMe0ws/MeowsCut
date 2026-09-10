using FluentAssertions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;
using static MeowsCut.Core.Tests.Editing.Fake;

namespace MeowsCut.Core.Tests.Editing;

public class EditCommandTests
{
    private static Sequence SingleClip(double seconds = 20) => Sequence.FromSource(Source(seconds));

    [Fact]
    public void Split_produces_two_clips_of_the_same_total_length()
    {
        var sequence = SingleClip();

        var result = new SplitClipCommand(S(5)).Apply(sequence);

        result.ClipCount.Should().Be(2);
        result.Duration.Should().Be(sequence.Duration);
        result.Video.Clips[0].TimelineDuration.Should().Be(S(5));
    }

    [Fact]
    public void Split_outside_any_clip_is_rejected()
    {
        var act = () => new SplitClipCommand(S(50)).Apply(SingleClip());

        act.Should().Throw<EditOperationException>();
    }

    [Fact]
    public void Remove_clip_pulls_the_rest_together()
    {
        var sequence = new SplitClipCommand(S(5)).Apply(SingleClip());
        var firstId = sequence.Video.Clips[0].Id;

        var result = new RemoveClipCommand(firstId).Apply(sequence);

        result.ClipCount.Should().Be(1);
        result.Duration.Should().Be(S(15));
        result.EnumeratePlaced().Single().Start.Should().Be(TimeSpan.Zero, "зазора не остаётся");
    }

    [Fact]
    public void Removing_several_clips_is_one_operation()
    {
        var sequence = new SplitClipCommand(S(10)).Apply(SingleClip());
        sequence = new SplitClipCommand(S(15)).Apply(sequence);

        var doomed = new[] { sequence.Video.Clips[0].Id, sequence.Video.Clips[2].Id };
        var result = new RemoveClipsCommand(doomed).Apply(sequence);

        result.ClipCount.Should().Be(1);
        result.Duration.Should().Be(S(5), "остался только средний кусок 10–15");
        result.EnumeratePlaced().Single().Start.Should().Be(TimeSpan.Zero, "зазора не остаётся");
    }

    [Fact]
    public void Removing_clips_that_are_not_there_is_rejected()
    {
        var act = () => new RemoveClipsCommand([ClipId.New()]).Apply(SingleClip());

        act.Should().Throw<EditOperationException>();
    }

    [Fact]
    public void Remove_range_cuts_the_middle_out()
    {
        // Классический сценарий: оставить 0–5, вырезать 5–8, оставить 8–20.
        var result = new RemoveRangeCommand(new TimeRangeSelection(S(5), S(8))).Apply(SingleClip());

        result.ClipCount.Should().Be(2);
        result.Duration.Should().Be(S(17));

        var placed = result.EnumeratePlaced().ToArray();
        placed[0].Clip.SourceRange.Should().Be(new TimeRange(TimeSpan.Zero, S(5)));
        placed[1].Clip.SourceRange.Should().Be(new TimeRange(S(8), S(20)));
        placed[1].Start.Should().Be(S(5), "второй кусок подтягивается к первому");
    }

    [Fact]
    public void Remove_range_at_the_beginning_keeps_the_tail()
    {
        var result = new RemoveRangeCommand(new TimeRangeSelection(TimeSpan.Zero, S(5))).Apply(SingleClip());

        result.ClipCount.Should().Be(1);
        result.Video.Clips[0].SourceRange.Should().Be(new TimeRange(S(5), S(20)));
    }

    [Fact]
    public void Remove_range_spanning_several_clips_removes_all_of_them()
    {
        var sequence = new SplitClipCommand(S(5)).Apply(SingleClip());
        sequence = new SplitClipCommand(S(10)).Apply(sequence);

        var result = new RemoveRangeCommand(new TimeRangeSelection(S(2), S(12))).Apply(sequence);

        result.Duration.Should().Be(S(10));
    }

    [Fact]
    public void Empty_selection_is_rejected()
    {
        var act = () => new RemoveRangeCommand(new TimeRangeSelection(S(5), S(5))).Apply(SingleClip());

        act.Should().Throw<EditOperationException>();
    }

    [Fact]
    public void Trim_edge_changes_only_the_target_clip()
    {
        var sequence = new SplitClipCommand(S(5)).Apply(SingleClip());
        var secondId = sequence.Video.Clips[1].Id;

        var result = new TrimClipEdgeCommand(secondId, ClipEdge.Start, S(3)).Apply(sequence);

        result.Video.Clips[0].TimelineDuration.Should().Be(S(5));
        result.Video.Clips[1].TimelineDuration.Should().Be(S(12));
        result.Duration.Should().Be(S(17));
    }

    [Fact]
    public void Trim_commands_of_the_same_edge_merge_by_summing_deltas()
    {
        var first = new TrimClipEdgeCommand(ClipId.New(), ClipEdge.End, S(1));
        var second = new TrimClipEdgeCommand(first.ClipId, ClipEdge.End, S(2));

        second.TryMergeWith(first, out var merged).Should().BeTrue();

        merged.Should().BeOfType<TrimClipEdgeCommand>()
            .Which.Delta.Should().Be(S(3));
    }

    [Fact]
    public void Trim_commands_of_different_edges_do_not_merge()
    {
        var id = ClipId.New();
        var first = new TrimClipEdgeCommand(id, ClipEdge.Start, S(1));
        var second = new TrimClipEdgeCommand(id, ClipEdge.End, S(1));

        second.TryMergeWith(first, out _).Should().BeFalse();
    }

    [Fact]
    public void Move_reorders_clips_without_changing_duration()
    {
        var sequence = new SplitClipCommand(S(5)).Apply(SingleClip());
        var firstId = sequence.Video.Clips[0].Id;

        var result = new MoveClipCommand(firstId, 1).Apply(sequence);

        result.Video.Clips[1].Id.Should().Be(firstId);
        result.Duration.Should().Be(sequence.Duration);
        result.EnumeratePlaced().First().Clip.TimelineDuration.Should().Be(S(15));
    }

    [Fact]
    public void Speed_command_changes_only_one_clip()
    {
        var sequence = new SplitClipCommand(S(5)).Apply(SingleClip());
        var secondId = sequence.Video.Clips[1].Id;

        var result = new SetClipSpeedCommand(secondId, 2d).Apply(sequence);

        result.Video.Clips[0].Speed.Should().Be(1d);
        result.Video.Clips[1].Speed.Should().Be(2d);
        result.Duration.Should().Be(S(12.5), "второй кусок стал вдвое короче");
    }

    [Fact]
    public void Audio_command_mutes_a_single_clip()
    {
        var sequence = SingleClip();
        var id = sequence.Video.Clips[0].Id;

        var result = new SetClipAudioCommand(id, ClipAudio.Muted).Apply(sequence);

        result.HasAudio.Should().BeFalse();
    }

    [Fact]
    public void Enabling_and_disabling_audio_are_separate_history_entries()
    {
        var id = ClipId.New();
        var mute = new SetClipAudioCommand(id, ClipAudio.Muted);
        var unmute = new SetClipAudioCommand(id, ClipAudio.Default);

        unmute.TryMergeWith(mute, out _).Should().BeFalse();
    }

    [Fact]
    public void Transform_command_stores_zoom_and_offset()
    {
        var sequence = SingleClip();
        var id = sequence.Video.Clips[0].Id;

        var result = new SetClipTransformCommand(id, new ClipTransform(1.5, 0.1, -0.2)).Apply(sequence);

        result.Video.Clips[0].Transform.Zoom.Should().Be(1.5);
        result.Video.Clips[0].Transform.IsIdentity.Should().BeFalse();
    }

    [Fact]
    public void Duplicate_puts_the_copy_right_after_the_original()
    {
        var sequence = SingleClip();
        var id = sequence.Video.Clips[0].Id;

        var result = new DuplicateClipCommand(id).Apply(sequence);

        result.ClipCount.Should().Be(2);
        result.Video.Clips[1].Id.Should().NotBe(id);
        result.Video.Clips[1].SourceRange.Should().Be(result.Video.Clips[0].SourceRange);
        result.Duration.Should().Be(S(40));
    }

    [Fact]
    public void Append_adds_a_clip_from_another_source()
    {
        var sequence = SingleClip();
        var another = Source(seconds: 5, path: @"C:\видео\второй файл.mkv");

        var result = new AppendClipCommand(Clip.FromSource(another)).Apply(sequence);

        result.ClipCount.Should().Be(2);
        result.Duration.Should().Be(S(25));
    }

    [Fact]
    public void Commands_referring_to_unknown_clips_are_rejected()
    {
        var missing = ClipId.New();

        var actions = new Action[]
        {
            () => new RemoveClipCommand(missing).Apply(SingleClip()),
            () => new TrimClipEdgeCommand(missing, ClipEdge.Start, S(1)).Apply(SingleClip()),
            () => new MoveClipCommand(missing, 0).Apply(SingleClip()),
            () => new SetClipSpeedCommand(missing, 2).Apply(SingleClip()),
            () => new DuplicateClipCommand(missing).Apply(SingleClip())
        };

        foreach (var act in actions)
        {
            act.Should().Throw<EditOperationException>();
        }
    }
}
