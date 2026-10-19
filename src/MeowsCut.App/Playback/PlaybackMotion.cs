using System.Diagnostics;

namespace MeowsCut.App.Playback;

/// <summary>
/// Идёт ли ролик на самом деле — по нему наложенный звук решает, играть или ждать.
/// </summary>
/// <remarks>
/// Нажатие «Играть» и переход на следующий файл не значат, что картинка пошла:
/// 4K HEVC раскачивается до секунды. Музыка, запущенная сразу, за это время
/// уезжала вперёд, и выравнивание по порогу отдёргивало её назад — раз за разом,
/// пока ролик не тронется. Поэтому наложенный звук играет, только пока позиция
/// ролика действительно движется, и замирает вместе с картинкой.
/// </remarks>
public sealed class PlaybackMotion
{
    /// <summary>
    /// Столько позиция может стоять, прежде чем ролик считается замершим. Больше шага
    /// таймера: проигрыватель обновляет позицию кадрами, и два шага подряд с одним
    /// значением — ещё не остановка.
    /// </summary>
    public static readonly TimeSpan StallThreshold = TimeSpan.FromMilliseconds(120);

    private static readonly Stopwatch WallClock = Stopwatch.StartNew();

    private readonly Func<TimeSpan> _now;

    private TimeSpan _lastPosition;
    private TimeSpan _lastMoveAt;
    private bool _hasMoved;

    public PlaybackMotion(Func<TimeSpan>? clock = null) => _now = clock ?? (() => WallClock.Elapsed);

    /// <summary>Можно ли сейчас звучать наложенному звуку.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>
    /// Ролик запустили, перемотали или остановили: движения ещё не было,
    /// звук ждёт первого сдвига позиции.
    /// </summary>
    public void Restart(TimeSpan position)
    {
        _lastPosition = position;
        _hasMoved = false;
        IsMoving = false;
    }

    /// <summary>Шаг воспроизведения.</summary>
    /// <param name="waitingForPlayer">Проигрыватель ещё открывает файл — картинки нет наверняка.</param>
    /// <returns>То же, что <see cref="IsMoving"/>.</returns>
    public bool Observe(TimeSpan position, bool playing, bool waitingForPlayer)
    {
        if (!playing)
        {
            Restart(position);
            return false;
        }

        var now = _now();

        if (position < _lastPosition)
        {
            // Позиция ушла назад — это перемотка, а не ход ролика.
            Restart(position);
        }
        else if (position > _lastPosition)
        {
            _hasMoved = true;
            _lastMoveAt = now;
            _lastPosition = position;
        }

        IsMoving = !waitingForPlayer && _hasMoved && now - _lastMoveAt <= StallThreshold;
        return IsMoving;
    }
}
