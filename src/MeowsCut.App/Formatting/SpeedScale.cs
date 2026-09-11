using System.Globalization;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Formatting;

/// <summary>
/// Шкала скорости для ползунка и разбор набранного числа. Общая для кусков видео
/// и звука: пределы у них одни и те же, и вести себя они должны одинаково.
/// </summary>
public static class SpeedScale
{
    /// <summary>
    /// Ползунок ходит по степеням двойки. На линейной шкале от 0.1× до 16× всё
    /// замедление уместилось бы в первые шесть процентов хода, а половина осталась
    /// бы на диапазон 8–16×, которым почти не пользуются. По логарифму же удвоение
    /// скорости всегда стоит одного и того же расстояния.
    /// </summary>
    public static double Min { get; } = Math.Log2(Clip.MinSpeed);

    public static double Max { get; } = Math.Log2(Clip.MaxSpeed);

    /// <summary>
    /// Насколько близко к обычной скорости ползунок к ней прилипает. Попасть мышью
    /// ровно в 1× иначе почти невозможно, а именно это значение нужно чаще всего —
    /// оно означает «верни как было».
    /// </summary>
    private const double SnapDistance = 0.05;

    public static double ToSlider(double speed) =>
        Math.Log2(Math.Clamp(speed, Clip.MinSpeed, Clip.MaxSpeed));

    public static double FromSlider(double position)
    {
        var speed = Math.Pow(2, position);

        if (Math.Abs(speed - 1d) < SnapDistance)
        {
            return 1d;
        }

        // Два знака: 1.15 и 2.35 набрать можно, а 1.1537 с ползунка — это мусор в поле
        return Math.Clamp(Math.Round(speed, 2), Clip.MinSpeed, Clip.MaxSpeed);
    }

    public static string Format(double speed) =>
        speed.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Разбирает набранное вручную. Незаконченный ввод вроде «0.» и значения за
    /// пределами просто не принимаются — поле продолжает жить своей жизнью,
    /// пока в нём не появится осмысленное число.
    /// </summary>
    public static bool TryParse(string value, out double speed)
    {
        speed = 1d;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // И точка, и запятая: раскладка у пользователя русская, а на цифровом блоке
        // запятая, и заставлять его помнить об этом незачем.
        var normalized = value.Trim().Replace(',', '.');

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed < Clip.MinSpeed || parsed > Clip.MaxSpeed)
        {
            return false;
        }

        speed = parsed;
        return true;
    }
}
