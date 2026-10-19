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
public sealed partial class TimelineControl : Control
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
        AddVisualChild(_playhead);
    }

    /// <summary>Доска лежит на своей карточке-панели, полосы — утопленные дорожки на ней.</summary>
    private Brush TrackBackground => (Brush)FindResource("Brush.Surface");

    private Brush ClipFill => (Brush)FindResource("Brush.SurfaceRaised");

    private Brush ClipBorder => (Brush)FindResource("Brush.Border");

    private Brush Accent => (Brush)FindResource("Brush.Accent");

    private Brush TextBrush => (Brush)FindResource("Brush.Text");

    private Brush MutedBrush => (Brush)FindResource("Brush.TextMuted");

    private Brush RazorBrush => (Brush)FindResource("Brush.Danger");

    private Brush LaneFill => (Brush)FindResource("Brush.Background");

    private Brush TitleFill => (Brush)FindResource("Brush.TitleClip");

    private Brush OnTitleBrush => (Brush)FindResource("Brush.OnTitleClip");

    /// <summary>Подложка подписей, лежащих поверх кадров.</summary>
    private Brush ScrimBrush => (Brush)FindResource("Brush.Scrim");

    /// <summary>Сторона значка на куске. Меньше — перестают читаться фигуры.</summary>
    private const double BadgeSize = 12d;

    private const double BadgeGap = 4d;
    private const double BadgePadding = 3d;

    /// <summary>
    /// Черта затухания. Всегда светлая: под ней чернота самого затухания,
    /// одинаковая в обеих темах.
    /// </summary>
    private static readonly Pen FadeRampPen = CreateFrozenPen(
        new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
        1.4);

    private Pen? _badgeIconPen;
    private Brush? _badgeIconBrush;

    /// <summary>
    /// Перо значков. Цвет берётся у темы: в светлой подложка почти белая,
    /// и зашитые белые значки на ней пропадали бы целиком.
    /// </summary>
    /// <remarks>
    /// Перо переживает перерисовки и меняется только со сменой темы: отрисовка
    /// идёт на каждое движение мыши, а кусков на доске бывают десятки.
    /// </remarks>
    private Pen BadgeIconPen
    {
        get
        {
            var brush = TextBrush;

            if (_badgeIconPen is null || !ReferenceEquals(_badgeIconBrush, brush))
            {
                _badgeIconBrush = brush;
                _badgeIconPen = CreateFrozenPen(brush, 2.4);
            }

            return _badgeIconPen;
        }
    }

    private static Pen CreateFrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        // Кисть темы может быть незамороженной, и тогда перо заморозить нельзя:
        // пытаться всё равно — значит получить исключение на ровном месте.
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }

    /// <summary>
    /// Тьма от края внутрь. Непрозрачна у самого края и сходит на нет там,
    /// где затухание заканчивается, — как и в готовом файле.
    /// </summary>
    private static Brush FadeBrush(bool fromLeft)
    {
        var brush = new LinearGradientBrush(
            Color.FromArgb(235, 0, 0, 0),
            Color.FromArgb(0, 0, 0, 0),
            fromLeft ? 0d : 180d);

        brush.Freeze();
        return brush;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_model is not null)
        {
            _model.VisualInvalidated -= OnVisualInvalidated;
            _model.PlayheadMoved -= OnPlayheadMoved;
        }

        _model = e.NewValue as TimelineViewModel;

        if (_model is not null)
        {
            _model.VisualInvalidated += OnVisualInvalidated;
            _model.PlayheadMoved += OnPlayheadMoved;

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
            PositionPlayhead();
            return;
        }

        var metrics = _model.Metrics;

        DrawRuler(context, metrics);

        var tracks = _model.AudioTracks;
        var titles = _model.Sequence.Titles;

        var lanes = _layout.Build(
            ActualHeight,
            [.. tracks.Select(track => track.Id)],
            titles.Count > 0);

        var video = lanes[0];

        foreach (var clip in _model.Clips)
        {
            DrawClip(context, clip, metrics, video.Top, video.Height);
        }

        for (var i = 0; i < tracks.Count && i + 1 < lanes.Count; i++)
        {
            DrawAudioLane(context, metrics, tracks[i], lanes[i + 1]);
        }

        foreach (var lane in lanes)
        {
            if (lane.Kind == LaneKind.Title)
            {
                DrawTitleLane(context, metrics, titles, lane);
            }
        }

        DrawRazorGuide(context, metrics, video.Top, ActualHeight - video.Top - TrackPadding);
        PositionPlayhead();
    }

    /// <summary>
    /// Показывает, где пройдёт разрез. Без неё ножницами приходится целиться вслепую:
    /// курсор стоит на одном пикселе, а режется кадр, и промах виден только после.
    /// </summary>
    /// <summary>
    /// Полоса надписей: узкие плашки с текстом над видеорядом.
    /// </summary>
    /// <remarks>
    /// Надпись не занимает места на ленте и лежит поверх картинки, поэтому
    /// и полоса у неё своя, над всем остальным. Без неё увидеть, где по времени
    /// стоят титры, можно было бы только листая их по одному в свойствах.
    /// </remarks>
    private void DrawTitleLane(
        DrawingContext context,
        TimelineMetrics metrics,
        IReadOnlyList<TitleClip> titles,
        TimelineLane lane)
    {
        context.DrawRoundedRectangle(
            LaneFill,
            null,
            new Rect(0, lane.Top, ActualWidth, lane.Height),
            4,
            4);

        var selected = _model?.SelectedTitle?.Id;

        foreach (var title in titles)
        {
            var left = metrics.TimeToX(title.TimelineStart);
            var right = metrics.TimeToX(title.TimelineEnd);

            if (right < 0 || left > ActualWidth)
            {
                continue;
            }

            var rect = new Rect(left, lane.Top + 2, Math.Max(3, right - left), lane.Height - 4);
            var isSelected = selected == title.Id;

            // Надпись тёплая, как буква «Т» у её полосы; выбранная — в акцентной рамке.
            context.DrawRoundedRectangle(
                TitleFill,
                isSelected ? new Pen(Accent, 2) : null,
                rect,
                5,
                5);

            if (rect.Width < 34)
            {
                continue;
            }

            var text = new FormattedText(
                title.Text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                10,
                OnTitleBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(10, rect.Width - 10),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };

            context.DrawText(text, new Point(rect.X + 5, rect.Y + (rect.Height - text.Height) / 2));
        }
    }

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

        DrawFades(context, metrics, rect, clip.Clip.EffectiveFadeIn, clip.Clip.EffectiveFadeOut);
        DrawClipCaption(context, clip, rect);
        DrawBadges(context, rect, ClipBadges.For(clip.Clip));
    }

    /// <summary>
    /// Затухания — прямо на кадрах: тьма от края внутрь ровно на их длину.
    /// </summary>
    /// <remarks>
    /// Рисуется то, что эффект и делает, поэтому читается без легенды и говорит
    /// не только «затухание есть», но и «вот такой длины». Значком этого
    /// не передать: он сказал бы только «есть», а выставленные пять секунд
    /// от выставленной половины не отличить.
    ///
    /// Поверх градиента идёт наклонная черта — та же рампа, что в больших
    /// монтажках. Без неё тёмный край не отличить от просто тёмного кадра.
    /// </remarks>
    private void DrawFades(
        DrawingContext context,
        TimelineMetrics metrics,
        Rect rect,
        TimeSpan fadeIn,
        TimeSpan fadeOut)
    {
        if (fadeIn <= TimeSpan.Zero && fadeOut <= TimeSpan.Zero)
        {
            return;
        }

        // Обрезаем по форме куска: градиент о скруглённых углах не знает.
        context.PushClip(new RectangleGeometry(rect, 6, 6));

        if (fadeIn > TimeSpan.Zero)
        {
            var width = Math.Min(metrics.DurationToWidth(fadeIn), rect.Width);
            var area = new Rect(rect.X, rect.Y, width, rect.Height);

            context.DrawRectangle(FadeBrush(fromLeft: true), null, area);
            context.DrawLine(
                FadeRampPen,
                new Point(area.Left, area.Bottom - 2),
                new Point(area.Right, area.Top + 2));
        }

        if (fadeOut > TimeSpan.Zero)
        {
            var width = Math.Min(metrics.DurationToWidth(fadeOut), rect.Width);
            var area = new Rect(rect.Right - width, rect.Y, width, rect.Height);

            context.DrawRectangle(FadeBrush(fromLeft: false), null, area);
            context.DrawLine(
                FadeRampPen,
                new Point(area.Left, area.Top + 2),
                new Point(area.Right, area.Bottom - 2));
        }

        context.Pop();
    }

    /// <summary>
    /// Ряд значков: что применено к куску сверх умолчания.
    /// </summary>
    /// <remarks>
    /// В правом верхнем углу — единственном, свободном и у видео, и у звука:
    /// внизу слева стоит подпись, а вверху слева у полосы звука лежит имя
    /// дорожки, и значки первого куска прятались бы под ним.
    ///
    /// На узком куске значки не рисуются вовсе: втиснутые в двадцать пикселей,
    /// они превращаются в грязь и мешают видеть сам кадр.
    /// </remarks>
    private void DrawBadges(DrawingContext context, Rect rect, IReadOnlyList<ClipBadge> badges)
    {
        if (badges.Count == 0)
        {
            return;
        }

        var pillWidth = (badges.Count * BadgeSize) + ((badges.Count - 1) * BadgeGap) + (BadgePadding * 2);

        if (rect.Width < pillWidth + 10 || rect.Height < BadgeSize + 10)
        {
            return;
        }

        var pill = new Rect(
            rect.Right - pillWidth - 4,
            rect.Y + 4,
            pillWidth,
            BadgeSize + (BadgePadding * 2));

        context.DrawRoundedRectangle(ScrimBrush, null, pill, 4, 4);

        var x = pill.X + BadgePadding;

        foreach (var badge in badges)
        {
            if (IconOf(badge) is { } geometry)
            {
                DrawIcon(context, geometry, x, pill.Y + BadgePadding);
            }

            x += BadgeSize + BadgeGap;
        }
    }

    /// <summary>
    /// Рисует значок, нарисованный для кнопок, в размер значка на куске.
    /// </summary>
    /// <remarks>
    /// Все фигуры нарисованы в квадрате 24×24, поэтому масштабируются одним
    /// коэффициентом. Толщина пера задаётся в тех же единицах и уменьшается
    /// вместе с ним — отсюда число заметно больше привычного.
    /// </remarks>
    private void DrawIcon(DrawingContext context, Geometry geometry, double x, double y)
    {
        const double scale = BadgeSize / 24d;

        context.PushTransform(new MatrixTransform(scale, 0, 0, scale, x, y));
        context.DrawGeometry(null, BadgeIconPen, geometry);
        context.Pop();
    }

    private Geometry? IconOf(ClipBadge badge)
    {
        var key = badge switch
        {
            ClipBadge.Speed => "Icon.Speed",
            ClipBadge.Muted => "Icon.VolumeOff",
            ClipBadge.Volume => "Icon.Volume",
            ClipBadge.Pitch => "Icon.Note",
            ClipBadge.Rotation => "Icon.RotateRight",
            ClipBadge.Framing => "Icon.Frame",
            _ => null
        };

        return key is null ? null : TryFindResource(key) as Geometry;
    }

    private void DrawClipCaption(DrawingContext context, ClipViewModel clip, Rect rect)
    {
        if (rect.Width < 46)
        {
            return;
        }

        // Только длительность: скорость и выключенный звук теперь показаны
        // значками, а точные числа всё равно живут в свойствах клипа.
        var text = new FormattedText(
            clip.DurationText,
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
        context.DrawRoundedRectangle(ScrimBrush, null, background, 4, 4);
        context.DrawText(text, new Point(rect.X + 8, rect.Bottom - text.Height - 4));
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
