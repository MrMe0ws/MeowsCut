using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>Добавить пустую аудиодорожку.</summary>
public sealed class AddAudioTrackCommand(AudioTrack track) : IEditCommand
{
    public string Title => "Добавить аудиодорожку";

    public AudioTrack Track { get; } = track;

    public Sequence Apply(Sequence sequence) =>
        sequence.WithTracks([.. sequence.AudioTracks, Track]);
}

/// <summary>Удалить аудиодорожку вместе со всем, что на ней лежит.</summary>
public sealed class RemoveAudioTrackCommand(AudioTrackId trackId) : IEditCommand
{
    public string Title => "Удалить аудиодорожку";

    public AudioTrackId TrackId { get; } = trackId;

    public Sequence Apply(Sequence sequence)
    {
        var tracks = sequence.AudioTracks.Where(track => track.Id != TrackId).ToList();

        if (tracks.Count == sequence.AudioTracks.Count)
        {
            throw new EditOperationException($"Аудиодорожка {TrackId} не найдена.");
        }

        return sequence.WithTracks(tracks);
    }
}

/// <summary>
/// Отделить звук видеоклипа на аудиодорожку.
/// </summary>
/// <remarks>
/// Отсюда растут «подрезать звук отдельно от картинки», «убрать родной звук
/// и подставить другой», «сдвинуть реплику на полсекунды». Пока звук приклеен
/// к видеоклипу, ничего этого сделать нельзя: он режется вместе с кадром.
/// Скорость клипа переносится на кусок звука, иначе отделённая дорожка
/// немедленно уехала бы относительно картинки.
/// </remarks>
public sealed class DetachClipAudioCommand(ClipId clipId, AudioTrackId? trackId = null) : IEditCommand
{
    public string Title => "Отделить звук";

    public ClipId ClipId { get; } = clipId;

    public AudioTrackId? TrackId { get; } = trackId;

    public Sequence Apply(Sequence sequence)
    {
        var placed = sequence.EnumeratePlaced().FirstOrDefault(item => item.Clip.Id == ClipId);

        if (placed.Clip is null)
        {
            throw new EditOperationException($"Клип {ClipId} не найден на дорожке.");
        }

        var clip = placed.Clip;

        if (!clip.SourceHasAudio)
        {
            throw new EditOperationException("У этого клипа нет звука — отделять нечего.");
        }

        var audio = new AudioClip(AudioClipId.New(), clip.SourceId, clip.SourceRange, placed.Start)
        {
            SourceDuration = clip.SourceDuration,
            Speed = clip.Speed,
            Gain = clip.Audio.Volume,
            Title = clip.Label ?? string.Empty
        };

        // Родной звук клипа выключается: иначе он зазвучал бы дважды.
        var video = sequence.Video.Replace(clip with { Audio = ClipAudio.Muted });
        var result = sequence.WithTrack(video);

        if (TrackId is { } id)
        {
            return result.WithTrack(result.RequireTrack(id).Add(audio));
        }

        var track = AudioTrack.Empty("Звук видео") with { Clips = [audio] };
        return result.WithTracks([.. result.AudioTracks, track]);
    }
}

/// <summary>Свойства всей дорожки: имя, тишина, громкость.</summary>
public sealed class SetAudioTrackPropertiesCommand(
    AudioTrackId trackId,
    string? title = null,
    bool? muted = null,
    double? gain = null) : IEditCommand
{
    public string Title => "Изменить аудиодорожку";

    public AudioTrackId TrackId { get; } = trackId;

    /// <summary>Незаданное свойство не трогается.</summary>
    public string? TrackTitle { get; } = title;

    public bool? Muted { get; } = muted;

    public double? Gain { get; } = gain;

    public Sequence Apply(Sequence sequence)
    {
        var track = sequence.RequireTrack(TrackId);

        if (TrackTitle is { Length: > 0 } newTitle)
        {
            track = track with { Title = newTitle };
        }

        if (Muted is { } isMuted)
        {
            track = track with { IsMuted = isMuted };
        }

        if (Gain is { } newGain)
        {
            track = track.WithGain(newGain);
        }

        return sequence.WithTrack(track);
    }

    /// <summary>
    /// Тянут ползунок громкости — в истории это одна правка, а не сорок.
    /// Свойства объединяются: склеенная команда применяется к состоянию до
    /// предыдущей правки, и «заменить целиком» стёрло бы всё, чего она не задаёт.
    /// </summary>
    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        if (previous is not SetAudioTrackPropertiesCommand other || other.TrackId != TrackId)
        {
            merged = this;
            return false;
        }

        merged = new SetAudioTrackPropertiesCommand(
            TrackId,
            TrackTitle ?? other.TrackTitle,
            Muted ?? other.Muted,
            Gain ?? other.Gain);

        return true;
    }
}
