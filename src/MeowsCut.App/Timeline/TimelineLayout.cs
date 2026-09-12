using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Timeline;

/// <summary>Что за полоса: надписи, видеоряд или звук.</summary>
public enum LaneKind
{
    Video = 0,
    Audio,

    /// <summary>Узкая полоса надписей над видеорядом.</summary>
    Title
}

/// <summary>Полоса доски монтажа со своим местом по вертикали.</summary>
public readonly record struct TimelineLane(LaneKind Kind, double Top, double Height, AudioTrackId TrackId)
{
    public double Bottom => Top + Height;

    public bool Contains(double y) => y >= Top && y < Bottom;
}

/// <summary>
/// Раскладка полос по высоте доски.
/// </summary>
/// <remarks>
/// Вынесена из контрола, потому что нужна двоим: тому, кто рисует, и тому, кто
/// определяет, по чему щёлкнули. Разъедься эти два расчёта — и клик попадал бы
/// не в ту дорожку, что нарисована, а искать такое глазами мучительно.
/// </remarks>
public sealed class TimelineLayout
{
    public const double RulerHeight = 24d;
    public const double TrackPadding = 10d;
    public const double LaneGap = 4d;

    /// <summary>Высота полосы звука: волны нет, нужно место под подпись и ручки.</summary>
    public const double AudioLaneHeight = 54d;

    /// <summary>
    /// Высота полосы надписей. Ниже остальных: в ней нет ни кадров, ни волны —
    /// только отрезок времени с текстом, и отнимать место у видеоряда незачем.
    /// </summary>
    public const double TitleLaneHeight = 24d;

    /// <summary>Ниже этого видеоряд перестаёт показывать кадры.</summary>
    public const double MinVideoLaneHeight = 44d;

    /// <summary>
    /// Раскладывает полосы по высоте.
    /// </summary>
    /// <remarks>
    /// Видеоряд остаётся первым в списке, а полоса надписей уходит в конец,
    /// хотя рисуется выше всех: порядок в списке — это не порядок на экране,
    /// и менять привычное «нулевая полоса — видео» ради этого нельзя.
    /// </remarks>
    public IReadOnlyList<TimelineLane> Build(
        double totalHeight,
        IReadOnlyList<AudioTrackId> audioTracks,
        bool hasTitles = false)
    {
        var top = RulerHeight + TrackPadding;
        var titleHeight = hasTitles ? TitleLaneHeight + LaneGap : 0d;

        var available = Math.Max(MinVideoLaneHeight, totalHeight - top - TrackPadding - titleHeight);

        var audioHeight = audioTracks.Count * (AudioLaneHeight + LaneGap);
        var videoHeight = Math.Max(MinVideoLaneHeight, available - audioHeight);

        var videoTop = top + titleHeight;

        var lanes = new List<TimelineLane>(audioTracks.Count + 2)
        {
            new(LaneKind.Video, videoTop, videoHeight, default)
        };

        var y = videoTop + videoHeight + LaneGap;

        foreach (var trackId in audioTracks)
        {
            lanes.Add(new TimelineLane(LaneKind.Audio, y, AudioLaneHeight, trackId));
            y += AudioLaneHeight + LaneGap;
        }

        if (hasTitles)
        {
            lanes.Add(new TimelineLane(LaneKind.Title, top, TitleLaneHeight, default));
        }

        return lanes;
    }

    public TimelineLane? LaneAt(IReadOnlyList<TimelineLane> lanes, double y)
    {
        foreach (var lane in lanes)
        {
            if (lane.Contains(y))
            {
                return lane;
            }
        }

        return null;
    }
}
