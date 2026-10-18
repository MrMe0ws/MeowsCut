namespace MeowsCut.Core.Abstractions;

/// <summary>Микрофон, из которого можно писать.</summary>
public sealed record AudioInputDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Запись голоса с микрофона в файл — для озвучки смонтированного ролика.
/// </summary>
/// <remarks>
/// Ядро не знает, чем захватывается звук. Реализация пишет файл потоково,
/// в памяти запись не копится (правило 9), и до конца записи файл лежит
/// с суффиксом <c>.part</c> (правило 10).
/// </remarks>
public interface IVoiceRecorder
{
    /// <summary>Включённые микрофоны. Пустой список — писать не из чего.</summary>
    IReadOnlyList<AudioInputDevice> ListDevices();

    /// <summary>
    /// Включает микрофон и начинает запись в <paramref name="outputPath"/>.
    /// </summary>
    /// <remarks>
    /// Задача завершается, когда от микрофона пришёл первый звук, а не когда
    /// устройство открыто: включение занимает до сотен миллисекунд, и запускать
    /// ролик раньше значило бы потерять начало реплики.
    /// </remarks>
    /// <param name="deviceId">Микрофон из <see cref="ListDevices"/>; null — системный по умолчанию.</param>
    /// <exception cref="Diagnostics.VoiceRecordingException">Микрофона нет, он занят или запрещён.</exception>
    Task<IVoiceRecording> StartAsync(string? deviceId, string outputPath, CancellationToken cancellationToken);
}

/// <summary>Идущая запись.</summary>
public interface IVoiceRecording : IAsyncDisposable
{
    /// <summary>Сколько уже записано.</summary>
    TimeSpan Recorded { get; }

    /// <summary>
    /// Был ли в записи хоть один не нулевой отсчёт. У живого микрофона всегда есть
    /// шум; ровные нули означают, что Windows не даёт приложению микрофон.
    /// </summary>
    bool HasSignal { get; }

    /// <summary>Пик сигнала с прошлого вызова, от 0 до 1. Для индикатора уровня.</summary>
    double TakePeak();

    /// <summary>
    /// Место в файле, соответствующее моменту <paramref name="timestamp"/>
    /// (метка <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>).
    /// </summary>
    /// <remarks>
    /// По нему запись привязывается к ролику: известно, в какой момент пошла картинка,
    /// а здесь — какой отсчёт файла был записан в этот момент.
    /// </remarks>
    TimeSpan FileTimeAt(long timestamp);

    /// <summary>
    /// Микрофон пропал посреди записи: выдернули, отключили в системе.
    /// Записанное до этого момента сохраняется через <see cref="FinishAsync"/>.
    /// Вызывается не в потоке интерфейса.
    /// </summary>
    event EventHandler<Exception>? Faulted;

    /// <summary>Останавливает запись и отдаёт путь готового файла.</summary>
    Task<string> FinishAsync(CancellationToken cancellationToken);

    /// <summary>Останавливает запись и удаляет файл — дубль не нужен.</summary>
    Task DiscardAsync();
}
