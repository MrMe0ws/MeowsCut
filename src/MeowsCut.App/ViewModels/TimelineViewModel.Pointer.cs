using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.History;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Доска монтажа: всё, что делается мышью — выделение, перетаскивание, разрез.
/// </summary>
public sealed partial class TimelineViewModel
{
    // ───────────────────────── работа мышью ─────────────────────────

    /// <summary>Сдвиг мыши, ниже которого нажатие считается кликом, а не перетаскиванием.</summary>
    private const double ClickSlopPixels = 3d;

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
        TrimAudioEnd,
        MoveTitle
    }

    public void PointerDown(double x, double y, PointerMode mode = PointerMode.Normal)
    {
        if (_history is null)
        {
            return;
        }

        _lastPointerX = x;
        _pressStartX = x;
        _dragMoved = false;
        _pressedOnSelected = false;

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

        if (hit.IsTitle)
        {
            HandleTitlePointerDown(hit);
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
                // Запоминаем до выделения: после него «был ли выбран» уже не узнать.
                _pressedOnSelected = mode == PointerMode.Normal && IsSelected(hit.Clip);

                Select(hit.Clip, mode);
                _drag = DragState.MoveClip;

                // За какое место клипа схватились: без этого он прыгает под курсор серединой.
                _dragReferenceTime = hit.Time - hit.Clip!.Value.Start;
                break;
        }
    }

    /// <summary>
    /// Нажатие в полосе надписей: выбрать и, если попали в саму надпись, тянуть.
    /// </summary>
    /// <remarks>
    /// Ножницы здесь не работают: резать надпись пополам бессмысленно — её
    /// длительность правят числом, а текст всё равно останется тем же.
    /// </remarks>
    private void HandleTitlePointerDown(TimelineHit hit)
    {
        SelectTitle(hit.Title);

        if (hit.Title is not { } title)
        {
            return;
        }

        _drag = DragState.MoveTitle;

        // За какое место схватились: без этого надпись прыгает под курсор началом.
        _dragReferenceTime = hit.Time - title.TimelineStart;
    }

    /// <summary>Выделение надписи снимает выделение клипа и звука: свойства показываются одни.</summary>
    private void SelectTitle(TitleClip? title)
    {
        if (title is not null)
        {
            ClearSelection();
            SelectedAudioClip = null;
        }

        SelectedTitle = title;
        RequestRedraw();
    }

    /// <summary>Тянут надпись: она встаёт туда, где отпустили, с прилипанием.</summary>
    private void DragTitle(double x)
    {
        if (SelectedTitle is not { } title)
        {
            return;
        }

        var start = Snap.Snap(Metrics.XToTime(x) - _dragReferenceTime, Sequence, Metrics, Playhead);

        if (start < TimeSpan.Zero)
        {
            start = TimeSpan.Zero;
        }

        if (start == title.TimelineStart)
        {
            return;
        }

        Execute(new MoveTitleCommand(title.Id, start));
        RefreshTitleSelection();
    }

    /// <summary>
    /// Подхватывает надпись заново после правки.
    /// </summary>
    /// <remarks>
    /// Последовательность иммутабельна: команда создаёт новый список, и старый
    /// объект в выделении устаревает сразу же. Без этого следующее движение мыши
    /// считало бы сдвиг от исходного положения, и надпись дёргалась бы назад.
    /// </remarks>
    private void RefreshTitleSelection()
    {
        if (SelectedTitle is { } title)
        {
            SelectedTitle = Sequence.FindTitle(title.Id);
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

        _pressedOnSelected = hit.AudioClip is { } pressed && SelectedAudioClip?.Id == pressed.Id;

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

        // Дрожание руки не должно считаться перетаскиванием: иначе повторный клик
        // по выбранному куску всегда выглядел бы как микросдвиг и не снимал выделение.
        if (Math.Abs(x - _pressStartX) > ClickSlopPixels)
        {
            _dragMoved = true;
        }

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

            case DragState.MoveTitle:
                DragTitle(x);
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

        // Клик по уже выбранному куску снимает выделение. Решается на отпускании,
        // а не на нажатии: иначе выбранный кусок нельзя было бы утащить мышью —
        // он терял бы выделение прямо в начале перетаскивания.
        if (_pressedOnSelected && !_dragMoved)
        {
            if (SelectedAudioClip is not null)
            {
                SelectedAudioClip = null;
                RequestRedraw();
            }
            else
            {
                ClearSelection();
            }
        }

        _pressedOnSelected = false;
        _dragMoved = false;
        _drag = DragState.None;
    }

    private bool IsSelected(PlacedClip? placed) =>
        placed is { } value && _selection.Count == 1 && _selection.Contains(value.Clip.Id);

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

    /// <summary>
    /// Перетаскивание клипа по ленте.
    /// </summary>
    /// <remarks>
    /// Обычный сдвиг меняет место клипа во времени: между кусками появляется зазор,
    /// в котором при экспорте будет чёрный кадр. Порядок меняется только тогда, когда
    /// клип серединой переваливает за середину соседа — так делают все монтажки,
    /// и случайной перестановки при мелком сдвиге не происходит.
    /// </remarks>
    private void DragClip(double x)
    {
        if (SelectedClip is not { } clip)
        {
            return;
        }

        var index = Sequence.Video.IndexOf(clip.Id);
        if (index < 0)
        {
            return;
        }

        var duration = clip.Clip.TimelineDuration;

        // Отрицательное начало здесь не обрезается: за нулём остаётся единственный
        // способ поставить клип перед первым, а дорожка всё равно прижмёт его к месту.
        var desired = Snap.Snap(Metrics.XToTime(x) - _dragReferenceTime, Sequence, Metrics, Playhead);

        var middle = desired + TimeSpan.FromTicks(duration.Ticks / 2);

        if (index > 0 && middle < Sequence.Video.MidpointOf(index - 1))
        {
            Execute(new MoveClipCommand(clip.Id, index - 1));
            return;
        }

        if (index + 1 < Sequence.Video.Count && middle > Sequence.Video.MidpointOf(index + 1))
        {
            Execute(new MoveClipCommand(clip.Id, index + 1));
            return;
        }

        if (desired != clip.Start)
        {
            Execute(new MoveClipInTimeCommand(clip.Id, desired));
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

        // Выделения не складываются в обе стороны: выбрав видеоклип, пользователь
        // перестал целиться в звук, и панель свойств обязана показать клип.
        SelectedAudioClip = null;

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
        SelectedAudioClip = null;
        SelectedTitle = null;
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
