using System.Globalization;

namespace MeowsCut.Ffmpeg.Progress;

/// <summary>Снимок прогресса, собранный из блока вывода -progress.</summary>
public sealed record ProgressSnapshot(
    long? Frame,
    double? Fps,
    TimeSpan? OutTime,
    long? TotalSizeBytes,
    double? SpeedFactor,
    bool Completed);

/// <summary>
/// Разбирает поток ключ=значение, который ffmpeg пишет при -progress pipe:1.
/// </summary>
/// <remarks>
/// Формат блочный: строки идут по одной, а блок заканчивается строкой progress=continue
/// или progress=end. Пока блок не закончен, показывать его нельзя — значения в нём
/// относятся к разным моментам времени.
/// </remarks>
public sealed class FfmpegProgressParser
{
    private long? _frame;
    private double? _fps;
    private TimeSpan? _outTime;
    private long? _totalSize;
    private double? _speed;

    /// <summary>
    /// Скармливает строку. Возвращает снимок, когда блок завершён, иначе null.
    /// </summary>
    public ProgressSnapshot? Feed(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return null;
        }

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();

        switch (key)
        {
            case "frame":
                _frame = ParseLong(value);
                break;

            case "fps":
                _fps = ParseDouble(value);
                break;

            case "out_time_us":
            case "out_time_ms":
                // out_time_ms у ffmpeg на самом деле в микросекундах — известная особенность.
                if (ParseLong(value) is { } microseconds && microseconds >= 0)
                {
                    _outTime = TimeSpan.FromTicks(microseconds * 10);
                }

                break;

            case "total_size":
                _totalSize = ParseLong(value);
                break;

            case "speed":
                _speed = ParseSpeed(value);
                break;

            case "progress":
            {
                var snapshot = new ProgressSnapshot(
                    _frame,
                    _fps,
                    _outTime,
                    _totalSize,
                    _speed,
                    string.Equals(value, "end", StringComparison.OrdinalIgnoreCase));

                return snapshot;
            }
        }

        return null;
    }

    private static long? ParseLong(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    /// <summary>Скорость приходит как «2.01x», а в начале работы — как «N/A».</summary>
    private static double? ParseSpeed(string value)
    {
        var trimmed = value.TrimEnd('x', 'X', ' ');
        return ParseDouble(trimmed);
    }
}
