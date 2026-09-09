using System.IO;

namespace MeowsCut.App.Services;

/// <summary>
/// Разбор аргументов запуска. Нужен для двух вещей: открыть видео двойным кликом
/// («Открыть с помощью» и перетаскивание на exe) и для режима снимка окна.
/// </summary>
public static class CommandLine
{
    /// <summary>
    /// Первый аргумент, который является существующим файлом и не относится к флагам.
    /// </summary>
    public static string? FindFilePath(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var value = args[i];

            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                i++; // у флагов есть значение — пропускаем его
                continue;
            }

            if (File.Exists(value))
            {
                return value;
            }
        }

        return null;
    }
}
