using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MeowsCut.App.Converters;

/// <summary>true → Visible, false → Collapsed.</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>true → Collapsed, false → Visible.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>
/// Человеческие названия для перечислений: в списке должно быть «MP4» и «H.264»,
/// а не имена элементов перечисления.
/// </summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        Core.Export.ContainerFormat container => container switch
        {
            Core.Export.ContainerFormat.Mp4 => "MP4",
            Core.Export.ContainerFormat.WebM => "WebM",
            Core.Export.ContainerFormat.Mov => "MOV",
            Core.Export.ContainerFormat.Mkv => "MKV",
            Core.Export.ContainerFormat.Avi => "AVI",
            _ => container.ToString()
        },
        Core.Export.VideoCodec codec => Core.Export.CodecNames.DisplayName(codec),
        Core.Export.AudioCodec codec => Core.Export.CodecNames.DisplayName(codec),
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Привязка переключателя к значению перечисления: кнопка нажата, когда выбран
/// именно её вариант. Нужна для панели инструментов доски.
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value.Equals(parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? parameter : Binding.DoNothing;
}

/// <summary>Ненулевое количество элементов → Visible.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Пустое значение прячет элемент: подсказки не должны оставлять пустых мест.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text
            ? string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible
            : value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
