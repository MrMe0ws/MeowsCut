namespace MeowsCut.Core.Export;

public enum ContainerFormat
{
    Mp4 = 0,
    WebM,
    Mov,
    Mkv,
    Avi,

    /// <summary>Только звук: дорожка без картинки.</summary>
    Mp3,

    /// <summary>Только звук, AAC в контейнере MPEG-4.</summary>
    M4a,

    /// <summary>Только звук, без сжатия.</summary>
    Wav
}

public enum VideoCodec
{
    /// <summary>Скопировать видеопоток без перекодирования.</summary>
    Copy = 0,
    H264,
    H265,
    Vp9,
    Av1
}

public enum AudioCodec
{
    /// <summary>Скопировать аудиопоток без перекодирования.</summary>
    Copy = 0,
    Aac,
    Opus,
    Mp3,
    Vorbis,
    Flac,

    /// <summary>Несжатый звук для WAV.</summary>
    Pcm
}

/// <summary>
/// Компромисс «скорость кодирования против размера файла». В конкретные ключи
/// энкодеров разворачивается в слое ffmpeg, а не здесь.
/// </summary>
public enum EncodingSpeed
{
    VeryFast = 0,
    Fast,
    Balanced,
    Slow
}

public enum OverwritePolicy
{
    /// <summary>Дописать к имени номер, если файл уже существует.</summary>
    AutoRename = 0,
    Overwrite,
    Fail
}

public static class ContainerFormatExtensions
{
    public static string FileExtension(this ContainerFormat format) => format switch
    {
        ContainerFormat.Mp4 => ".mp4",
        ContainerFormat.WebM => ".webm",
        ContainerFormat.Mov => ".mov",
        ContainerFormat.Mkv => ".mkv",
        ContainerFormat.Avi => ".avi",
        ContainerFormat.Mp3 => ".mp3",
        ContainerFormat.M4a => ".m4a",
        ContainerFormat.Wav => ".wav",
        _ => ".mp4"
    };

    /// <summary>
    /// Контейнер без картинки: результат — звуковая дорожка.
    /// </summary>
    /// <remarks>
    /// Признак контейнера, а не отдельная галочка «только звук»: выбрав MP3,
    /// пользователь уже сказал всё, что нужно, и второй переключатель с тем же
    /// смыслом только даёт им шанс разойтись.
    /// </remarks>
    public static bool IsAudioOnly(this ContainerFormat format) =>
        format is ContainerFormat.Mp3 or ContainerFormat.M4a or ContainerFormat.Wav;

    /// <summary>Имя мультиплексора для ключа -f, когда расширение не определяет формат.</summary>
    public static string MuxerName(this ContainerFormat format) => format switch
    {
        ContainerFormat.Mp4 => "mp4",
        ContainerFormat.WebM => "webm",
        ContainerFormat.Mov => "mov",
        ContainerFormat.Mkv => "matroska",
        ContainerFormat.Avi => "avi",
        ContainerFormat.Mp3 => "mp3",

        // Для m4a это не «mp4»: муксер ipod ставит правильный бренд файла,
        // иначе проигрыватели принимают звук за видео без картинки.
        ContainerFormat.M4a => "ipod",
        ContainerFormat.Wav => "wav",
        _ => "mp4"
    };

    public static ContainerFormat? FromExtension(string path)
    {
        var extension = Path.GetExtension(path);

        return extension.ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => ContainerFormat.Mp4,
            ".webm" => ContainerFormat.WebM,
            ".mov" => ContainerFormat.Mov,
            ".mkv" => ContainerFormat.Mkv,
            ".avi" => ContainerFormat.Avi,
            ".mp3" => ContainerFormat.Mp3,
            ".m4a" => ContainerFormat.M4a,
            ".wav" => ContainerFormat.Wav,
            _ => null
        };
    }
}

public static class CodecNames
{
    /// <summary>Как кодек называется в выводе ffprobe — нужно, чтобы понять, можно ли копировать поток.</summary>
    public static VideoCodec? VideoFromProbeName(string? name) => name?.ToLowerInvariant() switch
    {
        "h264" or "avc1" => VideoCodec.H264,
        "hevc" or "h265" => VideoCodec.H265,
        "vp9" => VideoCodec.Vp9,
        "av1" => VideoCodec.Av1,
        _ => null
    };

    public static AudioCodec? AudioFromProbeName(string? name) => name?.ToLowerInvariant() switch
    {
        "aac" => AudioCodec.Aac,
        "pcm_s16le" or "pcm_s24le" or "pcm_f32le" => AudioCodec.Pcm,
        "opus" => AudioCodec.Opus,
        "mp3" => AudioCodec.Mp3,
        "vorbis" => AudioCodec.Vorbis,
        "flac" => AudioCodec.Flac,
        _ => null
    };

    public static string DisplayName(this VideoCodec codec) => codec switch
    {
        VideoCodec.Copy => "как в исходнике",
        VideoCodec.H264 => "H.264",
        VideoCodec.H265 => "H.265",
        VideoCodec.Vp9 => "VP9",
        VideoCodec.Av1 => "AV1",
        _ => codec.ToString()
    };

    public static string DisplayName(this AudioCodec codec) => codec switch
    {
        AudioCodec.Copy => "как в исходнике",
        AudioCodec.Aac => "AAC",
        AudioCodec.Opus => "Opus",
        AudioCodec.Mp3 => "MP3",
        AudioCodec.Vorbis => "Vorbis",
        AudioCodec.Flac => "FLAC",
        AudioCodec.Pcm => "PCM",
        _ => codec.ToString()
    };
}
