using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Formatting;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Свойства выбранного куска звука: громкость, тональность, затухания,
/// плюс управление дорожкой, на которой он лежит.
/// </summary>
/// <remarks>
/// Отдельно от инспектора видеоклипа: показывать оба сразу значит каждый раз
/// гадать, к чему относится «громкость». Полоса под доской показывает то одно,
/// то другое — смотря что выбрано.
/// </remarks>
public sealed partial class AudioInspectorViewModel : ObservableObject
{
    private readonly TimelineViewModel _timeline;
    private bool _updating;

    public AudioInspectorViewModel(TimelineViewModel timeline)
    {
        _timeline = timeline;
        _timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TimelineViewModel.SelectedAudioClip))
            {
                Refresh();
            }
        };

        _timeline.SequenceChanged += (_, _) => Refresh();
    }

    /// <summary>Готовые сдвиги тональности: ниже голос, выше голос, обратно.</summary>
    public static IReadOnlyList<int> PitchPresets { get; } = [-12, -7, -5, -2, 0, 2, 5, 7, 12];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private AudioClip? _clip;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _rangeText = string.Empty;

    [ObservableProperty]
    private double _gainPercent = 100d;

    [ObservableProperty]
    private int _pitchSemitones;

    [ObservableProperty]
    private double _speed = 1d;

    /// <summary>Положение ползунка скорости — логарифм, см. <see cref="SpeedScale"/>.</summary>
    [ObservableProperty]
    private double _speedSlider;

    /// <summary>Скорость, набранная вручную: 1.15 или 2.35 ползунком не поймать.</summary>
    [ObservableProperty]
    private string _speedText = "1";

    [ObservableProperty]
    private double _fadeInSeconds;

    [ObservableProperty]
    private double _fadeOutSeconds;

    [ObservableProperty]
    private string _trackTitle = string.Empty;

    [ObservableProperty]
    private bool _trackMuted;

    [ObservableProperty]
    private double _trackGainPercent = 100d;

    public bool HasSelection => Clip is not null;

    private void Refresh()
    {
        _updating = true;

        Clip = _timeline.SelectedAudioClip;
        var track = _timeline.AudioTracks.FirstOrDefault(item => item.Id == _timeline.SelectedAudioTrack);

        if (Clip is { } clip)
        {
            Title = clip.Title.Length > 0 ? clip.Title : Localization.Strings.SectionSound;
            RangeText = $"{DisplayFormat.Duration(clip.TimelineStart)} → {DisplayFormat.Duration(clip.TimelineEnd)}";
            GainPercent = Math.Round(clip.Gain * 100);
            PitchSemitones = clip.PitchSemitones;
            Speed = clip.Speed;
            SpeedText = SpeedScale.Format(clip.Speed);
            SpeedSlider = SpeedScale.ToSlider(clip.Speed);
            FadeInSeconds = Math.Round(clip.FadeIn.TotalSeconds, 2);
            FadeOutSeconds = Math.Round(clip.FadeOut.TotalSeconds, 2);
        }

        TrackTitle = track?.Title ?? string.Empty;
        TrackMuted = track?.IsMuted ?? false;
        TrackGainPercent = Math.Round((track?.Gain ?? 1d) * 100);

        _updating = false;
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnGainPercentChanged(double value)
    {
        if (_updating || Clip is null)
        {
            return;
        }

        _timeline.SetAudioClipProperties(gain: value / 100d);
    }

    partial void OnPitchSemitonesChanged(int value)
    {
        if (_updating || Clip is null)
        {
            return;
        }

        _timeline.SetAudioClipProperties(pitch: value);
    }

    partial void OnSpeedSliderChanged(double value)
    {
        if (_updating)
        {
            return;
        }

        Speed = SpeedScale.FromSlider(value);
    }

    /// <summary>Применяется по мере набора: «2» даёт 2×, следующая «.35» — 2.35×.</summary>
    partial void OnSpeedTextChanged(string value)
    {
        if (_updating || !SpeedScale.TryParse(value, out var speed))
        {
            return;
        }

        Speed = speed;
    }

    partial void OnSpeedChanged(double value)
    {
        if (_updating || Clip is not { } clip || Math.Abs(clip.Speed - value) < 0.0001)
        {
            return;
        }

        _timeline.SetAudioClipProperties(speed: value);

        _updating = true;
        SpeedText = SpeedScale.Format(value);
        SpeedSlider = SpeedScale.ToSlider(value);
        _updating = false;
    }

    partial void OnFadeInSecondsChanged(double value) => ApplyFades();

    partial void OnFadeOutSecondsChanged(double value) => ApplyFades();

    private void ApplyFades()
    {
        if (_updating || Clip is null)
        {
            return;
        }

        _timeline.SetAudioClipProperties(
            fadeIn: TimeSpan.FromSeconds(Math.Max(0, FadeInSeconds)),
            fadeOut: TimeSpan.FromSeconds(Math.Max(0, FadeOutSeconds)));
    }

    partial void OnTrackGainPercentChanged(double value)
    {
        if (_updating)
        {
            return;
        }

        _timeline.SetAudioTrackGain(_timeline.SelectedAudioTrack, value / 100d);
    }

    [RelayCommand]
    private void ApplyPitch(int value) => PitchSemitones = value;

    [RelayCommand]
    private void ToggleMuted()
    {
        _timeline.ToggleAudioTrackMutedCommand.Execute(_timeline.SelectedAudioTrack);
        Refresh();
    }

    [RelayCommand]
    private void RemoveTrack() =>
        _timeline.RemoveAudioTrackCommand.Execute(_timeline.SelectedAudioTrack);

    /// <summary>Ползунок отпущен — следующая правка станет отдельной записью истории.</summary>
    [RelayCommand]
    private void EndInteraction() => _timeline.EndAudioEdit();
}
