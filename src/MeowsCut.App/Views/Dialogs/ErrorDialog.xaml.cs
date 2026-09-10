using System.Windows;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Diagnostics;

namespace MeowsCut.App.Views.Dialogs;

/// <summary>
/// Диалог ошибки: понятный текст сверху, подробности под раскрытием,
/// кнопки «скопировать» и «открыть лог» — этого хватает для внятного баг-репорта.
/// </summary>
public partial class ErrorDialog : Window
{
    private readonly AppError _error;
    private readonly AppPaths _paths;
    private readonly IShellIntegration _shell;

    public ErrorDialog(AppError error, AppPaths paths, IShellIntegration shell)
    {
        _error = error;
        _paths = paths;
        _shell = shell;

        DataContext = error;
        InitializeComponent();
    }

    private void OnCopyDetails(object sender, RoutedEventArgs e)
    {
        if (_error.Details is not { Length: > 0 } details)
        {
            return;
        }

        try
        {
            Clipboard.SetText(details);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException)
        {
            // Буфер обмена бывает занят другим приложением — не повод показывать
            // ещё одну ошибку поверх этой.
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e) => _shell.RevealInExplorer(_paths.LogsDirectory);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
