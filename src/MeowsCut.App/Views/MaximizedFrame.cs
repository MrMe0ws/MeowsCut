using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MeowsCut.App.Views;

/// <summary>
/// Насколько развёрнутое окно ушло за край рабочей области.
/// </summary>
/// <remarks>
/// Windows разворачивает окно с рамкой, сдвинутой за экран. У окна со своим
/// заголовком это срезает верх строки меню. <see cref="SystemParameters.WindowResizeBorderThickness"/>
/// не учитывает добавочную рамку Windows 10/11 и разный масштаб мониторов —
/// отступ по нему получался на 3–4 пикселя меньше, и кнопки прилипали к краю.
/// Поэтому вылет меряется напрямую: прямоугольник окна против рабочей области его монитора.
/// </remarks>
internal static class MaximizedFrame
{
    private const int MonitorDefaultToNearest = 2;

    public static Thickness Overhang(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;

        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds))
        {
            return Fallback;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (!GetMonitorInfo(MonitorFromWindow(handle, MonitorDefaultToNearest), ref info))
        {
            return Fallback;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var work = info.Work;

        var overhang = new Thickness(
            Math.Max(0, work.Left - bounds.Left) / dpi.DpiScaleX,
            Math.Max(0, work.Top - bounds.Top) / dpi.DpiScaleY,
            Math.Max(0, bounds.Right - work.Right) / dpi.DpiScaleX,
            Math.Max(0, bounds.Bottom - work.Bottom) / dpi.DpiScaleY);

        // Окно ещё не успело встать в развёрнутый размер — лучше прежняя оценка, чем ноль.
        return overhang.Top > 0 ? overhang : Fallback;
    }

    private static Thickness Fallback =>
        new(SystemParameters.WindowResizeBorderThickness.Left + SystemParameters.BorderWidth);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
