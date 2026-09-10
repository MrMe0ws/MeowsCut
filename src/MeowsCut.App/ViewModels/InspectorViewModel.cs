using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Formatting;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Свойства выделенного клипа: границы, скорость, звук.
/// </summary>
/// <remarks>
/// Правит модель только через команды таймлайна — прямых изменений последовательности
/// здесь нет, иначе история отмен рассыпалась бы.
/// </remarks>
public sealed partial class InspectorViewModel : ObservableObject
{
    private readonly TimelineViewModel _timeline;
    private bool _updating;

    public InspectorViewModel(TimelineViewModel timeline)
    {
        _timeline = timeline;
        _timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TimelineViewModel.SelectedClip))
            {
                Refresh();
            }
        };

        _timeline.SequenceChanged += (_, _) => Refresh();
    }

    public static IReadOnlyList<double> SpeedPresets { get; } = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 4];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ClipViewModel? _clip;

    [ObservableProperty]
    private string _startText = string.Empty;

    [ObservableProperty]
    private string _endText = string.Empty;

    [ObservableProperty]
    private string _durationText = string.Empty;

    [ObservableProperty]
    private string _sourceRangeText = string.Empty;

    [ObservableProperty]
    private double _speed = 1d;

    [ObservableProperty]
    private bool _audioEnabled = true;

    [ObservableProperty]
    private double _volumePercent = 100d;

    public bool HasSelection => Clip is not null;

    private void Refresh()
    {
        _updating = true;

        Clip = _timeline.SelectedClip;

        if (Clip is { } clip)
        {
            StartText = DisplayFormat.Duration(clip.Start);
            EndText = DisplayFormat.Duration(clip.End);
            DurationText = DisplayFormat.Duration(clip.Clip.TimelineDuration);
            SourceRangeText =
                $"{DisplayFormat.Duration(clip.Clip.SourceRange.Start)} → {DisplayFormat.Duration(clip.Clip.SourceRange.End)}";
            Speed = clip.Clip.Speed;
            AudioEnabled = clip.Clip.Audio.Enabled;
            VolumePercent = Math.Round(clip.Clip.Audio.Volume * 100);
        }
        else
        {
            StartText = EndText = DurationText = SourceRangeText = string.Empty;
            Speed = 1d;
            AudioEnabled = true;
            VolumePercent = 100d;
        }

        _updating = false;
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnSpeedChanged(double value)
    {
        if (_updating || Clip is not { } clip || Math.Abs(clip.Clip.Speed - value) < 0.0001)
        {
            return;
        }

        _timeline.SetClipSpeed(clip.Id, value);
    }

    partial void OnAudioEnabledChanged(bool value)
    {
        if (_updating || Clip is not { } clip || clip.Clip.Audio.Enabled == value)
        {
            return;
        }

        _timeline.SetClipAudio(clip.Id, clip.Clip.Audio with { Enabled = value });
        _timeline.EndInteraction();
    }

    partial void OnVolumePercentChanged(double value)
    {
        if (_updating || Clip is not { } clip)
        {
            return;
        }

        var volume = Math.Clamp(value / 100d, ClipAudio.MinVolume, ClipAudio.MaxVolume);
        if (Math.Abs(clip.Clip.Audio.Volume - volume) < 0.0001)
        {
            return;
        }

        _timeline.SetClipAudio(clip.Id, clip.Clip.Audio.WithVolume(volume));
    }

    /// <summary>Ползунки отпущены — следующая правка станет отдельной записью в истории.</summary>
    [RelayCommand]
    private void EndInteraction() => _timeline.EndInteraction();

    [RelayCommand]
    private void ApplySpeed(double value) => Speed = value;
}
