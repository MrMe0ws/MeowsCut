using System.Windows;
using MeowsCut.App.ViewModels;

namespace MeowsCut.App.Views.Dialogs;

/// <summary>Окно настроек. Вся логика — в SettingsViewModel.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        // Модели может не быть: так окно создаёт проверка разметки, и падать на этом
        // оно не должно — проверяются как раз кисти и стили, а не поведение.
        if (viewModel is not null)
        {
            viewModel.Saved += OnSaved;
            Closed += (_, _) => viewModel.Saved -= OnSaved;
        }
    }

    private void OnSaved(object? sender, EventArgs e) => Close();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
