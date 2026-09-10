using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace MeowsCut.App.Controls;

/// <summary>
/// Плавная прокрутка колесом для <see cref="ScrollViewer"/>.
/// </summary>
/// <remarks>
/// Системная прокрутка прыгает на три «строки» разом, и в длинной панели настроек
/// это выглядит рывком: глазу не за что зацепиться, и место, где ты был, теряется.
/// Здесь тот же путь проезжается за 220 мс с замедлением к концу.
///
/// Прокручиваемая величина копится в <see cref="TargetOffsetProperty"/>, а не читается
/// у <see cref="ScrollViewer"/>: во время анимации его собственное смещение отстаёт,
/// и второй поворот колеса подряд отматывал бы назад уже пройденное.
/// </remarks>
public static class SmoothScroll
{
    /// <summary>Сколько пикселей проезжает один щелчок колеса.</summary>
    private const double StepPixels = 110d;

    private static readonly Duration Glide = new(TimeSpan.FromMilliseconds(220));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScroll),
            new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Куда едем. Анимируется именно оно, а сеттер двигает сам ScrollViewer.</summary>
    private static readonly DependencyProperty TargetOffsetProperty =
        DependencyProperty.RegisterAttached(
            "TargetOffset",
            typeof(double),
            typeof(SmoothScroll),
            new PropertyMetadata(0d, OnTargetOffsetChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        viewer.PreviewMouseWheel -= OnWheel;

        if (e.NewValue is true)
        {
            viewer.PreviewMouseWheel += OnWheel;
        }
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.ScrollableHeight <= 0)
        {
            return;
        }

        var current = (double)viewer.GetValue(TargetOffsetProperty);

        // Первый поворот колеса начинается оттуда, где ScrollViewer стоит сейчас:
        // его могли прокрутить перетаскиванием ползунка или клавишами.
        if (viewer.Tag as string != Marker)
        {
            viewer.Tag = Marker;
            current = viewer.VerticalOffset;
        }

        var target = Math.Clamp(
            current - (Math.Sign(e.Delta) * StepPixels),
            0d,
            viewer.ScrollableHeight);

        viewer.BeginAnimation(TargetOffsetProperty, null);
        viewer.SetValue(TargetOffsetProperty, current);

        viewer.BeginAnimation(TargetOffsetProperty, new DoubleAnimation(target, Glide)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        });

        e.Handled = true;
    }

    private static void OnTargetOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer viewer)
        {
            viewer.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    /// <summary>Признак того, что накопленное смещение уже наше и читать чужое не нужно.</summary>
    private const string Marker = "smooth-scroll";
}
