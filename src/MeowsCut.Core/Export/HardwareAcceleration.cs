namespace MeowsCut.Core.Export;

/// <summary>
/// Кем кодировать видео: процессором или видеокартой.
/// </summary>
/// <remarks>
/// По умолчанию выключено намеренно. Аппаратные энкодеры кодируют в разы быстрее,
/// но при равном размере файла заметно уступают в качестве, а часть настроек
/// (постоянное качество, два прохода) понимают по-своему или не понимают вовсе.
/// Поэтому это осознанный выбор пользователя, а не молчаливая оптимизация.
/// </remarks>
public enum HardwareAcceleration
{
    /// <summary>Кодировать процессором. Медленнее, но качественнее и предсказуемее.</summary>
    None = 0,

    /// <summary>NVIDIA NVENC.</summary>
    Nvidia,

    /// <summary>Intel Quick Sync.</summary>
    Intel,

    /// <summary>AMD AMF.</summary>
    Amd
}

public static class HardwareAccelerationExtensions
{
    public static string DisplayName(this HardwareAcceleration value) => value switch
    {
        HardwareAcceleration.Nvidia => "NVIDIA NVENC",
        HardwareAcceleration.Intel => "Intel Quick Sync",
        HardwareAcceleration.Amd => "AMD AMF",
        _ => "Процессор"
    };

    /// <summary>Суффикс имени энкодера в ffmpeg: h264_nvenc, hevc_qsv и так далее.</summary>
    public static string? Suffix(this HardwareAcceleration value) => value switch
    {
        HardwareAcceleration.Nvidia => "nvenc",
        HardwareAcceleration.Intel => "qsv",
        HardwareAcceleration.Amd => "amf",
        _ => null
    };
}
