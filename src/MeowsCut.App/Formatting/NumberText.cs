using System.Globalization;

namespace MeowsCut.App.Formatting;

/// <summary>Число, набранное руками в поле рядом с ползунком.</summary>
public static class NumberText
{
    /// <summary>
    /// Разбирает набранное: запятая и точка равноправны, единица измерения
    /// и пробелы не мешают. null — это не число.
    /// </summary>
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = new string(text
            .Where(c => char.IsDigit(c) || c is '-' or '+' or '.' or ',' or '−')
            .ToArray())
            .Replace(',', '.')
            .Replace('−', '-');

        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Значение в пределах и с той точностью, с какой его показывают: иначе в модель
    /// уходило бы 37.4999, а на экране стояло бы 37.
    /// </summary>
    public static double Fit(double value, double minimum, double maximum, string format)
    {
        var low = Math.Min(minimum, maximum);
        var high = Math.Max(minimum, maximum);

        var shown = Math.Clamp(value, low, high).ToString(format, CultureInfo.InvariantCulture);

        return Parse(shown) is { } rounded ? Math.Clamp(rounded, low, high) : Math.Clamp(value, low, high);
    }
}
