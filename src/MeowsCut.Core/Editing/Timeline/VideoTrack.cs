using MeowsCut.Core.Diagnostics;

namespace MeowsCut.Core.Editing.Timeline;

/// <summary>
/// Видеодорожка: упорядоченные клипы. Позиция клипа вычисляется из порядка и из
/// его собственного отступа, а не хранится абсолютной величиной — вставка в середину
/// не заставляет пересчитывать все остальные клипы.
/// </summary>
/// <remarks>
/// Зазоры между клипами разрешены: без них кусок нельзя было отлепить от соседа
/// и подвинуть по ленте, а именно так собирают ряд из нескольких файлов. В зазоре
/// при экспорте рисуется чёрный кадр с тишиной — см. <see cref="Clip.LeadingGap"/>.
/// </remarks>
public sealed record VideoTrack(IReadOnlyList<Clip> Clips)
{
    public static readonly VideoTrack Empty = new([]);

    public int Count => Clips.Count;

    public bool IsEmpty => Clips.Count == 0;

    public TimeSpan Duration
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (var clip in Clips)
            {
                total += clip.TotalTimelineSpan;
            }

            return total;
        }
    }

    /// <summary>Есть ли на дорожке пустые места. От этого зависит выбор стратегии экспорта.</summary>
    public bool HasGaps
    {
        get
        {
            foreach (var clip in Clips)
            {
                if (clip.HasLeadingGap)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public int IndexOf(ClipId id)
    {
        for (var i = 0; i < Clips.Count; i++)
        {
            if (Clips[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    public Clip? Find(ClipId id)
    {
        var index = IndexOf(id);
        return index < 0 ? null : Clips[index];
    }

    public Clip Require(ClipId id) =>
        Find(id) ?? throw new EditOperationException($"Клип {id} не найден на дорожке.");

    /// <summary>Время начала клипа на таймлайне — сумма занятого предыдущими плюс свой отступ.</summary>
    public TimeSpan StartOf(int index)
    {
        RequireIndex(index);

        var start = TimeSpan.Zero;
        for (var i = 0; i < index; i++)
        {
            start += Clips[i].TotalTimelineSpan;
        }

        return start + Clips[index].LeadingGap;
    }

    /// <summary>Время, в которое заканчивается клип перед указанным. Ноль для первого.</summary>
    public TimeSpan EndOfPrevious(int index)
    {
        RequireIndex(index);

        var end = TimeSpan.Zero;
        for (var i = 0; i < index; i++)
        {
            end += Clips[i].TotalTimelineSpan;
        }

        return end;
    }

    /// <summary>
    /// Ставит клип в указанную точку таймлайна.
    /// </summary>
    /// <remarks>
    /// Клип сначала съедает свободное место рядом, и только исчерпав его толкает
    /// соседей перед собой. Так перетаскивание одного куска не едет всей лентой,
    /// но и не упирается намертво, когда справа стоит вплотную следующий клип.
    /// Наложения не бывает никогда: клип не может встать на предыдущий.
    /// </remarks>
    public VideoTrack MoveInTime(ClipId id, TimeSpan start)
    {
        var index = IndexOf(id);
        if (index < 0)
        {
            throw new EditOperationException($"Клип {id} не найден на дорожке.");
        }

        var clips = Clips.ToArray();
        var clip = clips[index];

        var floor = EndOfPrevious(index);
        var placed = start < floor ? floor : start;

        if (index + 1 < clips.Length)
        {
            // Отступ соседа сжимается до нуля и не дальше: дальше он просто едет
            // вперёд вместе с остальными, а клипы никогда не накладываются.
            var free = StartOf(index + 1) - (placed + clip.TimelineDuration);
            clips[index + 1] = clips[index + 1].WithLeadingGap(free);
        }

        clips[index] = clip.WithLeadingGap(placed - floor);

        return new VideoTrack(clips);
    }

    /// <summary>Середина клипа на таймлайне — по ней перетаскивание решает, менять ли порядок.</summary>
    public TimeSpan MidpointOf(int index)
    {
        RequireIndex(index);
        return StartOf(index) + TimeSpan.FromTicks(Clips[index].TimelineDuration.Ticks / 2);
    }

    public VideoTrack Replace(Clip clip)
    {
        var index = IndexOf(clip.Id);
        if (index < 0)
        {
            throw new EditOperationException($"Клип {clip.Id} не найден на дорожке.");
        }

        var clips = Clips.ToArray();
        clips[index] = clip;
        return new VideoTrack(clips);
    }

    public VideoTrack ReplaceAt(int index, params Clip[] replacement)
    {
        RequireIndex(index);

        var clips = new List<Clip>(Clips.Count + replacement.Length - 1);
        clips.AddRange(Clips.Take(index));
        clips.AddRange(replacement);
        clips.AddRange(Clips.Skip(index + 1));

        return new VideoTrack(clips);
    }

    public VideoTrack Insert(int index, Clip clip)
    {
        if (index < 0 || index > Clips.Count)
        {
            throw new EditOperationException($"Позиция {index} вне дорожки.");
        }

        var clips = new List<Clip>(Clips);
        clips.Insert(index, clip);
        return new VideoTrack(clips);
    }

    public VideoTrack Append(Clip clip) => Insert(Clips.Count, clip);

    public VideoTrack RemoveAt(int index)
    {
        RequireIndex(index);

        var clips = new List<Clip>(Clips);
        clips.RemoveAt(index);
        return new VideoTrack(clips);
    }

    public VideoTrack Remove(ClipId id)
    {
        var index = IndexOf(id);
        if (index < 0)
        {
            throw new EditOperationException($"Клип {id} не найден на дорожке.");
        }

        return RemoveAt(index);
    }

    /// <summary>Перестановка клипа: соседние клипы подтягиваются, зазоров не возникает.</summary>
    public VideoTrack Move(int fromIndex, int toIndex)
    {
        RequireIndex(fromIndex);

        if (toIndex < 0 || toIndex >= Clips.Count)
        {
            throw new EditOperationException($"Позиция {toIndex} вне дорожки.");
        }

        if (fromIndex == toIndex)
        {
            return this;
        }

        var clips = new List<Clip>(Clips);

        // Отступ описывает место клипа относительно соседа, а сосед меняется —
        // тащить старое значение за собой значило бы двигать клип рывком.
        var clip = clips[fromIndex].WithLeadingGap(TimeSpan.Zero);
        clips.RemoveAt(fromIndex);
        clips.Insert(toIndex, clip);

        // Первый клип не может начинаться с отступа: перед ним ничего нет.
        if (clips.Count > 0 && toIndex == 0)
        {
            clips[0] = clips[0].WithLeadingGap(TimeSpan.Zero);
        }

        return new VideoTrack(clips);
    }

    private void RequireIndex(int index)
    {
        if (index < 0 || index >= Clips.Count)
        {
            throw new EditOperationException($"Клип с индексом {index} не найден на дорожке.");
        }
    }
}
