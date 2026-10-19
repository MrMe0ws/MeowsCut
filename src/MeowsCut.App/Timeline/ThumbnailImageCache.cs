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

    public BitmapSource? Load(string path) => Load(path, decodeWidth: 0);

    /// <summary>Уже готовая картинка нужной ширины — без чтения с диска.</summary>
    public BitmapSource? TryGet(string path, int decodeWidth) => _images.GetValueOrDefault(Key(path, decodeWidth));

    /// <summary>
    /// Картинка, уменьшенная при декодировании до <paramref name="decodeWidth"/> точек.
    /// </summary>
    /// <remarks>
    /// Для фотографий в предпросмотре: снимок телефона — 4000×3000, это 48 МБ
    /// в памяти и треть секунды декодирования, хотя на экране он занимает
    /// меньше тысячи точек. JPEG уменьшается прямо при распаковке, и это
    /// в разы быстрее, чем распаковать целиком. 0 — без уменьшения.
    /// </remarks>
    public BitmapSource? Load(string path, int decodeWidth)
    {
        var key = Key(path, decodeWidth);

        if (_images.TryGetValue(key, out var cached))
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

            // Только уменьшаем: скриншот шириной меньше заданной
            // растянулся бы при распаковке и занял бы больше памяти, а не меньше.
            if (decodeWidth > 0 && SourceWidth(path) > decodeWidth)
            {
                image.DecodePixelWidth = decodeWidth;
            }

            image.EndInit();
            image.Freeze();

            _images[key] = image;
            _order.Enqueue(key);
            TrimIfNeeded();

            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or FileFormatException)
        {
            // Битый или недописанный кадр — просто не показываем его.
            return null;
        }
    }

    /// <summary>Ширина картинки по заголовку файла — без распаковки самих точек.</summary>
    private static int SourceWidth(string path)
    {
        var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        return decoder.Frames.Count > 0 ? decoder.Frames[0].PixelWidth : 0;
    }

    private static string Key(string path, int decodeWidth) =>
        decodeWidth > 0 ? $"{path}@{decodeWidth}" : path;

    private void TrimIfNeeded()
    {
        while (_images.Count > Capacity && _order.TryDequeue(out var oldest))
        {
            _images.TryRemove(oldest, out _);
        }
    }
}
