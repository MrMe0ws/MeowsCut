using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MeowsCut.App.Controls;

/// <summary>
/// Показывает кадрирование клипа прямо в предпросмотре.
/// </summary>
/// <remarks>
/// Масштаб и сдвиг кадра применяются при экспорте фильтрами ffmpeg, и до этой правки
/// увидеть их в редакторе было нельзя: пользователь ставил 200% и не понимал,
/// сработало ли. Здесь то же самое делается средствами WPF — приблизительно,
/// но достаточно, чтобы выбрать, какая часть кадра останется.
///
/// Знак сдвига обратный тому, что в фильтре: там двигается окно обрезки,
/// здесь — сама картинка под неподвижным окном.
/// </remarks>
public sealed class FramingHost : Decorator
{
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(
            nameof(Zoom),
            typeof(double),
            typeof(FramingHost),
            new FrameworkPropertyMetadata(1d, OnFramingChanged));

    public static readonly DependencyProperty OffsetXProperty =
        DependencyProperty.Register(
            nameof(OffsetX),
            typeof(double),
            typeof(FramingHost),
            new FrameworkPropertyMetadata(0d, OnFramingChanged));

    public static readonly DependencyProperty OffsetYProperty =
        DependencyProperty.Register(
            nameof(OffsetY),
            typeof(double),
            typeof(FramingHost),
            new FrameworkPropertyMetadata(0d, OnFramingChanged));

    public static readonly DependencyProperty RotationProperty =
        DependencyProperty.Register(
            nameof(Rotation),
            typeof(int),
            typeof(FramingHost),
            new FrameworkPropertyMetadata(0, OnFramingChanged));

    public FramingHost() => ClipToBounds = true;

    /// <summary>Масштаб кадра: 1 — как в исходнике.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Сдвиг по горизонтали в долях ширины кадра.</summary>
    public double OffsetX
    {
        get => (double)GetValue(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    public double OffsetY
    {
        get => (double)GetValue(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    /// <summary>Поворот кадра по часовой стрелке: 0, 90, 180 или 270 градусов.</summary>
    public int Rotation
    {
        get => (int)GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Apply();
    }

    private static void OnFramingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FramingHost)d).Apply();

    private void Apply()
    {
        if (Child is not { } child)
        {
            return;
        }

        var zoom = Zoom <= 0 ? 1d : Zoom;

        if (Rotation == 0 &&
            Math.Abs(zoom - 1d) < 0.0001 &&
            Math.Abs(OffsetX) < 0.0001 &&
            Math.Abs(OffsetY) < 0.0001)
        {
            child.RenderTransform = Transform.Identity;
            return;
        }

        var centerX = ActualWidth / 2;
        var centerY = ActualHeight / 2;

        var transform = new TransformGroup();

        if (Rotation != 0)
        {
            transform.Children.Add(new RotateTransform(Rotation, centerX, centerY));

            // Четверть оборота меняет ширину и высоту местами, и повёрнутый кадр
            // вылезает за окно предпросмотра. Ужимаем его обратно — так же,
            // как при экспорте кадр вписывается в кадр ролика.
            if (Rotation is 90 or 270 && ActualWidth > 0 && ActualHeight > 0)
            {
                var fit = Math.Min(ActualWidth / ActualHeight, ActualHeight / ActualWidth);
                transform.Children.Add(new ScaleTransform(fit, fit, centerX, centerY));
            }
        }

        transform.Children.Add(new ScaleTransform(zoom, zoom, centerX, centerY));
        transform.Children.Add(new TranslateTransform(-OffsetX * ActualWidth, -OffsetY * ActualHeight));

        child.RenderTransform = transform;
    }
}
