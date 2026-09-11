using System.Windows;
using MeowsCut.App.Services;
using MeowsCut.App.Localization;
using MeowsCut.App.ViewModels;

namespace MeowsCut.App.Views;

/// <summary>
/// Главное окно. Кода здесь ровно столько, сколько требует WPF для drag &amp; drop:
/// вся логика — во ViewModel.
/// </summary>
public partial class ShellWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public ShellWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var files = DragDropFileValidator.ExtractFiles(e.Data);
        e.Effects = files.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Пункт «Выход» и крестик в строке меню делают одно и то же.</summary>
    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeOrRestore(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>
    /// Разворачивая окно, Windows уводит его края за экран на толщину рамки: у окна
    /// со своим заголовком это срезает кнопки. Отступ возвращает их на место, а заодно
    /// кнопка меняет значок на «восстановить».
    /// </summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;

        BorderThickness = maximized
            ? new Thickness(SystemParameters.WindowResizeBorderThickness.Left + SystemParameters.BorderWidth)
            : default;

        MaximizeButton.Tag = FindResource(maximized ? "Icon.WindowRestore" : "Icon.WindowMaximize");
        MaximizeButton.ToolTip = maximized ? Strings.WindowRestore : Strings.WindowMaximize;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var files = DragDropFileValidator.ExtractFiles(e.Data);
        if (files.Count == 0)
        {
            return;
        }

        e.Handled = true;

        // Файлы, брошенные на уже открытый проект, встают в конец видеоряда:
        // так собирают ряд из нескольких роликов. Заменить проект — «Открыть».
        foreach (var file in files)
        {
            await _viewModel.AddToTimelineAsync(file, CancellationToken.None);
        }
    }
}
