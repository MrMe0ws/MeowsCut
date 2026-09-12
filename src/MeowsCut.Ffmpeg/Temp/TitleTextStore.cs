using System.Security.Cryptography;
using System.Text;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Ffmpeg.Temp;

/// <summary>
/// Складывает тексты надписей в файлы, из которых их читает drawtext.
/// </summary>
/// <remarks>
/// Имя файла — отпечаток самого текста. План экспорта строится заново на каждое
/// движение в окне настроек (там считается сводка и оценка размера), и писать
/// при этом новый файл на каждый чих нельзя. С отпечатком повторная сборка
/// находит файл на месте и ничего не делает, а разных файлов ровно столько,
/// сколько разных надписей.
///
/// Пишем без BOM: drawtext читает файл как есть, и три служебных байта в начале
/// выходят в кадр закорючкой перед первой буквой.
/// </remarks>
public static class TitleTextStore
{
    /// <summary>Папка с текстами внутри временной. Отдельная, чтобы её было видно и чем чистить.</summary>
    public const string FolderName = "titles";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Готовит файлы для всех непустых надписей.
    /// </summary>
    /// <returns>Где лежит текст каждой надписи. Не записанные надписи в карту не попадают.</returns>
    public static IReadOnlyDictionary<TitleId, string> Materialize(
        IReadOnlyList<TitleClip> titles,
        string temporaryDirectory)
    {
        var files = new Dictionary<TitleId, string>();

        if (titles.Count == 0)
        {
            return files;
        }

        var directory = Path.Combine(temporaryDirectory, FolderName);
        Directory.CreateDirectory(directory);

        foreach (var title in titles)
        {
            if (title.IsEmpty)
            {
                continue;
            }

            var path = Path.Combine(directory, FileNameOf(title.Text));

            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, title.Text, Utf8NoBom);
                }

                files[title.Id] = path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Не записали — надпись просто не попадёт в граф. Валить из-за
                // этого весь экспорт было бы хуже, чем отдать ролик без титра.
            }
        }

        return files;
    }

    private static string FileNameOf(string text)
    {
        var hash = SHA256.HashData(Utf8NoBom.GetBytes(text));

        return Convert.ToHexString(hash)[..16].ToLowerInvariant() + ".txt";
    }
}
