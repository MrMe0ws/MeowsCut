namespace MeowsCut.Core.Media;

/// <summary>
/// Расширения, которые приложение готово открывать. Список нужен для фильтра диалога
/// и быстрой проверки при перетаскивании; последнее слово всё равно за ffprobe —
/// если он открыл файл, работаем, даже если расширение незнакомое.
/// </summary>
public static class MediaFileTypes
{
    public static readonly IReadOnlyList<string> VideoExtensions =
    [
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".flv",
        ".ts", ".m2ts", ".mts", ".mpg", ".mpeg", ".3gp", ".ogv", ".gif"
    ];

    /// <summary>
    /// Звуковые файлы. Видео тоже годится как источник звука — из ролика берут
    /// реплику или музыку, — поэтому фильтр звука включает и видеорасширения.
    /// </summary>
    public static readonly IReadOnlyList<string> AudioExtensions =
    [
        ".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg", ".opus", ".wma", ".aiff", ".alac"
    ];

    /// <summary>
    /// Фотографии. Кладутся в видеоряд как обычные клипы, но длину им задаёт
    /// пользователь: своей у картинки нет.
    /// </summary>
    /// <remarks>
    /// GIF сюда не входит: он живёт среди видео, потому что бывает анимированным,
    /// и решать за пользователя, что это «просто картинка», нельзя.
    /// </remarks>
    public static readonly IReadOnlyList<string> ImageExtensions =
    [
        ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".avif"
    ];

    /// <summary>Файлы субтитров, которые понимает ffmpeg.</summary>
    public static readonly IReadOnlyList<string> SubtitleExtensions = [".srt", ".ass", ".ssa", ".vtt"];

    public static bool IsKnownVideoExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) &&
               (VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
                ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    public static bool IsKnownImageExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) &&
               ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Фильтр для диалога открытия файла в формате Win32.
    /// </summary>
    public static string BuildOpenDialogFilter(
        string mediaFilesLabel,
        string videoFilesLabel,
        string imageFilesLabel,
        string allFilesLabel)
    {
        var video = string.Join(";", VideoExtensions.Select(x => "*" + x));
        var images = string.Join(";", ImageExtensions.Select(x => "*" + x));

        // Первым — общий фильтр: чаще всего в папке лежит и то, и другое,
        // и заставлять переключать список ради одной фотографии незачем.
        return $"{mediaFilesLabel}|{video};{images}|" +
               $"{videoFilesLabel}|{video}|" +
               $"{imageFilesLabel}|{images}|" +
               $"{allFilesLabel}|*.*";
    }

    public static string BuildSubtitleDialogFilter(string subtitleFilesLabel, string allFilesLabel)
    {
        var patterns = string.Join(";", SubtitleExtensions.Select(x => "*" + x));
        return $"{subtitleFilesLabel}|{patterns}|{allFilesLabel}|*.*";
    }

    /// <summary>Фильтр для выбора звука: сначала звуковые файлы, потом видео.</summary>
    public static string BuildAudioDialogFilter(
        string audioFilesLabel,
        string videoFilesLabel,
        string allFilesLabel)
    {
        var audio = string.Join(";", AudioExtensions.Select(x => "*" + x));
        var video = string.Join(";", VideoExtensions.Select(x => "*" + x));

        return $"{audioFilesLabel}|{audio}|{videoFilesLabel}|{video}|{allFilesLabel}|*.*";
    }
}
