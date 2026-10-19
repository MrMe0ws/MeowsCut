using CommunityToolkit.Mvvm.ComponentModel;
using MeowsCut.App.Formatting;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Масштаб без потолка в 300% и наезд: кадр плавно идёт от 100% к заданному
/// масштабу и сдвигу (или обратно) за выбранное время.
/// </summary>
public sealed partial class InspectorViewModel
{
    /// <summary>Сдвиг, который ползунок даёт всегда, даже при 100%: его ставят до масштаба.</summary>
    private const double BaseOffsetLimitPercent = 50d;

    private bool _syncingZoom;

    /// <summary>Положение ползунка масштаба — логарифм, см. <see cref="ZoomScale"/>.</summary>
    [ObservableProperty]
    private double _zoomSlider;

    /// <summary>За сколько секунд от начала куска кадр доходит до заданного. 0 — сразу.</summary>
    [ObservableProperty]
    private double _motionSeconds;

    /// <summary>Отъезд: от заданного кадра к 100%.</summary>
    [ObservableProperty]
    private bool _motionOut;

    /// <summary>Длина выделенного куска: дольше него наезд всё равно не идёт.</summary>
    [ObservableProperty]
    private double _motionLimitSeconds = 10d;

    /// <summary>
    /// Предел сдвига в процентах. Растёт с масштабом: при 2000% до края картинки
    /// 950% ширины окна, а ±50% не довели бы и до середины. Уже выставленный
    /// сдвиг в пределы попадает всегда — ползунок не обрежет его сам.
    /// </summary>
    public double OffsetLimitPercent => Math.Ceiling(Math.Max(
        Math.Max(BaseOffsetLimitPercent, ClipMotion.OffsetLimit(ZoomPercent / 100d) * 100d),
        Math.Max(Math.Abs(OffsetXPercent), Math.Abs(OffsetYPercent))));

    public double MinOffsetPercent => -OffsetLimitPercent;

    public static double MaxMotionSeconds => ClipMotion.MaxSeconds;

    partial void OnZoomSliderChanged(double value)
    {
        if (_updating || _syncingZoom)
        {
            return;
        }

        ZoomPercent = ZoomScale.FromSlider(value);
    }

    partial void OnMotionSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(MotionSliderMax));
        ApplyTransform();
    }

    partial void OnMotionOutChanged(bool value)
    {
        ApplyTransform();
        if (!_updating)
        {
            _timeline.EndInteraction();
        }
    }

    partial void OnMotionLimitSecondsChanged(double value) => OnPropertyChanged(nameof(MotionSliderMax));

    /// <summary>Ползунок наезда — по длине куска, но не меньше уже заданного времени.</summary>
    public double MotionSliderMax => Math.Max(MotionLimitSeconds, MotionSeconds);

    private void RefreshMotion(ClipTransform transform)
    {
        MotionLimitSeconds = Clip is { } clip
            ? Math.Max(0.1, Math.Round(clip.Clip.TimelineDuration.TotalSeconds, 1))
            : 10d;
        MotionSeconds = Math.Round(transform.MotionSeconds, 1);
        MotionOut = transform.MotionOut;
        SyncZoomSlider(ZoomPercent);
        OnPropertyChanged(nameof(MotionSliderMax));
    }

    /// <summary>Число поменялось — ползунок встаёт за ним, не отправляя правку обратно.</summary>
    private void SyncZoomSlider(double percent)
    {
        _syncingZoom = true;
        ZoomSlider = ZoomScale.ToSlider(percent);
        _syncingZoom = false;
        OnPropertyChanged(nameof(OffsetLimitPercent));
        OnPropertyChanged(nameof(MinOffsetPercent));
    }
}
