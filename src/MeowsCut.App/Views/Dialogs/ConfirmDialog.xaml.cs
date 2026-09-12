using System.Windows;

namespace MeowsCut.App.Views.Dialogs;

/// <summary>
/// Вопрос с двумя ответами: «да» подсвечено, «нет» остаётся тихой кнопкой.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string acceptText, string declineText)
    {
        DataContext = new { Title = title, Message = message, AcceptText = acceptText, DeclineText = declineText };
        InitializeComponent();
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnDecline(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
