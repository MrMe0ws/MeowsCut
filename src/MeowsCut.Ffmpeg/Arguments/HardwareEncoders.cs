using MeowsCut.Core.Export;
using MeowsCut.Core.Media;

namespace MeowsCut.Ffmpeg.Arguments;

/// <summary>
/// Имена и ключи аппаратных энкодеров.
/// </summary>
/// <remarks>
/// Вынесено из <see cref="EncoderCatalog"/>: у видеокарт всё своё — и названия,
/// и способ задать качество, и шкала скорости. Смешав это с программными
/// энкодерами, получили бы один большой switch, в который страшно заглядывать.
/// </remarks>
public static class HardwareEncoders
{
    /// <summary>Имя энкодера ffmpeg или null, если такой пары кодек+железо не бывает.</summary>
    public static string? Name(VideoCodec codec, HardwareAcceleration hardware)
    {
        var suffix = hardware.Suffix();
        if (suffix is null)
        {
            return null;
        }

        var prefix = codec switch
        {
            VideoCodec.H264 => "h264",
            VideoCodec.H265 => "hevc",
            VideoCodec.Av1 => "av1",

            // VP9 аппаратно почти нигде не кодируется — остаётся libvpx-vp9.
            _ => null
        };

        return prefix is null ? null : $"{prefix}_{suffix}";
    }

    /// <summary>
    /// Годится ли энкодер на самом деле. Проверяется по списку тех, что запустились
    /// пробным кодированием: наличие имени в -encoders ничего не гарантирует.
    /// </summary>
    public static bool IsAvailable(VideoCodec codec, HardwareAcceleration hardware, MediaCapabilities capabilities) =>
        Name(codec, hardware) is { } name && capabilities.HasWorkingHardwareEncoder(name);

    /// <summary>Что из железа вообще умеет эта сборка ffmpeg и эта машина.</summary>
    public static IReadOnlyList<HardwareAcceleration> Available(
        VideoCodec codec,
        MediaCapabilities capabilities)
    {
        var result = new List<HardwareAcceleration> { HardwareAcceleration.None };

        foreach (var hardware in new[]
                 {
                     HardwareAcceleration.Nvidia,
                     HardwareAcceleration.Intel,
                     HardwareAcceleration.Amd
                 })
        {
            if (IsAvailable(codec, hardware, capabilities))
            {
                result.Add(hardware);
            }
        }

        return result;
    }

    /// <summary>
    /// Постоянное качество на видеокарте.
    /// </summary>
    /// <remarks>
    /// CRF аппаратные энкодеры не понимают: у NVENC это -cq при -rc vbr,
    /// у Quick Sync — -global_quality, у AMF — квантователь через -rc cqp.
    /// Число берётся то же, что и для процессора: шкалы близки, и пользователю
    /// не приходится держать в голове две.
    /// </remarks>
    public static IEnumerable<(string Key, string Value)> QualityOptions(
        HardwareAcceleration hardware,
        int quality)
    {
        var value = quality.ToString(System.Globalization.CultureInfo.InvariantCulture);

        switch (hardware)
        {
            case HardwareAcceleration.Nvidia:
                yield return ("-rc", "vbr");
                yield return ("-cq", value);
                yield return ("-b:v", "0");
                break;

            case HardwareAcceleration.Intel:
                yield return ("-global_quality", value);
                break;

            case HardwareAcceleration.Amd:
                yield return ("-rc", "cqp");
                yield return ("-qp_i", value);
                yield return ("-qp_p", value);
                break;
        }
    }

    /// <summary>Шкала «скорость против качества» у каждой видеокарты своя.</summary>
    public static IEnumerable<(string Key, string Value)> SpeedOptions(
        HardwareAcceleration hardware,
        EncodingSpeed speed)
    {
        switch (hardware)
        {
            case HardwareAcceleration.Nvidia:
                // p1 — самый быстрый, p7 — самый качественный.
                yield return ("-preset", speed switch
                {
                    EncodingSpeed.VeryFast => "p1",
                    EncodingSpeed.Fast => "p3",
                    EncodingSpeed.Slow => "p7",
                    _ => "p5"
                });
                break;

            case HardwareAcceleration.Intel:
                yield return ("-preset", speed switch
                {
                    EncodingSpeed.VeryFast => "veryfast",
                    EncodingSpeed.Fast => "fast",
                    EncodingSpeed.Slow => "slow",
                    _ => "medium"
                });
                break;

            case HardwareAcceleration.Amd:
                yield return ("-quality", speed switch
                {
                    EncodingSpeed.VeryFast or EncodingSpeed.Fast => "speed",
                    EncodingSpeed.Slow => "quality",
                    _ => "balanced"
                });
                break;
        }
    }
}
