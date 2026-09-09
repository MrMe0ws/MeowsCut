using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>
/// Одна правка таймлайна. Все изменения последовательности идут только через команды —
/// это единственный способ получить надёжный Ctrl+Z.
/// </summary>
public interface IEditCommand
{
    /// <summary>Название для списка истории: «Разрезать клип», «Удалить клип».</summary>
    string Title { get; }

    Sequence Apply(Sequence sequence);

    /// <summary>
    /// Склейка с предыдущей командой. Перетаскивание края клипа порождает десятки
    /// одинаковых команд в секунду; без склейки история превращается в мусор,
    /// а Ctrl+Z начинает отматывать движение мыши по пикселю.
    /// </summary>
    bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;
        return false;
    }
}
