using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MeowsCut.App.Controls;

/// <summary>
/// Кнопка, открывающая своё меню по обычному щелчку: «Добавить ▾», «⋯».
/// </summary>
/// <remarks>
/// Меню кнопки живёт в отдельном дереве и не видит окна; модель ему передаётся
/// через Tag кнопки — пункты берут команды из PlacementTarget.Tag.
/// </remarks>
public static class DropDownMenu
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DropDownMenu), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button)
        {
            return;
        }

        button.Click -= OnClick;

        if (e.NewValue is true)
        {
            button.Click += OnClick;
        }
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
