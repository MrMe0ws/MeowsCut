using System.Diagnostics;
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
public sealed class SequencePlaybackController
{
    /// <summary>
    /// Ход времени для участков, где проигрывателю нечего играть: фотография,
    /// зазор между клипами, хвост под музыку. Подставляется в тестах — иначе
    /// проверить их можно было бы только реальным ожиданием.
    /// </summary>
    private static readonly Stopwatch WallClock = Stopwatch.StartNew();

    private readonly IMediaPlayer player;
    private readonly Func<TimeSpan> _now;

    public SequencePlaybackController(IMediaPlayer player, Func<TimeSpan>? clock = null)
    {
        this.player = player;
        _now = clock ?? (() => WallClock.Elapsed);
    }

    /// <summary>Насколько раньше конца клипа считаем, что пора переходить к следующему.</summary>
    private static readonly TimeSpan BoundaryTolerance = TimeSpan.FromMilliseconds(60);

    private Project? _project;
    private Sequence _sequence = Sequence.Empty;
    private ClipId? _currentClip;
    private TimeSpan? _pendingSeek;
    private bool _pendingPlay;
    private bool _playerBroken;
    private bool _awaitingOpen;

    /// <summary>
    /// Под курсором фотография. Системный проигрыватель её не откроет, поэтому
    /// картинку показывает интерфейс, а время отсчитывают здесь по часам.
    /// </summary>
    private bool _onImage;
    private string? _lastImagePath;

    /// <summary>Под курсором пустое место: зазор между клипами или хвост под музыку.</summary>
    private bool _onBlackScreen;

    private TimeSpan _lastTickAt;

    public bool IsPlaying { get; private set; }

    /// <summary>Позиция на таймлайне.</summary>
    public TimeSpan Position { get; private set; }

    /// <summary>Проигрыватель не смог открыть файл — нужен покадровый предпросмотр.</summary>
    public bool IsPlayerUnavailable => _playerBroken;

    public event EventHandler<TimeSpan>? PositionChanged;

    public event EventHandler? StateChanged;

    /// <summary>
    /// Под курсором фотография — вот её файл. null означает обычное видео,
    /// то есть кадр снова показывает проигрыватель.
    /// </summary>
    public event EventHandler<string?>? StillImageChanged;

    /// <summary>Под курсором пустое место — картинку показывать нечем.</summary>
    public event EventHandler<bool>? BlackScreenChanged;

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
        _onImage = false;
        _onBlackScreen = false;
        _lastImagePath = null;
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
        _lastTickAt = _now();

        if (!_onImage && !_onBlackScreen)
        {
            player.Play();
        }

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

        if (placed is null)
        {
            // Ровно на конце ролика — последний кадр: пустой экран здесь выглядел бы
            // сбоем перемотки. В зазоре и в хвосте после видеоряда, наоборот, пусто
            // и должно быть: именно это увидят в готовом файле.
            var atVeryEnd = clamped >= _sequence.Duration && !_sequence.HasVideoTail;

            if (atVeryEnd && _sequence.ClipCount > 0)
            {
                placed = _sequence.EnumeratePlaced().Last();
            }
            else
            {
                EnterBlackScreen();
                return;
            }
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

        _currentClip = target.Clip.Id;

        // Фотография: проигрывателю её отдавать нельзя — он сообщит об ошибке,
        // и предпросмотр целиком уедет на запасной режим с кадрами из ffmpeg.
        if (target.Clip.SourceIsImage)
        {
            if (!_onImage || _lastImagePath != source.FilePath)
            {
                _onImage = true;
                _lastImagePath = source.FilePath;
                StillImageChanged?.Invoke(this, source.FilePath);
            }

            player.Pause();
            _lastTickAt = _now();
            return;
        }

        LeaveBlackScreen();

        if (_onImage)
        {
            _onImage = false;
            _lastImagePath = null;
            StillImageChanged?.Invoke(this, null);
        }

        var offset = clamped - target.Start;
        if (offset < TimeSpan.Zero)
        {
            offset = TimeSpan.Zero;
        }

        var sourceTime = target.Clip.ToSourceTime(offset);

        ApplyClipSettings(target.Clip);

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
        if (!IsPlaying || _project is null)
        {
            return;
        }

        // Пустое место — зазор или хвост под музыку: картинки нет, и время идёт
        // по часам. Иначе плейхед застревал бы в пустоте навсегда.
        if (_onBlackScreen)
        {
            AdvanceBlackScreen();
            return;
        }

        if (_currentClip is not { } clipId)
        {
            return;
        }

        var placed = _sequence.EnumeratePlaced().FirstOrDefault(p => p.Clip.Id == clipId);
        if (placed.Clip is null)
        {
            Pause();
            return;
        }

        // Фотография не играет сама: её время идёт по часам, иначе плейхед
        // застыл бы на месте и ролик никогда не дошёл до следующего клипа.
        if (_onImage)
        {
            AdvanceStillFrame(placed);
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

    /// <summary>
    /// Пустое место на ролике: чёрный экран вместо кадра.
    /// </summary>
    /// <remarks>
    /// Проигрыватель ставится на паузу, а не закрывается: следующий клип чаще всего
    /// из того же файла, и повторное открытие стоило бы заметной задержки.
    /// </remarks>
    private void EnterBlackScreen()
    {
        _currentClip = null;
        _lastTickAt = _now();

        if (_onBlackScreen)
        {
            return;
        }

        _onBlackScreen = true;
        player.Pause();

        if (_onImage)
        {
            _onImage = false;
            _lastImagePath = null;
        }

        StillImageChanged?.Invoke(this, null);
        BlackScreenChanged?.Invoke(this, true);
    }

    private void LeaveBlackScreen()
    {
        if (!_onBlackScreen)
        {
            return;
        }

        _onBlackScreen = false;
        BlackScreenChanged?.Invoke(this, false);
    }

    /// <summary>Ход времени в пустоте: как на фотографии, по часам.</summary>
    private void AdvanceBlackScreen()
    {
        var now = _now();
        var next = Position + (now - _lastTickAt);
        _lastTickAt = now;

        if (next >= _sequence.Duration)
        {
            Position = _sequence.Duration;
            PositionChanged?.Invoke(this, Position);
            Pause();
            return;
        }

        Position = next;
        PositionChanged?.Invoke(this, Position);

        // Дошли до следующего клипа — возвращаем картинку.
        if (_sequence.ClipAt(Position) is not null)
        {
            var wasPlaying = IsPlaying;
            Seek(Position);

            if (wasPlaying)
            {
                IsPlaying = true;
                _lastTickAt = _now();

                if (!_onImage && !_onBlackScreen)
                {
                    player.Play();
                }
            }
        }
    }

    /// <summary>Ход времени на фотографии: отсчитывается по часам, а не проигрывателем.</summary>
    private void AdvanceStillFrame(PlacedClip placed)
    {
        var now = _now();
        var elapsed = now - _lastTickAt;
        _lastTickAt = now;

        // Скорость клипа имеет смысл и здесь: ускоренная фотография просто
        // короче стоит на экране.
        var next = Position + (placed.Clip.IsSpeedChanged
            ? TimeSpan.FromTicks((long)(elapsed.Ticks * placed.Clip.Speed))
            : elapsed);

        if (next >= placed.End)
        {
            AdvanceToNextClip(placed);
            return;
        }

        Position = Clamp(next);
        PositionChanged?.Invoke(this, Position);
    }

    /// <summary>
    /// Клип доиграл — что дальше.
    /// </summary>
    /// <remarks>
    /// Пустое место не перепрыгивается: и зазор между клипами, и хвост под музыку —
    /// это часть ролика, и в готовом файле там идёт чёрный кадр. Раньше плеер
    /// телепортировался к следующему клипу, а в конце видеоряда просто вставал,
    /// хотя звук ещё шёл.
    /// </remarks>
    private void AdvanceToNextClip(PlacedClip current)
    {
        var next = _sequence.EnumeratePlaced().FirstOrDefault(p => p.Index == current.Index + 1);

        // Конец видеоряда. Если звук на этом не кончился — досматриваем чёрный экран.
        if (next.Clip is null)
        {
            // Смотрим на конец клипа, а не на текущую позицию: в момент перехода
            // она ещё не сдвинута, и по ней хвост мерещился бы всегда.
            if (current.End < _sequence.Duration - BoundaryTolerance)
            {
                ContinueOnBlack(current.End);
                return;
            }

            Position = _sequence.Duration;
            PositionChanged?.Invoke(this, Position);
            Pause();
            return;
        }

        // Между клипами зазор — проходим его чёрным экраном, а не прыжком.
        if (next.Start > current.End + BoundaryTolerance)
        {
            ContinueOnBlack(current.End);
            return;
        }

        var wasPlaying = IsPlaying;
        Seek(next.Start);

        if (wasPlaying)
        {
            IsPlaying = true;
            _lastTickAt = _now();

            if (!_onImage && !_onBlackScreen)
            {
                player.Play();
            }
        }
    }

    /// <summary>Продолжает воспроизведение по пустому месту, начиная с указанной точки.</summary>
    private void ContinueOnBlack(TimeSpan from)
    {
        Position = Clamp(from);
        PositionChanged?.Invoke(this, Position);

        EnterBlackScreen();
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
