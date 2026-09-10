using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Playback;

/// <summary>
/// Воспроизведение собранной последовательности одним проигрывателем.
/// </summary>
/// <remarks>
/// Плеер обязан показывать результат монтажа, а не исходник: с учётом порядка клипов,
/// точек входа и выхода, скорости и громкости каждого куска. Контроллер сопоставляет
/// время таймлайна с временем внутри файла и на границе клипа переключает источник.
/// Кадроточного композитора здесь нет — это второй движок размером со всё приложение,
/// поэтому на стыках возможен подскок (ADR-14).
/// </remarks>
public sealed class SequencePlaybackController(IMediaPlayer player)
{
    /// <summary>Насколько раньше конца клипа считаем, что пора переходить к следующему.</summary>
    private static readonly TimeSpan BoundaryTolerance = TimeSpan.FromMilliseconds(60);

    private Project? _project;
    private Sequence _sequence = Sequence.Empty;
    private ClipId? _currentClip;
    private TimeSpan? _pendingSeek;
    private bool _pendingPlay;
    private bool _playerBroken;
    private bool _awaitingOpen;

    public bool IsPlaying { get; private set; }

    /// <summary>Позиция на таймлайне.</summary>
    public TimeSpan Position { get; private set; }

    /// <summary>Проигрыватель не смог открыть файл — нужен покадровый предпросмотр.</summary>
    public bool IsPlayerUnavailable => _playerBroken;

    public event EventHandler<TimeSpan>? PositionChanged;

    public event EventHandler? StateChanged;

    public void Attach(Project project)
    {
        _project = project;
        _sequence = project.Sequence;
        _currentClip = null;
        _playerBroken = false;
        _awaitingOpen = false;

        player.Opened += OnPlayerOpened;
        player.Failed += OnPlayerFailed;

        Seek(TimeSpan.Zero);
    }

    public void Detach()
    {
        Pause();

        player.Opened -= OnPlayerOpened;
        player.Failed -= OnPlayerFailed;
        player.Close();

        _project = null;
        _sequence = Sequence.Empty;
        _currentClip = null;
        Position = TimeSpan.Zero;
    }

    /// <summary>
    /// В проект добавили файл. Последовательность придёт отдельным событием;
    /// здесь важно лишь то, что клип теперь сможет найти свой источник.
    /// </summary>
    public void UpdateProject(Project project) => _project = project;

    /// <summary>Последовательность изменилась на доске — подстраиваем воспроизведение.</summary>
    public void UpdateSequence(Sequence sequence)
    {
        _sequence = sequence;

        if (Position > sequence.Duration)
        {
            Seek(sequence.Duration);
        }
        else if (!IsPlaying)
        {
            // При правках во время паузы кадр должен соответствовать новому монтажу.
            Seek(Position);
        }
    }

    public void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Play()
    {
        if (_project is null || _sequence.IsEmpty || _playerBroken)
        {
            return;
        }

        // С конца последовательности начинаем сначала — иначе кнопка выглядит сломанной.
        if (Position >= _sequence.Duration - BoundaryTolerance)
        {
            Seek(TimeSpan.Zero);
        }

        IsPlaying = true;
        player.Play();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        _pendingPlay = false;
        player.Pause();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Перемотка по времени таймлайна.</summary>
    public void Seek(TimeSpan timelineTime)
    {
        if (_project is null || _sequence.IsEmpty)
        {
            return;
        }

        var clamped = Clamp(timelineTime);
        Position = clamped;
        PositionChanged?.Invoke(this, clamped);

        if (_playerBroken)
        {
            return;
        }

        var placed = _sequence.ClipAt(clamped);

        // На самом конце последовательности показываем последний кадр последнего клипа.
        if (placed is null && _sequence.ClipCount > 0)
        {
            placed = _sequence.EnumeratePlaced().Last();
        }

        if (placed is not { } target)
        {
            return;
        }

        var source = _project.Find(target.Clip.SourceId);
        if (source is null)
        {
            return;
        }

        var offset = clamped - target.Start;
        if (offset < TimeSpan.Zero)
        {
            offset = TimeSpan.Zero;
        }

        var sourceTime = target.Clip.ToSourceTime(offset);

        ApplyClipSettings(target.Clip);

        _currentClip = target.Clip.Id;

        var needsOpen = !string.Equals(player.Source, source.FilePath, StringComparison.OrdinalIgnoreCase);

        // Пока файл открывается, позицию задавать бесполезно: проигрыватель её отбросит.
        // Поэтому перемотки в этот момент копятся, а не теряются.
        if (needsOpen || _awaitingOpen)
        {
            _pendingSeek = sourceTime;
            _pendingPlay |= IsPlaying;

            if (needsOpen)
            {
                _awaitingOpen = true;
                player.Open(source.FilePath);
            }

            return;
        }

        player.Position = sourceTime;
    }

    /// <summary>
    /// Шаг воспроизведения. Вызывается таймером интерфейса, чтобы контроллер
    /// оставался проверяемым без диспетчера и реального времени.
    /// </summary>
    public void Tick()
    {
        if (!IsPlaying || _project is null || _currentClip is not { } clipId)
        {
            return;
        }

        var placed = _sequence.EnumeratePlaced().FirstOrDefault(p => p.Clip.Id == clipId);
        if (placed.Clip is null)
        {
            Pause();
            return;
        }

        var sourcePosition = player.Position;
        var reachedEnd = sourcePosition >= placed.Clip.SourceRange.End - BoundaryTolerance;

        if (reachedEnd)
        {
            AdvanceToNextClip(placed);
            return;
        }

        var offsetInSource = sourcePosition - placed.Clip.SourceRange.Start;
        if (offsetInSource < TimeSpan.Zero)
        {
            offsetInSource = TimeSpan.Zero;
        }

        var timelinePosition = placed.Start + TimeSpan.FromTicks((long)(offsetInSource.Ticks / placed.Clip.Speed));

        Position = Clamp(timelinePosition);
        PositionChanged?.Invoke(this, Position);
    }

    private void AdvanceToNextClip(PlacedClip current)
    {
        var next = _sequence.EnumeratePlaced().FirstOrDefault(p => p.Index == current.Index + 1);

        if (next.Clip is null)
        {
            // Конец последовательности: останавливаемся ровно на нём.
            Position = _sequence.Duration;
            PositionChanged?.Invoke(this, Position);
            Pause();
            return;
        }

        var wasPlaying = IsPlaying;
        Seek(next.Start);

        if (wasPlaying)
        {
            IsPlaying = true;
            player.Play();
        }
    }

    private void ApplyClipSettings(Clip clip)
    {
        player.SpeedRatio = clip.Speed;

        // MediaElement не умеет усиление выше единицы — громче оригинала здесь не сделать,
        // но в экспорт это значение уходит полностью.
        player.Volume = clip.HasAudio ? Math.Clamp(clip.Audio.Volume, 0d, 1d) : 0d;
    }

    private void OnPlayerOpened(object? sender, EventArgs e)
    {
        _playerBroken = false;
        _awaitingOpen = false;

        if (_pendingSeek is { } seek)
        {
            player.Position = seek;
            _pendingSeek = null;
        }

        if (_pendingPlay)
        {
            _pendingPlay = false;
            player.Play();
        }
    }

    private void OnPlayerFailed(object? sender, Exception error)
    {
        // Файл не открылся системным кодеком — переходим на кадры из ffmpeg.
        _playerBroken = true;
        _awaitingOpen = false;
        IsPlaying = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value > _sequence.Duration ? _sequence.Duration : value;
    }
}
