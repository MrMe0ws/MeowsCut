using MeowsCut.Core.Export;
using MeowsCut.Core.Media;

namespace MeowsCut.Ffmpeg.Arguments;

/// <summary>
/// Соответствие кодеков приложения энкодерам ffmpeg и их ключам.
/// </summary>
/// <remarks>
/// Единственное место, где живут строковые имена энкодеров и их особенности:
/// x264 понимает -preset, VP9 — -deadline и -cpu-used, SVT-AV1 — -preset числом.
/// Всё это спрятано здесь, чтобы планировщик оставался про логику, а не про ключи.
/// </remarks>
public static class EncoderCatalog
{
    public static string VideoEncoder(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "libx264",
        VideoCodec.H265 => "libx265",
        VideoCodec.Vp9 => "libvpx-vp9",
        VideoCodec.Av1 => "libsvtav1",
        _ => "copy"
    };

    public static string AudioEncoder(AudioCodec codec) => codec switch
    {
        AudioCodec.Aac => "aac",
        AudioCodec.Opus => "libopus",
        AudioCodec.Mp3 => "libmp3lame",
        AudioCodec.Vorbis => "libvorbis",
        AudioCodec.Flac => "flac",
        _ => "copy"
    };

    /// <summary>Запасной энкодер, если основного нет в этой сборке ffmpeg.</summary>
    public static string? Fallback(VideoCodec codec) => codec switch
    {
        VideoCodec.Av1 => "libaom-av1",
        _ => null
    };

    public static bool IsAvailable(VideoCodec codec, MediaCapabilities capabilities) =>
        codec == VideoCodec.Copy ||
        capabilities.HasVideoEncoder(VideoEncoder(codec)) ||
        (Fallback(codec) is { } fallback && capabilities.HasVideoEncoder(fallback));

    public static bool IsAvailable(AudioCodec codec, MediaCapabilities capabilities) =>
        codec == AudioCodec.Copy || capabilities.HasAudioEncoder(AudioEncoder(codec));

    /// <summary>Имя энкодера с учётом того, что реально есть в найденной сборке.</summary>
    public static string ResolveVideoEncoder(VideoCodec codec, MediaCapabilities capabilities)
    {
        var primary = VideoEncoder(codec);
        if (codec == VideoCodec.Copy || capabilities.HasVideoEncoder(primary))
        {
            return primary;
        }

        return Fallback(codec) is { } fallback && capabilities.HasVideoEncoder(fallback)
            ? fallback
            : primary;
    }

    /// <summary>Пиксельный формат по умолчанию: yuv420p открывается везде.</summary>
    public static string DefaultPixelFormat(VideoCodec codec) => codec switch
    {
        _ => "yuv420p"
    };

    /// <summary>
    /// Ключи, задающие компромисс «скорость против размера». У каждого энкодера свои.
    /// </summary>
    public static IEnumerable<(string Key, string Value)> SpeedOptions(
        VideoCodec codec,
        string encoderName,
        EncodingSpeed speed)
    {
        switch (codec)
        {
            case VideoCodec.H264:
            case VideoCodec.H265:
                yield return ("-preset", speed switch
                {
                    EncodingSpeed.VeryFast => "veryfast",
                    EncodingSpeed.Fast => "fast",
                    EncodingSpeed.Slow => "slow",
                    _ => "medium"
                });
                break;

            case VideoCodec.Vp9:
                yield return ("-deadline", speed == EncodingSpeed.Slow ? "best" : "good");
                yield return ("-cpu-used", speed switch
                {
                    EncodingSpeed.VeryFast => "5",
                    EncodingSpeed.Fast => "3",
                    EncodingSpeed.Slow => "0",
                    _ => "2"
                });
                // Без этого VP9 в WebM иногда даёт битые альфа-кадры при коротких клипах.
                yield return ("-row-mt", "1");
                break;

            case VideoCodec.Av1:
                if (encoderName == "libsvtav1")
                {
                    yield return ("-preset", speed switch
                    {
                        EncodingSpeed.VeryFast => "10",
                        EncodingSpeed.Fast => "8",
                        EncodingSpeed.Slow => "4",
                        _ => "6"
                    });
                }
                else
                {
                    yield return ("-cpu-used", speed switch
                    {
                        EncodingSpeed.VeryFast => "8",
                        EncodingSpeed.Fast => "6",
                        EncodingSpeed.Slow => "2",
                        _ => "4"
                    });
                }

                break;
        }
    }

    /// <summary>Особенности контейнеров, без которых файл открывается не везде.</summary>
    public static IEnumerable<(string Key, string Value)> ContainerOptions(
        ContainerFormat container,
        VideoCodec codec)
    {
        switch (container)
        {
            case ContainerFormat.Mp4:
                // Индекс в начало файла: иначе видео не начинает играть до полной загрузки.
                yield return ("-movflags", "+faststart");
                break;

            case ContainerFormat.Mov when codec == VideoCodec.H265:
                // Без тега hvc1 QuickTime и iPhone такой файл не открывают.
                yield return ("-tag:v", "hvc1");
                break;
        }
    }
}
