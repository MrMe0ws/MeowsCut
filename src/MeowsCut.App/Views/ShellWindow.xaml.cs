using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
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
    private readonly PanelLayoutKeeper _layout;
    private bool _closeConfirmed;

    public ShellWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        _layout = new PanelLayoutKeeper(this, viewModel);
    }

    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e) => _layout.OnSplitterMoved(sender);

    /// <summary>
    /// Закрытие с несохранёнными правками сначала спрашивает, что с ними делать.
    /// Вопрос асинхронный (сохранение — запись файла), а Closing ждать не умеет:
    /// закрытие отменяется и повторяется, когда ответ получен.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        if (!_closeConfirmed)
        {
            e.Cancel = true;
            _ = Dispatcher.InvokeAsync(ConfirmCloseAsync);
            return;
        }

        _layout.Save();
    }

    private async Task ConfirmCloseAsync()
    {
        if (!await _viewModel.ConfirmDiscardAsync())
        {
            return;
        }

        // Монтаж сохранён или выброшен осознанно: при следующем запуске
        // предлагать его восстановить незачем.
        _viewModel.ForgetRecovery();
        _closeConfirmed = true;
        Close();
    }

    /// <summary>
    /// Пока идёт стартовая подготовка, курсор над окном показывает запрет: файл,
    /// брошенный в этот момент, всё равно некому прочитать.
    /// </summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        var files = DragDropFileValidator.ExtractFiles(e.Data);
        e.Effects = files.Count > 0 && _viewModel.CanStartWork ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Разворачивая окно, Windows уводит его края за экран на толщину рамки: у окна
    /// со своим заголовком это срезает кнопки. Отступ возвращает их на место, а заодно
    /// кнопка меняет значок на «восстановить».
    /// </summary>
    /// <remarks>
    /// Вылет меряется ещё раз после раскладки: окно, развёрнутое при запуске,
    /// получает свой размер чуть позже события.
    /// </remarks>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        FitMaximizedFrame();
        TitleBar.ShowMaximized(WindowState == WindowState.Maximized);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(FitMaximizedFrame));
    }

    private void FitMaximizedFrame() =>
        BorderThickness = WindowState == WindowState.Maximized ? MaximizedFrame.Overhang(this) : default;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var files = DragDropFileValidator.ExtractFiles(e.Data);
        if (files.Count == 0 || !_viewModel.CanStartWork)
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
