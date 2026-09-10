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
/// Показ сообщений пользователю.
/// </summary>
public interface IDialogService
{
    void ShowError(string title, string message);

    /// <summary>Показывает разобранную ошибку с подробностями и доступом к логам.</summary>
    void ShowError(Core.Diagnostics.AppError error);

    void ShowSettings();

    /// <summary>Финальный шаг: пресеты и параметры вывода.</summary>
    void ShowExport();
}

/// <summary>
/// Собственные окна вместо системных: тёмная тема, подробности под раскрытием
/// и кнопки, которых хватает для внятного сообщения об ошибке.
/// </summary>
public sealed class DialogService(
    Core.Configuration.AppPaths paths,
    Core.Abstractions.IShellIntegration shell,
    Func<ViewModels.SettingsViewModel> settingsFactory,
    Func<ViewModels.PresetsViewModel> presetsFactory,
    Func<ViewModels.ExportViewModel> exportFactory) : IDialogService
{
    private Views.Dialogs.ExportWindow? _export;
    public void ShowError(string title, string message) =>
        ShowError(new Core.Diagnostics.AppError(
            Core.Diagnostics.ErrorCode.Unknown, title, message, null, null));

    public void ShowError(Core.Diagnostics.AppError error)
    {
        var dialog = new Views.Dialogs.ErrorDialog(error, paths, shell)
        {
            Owner = Application.Current.MainWindow
        };

        dialog.ShowDialog();
    }

    public void ShowExport()
    {
        // Окно немодальное и одно на приложение: экспорт длинный, и запирать
        // из-за него доску монтажа незачем — за это время можно подрезать хвост.
        if (_export is not null)
        {
            _export.Activate();
            return;
        }

        _export = new Views.Dialogs.ExportWindow(presetsFactory(), exportFactory())
        {
            Owner = Application.Current.MainWindow
        };

        _export.Closed += (_, _) => _export = null;
        _export.Show();
    }

    public void ShowSettings()
    {
        var window = new Views.Dialogs.SettingsWindow(settingsFactory())
        {
            Owner = Application.Current.MainWindow
        };

        window.ShowDialog();
    }
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
