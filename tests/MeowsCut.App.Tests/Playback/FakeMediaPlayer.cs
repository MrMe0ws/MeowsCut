using MeowsCut.App.Playback;

namespace MeowsCut.App.Tests.Playback;

/// <summary>
/// Подставной проигрыватель: позволяет проверить воспроизведение последовательности
/// без окна, без кодеков и без реального времени.
/// </summary>
internal sealed class FakeMediaPlayer : IMediaPlayer
{
    private bool _isOpened;
    private TimeSpan _position;

    public string? Source { get; private set; }

    /// <summary>
    /// Как и настоящий MediaElement, до события Opened позицию не принимает:
    /// иначе тесты не заметили бы потерянную перемотку.
    /// </summary>
    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (_isOpened)
            {
                _position = value;
            }
        }
    }

    public double SpeedRatio { get; set; } = 1d;

    public double Volume { get; set; } = 1d;

    public bool IsPlaying { get; private set; }

    /// <summary>Сколько раз открывался файл — по этому видно лишние переоткрытия.</summary>
    public int OpenCount { get; private set; }

    /// <summary>Открывать файл сразу или отложенно, как это делает настоящий MediaElement.</summary>
    public bool OpenImmediately { get; set; } = true;

    public event EventHandler? Opened;

    public event EventHandler<Exception>? Failed;

    public void Open(string path)
    {
        if (string.Equals(Source, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Source = path;
        OpenCount++;
        _isOpened = false;

        if (OpenImmediately)
        {
            CompleteOpen();
        }
    }

    public void CompleteOpen()
    {
        _isOpened = true;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    public void FailToOpen(string message = "кодек не найден") =>
        Failed?.Invoke(this, new InvalidOperationException(message));

    public void Play() => IsPlaying = true;

    public void Pause() => IsPlaying = false;

    public void Close()
    {
        IsPlaying = false;
        _isOpened = false;
        Source = null;
    }

    /// <summary>Имитация проигрывания: сдвигает позицию внутри файла.</summary>
    public void Advance(TimeSpan delta) => _position += delta;

    /// <summary>Позиция, выставленная напрямую (для проверок, где открытие не важно).</summary>
    public void ForcePosition(TimeSpan value) => _position = value;
}
