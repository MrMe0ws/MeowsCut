using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeowsCut.App.Timeline;

/// <summary>
/// Курсоры инструментов доски монтажа.
/// </summary>
/// <remarks>
/// Рисуются из тех же контуров, что и значки на кнопках, поэтому кнопка и курсор
/// не разъезжаются. WPF принимает курсор только потоком в формате .cur, готового
/// файла у нас нет — картинка собирается в память здесь же.
/// </remarks>
public static class ToolCursors
{
    private const int Size = 32;

    /// <summary>Система координат значков — 24×24, плюс запас на обводку.</summary>
    private const double IconBox = 26d;

    private static Cursor? _razor;

    /// <summary>Ножницы. Остриё курсора — точка, где сходятся лезвия.</summary>
    public static Cursor Razor => _razor ??= Build("Icon.Scissors", new Point(12, 12)) ?? Cursors.Cross;

    private static Cursor? Build(string geometryKey, Point hotspot)
    {
        if (Application.Current?.TryFindResource(geometryKey) is not Geometry geometry)
        {
            return null;
        }

        try
        {
            var scale = Size / IconBox;
            var pixels = Render(geometry, scale);
            var hot = new Point(Math.Round(hotspot.X * scale + 1), Math.Round(hotspot.Y * scale + 1));

            return new Cursor(new MemoryStream(Pack(pixels, hot)));
        }
        catch (Exception)
        {
            // Курсор — украшение: если система его не приняла, инструмент обязан работать.
            return null;
        }
    }

    private static byte[] Render(Geometry geometry, double scale)
    {
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new TranslateTransform(1, 1));
            context.PushTransform(new ScaleTransform(scale, scale));

            // Тёмная подложка под светлым рисунком: иначе курсор теряется
            // то на кадре видео, то на тёмном фоне доски.
            context.DrawGeometry(null, OutlinePen(Brushes.Black, 3.4 / scale), geometry);
            context.DrawGeometry(null, OutlinePen(Brushes.White, 1.6 / scale), geometry);

            context.Pop();
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var pixels = new byte[Size * Size * 4];
        bitmap.CopyPixels(pixels, Size * 4, 0);

        Unpremultiply(pixels);
        return pixels;
    }

    private static Pen OutlinePen(Brush brush, double thickness) =>
        new(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

    /// <summary>
    /// WPF отдаёт цвет, умноженный на прозрачность, а .cur ждёт обычный BGRA:
    /// без обратного пересчёта полупрозрачные края курсора выглядят закопчёнными.
    /// </summary>
    private static void Unpremultiply(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];

            if (alpha is 0 or 255)
            {
                continue;
            }

            for (var channel = 0; channel < 3; channel++)
            {
                pixels[i + channel] = (byte)Math.Min(255, pixels[i + channel] * 255 / alpha);
            }
        }
    }

    private static byte[] Pack(byte[] pixels, Point hotspot)
    {
        var stride = Size * 4;
        var maskStride = (Size + 31) / 32 * 4;
        var xorSize = stride * Size;
        var andSize = maskStride * Size;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)2); // тип: курсор
        writer.Write((ushort)1); // размер в файле один
        writer.Write((byte)Size);
        writer.Write((byte)Size);
        writer.Write((byte)0); // палитра не нужна
        writer.Write((byte)0);
        writer.Write((ushort)hotspot.X);
        writer.Write((ushort)hotspot.Y);
        writer.Write(40 + xorSize + andSize);
        writer.Write(22); // сразу за заголовками

        // BITMAPINFOHEADER. Высота удвоена: за картинкой идёт маска прозрачности.
        writer.Write(40);
        writer.Write(Size);
        writer.Write(Size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0); // без сжатия
        writer.Write(xorSize + andSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        // Строки BMP идут снизу вверх.
        for (var y = Size - 1; y >= 0; y--)
        {
            writer.Write(pixels, y * stride, stride);
        }

        // Маска остаётся нулевой: прозрачность берётся из альфа-канала.
        writer.Write(new byte[andSize]);

        writer.Flush();
        return stream.ToArray();
    }
}
