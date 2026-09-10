using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MeowsCut.App.Controls;

/// <summary>
/// Плавная прокрутка колесом для <see cref="ScrollViewer"/>.
/// </summary>
/// <remarks>
/// Системная прокрутка прыгает на три «строки» разом, и в длинной панели настроек
/// это выглядит рывком: глазу не за что зацепиться, и место, где ты был, теряется.
///
/// Движение считается в цикле отрисовки, а не <see cref="System.Windows.Media.Animation.DoubleAnimation"/>.
/// Анимация задавала свой темп и на тяжёлой панели не поспевала за отрисовкой —
/// получалась дёрганая прокрутка. Здесь на каждый нарисованный кадр смещение
/// подтягивается к цели на долю оставшегося пути: сколько кадров успевает окно,
/// столько шагов и делается, и рывков не бывает по построению.
/// </remarks>
public static class SmoothScroll
{
    /// <summary>Сколько пикселей проезжает один щелчок колеса.</summary>
    private const double StepPixels = 110d;

    /// <summary>Доля оставшегося пути за кадр. Больше — резче, меньше — вязче.</summary>
    private const double Approach = 0.22d;

    /// <summary>Ближе этого к цели считаем, что приехали: доли пикселя не видны.</summary>
    private const double Epsilon = 0.5d;

    private static readonly Dictionary<ScrollViewer, double> Targets = [];

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScroll),
            new PropertyMetadata(false, OnIsEnabledChanged));

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
        viewer.Unloaded -= OnUnloaded;

        if (e.NewValue is true)
        {
            viewer.PreviewMouseWheel += OnWheel;
            viewer.Unloaded += OnUnloaded;
        }
        else
        {
            Stop(viewer);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) => Stop((ScrollViewer)sender);

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.ScrollableHeight <= 0)
        {
            return;
        }

        // Пока едем — считаем от цели, а не от текущего положения: иначе второй
        // щелчок колеса подряд отматывал бы назад уже пройденное.
        var from = Targets.TryGetValue(viewer, out var target) ? target : viewer.VerticalOffset;

        var next = Math.Clamp(from - (Math.Sign(e.Delta) * StepPixels), 0d, viewer.ScrollableHeight);

        var wasMoving = Targets.ContainsKey(viewer);
        Targets[viewer] = next;

        if (!wasMoving)
        {
            CompositionTarget.Rendering += OnRendering;
        }

        e.Handled = true;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (Targets.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            return;
        }

        foreach (var viewer in Targets.Keys.ToArray())
        {
            var target = Targets[viewer];
            var current = viewer.VerticalOffset;
            var remaining = target - current;

            if (Math.Abs(remaining) <= Epsilon)
            {
                viewer.ScrollToVerticalOffset(target);
                Stop(viewer);
                continue;
            }

            viewer.ScrollToVerticalOffset(current + (remaining * Approach));
        }
    }

    private static void Stop(ScrollViewer viewer)
    {
        Targets.Remove(viewer);

        if (Targets.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }
}
