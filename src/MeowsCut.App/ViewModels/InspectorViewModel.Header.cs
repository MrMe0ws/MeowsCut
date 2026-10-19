using System.ComponentModel;
using MeowsCut.App.Formatting;
using MeowsCut.App.Localization;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Шапка панели свойств и быстрые значения скорости.
/// </summary>
public sealed partial class InspectorViewModel
{
    private static readonly double[] SpeedPresetValues = [0.5, 1, 1.5, 2, 4];

    private IReadOnlyList<ChoiceChip>? _speedPresets;

    /// <summary>Готовые скорости. Любую другую вписывают в поле рядом.</summary>
    public IReadOnlyList<ChoiceChip> SpeedPresets => _speedPresets ??= CreateSpeedPresets();

    /// <summary>Имя файла выделенного куска без расширения — его узнают глазами.</summary>
    public string HeaderTitle => Clip is { } clip
        ? System.IO.Path.GetFileNameWithoutExtension(clip.Title)
        : string.Empty;

    /// <summary>«Фото · 15,3 с на доске». При нескольких выделенных — сколько их.</summary>
    public string HeaderDetails
    {
        get
        {
            if (Clip is not { } clip)
            {
                return string.Empty;
            }

            if (_timeline.SelectionCount > 1)
            {
                return string.Format(LocalizationManager.Culture, Strings.InspectorManySelected, _timeline.SelectionCount);
            }

            var kind = clip.IsImage ? Strings.SourceKindImage : Strings.SourceKindVideo;
            return $"{kind} · {DisplayFormat.Duration(clip.Clip.TimelineDuration)}";
        }
    }

    public bool IsImageClip => Clip?.IsImage == true;

    private IReadOnlyList<ChoiceChip> CreateSpeedPresets()
    {
        var chips = SpeedPresetValues
            .Select(value => new ChoiceChip(SpeedScale.Format(value) + "×", value, ApplySpeedPreset))
            .ToArray();

        ChoiceChip.MarkActive(chips, Speed, 0.001);
        return chips;
    }

    /// <summary>
    /// Подсветка чипов идёт за скоростью, откуда бы она ни пришла: из поля,
    /// ползунка, колеса мыши или с другого выделенного куска.
    /// </summary>
    private void OnHeaderSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Speed) && _speedPresets is { } chips)
        {
            ChoiceChip.MarkActive(chips, Speed, 0.001);
        }

        if (e.PropertyName is nameof(Clip) or nameof(DurationText))
        {
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(HeaderDetails));
            OnPropertyChanged(nameof(IsImageClip));
        }
    }

    private void ApplySpeedPreset(double speed)
    {
        Speed = speed;
        _timeline.EndInteraction();
    }
}
