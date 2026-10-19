using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeowsCut.App.Views.Panes;

/// <summary>Кадр последовательности под курсором и транспорт.</summary>
/// <remarks>
/// Увеличение кадра — чисто экранное: оно не трогает ни монтаж, ни экспорт,
/// поэтому живёт здесь, а не в модели. Увеличенный кадр двигают мышью,
/// и сдвиг не даёт картинке уйти за край целиком.
/// </remarks>
public partial class PreviewPaneView : UserControl
{
    private Point? _panFrom;
    private Point _panStart;

    public PreviewPaneView() => InitializeComponent();

    private double Zoom => StageScale.ScaleX;

    private void OnZoomChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StageScale is null || ZoomBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        var zoom = double.Parse(tag, CultureInfo.InvariantCulture);
        StageScale.ScaleX = StageScale.ScaleY = zoom;

        // Возврат к «Вписать» сбрасывает и сдвиг: кадр снова целиком по центру.
        StagePan.X = StagePan.Y = 0;
        Stage.Cursor = zoom > 1 ? Cursors.SizeAll : null;

        // В увеличенном просмотре мышь двигает сам просмотр, а не кадр клипа.
        StageView.AllowFrameEditing = zoom <= 1;
    }

    private void OnStageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Zoom <= 1)
        {
            return;
        }

        _panFrom = e.GetPosition(Stage);
        _panStart = new Point(StagePan.X, StagePan.Y);
        Stage.CaptureMouse();
        e.Handled = true;
    }

    private void OnStageMouseMove(object sender, MouseEventArgs e)
    {
        if (_panFrom is not { } from)
        {
            return;
        }

        var position = e.GetPosition(Stage);

        // Дальше половины лишнего размера кадр не уходит: край картинки
        // всегда остаётся в окне, и потерять её невозможно.
        var limitX = StageContent.ActualWidth * (Zoom - 1) / 2;
        var limitY = StageContent.ActualHeight * (Zoom - 1) / 2;

        StagePan.X = Math.Clamp(_panStart.X + position.X - from.X, -limitX, limitX);
        StagePan.Y = Math.Clamp(_panStart.Y + position.Y - from.Y, -limitY, limitY);
    }

    private void OnStageMouseUp(object sender, MouseButtonEventArgs e)
    {
        _panFrom = null;
        Stage.ReleaseMouseCapture();
    }
}
