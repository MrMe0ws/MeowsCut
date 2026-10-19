using System.Windows.Media;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Playback;

/// <summary>
/// Одна звуковая дорожка в предпросмотре.
/// </summary>
/// <remarks>
/// Здесь <see cref="MediaPlayer"/>, а не <see cref="System.Windows.Controls.MediaElement"/>:
/// второй — элемент интерфейса и требует места в визуальном дереве, а звуковой дорожке
/// показывать нечего. Спрятанный элемент нулевого размера молчит, и звук не шёл.
///
/// Тональность здесь не сдвигается — системный проигрыватель этого не умеет,
/// в экспорте она применяется полностью. Нарастание и затухание куска идут
/// громкостью проигрывателя: дорожку и так сверяют с плейхедом на каждом кадре.
/// </remarks>
public sealed class AudioLanePlayer
{
    /// <summary>Расхождение, после которого дорожку подтягивают к общей позиции.</summary>
    private static readonly TimeSpan DriftTolerance = TimeSpan.FromMilliseconds(180);

    private readonly MediaPlayer _player = new();

    private AudioClipId? _currentClip;
    private TimeSpan? _pendingSeek;
    private bool _isOpened;
    private bool _wantsPlay;

    public AudioLanePlayer()
    {
        _player.MediaOpened += (_, _) =>
        {
            _isOpened = true;

            if (_pendingSeek is { } seek)
            {
                _player.Position = seek;
                _pendingSeek = null;
            }

            if (_wantsPlay)
            {
                _player.Play();
            }
        };

        _player.MediaFailed += (_, _) =>
        {
            // Файл не по зубам системе. Молчим: ронять предпросмотр из-за
            // подложенного звука нельзя, в экспорте его возьмёт ffmpeg.
            _isOpened = false;
            _currentClip = null;
        };
    }

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

        _player.Volume = Math.Clamp(track.Gain * clip.Gain * clip.FadeGainAt(position), 0d, 1d);
        _player.SpeedRatio = Math.Clamp(clip.Speed, 0.05, 16d);
        _wantsPlay = playing;

        if (!SameFile(filePath))
        {
            _currentClip = clip.Id;
            _isOpened = false;
            _pendingSeek = sourceTime;
            _player.Open(new Uri(filePath));
            return;
        }

        // Следующий кусок того же файла — разрезанный дубль, кусок после паузы.
        // Файл не переоткрывается: MediaPlayer на повторное открытие того же файла
        // события «открыт» не присылает, и дорожка молчала до конца воспроизведения.
        // Достаточно перемотать на точку входа нового куска.
        if (_currentClip != clip.Id)
        {
            _currentClip = clip.Id;
            seeked = true;
        }

        if (!_isOpened)
        {
            _pendingSeek = sourceTime;
            return;
        }

        // При воспроизведении дорожку не дёргают на каждом кадре: подтягивают,
        // только если она заметно уехала, иначе звук превратился бы в заикание.
        if (seeked || !playing || (_player.Position - sourceTime).Duration() > DriftTolerance)
        {
            _player.Position = sourceTime;
        }

        if (playing)
        {
            _player.Play();
        }
        else
        {
            _player.Pause();
        }
    }

    /// <summary>Заглушить дорожку, не трогая её громкость: на время записи озвучки.</summary>
    public bool IsMuted
    {
        get => _player.IsMuted;
        set => _player.IsMuted = value;
    }

    public void Silence()
    {
        _wantsPlay = false;
        _currentClip = null;
        _player.Pause();
    }

    public void Close()
    {
        Silence();
        _isOpened = false;
        _pendingSeek = null;
        _player.Close();
    }

    private bool SameFile(string filePath) =>
        _player.Source is { } source &&
        string.Equals(source.LocalPath, filePath, StringComparison.OrdinalIgnoreCase);
}
