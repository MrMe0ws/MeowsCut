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

    public bool HasVideo => VideoStreams.Count > 0;

    public bool HasAudio => AudioStreams.Count > 0;

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
