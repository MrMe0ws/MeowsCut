using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Timeline;

/// <summary>Инструмент доски монтажа.</summary>
/// <remarks>
/// Инструментов сознательно два. Отдельная «рука» была третьим режимом, ради которого
/// приходилось переключаться и возвращаться обратно; протяжка доски досталась средней
/// кнопке, пробелу и колесу — они доступны всегда, каким бы инструментом ни работали.
/// </remarks>
public enum TimelineTool
{
    /// <summary>Стрелка: выделение, перенос клипов и обрезка краёв.</summary>
    Select = 0,

    /// <summary>Ножницы: клик режет клип.</summary>
    Razor
}

/// <summary>Что находится под курсором.</summary>
public enum TimelineHitKind
{
    Empty = 0,
    Ruler,
    ClipBody,
    ClipStartEdge,
    ClipEndEdge,

    /// <summary>Пустое место на аудиодорожке — там просто тишина.</summary>
    AudioEmpty,
    AudioClipBody,
    AudioClipStartEdge,
    AudioClipEndEdge
}

public readonly record struct TimelineHit(TimelineHitKind Kind, PlacedClip? Clip, TimeSpan Time)
{
    /// <summary>Аудиодорожка под курсором, если он над полосой звука.</summary>
    public AudioTrackId TrackId { get; init; }

    public AudioClip? AudioClip { get; init; }

    public static TimelineHit Nothing(TimeSpan time) => new(TimelineHitKind.Empty, null, time);

    public bool IsEdge => Kind is TimelineHitKind.ClipStartEdge or TimelineHitKind.ClipEndEdge
        or TimelineHitKind.AudioClipStartEdge or TimelineHitKind.AudioClipEndEdge;

    public bool IsAudio => Kind is TimelineHitKind.AudioEmpty or TimelineHitKind.AudioClipBody
        or TimelineHitKind.AudioClipStartEdge or TimelineHitKind.AudioClipEndEdge;
}

/// <summary>
/// Определяет, по чему кликнул пользователь.
/// </summary>
/// <remarks>
/// Зоны краёв клипа фиксированы в пикселях: на мелком масштабе клип шириной в десяток
/// пикселей иначе состоял бы из одних только ручек, и его нельзя было бы выделить.
/// </remarks>
public sealed class TimelineHitTester
{
    public const double EdgeGripPixels = 7d;

    private readonly TimelineLayout _layout = new();

    public double RulerHeight { get; set; } = TimelineLayout.RulerHeight;

    /// <summary>Высота контрола: без неё не разложить полосы, а значит и не понять, куда попали.</summary>
    public double ViewportHeight { get; set; } = 240d;

    public TimelineHit Test(double x, double y, Sequence sequence, TimelineMetrics metrics)
    {
        var time = metrics.XToTime(x);

        if (y < RulerHeight)
        {
            return new TimelineHit(TimelineHitKind.Ruler, null, time);
        }

        var lanes = _layout.Build(ViewportHeight, [.. sequence.AudioTracks.Select(track => track.Id)]);
        var lane = _layout.LaneAt(lanes, y);

        if (lane is { Kind: LaneKind.Audio } audioLane)
        {
            return TestAudio(x, time, audioLane, sequence, metrics);
        }

        foreach (var placed in sequence.EnumeratePlaced())
        {
            var left = metrics.TimeToX(placed.Start);
            var right = metrics.TimeToX(placed.End);

            if (x < left || x > right)
            {
                continue;
            }

            // Ручки не должны занимать больше трети клипа, иначе по телу не попасть.
            var grip = Math.Min(EdgeGripPixels, (right - left) / 3d);

            if (x - left <= grip)
            {
                return new TimelineHit(TimelineHitKind.ClipStartEdge, placed, time);
            }

            if (right - x <= grip)
            {
                return new TimelineHit(TimelineHitKind.ClipEndEdge, placed, time);
            }

            return new TimelineHit(TimelineHitKind.ClipBody, placed, time);
        }

        return TimelineHit.Nothing(time);
    }

    private TimelineHit TestAudio(
        double x,
        TimeSpan time,
        TimelineLane lane,
        Sequence sequence,
        TimelineMetrics metrics)
    {
        var track = sequence.FindTrack(lane.TrackId);

        var miss = new TimelineHit(TimelineHitKind.AudioEmpty, null, time) { TrackId = lane.TrackId };

        if (track is null)
        {
            return miss;
        }

        foreach (var clip in track.Clips)
        {
            var left = metrics.TimeToX(clip.TimelineStart);
            var right = metrics.TimeToX(clip.TimelineEnd);

            if (x < left || x > right)
            {
                continue;
            }

            var grip = Math.Min(EdgeGripPixels, (right - left) / 3d);

            var kind = x - left <= grip
                ? TimelineHitKind.AudioClipStartEdge
                : right - x <= grip
                    ? TimelineHitKind.AudioClipEndEdge
                    : TimelineHitKind.AudioClipBody;

            return new TimelineHit(kind, null, time) { TrackId = lane.TrackId, AudioClip = clip };
        }

        return miss;
    }

    /// <summary>Индекс, на который встанет перетаскиваемый клип при отпускании в точке x.</summary>
    public int ResolveDropIndex(double x, Sequence sequence, TimelineMetrics metrics, ClipId draggedClip)
    {
        var time = metrics.XToTime(x);
        var index = 0;

        foreach (var placed in sequence.EnumeratePlaced())
        {
            if (placed.Clip.Id == draggedClip)
            {
                continue;
            }

            // Клип встаёт перед соседом, если курсор левее его середины.
            var middle = placed.Start + TimeSpan.FromTicks(placed.Clip.TimelineDuration.Ticks / 2);
            if (time < middle)
            {
                break;
            }

            index++;
        }

        return Math.Clamp(index, 0, Math.Max(0, sequence.ClipCount - 1));
    }
}
