using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Formatting;
using MeowsCut.App.Playback;
using MeowsCut.App.Services;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Editing;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Предпросмотр: воспроизведение собранной последовательности.
/// </summary>
/// <remarks>
/// Во время воспроизведения кадры даёт системный проигрыватель, при перемотке —
/// он же в режиме паузы. Если файл системе не по зубам, показываются кадры,
/// извлечённые ffmpeg: лучше медленный предпросмотр, чем чёрный прямоугольник.
/// </remarks>
public sealed partial class PreviewViewModel : ObservableObject
{
    private const int FrameWidth = 720;
    private static readonly TimeSpan FrameDebounce = TimeSpan.FromMilliseconds(80);

    private readonly IThumbnailService _thumbnails;
    private readonly ThumbnailImageCache _cache;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimelineViewModel _timeline;
    private readonly MediaElementPlayer _player;
    private readonly SequencePlaybackController _playback;
    private readonly DispatcherTimer _timer;

    private CancellationTokenSource? _cancellation;
    private Project? _project;
    private bool _syncingPlayhead;

    public PreviewViewModel(
        IThumbnailService thumbnails,
        ThumbnailImageCache cache,
        IUiDispatcher dispatcher,
        TimelineViewModel timeline)
    {
        _thumbnails = thumbnails;
        _cache = cache;
        _dispatcher = dispatcher;
        _timeline = timeline;

        _player = new MediaElementPlayer();
        _playback = new SequencePlaybackController(_player);

        // Шаг воспроизведения дёргается таймером интерфейса: сам контроллер
        // остаётся проверяемым без диспетчера и реального времени.
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };

        _timer.Tick += (_, _) => _playback.Tick();

        _playback.PositionChanged += OnPlaybackPositionChanged;
        _playback.StateChanged += (_, _) => _dispatcher.Post(RefreshPlaybackState);

        _timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TimelineViewModel.Playhead))
            {
                OnPlayheadMoved();
            }
        };

        _timeline.SequenceChanged += (_, sequence) =>
        {
            _playback.UpdateSequence(sequence);
            RefreshTexts();
            RequestFrame();
        };
    }

    /// <summary>Визуальный элемент проигрывателя для размещения в разметке.</summary>
    public FrameworkElement PlayerVisual => _player.Visual;

    [ObservableProperty]
    private BitmapSource? _frame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerVisible))]
    private bool _useFallbackFrames;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string _positionText = "00:00.00";

    [ObservableProperty]
    private string _durationText = "00:00.00";

    public bool IsPlayerVisible => !UseFallbackFrames;

    public void Attach(Project project)
    {
        _project = project;
        _playback.Attach(project);
        _timer.Start();

        RefreshTexts();
        RequestFrame();
    }

    public void Detach()
    {
        _timer.Stop();
        _cancellation?.Cancel();
        _playback.Detach();

        _project = null;
        Frame = null;
        UseFallbackFrames = false;
        IsPlaying = false;
        PositionText = DurationText = "00:00.00";
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (UseFallbackFrames)
        {
            return;
        }

        _playback.TogglePlay();
        RefreshPlaybackState();
    }

    [RelayCommand]
    private void Step(double seconds)
    {
        _playback.Pause();

        var target = _timeline.Playhead + TimeSpan.FromSeconds(seconds);
        _timeline.Playhead = target < TimeSpan.Zero
            ? TimeSpan.Zero
            : target > _timeline.Duration ? _timeline.Duration : target;
    }

    [RelayCommand]
    private void GoToStart() => _timeline.Playhead = TimeSpan.Zero;

    [RelayCommand]
    private void GoToEnd() => _timeline.Playhead = _timeline.Duration;

    /// <summary>Плейхед двигал пользователь — перематываем проигрыватель.</summary>
    private void OnPlayheadMoved()
    {
        RefreshTexts();

        if (_syncingPlayhead)
        {
            return;
        }

        _playback.Seek(_timeline.Playhead);
        RequestFrame();
    }

    /// <summary>Позицию сдвинуло воспроизведение — двигаем плейхед, не зациклившись.</summary>
    private void OnPlaybackPositionChanged(object? sender, TimeSpan position) =>
        _dispatcher.Post(() =>
        {
            _syncingPlayhead = true;
            _timeline.Playhead = position;
            _syncingPlayhead = false;

            RefreshTexts();
        });

    private void RefreshPlaybackState()
    {
        IsPlaying = _playback.IsPlaying;
        UseFallbackFrames = _playback.IsPlayerUnavailable;

        if (UseFallbackFrames)
        {
            RequestFrame();
        }
    }

    private void RefreshTexts()
    {
        PositionText = DisplayFormat.Duration(_timeline.Playhead);
        DurationText = DisplayFormat.Duration(_timeline.Duration);
    }

    /// <summary>
    /// Кадр из ffmpeg. Нужен, когда системный проигрыватель не открыл файл;
    /// в обычном режиме кадр показывает он сам.
    /// </summary>
    private void RequestFrame()
    {
        if (!UseFallbackFrames)
        {
            return;
        }

        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();

        _ = RefreshFrameAsync(_timeline.Playhead, _cancellation.Token);
    }

    private async Task RefreshFrameAsync(TimeSpan playhead, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FrameDebounce, cancellationToken).ConfigureAwait(false);

            if (_project is null)
            {
                return;
            }

            var lookup = _timeline.Sequence.Resolve(playhead)
                         ?? _timeline.Sequence.Resolve(playhead - TimeSpan.FromMilliseconds(40));

            if (lookup is not { } resolved || _project.Find(resolved.Clip.SourceId) is not { } source)
            {
                return;
            }

            var path = await _thumbnails
                .GetFrameAsync(source.FilePath, resolved.SourceTime, FrameWidth, cancellationToken)
                .ConfigureAwait(false);

            if (path is null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var image = _cache.Load(path);
            if (image is not null)
            {
                _dispatcher.Post(() => Frame = image);
            }
        }
        catch (OperationCanceledException)
        {
            // Пользователь продолжил двигать плейхед — этот кадр уже не нужен.
        }
    }
}
