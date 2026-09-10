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

    public MediaElementPlayer()
    {
        _element = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            ScrubbingEnabled = true,          // без этого при паузе не видно кадр
            Stretch = Stretch.Uniform,
            Volume = 1d
        };

        _element.MediaOpened += (_, _) =>
        {
            _isOpened = true;
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
    }

    public void Play()
    {
        if (Source is null)
        {
            return;
        }

        IsPlaying = true;
        _element.Play();
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
