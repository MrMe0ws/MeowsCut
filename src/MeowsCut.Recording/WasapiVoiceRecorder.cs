using System.Runtime.InteropServices;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace MeowsCut.Recording;

/// <summary>
/// Микрофоны системы и запись с них через WASAPI.
/// </summary>
/// <remarks>
/// Общий режим, а не монопольный: монопольный отнимает микрофон у остальных
/// программ, и параллельный звонок или стрим молча бы оборвался. Задержка общего
/// режима — десятки миллисекунд, для озвучки этого достаточно (ADR-38).
/// </remarks>
public sealed class WasapiVoiceRecorder(ILogger<WasapiVoiceRecorder> logger) : IVoiceRecorder
{
    public IReadOnlyList<AudioInputDevice> ListDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = DefaultDeviceId(enumerator);
            var devices = new List<AudioInputDevice>();

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    devices.Add(new AudioInputDevice(device.ID, device.FriendlyName, device.ID == defaultId));
                }
            }

            return devices;
        }
        catch (COMException ex)
        {
            // Список микрофонов — подсказка в настройках, а не повод для ошибки:
            // запись всё равно попробует системный микрофон.
            logger.LogWarning(ex, "Не удалось получить список микрофонов");
            return [];
        }
    }

    /// <remarks>
    /// Наружу уходит только <see cref="VoiceRecordingException"/> или отмена. Любой другой
    /// сбой звуковой подсистемы тоже переводится в неё: иначе он пролетел бы мимо
    /// обработчика, и кнопка записи навсегда осталась бы в «Включаю микрофон».
    /// </remarks>
    public async Task<IVoiceRecording> StartAsync(string? deviceId, string outputPath, CancellationToken cancellationToken)
    {
        try
        {
            var device = Open(deviceId);
            return await WasapiVoiceRecording.StartAsync(device, outputPath, logger, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not VoiceRecordingException)
        {
            logger.LogError(ex, "Микрофон не включился");
            throw CaptureErrors.Map(ex);
        }
    }

    /// <summary>
    /// Выбранный микрофон, а если его нет — системный. Гарнитуру часто отключают,
    /// и отказываться писать из-за этого вместо встроенного микрофона незачем.
    /// </summary>
    private MMDevice Open(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();

        if (deviceId is not null)
        {
            try
            {
                var device = enumerator.GetDevice(deviceId);
                if (device.State == DeviceState.Active)
                {
                    return device;
                }

                device.Dispose();
                logger.LogInformation("Выбранный микрофон {Id} выключен, пишу с системного", deviceId);
            }
            catch (COMException ex)
            {
                logger.LogInformation(ex, "Выбранный микрофон {Id} не найден, пишу с системного", deviceId);
            }
        }

        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        }
        catch (COMException ex)
        {
            throw new VoiceRecordingException(RecordingProblem.NoDevice, "В системе нет включённого микрофона.", ex);
        }
    }

    private static string? DefaultDeviceId(MMDeviceEnumerator enumerator)
    {
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console))
        {
            return null;
        }

        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        return device.ID;
    }
}
