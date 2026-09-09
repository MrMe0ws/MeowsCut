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

    public static bool IsKnownVideoExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) &&
               VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Фильтр для диалога открытия файла в формате Win32.
    /// </summary>
    public static string BuildOpenDialogFilter(string videoFilesLabel, string allFilesLabel)
    {
        var patterns = string.Join(";", VideoExtensions.Select(x => "*" + x));
        return $"{videoFilesLabel}|{patterns}|{allFilesLabel}|*.*";
    }
}
