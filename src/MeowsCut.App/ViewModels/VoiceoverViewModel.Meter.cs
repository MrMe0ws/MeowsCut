using CommunityToolkit.Mvvm.ComponentModel;
using MeowsCut.App.Formatting;
using MeowsCut.App.Localization;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Что видно во время записи: уровень сигнала, время, подсказка — и реакция
/// на то, что пользователь сделал с роликом.
/// </summary>
public sealed partial class VoiceoverViewModel
{
    private static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Нижняя граница шкалы. Тише −60 dBFS — это уже шум комнаты.</summary>
    private const double MeterFloorDb = -60d;

    /// <summary>Пик выше этого — перегруз: голос уже срезан.</summary>
    private const double ClippingPeak = 0.98;

    /// <summary>Сколько держится отметка перегруза — иначе её не успеть заметить.</summary>
    private static readonly TimeSpan ClippingHold = TimeSpan.FromSeconds(1);

    /// <summary>После стольких секунд ровных нулей микрофон явно не отдаёт звук.</summary>
    private static readonly TimeSpan SilenceWarningAfter = TimeSpan.FromSeconds(1.5);

    /// <summary>Ролик остановился ближе этого к концу — значит, доиграл, а не поставлен на паузу.</summary>
    private static readonly TimeSpan EndTolerance = TimeSpan.FromMilliseconds(150);

    private bool _wasPlaying;
    private bool _hasPlayed;
    private DateTime _clippingUntil;

    /// <summary>Уровень сигнала для шкалы, от 0 до 1 (−60…0 dBFS).</summary>
    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    private bool _isClipping;

    [ObservableProperty]
    private string _elapsedText = DisplayFormat.Duration(TimeSpan.Zero);

    /// <summary>Строка о том, что происходит: включается микрофон, идёт запись, ролик закончился.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    private void OnMeterTick()
    {
        if (State != VoiceoverState.Recording || _recording is not { } recording)
        {
            return;
        }

        var peak = recording.TakePeak();
        var recorded = recording.Recorded;

        // Шкала падает плавно: по-честному мгновенная, она дёргается так,
        // что уровень голоса на глаз не прочитать.
        Level = Math.Max(ToMeter(peak), Level * 0.8);
        ElapsedText = DisplayFormat.Duration(recorded);

        if (peak >= ClippingPeak)
        {
            _clippingUntil = DateTime.UtcNow + ClippingHold;
        }

        IsClipping = DateTime.UtcNow < _clippingUntil;

        if (ReactToPause())
        {
            return;
        }

        StatusText = !recording.HasSignal && recorded > SilenceWarningAfter
            ? Strings.VoiceoverSilent
            : _hasPlayed && !_preview.IsPlaying
                ? Strings.VoiceoverPastEnd
                : Strings.VoiceoverRecordingHint;
    }

    /// <summary>
    /// Пауза ролика заканчивает дубль: голос дальше уже не ляжет под картинку.
    /// Ролик, доигравший до конца, запись не останавливает — фразу часто
    /// договаривают на последнем кадре.
    /// </summary>
    /// <returns>true, если дубль закончен.</returns>
    private bool ReactToPause()
    {
        var playing = _preview.IsPlaying;
        var paused = _wasPlaying && !playing && _preview.Position < _timeline.Duration - EndTolerance;

        _wasPlaying = playing;
        _hasPlayed |= playing;

        if (paused)
        {
            _ = FinishAsync();
        }

        return paused;
    }

    /// <summary>Перемотка посреди дубля его заканчивает: дальше голос лёг бы не туда.</summary>
    private void OnUserSeeked(object? sender, EventArgs e)
    {
        if (State == VoiceoverState.Recording)
        {
            _logger.LogInformation("Ролик перемотали во время записи — дубль закончен");
            _ = FinishAsync();
        }
    }

    private static double ToMeter(double peak)
    {
        if (peak <= 0d)
        {
            return 0d;
        }

        var db = 20d * Math.Log10(peak);
        return Math.Clamp((db - MeterFloorDb) / -MeterFloorDb, 0d, 1d);
    }
}
