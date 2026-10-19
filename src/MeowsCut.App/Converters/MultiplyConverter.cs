using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MeowsCut.App.Converters;

/// <summary>
/// Число, умноженное на коэффициент: высота обложки из её ширины (16:10),
/// чтобы сетка медиа держала пропорции при любой ширине панели.
/// </summary>
public sealed class MultiplyConverter : IValueConverter
{
    public double Factor { get; set; } = 1d;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double number && !double.IsNaN(number) ? number * Factor : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
