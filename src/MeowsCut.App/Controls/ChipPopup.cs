using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MeowsCut.App.Controls;

/// <summary>
/// Всплывающая карточка параметра над куском на доске.
/// </summary>
/// <remarks>
/// Встаёт по центру над куском, а если сверху не хватает места — под ним.
/// Закрывается щелчком мимо, как меню, и клавишей Esc: карточка — быстрая
/// правка, а не окно, которое нужно закрывать отдельно.
/// </remarks>
public sealed class ChipPopup : Popup
{
    public ChipPopup()
    {
        AllowsTransparency = true;
        StaysOpen = false;
        Placement = PlacementMode.Custom;
        PopupAnimation = PopupAnimation.Fade;
        CustomPopupPlacementCallback = PlaceAboveCentered;
    }

    /// <summary>Открыть над участком доски (координаты доски).</summary>
    public void ShowOver(Rect anchor)
    {
        IsOpen = false;
        PlacementRectangle = anchor;
        IsOpen = true;

        // Фокус в карточку, чтобы Esc и колесо мыши работали сразу.
        Dispatcher.BeginInvoke(() => Child?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.Escape)
        {
            IsOpen = false;
            e.Handled = true;
        }
    }

    private static CustomPopupPlacement[] PlaceAboveCentered(Size popup, Size target, Point offset)
    {
        var x = (target.Width - popup.Width) / 2;

        return
        [
            new CustomPopupPlacement(new Point(x, -popup.Height + 6), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(x, target.Height - 6), PopupPrimaryAxis.Horizontal)
        ];
    }
}
