using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Formatting;
using MeowsCut.App.Services;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Editing;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Предпросмотр: кадр последовательности под курсором.
/// </summary>
/// <remarks>
/// Пока это стоп-кадр, а не воспроизведение: плеер появится на следующем этапе.
/// Кадр берётся у того же сервиса миниатюр, что и полоса таймлайна, поэтому
/// перемотка по уже просмотренным местам мгновенная.
/// </remarks>
public sealed partial class PreviewViewModel : ObservableObject
{
    private const int FrameWidth = 720;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(80);

    private readonly IThumbnailService _thumbnails;
    private readonly ThumbnailImageCache _cache;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimelineViewModel _timeline;

    private CancellationTokenSource? _cancellation;
    private Project? _project;

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

        _timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TimelineViewModel.Playhead))
            {
                OnPlayheadChanged();
            }
        };

        _timeline.SequenceChanged += (_, _) => OnPlayheadChanged();
    }

    [ObservableProperty]
    private BitmapSource? _frame;

    [ObservableProperty]
    private string _positionText = "00:00.00";

    [ObservableProperty]
    private string _durationText = "00:00.00";

    public void Attach(Project project)
    {
        _project = project;
        DurationText = DisplayFormat.Duration(project.Sequence.Duration);
        OnPlayheadChanged();
    }

    public void Detach()
    {
        _cancellation?.Cancel();
        _project = null;
        Frame = null;
        PositionText = DurationText = "00:00.00";
    }

    /// <summary>Шаг по времени: стрелки на клавиатуре и кнопки транспорта.</summary>
    [RelayCommand]
    private void Step(double seconds)
    {
        var target = _timeline.Playhead + TimeSpan.FromSeconds(seconds);
        _timeline.Playhead = target < TimeSpan.Zero
            ? TimeSpan.Zero
            : target > _timeline.Duration ? _timeline.Duration : target;
    }

    [RelayCommand]
    private void GoToStart() => _timeline.Playhead = TimeSpan.Zero;

    [RelayCommand]
    private void GoToEnd() => _timeline.Playhead = _timeline.Duration;

    private void OnPlayheadChanged()
    {
        PositionText = DisplayFormat.Duration(_timeline.Playhead);
        DurationText = DisplayFormat.Duration(_timeline.Duration);

        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();

        _ = RefreshFrameAsync(_timeline.Playhead, _cancellation.Token);
    }

    private async Task RefreshFrameAsync(TimeSpan playhead, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Debounce, cancellationToken).ConfigureAwait(false);

            if (_project is null)
            {
                return;
            }

            // Плейхед в конце последовательности не показывает ничего — берём последний кадр.
            var lookup = _timeline.Sequence.Resolve(playhead)
                         ?? _timeline.Sequence.Resolve(playhead - TimeSpan.FromMilliseconds(40));

            if (lookup is not { } resolved)
            {
                return;
            }

            var source = _project.Find(resolved.Clip.SourceId);
            if (source is null)
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
