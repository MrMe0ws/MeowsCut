using MeowsCut.Core.Media;

namespace MeowsCut.Core.Editing.Timeline;

/// <summary>Идентификатор надписи. Нужен выделению, чтобы пережить пересборку списка.</summary>
public readonly record struct TitleId(Guid Value)
{
    public static TitleId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N")[..8];
}

/// <summary>
/// Где на кадре стоит надпись.
/// </summary>
/// <remarks>
/// Девять мест вместо произвольных координат. Титры ставят по краям и по центру,
/// а не «на 37% ширины»: набор точек попадает в нужное место одним нажатием
/// и не разъезжается при смене формата ролика — в вертикальном кадре «внизу
/// по центру» остаётся внизу по центру.
/// </remarks>
public enum TitleAnchor
{
    TopLeft = 0,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleCenter,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

/// <summary>
/// Надпись поверх кадра: текст, время и место.
/// </summary>
/// <remarks>
/// Живёт отдельным списком у последовательности, а не клипом на видеоряде.
/// Надпись не занимает места на ленте: она лежит поверх того, что под ней,
/// и режется вместе с ним. Клипом её пришлось бы класть на вторую видеодорожку,
/// а её в редакторе нет и не планируется.
/// </remarks>
public sealed record TitleClip(TitleId Id, string Text, TimeSpan TimelineStart, TimeSpan Duration)
{
    /// <summary>Короче этого надпись не успевают прочитать.</summary>
    public static readonly TimeSpan MinDuration = TimeSpan.FromMilliseconds(300);

    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    public const double MinScale = 0.02;
    public const double MaxScale = 0.3;

    /// <summary>Высота букв как доля высоты кадра. 0.06 — примерно 65 пикселей в 1080p.</summary>
    public double Scale { get; init; } = 0.06;

    public TitleAnchor Anchor { get; init; } = TitleAnchor.BottomCenter;

    /// <summary>Цвет букв в виде #RRGGBB.</summary>
    public string Color { get; init; } = "#FFFFFF";

    /// <summary>
    /// Тёмная плашка под текстом.
    /// </summary>
    /// <remarks>
    /// Белые буквы на светлом кадре пропадают, и это выясняется уже в готовом
    /// файле. Плашка — самый дешёвый способ сделать надпись читаемой на любом
    /// кадре, и она включена по умолчанию.
    /// </remarks>
    public bool Backdrop { get; init; } = true;

    /// <summary>Отступ от края кадра как доля его высоты.</summary>
    public double Margin { get; init; } = 0.05;

    public TimeSpan TimelineEnd => TimelineStart + Duration;

    public TimeRange TimelineRange => new(TimelineStart, TimelineEnd);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public static TitleClip Create(string text, TimeSpan start) =>
        new(TitleId.New(), text, start < TimeSpan.Zero ? TimeSpan.Zero : start, DefaultDuration);

    public TitleClip WithText(string text) => this with { Text = text };

    public TitleClip WithStart(TimeSpan start) =>
        this with { TimelineStart = start < TimeSpan.Zero ? TimeSpan.Zero : start };

    public TitleClip WithDuration(TimeSpan duration) =>
        this with { Duration = duration < MinDuration ? MinDuration : duration };

    public TitleClip WithScale(double scale) =>
        this with { Scale = Math.Clamp(scale, MinScale, MaxScale) };

    /// <summary>Пересекается ли надпись с отрезком ленты.</summary>
    public bool Overlaps(TimeSpan start, TimeSpan end) => TimelineStart < end && TimelineEnd > start;
}
