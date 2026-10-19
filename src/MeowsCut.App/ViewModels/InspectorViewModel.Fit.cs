using CommunityToolkit.Mvvm.ComponentModel;
using MeowsCut.App.Localization;
using MeowsCut.Core.Export;

namespace MeowsCut.App.ViewModels;

/// <summary>Пункт выбора подгонки кадра: null — как во всём ролике.</summary>
public sealed record ClipFitOption(FitMode? Mode, string Title)
{
    public static IReadOnlyList<ClipFitOption> All { get; } =
    [
        new(null, Strings.ClipFitProject),
        new(FitMode.Contain, Strings.ClipFitContain),
        new(FitMode.Cover, Strings.ClipFitCover)
    ];

    public static ClipFitOption For(FitMode? mode) =>
        All.FirstOrDefault(option => option.Mode == mode) ?? All[0];
}

/// <summary>
/// Подгонка кадра у отдельного куска: целиком с полями или на весь кадр с обрезкой.
/// </summary>
/// <remarks>
/// Галочка «Заполнить кадр» под предпросмотром решает за весь ролик. Здесь — исключение
/// для одного куска: вертикальное видео растянуть, а фотографию оставить целиком.
/// </remarks>
public sealed partial class InspectorViewModel
{
    public IReadOnlyList<ClipFitOption> FitOptions => ClipFitOption.All;

    [ObservableProperty]
    private ClipFitOption _fitOption = ClipFitOption.All[0];

    partial void OnFitOptionChanged(ClipFitOption value)
    {
        if (_updating)
        {
            return;
        }

        ApplyTransform();
        _timeline.EndInteraction();
    }
}
