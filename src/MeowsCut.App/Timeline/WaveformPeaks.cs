using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeowsCut.App.Timeline;

/// <summary>
/// Громкость звука по столбцам: доля от 0 до 1 на каждый столбец картинки волны.
/// </summary>
/// <remarks>
/// Доска рисует волну столбиками в цвете дорожки, а не готовой картинкой:
/// картинку не перекрасить под тему и озвучку, а столбики считываются
/// с одного взгляда даже на узкой полосе.
/// Пики снимаются с картинки showwavespic — она симметрична относительно
/// середины, и высота закрашенного в столбце и есть громкость.
/// </remarks>
public sealed class WaveformPeaks
{
    /// <summary>Прозрачность, ниже которой пиксель считается фоном: края линий сглажены.</summary>
    private const byte AlphaThreshold = 24;

    private readonly float[] _columns;

    public WaveformPeaks(float[] columns) => _columns = columns;

    public int Count => _columns.Length;

    /// <summary>
    /// Самый громкий столбец на отрезке <paramref name="from"/>–<paramref name="to"/>
    /// (доли длительности файла). Берётся максимум, а не среднее: короткий удар
    /// на сжатой доске не должен пропадать.
    /// </summary>
    public double Max(double from, double to)
    {
        if (_columns.Length == 0)
        {
            return 0;
        }

        var first = Math.Clamp((int)(from * _columns.Length), 0, _columns.Length - 1);
        var last = Math.Clamp((int)Math.Ceiling(to * _columns.Length), first + 1, _columns.Length);

        var peak = 0f;
        for (var i = first; i < last; i++)
        {
            peak = Math.Max(peak, _columns[i]);
        }

        return peak;
    }

    public static WaveformPeaks FromImage(BitmapSource image)
    {
        var bitmap = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var columns = new float[width];
        var middle = height / 2d;

        for (var x = 0; x < width; x++)
        {
            var top = -1;
            var bottom = -1;

            for (var y = 0; y < height; y++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] < AlphaThreshold)
                {
                    continue;
                }

                if (top < 0)
                {
                    top = y;
                }

                bottom = y;
            }

            columns[x] = top < 0 ? 0f : (float)Math.Clamp(Math.Max(middle - top, bottom + 1 - middle) / middle, 0d, 1d);
        }

        return new WaveformPeaks(columns);
    }
}
