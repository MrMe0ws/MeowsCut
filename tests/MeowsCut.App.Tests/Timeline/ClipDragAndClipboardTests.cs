using FluentAssertions;
using MeowsCut.App.Tests.Support;
using MeowsCut.App.ViewModels;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Tests.Timeline;

/// <summary>
/// Перетаскивание клипов по ленте и буфер обмена.
/// </summary>
/// <remarks>
/// Здесь стерегут три вещи, которые пользователь замечает первыми: клип не удаётся
/// отлепить от соседа; мелкий сдвиг молча меняет порядок клипов; повторный клик
/// не снимает выделение и мешает попасть по пустому месту.
/// </remarks>
public class ClipDragAndClipboardTests
{
    private const double PixelsPerSecond = 50;
    private const double VideoY = 60;

    private static double X(double seconds) => seconds * PixelsPerSecond;

    /// <summary>Доска с двумя клипами по пять секунд, лежащими встык.</summary>
    private static TimelineViewModel Attached()
    {
        var timeline = new TimelineViewModel();
        var project = Fake.Project(20);

        timeline.Attach(project);
        timeline.Metrics.ViewportWidth = 1200;
        timeline.Metrics.PixelsPerSecond = PixelsPerSecond;
        timeline.Metrics.Scroll = TimeSpan.Zero;
        timeline.ViewportHeight = 300;
        timeline.SnapEnabled = false;

        // Режем двадцатисекундный клип надвое: получаем два куска по десять секунд.
        timeline.Playhead = TimeSpan.FromSeconds(10);
        timeline.SplitAtPlayheadCommand.Execute(null);

        return timeline;
    }

    private static void Drag(TimelineViewModel timeline, double fromSeconds, double toSeconds)
    {
        timeline.PointerDown(X(fromSeconds), VideoY);
        timeline.PointerMove(X(toSeconds), VideoY);
        timeline.PointerUp();
    }

    [Fact]
    public void Dragging_a_clip_right_detaches_it_from_the_neighbour()
    {
        var timeline = Attached();

        // Тянем второй кусок вправо: между ними обязано появиться пустое место.
        Drag(timeline, 12, 16);

        var placed = timeline.Sequence.EnumeratePlaced().ToArray();

        placed[0].End.Should().Be(TimeSpan.FromSeconds(10));
        placed[1].Start.Should().Be(TimeSpan.FromSeconds(14));
        timeline.Sequence.HasGaps.Should().BeTrue();
    }

    [Fact]
    public void A_small_shift_does_not_reorder_the_clips()
    {
        var timeline = Attached();
        var second = timeline.Sequence.Video.Clips[1].Id;

        Drag(timeline, 12, 13);

        timeline.Sequence.Video.Clips[1].Id.Should().Be(second, "случайная перестановка от дрожания руки недопустима");
    }

    [Fact]
    public void Dragging_past_the_middle_of_the_neighbour_reorders()
    {
        var timeline = Attached();
        var second = timeline.Sequence.Video.Clips[1].Id;

        // Второй кусок серединой переваливает за середину первого.
        Drag(timeline, 12, 1);

        timeline.Sequence.Video.Clips[0].Id.Should().Be(second);
    }

    [Fact]
    public void Clicking_the_same_clip_again_drops_the_selection()
    {
        var timeline = Attached();

        timeline.PointerDown(X(2), VideoY);
        timeline.PointerUp();
        timeline.SelectedClip.Should().NotBeNull();

        timeline.PointerDown(X(2), VideoY);
        timeline.PointerUp();

        timeline.SelectedClip.Should().BeNull();
    }

    [Fact]
    public void Dragging_a_selected_clip_keeps_it_selected()
    {
        var timeline = Attached();

        timeline.PointerDown(X(12), VideoY);
        timeline.PointerUp();

        // Второй клик по выбранному куску — но с перетаскиванием: выделение остаётся,
        // иначе выбранный клип нельзя было бы утащить мышью.
        Drag(timeline, 12, 16);

        timeline.SelectedClip.Should().NotBeNull();
    }

    [Fact]
    public void Copy_and_paste_add_a_clip_at_the_playhead()
    {
        var timeline = Attached();

        timeline.PointerDown(X(2), VideoY);
        timeline.PointerUp();
        timeline.CopySelectedCommand.Execute(null);

        timeline.Playhead = TimeSpan.FromSeconds(20);
        timeline.PasteCommand.Execute(null);

        timeline.Sequence.ClipCount.Should().Be(3);
        timeline.Sequence.Duration.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void A_pasted_clip_is_a_separate_piece()
    {
        var timeline = Attached();

        timeline.PointerDown(X(2), VideoY);
        timeline.PointerUp();

        var original = timeline.SelectedClip!.Id;

        timeline.CopySelectedCommand.Execute(null);
        timeline.Playhead = TimeSpan.FromSeconds(20);
        timeline.PasteCommand.Execute(null);

        // Одинаковый идентификатор означал бы, что удаление копии убирает и оригинал.
        timeline.Sequence.Video.Clips.Select(clip => clip.Id).Should().OnlyHaveUniqueItems();
        timeline.Sequence.Video.Clips.Count(clip => clip.Id == original).Should().Be(1);
    }

    [Fact]
    public void Cutting_removes_the_piece_and_paste_brings_it_back()
    {
        var timeline = Attached();

        timeline.PointerDown(X(2), VideoY);
        timeline.PointerUp();
        timeline.CutSelectedCommand.Execute(null);

        timeline.Sequence.ClipCount.Should().Be(1);

        timeline.Playhead = TimeSpan.Zero;
        timeline.PasteCommand.Execute(null);

        timeline.Sequence.ClipCount.Should().Be(2);
    }

    [Fact]
    public void Nothing_is_pasted_from_an_empty_clipboard()
    {
        var timeline = Attached();

        timeline.PasteCommand.CanExecute(null).Should().BeFalse();
    }
}
