using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MeowsCut.App.Controls;

/// <summary>
/// Колесо мыши над строкой параметра двигает её значение.
/// </summary>
/// <remarks>
/// Целиться колесом в узкое поле с числом неудобно: рука ведёт курсор к строке,
/// а не к цифрам. Поэтому колесо слушает вся строка — подпись, ползунок и пустое
/// место между ними — и двигает ползунок этой строки на один шаг (с Shift —
/// на десять). Само поле с числом колесо по-прежнему обрабатывает само.
///
/// Шаг: <see cref="StepProperty"/> на строке, иначе деление ползунка, если он
/// прилипает к делениям, иначе его SmallChange. Правка закрывается тем же
/// сигналом, что и отпущенный ползунок, — через паузу после последнего щелчка,
/// иначе каждый щелчок колеса ложился бы в историю отдельной записью.
/// </remarks>
public static class ParamWheel
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(600);

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ParamWheel), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Свой шаг колеса для строки; 0 — взять у ползунка.</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.RegisterAttached(
        "Step", typeof(double), typeof(ParamWheel), new PropertyMetadata(0d));

    private static readonly DependencyProperty TimerProperty = DependencyProperty.RegisterAttached(
        "Timer", typeof(DispatcherTimer), typeof(ParamWheel), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static double GetStep(DependencyObject element) => (double)element.GetValue(StepProperty);

    public static void SetStep(DependencyObject element, double value) => element.SetValue(StepProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewMouseWheel -= OnWheel;

        if (e.NewValue is true)
        {
            element.PreviewMouseWheel += OnWheel;
        }
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement row || e.Delta == 0)
        {
            return;
        }

        // Над полем с числом работает его собственное колесо — со своим шагом.
        if (FindAncestor<NumberBox>(e.OriginalSource as DependencyObject, row) is not null)
        {
            return;
        }

        if (FindDescendant<Slider>(row) is not { IsEnabled: true } slider)
        {
            return;
        }

        e.Handled = true;

        var step = GetStep(row);
        if (step <= 0)
        {
            step = slider.IsSnapToTickEnabled && slider.TickFrequency > 0 ? slider.TickFrequency : slider.SmallChange;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            step *= 10;
        }

        var next = Math.Round(slider.Value / step + Math.Sign(e.Delta), MidpointRounding.AwayFromZero) * step;
        slider.Value = Math.Clamp(next, slider.Minimum, slider.Maximum);

        RestartSettle(row, slider);
    }

    private static void RestartSettle(FrameworkElement row, Slider slider)
    {
        if (row.GetValue(TimerProperty) is not DispatcherTimer timer)
        {
            timer = new DispatcherTimer { Interval = Settle };
            timer.Tick += (_, _) =>
            {
                timer.Stop();

                if (SliderInteraction.GetEndCommand(slider) is { } command && command.CanExecute(null))
                {
                    command.Execute(null);
                }
            };
            row.SetValue(TimerProperty, timer);
        }

        timer.Stop();
        timer.Start();
    }

    private static T? FindAncestor<T>(DependencyObject? start, DependencyObject stop)
        where T : DependencyObject
    {
        for (var node = start; node is not null && !ReferenceEquals(node, stop); node = Parent(node))
        {
            if (node is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static DependencyObject? Parent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match && (child as UIElement)?.Visibility == Visibility.Visible)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
