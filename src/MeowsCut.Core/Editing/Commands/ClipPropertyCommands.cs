using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>
/// Скорость отдельного клипа: 0.25×, 2×, произвольная.
/// </summary>
public sealed class SetClipSpeedCommand(ClipId clipId, double speed) : IEditCommand
{
    public string Title => "Изменить скорость клипа";

    public ClipId ClipId { get; } = clipId;

    public double Speed { get; } = speed;

    public Sequence Apply(Sequence sequence)
    {
        var clip = sequence.Video.Require(ClipId);
        return sequence.WithTrack(sequence.Video.Replace(clip.WithSpeed(Speed)));
    }

    /// <summary>Ползунок скорости шлёт значения непрерывно — в истории это одна правка.</summary>
    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        if (previous is SetClipSpeedCommand other && other.ClipId == ClipId)
        {
            merged = this;
            return true;
        }

        merged = this;
        return false;
    }
}

/// <summary>
/// Звук клипа: включение и громкость.
/// </summary>
public sealed class SetClipAudioCommand(ClipId clipId, ClipAudio audio) : IEditCommand
{
    public string Title => Audio.Enabled ? "Изменить звук клипа" : "Выключить звук клипа";

    public ClipId ClipId { get; } = clipId;

    public ClipAudio Audio { get; } = audio;

    public Sequence Apply(Sequence sequence)
    {
        var clip = sequence.Video.Require(ClipId);
        return sequence.WithTrack(sequence.Video.Replace(clip.WithAudio(Audio)));
    }

    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        // Склеиваем только тягание громкости; включение и выключение звука —
        // отдельные осознанные действия, каждое должно отменяться само по себе.
        if (previous is SetClipAudioCommand other &&
            other.ClipId == ClipId &&
            other.Audio.Enabled == Audio.Enabled)
        {
            merged = this;
            return true;
        }

        merged = this;
        return false;
    }
}

/// <summary>
/// Масштаб и положение кадра внутри кадра последовательности.
/// Нужно для стикеров: произвольное видео приводится к квадрату, и пользователь
/// выбирает, какую часть кадра оставить.
/// </summary>
public sealed class SetClipTransformCommand(ClipId clipId, ClipTransform transform) : IEditCommand
{
    public string Title => "Изменить кадрирование";

    public ClipId ClipId { get; } = clipId;

    public ClipTransform Transform { get; } = transform;

    public Sequence Apply(Sequence sequence)
    {
        var clip = sequence.Video.Require(ClipId);
        return sequence.WithTrack(sequence.Video.Replace(clip.WithTransform(Transform)));
    }

    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        if (previous is SetClipTransformCommand other && other.ClipId == ClipId)
        {
            merged = this;
            return true;
        }

        merged = this;
        return false;
    }
}
