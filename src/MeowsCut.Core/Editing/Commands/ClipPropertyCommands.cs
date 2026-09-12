using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>
/// Общая часть правок, меняющих свойство сразу у нескольких выделенных клипов.
/// </summary>
/// <remarks>
/// Команда берёт набор клипов, а не один: выделив три куска и нажав «2×»,
/// пользователь ждёт, что ускорятся все три и что Ctrl+Z вернёт их разом,
/// а не по одному. Один клип — частный случай набора из одного элемента.
/// </remarks>
public abstract class ClipPropertyCommand(IReadOnlyList<ClipId> clipIds) : IEditCommand
{
    public IReadOnlyList<ClipId> ClipIds { get; } = clipIds;

    public abstract string Title { get; }

    public Sequence Apply(Sequence sequence)
    {
        var track = sequence.Video;

        foreach (var id in ClipIds)
        {
            track = track.Replace(Change(track.Require(id)));
        }

        return sequence.WithTrack(track);
    }

    protected abstract Clip Change(Clip clip);

    /// <summary>Тот же набор клипов и то же свойство — одна запись в истории.</summary>
    public virtual bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;
        return previous.GetType() == GetType() &&
               previous is ClipPropertyCommand other &&
               SameClips(other.ClipIds);
    }

    protected bool SameClips(IReadOnlyList<ClipId> other)
    {
        if (other.Count != ClipIds.Count)
        {
            return false;
        }

        for (var i = 0; i < other.Count; i++)
        {
            if (other[i] != ClipIds[i])
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Скорость клипов: 0.25×, 2×, произвольная.
/// </summary>
public sealed class SetClipSpeedCommand(IReadOnlyList<ClipId> clipIds, double speed)
    : ClipPropertyCommand(clipIds)
{
    public SetClipSpeedCommand(ClipId clipId, double speed) : this([clipId], speed)
    {
    }

    public override string Title => "Изменить скорость клипа";

    public double Speed { get; } = speed;

    protected override Clip Change(Clip clip) => clip.WithSpeed(Speed);
}

/// <summary>
/// Звук клипов: включение и громкость.
/// </summary>
public sealed class SetClipAudioCommand(IReadOnlyList<ClipId> clipIds, ClipAudio audio)
    : ClipPropertyCommand(clipIds)
{
    public SetClipAudioCommand(ClipId clipId, ClipAudio audio) : this([clipId], audio)
    {
    }

    public override string Title => Audio.Enabled ? "Изменить звук клипа" : "Выключить звук клипа";

    public ClipAudio Audio { get; } = audio;

    protected override Clip Change(Clip clip) => clip.WithAudio(Audio);

    public override bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;

        // Склеиваем только тягание громкости; включение и выключение звука —
        // отдельные осознанные действия, каждое должно отменяться само по себе.
        return previous is SetClipAudioCommand other &&
               other.Audio.Enabled == Audio.Enabled &&
               SameClips(other.ClipIds);
    }
}

/// <summary>
/// Кадр всего ролика: пропорции и то, как в них ложатся куски.
/// </summary>
/// <remarks>
/// Через историю, как и всё остальное: смена формата переворачивает картинку
/// у каждого куска, и отменяться она обязана одним Ctrl+Z.
/// </remarks>
public sealed class SetSequenceFormatCommand(SequenceFormat format) : IEditCommand
{
    public string Title => "Изменить формат ролика";

    public SequenceFormat Format { get; } = format;

    public Sequence Apply(Sequence sequence) => sequence with { Format = Format };

    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;
        return false;
    }
}

/// <summary>
/// Появление из чёрного и уход в чёрное у выделенных клипов.
/// </summary>
/// <remarks>
/// Оба края одной командой: их правят соседними полями, и раздельные записи
/// в истории заставляли бы жать Ctrl+Z дважды там, где пользователь сделал
/// одно движение.
/// </remarks>
public sealed class SetClipFadesCommand(
    IReadOnlyList<ClipId> clipIds,
    TimeSpan? fadeIn = null,
    TimeSpan? fadeOut = null)
    : ClipPropertyCommand(clipIds)
{
    public SetClipFadesCommand(ClipId clipId, TimeSpan? fadeIn = null, TimeSpan? fadeOut = null)
        : this([clipId], fadeIn, fadeOut)
    {
    }

    public override string Title => "Изменить затухание клипа";

    public TimeSpan? FadeIn { get; } = fadeIn;

    public TimeSpan? FadeOut { get; } = fadeOut;

    protected override Clip Change(Clip clip) => clip.WithFades(FadeIn, FadeOut);

    public override bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;

        // Склеиваем только правку того же края: подвинули появление, потом
        // затухание — это два разных действия, и отменяться они должны порознь.
        return previous is SetClipFadesCommand other &&
               (other.FadeIn is null) == (FadeIn is null) &&
               (other.FadeOut is null) == (FadeOut is null) &&
               SameClips(other.ClipIds);
    }
}

/// <summary>
/// Масштаб и положение кадра внутри кадра последовательности.
/// Нужно для стикеров: произвольное видео приводится к квадрату, и пользователь
/// выбирает, какую часть кадра оставить.
/// </summary>
public sealed class SetClipTransformCommand(IReadOnlyList<ClipId> clipIds, ClipTransform transform)
    : ClipPropertyCommand(clipIds)
{
    public SetClipTransformCommand(ClipId clipId, ClipTransform transform) : this([clipId], transform)
    {
    }

    public override string Title => "Изменить кадрирование";

    public ClipTransform Transform { get; } = transform;

    protected override Clip Change(Clip clip) => clip.WithTransform(Transform);
}
