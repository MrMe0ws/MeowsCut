using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Formatting;
using MeowsCut.App.Localization;
using MeowsCut.App.Services;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Export;
using MeowsCut.Core.Jobs;
using MeowsCut.Core.Media;
using MeowsCut.Core.Processing;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

public enum ExportState
{
    Idle = 0,
    Running,
    Done,
    Failed
}

/// <summary>
/// Панель экспорта: настройки, сводка, запуск, прогресс и результат.
/// Про ffmpeg не знает — только про планировщик, очередь и движок через интерфейсы.
/// </summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private readonly IExportPlanner _planner;
    private readonly IExportEngine _engine;
    private readonly IJobQueue _jobQueue;
    private readonly IMediaToolsetProvider _toolsetProvider;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IShellIntegration _shell;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimelineViewModel _timeline;
    private readonly AppPaths _paths;
    private readonly ILogger<ExportViewModel> _logger;

    /// <summary>Сколько секунд рендерит кнопка проверки фрагмента.</summary>
    private static readonly TimeSpan PreviewFragmentLength = TimeSpan.FromSeconds(5);

    private Project? _project;
    private JobHandle? _currentJob;
    private bool _previewMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsRunning), nameof(IsDone))]
    private ExportState _state = ExportState.Idle;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private ContainerFormat _container = ContainerFormat.Mp4;

    [ObservableProperty]
    private VideoCodec _videoCodec = VideoCodec.H264;

    [ObservableProperty]
    private ResolutionOption _resolution = ResolutionOption.Original;

    [ObservableProperty]
    private FrameRateOption _frameRate = FrameRateOption.Original;

    [ObservableProperty]
    private bool _keepAudio = true;

    [ObservableProperty]
    private bool _preferStreamCopy;

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private string _progressStage = string.Empty;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    [ObservableProperty]
    private string _remainingText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _resultSizeText = string.Empty;

    public ExportViewModel(
        IExportPlanner planner,
        IExportEngine engine,
        IJobQueue jobQueue,
        IMediaToolsetProvider toolsetProvider,
        IAppSettingsStore settingsStore,
        IShellIntegration shell,
        IUiDispatcher dispatcher,
        TimelineViewModel timeline,
        AppPaths paths,
        ILogger<ExportViewModel> logger)
    {
        _timeline = timeline;
        _paths = paths;
        _planner = planner;
        _engine = engine;
        _jobQueue = jobQueue;
        _toolsetProvider = toolsetProvider;
        _settingsStore = settingsStore;
        _shell = shell;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public bool IsIdle => State is ExportState.Idle or ExportState.Failed;

    public bool IsRunning => State == ExportState.Running;

    public bool IsDone => State == ExportState.Done;

    public ObservableCollection<SummaryLine> SummaryLines { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public IReadOnlyList<ContainerFormat> Containers { get; } =
        [ContainerFormat.Mp4, ContainerFormat.WebM, ContainerFormat.Mov, ContainerFormat.Mkv, ContainerFormat.Avi];

    public ObservableCollection<VideoCodec> VideoCodecs { get; } = [];

    public IReadOnlyList<ResolutionOption> Resolutions { get; } = ResolutionOption.All;

    public IReadOnlyList<FrameRateOption> FrameRates { get; } = FrameRateOption.All;

    /// <summary>Готовит панель под открытый проект.</summary>
    public void Attach(Project project)
    {
        _project = project;
        State = ExportState.Idle;
        ErrorMessage = null;

        var source = project.Sources.FirstOrDefault();
        if (source is not null)
        {
            OutputPath = OutputNameBuilder.Build(source.FilePath, _settingsStore.Current, Container.FileExtension());
        }

        RefreshCodecs();
        RefreshSummary();
        RefreshCommands();
    }

    /// <summary>
    /// Проект изменился на таймлайне: пересчитываем сводку, но путь результата
    /// и настройки не трогаем — их выбрал пользователь.
    /// </summary>
    public void UpdateProject(Project project)
    {
        _project = project;

        if (State == ExportState.Done)
        {
            State = ExportState.Idle;
        }

        RefreshSummary();
    }

    public void Detach()
    {
        _project = null;
        SummaryLines.Clear();
        Warnings.Clear();
        State = ExportState.Idle;
        RefreshCommands();
    }

    /// <summary>
    /// Пересчёт доступности кнопок. Команды не опрашиваются сами: без этого вызова
    /// «Экспортировать» осталась бы серой до первого другого действия.
    /// </summary>
    private void RefreshCommands()
    {
        StartCommand.NotifyCanExecuteChanged();
        PreviewFragmentCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OpenResultCommand.NotifyCanExecuteChanged();
        ShowInFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnOutputPathChanged(string value) => RefreshCommands();

    partial void OnStateChanged(ExportState value) => RefreshCommands();

    partial void OnContainerChanged(ContainerFormat value)
    {
        RefreshCodecs();

        if (!string.IsNullOrEmpty(OutputPath))
        {
            OutputPath = Path.ChangeExtension(OutputPath, value.FileExtension());
        }

        RefreshSummary();
    }

    partial void OnVideoCodecChanged(VideoCodec value) => RefreshSummary();

    partial void OnResolutionChanged(ResolutionOption value) => RefreshSummary();

    partial void OnFrameRateChanged(FrameRateOption value) => RefreshSummary();

    partial void OnKeepAudioChanged(bool value) => RefreshSummary();

    partial void OnPreferStreamCopyChanged(bool value) => RefreshSummary();

    private void RefreshCodecs()
    {
        var capabilities = _toolsetProvider.Current?.Capabilities;

        VideoCodecs.Clear();
        foreach (var codec in CompatibilityMatrix.VideoCodecsFor(Container))
        {
            // Не показываем то, чего нет в найденной сборке ffmpeg: лучше не предложить,
            // чем упасть в конце длинного экспорта.
            if (capabilities is null || Ffmpeg.Arguments.EncoderCatalog.IsAvailable(codec, capabilities))
            {
                VideoCodecs.Add(codec);
            }
        }

        if (!VideoCodecs.Contains(VideoCodec))
        {
            VideoCodec = VideoCodecs.FirstOrDefault(CompatibilityMatrix.DefaultVideoCodec(Container));
        }
    }

    public ExportSettings BuildSettings() => new ExportSettings
    {
        Container = Container,
        OutputPath = OutputPath,
        PreferStreamCopy = PreferStreamCopy,
        Video = VideoSettings.Default with
        {
            Codec = VideoCodec,
            Resolution = Resolution.ToSpec(),
            FrameRate = FrameRate.ToSpec()
        },
        Audio = KeepAudio ? AudioSettings.Default : AudioSettings.Disabled
    }.Normalized();

    private void RefreshSummary()
    {
        SummaryLines.Clear();
        Warnings.Clear();

        if (_project is null || _toolsetProvider.Current is null)
        {
            return;
        }

        try
        {
            var plan = _planner.CreatePlan(new ExportRequest(_project, BuildSettings()), _toolsetProvider.Current.Capabilities);
            var summary = plan.Summary;

            SummaryLines.Add(new SummaryLine(Strings.FieldResolution, summary.Resolution.ToString()));
            SummaryLines.Add(new SummaryLine(Strings.FieldFrameRate, $"{summary.Fps:0.###} fps"));
            SummaryLines.Add(new SummaryLine(Strings.FieldCodec, summary.VideoCodecLabel));
            SummaryLines.Add(new SummaryLine(Strings.FieldBitrate, summary.VideoBitrateLabel));
            SummaryLines.Add(new SummaryLine(Strings.SectionAudio, summary.AudioLabel));
            SummaryLines.Add(new SummaryLine(Strings.FieldDuration, DisplayFormat.Duration(summary.Duration)));
            SummaryLines.Add(new SummaryLine(
                Strings.EstimatedSize,
                summary.EstimatedSizeBytes is { } bytes ? "~ " + DisplayFormat.FileSize(bytes) : Strings.Unknown));

            foreach (var warning in plan.Warnings)
            {
                Warnings.Add(warning.Message);
            }
        }
        catch (MeowsCutException ex)
        {
            Warnings.Add(ex.Message);
        }
    }

    /// <summary>
    /// Рендерит короткий кусок от курсора теми же настройками и открывает его.
    /// </summary>
    /// <remarks>
    /// Это единственный честный способ увидеть заранее, как настройки скажутся
    /// на картинке: предпросмотр идёт ровно тем же пайплайном, что и полный экспорт,
    /// поэтому расхождений между проверкой и результатом не бывает.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private void PreviewFragment()
    {
        if (_project is null || _toolsetProvider.Current is null)
        {
            return;
        }

        var start = _timeline.Playhead;
        var range = new TimeRange(start, start + PreviewFragmentLength);
        var sliced = _project.Sequence.Slice(range);

        if (sliced.IsEmpty)
        {
            ErrorMessage = Strings.PreviewFragmentEmpty;
            return;
        }

        var previewPath = Path.Combine(
            _paths.PreviewDirectory,
            $"фрагмент-{DateTime.Now:HH-mm-ss}{Container.FileExtension()}");

        Directory.CreateDirectory(_paths.PreviewDirectory);

        var settings = BuildSettings() with
        {
            OutputPath = previewPath,
            Overwrite = OverwritePolicy.Overwrite,
            Video = BuildSettings().Video with { Speed = EncodingSpeed.VeryFast }
        };

        ExportPlan plan;
        try
        {
            plan = _planner.CreatePlan(
                new ExportRequest(_project.WithSequence(sliced), settings),
                _toolsetProvider.Current.Capabilities);
        }
        catch (MeowsCutException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        _previewMode = true;
        State = ExportState.Running;
        Percent = 0;
        ErrorMessage = null;
        ProgressStage = Strings.PreviewFragmentRunning;

        StartJob(plan, Strings.PreviewFragmentRunning);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (_project is null || _toolsetProvider.Current is null)
        {
            return;
        }

        ExportPlan plan;
        try
        {
            plan = _planner.CreatePlan(
                new ExportRequest(_project, BuildSettings()),
                _toolsetProvider.Current.Capabilities);
        }
        catch (MeowsCutException ex)
        {
            State = ExportState.Failed;
            ErrorMessage = ex.Message;
            return;
        }

        _previewMode = false;
        State = ExportState.Running;
        Percent = 0;
        ErrorMessage = null;
        ProgressStage = plan.StageDisplayName(0);
        OutputPath = plan.OutputPath;

        StartJob(plan, $"Экспорт {Path.GetFileName(plan.OutputPath)}");
    }

    private void StartJob(ExportPlan plan, string title)
    {
        var descriptor = new JobDescriptor(
            title,
            JobKind.Export,
            (progress, token) => _engine.ExecuteAsync(plan, progress, token));

        var handle = _jobQueue.Enqueue(descriptor);
        _currentJob = handle;

        handle.ProgressChanged += OnProgressChanged;
        _ = WaitForCompletionAsync(handle);

        CancelCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        PreviewFragmentCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() =>
        _project is not null && State != ExportState.Running && !string.IsNullOrWhiteSpace(OutputPath);

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _currentJob?.Cancel();

    [RelayCommand(CanExecute = nameof(IsDone))]
    private void OpenResult() => _shell.OpenFile(OutputPath);

    [RelayCommand(CanExecute = nameof(IsDone))]
    private void ShowInFolder() => _shell.RevealInExplorer(OutputPath);

    private void OnProgressChanged(object? sender, JobProgress progress) =>
        _dispatcher.Post(() =>
        {
            Percent = progress.Percent;
            ProgressStage = progress.Detail is null
                ? progress.StageName
                : $"{progress.StageName} · {progress.Detail}";

            ElapsedText = DisplayFormat.Duration(progress.Elapsed);
            RemainingText = progress.Remaining is { } remaining
                ? DisplayFormat.Duration(remaining)
                : "…";
        });

    private async Task WaitForCompletionAsync(JobHandle handle)
    {
        var result = await handle.Completion.ConfigureAwait(false);
        handle.ProgressChanged -= OnProgressChanged;

        _dispatcher.Post(() =>
        {
            switch (result.Status)
            {
                case JobStatus.Completed when _previewMode:
                    // Фрагмент не результат работы, а проверка: показываем его и
                    // возвращаем панель в исходное состояние.
                    _previewMode = false;
                    State = ExportState.Idle;
                    Percent = 0;

                    if (result.OutputPath is { } fragment)
                    {
                        _shell.OpenFile(fragment);
                    }

                    break;

                case JobStatus.Completed:
                    State = ExportState.Done;
                    Percent = 100;
                    ResultSizeText = File.Exists(OutputPath)
                        ? DisplayFormat.FileSize(new FileInfo(OutputPath).Length)
                        : string.Empty;
                    break;

                case JobStatus.Canceled:
                    State = ExportState.Idle;
                    Percent = 0;
                    break;

                default:
                    State = ExportState.Failed;
                    ErrorMessage = result.Error is MeowsCutException known
                        ? known.Message
                        : result.Error?.Message ?? Strings.ExportFailed;
                    _logger.LogError(result.Error, "Экспорт завершился ошибкой");
                    break;
            }

            _previewMode = false;
            _currentJob?.Dispose();
            _currentJob = null;

            StartCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            PreviewFragmentCommand.NotifyCanExecuteChanged();
            OpenResultCommand.NotifyCanExecuteChanged();
            ShowInFolderCommand.NotifyCanExecuteChanged();
        });
    }
}

/// <summary>Строка сводки перед экспортом.</summary>
public sealed record SummaryLine(string Label, string Value);
