using System.Diagnostics;

namespace MeowsCut.App.ViewModels;

/// <summary>Шаг воспроизведения: где стоял ролик и когда это измерено (метка <see cref="Stopwatch"/>).</summary>
public readonly record struct PlaybackTick(TimeSpan Position, long Timestamp);

/// <summary>
/// Предпросмотр под запись озвучки: ролик играет, а микрофон пишет.
/// </summary>
public sealed partial class PreviewViewModel
{
    private bool _isMonitorMuted;

    /// <summary>
    /// Шаг воспроизведения во время игры. По нему запись привязывается к ролику:
    /// метка ставится сразу после опроса проигрывателя, а не после перерисовки,
    /// иначе в неё попала бы задержка очереди интерфейса.
    /// </summary>
    public event EventHandler<PlaybackTick>? Ticked;

    /// <summary>
    /// Пользователь сам передвинул плейхед: щелчком по доске, стрелками, Home и End.
    /// Шаги воспроизведения сюда не попадают.
    /// </summary>
    public event EventHandler? UserSeeked;

    /// <summary>Заглушить ролик и наложенный звук, не трогая громкость клипов.</summary>
    public bool IsMonitorMuted
    {
        get => _isMonitorMuted;
        set
        {
            if (_isMonitorMuted == value)
            {
                return;
            }

            _isMonitorMuted = value;
            _player.IsMuted = value;
            _audio.IsMuted = value;
        }
    }

    /// <summary>Позиция ролика — та, что видна на доске.</summary>
    public TimeSpan Position => _playback.Position;

    /// <summary>Запустить ролик, если он стоит. В отличие от кнопки, не останавливает идущий.</summary>
    public void Play()
    {
        if (UseFallbackFrames || _playback.IsPlaying)
        {
            return;
        }

        _playback.Play();
        _audio.Sync(_playback.Position, _playback.IsPlaying, seeked: true);
        RefreshPlaybackState();
    }

    public void Pause()
    {
        if (!_playback.IsPlaying)
        {
            return;
        }

        _playback.Pause();
        _audio.Sync(_playback.Position, playing: false, seeked: true);
        RefreshPlaybackState();
    }

    private void ReportTick()
    {
        if (_playback.IsPlaying)
        {
            Ticked?.Invoke(this, new PlaybackTick(_playback.Position, Stopwatch.GetTimestamp()));
        }
    }
}
