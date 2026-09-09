using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeowsCut.App.Services;

/// <summary>
/// Сохраняет содержимое окна в PNG средствами WPF — без захвата экрана.
/// Нужен для проверки интерфейса при разработке: снимок получается ровно таким,
/// каким его нарисовало приложение, и в кадр не попадает ничего постороннего.
/// Запуск: MeowsCut.exe --screenshot путь\к\файлу.png
/// </summary>
public static class WindowSnapshot
{
    private const string Argument = "--screenshot";

    /// <summary>
    /// Возвращает путь для снимка, если приложение запущено в этом режиме.
    /// </summary>
    public static string? GetRequestedPath(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], Argument, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    public static void Save(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var target = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        target.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
