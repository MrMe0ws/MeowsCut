using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Localization;
using MeowsCut.App.Services;
using MeowsCut.App.Theming;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Presets;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Настройки приложения: где взять FFmpeg, куда складывать результаты, что писать в лог.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsStore _store;
    private readonly IMediaToolsetLocator _locator;
    private readonly IMediaToolsetProvider _toolsetProvider;
    private readonly IThumbnailService _thumbnails;
    private readonly IPresetProvider _presets;
    private readonly IFileDialogService _fileDialogs;
    private readonly IShellIntegration _shell;
    private readonly AppPaths _paths;
    private readonly ThemeManager _themes;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        IAppSettingsStore store,
        IMediaToolsetLocator locator,
        IMediaToolsetProvider toolsetProvider,
        IThumbnailService thumbnails,
        IPresetProvider presets,
        IFileDialogService fileDialogs,
        IShellIntegration shell,
        AppPaths paths,
        ThemeManager themes,
        ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _locator = locator;
        _toolsetProvider = toolsetProvider;
        _thumbnails = thumbnails;
        _presets = presets;
        _fileDialogs = fileDialogs;
        _shell = shell;
        _paths = paths;
        _themes = themes;
        _logger = logger;

        Load();
    }

    [ObservableProperty]
    private string _ffmpegDirectory = string.Empty;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private string _outputNameTemplate = string.Empty;

    [ObservableProperty]
    private bool _verboseLogging;

    /// <summary>
    /// Тема применяется сразу при выборе, а не по кнопке «Сохранить»: выбирают её
    /// глазами, и посмотреть на светлую тему надо раньше, чем решить оставить её.
    /// Не сохранённый выбор доживёт до закрытия приложения.
    /// </summary>
    [ObservableProperty]
    private ThemeOption _theme = ThemeOption.All[0];

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>Где приложение нашло FFmpeg на самом деле — это не всегда то, что в настройках.</summary>
    public string ToolsetSummary =>
        _toolsetProvider.Current is { } toolset
            ? $"{toolset.Version} · {toolset.Directory}"
            : Strings.FfmpegNotFoundTitle;

    public string UserPresetsDirectory => _paths.UserPresetsDirectory;

    private void Load()
    {
        var settings = _store.Current;

        FfmpegDirectory = settings.FfmpegDirectory ?? string.Empty;
        OutputDirectory = settings.DefaultOutputDirectory ?? string.Empty;
        OutputNameTemplate = settings.OutputNameTemplate;
        VerboseLogging = settings.VerboseLogging;
        Theme = ThemeOption.For(settings.Theme);
    }

    public IReadOnlyList<ThemeOption> Themes { get; } = ThemeOption.All;

    partial void OnThemeChanged(ThemeOption value) => _themes.Apply(value.Value);

    [RelayCommand]
    private void BrowseFfmpeg()
    {
        var folder = _fileDialogs.PickFolder(Strings.FfmpegChooseFolderTitle);
        if (folder is not null)
        {
            FfmpegDirectory = folder;
        }
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var folder = _fileDialogs.PickFolder(Strings.OutputFolderTitle);
        if (folder is not null)
        {
            OutputDirectory = folder;
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var directory = string.IsNullOrWhiteSpace(FfmpegDirectory) ? null : FfmpegDirectory.Trim();

        // Указанную папку проверяем сразу: узнать о неверном пути в момент экспорта — обидно.
        if (directory is not null)
        {
            var result = await _locator.ValidateDirectoryAsync(directory, cancellationToken).ConfigureAwait(true);

            if (!result.Found)
            {
                Status = result.FailureReason ?? Strings.FfmpegNotFoundMessage;
                return;
            }

            _toolsetProvider.Set(result.Toolset!);
            OnPropertyChanged(nameof(ToolsetSummary));
        }

        var settings = _store.Current with
        {
            FfmpegDirectory = directory,
            DefaultOutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory.Trim(),
            OutputNameTemplate = string.IsNullOrWhiteSpace(OutputNameTemplate)
                ? "{name}_meows{ext}"
                : OutputNameTemplate.Trim(),
            Theme = Theme.Value,
            VerboseLogging = VerboseLogging
        };

        await _store.SaveAsync(settings, cancellationToken).ConfigureAwait(true);

        Status = Strings.SettingsSaved;
        _logger.LogInformation("Настройки сохранены");
    }

    [RelayCommand]
    private void OpenLogs() => _shell.RevealInExplorer(_paths.LogsDirectory);

    [RelayCommand]
    private void OpenPresetsFolder()
    {
        Directory.CreateDirectory(_paths.UserPresetsDirectory);
        _shell.RevealInExplorer(_paths.UserPresetsDirectory);
    }

    [RelayCommand]
    private void ReloadPresets()
    {
        _presets.Reload();
        Status = Strings.PresetsReloaded;
    }

    [RelayCommand]
    private void ClearThumbnailCache()
    {
        _thumbnails.TrimCache(0);
        Status = Strings.CacheCleared;
    }
}
