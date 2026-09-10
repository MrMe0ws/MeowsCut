using System.Text;

namespace MeowsCut.Ffmpeg.Temp;

/// <summary>
/// Пишет список файлов для демультиплексора concat.
/// </summary>
/// <remarks>
/// Формат придирчив: каждая строка вида <c>file '&lt;путь&gt;'</c>, кодировка UTF-8
/// строго без BOM (иначе ffmpeg не распознаёт первую строку), а одинарная кавычка
/// внутри пути экранируется последовательностью <c>'\''</c>. Пути с пробелами
/// и кириллицей встречаются постоянно, поэтому это отдельный проверяемый тип.
/// </remarks>
public static class ConcatListWriter
{
    private static readonly UTF8Encoding NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string Build(IEnumerable<string> files)
    {
        var builder = new StringBuilder();

        foreach (var file in files)
        {
            builder.Append("file '").Append(Escape(file)).Append("'\n");
        }

        return builder.ToString();
    }

    public static void Write(string listFile, IEnumerable<string> files)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(listFile));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(listFile, Build(files), NoBom);
    }

    private static string Escape(string path) =>
        path.Replace("'", @"'\''", StringComparison.Ordinal);
}
