using System.IO;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MeowsCut.App.Localization;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Projects;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Черновик проекта: сохранить монтаж и вернуться к нему позже.
/// </summary>
/// <remarks>
/// Черновик — это не экспорт. Экспорт отдаёт готовый ролик и забывает, как он собран;
/// черновик хранит саму раскладку кусков, чтобы её можно было править дальше.
/// </remarks>
public sealed partial class ShellViewModel
{
    /// <summary>Путь открытого черновика. Пусто — монтаж ещё ни разу не сохраняли.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectFileName))]
    private string? _projectPath;

    public string? ProjectFileName =>
        ProjectPath is null ? null : Path.GetFileNameWithoutExtension(ProjectPath);

    [RelayCommand]
    private async Task SaveProjectAsync(CancellationToken cancellationToken)
    {
        // Уже сохранённый черновик перезаписывается молча: «Сохранить» не должен
        // каждый раз спрашивать, куда именно.
        if (ProjectPath is { } path)
        {
            await WriteAsync(path, cancellationToken).ConfigureAwait(true);
            return;
        }

        await SaveProjectAsAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveProjectAsAsync(CancellationToken cancellationToken)
    {
        if (Project is null)
        {
            return;
        }

        var chosen = _fileDialogService.PickProjectToSave(SuggestName());
        if (chosen is null)
        {
            return;
        }

        await WriteAsync(chosen, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenProjectAsync(CancellationToken cancellationToken)
    {
        var path = _fileDialogService.PickProjectToOpen();
        if (path is null)
        {
            return;
        }

        await OpenProjectFileAsync(path, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Открывает черновик по пути — годится и для двойного клика по файлу.</summary>
    public async Task OpenProjectFileAsync(string path, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!IsToolsetReady)
        {
            _dialogService.ShowError(Strings.FfmpegNotFoundTitle, Strings.FfmpegNotFoundMessage);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _projectStore.LoadAsync(path, cancellationToken).ConfigureAwait(true);

            Attach(result.Project, MediaSummaryOf(result.Project));
            ProjectPath = path;

            var settings = _settingsStore.Current.WithRecentFile(path);
            await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(true);
            RefreshRecentFiles();

            // О потерянном молчать нельзя: иначе пропавший файл выглядит как
            // «редактор сам выкинул половину монтажа».
            if (result.HasWarnings)
            {
                _dialogService.ShowError(Strings.ProjectOpenedWithProblems, string.Join(Environment.NewLine, result.Warnings));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is MeowsCutException or IOException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Не удалось открыть черновик {Path}", path);
            _dialogService.ShowError(Strings.ProjectOpenFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task WriteAsync(string path, CancellationToken cancellationToken)
    {
        if (Project is not { } project)
        {
            return;
        }

        try
        {
            await _projectStore.SaveAsync(project, path, cancellationToken).ConfigureAwait(true);
            ProjectPath = path;
            ToolsetStatus = string.Format(Strings.ProjectSaved, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Не удалось сохранить черновик {Path}", path);
            _dialogService.ShowError(Strings.ProjectSaveFailed, ex.Message);
        }
    }

    /// <summary>Имя по умолчанию — от первого файла: черновик обычно про него.</summary>
    private string SuggestName() =>
        Project?.Sources.Count > 0
            ? Path.GetFileNameWithoutExtension(Project.Sources[0].FilePath)
            : Strings.AppTitle;

    private MediaSummaryViewModel? MediaSummaryOf(Project project) =>
        project.Sources.Count > 0 ? MediaSummaryViewModel.Create(project.Sources[0].Info) : null;
}
