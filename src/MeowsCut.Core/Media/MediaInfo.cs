namespace MeowsCut.Core.Media;

/// <summary>
/// Всё, что известно о медиафайле после ffprobe. Иммутабельный снимок,
/// на который опираются таймлайн, планировщик экспорта и панель информации.
/// </summary>
public sealed record MediaInfo(
    string FilePath,
    long FileSizeBytes,
    TimeSpan Duration,
    ContainerInfo Container,
    IReadOnlyList<VideoStreamInfo> VideoStreams,
    IReadOnlyList<AudioStreamInfo> AudioStreams,
    IReadOnlyList<SubtitleStreamInfo> SubtitleStreams)
{
    public string FileName => Path.GetFileName(FilePath);

    public VideoStreamInfo? PrimaryVideo => VideoStreams.Count > 0 ? VideoStreams[0] : null;

    public AudioStreamInfo? PrimaryAudio => AudioStreams.Count > 0 ? AudioStreams[0] : null;

    /// <summary>
    /// Сколько времени фотография «длится» как источник.
    /// </summary>
    /// <remarks>
    /// У картинки длительности нет вовсе, но клип обязан во что-то упираться при
    /// растягивании. Четыре часа — заведомо больше любого ролика, который здесь
    /// собирают, и при этом не переполняют арифметику времени.
    /// </remarks>
    public static readonly TimeSpan ImageSourceDuration = TimeSpan.FromHours(4);

    /// <summary>Сколько фотография занимает на доске сразу после добавления.</summary>
    public static readonly TimeSpan DefaultImageClipDuration = TimeSpan.FromSeconds(5);

    public bool HasVideo => VideoStreams.Count > 0;

    public bool HasAudio => AudioStreams.Count > 0;

    /// <summary>
    /// Это фотография, а не видео.
    /// </summary>
    /// <remarks>
    /// Признак — демультиплексор: ffprobe отдаёт для картинок image2 или *_pipe
    /// (png_pipe, mjpeg_pipe). По кодеку различить нельзя: mjpeg встречается
    /// и в настоящем видео. Анимированный GIF сюда не попадает — у него формат gif
    /// и есть длительность, то есть это обычное видео.
    /// </remarks>
    public bool IsImage =>
        AudioStreams.Count == 0 &&
        VideoStreams.Count == 1 &&
        (Container.FormatName.Equals("image2", StringComparison.OrdinalIgnoreCase) ||
         Container.FormatName.EndsWith("_pipe", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Битрейт всего файла: сначала из контейнера, иначе оценка по размеру и длительности.
    /// </summary>
    public long? OverallBitrateBps
    {
        get
        {
            if (Container.BitrateBps is > 0)
            {
                return Container.BitrateBps;
            }

            return Duration > TimeSpan.Zero
                ? (long)(FileSizeBytes * 8 / Duration.TotalSeconds)
                : null;
        }
    }
}
