using System.Windows;
using System.Windows.Controls;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Playback;

/// <summary>
/// Одна звуковая дорожка в предпросмотре.
/// </summary>
/// <remarks>
/// Каждой дорожке — свой <see cref="MediaElement"/>: смешивать звук самим значило бы
/// написать микшер с ресемплером, а система уже умеет играть несколько файлов разом.
/// Тональность здесь не сдвигается — <see cref="MediaElement"/> этого не умеет,
/// и в предпросмотре звук идёт как есть; в экспорте сдвиг применяется полностью.
/// </remarks>
public sealed class AudioLanePlayer
{
    /// <summary>Расхождение, после которого дорожку подтягивают к общей позиции.</summary>
    private static readonly TimeSpan DriftTolerance = TimeSpan.FromMilliseconds(180);

    private readonly MediaElement _element;

    private AudioClipId? _currentClip;
    private TimeSpan? _pendingSeek;
    private bool _isOpened;
    private bool _wantsPlay;

    public AudioLanePlayer()
    {
        // Нулевая высота, а не Collapsed: вне визуального дерева и со свёрнутым
        // размещением MediaElement молча не играет.
        _element = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Width = 0,
            Height = 0,
            Volume = 1d
        };

        _element.MediaOpened += (_, _) =>
        {
            _isOpened = true;

            if (_pendingSeek is { } seek)
            {
                _element.Position = seek;
                _pendingSeek = null;
            }

            if (_wantsPlay)
            {
                _element.Play();
            }
        };

        _element.MediaFailed += (_, _) =>
        {
            // Файл не по зубам системе. Молчим: ронять предпросмотр из-за
            // подложенного звука нельзя, в экспорте его возьмёт ffmpeg.
            _isOpened = false;
            _currentClip = null;
        };
    }

    public UIElement Visual => _element;

    /// <summary>Ставит дорожку в нужную точку таймлайна и решает, звучать ей или молчать.</summary>
    public void Sync(AudioTrack track, string? filePath, TimeSpan position, bool playing, bool seeked)
    {
        var clip = track.IsAudible ? track.ClipAt(position) : null;

        if (clip is null || filePath is null)
        {
            Silence();
            return;
        }

        var sourceTime = clip.SourceTimeAt(position);
        var changed = _currentClip != clip.Id ||
                      !string.Equals(_element.Source?.LocalPath, filePath, StringComparison.OrdinalIgnoreCase);

        _element.Volume = Math.Clamp(track.Gain * clip.Gain, 0d, 1d);
        _element.SpeedRatio = Math.Clamp(clip.Speed, 0.05, 16d);
        _wantsPlay = playing;

        if (changed)
        {
            _currentClip = clip.Id;
            _isOpened = false;
            _pendingSeek = sourceTime;
            _element.Source = new Uri(filePath);
            return;
        }

        if (!_isOpened)
        {
            _pendingSeek = sourceTime;
            return;
        }

        // При воспроизведении дорожку не дёргают на каждом кадре: подтягивают,
        // только если она заметно уехала, иначе звук превратился бы в заикание.
        if (seeked || !playing || (_element.Position - sourceTime).Duration() > DriftTolerance)
        {
            _element.Position = sourceTime;
        }

        if (playing)
        {
            _element.Play();
        }
        else
        {
            _element.Pause();
        }
    }

    public void Silence()
    {
        _wantsPlay = false;
        _currentClip = null;
        _element.Pause();
    }

    public void Close()
    {
        Silence();
        _isOpened = false;
        _pendingSeek = null;
        _element.Stop();
        _element.Source = null;
    }
}
