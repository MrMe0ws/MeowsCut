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
public sealed partial class TimelineViewModel : ObservableObject
{
    private readonly TimelineHitTester _hitTester = new();
    private readonly List<ClipViewModel> _clipPool = [];

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

    public TimelineMetrics Metrics { get; } = new();

    public SnapEngine Snap { get; } = new();

    public ObservableCollection<ClipViewModel> Clips { get; } = [];

    public Sequence Sequence => _history?.Current ?? Sequence.Empty;

    public bool HasProject => _history is not null;

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
        SelectedClip = null;
        _needsInitialFit = true;

        Metrics.ZoomToFit(project.Sequence.Duration);
        RebuildClips();
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

    public void Detach()
    {
        if (_history is not null)
        {
            _history.Changed -= OnHistoryChanged;
        }

        _history = null;
        _project = null;
        Clips.Clear();
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

        SelectedClip = selectedId is { } id ? Clips.FirstOrDefault(clip => clip.Id == id) : null;

        foreach (var clip in Clips)
        {
            clip.IsSelected = clip.Id == SelectedClip?.Id;
        }

        if (Playhead > Duration)
        {
            Playhead = Duration;
        }

        ThumbnailsRequested?.Invoke(this, EventArgs.Empty);
        VisualInvalidated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedClipChanged(ClipViewModel? value)
    {
        foreach (var clip in Clips)
        {
            clip.IsSelected = clip.Id == value?.Id;
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
        if (SelectedClip is { } clip)
        {
            Execute(new RemoveClipCommand(clip.Id));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateSelected()
    {
        if (SelectedClip is { } clip)
        {
            Execute(new DuplicateClipCommand(clip.Id));
        }
    }

    private bool HasSelection() => SelectedClip is not null;

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

    public void SetClipSpeed(ClipId clipId, double speed) => Execute(new SetClipSpeedCommand(clipId, speed));

    public void SetClipAudio(ClipId clipId, ClipAudio audio) => Execute(new SetClipAudioCommand(clipId, audio));

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
        Pan
    }

    public void PointerDown(double x, double y)
    {
        if (_history is null)
        {
            return;
        }

        _lastPointerX = x;
        var hit = _hitTester.Test(x, y, Sequence, Metrics);

        if (ActiveTool == TimelineTool.Hand)
        {
            _drag = DragState.Pan;
            return;
        }

        if (ActiveTool == TimelineTool.Razor)
        {
            if (hit.Clip is { } target)
            {
                Execute(new SplitClipCommand(hit.Time));
                _history?.EndMergeGroup();
            }

            return;
        }

        switch (hit.Kind)
        {
            case TimelineHitKind.Ruler:
            case TimelineHitKind.Empty:
                _drag = DragState.Playhead;
                MovePlayheadTo(hit.Time);
                break;

            case TimelineHitKind.ClipStartEdge:
                Select(hit.Clip);
                _drag = DragState.TrimStart;
                _dragReferenceTime = hit.Clip!.Value.Start;
                break;

            case TimelineHitKind.ClipEndEdge:
                Select(hit.Clip);
                _drag = DragState.TrimEnd;
                _dragReferenceTime = hit.Clip!.Value.End;
                break;

            case TimelineHitKind.ClipBody:
                Select(hit.Clip);
                _drag = DragState.MoveClip;
                break;
        }
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
        }
    }

    public void PointerUp()
    {
        if (_drag is DragState.TrimStart or DragState.TrimEnd or DragState.MoveClip)
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

    private void Select(PlacedClip? placed)
    {
        SelectedClip = placed is { } value
            ? Clips.FirstOrDefault(clip => clip.Id == value.Clip.Id)
            : null;
    }

    /// <summary>Просит контрол перерисоваться — например, когда догрузились кадры.</summary>
    public void NotifyVisualInvalidated() => VisualInvalidated?.Invoke(this, EventArgs.Empty);

    /// <summary>Форма курсора зависит от того, что под ним: тело клипа или его край.</summary>
    public TimelineHitKind HitKindAt(double x, double y) =>
        _history is null ? TimelineHitKind.Empty : _hitTester.Test(x, y, Sequence, Metrics).Kind;
}
