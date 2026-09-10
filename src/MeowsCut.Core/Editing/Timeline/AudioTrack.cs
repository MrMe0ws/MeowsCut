using MeowsCut.Core.Diagnostics;

namespace MeowsCut.Core.Editing.Timeline;

/// <summary>Идентификатор аудиодорожки.</summary>
public readonly record struct AudioTrackId(Guid Value)
{
    public static AudioTrackId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N")[..8];
}

/// <summary>
/// Аудиодорожка: куски звука со своими местами на таймлайне.
/// </summary>
/// <remarks>
/// Дорожек может быть несколько — на этом и держится наложение звуков. Порядок
/// дорожек ни на что не влияет: при экспорте они смешиваются, а не перекрывают
/// друг друга, в отличие от видео.
/// </remarks>
public sealed record AudioTrack(AudioTrackId Id, string Title, IReadOnlyList<AudioClip> Clips)
{
    public const double MinGain = 0d;
    public const double MaxGain = 4d;

    /// <summary>Дорожка выключена: не звучит и не попадает в экспорт.</summary>
    public bool IsMuted { get; init; }

    /// <summary>Громкость всей дорожки, поверх громкости отдельных кусков.</summary>
    public double Gain { get; init; } = 1d;

    public int Count => Clips.Count;

    public bool IsEmpty => Clips.Count == 0;

    public bool IsAudible => !IsMuted && Gain > 0.0001 && Clips.Count > 0;

    public TimeSpan Duration
    {
        get
        {
            var end = TimeSpan.Zero;

            foreach (var clip in Clips)
            {
                if (clip.TimelineEnd > end)
                {
                    end = clip.TimelineEnd;
                }
            }

            return end;
        }
    }

    public static AudioTrack Empty(string title) => new(AudioTrackId.New(), title, []);

    public int IndexOf(AudioClipId id)
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

    public AudioClip? Find(AudioClipId id)
    {
        var index = IndexOf(id);
        return index < 0 ? null : Clips[index];
    }

    public AudioClip Require(AudioClipId id) =>
        Find(id) ?? throw new EditOperationException($"Кусок звука {id} не найден на дорожке «{Title}».");

    /// <summary>Кусок, звучащий в этот момент. Пауза между кусками — не ошибка, а тишина.</summary>
    public AudioClip? ClipAt(TimeSpan time) =>
        Clips.FirstOrDefault(clip => clip.TimelineRange.Contains(time));

    /// <summary>Куски по порядку на таймлайне — так их рисуют и так подают в микшер.</summary>
    public IEnumerable<AudioClip> InTimelineOrder() => Clips.OrderBy(clip => clip.TimelineStart);

    public AudioTrack Add(AudioClip clip) => this with { Clips = [.. Clips, clip] };

    public AudioTrack Replace(AudioClip clip)
    {
        var index = IndexOf(clip.Id);
        if (index < 0)
        {
            throw new EditOperationException($"Кусок звука {clip.Id} не найден на дорожке «{Title}».");
        }

        var clips = Clips.ToArray();
        clips[index] = clip;
        return this with { Clips = clips };
    }

    public AudioTrack ReplaceAt(int index, params AudioClip[] replacement)
    {
        if (index < 0 || index >= Clips.Count)
        {
            throw new EditOperationException($"Кусок звука с индексом {index} не найден.");
        }

        var clips = new List<AudioClip>(Clips.Count + replacement.Length - 1);
        clips.AddRange(Clips.Take(index));
        clips.AddRange(replacement);
        clips.AddRange(Clips.Skip(index + 1));

        return this with { Clips = clips };
    }

    public AudioTrack Remove(AudioClipId id)
    {
        var index = IndexOf(id);
        if (index < 0)
        {
            throw new EditOperationException($"Кусок звука {id} не найден на дорожке «{Title}».");
        }

        var clips = new List<AudioClip>(Clips);
        clips.RemoveAt(index);
        return this with { Clips = clips };
    }

    public AudioTrack WithGain(double gain) => this with { Gain = Math.Clamp(gain, MinGain, MaxGain) };
}
