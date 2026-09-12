using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>
/// Общая часть правок надписи: найти её в списке и заменить новой.
/// </summary>
/// <remarks>
/// Список надписей держится отсортированным по времени: так они идут по порядку
/// и в полосе доски, и в графе фильтров, и читать его глазами проще.
/// </remarks>
public abstract class TitleCommand(TitleId titleId) : IEditCommand
{
    public TitleId TitleId { get; } = titleId;

    public abstract string Title { get; }

    public Sequence Apply(Sequence sequence)
    {
        var titles = new List<TitleClip>(sequence.Titles);
        var index = titles.FindIndex(title => title.Id == TitleId);

        if (index < 0)
        {
            return sequence;
        }

        var changed = Change(titles[index]);

        if (changed is null)
        {
            titles.RemoveAt(index);
        }
        else
        {
            titles[index] = changed;
        }

        titles.Sort((left, right) => left.TimelineStart.CompareTo(right.TimelineStart));

        return sequence.WithTitles(titles);
    }

    /// <summary>Новая надпись или null, если её нужно убрать.</summary>
    protected abstract TitleClip? Change(TitleClip title);

    public virtual bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;
        return previous.GetType() == GetType() &&
               previous is TitleCommand other &&
               other.TitleId == TitleId;
    }
}

/// <summary>Новая надпись на ленте.</summary>
public sealed class AddTitleCommand(TitleClip title) : IEditCommand
{
    public string Title => "Добавить надпись";

    public TitleClip Clip { get; } = title;

    public Sequence Apply(Sequence sequence)
    {
        var titles = new List<TitleClip>(sequence.Titles) { Clip };
        titles.Sort((left, right) => left.TimelineStart.CompareTo(right.TimelineStart));

        return sequence.WithTitles(titles);
    }

    public bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;
        return false;
    }
}

public sealed class RemoveTitleCommand(TitleId titleId) : TitleCommand(titleId)
{
    public override string Title => "Убрать надпись";

    protected override TitleClip? Change(TitleClip title) => null;

    public override bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;
        return false;
    }
}

/// <summary>Текст надписи. Склеивается в одну запись истории: его набирают по букве.</summary>
public sealed class SetTitleTextCommand(TitleId titleId, string text) : TitleCommand(titleId)
{
    public override string Title => "Изменить текст надписи";

    public string Text { get; } = text;

    protected override TitleClip Change(TitleClip title) => title.WithText(Text);
}

/// <summary>Сдвиг надписи по ленте.</summary>
public sealed class MoveTitleCommand(TitleId titleId, TimeSpan start) : TitleCommand(titleId)
{
    public override string Title => "Передвинуть надпись";

    public TimeSpan Start { get; } = start;

    protected override TitleClip Change(TitleClip title) => title.WithStart(Start);
}

/// <summary>Длительность надписи.</summary>
public sealed class SetTitleDurationCommand(TitleId titleId, TimeSpan duration) : TitleCommand(titleId)
{
    public override string Title => "Изменить длительность надписи";

    public TimeSpan Duration { get; } = duration;

    protected override TitleClip Change(TitleClip title) => title.WithDuration(Duration);
}

/// <summary>
/// Вид надписи: место на кадре, размер, цвет и плашка.
/// </summary>
/// <remarks>
/// Одной командой, потому что правят их подряд, глядя на кадр: отдельная запись
/// в истории на каждый щелчок заставляла бы жать Ctrl+Z по десять раз.
/// </remarks>
public sealed class SetTitleLookCommand(
    TitleId titleId,
    TitleAnchor? anchor = null,
    double? scale = null,
    string? color = null,
    bool? backdrop = null)
    : TitleCommand(titleId)
{
    public override string Title => "Изменить вид надписи";

    public TitleAnchor? Anchor { get; } = anchor;

    public double? Scale { get; } = scale;

    public string? Color { get; } = color;

    public bool? Backdrop { get; } = backdrop;

    protected override TitleClip Change(TitleClip title)
    {
        var changed = title;

        if (Anchor is { } anchorValue)
        {
            changed = changed with { Anchor = anchorValue };
        }

        if (Scale is { } scaleValue)
        {
            changed = changed.WithScale(scaleValue);
        }

        if (Color is { } colorValue)
        {
            changed = changed with { Color = colorValue };
        }

        if (Backdrop is { } backdropValue)
        {
            changed = changed with { Backdrop = backdropValue };
        }

        return changed;
    }

    public override bool TryMergeWith(IEditCommand previous, out IEditCommand merged)
    {
        merged = this;

        // Склеиваем только правку того же свойства: подвинули размер, потом
        // сменили цвет — это два разных действия.
        return previous is SetTitleLookCommand other &&
               other.TitleId == TitleId &&
               (other.Anchor is null) == (Anchor is null) &&
               (other.Scale is null) == (Scale is null) &&
               (other.Color is null) == (Color is null) &&
               (other.Backdrop is null) == (Backdrop is null);
    }
}
