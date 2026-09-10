using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeowsCut.App.Timeline;
using MeowsCut.App.ViewModels;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Controls;

/// <summary>
/// Доска монтажа: разметка времени, клипы с кадрами, выделение и плейхед.
/// </summary>
/// <remarks>
/// Рисуется вручную, а не собирается из элементов: при сотне кадров на дорожке
/// дерево визуальных элементов начинает заметно тормозить, а здесь достаточно
/// перерисовать видимый диапазон.
/// </remarks>
public sealed class TimelineControl : Control
{
    private const double RulerHeight = 24d;
    private const double TrackPadding = 10d;
    private const double ClipGap = 1.5d;

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private readonly TimelineLayout _layout = new();

    private TimelineViewModel? _model;
    private Point _lastHoverPosition;
    private bool _dragging;

    public TimelineControl()
    {
        ClipToBounds = true;
        Focusable = true;
        SnapsToDevicePixels = true;
        DataContextChanged += OnDataContextChanged;
    }

    private Brush TrackBackground => (Brush)FindResource("Brush.Background");

    private Brush ClipFill => (Brush)FindResource("Brush.SurfaceRaised");

    private Brush ClipBorder => (Brush)FindResource("Brush.Border");

    private Brush Accent => (Brush)FindResource("Brush.Accent");

    private Brush TextBrush => (Brush)FindResource("Brush.Text");

    private Brush MutedBrush => (Brush)FindResource("Brush.TextMuted");

    private Brush RazorBrush => (Brush)FindResource("Brush.Danger");

    private Brush LaneFill => (Brush)FindResource("Brush.Surface");

    private Brush AudioFill => (Brush)FindResource("Brush.AudioClip");

    private Brush MutedAudioFill => (Brush)FindResource("Brush.SurfaceRaised");

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_model is not null)
        {
            _model.VisualInvalidated -= OnVisualInvalidated;
        }

        _model = e.NewValue as TimelineViewModel;

        if (_model is not null)
        {
            _model.VisualInvalidated += OnVisualInvalidated;

            if (ActualWidth > 0)
            {
                _model.Metrics.ViewportWidth = ActualWidth;
                _model.EnsureInitialFit();
            }
        }

        InvalidateVisual();
    }

    private void OnVisualInvalidated(object? sender, EventArgs e)
    {
        UpdateCursor();

        // Проект мог открыться уже после того, как контрол получил размер:
        // тогда вписать последовательность в окно надо именно здесь.
        if (_model is not null && ActualWidth > 0)
        {
            _model.Metrics.ViewportWidth = ActualWidth;
            _model.EnsureInitialFit();
        }

        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);

        if (_model is not null)
        {
            _model.Metrics.ViewportWidth = ActualWidth;
            _model.ViewportHeight = ActualHeight;
            _model.EnsureInitialFit();
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        context.DrawRectangle(TrackBackground, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_model is null || !_model.HasProject)
        {
            return;
        }

        var metrics = _model.Metrics;

        DrawRuler(context, metrics);

        var tracks = _model.AudioTracks;
        var lanes = _layout.Build(ActualHeight, [.. tracks.Select(track => track.Id)]);
        var video = lanes[0];

        foreach (var clip in _model.Clips)
        {
            DrawClip(context, clip, metrics, video.Top, video.Height);
        }

        for (var i = 0; i < tracks.Count && i + 1 < lanes.Count; i++)
        {
            DrawAudioLane(context, metrics, tracks[i], lanes[i + 1]);
        }

        DrawRazorGuide(context, metrics, video.Top, ActualHeight - video.Top - TrackPadding);
        DrawPlayhead(context, metrics);
    }

    /// <summary>
    /// Полоса звука: подложка, подпись дорожки и её куски.
    /// </summary>
    /// <remarks>
    /// Волна берётся готовой картинкой на весь файл, из которой вырезается кусок
    /// по точкам входа и выхода. Пока она считается, кусок рисуется без неё —
    /// ждать на перерисовке нельзя.
    /// </remarks>
    private void DrawAudioLane(DrawingContext context, TimelineMetrics metrics, AudioTrack track, TimelineLane lane)
    {
        var laneRect = new Rect(0, lane.Top, ActualWidth, lane.Height);
        context.DrawRoundedRectangle(LaneFill, null, laneRect, 4, 4);

        var muted = track.IsMuted;

        foreach (var clip in track.Clips)
        {
            var left = metrics.TimeToX(clip.TimelineStart);
            var right = metrics.TimeToX(clip.TimelineEnd);

            if (right < 0 || left > ActualWidth)
            {
                continue;
            }

            var rect = new Rect(left + ClipGap, lane.Top + 3, Math.Max(2d, right - left - (ClipGap * 2)), lane.Height - 6);
            var selected = _model?.SelectedAudioClip?.Id == clip.Id;

            var pen = new Pen(selected ? Accent : ClipBorder, selected ? 2 : 1);
            context.DrawRoundedRectangle(muted ? MutedAudioFill : AudioFill, pen, rect, 4, 4);

            DrawWaveform(context, clip, rect, muted);
            DrawAudioCaption(context, clip, track, rect);
        }

        // Имя дорожки рисуется последним и на подложке: кусок звука может начинаться
        // с нуля, и без подложки подпись сливалась бы с его названием.
        var title = new FormattedText(
            muted ? track.Title + "  ·  " + Localization.Strings.TrackMuted : track.Title,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10,
            MutedBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var badge = new Rect(4, lane.Top + 2, title.Width + 10, title.Height + 3);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210, 10, 10, 12)), null, badge, 3, 3);
        context.DrawText(title, new Point(9, lane.Top + 3));
    }

    /// <summary>
    /// Волна внутри куска звука.
    /// </summary>
    /// <remarks>
    /// Рисуется под подписью и с отступом сверху: подпись обязана оставаться
    /// читаемой, а волна на всю высоту перечёркивала бы её.
    /// Кисть скругления не знает, поэтому прямоугольник обрезается по форме куска.
    /// </remarks>
    private void DrawWaveform(DrawingContext context, AudioClip clip, Rect rect, bool muted)
    {
        if (rect.Width < 4 || rect.Height < 10 || _model?.WaveformFor(clip) is not { } brush)
        {
            return;
        }

        brush.Opacity = muted ? 0.25 : 0.75;

        var area = new Rect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);

        context.PushClip(new RectangleGeometry(area, 3, 3));
        context.DrawRectangle(brush, null, area);
        context.Pop();
    }

    private void DrawAudioCaption(DrawingContext context, AudioClip clip, AudioTrack track, Rect rect)
    {
        if (rect.Width < 40)
        {
            return;
        }

        var caption = clip.Title.Length > 0 ? clip.Title : Localization.Strings.SectionSound;

        if (clip.IsPitchShifted)
        {
            caption += "  ·  " + string.Format(
                CultureInfo.CurrentUICulture,
                Localization.Strings.PitchSemitones,
                clip.PitchSemitones);
        }

        if (clip.IsGainChanged || Math.Abs(track.Gain - 1d) > 0.0001)
        {
            caption += $"  ·  {clip.Gain * track.Gain * 100:0}%";
        }

        var text = new FormattedText(
            caption,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10,
            TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(10, rect.Width - 10),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        context.DrawText(text, new Point(rect.X + 6, rect.Bottom - text.Height - 4));
    }

    /// <summary>
    /// Показывает, где пройдёт разрез. Без неё ножницами приходится целиться вслепую:
    /// курсор стоит на одном пикселе, а режется кадр, и промах виден только после.
    /// </summary>
    private void DrawRazorGuide(DrawingContext context, TimelineMetrics metrics, double top, double height)
    {
        if (_model is null || _model.ActiveTool != TimelineTool.Razor || !IsMouseOver)
        {
            return;
        }

        var x = _lastHoverPosition.X;
        if (x < 0 || x > ActualWidth || _model.HitKindAt(x, _lastHoverPosition.Y) == TimelineHitKind.Ruler)
        {
            return;
        }

        // Пунктир, чтобы линию реза не путали с плейхедом.
        var pen = new Pen(RazorBrush, 1.5) { DashStyle = new DashStyle([3, 3], 0) };
        context.DrawLine(pen, new Point(x, top - 4), new Point(x, top + height + 4));

        var label = new FormattedText(
            FormatTimeLabel(metrics.XToTime(x), TimeSpan.FromSeconds(0.1)),
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10,
            RazorBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        context.DrawText(label, new Point(x + 4, top - 2));
    }

    private void DrawRuler(DrawingContext context, TimelineMetrics metrics)
    {
        var pen = new Pen(ClipBorder, 1);
        context.DrawLine(pen, new Point(0, RulerHeight), new Point(ActualWidth, RulerHeight));

        var step = metrics.RulerStep();
        if (step <= TimeSpan.Zero)
        {
            return;
        }

        var first = Math.Floor(metrics.Scroll.TotalSeconds / step.TotalSeconds) * step.TotalSeconds;

        for (var seconds = first; ; seconds += step.TotalSeconds)
        {
            var x = metrics.TimeToX(TimeSpan.FromSeconds(seconds));
            if (x > ActualWidth)
            {
                break;
            }

            if (x < -60 || seconds < 0)
            {
                continue;
            }

            context.DrawLine(pen, new Point(x, RulerHeight - 6), new Point(x, RulerHeight));

            var label = FormatTimeLabel(TimeSpan.FromSeconds(seconds), step);
            var text = new FormattedText(
                label,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                10,
                MutedBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            context.DrawText(text, new Point(x + 4, 4));
        }
    }

    private static string FormatTimeLabel(TimeSpan time, TimeSpan step) =>
        step.TotalSeconds < 1
            ? time.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture)
            : time.TotalHours >= 1
                ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private void DrawClip(
        DrawingContext context,
        ClipViewModel clip,
        TimelineMetrics metrics,
        double top,
        double height)
    {
        var left = metrics.TimeToX(clip.Start);
        var right = metrics.TimeToX(clip.End);

        if (right < 0 || left > ActualWidth)
        {
            return;
        }

        // Полупиксельный отступ с каждой стороны: встык нарисованные клипы
        // сливаются в одну ленту, и место разреза на доске не видно.
        var rect = new Rect(left + ClipGap, top, Math.Max(2d, right - left - (ClipGap * 2)), height);
        var radius = 6d;

        var borderPen = new Pen(clip.IsSelected ? Accent : ClipBorder, clip.IsSelected ? 2 : 1);
        context.DrawRoundedRectangle(ClipFill, borderPen, rect, radius, radius);

        // Кадры рисуем внутри клипа, обрезая по его границам.
        if (clip.Thumbnails.Count > 0)
        {
            context.PushClip(new RectangleGeometry(rect, radius, radius));

            foreach (var thumbnail in clip.Thumbnails)
            {
                var thumbRect = new Rect(thumbnail.X, top + 1, thumbnail.Width, height - 2);
                if (thumbRect.Right < rect.Left || thumbRect.Left > rect.Right)
                {
                    continue;
                }

                context.DrawRectangle(thumbnail.Brush, null, thumbRect);
            }

            context.Pop();
        }

        DrawClipCaption(context, clip, rect);
    }

    private void DrawClipCaption(DrawingContext context, ClipViewModel clip, Rect rect)
    {
        if (rect.Width < 46)
        {
            return;
        }

        var caption = clip.SpeedLabel is { } speed ? $"{clip.DurationText}  ·  {speed}" : clip.DurationText;
        if (clip.IsMuted)
        {
            caption += "  ·  без звука";
        }

        var text = new FormattedText(
            caption,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(10, rect.Width - 12),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        // Подложка под подписью: поверх кадров белый текст иначе не читается.
        var background = new Rect(rect.X + 4, rect.Bottom - text.Height - 6, text.Width + 8, text.Height + 4);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(190, 10, 10, 12)), null, background, 4, 4);
        context.DrawText(text, new Point(rect.X + 8, rect.Bottom - text.Height - 4));
    }

    private void DrawPlayhead(DrawingContext context, TimelineMetrics metrics)
    {
        if (_model is null)
        {
            return;
        }

        var x = metrics.TimeToX(_model.Playhead);
        if (x < 0 || x > ActualWidth)
        {
            return;
        }

        var pen = new Pen(Accent, 1.5);
        context.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));

        var head = new StreamGeometry();
        using (var figure = head.Open())
        {
            figure.BeginFigure(new Point(x - 5, 0), true, true);
            figure.LineTo(new Point(x + 5, 0), true, false);
            figure.LineTo(new Point(x, 8), true, false);
        }

        head.Freeze();
        context.DrawGeometry(Accent, null, head);
    }

    // ───────────────────────────── ввод ─────────────────────────────

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (_model is null || e.ChangedButton is not (MouseButton.Left or MouseButton.Middle))
        {
            return;
        }

        Focus();
        var position = e.GetPosition(this);

        _dragging = true;
        _model.PointerDown(position.X, position.Y, ResolveMode(e.ChangedButton));
        CaptureMouse();
        InvalidateVisual();
    }

    /// <summary>
    /// Средняя кнопка тянет доску каким угодно инструментом — она и заменила
    /// отдельную «руку». Ctrl добавляет клип к выделению, Shift берёт ряд.
    /// </summary>
    private static PointerMode ResolveMode(MouseButton button)
    {
        if (button == MouseButton.Middle)
        {
            return PointerMode.Pan;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return PointerMode.Toggle;
        }

        return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? PointerMode.Range : PointerMode.Normal;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_model is null)
        {
            return;
        }

        var position = e.GetPosition(this);

        if (_dragging)
        {
            _model.PointerMove(position.X, position.Y);
            InvalidateVisual();
            return;
        }

        _lastHoverPosition = position;
        UpdateCursor();

        if (_model.ActiveTool == TimelineTool.Razor)
        {
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    /// <summary>
    /// Форма курсора зависит и от инструмента, и от того, что под ним. Инструмент
    /// меняют клавишей — мышь при этом не двигается, и без явного обновления
    /// курсор остался бы от прошлого инструмента до первого движения.
    /// </summary>
    private void UpdateCursor()
    {
        if (_model is null)
        {
            return;
        }

        Cursor = ResolveCursor(_model.HitKindAt(_lastHoverPosition.X, _lastHoverPosition.Y));
    }

    private Cursor ResolveCursor(TimelineHitKind kind)
    {
        if (Mouse.MiddleButton == MouseButtonState.Pressed)
        {
            return Cursors.SizeAll;
        }

        if (_model?.ActiveTool == TimelineTool.Razor)
        {
            return ToolCursors.Razor;
        }

        return kind switch
        {
            TimelineHitKind.ClipStartEdge or TimelineHitKind.ClipEndEdge => Cursors.SizeWE,
            TimelineHitKind.AudioClipStartEdge or TimelineHitKind.AudioClipEndEdge => Cursors.SizeWE,
            TimelineHitKind.ClipBody or TimelineHitKind.AudioClipBody => Cursors.Hand,
            _ => Cursors.Arrow
        };
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle))
        {
            return;
        }

        _dragging = false;
        _model?.PointerUp();
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (_model is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        var zoom = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        _model.Wheel(e.Delta, position.X, zoom);
        e.Handled = true;
    }
}
