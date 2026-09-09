using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>
/// Перестановка клипа на дорожке.
/// </summary>
public sealed class MoveClipCommand(ClipId clipId, int targetIndex) : IEditCommand
{
    public string Title => "Переместить клип";

    public ClipId ClipId { get; } = clipId;

    public int TargetIndex { get; } = targetIndex;

    public Sequence Apply(Sequence sequence)
    {
        var fromIndex = sequence.Video.IndexOf(ClipId);
        if (fromIndex < 0)
        {
            throw new Diagnostics.EditOperationException($"Клип {ClipId} не найден на дорожке.");
        }

        var target = Math.Clamp(TargetIndex, 0, sequence.Video.Count - 1);
        return sequence.WithTrack(sequence.Video.Move(fromIndex, target));
    }

    /// <summary>Перетаскивание через несколько позиций — одна правка в истории.</summary>
    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        if (previous is MoveClipCommand other && other.ClipId == ClipId)
        {
            merged = this;
            return true;
        }

        merged = this;
        return false;
    }
}

/// <summary>
/// Добавить клип в конец дорожки — например, при добавлении второго файла в проект.
/// </summary>
public sealed class AppendClipCommand(Clip clip) : IEditCommand
{
    public string Title => "Добавить клип";

    public Clip Clip { get; } = clip;

    public Sequence Apply(Sequence sequence)
    {
        var track = sequence.Video.Append(Clip);

        // Формат последовательности задаёт первый клип; последующие подгоняются при экспорте.
        return sequence.WithTrack(track);
    }
}

/// <summary>
/// Дублировать клип: копия встаёт сразу после оригинала и получает новый идентификатор.
/// </summary>
public sealed class DuplicateClipCommand(ClipId clipId) : IEditCommand
{
    public string Title => "Дублировать клип";

    public ClipId ClipId { get; } = clipId;

    public Sequence Apply(Sequence sequence)
    {
        var index = sequence.Video.IndexOf(ClipId);
        if (index < 0)
        {
            throw new Diagnostics.EditOperationException($"Клип {ClipId} не найден на дорожке.");
        }

        var copy = sequence.Video.Clips[index] with { Id = ClipId.New() };
        return sequence.WithTrack(sequence.Video.Insert(index + 1, copy));
    }
}
