using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MeowsCut.App.Playback;

/// <summary>
/// Проигрыватель на системном <see cref="MediaElement"/>.
/// </summary>
/// <remarks>
/// Играет ровно то, что умеет открыть Windows. Перемотка у него не кадроточная,
/// поэтому на стыках клипов заметен подскок — это принято сознательно
/// (см. ADR-14). Если файл не открывается, предпросмотр переходит на кадры ffmpeg.
/// </remarks>
public sealed class MediaElementPlayer : IMediaPlayer
{
    private readonly MediaElement _element;
    private bool _isOpened;

    /// <summary>Файл ни разу не запускали с момента открытия.</summary>
    private bool _neverPlayed;

    /// <summary>Последняя позиция, которую просили выставить.</summary>
    private TimeSpan? _requestedPosition;

    public MediaElementPlayer()
    {
        _element = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            ScrubbingEnabled = true,          // без этого при паузе не видно кадр
            Volume = 1d
        };

        _element.MediaOpened += (_, _) =>
        {
            _isOpened = true;
            _neverPlayed = true;
            _requestedPosition = null;
            Opened?.Invoke(this, EventArgs.Empty);
        };

        _element.MediaFailed += (_, args) =>
        {
            _isOpened = false;
            Failed?.Invoke(this, args.ErrorException ?? new InvalidOperationException("Файл не открылся"));
        };
    }

    /// <summary>Визуальный элемент для размещения в разметке.</summary>
    public FrameworkElement Visual => _element;

    public string? Source { get; private set; }

    public TimeSpan Position
    {
        get => _element.Position;
        set
        {
            if (_isOpened)
            {
                _element.Position = value;
                _requestedPosition = value;
            }
        }
    }

    public double SpeedRatio
    {
        get => _element.SpeedRatio;
        set => _element.SpeedRatio = Math.Clamp(value, 0.05, 16d);
    }

    public double Volume
    {
        get => _element.Volume;
        set => _element.Volume = Math.Clamp(value, 0d, 1d);
    }

    /// <summary>
    /// Звук выключен поверх громкости клипа. Громкость задаёт контроллер на каждом
    /// клипе, поэтому заглушить предпросмотр на время записи можно только так.
    /// </summary>
    public bool IsMuted
    {
        get => _element.IsMuted;
        set => _element.IsMuted = value;
    }

    public bool IsPlaying { get; private set; }

    public event EventHandler? Opened;

    public event EventHandler<Exception>? Failed;

    public void Open(string path)
    {
        if (string.Equals(Source, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _isOpened = false;
        Source = path;
        _element.Source = new Uri(path);

        // Новый файл открывается на паузе. Без явной команды MediaElement мог начать
        // играть его сам, ещё до события открытия: кусок успевал отыграть со звуком,
        // а потом контроллер отматывал его к точке входа клипа — повтор.
        IsPlaying = false;
        _element.Pause();
    }

    public void Play()
    {
        if (Source is null)
        {
            return;
        }

        IsPlaying = true;
        _element.Play();

        // Перемотку, сделанную до первого запуска после открытия, MediaElement
        // забывает и начинает файл с нуля. Плейхед посреди клипа отскакивал к его
        // началу — на десятки секунд назад, и до только что записанной озвучки
        // ролик просто не доходил. Поэтому позиция повторяется после запуска.
        if (_neverPlayed && _requestedPosition is { } position)
        {
            _element.Position = position;
        }

        _neverPlayed = false;
    }

    public void Pause()
    {
        IsPlaying = false;
        _element.Pause();
    }

    public void Close()
    {
        IsPlaying = false;
        _isOpened = false;
        Source = null;
        _element.Stop();
        _element.Source = null;
    }
}
