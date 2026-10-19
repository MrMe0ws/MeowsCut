using System.IO;
using MeowsCut.App.Localization;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Шумоподавление дубля: шипение микрофона и гул комнаты убираются сразу после «Стоп».
/// </summary>
/// <remarks>
/// Убирается из готового файла, а не на лету: так очищенный дубль слышен в предпросмотре
/// таким же, каким уйдёт в экспорт, а сырой остаётся рядом — если подавление съело
/// что-то лишнее, переписывать реплику не придётся.
/// </remarks>
public sealed partial class VoiceoverViewModel
{
    /// <summary>Переключатель у кнопки записи. Тот же, что в настройках: меняется сразу в обоих местах.</summary>
    public bool ReduceNoise
    {
        get => _settings.Current.ReduceRecordingNoise;
        set
        {
            if (value == ReduceNoise)
            {
                return;
            }

            _ = SaveReduceNoiseAsync(value);
        }
    }

    private async Task SaveReduceNoiseAsync(bool value)
    {
        try
        {
            await _settings.SaveAsync(_settings.Current with { ReduceRecordingNoise = value }, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (MeowsCutException ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить переключатель шумоподавления");
        }

        OnPropertyChanged(nameof(ReduceNoise));
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        _dispatcher.Post(() => OnPropertyChanged(nameof(ReduceNoise)));

    /// <summary>
    /// Очищенный дубль, если шумоподавление включено, иначе — тот же файл.
    /// Ошибка подавления не отменяет дубль: он ложится как записан.
    /// </summary>
    private async Task<string> CleanAsync(string path)
    {
        var settings = _settings.Current;

        if (!settings.ReduceRecordingNoise)
        {
            return path;
        }

        StatusText = Strings.VoiceoverCleaning;

        var cleanPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            Path.GetFileNameWithoutExtension(path) + Strings.RecordingCleanSuffix + Path.GetExtension(path));

        try
        {
            var cleaned = await _noise
                .ReduceAsync(path, cleanPath, settings.RecordingNoiseReductionDb, CancellationToken.None)
                .ConfigureAwait(true);

            return cleaned ? cleanPath : path;
        }
        catch (MeowsCutException ex)
        {
            _logger.LogWarning(ex, "Шумоподавление не удалось, дубль кладётся как записан");
            _dialogs.ShowError(Strings.Voiceover, Strings.VoiceoverDenoiseFailed);
            return path;
        }
    }
}
