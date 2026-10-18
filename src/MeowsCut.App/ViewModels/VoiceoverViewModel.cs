using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Recording;
using MeowsCut.App.Services;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

public enum VoiceoverState
{
    Idle = 0,

    /// <summary>Микрофон включается — ролик ещё стоит.</summary>
    Starting,

    Recording,

    /// <summary>Файл дописывается и ложится на доску.</summary>
    Saving
}

/// <summary>
/// Озвучка: ролик играет с плейхеда, микрофон пишет, дубль ложится на доску.
/// </summary>
/// <remarks>
/// Порядок важен: сначала микрофон, потом ролик. Микрофон включается до сотен
/// миллисекунд, и запусти ролик раньше — начало реплики пришлось бы на тишину.
/// Лишнее в начале файла потом отрезается по <see cref="PlaybackOrigin"/>.
/// Сам проект модель не трогает: файл добавляет оболочка, на доску его кладёт
/// доска — через историю правок, как и всё остальное.
/// </remarks>
public sealed partial class VoiceoverViewModel : ObservableObject
{
    private readonly IVoiceRecorder _recorder;
    private readonly IAppSettingsStore _settings;
    private readonly PreviewViewModel _preview;
    private readonly TimelineViewModel _timeline;
    private readonly IDialogService _dialogs;
    private readonly IErrorPresenter _errors;
    private readonly IUiDispatcher _dispatcher;
    private readonly AppPaths _paths;
    private readonly ILogger<VoiceoverViewModel> _logger;
    private readonly DispatcherTimer _meter;

    private IVoiceRecording? _recording;
    private PlaybackOrigin? _origin;
    private CancellationTokenSource? _starting;
    private TimeSpan _start;

    public VoiceoverViewModel(
        IVoiceRecorder recorder,
        IAppSettingsStore settings,
        PreviewViewModel preview,
        TimelineViewModel timeline,
        IDialogService dialogs,
        IErrorPresenter errors,
        IUiDispatcher dispatcher,
        AppPaths paths,
        ILogger<VoiceoverViewModel> logger)
    {
        _recorder = recorder;
        _settings = settings;
        _preview = preview;
        _timeline = timeline;
        _dialogs = dialogs;
        _errors = errors;
        _dispatcher = dispatcher;
        _paths = paths;
        _logger = logger;

        _meter = new DispatcherTimer(DispatcherPriority.Render) { Interval = MeterInterval };
        _meter.Tick += (_, _) => OnMeterTick();

        _timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TimelineViewModel.HasProject))
            {
                ToggleRecordCommand.NotifyCanExecuteChanged();
            }
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecording))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private VoiceoverState _state;

    public bool IsRecording => State == VoiceoverState.Recording;

    /// <summary>Микрофон включается или запись сохраняется — на это время виден индикатор.</summary>
    public bool IsWaiting => State is VoiceoverState.Starting or VoiceoverState.Saving;

    /// <summary>Идёт что-то из записи — показывается полоса озвучки вместо обычных кнопок.</summary>
    public bool IsActive => State != VoiceoverState.Idle;

    /// <summary>
    /// Добавляет записанный файл в проект и отдаёт обновлённый проект с источником.
    /// Ставит оболочка: проект живёт у неё. null — файл добавить не удалось,
    /// об ошибке оболочка сообщила сама.
    /// </summary>
    public Func<string, CancellationToken, Task<(Project Project, MediaSource Source)?>>? ImportRecording { get; set; }

    /// <summary>Папка сохранённого черновика: записи кладутся рядом с ним.</summary>
    public Func<string?>? ProjectFolder { get; set; }

    /// <summary>Можно ли сейчас начинать: приложение не занято другим делом.</summary>
    public Func<bool>? CanStartNow { get; set; }

    /// <summary>R и кнопка «Запись»: начать дубль или закончить идущий.</summary>
    [RelayCommand(CanExecute = nameof(CanToggleRecord))]
    private async Task ToggleRecordAsync()
    {
        if (State == VoiceoverState.Recording)
        {
            await FinishAsync().ConfigureAwait(true);
        }
        else if (State == VoiceoverState.Idle)
        {
            await StartAsync().ConfigureAwait(true);
        }
    }

    private bool CanToggleRecord() =>
        State == VoiceoverState.Recording ||
        (State == VoiceoverState.Idle && _timeline.HasProject && (CanStartNow?.Invoke() ?? true));

    /// <summary>Пересчитать доступность кнопки — оболочка зовёт, когда освобождается.</summary>
    public void RefreshAvailability() => ToggleRecordCommand.NotifyCanExecuteChanged();

    /// <summary>Esc: выбросить дубль, ничего не кладя на доску.</summary>
    [RelayCommand(CanExecute = nameof(IsActive))]
    private async Task CancelAsync()
    {
        if (State == VoiceoverState.Starting)
        {
            _starting?.Cancel();
            return;
        }

        if (State != VoiceoverState.Recording || _recording is not { } recording)
        {
            return;
        }

        StopMonitoring();
        _recording = null;
        State = VoiceoverState.Saving;

        await recording.DisposeAsync().ConfigureAwait(true);

        _logger.LogInformation("Дубль озвучки выброшен");
        ResetToIdle();
    }

    /// <summary>Проект закрывают или выходят из программы — незаконченный дубль выбрасывается.</summary>
    public async Task AbortAsync()
    {
        _starting?.Cancel();

        if (_recording is { } recording)
        {
            StopMonitoring();
            _recording = null;
            await recording.DisposeAsync().ConfigureAwait(true);
        }

        ResetToIdle();
    }
}
