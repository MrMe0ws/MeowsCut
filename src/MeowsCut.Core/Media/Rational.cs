using System.Globalization;

namespace MeowsCut.Core.Media;

/// <summary>
/// Дробь вида 30000/1001, как их отдаёт ffprobe для частоты кадров и аспекта пикселя.
/// </summary>
public readonly record struct Rational(int Numerator, int Denominator)
{
    public static readonly Rational Zero = new(0, 1);

    public bool IsZero => Numerator == 0 || Denominator == 0;

    public double Value => Denominator == 0 ? 0d : (double)Numerator / Denominator;

    /// <summary>
    /// Разбирает строку ffprobe: "30000/1001", "25/1", "0/0", "29.97".
    /// Некорректное значение — не исключение: у VFR-видео поле бывает "0/0".
    /// </summary>
    public static Rational Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Zero;
        }

        var slash = value.IndexOf('/');
        if (slash < 0)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var single)
                ? FromDouble(single)
                : Zero;
        }

        var numeratorText = value[..slash];
        var denominatorText = value[(slash + 1)..];

        if (int.TryParse(numeratorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator) &&
            int.TryParse(denominatorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator))
        {
            return new Rational(numerator, denominator);
        }

        return Zero;
    }

    /// <summary>
    /// Приближает вещественное значение дробью: 29.97 → 30000/1001, 23.976 → 24000/1001.
    /// </summary>
    public static Rational FromDouble(double value)
    {
        if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            return Zero;
        }

        foreach (var candidate in NtscCandidates)
        {
            if (Math.Abs(candidate.Value - value) < 0.005)
            {
                return candidate;
            }
        }

        if (Math.Abs(value - Math.Round(value)) < 0.0005)
        {
            return new Rational((int)Math.Round(value), 1);
        }

        return new Rational((int)Math.Round(value * 1000), 1000);
    }

    private static readonly Rational[] NtscCandidates =
    [
        new(24000, 1001),
        new(30000, 1001),
        new(60000, 1001),
        new(120000, 1001)
    ];

    public override string ToString() =>
        IsZero ? "0" : Value.ToString("0.###", CultureInfo.InvariantCulture);
}
