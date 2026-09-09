using System.Globalization;
using MeowsCut.App.Localization;
using MeowsCut.Core.Media;

namespace MeowsCut.App.Formatting;

/// <summary>
/// Форматирование значений для интерфейса. Одно место — чтобы длительность и размеры
/// выглядели одинаково во всех панелях.
/// </summary>
public static class DisplayFormat
{
    private static readonly CultureInfo Culture = CultureInfo.CurrentUICulture;

    public static string Duration(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss\.ff", CultureInfo.InvariantCulture);

    public static string FileSize(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var digits = size >= 100 || unit == 0 ? 0 : 1;
        return string.Create(Culture, $"{Math.Round(size, digits)} {units[unit]}");
    }

    public static string Bitrate(long? bitsPerSecond)
    {
        if (bitsPerSecond is not > 0)
        {
            return Strings.Unknown;
        }

        var mbps = bitsPerSecond.Value / 1_000_000d;
        if (mbps >= 1)
        {
            return string.Create(Culture, $"{Math.Round(mbps, 1)} Мбит/с");
        }

        return string.Create(Culture, $"{bitsPerSecond.Value / 1000} кбит/с");
    }

    public static string FrameRate(Rational value) =>
        value.IsZero ? Strings.Unknown : string.Create(Culture, $"{Math.Round(value.Value, 3)} fps");

    public static string SampleRate(int hertz) =>
        hertz <= 0 ? Strings.Unknown : string.Create(Culture, $"{hertz / 1000d:0.#} кГц");

    public static string Channels(int channels, string? layout)
    {
        if (channels <= 0)
        {
            return Strings.Unknown;
        }

        return string.IsNullOrWhiteSpace(layout)
            ? channels.ToString(Culture)
            : $"{channels} ({layout})";
    }

    public static string Codec(string name, string? profile) =>
        string.IsNullOrWhiteSpace(profile) ? name : $"{name} ({profile})";
}
