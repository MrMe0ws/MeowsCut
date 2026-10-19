using System.Windows;

namespace MeowsCut.App.Views.Dialogs;

/// <summary>
/// Вопрос с двумя ответами: «да» подсвечено, «нет» остаётся тихой кнопкой.
/// С третьей кнопкой «отмена» — <see cref="DialogResult"/> null, как и у крестика окна.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string acceptText, string declineText, string? cancelText = null)
    {
        DataContext = new
        {
            Title = title,
            Message = message,
            AcceptText = acceptText,
            DeclineText = declineText,
            CancelText = cancelText ?? string.Empty,
            HasCancel = cancelText is not null
        };
        InitializeComponent();
    }

    /// <summary>Ответ на вопрос с тремя вариантами: null — передумали.</summary>
    public bool? Answer { get; private set; }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Answer = true;
        DialogResult = true;
        Close();
    }

    private void OnDecline(object sender, RoutedEventArgs e)
    {
        Answer = false;
        DialogResult = false;
        Close();
    }
}
