using System.IO;
using System.Windows;
using System.Windows.Threading;
using MeowsCut.Core.Media;
using MeowsCut.App.Localization;
using Microsoft.Win32;

namespace MeowsCut.App.Services;

/// <summary>
/// Маршалинг в UI-поток. Отдельный интерфейс, чтобы ViewModels не зависели от Dispatcher.
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);

    Task InvokeAsync(Action action);
}

public sealed class UiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    public Task InvokeAsync(Action action) => dispatcher.InvokeAsync(action).Task;
}

/// <summary>
/// Системные диалоги. ViewModel не должна знать про Win32-диалоги напрямую.
/// </summary>
public interface IFileDialogService
{
    string? PickVideoFile();

    string? PickFolder(string title);
}

public sealed class FileDialogService : IFileDialogService
{
    public string? PickVideoFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.OpenDialogTitle,
            Filter = MediaFileTypes.BuildOpenDialogFilter(Strings.FilterVideoFiles, Strings.FilterAllFiles),
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}

/// <summary>
/// Показ сообщений пользователю. На этапе 1 — системные окна; на этапе полировки
/// заменяется на собственный диалог с деталями и копированием лога.
/// </summary>
public interface IDialogService
{
    void ShowError(string title, string message);
}

public sealed class DialogService : IDialogService
{
    public void ShowError(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}

/// <summary>
/// Проверка перетаскиваемых файлов до обращения к ffprobe: мгновенная реакция на drag-over.
/// </summary>
public static class DragDropFileValidator
{
    public static string? ExtractSingleFile(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return null;
        }

        if (data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
        {
            return null;
        }

        var candidate = files.FirstOrDefault(File.Exists);
        return candidate;
    }

    /// <summary>
    /// Знакомое расширение подсвечиваем сразу; незнакомое всё равно пробуем открыть,
    /// решение принимает ffprobe.
    /// </summary>
    public static bool LooksLikeVideo(string path) => MediaFileTypes.IsKnownVideoExtension(path);
}
