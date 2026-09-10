using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeowsCut.App.Timeline;

/// <summary>
/// Кадр на полосе клипа: где нарисовать и чем закрасить.
/// </summary>
/// <remarks>
/// Хранится готовая кисть, а не картинка: она заполняет ячейку с обрезкой, поэтому
/// кадры не растягиваются по вертикали, и её не нужно создавать при каждой перерисовке.
/// </remarks>
public sealed record ClipThumbnail(double X, double Width, Brush Brush);

/// <summary>
/// Загруженные кадры в памяти.
/// </summary>
/// <remarks>
/// Файлы кэша лежат на диске, но перечитывать их при каждой перерисовке нельзя —
/// таймлайн рисуется десятки раз в секунду. Картинки заморожены, поэтому их можно
/// готовить в фоновом потоке и рисовать в UI-потоке без копирования.
/// </remarks>
public sealed class ThumbnailImageCache
{
    private const int Capacity = 400;

    private readonly ConcurrentDictionary<string, BitmapSource> _images = new();
    private readonly ConcurrentQueue<string> _order = new();

    public BitmapSource? TryGet(string path) => _images.GetValueOrDefault(path);

    /// <summary>Кисть, заполняющая ячейку кадром с обрезкой по центру.</summary>
    public Brush? LoadBrush(string path)
    {
        var image = Load(path);
        if (image is null)
        {
            return null;
        }

        var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        brush.Freeze();

        return brush;
    }

    public BitmapSource? Load(string path)
    {
        if (_images.TryGetValue(path, out var cached))
        {
            return cached;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // файл не остаётся заблокированным
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();

            _images[path] = image;
            _order.Enqueue(path);
            TrimIfNeeded();

            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            // Битый или недописанный кадр — просто не показываем его.
            return null;
        }
    }

    private void TrimIfNeeded()
    {
        while (_images.Count > Capacity && _order.TryDequeue(out var oldest))
        {
            _images.TryRemove(oldest, out _);
        }
    }
}
