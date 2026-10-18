using System.Diagnostics;
using NAudio.Wave;

namespace MeowsCut.Recording;

/// <summary>
/// Файл одного дубля: сводит звук в 16-битное моно, дописывает его на диск
/// и помнит, сколько и когда записано.
/// </summary>
/// <remarks>
/// Куски приходят из потока захвата, а уровень и время читает интерфейс —
/// поэтому всё общее состояние под одним замком. В памяти лежит только
/// текущий кусок: дубль любой длины пишется потоком (правило 9).
/// </remarks>
internal sealed class TakeWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly CaptureFormat _format;
    private readonly int _sampleRate;

    private WaveFileWriter? _writer;
    private byte[] _converted = [];
    private long _frames;
    private long _lastArrival;
    private float _peak;
    private bool _hasSignal;

    public TakeWriter(string path, CaptureFormat format, int sampleRate)
    {
        _format = format;
        _sampleRate = sampleRate;
        _writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
    }

    public TimeSpan Recorded
    {
        get
        {
            lock (_gate)
            {
                return FramesToTime(_frames);
            }
        }
    }

    public bool HasSignal
    {
        get
        {
            lock (_gate)
            {
                return _hasSignal;
            }
        }
    }

    /// <summary>
    /// Дописывает кусок, пришедший в момент <paramref name="arrival"/>.
    /// </summary>
    /// <returns>false, если файл уже закрыт и кусок некуда писать.</returns>
    /// <exception cref="IOException">Кончилось место или пропал диск.</exception>
    public bool Append(ReadOnlySpan<byte> buffer, long arrival)
    {
        var frames = buffer.Length / _format.BlockAlign;
        if (_converted.Length < frames * 2)
        {
            _converted = new byte[frames * 2];
        }

        var bytes = SampleDownmix.ToMono16(_format, buffer, _converted, out var peak);

        lock (_gate)
        {
            if (_writer is null)
            {
                return false;
            }

            _writer.Write(_converted, 0, bytes);

            _frames += bytes / 2;
            _lastArrival = arrival;
            _peak = Math.Max(_peak, peak);
            _hasSignal |= peak > 0f;
            return true;
        }
    }

    public double TakePeak()
    {
        lock (_gate)
        {
            var peak = _peak;
            _peak = 0f;
            return peak;
        }
    }

    /// <summary>
    /// Место в файле для момента <paramref name="timestamp"/>: записанное плюс
    /// время, прошедшее с прихода последнего куска. Отсчёты идут без пропусков,
    /// поэтому так можно смотреть и вперёд, и назад.
    /// </summary>
    public TimeSpan FileTimeAt(long timestamp)
    {
        lock (_gate)
        {
            return FramesToTime(_frames) + Stopwatch.GetElapsedTime(_lastArrival, timestamp);
        }
    }

    /// <summary>Закрывает файл — заголовок WAV с длиной дописывается здесь.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private TimeSpan FramesToTime(long frames) => TimeSpan.FromSeconds((double)frames / _sampleRate);
}
