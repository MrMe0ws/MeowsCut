using MeowsCut.Core.Media;

namespace MeowsCut.Core.Abstractions;

/// <summary>
/// Насколько тихо и насколько долго должно быть, чтобы считать это паузой.
/// </summary>
/// <remarks>
/// Порог в децибелах полной шкалы: −30 дБ — это уже отчётливая тишина,
/// в которой слышен только шум комнаты. Слишком высокий порог начинает резать
/// тихую речь, слишком низкий не замечает пауз вовсе — поэтому значение отдано
/// пользователю, а не зашито.
/// </remarks>
public sealed record SilenceOptions(double ThresholdDb = -30d, TimeSpan MinDuration = default)
{
    public static readonly SilenceOptions Default = new(-30d, TimeSpan.FromMilliseconds(600));

    /// <summary>Пауза короче этой не считается паузой: так дышат между фразами.</summary>
    public TimeSpan EffectiveMinDuration =>
        MinDuration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(600) : MinDuration;

    /// <summary>
    /// Сколько тишины оставить по краям найденной паузы.
    /// </summary>
    /// <remarks>
    /// Без запаса речь начинается ровно на стыке, и склейка звучит как обрыв:
    /// слово словно наступает на предыдущее. Десятая доля секунды по краям
    /// возвращает дыхание, не возвращая самой паузы.
    /// </remarks>
    public TimeSpan Padding { get; init; } = TimeSpan.FromMilliseconds(120);
}

/// <summary>Ищет тишину в звуковой дорожке файла.</summary>
public interface ISilenceDetector
{
    /// <summary>
    /// Отрезки тишины во времени самого файла.
    /// </summary>
    /// <remarks>
    /// Возвращает именно отрезки файла, а не ленты: как их разложить по клипам,
    /// решает тот, кто знает монтаж.
    /// </remarks>
    Task<IReadOnlyList<TimeRange>> DetectAsync(
        string filePath,
        SilenceOptions options,
        CancellationToken cancellationToken);
}
