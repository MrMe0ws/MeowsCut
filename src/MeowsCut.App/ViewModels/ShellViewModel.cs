using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Localization;
using MeowsCut.App.Services;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Корневая ViewModel: состояние приложения, открытие файла, статус FFmpeg.
/// Про процессы и аргументы не знает — только про абстракции ядра.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IMediaProbe _mediaProbe;
    private readonly IMediaToolsetLocator _toolsetLocator;
    private readonly IMediaToolsetProvider _toolsetProvider;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IFileDialogService _fileDialogService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<ShellViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia))]
    private MediaSummaryViewModel? _media;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _toolsetStatus = Strings.FfmpegSearching;

    [ObservableProperty]
    private bool _isToolsetReady;

    [ObservableProperty]
    private string? _toolsetProblem;

    public ShellViewModel(
        IMediaProbe mediaProbe,
        IMediaToolsetLocator toolsetLocator,
        IMediaToolsetProvider toolsetProvider,
        IAppSettingsStore settingsStore,
        IFileDialogService fileDialogService,
        IDialogService dialogService,
        ILogger<ShellViewModel> logger)
    {
        _mediaProbe = mediaProbe;
        _toolsetLocator = toolsetLocator;
        _toolsetProvider = toolsetProvider;
        _settingsStore = settingsStore;
        _fileDialogService = fileDialogService;
        _dialogService = dialogService;
        _logger = logger;
    }

    public bool HasMedia => Media is not null;

    public ObservableCollection<RecentFileViewModel> RecentFiles { get; } = [];

    /// <summary>
    /// Стартовая проверка: ищем ffmpeg, не блокируя показ окна. Если приложение запущено
    /// с путём к файлу (двойной клик по видео, «Открыть с помощью»), сразу открываем его.
    /// </summary>
    public async Task InitializeAsync(string? initialFile, CancellationToken cancellationToken)
    {
        await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(true);
        RefreshRecentFiles();

        var result = await _toolsetLocator.LocateAsync(cancellationToken).ConfigureAwait(true);
        ApplyToolsetResult(result);

        if (initialFile is not null && File.Exists(initialFile))
        {
            await OpenAsync(initialFile, cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync(CancellationToken cancellationToken)
    {
        var path = _fileDialogService.PickVideoFile();
        if (path is null)
        {
            return;
        }

        await OpenAsync(path, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Открывает файл: читает характеристики через ffprobe и показывает их.
    /// Вызывается и из диалога, и из drag &amp; drop, и из списка недавних.
    /// </summary>
    public async Task OpenAsync(string path, CancellationToken cancellationToken)
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
            var info = await _mediaProbe.ProbeAsync(path, cancellationToken).ConfigureAwait(true);
            Media = MediaSummaryViewModel.Create(info);

            var settings = _settingsStore.Current.WithRecentFile(path);
            await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(true);
            RefreshRecentFiles();
        }
        catch (OperationCanceledException)
        {
            // Отмена — не ошибка: пользователь закрыл окно или выбрал другой файл.
        }
        catch (MeowsCutException ex)
        {
            _logger.LogWarning(ex, "Не удалось открыть {Path}", path);
            _dialogService.ShowError(Strings.ErrorTitle, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CloseMedia() => Media = null;

    [RelayCommand]
    private async Task ChooseFfmpegFolderAsync(CancellationToken cancellationToken)
    {
        var folder = _fileDialogService.PickFolder(Strings.FfmpegChooseFolderTitle);
        if (folder is null)
        {
            return;
        }

        var result = await _toolsetLocator.ValidateDirectoryAsync(folder, cancellationToken).ConfigureAwait(true);
        if (!result.Found)
        {
            _dialogService.ShowError(Strings.FfmpegNotFoundTitle, result.FailureReason ?? Strings.FfmpegNotFoundMessage);
            return;
        }

        var settings = _settingsStore.Current with { FfmpegDirectory = folder };
        await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(true);

        ApplyToolsetResult(result);
    }

    private void ApplyToolsetResult(ToolsetLocationResult result)
    {
        if (result is { Found: true, Toolset: not null })
        {
            _toolsetProvider.Set(result.Toolset);
            IsToolsetReady = true;
            ToolsetProblem = null;
            ToolsetStatus = string.Format(Strings.FfmpegReady, result.Toolset.Version);
            return;
        }

        IsToolsetReady = false;
        ToolsetStatus = Strings.FfmpegNotFoundTitle;
        ToolsetProblem = Strings.FfmpegNotFoundMessage;
    }

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var path in _settingsStore.Current.RecentFiles.Take(5))
        {
            RecentFiles.Add(new RecentFileViewModel(path, this));
        }
    }
}

/// <summary>
/// Пункт списка недавних файлов.
/// </summary>
public sealed partial class RecentFileViewModel(string path, ShellViewModel shell) : ObservableObject
{
    public string Path { get; } = path;

    public string FileName { get; } = System.IO.Path.GetFileName(path);

    [RelayCommand]
    private Task OpenAsync(CancellationToken cancellationToken) => shell.OpenAsync(Path, cancellationToken);
}
