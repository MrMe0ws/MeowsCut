using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MeowsCut.App.Formatting;
using MeowsCut.App.ViewModels;

namespace MeowsCut.App.Views.Panes;

/// <summary>Кадр последовательности под курсором: картинка, надписи, затемнение.</summary>
/// <remarks>
/// Поверх кадра — рамка выделенного клипа. Тянуть её — сдвиг кадра, колесо —
/// масштаб. Правки идут через ту же модель свойств, что и вкладка «Кадр»,
/// поэтому ползунки справа двигаются следом, а Ctrl+Z отменяет их одним шагом.
/// </remarks>
public partial class PreviewStage : UserControl
{
    private const double WheelFactor = 1.05;

    private readonly DispatcherTimer _wheelSettle = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private Point? _dragFrom;
    private Point _offsetAtStart;

    public PreviewStage()
    {
        InitializeComponent();
        _wheelSettle.Tick += (_, _) =>
        {
            _wheelSettle.Stop();
            EndEdit();
        };
    }

    /// <summary>Правка кадра мышью разрешена: при увеличенном просмотре мышь двигает сам просмотр.</summary>
    public bool AllowFrameEditing { get; set; } = true;

    private InspectorViewModel? Inspector => (Window.GetWindow(this)?.DataContext as ShellViewModel)?.Inspector;

    private void OnFrameMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!AllowFrameEditing || Inspector is not { HasSelection: true } inspector)
        {
            return;
        }

        _dragFrom = e.GetPosition(SelectionFrame);
        _offsetAtStart = new Point(inspector.OffsetXPercent, inspector.OffsetYPercent);
        SelectionFrame.CaptureMouse();
        e.Handled = true;
    }

    private void OnFrameMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragFrom is not { } from || Inspector is not { } inspector || SelectionFrame.ActualWidth <= 0)
        {
            return;
        }

        // Сдвиг в долях кадра с обратным знаком: так его применяет FramingHost,
        // и картинка едет ровно за мышью.
        var position = e.GetPosition(SelectionFrame);
        var dx = (position.X - from.X) / SelectionFrame.ActualWidth * 100;
        var dy = (position.Y - from.Y) / SelectionFrame.ActualHeight * 100;

        inspector.OffsetXPercent = Math.Round(Math.Clamp(_offsetAtStart.X - dx, inspector.MinOffsetPercent, inspector.OffsetLimitPercent));
        inspector.OffsetYPercent = Math.Round(Math.Clamp(_offsetAtStart.Y - dy, inspector.MinOffsetPercent, inspector.OffsetLimitPercent));
    }

    private void OnFrameMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragFrom is null)
        {
            return;
        }

        _dragFrom = null;
        SelectionFrame.ReleaseMouseCapture();
        EndEdit();
    }

    private void OnFrameWheel(object sender, MouseWheelEventArgs e)
    {
        if (!AllowFrameEditing || Inspector is not { HasSelection: true } inspector || e.Delta == 0)
        {
            return;
        }

        e.Handled = true;

        var factor = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? WheelFactor * WheelFactor * WheelFactor : WheelFactor;
        var zoom = e.Delta > 0 ? inspector.ZoomPercent * factor : inspector.ZoomPercent / factor;

        inspector.ZoomPercent = Math.Round(Math.Clamp(zoom, ZoomScale.MinPercent, ZoomScale.MaxPercent));

        _wheelSettle.Stop();
        _wheelSettle.Start();
    }

    private void EndEdit()
    {
        if (Inspector?.EndInteractionCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
