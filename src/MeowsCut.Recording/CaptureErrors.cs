using MeowsCut.Core.Diagnostics;

namespace MeowsCut.Recording;

/// <summary>
/// Перевод кодов ошибок WASAPI в причины, понятные пользователю.
/// </summary>
/// <remarks>
/// Звуковая подсистема сообщает об ошибках HRESULT-кодами, а «0x8889000A»
/// пользователю не скажет ничего. Причин всего несколько, и для каждой есть
/// понятный совет — его выбирает <see cref="ErrorPresenter"/>.
/// </remarks>
internal static class CaptureErrors
{
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int NotFound = unchecked((int)0x80070490);
    private const int DeviceInUse = unchecked((int)0x8889000A);
    private const int DeviceInvalidated = unchecked((int)0x88890004);

    public static VoiceRecordingException Map(Exception exception) => exception switch
    {
        VoiceRecordingException known => known,

        UnauthorizedAccessException or { HResult: AccessDenied } => new VoiceRecordingException(
            RecordingProblem.AccessDenied,
            "Windows не даёт программе доступ к микрофону.",
            exception),

        { HResult: DeviceInUse } => new VoiceRecordingException(
            RecordingProblem.DeviceBusy,
            "Микрофон занят другой программой.",
            exception),

        { HResult: DeviceInvalidated } => new VoiceRecordingException(
            RecordingProblem.DeviceLost,
            "Микрофон отключился во время записи.",
            exception),

        { HResult: NotFound } => new VoiceRecordingException(
            RecordingProblem.NoDevice,
            "Выбранный микрофон не найден.",
            exception),

        IOException => new VoiceRecordingException(
            RecordingProblem.FileFailed,
            "Не удалось записать файл озвучки.",
            exception),

        _ => new VoiceRecordingException(
            RecordingProblem.NoResponse,
            "Микрофон не открылся.",
            exception)
    };
}
