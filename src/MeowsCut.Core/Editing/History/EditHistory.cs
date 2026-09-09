using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.History;

/// <summary>Что именно изменило последовательность — для подписчиков.</summary>
public sealed class SequenceChangedEventArgs(Sequence sequence, string reason, SequenceChangeKind kind)
    : EventArgs
{
    public Sequence Sequence { get; } = sequence;

    public string Reason { get; } = reason;

    public SequenceChangeKind Kind { get; } = kind;
}

public enum SequenceChangeKind
{
    Executed = 0,
    Merged,
    Undone,
    Redone,
    Reset
}

/// <summary>
/// История правок таймлайна.
/// </summary>
/// <remarks>
/// Undo реализован снимками последовательности, а не обратными операциями.
/// Последовательность — несколько десятков иммутабельных записей на килобайты,
/// кадры в ней не лежат; а обратные операции для дюжины команд — классический
/// источник трудноуловимых багов.
/// </remarks>
public sealed class EditHistory
{
    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];
    private readonly int _depth;

    private bool _mergeBlocked;

    public EditHistory(Sequence initial, int depth = 100)
    {
        Current = initial;
        _depth = Math.Max(1, depth);
    }

    public Sequence Current { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? NextUndoTitle => CanUndo ? _undo[^1].Command.Title : null;

    public string? NextRedoTitle => CanRedo ? _redo[^1].Command.Title : null;

    /// <summary>Названия правок от самой ранней к последней — для панели истории.</summary>
    public IReadOnlyList<string> UndoTitles => _undo.Select(entry => entry.Command.Title).ToArray();

    public event EventHandler<SequenceChangedEventArgs>? Changed;

    /// <summary>
    /// Применяет команду. Если она склеивается с предыдущей (перетаскивание края,
    /// движение ползунка), запись в истории не добавляется, а заменяется.
    /// </summary>
    public Sequence Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (TryMerge(command, out var mergedSequence))
        {
            return mergedSequence;
        }

        var before = Current;
        var after = command.Apply(before);

        _undo.Add(new HistoryEntry(command, before, after));
        TrimDepth();
        _redo.Clear();

        Current = after;
        Raise("Выполнено: " + command.Title, SequenceChangeKind.Executed);

        return Current;
    }

    /// <summary>
    /// Завершает серию склеиваемых команд. Вызывается по отпусканию кнопки мыши:
    /// следующее перетаскивание того же края должно стать отдельной правкой.
    /// </summary>
    public void EndMergeGroup() => _mergeBlocked = true;

    public Sequence Undo()
    {
        if (!CanUndo)
        {
            return Current;
        }

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);

        Current = entry.Before;
        _mergeBlocked = true;
        Raise("Отменено: " + entry.Command.Title, SequenceChangeKind.Undone);

        return Current;
    }

    public Sequence Redo()
    {
        if (!CanRedo)
        {
            return Current;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);

        Current = entry.After;
        _mergeBlocked = true;
        Raise("Повторено: " + entry.Command.Title, SequenceChangeKind.Redone);

        return Current;
    }

    /// <summary>Новый проект: история очищается полностью.</summary>
    public void Reset(Sequence sequence)
    {
        _undo.Clear();
        _redo.Clear();
        _mergeBlocked = false;
        Current = sequence;

        Raise("Новая последовательность", SequenceChangeKind.Reset);
    }

    private bool TryMerge(IEditCommand command, out Sequence sequence)
    {
        sequence = Current;

        if (_mergeBlocked || _undo.Count == 0)
        {
            _mergeBlocked = false;
            return false;
        }

        var last = _undo[^1];
        if (!command.TryMergeWith(last.Command, out var merged))
        {
            return false;
        }

        // Применяем объединённую команду к состоянию ДО предыдущей: иначе дельты
        // перетаскивания сложились бы дважды.
        var after = merged.Apply(last.Before);

        _undo[^1] = new HistoryEntry(merged, last.Before, after);
        _redo.Clear();

        Current = after;
        sequence = after;
        Raise("Выполнено: " + merged.Title, SequenceChangeKind.Merged);

        return true;
    }

    private void TrimDepth()
    {
        while (_undo.Count > _depth)
        {
            _undo.RemoveAt(0);
        }
    }

    private void Raise(string reason, SequenceChangeKind kind) =>
        Changed?.Invoke(this, new SequenceChangedEventArgs(Current, reason, kind));

    private sealed record HistoryEntry(IEditCommand Command, Sequence Before, Sequence After);
}
