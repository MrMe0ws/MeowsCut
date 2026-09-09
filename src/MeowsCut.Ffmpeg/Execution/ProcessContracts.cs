using System.Text;

namespace MeowsCut.Ffmpeg.Execution;

/// <summary>
/// Запрос на запуск процесса. Аргументы — строго список: их экранированием занимается
/// рантайм, а не мы. Это то, что делает безопасными пути с пробелами и кириллицей.
/// </summary>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    bool RedirectStandardInput = false);

/// <summary>
/// Итог запуска. Хвост stderr нужен для диагностики; целиком поток не храним.
/// </summary>
public sealed record ProcessResult(
    int ExitCode,
    IReadOnlyList<string> StdErrTail,
    TimeSpan Elapsed)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Кольцевой буфер последних строк stderr. ffmpeg пишет туда мегабайты,
/// а для разбора ошибки нужны последние строки.
/// </summary>
public sealed class StdErrRingBuffer(int capacity = 200)
{
    private readonly Queue<string> _lines = new(capacity);
    private readonly object _sync = new();

    public void Add(string line)
    {
        lock (_sync)
        {
            if (_lines.Count == capacity)
            {
                _lines.Dequeue();
            }

            _lines.Enqueue(line);
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_sync)
        {
            return _lines.ToArray();
        }
    }
}

/// <summary>
/// Форматирование команды для логов и для кнопки «Скопировать детали».
/// Используется ТОЛЬКО для показа человеку: запускается процесс всегда списком аргументов.
/// </summary>
public static class CommandLineFormatter
{
    public static string Format(ProcessRequest request)
    {
        var builder = new StringBuilder();
        builder.Append(Quote(request.FileName));

        foreach (var argument in request.Arguments)
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string value) =>
        value.Contains(' ') || value.Contains('"') ? $"\"{value.Replace("\"", "\\\"")}\"" : value;
}
