using MeowsCut.Core.Export;

namespace MeowsCut.Ffmpeg.Arguments;

/// <summary>
/// Фильтр вшивания субтитров.
/// </summary>
/// <remarks>
/// Вся сложность здесь — в экранировании пути. Внутри графа фильтров ffmpeg
/// разбирает строку сам: двоеточие разделяет параметры, обратная косая черта
/// экранирует, одинарная кавычка ограничивает значение. Путь вида
/// C:\видео\субтитры.srt содержит сразу два опасных символа, поэтому он
/// приводится к прямым косым и двоеточие экранируется. Ошибиться здесь легко,
/// а проявится это отказом ffmpeg в конце длинного экспорта.
/// </remarks>
public static class SubtitleFilter
{
    public static string Build(SubtitleSettings subtitles)
    {
        var filter = "subtitles=filename='" + EscapePath(subtitles.FilePath!) + "'";

        if (!string.IsNullOrWhiteSpace(subtitles.ForceStyle) && !IsAdvancedFormat(subtitles.FilePath!))
        {
            filter += ":force_style='" + EscapeValue(subtitles.ForceStyle) + "'";
        }

        return filter;
    }

    /// <summary>У ASS и SSA оформление своё — навязывать ему стиль нельзя.</summary>
    public static bool IsAdvancedFormat(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase);
    }

    public static string EscapePath(string path)
    {
        // Прямые косые понимает и Windows, а обратные пришлось бы экранировать дважды.
        var normalized = path.Replace('\\', '/');

        return normalized
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("'", @"\'", StringComparison.Ordinal)
            .Replace(":", @"\:", StringComparison.Ordinal);
    }

    private static string EscapeValue(string value) =>
        value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("'", @"\'", StringComparison.Ordinal)
            .Replace(":", @"\:", StringComparison.Ordinal);
}
