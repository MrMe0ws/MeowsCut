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

        if (Math.Abs(zoom - 1d) < 0.0001 && Math.Abs(OffsetX) < 0.0001 && Math.Abs(OffsetY) < 0.0001)
        {
            child.RenderTransform = Transform.Identity;
            return;
        }

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(zoom, zoom, ActualWidth / 2, ActualHeight / 2));
        transform.Children.Add(new TranslateTransform(-OffsetX * ActualWidth, -OffsetY * ActualHeight));

        child.RenderTransform = transform;
    }
}
