using System.Windows;
using System.Windows.Media;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Надпись, готовая к показу поверх предпросмотра.
/// </summary>
/// <remarks>
/// Отдельный тип, а не сама <see cref="TitleClip"/>: разметке нужны кисть,
/// выравнивание и размер в долях кадра, а модели про них знать незачем.
/// Размер в предпросмотре считается от высоты кадра прямо в разметке —
/// так надпись остаётся на своём месте при любом размере окна.
/// </remarks>
public sealed record PreviewTitle(
    string Text,
    Brush Foreground,
    bool Backdrop,
    HorizontalAlignment Horizontal,
    VerticalAlignment Vertical,
    double Scale,
    double Margin)
{
    public static PreviewTitle From(TitleClip title) => new(
        title.Text,
        BrushOf(title.Color),
        title.Backdrop,
        HorizontalOf(title.Anchor),
        VerticalOf(title.Anchor),
        title.Scale,
        title.Margin);

    private static HorizontalAlignment HorizontalOf(TitleAnchor anchor) => anchor switch
    {
        TitleAnchor.TopLeft or TitleAnchor.MiddleLeft or TitleAnchor.BottomLeft => HorizontalAlignment.Left,
        TitleAnchor.TopRight or TitleAnchor.MiddleRight or TitleAnchor.BottomRight => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center
    };

    private static VerticalAlignment VerticalOf(TitleAnchor anchor) => anchor switch
    {
        TitleAnchor.TopLeft or TitleAnchor.TopCenter or TitleAnchor.TopRight => VerticalAlignment.Top,
        TitleAnchor.BottomLeft or TitleAnchor.BottomCenter or TitleAnchor.BottomRight => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Center
    };

    private static Brush BrushOf(string color)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
        catch (FormatException)
        {
            // Цвет из чужого черновика может быть любым — белый безопаснее падения.
            return Brushes.White;
        }
    }
}
