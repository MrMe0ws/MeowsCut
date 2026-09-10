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
        var file = DragDropFileValidator.ExtractSingleFile(e.Data);
        e.Effects = file is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var file = DragDropFileValidator.ExtractSingleFile(e.Data);
        if (file is null)
        {
            return;
        }

        e.Handled = true;

        // Файл, брошенный на уже открытый проект, встаёт в конец видеоряда:
        // так собирают ряд из нескольких роликов. Заменить проект — «Открыть».
        await _viewModel.AddToTimelineAsync(file, CancellationToken.None);
    }
}
