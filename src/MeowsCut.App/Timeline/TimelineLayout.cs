using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Timeline;

/// <summary>Что за полоса: видеоряд или звук.</summary>
public enum LaneKind
{
    Video = 0,
    Audio
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
    public const double AudioLaneHeight = 40d;

    /// <summary>Ниже этого видеоряд перестаёт показывать кадры.</summary>
    public const double MinVideoLaneHeight = 44d;

    public IReadOnlyList<TimelineLane> Build(double totalHeight, IReadOnlyList<AudioTrackId> audioTracks)
    {
        var top = RulerHeight + TrackPadding;
        var available = Math.Max(MinVideoLaneHeight, totalHeight - top - TrackPadding);

        var audioHeight = audioTracks.Count * (AudioLaneHeight + LaneGap);
        var videoHeight = Math.Max(MinVideoLaneHeight, available - audioHeight);

        var lanes = new List<TimelineLane>(audioTracks.Count + 1)
        {
            new(LaneKind.Video, top, videoHeight, default)
        };

        var y = top + videoHeight + LaneGap;

        foreach (var trackId in audioTracks)
        {
            lanes.Add(new TimelineLane(LaneKind.Audio, y, AudioLaneHeight, trackId));
            y += AudioLaneHeight + LaneGap;
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
