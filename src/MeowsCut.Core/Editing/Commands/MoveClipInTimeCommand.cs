using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>
/// Сдвиг клипа по ленте: клип встаёт в указанную точку, а перед ним появляется зазор.
/// </summary>
/// <remarks>
/// Отдельно от <see cref="MoveClipCommand"/>: та меняет порядок клипов, эта — место
/// на ленте. Перетаскивание мышью пользуется обеими, и разделять их обязательно:
/// сдвиг вправо не должен молча превращаться в перестановку соседей.
/// Клипы после сдвинутого едут следом — зазор вставляется, а не съедается.
/// </remarks>
public sealed class MoveClipInTimeCommand(ClipId clipId, TimeSpan start) : IEditCommand
{
    public string Title => "Сдвинуть клип";

    public ClipId ClipId { get; } = clipId;

    /// <summary>Куда встаёт начало клипа. Наехать на предыдущий нельзя — дорожка прижмёт.</summary>
    public TimeSpan Start { get; } = start;

    public Sequence Apply(Sequence sequence) => sequence.WithTrack(sequence.Video.MoveInTime(ClipId, Start));

    /// <summary>Протяжка мышью — одна правка в истории, а не сотня по кадру.</summary>
    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;
        return previous is MoveClipInTimeCommand other && other.ClipId == ClipId;
    }
}
