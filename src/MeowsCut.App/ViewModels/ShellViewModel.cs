using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Localization;
using MeowsCut.App.Services;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
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
    private readonly TimelineThumbnailLoader _thumbnailLoader;
    private readonly IErrorPresenter _errorPresenter;
    private readonly IShellIntegration _shellIntegration;
    private readonly ILogger<ShellViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia))]
    private MediaSummaryViewModel? _media;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusBarVisible))]
    private bool _isBusy;

    [ObservableProperty]
    private string _toolsetStatus = Strings.FfmpegSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusBarVisible))]
    [NotifyPropertyChangedFor(nameof(IsPreparing))]
    private bool _isToolsetReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreparing))]
    private string? _toolsetProblem;

    public ShellViewModel(
        IMediaProbe mediaProbe,
        IMediaToolsetLocator toolsetLocator,
        IMediaToolsetProvider toolsetProvider,
        IAppSettingsStore settingsStore,
        IFileDialogService fileDialogService,
        IDialogService dialogService,
        ExportViewModel export,
        TimelineViewModel timeline,
        PreviewViewModel preview,
        InspectorViewModel inspector,
        AudioInspectorViewModel audioInspector,
        PresetsViewModel presets,
        TimelineThumbnailLoader thumbnailLoader,
        IErrorPresenter errorPresenter,
        IShellIntegration shellIntegration,
        ILogger<ShellViewModel> logger)
    {
        _mediaProbe = mediaProbe;
        _toolsetLocator = toolsetLocator;
        _toolsetProvider = toolsetProvider;
        _settingsStore = settingsStore;
        _fileDialogService = fileDialogService;
        _dialogService = dialogService;
        _thumbnailLoader = thumbnailLoader;
        _errorPresenter = errorPresenter;
        _shellIntegration = shellIntegration;
        Export = export;
        Timeline = timeline;
        Preview = preview;
        Inspector = inspector;
        AudioInspector = audioInspector;
        Presets = presets;
        _logger = logger;

        // Правка на доске меняет проект: сводку экспорта нужно пересчитать сразу.
        Timeline.SequenceChanged += (_, sequence) =>
        {
            if (Project is null)
            {
                return;
            }

            Project = Project.WithSequence(sequence);
            Export.UpdateProject(Project);
            Presets.UpdateProject(Project);
        };

        Sources.AddToBoard = PlaceOnBoard;
        Sources.ShowInFolder = path => _shellIntegration.RevealInExplorer(path);
    }

    /// <summary>Панель экспорта. Живёт рядом с проектом и обновляется вместе с ним.</summary>
    public ExportViewModel Export { get; }

    /// <summary>Доска монтажа.</summary>
    public TimelineViewModel Timeline { get; }

    /// <summary>Кадр под курсором.</summary>
    public PreviewViewModel Preview { get; }

    /// <summary>Свойства выделенного клипа.</summary>
    public InspectorViewModel Inspector { get; }

    /// <summary>Свойства выбранного куска звука.</summary>
    public AudioInspectorViewModel AudioInspector { get; }

    /// <summary>Пресеты площадок, включая Telegram.</summary>
    public PresetsViewModel Presets { get; }

    /// <summary>Файлы, добавленные в проект.</summary>
    public SourcesViewModel Sources { get; } = new();

    /// <summary>
    /// Видна ли нижняя строка состояния. В обычной работе версия ffmpeg —
    /// техническая подробность, отъедающая высоту у доски монтажа; она нужна,
    /// только пока идёт чтение файла или пока ffmpeg не найден.
    /// </summary>
    public bool IsStatusBarVisible => !IsToolsetReady || IsBusy;

    /// <summary>
    /// Идёт стартовая подготовка: ffmpeg ещё ищется, а каждый аппаратный кодировщик
    /// проверяется пробным кадром — на машине без NVENC и Quick Sync это секунды
    /// ожидания. Без явного индикатора серая кнопка «Открыть видео» выглядит поломкой,
    /// поэтому на время подготовки она уступает место строке о том, что происходит.
    /// Не найденный ffmpeg — это уже не подготовка, а ошибка со своим сообщением.
    /// </summary>
    public bool IsPreparing => !IsToolsetReady && ToolsetProblem is null;

    /// <summary>Проект целиком: источники и таймлайн. Доска монтажа появится на следующем этапе.</summary>
    public Project? Project { get; private set; }

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
        var paths = _fileDialogService.PickVideoFiles();
        if (paths.Count == 0)
        {
            return;
        }

        // Первый файл открывает проект, остальные встают следом: выбрать сразу
        // несколько роликов и собрать из них один — обычный сценарий, а не редкость.
        await OpenAsync(paths[0], cancellationToken).ConfigureAwait(true);

        foreach (var path in paths.Skip(1))
        {
            await AddToTimelineAsync(path, cancellationToken).ConfigureAwait(true);
        }
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
            Project = Project.FromMedia(info);

            Timeline.Attach(Project);
            Preview.Attach(Project);
            _thumbnailLoader.Attach(Timeline);
            Export.Attach(Project);
            Presets.Attach(Project);
            Sources.Update(Project);

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
            _dialogService.ShowError(_errorPresenter.Present(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Добавить ещё один файл в конец видеоряда.</summary>
    [RelayCommand]
    private async Task AddFileAsync(CancellationToken cancellationToken)
    {
        foreach (var path in _fileDialogService.PickVideoFiles())
        {
            await AddToTimelineAsync(path, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Кладёт файл в конец видеоряда, не трогая уже собранное.
    /// </summary>
    /// <remarks>
    /// Если проекта ещё нет, это обычное открытие: отдельная ветка нужна только
    /// для второго и последующих файлов, из которых и собирается видеоряд.
    /// </remarks>
    public async Task AddToTimelineAsync(string path, CancellationToken cancellationToken)
    {
        if (Project is null)
        {
            await OpenAsync(path, cancellationToken).ConfigureAwait(true);
            return;
        }

        if (IsBusy || !IsToolsetReady)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var info = await _mediaProbe.ProbeAsync(path, cancellationToken).ConfigureAwait(true);
            var (project, source) = Project.WithSource(info);

            Project = project;
            Preview.UpdateProject(project);
            Timeline.AppendSource(project, source);
            Sources.Update(project);

            var settings = _settingsStore.Current.WithRecentFile(path);
            await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(true);
            RefreshRecentFiles();
        }
        catch (OperationCanceledException)
        {
            // Отмена — не ошибка.
        }
        catch (MeowsCutException ex)
        {
            _logger.LogWarning(ex, "Не удалось добавить {Path}", path);
            _dialogService.ShowError(_errorPresenter.Present(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Положить звук из файла на аудиодорожку.</summary>
    [RelayCommand]
    private async Task AddSoundAsync(CancellationToken cancellationToken)
    {
        if (Project is null || IsBusy || !IsToolsetReady)
        {
            return;
        }

        var path = _fileDialogService.PickAudioFile();
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var info = await _mediaProbe.ProbeAsync(path, cancellationToken).ConfigureAwait(true);

            if (!info.HasAudio)
            {
                _dialogService.ShowError(Strings.AddSound, Strings.FileHasNoSound);
                return;
            }

            var (project, source) = Project.WithSource(info);

            Project = project;
            Preview.UpdateProject(project);
            Timeline.AppendAudioSource(project, source);
            Sources.Update(project);
        }
        catch (OperationCanceledException)
        {
            // Отмена — не ошибка.
        }
        catch (MeowsCutException ex)
        {
            _logger.LogWarning(ex, "Не удалось добавить звук {Path}", path);
            _dialogService.ShowError(_errorPresenter.Present(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Кладёт уже добавленный файл на доску ещё раз.
    /// </summary>
    /// <remarks>
    /// Второй раз читать файл не нужно — источник в проекте уже есть. Видео и фото
    /// идут в конец видеоряда, звук — на дорожку от плейхеда: класть музыку в конец
    /// ролика бессмысленно, а кадр посреди дорожки звука — некуда.
    /// </remarks>
    private void PlaceOnBoard(MediaSource source)
    {
        if (Project is not { } project)
        {
            return;
        }

        if (source.Info.HasVideo)
        {
            Timeline.AppendSource(project, source);
        }
        else
        {
            Timeline.AppendAudioSource(project, source);
        }
    }

    /// <summary>Открыть финальный шаг: пресеты и параметры вывода.</summary>
    [RelayCommand]
    private void OpenExport() => _dialogService.ShowExport();

    [RelayCommand]
    private void OpenSettings() => _dialogService.ShowSettings();

    [RelayCommand]
    private void CloseMedia()
    {
        Media = null;
        Project = null;
        Sources.Update(null);

        _thumbnailLoader.Detach();
        Timeline.Detach();
        Preview.Detach();
        Export.Detach();
        Presets.Detach();
    }

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
