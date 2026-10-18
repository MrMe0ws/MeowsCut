using System.Diagnostics;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeowsCut.Recording;

/// <summary>
/// Одна запись с микрофона: WASAPI в общем режиме, потоком в WAV.
/// </summary>
/// <remarks>
/// Звук приходит в потоке захвата кусками по <see cref="BufferMilliseconds"/> мс
/// и сразу уходит в <see cref="TakeWriter"/>. Здесь — только жизнь захвата:
/// дождаться первого звука, остановиться, сообщить о пропавшем микрофоне.
///
/// Продолжения после await остаются в потоке вызывающего (<c>ConfigureAwait(true)</c>):
/// устройство создано в потоке интерфейса, а COM-объекты NAudio из другого
/// апартамента не вызываются — обращение к микрофону из пула потоков падало
/// с E_NOINTERFACE, и запись зависала на «Включаю микрофон».
/// </remarks>
internal sealed class WasapiVoiceRecording : IVoiceRecording
{
    /// <summary>Короткий буфер — точнее метка времени у каждого куска.</summary>
    private const int BufferMilliseconds = 20;

    private static readonly TimeSpan FirstSoundTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;
    private readonly TakeWriter _take;
    private readonly string _outputPath;
    private readonly string _partPath;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _firstSound = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _finished;
    private Exception? _writeError;

    private WasapiVoiceRecording(MMDevice device, string outputPath, ILogger logger)
    {
        _device = device;
        _outputPath = outputPath;
        _partPath = outputPath + ".part";
        _logger = logger;

        _capture = new WasapiCapture(device, true, BufferMilliseconds);

        try
        {
            var format = CaptureFormat.From(_capture.WaveFormat);
            if (!format.IsSupported)
            {
                throw new VoiceRecordingException(
                    RecordingProblem.NoResponse,
                    $"Микрофон отдаёт звук в формате, который программа не умеет записывать: {_capture.WaveFormat}.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            _take = new TakeWriter(_partPath, format, _capture.WaveFormat.SampleRate);
        }
        catch
        {
            _capture.Dispose();
            throw;
        }

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public TimeSpan Recorded => _take.Recorded;

    public bool HasSignal => _take.HasSignal;

    public event EventHandler<Exception>? Faulted;

    public static async Task<WasapiVoiceRecording> StartAsync(
        MMDevice device,
        string outputPath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        WasapiVoiceRecording recording;
        string deviceName;

        try
        {
            deviceName = device.FriendlyName;
            recording = new WasapiVoiceRecording(device, outputPath, logger);
        }
        catch (Exception ex)
        {
            device.Dispose();
            throw CaptureErrors.Map(ex);
        }

        try
        {
            recording._capture.StartRecording();
            await recording._firstSound.Task.WaitAsync(FirstSoundTimeout, cancellationToken).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            await recording.DisposeAsync().ConfigureAwait(true);
            throw new VoiceRecordingException(RecordingProblem.NoResponse, "Микрофон открылся, но звук от него не пришёл.");
        }
        catch (Exception ex)
        {
            await recording.DisposeAsync().ConfigureAwait(true);

            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw CaptureErrors.Map(ex);
        }

        logger.LogInformation(
            "Запись с микрофона «{Device}» пошла: {Format} → {Path}",
            deviceName,
            recording._capture.WaveFormat,
            outputPath);

        return recording;
    }

    public double TakePeak() => _take.TakePeak();

    public TimeSpan FileTimeAt(long timestamp) => _take.FileTimeAt(timestamp);

    public async Task<string> FinishAsync(CancellationToken cancellationToken)
    {
        await StopAsync().ConfigureAwait(true);

        if (_writeError is { } error)
        {
            DeletePart();
            throw CaptureErrors.Map(error);
        }

        try
        {
            File.Move(_partPath, _outputPath, overwrite: false);
        }
        catch (IOException ex)
        {
            throw CaptureErrors.Map(ex);
        }

        _logger.LogInformation("Запись сохранена: {Path}, {Duration}", _outputPath, Recorded);
        return _outputPath;
    }

    public async Task DiscardAsync()
    {
        await StopAsync().ConfigureAwait(true);
        DeletePart();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_finished)
        {
            await DiscardAsync().ConfigureAwait(true);
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
        _device.Dispose();
    }

    /// <summary>Останавливает захват и закрывает файл: заголовок WAV дописывается здесь.</summary>
    private async Task StopAsync()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;

        if (_capture.CaptureState is CaptureState.Capturing or CaptureState.Starting)
        {
            _capture.StopRecording();

            try
            {
                await _stopped.Task.WaitAsync(StopTimeout).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Микрофон не подтвердил остановку за {Timeout}", StopTimeout);
            }
        }

        _take.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var arrival = Stopwatch.GetTimestamp();

        if (e.BytesRecorded <= 0)
        {
            return;
        }

        try
        {
            if (!_take.Append(e.Buffer.AsSpan(0, e.BytesRecorded), arrival))
            {
                return;
            }
        }
        catch (IOException ex)
        {
            // Кончилось место или пропал диск. Захват останавливаем: писать дальше некуда.
            _writeError = ex;
            _capture.StopRecording();
            return;
        }

        _firstSound.TrySetResult();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        var problem = e.Exception ?? _writeError;

        if (problem is not null)
        {
            var mapped = CaptureErrors.Map(problem);
            _logger.LogWarning(problem, "Запись с микрофона прервалась");

            if (!_firstSound.TrySetException(mapped) && !_finished)
            {
                Faulted?.Invoke(this, mapped);
            }
        }
        else
        {
            _firstSound.TrySetCanceled();
        }

        _stopped.TrySetResult();
    }

    private void DeletePart()
    {
        try
        {
            File.Delete(_partPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Не удалось удалить {Path}", _partPath);
        }
    }
}
