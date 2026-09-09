using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

public enum ClipEdge
{
    Start = 0,
    End
}

/// <summary>
/// Обрезка края клипа перетаскиванием. Положительная дельта всегда сдвигает
/// край вправо по таймлайну: левый край — укорачивает клип, правый — удлиняет.
/// </summary>
public sealed class TrimClipEdgeCommand(ClipId clipId, ClipEdge edge, TimeSpan delta) : IEditCommand
{
    public string Title => Edge == ClipEdge.Start ? "Обрезать начало клипа" : "Обрезать конец клипа";

    public ClipId ClipId { get; } = clipId;

    public ClipEdge Edge { get; } = edge;

    public TimeSpan Delta { get; } = delta;

    public Sequence Apply(Sequence sequence)
    {
        var clip = sequence.Video.Require(ClipId);

        var trimmed = Edge == ClipEdge.Start
            ? clip.TrimStart(Delta)
            : clip.TrimEnd(Delta);

        return sequence.WithTrack(sequence.Video.Replace(trimmed));
    }

    /// <summary>
    /// Серия движений мыши по одному краю одного клипа схлопывается в одну запись истории:
    /// пользователь тянул край один раз, значит и отменяться это должно один раз.
    /// </summary>
    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        if (previous is TrimClipEdgeCommand other && other.ClipId == ClipId && other.Edge == Edge)
        {
            merged = new TrimClipEdgeCommand(ClipId, Edge, other.Delta + Delta);
            return true;
        }

        merged = this;
        return false;
    }
}
