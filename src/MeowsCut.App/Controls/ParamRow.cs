using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MeowsCut.App.Controls;

/// <summary>
/// Строка параметра: значок и подпись слева, орган (обычно ползунок) посередине,
/// поле с числом справа.
/// </summary>
/// <remarks>
/// Одна и та же строка в панели свойств и в чипе у клипа на доске: правка
/// выглядит одинаково, где бы её ни делали. Колесо мыши над строкой двигает
/// значение (см. <see cref="ParamWheel"/>), число можно вписать руками —
/// готовые значения рядом никогда не ограничивают выбор.
/// </remarks>
public sealed class ParamRow : ContentControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(ParamRow), new PropertyMetadata(null));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(ParamRow), new PropertyMetadata(string.Empty));

    /// <summary>Что стоит справа: поле с числом, подпись значения.</summary>
    public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(
        nameof(Trailing), typeof(object), typeof(ParamRow), new PropertyMetadata(null));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public object? Trailing
    {
        get => GetValue(TrailingProperty);
        set => SetValue(TrailingProperty, value);
    }
}
