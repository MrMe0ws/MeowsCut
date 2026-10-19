using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Formatting;

/// <summary>
/// Шкала масштаба для ползунка: от 10% до 5000% по степеням двойки.
/// </summary>
/// <remarks>
/// На прямой шкале до 5000% всё привычное — от 50% до 300% — уместилось бы
/// в первые пять процентов хода. По логарифму каждое удвоение стоит одного
/// и того же расстояния, а 100% оказывается ближе к середине.
/// </remarks>
public static class ZoomScale
{
    public static double Min { get; } = Math.Log2(ClipTransform.MinZoom);

    public static double Max { get; } = Math.Log2(ClipTransform.MaxZoom);

    /// <summary>Наименьший масштаб в процентах — для поля ввода.</summary>
    public static double MinPercent => ClipTransform.MinZoom * 100;

    public static double MaxPercent => ClipTransform.MaxZoom * 100;

    /// <summary>Как близко к 100% ползунок к ним прилипает: «как было» нужно чаще всего.</summary>
    private const double SnapDistance = 0.04;

    public static double ToSlider(double percent) =>
        Math.Log2(Math.Clamp(percent / 100d, ClipTransform.MinZoom, ClipTransform.MaxZoom));

    public static double FromSlider(double position)
    {
        if (Math.Abs(position) < SnapDistance)
        {
            return 100d;
        }

        return Math.Clamp(Math.Round(Math.Pow(2, position) * 100d), MinPercent, MaxPercent);
    }
}
