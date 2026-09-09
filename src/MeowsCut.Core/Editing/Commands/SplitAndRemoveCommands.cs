using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>
/// Ножницы: разрезать клип в точке таймлайна.
/// </summary>
public sealed class SplitClipCommand(TimeSpan timelineTime) : IEditCommand
{
    public string Title => "Разрезать клип";

    public TimeSpan TimelineTime { get; } = timelineTime;

    public Sequence Apply(Sequence sequence)
    {
        var placed = sequence.ClipAt(TimelineTime)
            ?? throw new EditOperationException("В этой точке таймлайна нет клипа.");

        var offset = TimelineTime - placed.Start;
        if (!placed.Clip.CanSplitAt(offset))
        {
            throw new EditOperationException(
                "Разрез слишком близко к краю клипа: каждая половина должна остаться заметной.");
        }

        var (left, right) = placed.Clip.SplitAt(offset);
        return sequence.WithTrack(sequence.Video.ReplaceAt(placed.Index, left, right));
    }
}

/// <summary>
/// Удалить клип; соседние подтягиваются (ripple).
/// </summary>
public sealed class RemoveClipCommand(ClipId clipId) : IEditCommand
{
    public string Title => "Удалить клип";

    public ClipId ClipId { get; } = clipId;

    public Sequence Apply(Sequence sequence) => sequence.WithTrack(sequence.Video.Remove(ClipId));
}

/// <summary>
/// Вырезать интервал таймлайна: два разреза и удаление того, что между ними.
/// Именно так работает «удалить кусок из середины» — отдельной сущности для этого нет.
/// </summary>
public sealed class RemoveRangeCommand(TimeRangeSelection selection) : IEditCommand
{
    public string Title => "Вырезать фрагмент";

    public TimeRangeSelection Selection { get; } = selection;

    public Sequence Apply(Sequence sequence)
    {
        if (Selection.Duration <= TimeSpan.Zero)
        {
            throw new EditOperationException("Пустое выделение: нечего вырезать.");
        }

        // Сначала режем по обеим границам, затем удаляем всё, что оказалось внутри.
        var result = sequence.SplitAt(Selection.Start).SplitAt(Selection.End);

        var doomed = result
            .EnumeratePlaced()
            .Where(placed => placed.Start >= Selection.Start - Tolerance &&
                             placed.End <= Selection.End + Tolerance)
            .Select(placed => placed.Clip.Id)
            .ToArray();

        if (doomed.Length == 0)
        {
            throw new EditOperationException("В выделении нет клипов целиком — нечего удалять.");
        }

        foreach (var id in doomed)
        {
            result = result.WithTrack(result.Video.Remove(id));
        }

        return result;
    }

    /// <summary>Клипы стыкуются по вычисленным длительностям, поэтому сравниваем с допуском.</summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(1);
}

/// <summary>Выделенный интервал таймлайна.</summary>
public readonly record struct TimeRangeSelection(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
}
