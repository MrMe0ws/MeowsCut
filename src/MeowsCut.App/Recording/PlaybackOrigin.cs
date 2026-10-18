using System.Diagnostics;

namespace MeowsCut.App.Recording;

/// <summary>
/// Когда на самом деле пошёл ролик во время записи озвучки.
/// </summary>
/// <remarks>
/// Нажатие «Запись» и первый показанный кадр разделяют десятки, а то и сотни
/// миллисекунд: проигрывателю нужно открыть файл и раскачаться. Голос же
/// пользователь подаёт под то, что видит, — и привязывать запись к моменту нажатия
/// значило бы стабильно сдвигать озвучку на эту задержку.
///
/// Поэтому момент старта вычисляется по ходу воспроизведения: в каждом шаге известно,
/// где стоит ролик и когда это измерено, отсюда — когда ролик был в точке старта.
/// Отдельные измерения пляшут вместе с таймером интерфейса, поэтому берётся медиана
/// первых <see cref="SampleCount"/> шагов после того, как картинка сдвинулась.
/// </remarks>
public sealed class PlaybackOrigin
{
    /// <summary>Столько шагов воспроизведения хватает на устойчивую оценку — это около секунды.</summary>
    public const int SampleCount = 24;

    /// <summary>Пока ролик не сдвинулся хотя бы на столько, он ещё не пошёл.</summary>
    private static readonly TimeSpan MovementThreshold = TimeSpan.FromMilliseconds(15);

    private readonly List<long> _candidates = new(SampleCount);
    private readonly TimeSpan _start;
    private readonly long _requestedAt;
    private TimeSpan _lastPosition;

    /// <param name="start">Позиция на таймлайне, с которой пошла запись.</param>
    /// <param name="requestedAt">Когда ролик попросили играть — запасной ответ, если он так и не пошёл.</param>
    public PlaybackOrigin(TimeSpan start, long requestedAt)
    {
        _start = start;
        _requestedAt = requestedAt;
        _lastPosition = start;
    }

    /// <summary>Момент, когда ролик был в точке старта: метка <see cref="Stopwatch"/>.</summary>
    public long Timestamp => _candidates.Count == 0 ? _requestedAt : Median();

    /// <summary>Оценка уже не уточняется — дальше измерения не нужны.</summary>
    public bool IsSettled => _candidates.Count >= SampleCount;

    /// <summary>Шаг воспроизведения: ролик стоял в <paramref name="position"/> в момент <paramref name="timestamp"/>.</summary>
    public void Observe(TimeSpan position, long timestamp)
    {
        if (IsSettled)
        {
            return;
        }

        // Перемотка назад или прыжок через склейку — уже не та непрерывная дорожка,
        // по которой можно считать: такие шаги пропускаются.
        var moved = position - _start;
        var step = position - _lastPosition;
        _lastPosition = position;

        if (moved < MovementThreshold || step < TimeSpan.Zero)
        {
            return;
        }

        _candidates.Add(timestamp - (long)(moved.TotalSeconds * Stopwatch.Frequency));
    }

    private long Median()
    {
        var sorted = _candidates.Order().ToArray();
        return sorted[sorted.Length / 2];
    }
}
