namespace MeowsCut.Core.Diagnostics;

/// <summary>
/// Код ошибки — то, по чему <c>IErrorPresenter</c> выбирает человеческий текст.
/// </summary>
public enum ErrorCode
{
    Unknown = 0,
    FfmpegNotFound,
    FfmpegFailed,
    ProbeFailed,
    UnsupportedMedia,
    OutputWriteFailed,
    NotEnoughDiskSpace,
    TempWorkspaceFailed,
    SettingsFailed,
    EditOperationFailed
}

/// <summary>
/// Базовое исключение приложения. Всё, что ловится и показывается пользователю,
/// наследуется отсюда и несёт код ошибки.
/// </summary>
public abstract class MeowsCutException : Exception
{
    protected MeowsCutException(ErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}

/// <summary>
/// Бинарники ffmpeg не найдены или не запускаются.
/// </summary>
public sealed class FfmpegNotFoundException(string message, Exception? innerException = null)
    : MeowsCutException(ErrorCode.FfmpegNotFound, message, innerException);

/// <summary>
/// ffmpeg/ffprobe завершился с ненулевым кодом. Хвост stderr нужен для диагностики
/// и попадает в «Подробности» диалога ошибки, но не в первый экран.
/// </summary>
public sealed class FfmpegExecutionException(
    string message,
    int exitCode,
    IReadOnlyList<string> stdErrTail,
    string commandLine,
    Exception? innerException = null)
    : MeowsCutException(ErrorCode.FfmpegFailed, message, innerException)
{
    public int ExitCode { get; } = exitCode;

    public IReadOnlyList<string> StdErrTail { get; } = stdErrTail;

    public string CommandLine { get; } = commandLine;
}

/// <summary>
/// Файл не удалось разобрать: битый, недоступен, не медиа.
/// </summary>
public sealed class MediaProbeException(string message, string filePath, Exception? innerException = null)
    : MeowsCutException(ErrorCode.ProbeFailed, message, innerException)
{
    public string FilePath { get; } = filePath;
}

/// <summary>
/// Файл прочитан, но работать с ним нельзя: нет видеопотока, только обложка и т.п.
/// </summary>
public sealed class UnsupportedMediaException(string message, string filePath)
    : MeowsCutException(ErrorCode.UnsupportedMedia, message)
{
    public string FilePath { get; } = filePath;
}

/// <summary>
/// Недопустимая правка таймлайна: разрез вплотную к краю, ссылка на несуществующий клип
/// и подобное. Интерфейс должен не давать так делать, но модель обязана защищаться сама.
/// </summary>
public sealed class EditOperationException(string message)
    : MeowsCutException(ErrorCode.EditOperationFailed, message);
