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
    private readonly Services.IFileDialogService _fileDialogService;
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
    [NotifyPropertyChangedFor(nameof(IsQualityVisible), nameof(IsBitrateVisible), nameof(IsTargetSizeVisible))]
    private QualityModeOption _qualityMode = QualityModeOption.All[0];

    [ObservableProperty]
    private int _crf = 23;

    [ObservableProperty]
    private int _bitrateKbps = 4000;

    [ObservableProperty]
    private double _targetSizeMegabytes = 8d;

    [ObservableProperty]
    private int _customWidth = 1080;

    [ObservableProperty]
    private int _customHeight = 1080;

    [ObservableProperty]
    private FitModeOption _fitMode = FitModeOption.All[0];

    [ObservableProperty]
    private double _customFps = 30d;

    [ObservableProperty]
    private AudioCodec _audioCodec = AudioCodec.Aac;

    [ObservableProperty]
    private int _audioBitrateKbps = 192;

    [ObservableProperty]
    private int _audioSampleRateHz = 48_000;

    [ObservableProperty]
    private int _audioChannels = 2;

    [ObservableProperty]
    private double _masterVolumePercent = 100d;

    /// <summary>Привести громкость результата к вещательной норме.</summary>
    [ObservableProperty]
    private bool _normalizeLoudness;

    /// <summary>
    /// Экспортировать только выделенный на доске кусок.
    /// </summary>
    /// <remarks>
    /// Переключатель, а не отдельная кнопка: все настройки вывода для куска
    /// те же самые, и дублировать ради него весь экран было бы странно.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopeText))]
    private bool _selectionOnly;

    [ObservableProperty]
    private bool _showAdvanced;

    [ObservableProperty]
    private EncodingSpeedOption _encodingSpeed = EncodingSpeedOption.All[2];

    [ObservableProperty]
    private bool _twoPass;

    /// <summary>Чем кодировать: процессором или видеокартой.</summary>
    [ObservableProperty]
    private HardwareAcceleration _hardware = HardwareAcceleration.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtitles), nameof(SubtitleFileName))]
    private string? _subtitlePath;

    [ObservableProperty]
    private SubtitleModeOption _subtitleMode = SubtitleModeOption.All[0];

    [ObservableProperty]
    private string _pixelFormat = string.Empty;

    [ObservableProperty]
    private int _keyframeIntervalFrames;

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

    /// <summary>
    /// Объяснение автоматической замены кодека. Молча менять выбор пользователя нельзя:
    /// он потом не поймёт, почему в файле оказался не тот кодек.
    /// </summary>
    [ObservableProperty]
    private string? _codecNotice;

    public ExportViewModel(
        IExportPlanner planner,
        IExportEngine engine,
        IJobQueue jobQueue,
        IMediaToolsetProvider toolsetProvider,
        IAppSettingsStore settingsStore,
        IShellIntegration shell,
        Services.IFileDialogService fileDialogService,
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
        _fileDialogService = fileDialogService;
        _dispatcher = dispatcher;
        _logger = logger;

        // Выделение на доске меняет и подпись отрезка, и саму возможность
        // экспортировать кусок: без подписки переключатель оставался бы серым
        // до следующего действия в окне экспорта.
        _timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TimelineViewModel.SelectedClip)
                or nameof(TimelineViewModel.SelectedAudioClip)
                or nameof(TimelineViewModel.SelectionCount))
            {
                RefreshScope();
            }
        };
    }

    /// <summary>Есть ли на доске выделение, которое можно отдать в экспорт отдельно.</summary>
    public bool HasSelectionRange => _timeline.SelectionRange is not null;

    /// <summary>
    /// Что именно уйдёт в файл. Отрезок называется временами, а не числом клипов:
    /// длительность результата — это то, что пользователь проверяет глазами.
    /// </summary>
    public string ScopeText
    {
        get
        {
            if (!SelectionOnly)
            {
                return Strings.ExportScopeWhole;
            }

            return _timeline.SelectionRange is { } range
                ? $"{DisplayFormat.Duration(range.Start)} → {DisplayFormat.Duration(range.End)}"
                : Strings.ExportScopeEmpty;
        }
    }

    private void RefreshScope()
    {
        OnPropertyChanged(nameof(HasSelectionRange));
        OnPropertyChanged(nameof(ScopeText));

        // Выделение сняли, пока стоял переключатель: молча экспортировать весь
        // ролик вместо куска нельзя — это не то, что просили.
        if (SelectionOnly && !HasSelectionRange)
        {
            SelectionOnly = false;
        }

        RefreshSummary();
        RefreshCommands();
    }

    partial void OnSelectionOnlyChanged(bool value)
    {
        RefreshSummary();
        RefreshCommands();
    }

    public bool IsIdle => State is ExportState.Idle or ExportState.Failed;

    public bool IsRunning => State == ExportState.Running;

    public bool IsDone => State == ExportState.Done;

    public bool IsQualityVisible => QualityMode.Mode == ViewModels.QualityMode.ConstantQuality;

    public bool IsBitrateVisible => QualityMode.Mode == ViewModels.QualityMode.Bitrate;

    public bool IsTargetSizeVisible => QualityMode.Mode == ViewModels.QualityMode.TargetSize;

    public bool IsCustomResolution => Resolution.IsCustom;

    public bool IsCustomFrameRate => FrameRate.IsCustom;

    public IReadOnlyList<QualityModeOption> QualityModes { get; } = QualityModeOption.All;

    public IReadOnlyList<EncodingSpeedOption> EncodingSpeeds { get; } = EncodingSpeedOption.All;

    public IReadOnlyList<SubtitleModeOption> SubtitleModes { get; } = SubtitleModeOption.All;

    public bool HasSubtitles => !string.IsNullOrWhiteSpace(SubtitlePath);

    /// <summary>Только имя файла: полный путь в узкой панели всё равно не помещается.</summary>
    public string SubtitleFileName =>
        string.IsNullOrWhiteSpace(SubtitlePath) ? string.Empty : Path.GetFileName(SubtitlePath);

    /// <summary>Доступное железо для выбранного кодека: что нашлось в этой сборке ffmpeg.</summary>
    public ObservableCollection<HardwareAcceleration> HardwareOptions { get; } = [];

    public bool HasHardwareChoice => HardwareOptions.Count > 1;

    public IReadOnlyList<FitModeOption> FitModes { get; } = FitModeOption.All;

    public ObservableCollection<AudioCodec> AvailableAudioCodecs { get; } = [];

    public IReadOnlyList<int> AudioBitrates { get; } = AudioSettings.BitratePresetsKbps;

    public IReadOnlyList<int> SampleRates { get; } = AudioSettings.SampleRatePresetsHz;

    public IReadOnlyList<int> ChannelOptions { get; } = [1, 2];

    public ObservableCollection<SummaryLine> SummaryLines { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>
    /// Контейнеры одним списком, звуковые в конце. Отдельного переключателя
    /// «только звук» нет намеренно: выбрав MP3, пользователь уже сказал всё,
    /// что нужно, а два органа с одним смыслом рано или поздно разойдутся.
    /// </summary>
    public IReadOnlyList<ContainerFormat> Containers { get; } =
    [
        ContainerFormat.Mp4, ContainerFormat.WebM, ContainerFormat.Mov,
        ContainerFormat.Mkv, ContainerFormat.Avi,
        ContainerFormat.Mp3, ContainerFormat.M4a, ContainerFormat.Wav
    ];

    public ObservableCollection<VideoCodec> VideoCodecs { get; } = [];

    /// <summary>Выбран звуковой контейнер: настройки картинки к результату не относятся.</summary>
    public bool IsAudioOnly => Container.IsAudioOnly();

    /// <summary>Обратное — чтобы разметка прятала блоки картинки без конвертера-отрицания.</summary>
    public bool HasVideoOutput => !IsAudioOnly;

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
        OnPropertyChanged(nameof(IsAudioOnly));
        OnPropertyChanged(nameof(HasVideoOutput));

        // Звук — единственное, что попадёт в файл: выключенный звук оставил бы
        // пустой контейнер, и кнопка экспорта делала бы бессмыслицу.
        if (value.IsAudioOnly())
        {
            KeepAudio = true;
        }

        if (!string.IsNullOrEmpty(OutputPath))
        {
            OutputPath = Path.ChangeExtension(OutputPath, value.FileExtension());
        }

        RefreshSummary();
    }

    partial void OnVideoCodecChanged(VideoCodec value)
    {
        Crf = RateControlPolicy.CrfRange(value).Default;
        RefreshSummary();
    }

    partial void OnResolutionChanged(ResolutionOption value)
    {
        OnPropertyChanged(nameof(IsCustomResolution));
        RefreshSummary();
    }

    partial void OnFrameRateChanged(FrameRateOption value)
    {
        OnPropertyChanged(nameof(IsCustomFrameRate));
        RefreshSummary();
    }

    partial void OnKeepAudioChanged(bool value) => RefreshSummary();

    partial void OnPreferStreamCopyChanged(bool value) => RefreshSummary();

    partial void OnQualityModeChanged(QualityModeOption value)
    {
        // Дефолт CRF зависит от кодека: у H.265 и VP9 та же цифра означает другое качество.
        Crf = RateControlPolicy.CrfRange(VideoCodec).Default;
        RefreshSummary();
    }

    partial void OnCrfChanged(int value) => RefreshSummary();

    partial void OnBitrateKbpsChanged(int value) => RefreshSummary();

    partial void OnTargetSizeMegabytesChanged(double value) => RefreshSummary();

    partial void OnCustomWidthChanged(int value) => RefreshSummary();

    partial void OnCustomHeightChanged(int value) => RefreshSummary();

    partial void OnCustomFpsChanged(double value) => RefreshSummary();

    partial void OnFitModeChanged(FitModeOption value) => RefreshSummary();

    partial void OnAudioCodecChanged(AudioCodec value) => RefreshSummary();

    partial void OnAudioBitrateKbpsChanged(int value) => RefreshSummary();

    partial void OnAudioSampleRateHzChanged(int value) => RefreshSummary();

    partial void OnAudioChannelsChanged(int value) => RefreshSummary();

    partial void OnMasterVolumePercentChanged(double value) => RefreshSummary();

    partial void OnEncodingSpeedChanged(EncodingSpeedOption value) => RefreshSummary();

    partial void OnTwoPassChanged(bool value) => RefreshSummary();

    partial void OnPixelFormatChanged(string value) => RefreshSummary();

    partial void OnKeyframeIntervalFramesChanged(int value) => RefreshSummary();

    partial void OnHardwareChanged(HardwareAcceleration value) => RefreshSummary();

    partial void OnSubtitlePathChanged(string? value) => RefreshSummary();

    partial void OnSubtitleModeChanged(SubtitleModeOption value) => RefreshSummary();

    /// <summary>
    /// Список железа зависит от кодека: у VP9 аппаратного энкодера нет вовсе,
    /// а у H.264 их может быть три. Показываем только то, что реально запустится.
    /// </summary>
    private void RefreshHardware(MediaCapabilities? capabilities)
    {
        HardwareOptions.Clear();

        var available = capabilities is null
            ? [HardwareAcceleration.None]
            : Ffmpeg.Arguments.HardwareEncoders.Available(VideoCodec, capabilities);

        foreach (var option in available)
        {
            HardwareOptions.Add(option);
        }

        if (!HardwareOptions.Contains(Hardware))
        {
            Hardware = HardwareAcceleration.None;
        }

        OnPropertyChanged(nameof(HasHardwareChoice));
    }

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

        RefreshHardware(capabilities);

        if (!VideoCodecs.Contains(VideoCodec))
        {
            var previous = VideoCodec;
            VideoCodec = VideoCodecs.FirstOrDefault(CompatibilityMatrix.DefaultVideoCodec(Container));

            CodecNotice = string.Format(
                Strings.CodecReplacedNotice,
                CodecNames.DisplayName(previous),
                ContainerLabel(Container),
                CodecNames.DisplayName(VideoCodec));
        }
        else
        {
            CodecNotice = null;
        }

        AvailableAudioCodecs.Clear();
        foreach (var codec in CompatibilityMatrix.AudioCodecsFor(Container))
        {
            if (capabilities is null || Ffmpeg.Arguments.EncoderCatalog.IsAvailable(codec, capabilities))
            {
                AvailableAudioCodecs.Add(codec);
            }
        }

        if (!AvailableAudioCodecs.Contains(AudioCodec))
        {
            AudioCodec = AvailableAudioCodecs.FirstOrDefault(CompatibilityMatrix.DefaultAudioCodec(Container));
        }
    }

    private static string ContainerLabel(ContainerFormat container) => container switch
    {
        ContainerFormat.Mp4 => "MP4",
        ContainerFormat.WebM => "WebM",
        ContainerFormat.Mov => "MOV",
        ContainerFormat.Mkv => "MKV",
        ContainerFormat.Avi => "AVI",
        _ => container.ToString()
    };

    /// <summary>
    /// Загружает готовые настройки в поля панели.
    /// </summary>
    /// <remarks>
    /// Нужна пресетам: они возвращают готовый набор параметров, а панель обязана
    /// показать его целиком — иначе пользователь увидит одно, а экспортируется другое.
    /// </remarks>
    public void ApplySettings(ExportSettings settings)
    {
        Container = settings.Container;
        VideoCodec = settings.Video.Codec;
        PreferStreamCopy = settings.PreferStreamCopy;
        EncodingSpeed = EncodingSpeeds.FirstOrDefault(option => option.Speed == settings.Video.Speed)
                        ?? EncodingSpeed;

        Hardware = HardwareOptions.Contains(settings.Video.Hardware)
            ? settings.Video.Hardware
            : HardwareAcceleration.None;

        switch (settings.Video.RateControl)
        {
            case RateControl.ConstantQuality quality:
                QualityMode = QualityModes.First(mode => mode.Mode == ViewModels.QualityMode.ConstantQuality);
                Crf = quality.Crf;
                break;

            case RateControl.ConstantBitrate bitrate:
                QualityMode = QualityModes.First(mode => mode.Mode == ViewModels.QualityMode.Bitrate);
                BitrateKbps = bitrate.Kbps;
                break;

            case RateControl.TargetSize target:
                QualityMode = QualityModes.First(mode => mode.Mode == ViewModels.QualityMode.TargetSize);
                TargetSizeMegabytes = Math.Round(target.Bytes / 1024d / 1024d, 3);
                break;

            default:
                QualityMode = QualityModes.First(mode => mode.Mode == ViewModels.QualityMode.Auto);
                break;
        }

        switch (settings.Video.Resolution)
        {
            case ResolutionSpec.Custom custom:
                Resolution = ResolutionOption.Custom;
                CustomWidth = custom.Width;
                CustomHeight = custom.Height;
                FitMode = FitModes.FirstOrDefault(option => option.Mode == custom.Fit) ?? FitMode;
                break;

            case ResolutionSpec.Preset preset:
                Resolution = Resolutions.FirstOrDefault(option => option.TargetHeight == preset.TargetHeight)
                             ?? ResolutionOption.Original;
                break;

            default:
                Resolution = ResolutionOption.Original;
                break;
        }

        if (settings.Video.FrameRate is FrameRateSpec.Fixed fixedFps)
        {
            var known = FrameRates.FirstOrDefault(option => option.Fps is { } value && Math.Abs(value - fixedFps.Fps) < 0.01);

            if (known is not null)
            {
                FrameRate = known;
            }
            else
            {
                FrameRate = FrameRateOption.Custom;
                CustomFps = fixedFps.Fps;
            }
        }
        else
        {
            FrameRate = FrameRateOption.Original;
        }

        KeepAudio = settings.Audio.Enabled;

        if (settings.Audio.Enabled)
        {
            AudioCodec = settings.Audio.Codec;
            AudioBitrateKbps = settings.Audio.BitrateKbps;
            AudioSampleRateHz = settings.Audio.SampleRateHz ?? AudioSampleRateHz;
            AudioChannels = settings.Audio.Channels ?? AudioChannels;
            MasterVolumePercent = Math.Round(settings.Audio.MasterVolume * 100);
        }

        PixelFormat = settings.Video.Advanced.PixelFormat ?? string.Empty;
        TwoPass = settings.Video.Advanced.TwoPass;

        RefreshSummary();
    }

    /// <summary>
    /// Проект, урезанный до того, что просили экспортировать.
    /// </summary>
    /// <remarks>
    /// Кусок вырезается в модели, а не в аргументах ffmpeg: тогда все стратегии
    /// планировщика — копирование потоков, быстрая склейка, обычное кодирование —
    /// работают с ним без единой правки, а сводка и оценка размера считаются
    /// по той длительности, которая на самом деле уйдёт в файл.
    /// </remarks>
    private Project ScopedProject(Project project)
    {
        if (!SelectionOnly || _timeline.SelectionRange is not { } range)
        {
            return project;
        }

        return project.WithSequence(project.Sequence.Slice(range));
    }

    public ExportSettings BuildSettings() => new ExportSettings
    {
        Container = Container,
        OutputPath = OutputPath,
        PreferStreamCopy = PreferStreamCopy,
        Video = VideoSettings.Default with
        {
            Codec = VideoCodec,
            RateControl = BuildRateControl(),
            Speed = EncodingSpeed.Speed,
            Hardware = Hardware,
            Resolution = Resolution.ToSpec(CustomWidth, CustomHeight, FitMode.Mode),
            FrameRate = FrameRate.ToSpec(CustomFps),
            Advanced = BuildAdvanced()
        },
        Audio = BuildAudio(),
        Subtitles = BuildSubtitles()
    }.Normalized();

    private SubtitleSettings BuildSubtitles() => string.IsNullOrWhiteSpace(SubtitlePath)
        ? SubtitleSettings.None
        : new SubtitleSettings
        {
            FilePath = SubtitlePath,
            Mode = SubtitleMode.Mode,
            ForceStyle = SubtitleFormats.DefaultStyle
        };

    [RelayCommand]
    private void ChooseSubtitles()
    {
        if (_fileDialogService.PickSubtitleFile() is { } path)
        {
            SubtitlePath = path;
        }
    }

    [RelayCommand]
    private void ClearSubtitles() => SubtitlePath = null;

    private RateControl BuildRateControl() => QualityMode.Mode switch
    {
        ViewModels.QualityMode.ConstantQuality => new RateControl.ConstantQuality(Crf),
        ViewModels.QualityMode.Bitrate => new RateControl.ConstantBitrate(BitrateKbps),
        ViewModels.QualityMode.TargetSize => new RateControl.TargetSize((long)(TargetSizeMegabytes * 1024 * 1024)),
        _ => new RateControl.Auto()
    };

    private AdvancedVideoSettings BuildAdvanced() => new()
    {
        TwoPass = TwoPass,
        PixelFormat = string.IsNullOrWhiteSpace(PixelFormat) ? null : PixelFormat.Trim(),
        KeyframeIntervalFrames = KeyframeIntervalFrames > 0 ? KeyframeIntervalFrames : null
    };

    private AudioSettings BuildAudio()
    {
        if (!KeepAudio)
        {
            return AudioSettings.Disabled;
        }

        return AudioSettings.Default with
        {
            Codec = AudioCodec,
            BitrateKbps = AudioBitrateKbps,
            SampleRateHz = AudioSampleRateHz,
            Channels = AudioChannels,
            MasterVolume = MasterVolumePercent / 100d,
            NormalizeLoudness = NormalizeLoudness
        };
    }

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
            var plan = _planner.CreatePlan(
                new ExportRequest(ScopedProject(_project), BuildSettings()),
                _toolsetProvider.Current.Capabilities);

            var summary = plan.Summary;

            // У звукового файла ни разрешения, ни кадров в секунду нет: строки
            // про картинку в сводке обещали бы то, чего в файле не будет.
            if (!IsAudioOnly)
            {
                SummaryLines.Add(new SummaryLine(Strings.FieldResolution, summary.Resolution.ToString()));
                SummaryLines.Add(new SummaryLine(Strings.FieldFrameRate, $"{summary.Fps:0.###} fps"));
                SummaryLines.Add(new SummaryLine(Strings.FieldCodec, summary.VideoCodecLabel));
                SummaryLines.Add(new SummaryLine(Strings.FieldBitrate, summary.VideoBitrateLabel));
            }

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

        // Не IsEmpty: кусок хвоста под музыку картинки не содержит, но проверить
        // его нужно ровно так же — там чёрный кадр со звуком.
        if (sliced.Duration <= TimeSpan.Zero)
        {
            ErrorMessage = Strings.PreviewFragmentEmpty;
            return;
        }

        var previewPath = Path.Combine(
            _paths.PreviewDirectory,
            $"фрагмент-{DateTime.Now:HH-mm-ss}{Container.FileExtension()}");

        Directory.CreateDirectory(_paths.PreviewDirectory);

        var baseSettings = BuildSettings();

        var settings = baseSettings with
        {
            OutputPath = previewPath,
            Overwrite = OverwritePolicy.Overwrite,

            // Проверка должна быть быстрой: качество здесь оценивается на глаз,
            // а ждать медленный пресет ради пяти секунд бессмысленно.
            Video = baseSettings.Video with
            {
                Speed = Core.Export.EncodingSpeed.VeryFast,
                Advanced = baseSettings.Video.Advanced with { TwoPass = false }
            }
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
                new ExportRequest(ScopedProject(_project), BuildSettings()),
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
        _project is not null &&
        State != ExportState.Running &&
        !string.IsNullOrWhiteSpace(OutputPath) &&

        // Кусок без выделения экспортировать некуда: кнопка гаснет, а не выдаёт
        // весь ролик вместо запрошенного отрезка.
        (!SelectionOnly || HasSelectionRange);

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
