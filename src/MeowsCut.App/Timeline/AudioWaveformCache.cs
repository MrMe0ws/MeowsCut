using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeowsCut.App.Services;
using MeowsCut.Core.Abstractions;

namespace MeowsCut.App.Timeline;

/// <summary>
/// Волны звука, готовые к отрисовке.
/// </summary>
/// <remarks>
/// Доска рисуется десятки раз в секунду и ждать в ней нельзя, поэтому запрос волны
/// возвращает то, что уже есть, а недостающее считает в фоне и сообщает событием.
/// Волна строится на весь файл целиком, а кусок берётся из неё через
/// <see cref="ImageBrush.Viewbox"/> — обрезка куска не требует нового прохода ffmpeg.
/// </remarks>
public sealed class AudioWaveformCache(IWaveformService waveforms, IUiDispatcher dispatcher)
{
    private readonly ConcurrentDictionary<string, BitmapSource?> _images = new();
    private readonly ConcurrentDictionary<string, byte> _loading = new();

    /// <summary>Готова очередная волна — доске пора перерисоваться.</summary>
    public event EventHandler? Ready;

    /// <summary>
    /// Кисть с куском волны от <paramref name="from"/> до <paramref name="to"/>
    /// (доли длительности файла). null — волна ещё не готова или её не построить.
    /// </summary>
    public Brush? Brush(string sourcePath, double from, double to)
    {
        var image = Get(sourcePath);
        if (image is null)
        {
            return null;
        }

        var width = Math.Clamp(to - from, 0.0001, 1d);
        var left = Math.Clamp(from, 0d, 1d - width);

        return new ImageBrush(image)
        {
            Viewbox = new System.Windows.Rect(left, 0, width, 1),
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Stretch = Stretch.Fill
        };
    }

    private BitmapSource? Get(string sourcePath)
    {
        if (_images.TryGetValue(sourcePath, out var cached))
        {
            return cached;
        }

        // Ключ занимается до запуска задачи: перерисовка успевает случиться
        // несколько раз, пока ffmpeg идёт по файлу.
        if (_loading.TryAdd(sourcePath, 0))
        {
            _ = LoadAsync(sourcePath);
        }

        return null;
    }

    private async Task LoadAsync(string sourcePath)
    {
        try
        {
            var path = await waveforms
                .GetWaveformAsync(sourcePath, CancellationToken.None)
                .ConfigureAwait(false);

            // null запоминается тоже: файл без звука не должен опрашиваться заново
            // при каждой перерисовке доски.
            _images[sourcePath] = path is null ? null : LoadImage(path);

            dispatcher.Post(() => Ready?.Invoke(this, EventArgs.Empty));
        }
        finally
        {
            _loading.TryRemove(sourcePath, out _);
        }
    }

    private static BitmapSource? LoadImage(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // файл не остаётся заблокированным
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            // Битая или недописанная картинка — просто рисуем кусок без волны.
            return null;
        }
    }
}
