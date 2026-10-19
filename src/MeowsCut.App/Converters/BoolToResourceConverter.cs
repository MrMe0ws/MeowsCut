using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MeowsCut.App.Converters;

/// <summary>
/// Выбирает один из двух ресурсов по флагу: значок «Играть» или «Пауза»,
/// солнце или месяц на кнопке темы.
/// </summary>
/// <remarks>
/// Ресурс ищется в момент преобразования, а не при загрузке словаря:
/// значки лежат в соседнем словаре, и порядок их загрузки здесь не важен.
/// </remarks>
public sealed class BoolToResourceConverter : IValueConverter
{
    public string TrueKey { get; set; } = string.Empty;

    public string FalseKey { get; set; } = string.Empty;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current?.TryFindResource(value is true ? TrueKey : FalseKey);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
