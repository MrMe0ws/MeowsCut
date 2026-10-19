using System.Windows;
using System.Windows.Controls;
using MeowsCut.App.Localization;

namespace MeowsCut.App.Views;

/// <summary>
/// Строка меню на месте заголовка окна. Кнопки окна управляют самим окном,
/// поэтому их обработчики здесь, а не в модели.
/// </summary>
public partial class ShellTitleBar : UserControl
{
    public ShellTitleBar() => InitializeComponent();

    /// <summary>Развёрнутое окно показывает «восстановить» вместо «развернуть».</summary>
    public void ShowMaximized(bool maximized)
    {
        MaximizeButton.Tag = FindResource(maximized ? "Icon.WindowRestore" : "Icon.WindowMaximize");
        MaximizeButton.ToolTip = maximized ? Strings.WindowRestore : Strings.WindowMaximize;
    }

    private Window? Host => Window.GetWindow(this);

    /// <summary>Пункт «Выход» и крестик в строке меню делают одно и то же.</summary>
    private void OnExit(object sender, RoutedEventArgs e) => Host?.Close();

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        if (Host is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeOrRestore(object sender, RoutedEventArgs e)
    {
        if (Host is { } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    private void OnBigPreview(object sender, RoutedEventArgs e)
    {
        if (Host is ShellWindow shell)
        {
            shell.IsBigPreview = !shell.IsBigPreview;
        }
    }
}
