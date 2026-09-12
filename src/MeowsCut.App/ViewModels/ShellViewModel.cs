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
using MeowsCut.Core.Projects;
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
    private readonly IProjectStore _projectStore;
    private readonly IShellIntegration _shellIntegration;
    private readonly AutosaveService _autosave;
    private readonly ISilenceDetector _silenceDetector;
    private readonly ILogger<ShellViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia))]
    private MediaSummaryViewModel? _media;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusBarVisible))]
    [NotifyPropertyChangedFor(nameof(CanStartWork))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSoundCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSilenceCommand))]
    private bool _isBusy;

    /// <summary>
    /// Чем занято приложение прямо сейчас. Показывается поверх окна вместе
    /// с ожиданием: чтение файла занимает секунды, и без надписи это выглядит
    /// зависанием.
    /// </summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    [ObservableProperty]
    private string _toolsetStatus = Strings.FfmpegSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusBarVisible))]
    [NotifyPropertyChangedFor(nameof(IsPreparing))]
    [NotifyPropertyChangedFor(nameof(CanStartWork))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSoundCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSilenceCommand))]
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
        TitleInspectorViewModel titleInspector,
        PresetsViewModel presets,
        TimelineThumbnailLoader thumbnailLoader,
        IErrorPresenter errorPresenter,
        IProjectStore projectStore,
        IShellIntegration shellIntegration,
        AutosaveService autosave,
        ISilenceDetector silenceDetector,
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
        _projectStore = projectStore;
        _shellIntegration = shellIntegration;
        _autosave = autosave;
        _silenceDetector = silenceDetector;
        Export = export;
        Timeline = timeline;
        Preview = preview;
        Inspector = inspector;
        AudioInspector = audioInspector;
        TitleInspector = titleInspector;
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

            // Правка на доске запускает отсчёт до автосохранения заново.
            _autosave.Track(Project, ProjectPath);
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

    /// <summary>Свойства выбранной надписи.</summary>
    public TitleInspectorViewModel TitleInspector { get; }

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

    /// <summary>
    /// Можно ли сейчас брать файлы в работу. Пока идёт стартовая подготовка, ffmpeg
    /// ещё не найден, и открытие файла упиралось бы в сообщение «FFmpeg не найден» —
    /// про программу, которая на самом деле просто не успела запуститься. Поэтому
    /// пункты меню и перетаскивание на это время выключены, а чем занято приложение,
    /// видно в строке состояния.
    /// </summary>
    public bool CanStartWork => IsToolsetReady && !IsBusy;

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

        if (initialFile is null || !File.Exists(initialFile))
        {
            // Файл не открывают — самое время спросить про прошлый сеанс.
            // Если открывают, предложение восстановиться только мешало бы:
            // человек уже сказал, с чем хочет работать.
            await TryRecoverAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        // Черновик открывается как проект, всё остальное — как новый файл на доске
        if (Path.GetExtension(initialFile).Equals(IProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
        {
            await OpenProjectFileAsync(initialFile, cancellationToken).ConfigureAwait(true);
            return;
        }

        await OpenAsync(initialFile, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanStartWork))]
    private async Task OpenFileAsync(CancellationToken cancellationToken)
    {
        var paths = _fileDialogService.PickVideoFiles();
        if (paths.Count == 0)
        {
            return;
        }

        if (Path.GetExtension(paths[0]).Equals(IProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
        {
            await OpenProjectFileAsync(paths[0], cancellationToken).ConfigureAwait(true);
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

        BusyText = Strings.BusyOpeningFile;
        IsBusy = true;
        try
        {
            var info = await _mediaProbe.ProbeAsync(path, cancellationToken).ConfigureAwait(true);

            Attach(Project.FromMedia(info), MediaSummaryViewModel.Create(info));
            ProjectPath = null;

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
    [RelayCommand(CanExecute = nameof(CanStartWork))]
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

        BusyText = Strings.BusyAddingFile;
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
    [RelayCommand(CanExecute = nameof(CanStartWork))]
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

    /// <summary>
    /// Ставит проект на доску и раздаёт его панелям. Общее место для обоих входов:
    /// открытия файла и открытия черновика.
    /// </summary>
    private void Attach(Project project, MediaSummaryViewModel? media)
    {
        Media = media;
        Project = project;
        _autosave.Track(project, ProjectPath);

        Timeline.Attach(project);
        RemoveSilenceCommand.NotifyCanExecuteChanged();
        Preview.Attach(project);
        _thumbnailLoader.Attach(Timeline);
        Export.Attach(project);
        Presets.Attach(project);
        Sources.Update(project);
    }

    [RelayCommand]
    private void CloseMedia()
    {
        ProjectPath = null;
        Media = null;
        Project = null;
        Sources.Update(null);

        // Проект закрыли осознанно — восстанавливать нечего.
        _autosave.Clear();

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

    partial void OnIsToolsetReadyChanged(bool value) => RefreshRecentFileAvailability();

    partial void OnIsBusyChanged(bool value) => RefreshRecentFileAvailability();

    /// <summary>Кнопки недавних файлов гаснут вместе с пунктами меню.</summary>
    private void RefreshRecentFileAvailability()
    {
        foreach (var file in RecentFiles)
        {
            file.RefreshAvailability();
        }
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

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenAsync(CancellationToken cancellationToken) =>
        IsProject
            ? shell.OpenProjectFileAsync(Path, cancellationToken)
            : shell.OpenAsync(Path, cancellationToken);

    private bool CanOpen() => shell.CanStartWork;

    /// <summary>Пересчитать доступность: список переживает стартовую подготовку.</summary>
    internal void RefreshAvailability() => OpenCommand.NotifyCanExecuteChanged();

    /// <summary>Черновик в списке недавних открывается как проект, а не как новый файл.</summary>
    private bool IsProject =>
        System.IO.Path.GetExtension(Path).Equals(IProjectStore.Extension, StringComparison.OrdinalIgnoreCase);
}
