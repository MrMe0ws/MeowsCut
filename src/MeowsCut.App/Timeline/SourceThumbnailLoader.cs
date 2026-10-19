using MeowsCut.App.Services;
using MeowsCut.App.ViewModels;
using MeowsCut.Core.Abstractions;

namespace MeowsCut.App.Timeline;

/// <summary>
/// Кадры-обложки для панели медиа.
/// </summary>
/// <remarks>
/// По имени вида PXL_20261018_065200711 файл не узнать — узнают его по картинке.
/// Фотография уменьшается прямо при распаковке, у видео берётся кадр с первой
/// секунды: самый первый кадр часто чёрный. Всё в фоне и по одному, чтобы
/// десяток файлов не запускал десяток ffmpeg разом.
/// </remarks>
public sealed class SourceThumbnailLoader(
    IThumbnailService thumbnails,
    ThumbnailImageCache cache,
    IUiDispatcher dispatcher)
{
    private const int Width = 320;

    private CancellationTokenSource? _cancellation;

    public void Load(IReadOnlyList<ProjectFileViewModel> files)
    {
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();

        var pending = files.Where(file => file.Thumbnail is null && !file.IsAudio).ToArray();
        if (pending.Length > 0)
        {
            _ = LoadAsync(pending, _cancellation.Token);
        }
    }

    private async Task LoadAsync(IReadOnlyList<ProjectFileViewModel> files, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var image = file.IsImage
                    ? await Task.Run(() => cache.Load(file.Path, Width), cancellationToken).ConfigureAwait(false)
                    : await LoadVideoFrameAsync(file, cancellationToken).ConfigureAwait(false);

                if (image is not null)
                {
                    dispatcher.Post(() => file.Thumbnail = image);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Проект сменился, пока кадры готовились: они уже никому не нужны.
        }
    }

    private async Task<System.Windows.Media.Imaging.BitmapSource?> LoadVideoFrameAsync(
        ProjectFileViewModel file,
        CancellationToken cancellationToken)
    {
        var position = TimeSpan.FromSeconds(Math.Min(1d, file.Source.Duration.TotalSeconds / 2));
        var path = await thumbnails.GetFrameAsync(file.Path, position, Width, cancellationToken).ConfigureAwait(false);

        return path is null ? null : cache.Load(path);
    }
}
