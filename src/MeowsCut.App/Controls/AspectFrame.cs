using System.Windows;
using System.Windows.Controls;

namespace MeowsCut.App.Controls;

/// <summary>
/// Держит содержимое в кадре заданных пропорций.
/// </summary>
/// <remarks>
/// Нужен вертикальному формату. Пока предпросмотр просто растягивал видео
/// по месту, выбрать формат 9:16 было невозможно: пользователь ставил его,
/// а на экране оставался тот же горизонтальный кадр — и понять, что попадёт
/// в файл, а что обрежется, можно было только экспортом.
///
/// Кадр вписывается в отведённое место целиком и стоит по центру: так видно
/// и сам кадр, и поля вокруг него.
/// </remarks>
public sealed class AspectFrame : Decorator
{
    public static readonly DependencyProperty AspectProperty =
        DependencyProperty.Register(
            nameof(Aspect),
            typeof(double),
            typeof(AspectFrame),
            new FrameworkPropertyMetadata(
                16d / 9d,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Отношение ширины к высоте: 1.78 — горизонтальный кадр, 0.5625 — вертикальный.</summary>
    public double Aspect
    {
        get => (double)GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not { } child)
        {
            return default;
        }

        var frame = Fit(constraint);
        child.Measure(frame);

        // Отдаём размер самого кадра, а не всего места: иначе рамка предпросмотра
        // осталась бы горизонтальной, а вертикальное видео болталось бы внутри неё.
        return frame;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child)
        {
            return finalSize;
        }

        var frame = Fit(finalSize);

        child.Arrange(new Rect(
            (finalSize.Width - frame.Width) / 2,
            (finalSize.Height - frame.Height) / 2,
            frame.Width,
            frame.Height));

        return finalSize;
    }

    /// <summary>Наибольший кадр нужных пропорций, влезающий в отведённое место.</summary>
    private Size Fit(Size available)
    {
        var aspect = Aspect > 0 ? Aspect : 16d / 9d;

        var width = available.Width;
        var height = available.Height;

        // Бесконечность приходит от прокрутки и от панелей, которые меряют
        // содержимое «сколько попросишь»: там опереться можно только на вторую сторону.
        if (double.IsInfinity(width) && double.IsInfinity(height))
        {
            return new Size(0, 0);
        }

        if (double.IsInfinity(width))
        {
            return new Size(height * aspect, height);
        }

        if (double.IsInfinity(height))
        {
            return new Size(width, width / aspect);
        }

        return width / aspect <= height
            ? new Size(width, width / aspect)
            : new Size(height * aspect, height);
    }
}
