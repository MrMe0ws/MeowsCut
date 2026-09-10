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

    /// <summary>
    /// Скорость, набранная вручную. Кнопок-заготовок не хватает: замедление до 0.4
    /// или ускорение до 3.2 — обычное дело, а заводить кнопку под каждое число глупо.
    /// </summary>
    [ObservableProperty]
    private string _speedText = "1";

    [ObservableProperty]
    private bool _audioEnabled = true;

    [ObservableProperty]
    private double _volumePercent = 100d;

    [ObservableProperty]
    private double _zoomPercent = 100d;

    [ObservableProperty]
    private double _offsetXPercent;

    [ObservableProperty]
    private double _offsetYPercent;

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
            SpeedText = FormatSpeed(clip.Clip.Speed);
            AudioEnabled = clip.Clip.Audio.Enabled;
            VolumePercent = Math.Round(clip.Clip.Audio.Volume * 100);
            ZoomPercent = Math.Round(clip.Clip.Transform.Zoom * 100);
            OffsetXPercent = Math.Round(clip.Clip.Transform.OffsetX * 100);
            OffsetYPercent = Math.Round(clip.Clip.Transform.OffsetY * 100);
        }
        else
        {
            StartText = EndText = DurationText = SourceRangeText = string.Empty;
            Speed = 1d;
            SpeedText = "1";
            AudioEnabled = true;
            VolumePercent = 100d;
            ZoomPercent = 100d;
            OffsetXPercent = 0d;
            OffsetYPercent = 0d;
        }

        _updating = false;
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>
    /// Применяется по мере набора: «3» даёт 3×, следующая «.5» — 3.5×.
    /// Ждать Enter незачем, а незаконченный ввод вроде «0.» просто игнорируется.
    /// </summary>
    partial void OnSpeedTextChanged(string value)
    {
        if (_updating || !TryParseSpeed(value, out var speed))
        {
            return;
        }

        Speed = speed;
    }

    private static bool TryParseSpeed(string value, out double speed)
    {
        speed = 1d;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // И точка, и запятая: раскладка у пользователя русская, а на клавиатуре
        // цифрового блока запятая, и заставлять его помнить об этом незачем.
        var normalized = value.Trim().Replace(',', '.');

        if (!double.TryParse(
                normalized,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        // Полное имя типа: у самой модели есть свойство Clip, и короткое имя
        // разрешилось бы в него.
        if (parsed < Core.Editing.Timeline.Clip.MinSpeed || parsed > Core.Editing.Timeline.Clip.MaxSpeed)
        {
            return false;
        }

        speed = parsed;
        return true;
    }

    private static string FormatSpeed(double speed) =>
        speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    partial void OnSpeedChanged(double value)
    {
        if (_updating || Clip is not { } clip || Math.Abs(clip.Clip.Speed - value) < 0.0001)
        {
            return;
        }

        _timeline.SetClipSpeed(value);

        // Поле подписи держим в согласии с кнопками-заготовками.
        _updating = true;
        SpeedText = FormatSpeed(value);
        _updating = false;
    }

    partial void OnAudioEnabledChanged(bool value)
    {
        if (_updating || Clip is not { } clip || clip.Clip.Audio.Enabled == value)
        {
            return;
        }

        _timeline.SetClipAudio(clip.Clip.Audio with { Enabled = value });
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

        _timeline.SetClipAudio(clip.Clip.Audio.WithVolume(volume));
    }

    partial void OnZoomPercentChanged(double value) => ApplyTransform();

    partial void OnOffsetXPercentChanged(double value) => ApplyTransform();

    partial void OnOffsetYPercentChanged(double value) => ApplyTransform();

    /// <summary>
    /// Масштаб и сдвиг кадра внутри клипа: то, чем выбирают, какая часть картинки
    /// попадёт в квадрат стикера.
    /// </summary>
    private void ApplyTransform()
    {
        if (_updating || Clip is not { } clip)
        {
            return;
        }

        var transform = new ClipTransform(
            Math.Clamp(ZoomPercent / 100d, ClipTransform.MinZoom, ClipTransform.MaxZoom),
            OffsetXPercent / 100d,
            OffsetYPercent / 100d);

        if (transform == clip.Clip.Transform)
        {
            return;
        }

        _timeline.SetClipTransform(transform);
    }

    [RelayCommand]
    private void ResetTransform()
    {
        ZoomPercent = 100d;
        OffsetXPercent = 0d;
        OffsetYPercent = 0d;
        _timeline.EndInteraction();
    }

    /// <summary>Ползунки отпущены — следующая правка станет отдельной записью в истории.</summary>
    [RelayCommand]
    private void EndInteraction() => _timeline.EndInteraction();

    [RelayCommand]
    private void ApplySpeed(double value) => Speed = value;
}
