using MeowsCut.App.Formatting;

namespace MeowsCut.App.ViewModels;

/// <summary>Готовые скорости для куска звука — как у клипа, плюс любое своё число в поле.</summary>
public sealed partial class AudioInspectorViewModel
{
    private static readonly double[] SpeedPresetValues = [0.5, 0.75, 1, 1.5, 2];

    private IReadOnlyList<ChoiceChip>? _speedPresets;

    public IReadOnlyList<ChoiceChip> SpeedPresets
    {
        get
        {
            if (_speedPresets is null)
            {
                _speedPresets = SpeedPresetValues
                    .Select(value => new ChoiceChip(SpeedScale.Format(value) + "×", value, ApplySpeedPreset))
                    .ToArray();

                ChoiceChip.MarkActive(_speedPresets, Speed, 0.001);
                PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(Speed))
                    {
                        ChoiceChip.MarkActive(_speedPresets, Speed, 0.001);
                    }
                };
            }

            return _speedPresets;
        }
    }

    private void ApplySpeedPreset(double speed)
    {
        Speed = speed;
        _timeline.EndAudioEdit();
    }
}
