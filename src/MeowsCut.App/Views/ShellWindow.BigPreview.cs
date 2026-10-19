using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeowsCut.App.Views;

/// <summary>
/// Крупный просмотр: карточка кадра растягивается поверх панелей и доски.
/// </summary>
/// <remarks>
/// Не отдельное окно: проигрыватель в приложении один, и переносить его между
/// окнами значило бы перезапускать воспроизведение. Карточка просто занимает
/// всё рабочее место, а остальное остаётся под ней нетронутым — выход (Esc, F11)
/// возвращает раскладку ровно такой, какой она была.
/// </remarks>
public partial class ShellWindow
{
    public static readonly DependencyProperty IsBigPreviewProperty = DependencyProperty.Register(
        nameof(IsBigPreview), typeof(bool), typeof(ShellWindow), new PropertyMetadata(false, OnBigPreviewChanged));

    public bool IsBigPreview
    {
        get => (bool)GetValue(IsBigPreviewProperty);
        set => SetValue(IsBigPreviewProperty, value);
    }

    private static void OnBigPreviewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var window = (ShellWindow)d;
        var big = e.NewValue is true;

        Grid.SetColumn(window.PreviewCard, big ? 0 : 2);
        Grid.SetColumnSpan(window.PreviewCard, big ? 5 : 1);
        Panel.SetZIndex(window.PreviewCard, big ? 1 : 0);

        Grid.SetRowSpan(window.TopArea, big ? 3 : 1);
        Panel.SetZIndex(window.TopArea, big ? 1 : 0);
        window.TopArea.Margin = big ? new Thickness(0, 0, 0, 6) : default;
    }

    /// <summary>F11 — крупный просмотр, Esc — выход из него.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            IsBigPreview = !IsBigPreview;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && IsBigPreview)
        {
            IsBigPreview = false;
            e.Handled = true;
        }
    }
}
