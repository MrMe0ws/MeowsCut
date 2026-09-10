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
    private readonly TimelineHitTester _hitTester = new();
    private readonly List<ClipViewModel> _clipPool = [];
    private readonly HashSet<ClipId> _selection = [];

    private EditHistory? _history;
    private Project? _project;
    private DragState _drag = DragState.None;
    private double _lastPointerX;
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
    }

    /// <summary>
    /// Кладёт звук из файла на аудиодорожку, начиная с плейхеда.
    /// </summary>
    /// <remarks>
    /// Новая дорожка создаётся, только если ни одной ещё нет: иначе каждый
    /// добавленный звук плодил бы полосу, и доска уехала бы за край экрана.
    /// Наложение делается второй дорожкой осознанно — кнопкой «Дорожка».
    /// </remarks>
    public void AppendAudioSource(Project project, MediaSource source)
    {
        _project = project;

        if (_history is null)
        {
            return;
        }

        var clip = AudioClip.FromSource(source, Playhead);

        if (Sequence.AudioTracks.Count == 0)
        {
            var track = AudioTrack.Empty(Localization.Strings.AudioTrackDefaultName) with { Clips = [clip] };
            Execute(new AddAudioTrackCommand(track));
            SelectAudio(track.Id, clip);
        }
        else
        {
            var trackId = Sequence.AudioTracks[^1].Id;
            Execute(new AddAudioClipCommand(trackId, clip));
            SelectAudio(trackId, clip);
        }

        _history.EndMergeGroup();
    }

    /// <summary>Добавляет пустую дорожку — под неё кладут второй слой звука.</summary>
    [RelayCommand(CanExecute = nameof(HasProject))]
    private void AddAudioTrack()
    {
        var title = string.Format(
            System.Globalization.CultureInfo.CurrentUICulture,
            Localization.Strings.AudioTrackNumbered,
            Sequence.AudioTracks.Count + 1);

        Execute(new AddAudioTrackCommand(AudioTrack.Empty(title)));
        _history?.EndMergeGroup();
    }

    /// <summary>Снять звук с выбранного видеоклипа на отдельную дорожку.</summary>
    [RelayCommand(CanExecute = nameof(CanDetachAudio))]
    private void DetachAudio()
    {
        if (SelectedClip is not { } clip)
        {
            return;
        }

        Execute(new DetachClipAudioCommand(clip.Id));
        _history?.EndMergeGroup();
    }

    private bool CanDetachAudio() => SelectedClip is { } clip && clip.Clip.SourceHasAudio;

    [RelayCommand]
    private void RemoveAudioTrack(AudioTrackId trackId)
    {
        Execute(new RemoveAudioTrackCommand(trackId));
        _history?.EndMergeGroup();

        if (SelectedAudioTrack == trackId)
        {
            SelectedAudioClip = null;
        }
    }

    [RelayCommand]
    private void ToggleAudioTrackMuted(AudioTrackId trackId)
    {
        if (Sequence.FindTrack(trackId) is not { } track)
        {
            return;
        }

        Execute(new SetAudioTrackPropertiesCommand(trackId, muted: !track.IsMuted));
        _history?.EndMergeGroup();
    }

    /// <summary>Громкость дорожки целиком — ползунок на её заголовке.</summary>
    public void SetAudioTrackGain(AudioTrackId trackId, double gain)
    {
        if (Sequence.FindTrack(trackId) is not { } track || Math.Abs(track.Gain - gain) < 0.0001)
        {
            return;
        }

        Execute(new SetAudioTrackPropertiesCommand(trackId, gain: gain));
    }

    /// <summary>Свойства выбранного куска звука: громкость, тональность, затухания.</summary>
    public void SetAudioClipProperties(
        double? gain = null,
        int? pitch = null,
        TimeSpan? fadeIn = null,
        TimeSpan? fadeOut = null)
    {
        if (SelectedAudioClip is not { } clip)
        {
            return;
        }

        Execute(new SetAudioClipPropertiesCommand(SelectedAudioTrack, clip.Id, gain, pitch, fadeIn, fadeOut));
        RefreshAudioSelection();
    }

    /// <summary>Завершает серию правок ползунком — дальше история начнёт новую запись.</summary>
    public void EndAudioEdit() => _history?.EndMergeGroup();

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

    public void Detach()
    {
        if (_history is not null)
        {
            _history.Changed -= OnHistoryChanged;
        }

        _history = null;
        _project = null;
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

    public void SetClipSpeed(ClipId clipId, double speed) => Execute(new SetClipSpeedCommand(clipId, speed));

    public void SetClipAudio(ClipId clipId, ClipAudio audio) => Execute(new SetClipAudioCommand(clipId, audio));

    public void SetClipTransform(ClipId clipId, ClipTransform transform) =>
        Execute(new SetClipTransformCommand(clipId, transform));

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

    // ───────────────────────── работа мышью ─────────────────────────

    private enum DragState
    {
        None = 0,
        Playhead,
        MoveClip,
        TrimStart,
        TrimEnd,
        Pan,
        MoveAudio,
        TrimAudioStart,
        TrimAudioEnd
    }

    public void PointerDown(double x, double y, PointerMode mode = PointerMode.Normal)
    {
        if (_history is null)
        {
            return;
        }

        _lastPointerX = x;
        var hit = _hitTester.Test(x, y, Sequence, Metrics);

        // Протяжка доски доступна всегда: средняя кнопка или пробел. Отдельный
        // инструмент ради неё заставлял переключаться туда и обратно.
        if (mode == PointerMode.Pan)
        {
            _drag = DragState.Pan;
            return;
        }

        // Полоса звука разбирается своими правилами, включая ножницы: иначе
        // разрез по звуку уходил бы в ветку видеоряда и молча ничего не делал.
        if (hit.IsAudio)
        {
            HandleAudioPointerDown(hit);
            return;
        }

        if (ActiveTool == TimelineTool.Razor)
        {
            if (hit.Clip is not null)
            {
                Execute(new SplitClipCommand(hit.Time));
                _history?.EndMergeGroup();

                // После разреза выделяем правую половину: обычно режут, чтобы
                // сразу что-то сделать с хвостом — удалить или ускорить.
                SelectClipStartingAt(hit.Time);
            }

            return;
        }

        switch (hit.Kind)
        {
            case TimelineHitKind.Ruler:
            case TimelineHitKind.Empty:
                ClearSelection();
                _drag = DragState.Playhead;
                MovePlayheadTo(hit.Time);
                break;

            case TimelineHitKind.ClipStartEdge:
                Select(hit.Clip, mode);
                _drag = DragState.TrimStart;
                _dragReferenceTime = hit.Clip!.Value.Start;
                break;

            case TimelineHitKind.ClipEndEdge:
                Select(hit.Clip, mode);
                _drag = DragState.TrimEnd;
                _dragReferenceTime = hit.Clip!.Value.End;
                break;

            case TimelineHitKind.ClipBody:
                Select(hit.Clip, mode);
                _drag = DragState.MoveClip;
                break;
        }
    }

    private void HandleAudioPointerDown(TimelineHit hit)
    {
        if (ActiveTool == TimelineTool.Razor)
        {
            if (hit.AudioClip is not null)
            {
                Execute(new SplitAudioClipCommand(hit.TrackId, hit.Time));
                _history?.EndMergeGroup();
            }

            return;
        }

        SelectAudio(hit.TrackId, hit.AudioClip);

        if (hit.AudioClip is null)
        {
            return;
        }

        _drag = hit.Kind switch
        {
            TimelineHitKind.AudioClipStartEdge => DragState.TrimAudioStart,
            TimelineHitKind.AudioClipEndEdge => DragState.TrimAudioEnd,
            _ => DragState.MoveAudio
        };

        _dragReferenceTime = hit.Time - hit.AudioClip.TimelineStart;
    }

    private void SelectAudio(AudioTrackId trackId, AudioClip? clip)
    {
        // Выделения не складываются: выбрав звук, пользователь перестаёт
        // целиться в видеоклип, и наоборот.
        _selection.Clear();
        SelectedClip = null;
        ApplySelectionToClips();

        SelectedAudioTrack = trackId;
        SelectedAudioClip = clip;
        RequestRedraw();
    }

    public void PointerMove(double x, double y)
    {
        if (_history is null || _drag == DragState.None)
        {
            return;
        }

        var deltaPixels = x - _lastPointerX;
        _lastPointerX = x;

        switch (_drag)
        {
            case DragState.Playhead:
                MovePlayheadTo(Metrics.XToTime(x));
                break;

            case DragState.Pan:
                Metrics.Scroll -= Metrics.WidthToDuration(deltaPixels);
                RequestRedraw();
                break;

            case DragState.TrimStart:
            case DragState.TrimEnd:
                DragEdge(x);
                break;

            case DragState.MoveClip:
                DragClip(x);
                break;

            case DragState.MoveAudio:
                DragAudioClip(x);
                break;

            case DragState.TrimAudioStart:
            case DragState.TrimAudioEnd:
                DragAudioEdge(x);
                break;
        }
    }

    /// <summary>Тянут кусок звука: он встаёт туда, где отпустили, с прилипанием.</summary>
    private void DragAudioClip(double x)
    {
        if (SelectedAudioClip is not { } clip)
        {
            return;
        }

        var start = Snap.Snap(Metrics.XToTime(x) - _dragReferenceTime, Sequence, Metrics, Playhead);

        if (start < TimeSpan.Zero)
        {
            start = TimeSpan.Zero;
        }

        if (start == clip.TimelineStart)
        {
            return;
        }

        Execute(new MoveAudioClipCommand(SelectedAudioTrack, clip.Id, start));
        RefreshAudioSelection();
    }

    private void DragAudioEdge(double x)
    {
        if (SelectedAudioClip is not { } clip)
        {
            return;
        }

        var edge = _drag == DragState.TrimAudioStart ? ClipEdge.Start : ClipEdge.End;
        var target = Snap.Snap(Metrics.XToTime(x), Sequence, Metrics, Playhead);

        var current = edge == ClipEdge.Start ? clip.TimelineStart : clip.TimelineEnd;
        var delta = target - current;

        if (Math.Abs(delta.TotalMilliseconds) < 1)
        {
            return;
        }

        Execute(new TrimAudioClipEdgeCommand(SelectedAudioTrack, clip.Id, edge, delta));
        RefreshAudioSelection();
    }

    /// <summary>После правки объект куска другой — выделение надо перечитать из модели.</summary>
    private void RefreshAudioSelection()
    {
        if (SelectedAudioClip is not { } clip)
        {
            return;
        }

        SelectedAudioClip = Sequence.FindTrack(SelectedAudioTrack)?.Find(clip.Id);
    }

    public void PointerUp()
    {
        if (_drag is DragState.TrimStart or DragState.TrimEnd or DragState.MoveClip
            or DragState.MoveAudio or DragState.TrimAudioStart or DragState.TrimAudioEnd)
        {
            _history?.EndMergeGroup();
        }

        _drag = DragState.None;
    }

    /// <summary>Колесо: прокрутка, с Ctrl — масштаб с фиксацией точки под курсором.</summary>
    public void Wheel(double delta, double x, bool zoom)
    {
        if (zoom)
        {
            Metrics.ZoomAt(delta > 0 ? 1.15 : 1 / 1.15, x);
        }
        else
        {
            Metrics.Scroll += Metrics.WidthToDuration(delta > 0 ? -60 : 60);
        }

        RequestRedraw();
    }

    private void DragEdge(double x)
    {
        if (SelectedClip is not { } clip)
        {
            return;
        }

        var edge = _drag == DragState.TrimStart ? ClipEdge.Start : ClipEdge.End;
        var target = Snap.Snap(Metrics.XToTime(x), Sequence, Metrics, Playhead);

        var current = edge == ClipEdge.Start ? clip.Start : clip.End;
        var delta = target - current;

        if (Math.Abs(delta.TotalMilliseconds) < 1)
        {
            return;
        }

        Execute(new TrimClipEdgeCommand(clip.Id, edge, delta));
    }

    private void DragClip(double x)
    {
        if (SelectedClip is not { } clip)
        {
            return;
        }

        var targetIndex = _hitTester.ResolveDropIndex(x, Sequence, Metrics, clip.Id);
        var currentIndex = Sequence.Video.IndexOf(clip.Id);

        if (targetIndex != currentIndex)
        {
            Execute(new MoveClipCommand(clip.Id, targetIndex));
        }
    }

    private void MovePlayheadTo(TimeSpan time)
    {
        var snapped = Snap.Snap(time, Sequence, Metrics);
        Playhead = snapped < TimeSpan.Zero
            ? TimeSpan.Zero
            : snapped > Duration ? Duration : snapped;
    }

    private void Select(PlacedClip? placed, PointerMode mode = PointerMode.Normal)
    {
        var clip = placed is { } value
            ? Clips.FirstOrDefault(item => item.Id == value.Clip.Id)
            : null;

        if (clip is null)
        {
            ClearSelection();
            return;
        }

        switch (mode)
        {
            case PointerMode.Toggle when _selection.Contains(clip.Id):
                _selection.Remove(clip.Id);
                SelectedClip = Clips.FirstOrDefault(item => _selection.Contains(item.Id));
                break;

            case PointerMode.Toggle:
                _selection.Add(clip.Id);
                SelectedClip = clip;
                break;

            case PointerMode.Range when SelectedClip is { } anchor:
                SelectRange(anchor, clip);
                break;

            default:
                _selection.Clear();
                _selection.Add(clip.Id);
                SelectedClip = clip;
                break;
        }

        ApplySelectionToClips();
    }

    /// <summary>Всё между опорным клипом и указанным — выбор кадра из середины ряда.</summary>
    private void SelectRange(ClipViewModel anchor, ClipViewModel clip)
    {
        var from = Clips.IndexOf(anchor);
        var to = Clips.IndexOf(clip);

        if (from < 0 || to < 0)
        {
            return;
        }

        _selection.Clear();

        for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
        {
            _selection.Add(Clips[i].Id);
        }

        SelectedClip = clip;
    }

    private void ClearSelection()
    {
        _selection.Clear();
        SelectedClip = null;
        ApplySelectionToClips();
    }

    private void ApplySelectionToClips()
    {
        foreach (var clip in Clips)
        {
            clip.IsSelected = _selection.Contains(clip.Id);
        }

        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(HasMultipleSelected));
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        DuplicateSelectedCommand.NotifyCanExecuteChanged();
        RequestRedraw();
    }

    /// <summary>Выделяет клип, начинающийся в указанной точке, — половину после разреза.</summary>
    private void SelectClipStartingAt(TimeSpan start)
    {
        var found = Clips.FirstOrDefault(clip => clip.Start == start);

        if (found is not null)
        {
            _selection.Clear();
            _selection.Add(found.Id);
            SelectedClip = found;
            ApplySelectionToClips();
        }
    }

    /// <summary>Просит контрол перерисоваться — например, когда догрузились кадры.</summary>
    public void NotifyVisualInvalidated() => VisualInvalidated?.Invoke(this, EventArgs.Empty);

    /// <summary>Форма курсора зависит от того, что под ним: тело клипа или его край.</summary>
    public TimelineHitKind HitKindAt(double x, double y) =>
        _history is null ? TimelineHitKind.Empty : _hitTester.Test(x, y, Sequence, Metrics).Kind;
}
