namespace MeowsCut.Core.Diagnostics;

/// <summary>
/// Ошибка в том виде, в каком её показывают человеку.
/// </summary>
/// <remarks>
/// Технические подробности не выбрасываются, но и не лезут вперёд: они нужны
/// для разбора, а не для первого экрана. «Exit code 1» пользователю не говорит ничего.
/// </remarks>
public sealed record AppError(
    ErrorCode Code,
    string Title,
    string Message,
    string? Suggestion,
    string? Details);

/// <summary>Переводит исключения приложения в человеческий текст.</summary>
public interface IErrorPresenter
{
    AppError Present(Exception exception);
}

public sealed class ErrorPresenter : IErrorPresenter
{
    public AppError Present(Exception exception) => exception switch
    {
        FfmpegNotFoundException notFound => new AppError(
            ErrorCode.FfmpegNotFound,
            "FFmpeg не найден",
            notFound.Message,
            "Положите ffmpeg.exe и ffprobe.exe в папку ffmpeg рядом с приложением или укажите папку в настройках.",
            null),

        FfmpegExecutionException failed => new AppError(
            ErrorCode.FfmpegFailed,
            "Не удалось обработать видео",
            failed.Message,
            SuggestFor(failed),
            BuildDetails(failed)),

        MediaProbeException probe => new AppError(
            ErrorCode.ProbeFailed,
            "Не удалось открыть файл",
            probe.Message,
            "Попробуйте открыть файл в проигрывателе: если и он не справится, файл повреждён.",
            probe.FilePath),

        UnsupportedMediaException unsupported => new AppError(
            ErrorCode.UnsupportedMedia,
            "С этим файлом нечего делать",
            unsupported.Message,
            null,
            unsupported.FilePath),

        OutputWriteException write => new AppError(
            ErrorCode.OutputWriteFailed,
            "Не удалось сохранить результат",
            write.Message,
            "Проверьте свободное место и закройте файл в других программах.",
            write.InnerException?.Message),

        EditOperationException edit => new AppError(
            ErrorCode.EditOperationFailed,
            "Так не получится",
            edit.Message,
            null,
            null),

        OperationCanceledException => new AppError(
            ErrorCode.Unknown,
            "Отменено",
            "Операция остановлена.",
            null,
            null),

        _ => new AppError(
            ErrorCode.Unknown,
            "Что-то пошло не так",
            exception.Message,
            "Подробности записаны в лог — их можно приложить к сообщению об ошибке.",
            exception.ToString())
    };

    private static string? SuggestFor(FfmpegExecutionException failed) => failed.Message switch
    {
        var message when message.Contains("места", StringComparison.OrdinalIgnoreCase) =>
            "Освободите место на диске или выберите другую папку для результата.",

        var message when message.Contains("прав", StringComparison.OrdinalIgnoreCase) =>
            "Выберите папку, куда можно писать: например, «Видео» или рабочий стол.",

        var message when message.Contains("кодек", StringComparison.OrdinalIgnoreCase) =>
            "Выберите другой кодек — в этой сборке FFmpeg нужного нет.",

        var message when message.Contains("повреждён", StringComparison.OrdinalIgnoreCase) =>
            "Попробуйте другой исходный файл.",

        _ => "Подробности ниже можно приложить к сообщению об ошибке."
    };

    /// <summary>
    /// Команда и хвост вывода ffmpeg: по ним ошибка воспроизводится в консоли
    /// за минуту, поэтому они всегда попадают в подробности.
    /// </summary>
    private static string BuildDetails(FfmpegExecutionException failed)
    {
        var lines = new List<string>
        {
            $"Код завершения: {failed.ExitCode}",
            string.Empty,
            "Команда:",
            failed.CommandLine
        };

        if (failed.StdErrTail.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Вывод FFmpeg:");
            lines.AddRange(failed.StdErrTail);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
