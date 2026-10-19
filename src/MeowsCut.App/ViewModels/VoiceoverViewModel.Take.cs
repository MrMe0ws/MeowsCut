using System.Diagnostics;
using System.IO;
using MeowsCut.App.Localization;
using MeowsCut.App.Recording;
using MeowsCut.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Один дубль от начала до доски: включить микрофон, запустить ролик,
/// выровнять запись по ролику и положить её на дорожку.
/// </summary>
public sealed partial class VoiceoverViewModel
{
    private async Task StartAsync()
    {
        var settings = _settings.Current;

        State = VoiceoverState.Starting;
        StatusText = Strings.VoiceoverStarting;
        _starting = new CancellationTokenSource();

        try
        {
            _recording = await _recorder
                .StartAsync(settings.RecordingDeviceId, NextRecordingPath(), _starting.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ResetToIdle();
            return;
        }
        catch (VoiceRecordingException ex)
        {
            _logger.LogWarning(ex, "Микрофон не включился");
            ResetToIdle();
            _dialogs.ShowError(_errors.Present(ex));
            return;
        }
        finally
        {
            _starting.Dispose();
            _starting = null;
        }

        _recording.Faulted += OnRecordingFaulted;
        _preview.IsMonitorMuted = settings.MuteWhileRecording;
        _preview.Ticked += OnPlaybackTicked;
        _preview.UserSeeked += OnUserSeeked;

        var requestedAt = Stopwatch.GetTimestamp();
        _preview.Play();

        // Позиция берётся после запуска: с конца ролика проигрыватель начинает сначала.
        _start = _preview.Position;
        _origin = new PlaybackOrigin(_start, requestedAt);
        _wasPlaying = _hasPlayed = _preview.IsPlaying;

        State = VoiceoverState.Recording;
        StatusText = Strings.VoiceoverRecordingHint;
        _meter.Start();
    }

    private async Task FinishAsync()
    {
        if (State != VoiceoverState.Recording || _recording is not { } recording || _origin is not { } origin)
        {
            return;
        }

        StopMonitoring();
        _recording = null;
        State = VoiceoverState.Saving;
        StatusText = Strings.VoiceoverSaving;

        var leadIn = recording.FileTimeAt(origin.Timestamp) +
                     TimeSpan.FromMilliseconds(_settings.Current.RecordingOffsetMs);

        // По этой строке разбирают жалобы «озвучка отстаёт»: видно, сколько отрезано
        // и успела ли оценка старта устояться.
        _logger.LogInformation(
            "Дубль: с {Start} на таймлайне, начало файла отрезано на {LeadIn} мс, оценка старта {Settled}",
            _start,
            leadIn.TotalMilliseconds,
            origin.IsSettled ? "устоялась" : "по нажатию");

        try
        {
            var path = await CleanAsync(await recording.FinishAsync(CancellationToken.None).ConfigureAwait(true))
                .ConfigureAwait(true);

            if (ImportRecording is not null &&
                await ImportRecording(path, CancellationToken.None).ConfigureAwait(true) is { } imported &&
                !_timeline.PlaceRecording(imported.Project, imported.Source, _start, leadIn))
            {
                _dialogs.ShowError(Strings.Voiceover, Strings.VoiceoverTooShort);
            }
        }
        catch (VoiceRecordingException ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить запись");
            _dialogs.ShowError(_errors.Present(ex));
        }
        finally
        {
            await recording.DisposeAsync().ConfigureAwait(true);
            ResetToIdle();
        }
    }

    /// <summary>Микрофон пропал: записанное до этого кладём на доску и говорим, что случилось.</summary>
    private void OnRecordingFaulted(object? sender, Exception error) =>
        _dispatcher.Post(async () =>
        {
            await FinishAsync().ConfigureAwait(true);
            _dialogs.ShowError(_errors.Present(error));
        });

    private void OnPlaybackTicked(object? sender, PlaybackTick tick) => _origin?.Observe(tick.Position, tick.Timestamp);

    private void StopMonitoring()
    {
        _meter.Stop();
        _preview.Ticked -= OnPlaybackTicked;
        _preview.UserSeeked -= OnUserSeeked;
        _preview.Pause();
        _preview.IsMonitorMuted = false;

        if (_recording is not null)
        {
            _recording.Faulted -= OnRecordingFaulted;
        }
    }

    private void ResetToIdle()
    {
        _origin = null;
        State = VoiceoverState.Idle;
        StatusText = string.Empty;
        Level = 0d;
        IsClipping = false;
        ElapsedText = Formatting.DisplayFormat.Duration(TimeSpan.Zero);
    }

    /// <summary>
    /// Рядом с сохранённым черновиком, иначе — в папке программы. Имя по времени:
    /// дублей бывает много, и перезаписать вчерашний сегодняшним нельзя.
    /// </summary>
    private string NextRecordingPath()
    {
        var folder = ProjectFolder?.Invoke() is { } projectFolder
            ? Path.Combine(projectFolder, Strings.RecordingsFolderName)
            : _paths.RecordingsDirectory;

        var name = string.Format(
            System.Globalization.CultureInfo.CurrentUICulture,
            Strings.RecordingFileName,
            DateTime.Now);

        return Path.Combine(folder, name);
    }
}
