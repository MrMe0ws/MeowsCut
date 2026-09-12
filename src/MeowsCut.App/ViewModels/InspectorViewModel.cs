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
    /// Положение ползунка — логарифм скорости, см. <see cref="SpeedScale"/>.
    /// Отдельным свойством, потому что ползунок и число живут в разных шкалах.
    /// </summary>
    [ObservableProperty]
    private double _speedSlider;

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

    /// <summary>
    /// Появление и затухание в секундах: так их называет пользователь
    /// («секунду на затемнение»), и так же подписаны поля у звука.
    /// </summary>
    [ObservableProperty]
    private double _fadeInSeconds;

    [ObservableProperty]
    private double _fadeOutSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotationText))]
    private int _rotation;

    public bool HasSelection => Clip is not null;

    /// <summary>Насколько кадр повёрнут сейчас. «0°» тоже показываем: иначе непонятно,
    /// довернули клип или кнопка не сработала.</summary>
    public string RotationText => Rotation + "°";

    /// <summary>Полное имя типа: здесь <c>Clip</c> — это свойство с выделенным клипом.</summary>
    public static double MaxFadeSeconds => Core.Editing.Timeline.Clip.MaxFade.TotalSeconds;

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
            SpeedText = SpeedScale.Format(clip.Clip.Speed);
            SpeedSlider = SpeedScale.ToSlider(clip.Clip.Speed);
            AudioEnabled = clip.Clip.Audio.Enabled;
            VolumePercent = Math.Round(clip.Clip.Audio.Volume * 100);
            ZoomPercent = Math.Round(clip.Clip.Transform.Zoom * 100);
            OffsetXPercent = Math.Round(clip.Clip.Transform.OffsetX * 100);
            OffsetYPercent = Math.Round(clip.Clip.Transform.OffsetY * 100);
            Rotation = clip.Clip.Transform.Rotation;
            FadeInSeconds = Round(clip.Clip.FadeIn);
            FadeOutSeconds = Round(clip.Clip.FadeOut);
        }
        else
        {
            StartText = EndText = DurationText = SourceRangeText = string.Empty;
            Speed = 1d;
            SpeedText = "1";
            SpeedSlider = 0d;
            AudioEnabled = true;
            VolumePercent = 100d;
            ZoomPercent = 100d;
            OffsetXPercent = 0d;
            OffsetYPercent = 0d;
            Rotation = 0;
            FadeInSeconds = 0d;
            FadeOutSeconds = 0d;
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
        if (_updating || !SpeedScale.TryParse(value, out var speed))
        {
            return;
        }

        Speed = speed;
    }

    partial void OnSpeedSliderChanged(double value)
    {
        if (_updating)
        {
            return;
        }

        Speed = SpeedScale.FromSlider(value);
    }

    partial void OnSpeedChanged(double value)
    {
        if (_updating || Clip is not { } clip || Math.Abs(clip.Clip.Speed - value) < 0.0001)
        {
            return;
        }

        _timeline.SetClipSpeed(value);

        // Поле и ползунок держим в согласии друг с другом.
        _updating = true;
        SpeedText = SpeedScale.Format(value);
        SpeedSlider = SpeedScale.ToSlider(value);
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

    /// <summary>Десятые доли секунды: шага ползунка мельче глаз всё равно не различает.</summary>
    private static double Round(TimeSpan value) => Math.Round(value.TotalSeconds, 1);

    partial void OnFadeInSecondsChanged(double value)
    {
        if (_updating || Clip is not { } clip)
        {
            return;
        }

        var fade = TimeSpan.FromSeconds(Math.Clamp(value, 0d, MaxFadeSeconds));
        if (Math.Abs((clip.Clip.FadeIn - fade).TotalSeconds) < 0.01)
        {
            return;
        }

        _timeline.SetClipFades(fadeIn: fade);
    }

    partial void OnFadeOutSecondsChanged(double value)
    {
        if (_updating || Clip is not { } clip)
        {
            return;
        }

        var fade = TimeSpan.FromSeconds(Math.Clamp(value, 0d, MaxFadeSeconds));
        if (Math.Abs((clip.Clip.FadeOut - fade).TotalSeconds) < 0.01)
        {
            return;
        }

        _timeline.SetClipFades(fadeOut: fade);
    }

    /// <summary>Довернуть кадр на четверть оборота — по часовой стрелке и против.</summary>
    [RelayCommand]
    private void RotateLeft() => _timeline.RotateClipsBy(-90);

    [RelayCommand]
    private void RotateRight() => _timeline.RotateClipsBy(90);

    [RelayCommand]
    private void RotateHalfTurn() => _timeline.RotateClipsBy(180);

    [RelayCommand]
    private void ClearFades()
    {
        FadeInSeconds = 0d;
        FadeOutSeconds = 0d;
        _timeline.SetClipFades(TimeSpan.Zero, TimeSpan.Zero);
        _timeline.EndInteraction();
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
            OffsetYPercent / 100d) with { Rotation = Rotation };

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
        Rotation = 0;
        _timeline.SetClipTransform(ClipTransform.Identity);
        _timeline.EndInteraction();
    }

    /// <summary>Ползунки отпущены — следующая правка станет отдельной записью в истории.</summary>
    [RelayCommand]
    private void EndInteraction() => _timeline.EndInteraction();

}
