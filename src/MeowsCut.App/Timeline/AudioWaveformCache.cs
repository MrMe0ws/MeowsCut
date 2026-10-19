using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using MeowsCut.App.Services;
using MeowsCut.Core.Abstractions;

namespace MeowsCut.App.Timeline;

/// <summary>
/// Волны звука, готовые к отрисовке.
/// </summary>
/// <remarks>
/// Доска рисуется десятки раз в секунду и ждать в ней нельзя, поэтому запрос волны
/// возвращает то, что уже есть, а недостающее считает в фоне и сообщает событием.
/// Волна строится на весь файл целиком и хранится пиками по столбцам, а кусок
/// берётся из них по долям файла — обрезка куска не требует нового прохода ffmpeg.
/// </remarks>
public sealed class AudioWaveformCache(IWaveformService waveforms, IUiDispatcher dispatcher)
{
    private readonly ConcurrentDictionary<string, WaveformPeaks?> _peaks = new();
    private readonly ConcurrentDictionary<string, byte> _loading = new();

    /// <summary>Готова очередная волна — доске пора перерисоваться.</summary>
    public event EventHandler? Ready;

    /// <summary>
    /// Кусок волны от <paramref name="from"/> до <paramref name="to"/>
    /// (доли длительности файла). null — волна ещё не готова или её не построить.
    /// </summary>
    public WaveformSlice? Slice(string sourcePath, double from, double to)
    {
        if (Get(sourcePath) is not { } peaks)
        {
            return null;
        }

        var width = Math.Clamp(to - from, 0.0001, 1d);
        var left = Math.Clamp(from, 0d, 1d - width);

        return new WaveformSlice(peaks, left, left + width);
    }

    private WaveformPeaks? Get(string sourcePath)
    {
        if (_peaks.TryGetValue(sourcePath, out var cached))
        {
            return cached;
        }

        // Ключ занимается до запуска задачи: перерисовка успевает случиться
        // несколько раз, пока ffmpeg идёт по файлу.
        if (_loading.TryAdd(sourcePath, 0))
        {
            _ = LoadAsync(sourcePath);
        }

        return null;
    }

    private async Task LoadAsync(string sourcePath)
    {
        try
        {
            var path = await waveforms
                .GetWaveformAsync(sourcePath, CancellationToken.None)
                .ConfigureAwait(false);

            // null запоминается тоже: файл без звука не должен опрашиваться заново
            // при каждой перерисовке доски.
            _peaks[sourcePath] = path is null ? null : LoadPeaks(path);

            dispatcher.Post(() => Ready?.Invoke(this, EventArgs.Empty));
        }
        finally
        {
            _loading.TryRemove(sourcePath, out _);
        }
    }

    private static WaveformPeaks? LoadPeaks(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // файл не остаётся заблокированным
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();

            return WaveformPeaks.FromImage(image);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            // Битая или недописанная картинка — просто рисуем кусок без волны.
            return null;
        }
    }
}
