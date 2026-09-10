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

/// <summary>
/// Вставка готовых клипов в указанное место дорожки — то, что делает Ctrl+V.
/// </summary>
/// <remarks>
/// Все клипы вставляются одной правкой: вставив три куска, пользователь ждёт,
/// что Ctrl+Z уберёт их разом, а не будет отматывать по одному.
/// Идентификаторы им выдаёт вызывающая сторона — команда обязана быть
/// повторяемой, иначе повтор после отмены дал бы другие клипы.
/// </remarks>
public sealed class InsertClipsCommand(int index, IReadOnlyList<Clip> clips) : IEditCommand
{
    public string Title => "Вставить клипы";

    public int Index { get; } = index;

    public IReadOnlyList<Clip> Clips { get; } = clips;

    public Sequence Apply(Sequence sequence)
    {
        if (Clips.Count == 0)
        {
            return sequence;
        }

        var track = sequence.Video;
        var at = Math.Clamp(Index, 0, track.Count);

        for (var i = 0; i < Clips.Count; i++)
        {
            track = track.Insert(at + i, Clips[i]);
        }

        return sequence.WithTrack(track);
    }
}
