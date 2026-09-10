using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>
/// Заменяет последовательность целиком.
/// </summary>
/// <remarks>
/// Нужна для правок, которые трогают сразу всё: пресет укорачивает ролик под лимит
/// площадки или ускоряет его целиком. Такие изменения обязаны проходить через историю
/// наравне с ножницами — иначе Ctrl+Z перестаёт быть надёжным именно там,
/// где правка самая крупная.
/// </remarks>
public sealed class ReplaceSequenceCommand(Sequence sequence, string title) : IEditCommand
{
    public string Title { get; } = title;

    public Sequence Sequence { get; } = sequence;

    public Sequence Apply(Sequence current) => Sequence;
}
