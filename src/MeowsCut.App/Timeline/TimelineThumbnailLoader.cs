using MeowsCut.App.Services;
using MeowsCut.App.ViewModels;
using MeowsCut.Core.Abstractions;

namespace MeowsCut.App.Timeline;

/// <summary>
/// Подгружает кадры для видимой части таймлайна.
/// </summary>
/// <remarks>
/// Кадры запрашиваются только для того, что реально видно, и только после короткой
/// паузы: при перетаскивании ползунка масштаба иначе улетают сотни запусков ffmpeg,
/// результат которых никто не увидит. Предыдущая пачка при этом отменяется.
/// </remarks>
public sealed class TimelineThumbnailLoader(
    IThumbnailService thumbnails,
    ThumbnailImageCache cache,
    IUiDispatcher dispatcher)
{
    private const double SlotWidth = 96d;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(120);

    private CancellationTokenSource? _cancellation;
    private TimelineViewModel? _timeline;

    public void Attach(TimelineViewModel timeline)
    {
        Detach();

        _timeline = timeline;
        timeline.ThumbnailsRequested += OnThumbnailsRequested;
    }

    public void Detach()
    {
        if (_timeline is not null)
        {
            _timeline.ThumbnailsRequested -= OnThumbnailsRequested;
        }

        _cancellation?.Cancel();
        _timeline = null;
    }

    private void OnThumbnailsRequested(object? sender, EventArgs e)
    {
        if (_timeline is null)
        {
            return;
        }

        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();

        _ = RefreshAsync(_timeline, _cancellation.Token);
    }

    private async Task RefreshAsync(TimelineViewModel timeline, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Debounce, cancellationToken).ConfigureAwait(false);

            var metrics = timeline.Metrics;
            var viewportWidth = metrics.ViewportWidth;

            foreach (var clip in timeline.Clips.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (clip.SourcePath is null)
                {
                    continue;
                }

                var left = Math.Max(0d, metrics.TimeToX(clip.Start));
                var right = Math.Min(viewportWidth, metrics.TimeToX(clip.End));

                if (right - left < 24d)
                {
                    // Клип уже сжат в полоску — кадры в нём всё равно не разглядеть.
                    clip.Thumbnails = [];
                    continue;
                }

                var slots = new List<ClipThumbnail>();

                for (var x = left; x < right; x += SlotWidth)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var width = Math.Min(SlotWidth, right - x);
                    var centre = metrics.XToTime(x + (width / 2));
                    var sourceTime = clip.SourceTimeAt(centre);

                    var path = await thumbnails
                        .GetFrameAsync(clip.SourcePath, sourceTime, (int)SlotWidth * 2, cancellationToken)
                        .ConfigureAwait(false);

                    if (path is null)
                    {
                        continue;
                    }

                    var brush = cache.LoadBrush(path);
                    if (brush is null)
                    {
                        continue;
                    }

                    slots.Add(new ClipThumbnail(x, width, brush));

                    // Кадры показываем по мере готовности: ждать всю полосу
                    // на длинном клипе пришлось бы несколько секунд.
                    clip.Thumbnails = slots.ToArray();
                    dispatcher.Post(timeline.NotifyVisualInvalidated);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Обычное дело: пользователь продолжил двигать масштаб.
        }
    }
}
