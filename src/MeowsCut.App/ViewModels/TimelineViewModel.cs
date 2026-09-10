using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.History;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Доска монтажа: состояние инструментов, выделение, плейхед и правки.
/// </summary>
/// <remarks>
/// Все изменения последовательности идут через <see cref="EditHistory"/>; ViewModel
/// не мутирует модель напрямую, иначе Ctrl+Z ломается незаметно.
/// </remarks>
/// <summary>Что означает нажатие кнопки мыши: обычный клик, добавление к выделению или протяжка.</summary>
public enum PointerMode
{
    Normal = 0,

    /// <summary>Ctrl: добавить клип к выделению или убрать из него.</summary>
    Toggle,

    /// <summary>Shift: выделить всё от опорного клипа до указанного.</summary>
    Range,

    /// <summary>Средняя кнопка или пробел: тянуть доску.</summary>
    Pan
}

public sealed partial class TimelineViewModel : ObservableObject
{
    /// <summary>
    /// Волны звука для полосы дорожек. Необязательна: тесты доски работают
    /// без ffmpeg, и рисовать им нечего.
    /// </summary>
    private readonly AudioWaveformCache? _waveforms;

    private readonly TimelineHitTester _hitTester = new();
    private readonly List<ClipViewModel> _clipPool = [];
    private readonly HashSet<ClipId> _selection = [];

    private EditHistory? _history;
    private Project? _project;
    private DragState _drag = DragState.None;
    private double _lastPointerX;
    private double _pressStartX;
    private bool _dragMoved;
    private bool _pressedOnSelected;
    private TimeSpan _dragReferenceTime;
    private bool _needsInitialFit;

    [ObservableProperty]
    private TimelineTool _activeTool = TimelineTool.Select;

    [ObservableProperty]
    private TimeSpan _playhead;

    [ObservableProperty]
    private bool _snapEnabled = true;

    [ObservableProperty]
    private ClipViewModel? _selectedClip;

    /// <summary>
    /// Выбранный кусок звука. Отдельно от видеовыделения: инспектор показывает
    /// либо одно, либо другое, и держать их вместе значило бы каждый раз гадать,
    /// к чему относится «громкость».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAudioSelection))]
    private AudioClip? _selectedAudioClip;

    [ObservableProperty]
    private AudioTrackId _selectedAudioTrack;

    public TimelineViewModel(AudioWaveformCache? waveforms = null)
    {
        _waveforms = waveforms;

        if (_waveforms is not null)
        {
            _waveforms.Ready += (_, _) => VisualInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public TimelineMetrics Metrics { get; } = new();

    /// <summary>
    /// Высота доски. Нужна тесту попаданий: полосы раскладываются по высоте,
    /// и без неё щелчок по звуку считался бы щелчком по видео.
    /// </summary>
    public double ViewportHeight
    {
        get => _hitTester.ViewportHeight;
        set => _hitTester.ViewportHeight = value;
    }

    public SnapEngine Snap { get; } = new();

    public ObservableCollection<ClipViewModel> Clips { get; } = [];

    public Sequence Sequence => _history?.Current ?? Sequence.Empty;

    public bool HasProject => _history is not null;

    /// <summary>Короткий итог доски для строки перед экспортом.</summary>
    public string SummaryLine => _history is null
        ? string.Empty
        : string.Format(
            System.Globalization.CultureInfo.CurrentUICulture,
            Localization.Strings.TimelineSummary,
            Clips.Count,
            Duration.ToString(Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss\.ff",
                System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Сколько клипов выделено — инспектор показывает это, когда их несколько.</summary>
    public int SelectionCount => _selection.Count;

    public bool HasMultipleSelected => _selection.Count > 1;

    public bool HasAudioSelection => SelectedAudioClip is not null;

    /// <summary>
    /// Кисть с волной для куска звука. null — волна ещё считается или её нет.
    /// Отрисовка спрашивает и рисует то, что есть: ждать на доске нельзя.
    /// </summary>
    public System.Windows.Media.Brush? WaveformFor(AudioClip clip)
    {
        if (_waveforms is null || _project?.Find(clip.SourceId) is not { } source)
        {
            return null;
        }

        var duration = clip.SourceDuration > TimeSpan.Zero ? clip.SourceDuration : source.Duration;
        if (duration <= TimeSpan.Zero)
        {
            return null;
        }

        return _waveforms.Brush(
            source.FilePath,
            clip.SourceRange.Start / duration,
            clip.SourceRange.End / duration);
    }

    /// <summary>Дорожки звука для отрисовки и панели свойств.</summary>
    public IReadOnlyList<AudioTrack> AudioTracks => Sequence.AudioTracks;

    public bool HasAudioTracks => Sequence.AudioTracks.Count > 0;

    /// <summary>Выделенные клипы в порядке дорожки.</summary>
    public IReadOnlyList<ClipViewModel> SelectedClips =>
        [.. Clips.Where(clip => _selection.Contains(clip.Id))];

    public bool CanUndo => _history?.CanUndo == true;

    public bool CanRedo => _history?.CanRedo == true;

    public TimeSpan Duration => Sequence.Duration;

    /// <summary>Последовательность изменилась: пора перерисовать доску и обновить проект.</summary>
    public event EventHandler<Sequence>? SequenceChanged;

    /// <summary>Нужно перерисовать контрол (изменились масштаб, выделение или кадры).</summary>
    public event EventHandler? VisualInvalidated;

    /// <summary>Клипам нужны кадры для видимой области.</summary>
    public event EventHandler? ThumbnailsRequested;

    public void Attach(Project project)
    {
        _project = project;
        _history = new EditHistory(project.Sequence);
        _history.Changed += OnHistoryChanged;

        Playhead = TimeSpan.Zero;
        _selection.Clear();
        SelectedClip = null;
        _needsInitialFit = true;

        Metrics.ZoomToFit(project.Sequence.Duration);
        RebuildClips();
        OnPropertyChanged(nameof(SummaryLine));
        RefreshProjectCommands();
    }

    /// <summary>
    /// Добавляет файл в конец видеоряда. Проект передаётся целиком, потому что
    /// в нём уже лежит новый источник — без него клипу неоткуда взять кадры.
    /// </summary>
    public void AppendSource(Project project, MediaSource source)
    {
        _project = project;

        if (_history is null)
        {
            Attach(project);
            return;
        }

        Execute(new AppendClipCommand(Clip.FromSource(source)));
        _history.EndMergeGroup();

        // Новый клип сразу становится выбранным: почти всегда следующее действие — про него.
        _selection.Clear();
        SelectedClip = Clips.LastOrDefault();
        ApplySelectionToClips();

        // И сразу показываем его: добавленный за краем экрана кусок выглядит так,
        // будто кнопка ничего не сделала.
        if (SelectedClip is { } added)
        {
            Metrics.EnsureVisible(added.Start);
            Metrics.EnsureVisible(added.End);
            RequestRedraw();
        }
    }


    /// <summary>
    /// Вписывает последовательность в окно, когда контрол наконец знает свою ширину.
    /// В момент открытия файла она ещё не известна, и масштаб получился бы случайным.
    /// </summary>
    public void EnsureInitialFit()
    {
        if (!_needsInitialFit || _history is null || Duration <= TimeSpan.Zero)
        {
            return;
        }

        _needsInitialFit = false;
        Metrics.ZoomToFit(Duration);
        RequestRedraw();
    }

    /// <summary>
    /// Пересчитывает доступность команд, которым нужен открытый проект.
    /// </summary>
    /// <remarks>
    /// Без этого «Дорожка» оставалась серой навсегда: её условие проверяется один раз
    /// при создании модели, когда проекта ещё нет, и открытие файла об этом не сообщало.
    /// </remarks>
    private void RefreshProjectCommands()
    {
        OnPropertyChanged(nameof(HasProject));
        AddAudioTrackCommand.NotifyCanExecuteChanged();
    }

    public void Detach()
    {
        if (_history is not null)
        {
            _history.Changed -= OnHistoryChanged;
        }

        _history = null;
        _project = null;
        RefreshProjectCommands();
        Clips.Clear();
        _selection.Clear();
        SelectedClip = null;
        Playhead = TimeSpan.Zero;
    }

    private void OnHistoryChanged(object? sender, SequenceChangedEventArgs e)
    {
        RebuildClips();
        SequenceChanged?.Invoke(this, e.Sequence);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(SummaryLine));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Пересобирает проекцию клипов, сохраняя объекты по идентификатору:
    /// так выделение и уже загруженные кадры переживают правку.
    /// </summary>
    private void RebuildClips()
    {
        var selectedId = SelectedClip?.Id;

        _clipPool.Clear();
        _clipPool.AddRange(Clips);
        Clips.Clear();

        foreach (var placed in Sequence.EnumeratePlaced())
        {
            var source = _project?.Find(placed.Clip.SourceId);
            var existing = _clipPool.FirstOrDefault(clip => clip.Id == placed.Clip.Id);

            if (existing is not null)
            {
                existing.Update(placed, source);
                Clips.Add(existing);
            }
            else
            {
                Clips.Add(new ClipViewModel(placed, source));
            }
        }

        // Из выделения выпадают клипы, которых больше нет: разрезанный клип
        // исчезает, и держать его идентификатор — значит удалить потом не то.
        _selection.RemoveWhere(id => Clips.All(clip => clip.Id != id));

        SelectedClip = selectedId is { } previous && _selection.Contains(previous)
            ? Clips.FirstOrDefault(clip => clip.Id == previous)
            : Clips.FirstOrDefault(clip => _selection.Contains(clip.Id));

        foreach (var clip in Clips)
        {
            clip.IsSelected = _selection.Contains(clip.Id);
        }

        if (Playhead > Duration)
        {
            Playhead = Duration;
        }

        RefreshAudioSelection();
        OnPropertyChanged(nameof(AudioTracks));
        OnPropertyChanged(nameof(HasAudioTracks));

        ThumbnailsRequested?.Invoke(this, EventArgs.Empty);
        VisualInvalidated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedAudioClipChanged(AudioClip? value)
    {
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasAudioSelection));
    }

    partial void OnSelectedClipChanged(ClipViewModel? value)
    {
        DetachAudioCommand.NotifyCanExecuteChanged();

        // Основной клип — тот, что показывает инспектор. Само выделение живёт
        // в _selection: сбрасывать его здесь значило бы терять множественный выбор.
        if (value is not null)
        {
            _selection.Add(value.Id);
        }

        SplitAtPlayheadCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        DuplicateSelectedCommand.NotifyCanExecuteChanged();
        VisualInvalidated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnPlayheadChanged(TimeSpan value) => VisualInvalidated?.Invoke(this, EventArgs.Empty);

    partial void OnActiveToolChanged(TimelineTool value) => VisualInvalidated?.Invoke(this, EventArgs.Empty);

    partial void OnSnapEnabledChanged(bool value) => Snap.IsEnabled = value;

    // ───────────────────────────── команды ─────────────────────────────

    [RelayCommand]
    private void SetTool(TimelineTool tool) => ActiveTool = tool;

    [RelayCommand(CanExecute = nameof(CanSplitAtPlayhead))]
    private void SplitAtPlayhead() => Execute(new SplitClipCommand(Playhead));

    private bool CanSplitAtPlayhead() =>
        _history is not null && Sequence.ClipAt(Playhead) is { } placed &&
        placed.Clip.CanSplitAt(Playhead - placed.Start);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        if (SelectedAudioClip is { } audio)
        {
            Execute(new RemoveAudioClipCommand(SelectedAudioTrack, audio.Id));
            _history?.EndMergeGroup();
            SelectedAudioClip = null;
            return;
        }

        if (_selection.Count == 0)
        {
            return;
        }

        // Одно действие пользователя — одна правка в истории, сколько бы клипов
        // он ни выделил: иначе Ctrl+Z возвращал бы их по одному.
        Execute(_selection.Count == 1
            ? new RemoveClipCommand(_selection.First())
            : new RemoveClipsCommand([.. _selection]));

        _history?.EndMergeGroup();
    }

    /// <summary>Выделить все клипы (Ctrl+A).</summary>
    [RelayCommand]
    private void SelectAll()
    {
        _selection.Clear();

        foreach (var clip in Clips)
        {
            _selection.Add(clip.Id);
        }

        SelectedClip = Clips.FirstOrDefault();
        ApplySelectionToClips();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateSelected()
    {
        if (SelectedClip is { } clip)
        {
            Execute(new DuplicateClipCommand(clip.Id));
        }
    }

    private bool HasSelection() => SelectedClip is not null || SelectedAudioClip is not null;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _history?.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => _history?.Redo();

    [RelayCommand]
    private void ZoomIn() => ZoomBy(1.25);

    [RelayCommand]
    private void ZoomOut() => ZoomBy(1 / 1.25);

    [RelayCommand]
    private void ZoomToFit()
    {
        Metrics.ZoomToFit(Duration);
        RequestRedraw();
    }

    [RelayCommand]
    private void ToggleSnap() => SnapEnabled = !SnapEnabled;

    /// <summary>Ставит границу выделенного клипа в позицию курсора (клавиши I и O).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void TrimStartToPlayhead() => TrimEdgeToPlayhead(ClipEdge.Start);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void TrimEndToPlayhead() => TrimEdgeToPlayhead(ClipEdge.End);

    private void TrimEdgeToPlayhead(ClipEdge edge)
    {
        if (SelectedClip is not { } clip)
        {
            return;
        }

        var current = edge == ClipEdge.Start ? clip.Start : clip.End;
        var delta = Playhead - current;

        if (delta != TimeSpan.Zero)
        {
            Execute(new TrimClipEdgeCommand(clip.Id, edge, delta));
            _history?.EndMergeGroup();
        }
    }

    /// <summary>
    /// Заменяет последовательность целиком — например, когда пресет укоротил ролик
    /// под лимит площадки. Идёт через историю, чтобы отменялось как обычная правка.
    /// </summary>
    public void ApplySequence(Sequence sequence, string title)
    {
        Execute(new ReplaceSequenceCommand(sequence, title));
        _history?.EndMergeGroup();
    }

    /// <summary>
    /// Свойства применяются ко всему выделению, а не к одному клипу.
    /// </summary>
    /// <remarks>
    /// Выделив три куска и нажав «2×», пользователь ждёт, что ускорятся все три.
    /// Основной клип идёт первым: если выделение пусто, правка не делается вовсе.
    /// </remarks>
    public IReadOnlyList<ClipId> TargetClips => _selection.Count > 0
        ? [.. Clips.Where(clip => _selection.Contains(clip.Id)).Select(clip => clip.Id)]
        : SelectedClip is { } single ? [single.Id] : [];

    public void SetClipSpeed(double speed)
    {
        if (TargetClips is { Count: > 0 } clips)
        {
            Execute(new SetClipSpeedCommand(clips, speed));
        }
    }

    public void SetClipAudio(ClipAudio audio)
    {
        if (TargetClips is { Count: > 0 } clips)
        {
            Execute(new SetClipAudioCommand(clips, audio));
        }
    }

    public void SetClipTransform(ClipTransform transform)
    {
        if (TargetClips is { Count: > 0 } clips)
        {
            Execute(new SetClipTransformCommand(clips, transform));
        }
    }

    public void EndInteraction() => _history?.EndMergeGroup();

    private void Execute(IEditCommand command)
    {
        if (_history is null)
        {
            return;
        }

        try
        {
            _history.Execute(command);
        }
        catch (EditOperationException)
        {
            // Недопустимая правка — просто ничего не делаем: интерфейс и так
            // не должен был её предлагать, а падать из-за неё незачем.
        }
    }

    private void ZoomBy(double factor)
    {
        Metrics.ZoomAt(factor, Metrics.ViewportWidth / 2);
        RequestRedraw();
    }

    private void RequestRedraw()
    {
        ThumbnailsRequested?.Invoke(this, EventArgs.Empty);
        VisualInvalidated?.Invoke(this, EventArgs.Empty);
    }
}
