using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MeowsCut.App.Controls;

/// <summary>
/// Курсор времени — отдельный слой поверх доски.
/// </summary>
/// <remarks>
/// При воспроизведении курсор двигается 30 раз в секунду. Пока он рисовался
/// вместе с доской, каждый его шаг перерисовывал все кадры, волны и подписи,
/// и поток отрисовки, на котором выводится и видео, не успевал: просмотр шёл
/// рывками по 4–5 кадров в секунду. Теперь курсор нарисован один раз и только
/// сдвигается — содержимое доски остаётся готовым.
/// </remarks>
public sealed partial class TimelineControl
{
    private readonly DrawingVisual _playhead = new();
    private readonly TranslateTransform _playheadShift = new();

    private Brush? _playheadBrush;
    private double _playheadHeight = -1;

    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;

    protected override Visual GetVisualChild(int index) =>
        index < base.VisualChildrenCount ? base.GetVisualChild(index) : _playhead;

    /// <summary>
    /// Шаблона у доски нет, а базовый <see cref="Control"/> принял бы слой курсора
    /// за корень шаблона и попытался измерить его как элемент разметки.
    /// </summary>
    protected override Size MeasureOverride(Size constraint) => default;

    protected override Size ArrangeOverride(Size arrangeBounds) => arrangeBounds;

    private void OnPlayheadMoved(object? sender, EventArgs e) => PositionPlayhead();

    private void PositionPlayhead()
    {
        if (_model is null || !_model.HasProject)
        {
            _playhead.Opacity = 0;
            return;
        }

        DrawPlayheadOnce();

        var x = _model.Metrics.TimeToX(_model.Playhead);
        var visible = x >= 0 && x <= ActualWidth;

        _playhead.Opacity = visible ? 1 : 0;
        _playheadShift.X = x;
    }

    /// <summary>Линия и треугольник на нулевой отметке. Перерисовываются только со сменой темы или высоты.</summary>
    private void DrawPlayheadOnce()
    {
        var brush = Accent;

        if (ReferenceEquals(brush, _playheadBrush) && _playheadHeight == ActualHeight)
        {
            return;
        }

        _playheadBrush = brush;
        _playheadHeight = ActualHeight;
        _playhead.Transform = _playheadShift;

        var head = new StreamGeometry();
        using (var figure = head.Open())
        {
            figure.BeginFigure(new Point(-5, 0), true, true);
            figure.LineTo(new Point(5, 0), true, false);
            figure.LineTo(new Point(0, 8), true, false);
        }

        head.Freeze();

        using var context = _playhead.RenderOpen();
        context.DrawLine(new Pen(brush, 1.5), new Point(0, 0), new Point(0, ActualHeight));
        context.DrawGeometry(brush, null, head);
    }
}
