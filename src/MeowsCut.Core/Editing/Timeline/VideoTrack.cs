using MeowsCut.Core.Diagnostics;

namespace MeowsCut.Core.Editing.Timeline;

/// <summary>
/// Видеодорожка: упорядоченные клипы, лежащие встык. Позиция клипа вычисляется
/// из порядка, а не хранится — в v1 зазоров нет, и это исключает состояния
/// «чёрная дыра посреди видео».
/// </summary>
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
                total += clip.TimelineDuration;
            }

            return total;
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

    /// <summary>Время начала клипа на таймлайне — сумма длительностей предыдущих.</summary>
    public TimeSpan StartOf(int index)
    {
        RequireIndex(index);

        var start = TimeSpan.Zero;
        for (var i = 0; i < index; i++)
        {
            start += Clips[i].TimelineDuration;
        }

        return start;
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
        var clip = clips[fromIndex];
        clips.RemoveAt(fromIndex);
        clips.Insert(toIndex, clip);

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
