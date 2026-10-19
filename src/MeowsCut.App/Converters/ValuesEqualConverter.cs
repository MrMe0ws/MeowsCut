using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MeowsCut.App.Converters;

/// <summary>
/// Равны ли два значения — подсветка текущего чипа, когда значения хранятся
/// простыми числами (полутона тональности), а не готовыми чипами.
/// </summary>
public sealed class ValuesEqualConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is not null && System.Convert.ToDouble(values[0], CultureInfo.InvariantCulture)
            .Equals(System.Convert.ToDouble(values[1] ?? double.NaN, CultureInfo.InvariantCulture));

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
