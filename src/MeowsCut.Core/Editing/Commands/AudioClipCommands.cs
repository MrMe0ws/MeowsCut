using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>Положить кусок звука на дорожку.</summary>
public sealed class AddAudioClipCommand(AudioTrackId trackId, AudioClip clip) : IEditCommand
{
    public string Title => "Добавить звук";

    public AudioTrackId TrackId { get; } = trackId;

    public AudioClip Clip { get; } = clip;

    public Sequence Apply(Sequence sequence) =>
        sequence.WithTrack(sequence.RequireTrack(TrackId).Add(Clip));
}

/// <summary>Убрать кусок звука. Соседние остаются на своих местах — здесь нет ripple.</summary>
public sealed class RemoveAudioClipCommand(AudioTrackId trackId, AudioClipId clipId) : IEditCommand
{
    public string Title => "Удалить звук";

    public AudioTrackId TrackId { get; } = trackId;

    public AudioClipId ClipId { get; } = clipId;

    public Sequence Apply(Sequence sequence) =>
        sequence.WithTrack(sequence.RequireTrack(TrackId).Remove(ClipId));
}

/// <summary>
/// Передвинуть кусок звука по времени.
/// </summary>
/// <remarks>
/// Звук двигают часто и мелко — попасть репликой точно под кадр иначе нельзя,
/// поэтому команда хранит итоговую позицию и склеивается с предыдущей.
/// </remarks>
public sealed class MoveAudioClipCommand(AudioTrackId trackId, AudioClipId clipId, TimeSpan start) : IEditCommand
{
    public string Title => "Передвинуть звук";

    public AudioTrackId TrackId { get; } = trackId;

    public AudioClipId ClipId { get; } = clipId;

    public TimeSpan Start { get; } = start;

    public Sequence Apply(Sequence sequence)
    {
        var track = sequence.RequireTrack(TrackId);
        return sequence.WithTrack(track.Replace(track.Require(ClipId).MoveTo(Start)));
    }

    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;
        return previous is MoveAudioClipCommand other && other.ClipId == ClipId;
    }
}

/// <summary>Обрезка края куска звука перетаскиванием.</summary>
public sealed class TrimAudioClipEdgeCommand(
    AudioTrackId trackId,
    AudioClipId clipId,
    ClipEdge edge,
    TimeSpan delta) : IEditCommand
{
    public string Title => Edge == ClipEdge.Start ? "Обрезать начало звука" : "Обрезать конец звука";

    public AudioTrackId TrackId { get; } = trackId;

    public AudioClipId ClipId { get; } = clipId;

    public ClipEdge Edge { get; } = edge;

    public TimeSpan Delta { get; } = delta;

    public Sequence Apply(Sequence sequence)
    {
        var track = sequence.RequireTrack(TrackId);
        var clip = track.Require(ClipId);

        var trimmed = Edge == ClipEdge.Start ? clip.TrimStart(Delta) : clip.TrimEnd(Delta);

        return sequence.WithTrack(track.Replace(trimmed));
    }

    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        if (previous is TrimAudioClipEdgeCommand other && other.ClipId == ClipId && other.Edge == Edge)
        {
            merged = new TrimAudioClipEdgeCommand(TrackId, ClipId, Edge, other.Delta + Delta);
            return true;
        }

        merged = this;
        return false;
    }
}

/// <summary>Разрезать кусок звука в указанной точке таймлайна.</summary>
public sealed class SplitAudioClipCommand(AudioTrackId trackId, TimeSpan timelineTime) : IEditCommand
{
    public string Title => "Разрезать звук";

    public AudioTrackId TrackId { get; } = trackId;

    public TimeSpan TimelineTime { get; } = timelineTime;

    public Sequence Apply(Sequence sequence)
    {
        var track = sequence.RequireTrack(TrackId);

        var clip = track.ClipAt(TimelineTime)
            ?? throw new EditOperationException("В этой точке дорожки нет звука.");

        var offset = TimelineTime - clip.TimelineStart;

        if (!clip.CanSplitAt(offset))
        {
            throw new EditOperationException(
                "Разрез слишком близко к краю: каждая половина должна остаться слышимой.");
        }

        var (left, right) = clip.SplitAt(offset);
        return sequence.WithTrack(track.ReplaceAt(track.IndexOf(clip.Id), left, right));
    }
}

/// <summary>Громкость, тональность и затухания одного куска звука.</summary>
public sealed class SetAudioClipPropertiesCommand(
    AudioTrackId trackId,
    AudioClipId clipId,
    double? gain = null,
    int? pitchSemitones = null,
    TimeSpan? fadeIn = null,
    TimeSpan? fadeOut = null) : IEditCommand
{
    public string Title => "Изменить звук";

    public AudioTrackId TrackId { get; } = trackId;

    public AudioClipId ClipId { get; } = clipId;

    public Sequence Apply(Sequence sequence)
    {
        var track = sequence.RequireTrack(TrackId);
        var clip = track.Require(ClipId);

        if (gain is { } newGain)
        {
            clip = clip.WithGain(newGain);
        }

        if (pitchSemitones is { } semitones)
        {
            clip = clip.WithPitch(semitones);
        }

        if (fadeIn is { } head || fadeOut is { } tail)
        {
            clip = clip.WithFades(fadeIn ?? clip.FadeIn, fadeOut ?? clip.FadeOut);
        }

        return sequence.WithTrack(track.Replace(clip));
    }

    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;
        return previous is SetAudioClipPropertiesCommand other && other.ClipId == ClipId;
    }
}
