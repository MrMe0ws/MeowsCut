using System.Windows;
using MeowsCut.App.Services;
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
