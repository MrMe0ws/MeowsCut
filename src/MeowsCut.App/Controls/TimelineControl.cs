using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeowsCut.App.Timeline;
using MeowsCut.App.ViewModels;

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

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private TimelineViewModel? _model;

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

        var trackTop = RulerHeight + TrackPadding;
        var trackHeight = Math.Max(40d, ActualHeight - trackTop - TrackPadding);

        foreach (var clip in _model.Clips)
        {
            DrawClip(context, clip, metrics, trackTop, trackHeight);
        }

        DrawPlayhead(context, metrics);
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

        var rect = new Rect(left, top, Math.Max(2d, right - left), height);
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

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (_model is null)
        {
            return;
        }

        Focus();
        var position = e.GetPosition(this);
        _model.PointerDown(position.X, position.Y);
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_model is null)
        {
            return;
        }

        var position = e.GetPosition(this);

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _model.PointerMove(position.X, position.Y);
            InvalidateVisual();
            return;
        }

        Cursor = ResolveCursor(_model.HitKindAt(position.X, position.Y));
    }

    private Cursor ResolveCursor(TimelineHitKind kind)
    {
        if (_model?.ActiveTool == TimelineTool.Hand)
        {
            return Cursors.SizeAll;
        }

        if (_model?.ActiveTool == TimelineTool.Razor)
        {
            return Cursors.Cross;
        }

        return kind switch
        {
            TimelineHitKind.ClipStartEdge or TimelineHitKind.ClipEndEdge => Cursors.SizeWE,
            TimelineHitKind.ClipBody => Cursors.Hand,
            _ => Cursors.Arrow
        };
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

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
