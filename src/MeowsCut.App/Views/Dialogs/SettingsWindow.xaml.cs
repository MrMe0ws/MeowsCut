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
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
